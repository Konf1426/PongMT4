const dgram = require("dgram");
const http = require("http");
const path = require("path");
const ballRules = require("./ball-rules");
const circleMath = require("./circle-math");
const { createClientRegistry } = require("./client-registry");
const { createScoreStore } = require("./score-store");
const { createSnapshotBuilder } = require("./snapshot-builder");

const UDP_PORT = Number(process.env.UDP_PORT || 41234);
const HEALTH_PORT = Number(process.env.UDP_HEALTH_PORT || 8082);
const SERVER_VERSION = "udp-authoritative-2026-06-03-01";
const DEBUG_UDP = process.env.UDP_DEBUG === "1";

const arenaRadius = 5;
const paddleArcDegrees = 22;
const paddleAngularSpeed = 120;
const ballSpeed = 3.5;
const paddleAimInfluence = 0.45;
const minimumPlayers = 2;
const maximumPlayers = 10;
const clientTimeoutMs = 10000;


const startCountdownMs = 5000;    
const joinGraceMs = 2000;         
const startCountdownMaxMs = 15000;  

const startingLives = 3;

const survivalPoints = 1;          
const winBonusPoints = 3;           
const scoresFilePath = path.join(__dirname, "scores.json");

const socket = dgram.createSocket("udp4");

const game = {
  playerCount: minimumPlayers,
  connectedPlayerCount: 0,
  readyPlayerCount: 0,
  players: [],
  ballX: 0,
  ballY: 0,
  ballDirX: 1,
  ballDirY: 0,
  lobbyOpen: false,
  gameStarted: false,
  gameOver: false,
  replayVoteCount: 0,
  postGameDeadline: 0,
  startDeadline: 0,
  countdownPlayerCount: 0,
  winnerId: 0,
  status: "Waiting for someone to start a game"
};

let lastTick = Date.now();

const clients = createClientRegistry({
  clientTimeoutMs,
  maximumPlayers,
  onChange: updateStatus
});
const clientsByDevice = clients.clientsByDevice;

const scoreStore = createScoreStore(scoresFilePath);
const snapshots = createSnapshotBuilder({
  clientsByDevice,
  clientForPlayerId: clients.clientForPlayerId,
  clientLabel: clients.clientLabel,
  countInGameDevices,
  countReadyDevices,
  game,
  maximumPlayers,
  minimumPlayers,
  pointsForPlayer,
  scoreStore
});

function awardPoints(player, amount) {
  const owner = clients.clientForPlayerId(player.id);
  if (!owner) {
    return;
  }
  scoreStore.award(owner.deviceId, clients.clientLabel(owner), amount);
}

function pointsForPlayer(player) {
  const owner = clients.clientForPlayerId(player.id);
  if (!owner) {
    return 0;
  }
  return scoreStore.pointsForDevice(owner.deviceId);
}

socket.on("message", (buffer, remote) => {
  if (DEBUG_UDP) {
    console.log(`[udp] ${remote.address}:${remote.port} ${buffer.toString("utf8")}`);
  }

  let envelope;
  try {
    envelope = JSON.parse(buffer.toString("utf8"));
  } catch (error) {
    if (DEBUG_UDP) {
      console.log(`[udp] rejected invalid json: ${error.message}`);
    }
    return;
  }

  const payload = envelope.payload || envelope;
  const deviceId = clients.sanitizeId(envelope.deviceId || payload.deviceId || "");
  const deviceName = clients.sanitizeDeviceName(envelope.deviceName || payload.deviceName || "");
  if (!deviceId) {
    if (DEBUG_UDP) {
      console.log("[udp] rejected packet without deviceId");
    }
    return;
  }

  const client = clients.registerClient(deviceId, deviceName, remote);

  const displayName = clients.sanitizeDeviceName(envelope.displayName || payload.displayName || "");
  if (displayName) {
    client.displayName = displayName;
  }
  // Unicité des couleurs : on n'applique une couleur demandée que si aucun autre
  // joueur connecté ne l'utilise déjà (sinon on garde l'actuelle).
  const requestedColor = clients.sanitizeColor(envelope.color || payload.color || "");
  if (!requestedColor || !clients.isColorTakenByOther(requestedColor, client)) {
    client.color = requestedColor;
  }

  if (payload.type === "hello") {
    sendSnapshot(client);
    return;
  }

  if (payload.type === "join" || payload.type === "start") {
    joinGame(client);
    broadcastSnapshot();
    return;
  }

  if (payload.type === "input") {
    client.input = circleMath.clamp(Number(payload.direction) || 0, -1, 1);
    client.lastSeen = Date.now();
    return;
  }

  if (payload.type === "restart") {
    resetToLobby(client);
    broadcastSnapshot();
    return;
  }

  if (payload.type === "replay") {
    voteReplay(client);
    broadcastSnapshot();
    return;
  }

  if (payload.type === "lobby") {
    returnToLobby();
    broadcastSnapshot();
  }
});

socket.on("listening", () => {
  const address = socket.address();
  console.log(`Pong UDP server listening on udp://${address.address}:${address.port}`);
  console.log(`Health listening on http://0.0.0.0:${HEALTH_PORT}/health`);
});

socket.bind(UDP_PORT, "0.0.0.0");

http.createServer((req, res) => {
  if (req.url.split("?")[0] !== "/health") {
    res.writeHead(404);
    res.end("Not found");
    return;
  }

  reconcileGameState();
  res.writeHead(200, { "Content-Type": "application/json; charset=utf-8" });
  res.end(JSON.stringify({
    ok: true,
    transport: "udp",
    version: SERVER_VERSION,
    udpPort: UDP_PORT,
    connectedPlayerCount: game.connectedPlayerCount,
    readyPlayerCount: game.readyPlayerCount,
    playerCount: game.playerCount,
    lobbyOpen: game.lobbyOpen,
    gameStarted: game.gameStarted,
    gameOver: game.gameOver,
    winnerId: game.winnerId,
    status: game.status,
    devices: snapshots.buildDeviceList(),
    lobbyDevices: snapshots.buildLobbyDeviceList(),
    scoreboard: scoreStore.buildScoreboard()
  }));
}).listen(HEALTH_PORT, "0.0.0.0");

// Vrai si un autre joueur connecté utilise déjà cette couleur (comparaison insensible à la casse).
function joinGame(client) {
  if (!client || game.gameOver) {
    return;
  }

  if (client.playerId <= 0 && clients.getReadyClients().length >= maximumPlayers) {
    return;
  }

  client.ready = true;

  if (game.gameStarted && !game.gameOver) {
    addReadyPlayerToRunningGame(client);
    return;
  }

  game.lobbyOpen = true;
  game.gameStarted = false;
  game.gameOver = false;
  game.replayVoteCount = 0;
  game.postGameDeadline = 0;
  game.winnerId = 0;
  assignLobbyPlayers();
}

function addReadyPlayerToRunningGame() {
  const activeClients = clients.getReadyClients();
  const previousPlayerCount = game.players.length;
  activeClients.forEach((readyClient, index) => {
    readyClient.playerId = index + 1;
  });

  rebuildPlayersForReadyClients(activeClients, true);
  if (game.players.length > previousPlayerCount) {
    for (let i = previousPlayerCount; i < game.players.length; i++) {
      game.players[i].alive = true;
      game.players[i].hasPaddleAngle = false;
    }
  }

  redistributeAlivePlayers();
  updateStatus();
}

function resetToLobby(requestingClient) {
  for (const client of clientsByDevice.values()) {
    client.ready = false;
    client.input = 0;
  }

  if (requestingClient) {
    requestingClient.ready = true;
  }

  game.lobbyOpen = true;
  game.gameStarted = false;
  game.gameOver = false;
  game.replayVoteCount = 0;
  game.postGameDeadline = 0;
  game.winnerId = 0;
  assignLobbyPlayers();
}

function returnToLobby() {
  for (const client of clientsByDevice.values()) {
    client.ready = false;
    client.input = 0;
    client.wantsReplay = false;
    client.playerId = 0;
  }

  game.lobbyOpen = false;
  game.gameStarted = false;
  game.gameOver = false;
  game.replayVoteCount = 0;
  game.postGameDeadline = 0;
  game.winnerId = 0;
  assignLobbyPlayers();
}

function assignLobbyPlayers() {
  const readyClients = clients.getReadyClients();

  for (const client of clientsByDevice.values()) {
    client.playerId = 0;
    client.input = 0;
  }

  game.connectedPlayerCount = 0;
  game.readyPlayerCount = readyClients.length;
  game.playerCount = Math.max(minimumPlayers, Math.min(maximumPlayers, readyClients.length));
  rebuildPlayersForReadyClients(readyClients, false);

  if (game.lobbyOpen) {
    readyClients.forEach((client, index) => {
      client.playerId = index + 1;
    });
  }

  redistributeAlivePlayers();
  resetBall();
  updateStatus();

}

function beginMatch() {
  game.lobbyOpen = true;
  game.gameStarted = true;
  game.gameOver = false;
  game.replayVoteCount = 0;
  game.postGameDeadline = 0;
  game.startDeadline = 0;
  game.winnerId = 0;
  game.status = "Playing";

  for (const player of game.players) {
    player.alive = true;
    player.hasPaddleAngle = false;
    player.lives = startingLives;
  }

  for (const client of clientsByDevice.values()) {
    client.wantsReplay = false;
  }

  // Score persistant
  for (const player of game.players) {
    const owner = clients.clientForPlayerId(player.id);
    if (owner) {
      scoreStore.recordGame(owner.deviceId, clients.clientLabel(owner));
    }
  }

  redistributeAlivePlayers();
  resetBall();
  updateStatus();
}

function rebuildPlayersForReadyClients(readyClients, preserveExistingPlayers) {
  const previousPlayers = new Map();
  if (preserveExistingPlayers) {
    for (const player of game.players) {
      previousPlayers.set(player.id, player);
    }
  }

  game.playerCount = Math.max(minimumPlayers, Math.min(maximumPlayers, readyClients.length));
  game.players = [];

  for (let i = 0; i < game.playerCount; i++) {
    const playerId = i + 1;
    const existingPlayer = previousPlayers.get(playerId);
    if (existingPlayer) {
      game.players.push(existingPlayer);
      continue;
    }

    game.players.push({
      id: playerId,
      alive: true,
      hasPaddleAngle: false,
      lives: startingLives,
      paddleAngle: 0,
      input: 0,
      sectorStartAngle: 0,
      sectorEndAngle: 0
    });
  }
}

function voteReplay(client) {
  if (!game.gameOver || !client || client.playerId <= 0) {
    return;
  }

  client.wantsReplay = true;
  game.replayVoteCount = countReplayVotes();
  updatePostGameStatus();
}

function tick() {
  const now = Date.now();
  const deltaTime = Math.min(0.05, (now - lastTick) / 1000);
  lastTick = now;

  clients.cleanupClients();
  reconcileGameState();
  updateStartCountdown();
  updateInputs();
  updatePostGameTimeout();
  updatePaddles(deltaTime);
  updateBall(deltaTime);
}


function updateStartCountdown() {
  if (game.gameOver || game.gameStarted) {
    game.startDeadline = 0;
    return;
  }

  const readyCount = clients.getReadyClients().length;
  if (!game.lobbyOpen || readyCount < minimumPlayers) {
    game.startDeadline = 0;
    return;
  }

  const now = Date.now();
  if (game.startDeadline <= 0) {
    game.startDeadline = now + startCountdownMs;
    game.countdownPlayerCount = readyCount;
  } else if (readyCount > game.countdownPlayerCount) {
    game.startDeadline = Math.min(now + startCountdownMaxMs, game.startDeadline + joinGraceMs);
    game.countdownPlayerCount = readyCount;
  }

  if (now >= game.startDeadline) {
    beginMatch();
    return;
  }

  updateStatus();
}

function reconcileGameState() {
  if (game.gameOver) {
    return;
  }

  const readyClients = clients.getReadyClients();
  if (!game.gameStarted && readyClients.length >= minimumPlayers) {
    game.lobbyOpen = true;
    assignLobbyPlayers();
    return;
  }

  if (game.gameStarted) {
    if (readyClients.length < minimumPlayers) {
      returnToLobby();
      return;
    }

    let changed = readyClients.length !== game.players.length;
    readyClients.forEach((client, index) => {
      const expectedPlayerId = index + 1;
      if (client.playerId !== expectedPlayerId) {
        client.playerId = expectedPlayerId;
        changed = true;
      }
    });

    if (changed) {
      rebuildPlayersForReadyClients(readyClients, true);
      redistributeAlivePlayers();
    }
  }

  updateStatus();
}

function updatePostGameTimeout() {
  if (!game.gameOver || game.postGameDeadline <= 0) {
    return;
  }

  if (Date.now() >= game.postGameDeadline) {
    game.replayVoteCount = countReplayVotes();
    if (game.replayVoteCount >= minimumPlayers) {
      beginMatch();
    } else {
      returnToLobby();
    }
    broadcastSnapshot();
  } else {
    updatePostGameStatus();
  }
}

function updateInputs() {
  for (const player of game.players) {
    player.input = 0;
  }

  for (const client of clientsByDevice.values()) {
    const player = game.players[client.playerId - 1];
    if (player) {
      player.input = client.input;
    }
  }
}

function updatePaddles(deltaTime) {
  if (!game.gameStarted || game.gameOver) {
    return;
  }

  for (const player of game.players) {
    if (!player.alive) {
      continue;
    }

    player.paddleAngle += player.input * paddleAngularSpeed * deltaTime;
    player.paddleAngle = circleMath.clampPaddleAngle(player.paddleAngle, player.sectorStartAngle, player.sectorEndAngle, paddleArcDegrees);
  }
}

function updateBall(deltaTime) {
  if (!game.gameStarted || game.gameOver) {
    return;
  }

  game.ballX += game.ballDirX * ballSpeed * deltaTime;
  game.ballY += game.ballDirY * ballSpeed * deltaTime;

  const radius = Math.hypot(game.ballX, game.ballY);
  if (radius < arenaRadius) {
    return;
  }

  const angle = circleMath.directionToAngle(game.ballX, game.ballY);
  const defender = findPlayerAtAngle(angle);
  if (!defender || !defender.alive) {
    resetBall();
    return;
  }

  const paddleDelta = Math.abs(circleMath.deltaAngle(angle, defender.paddleAngle));
  if (paddleDelta <= paddleArcDegrees * 0.5) {
    bounceOnPaddle(defender, angle);
  } else {
    concedeGoal(defender);
  }
}

// Une balle ratée coûte une vie ; l'élimination réelle n'arrive qu'à 0 vie.
// Évite les parties qui se terminent dès le premier raté.
function concedeGoal(defender) {
  defender.lives -= 1;

  if (defender.lives <= 0) {
    eliminatePlayer(defender);
    return;
  }

  game.status = `Player ${defender.id} lost a life (${defender.lives} left)`;
  resetBall();
}

function updateStatus() {
  game.connectedPlayerCount = countInGameDevices();
  game.readyPlayerCount = countReadyDevices();

  if (game.gameOver) {
    return;
  }

  if (game.gameStarted) {
    game.status = `Playing ${game.connectedPlayerCount}/${maximumPlayers} players`;
    return;
  }

  if (game.lobbyOpen) {
    if (game.startDeadline > 0) {
      const seconds = Math.max(0, Math.ceil((game.startDeadline - Date.now()) / 1000));
      game.status = `Starting in ${seconds}s — ${game.readyPlayerCount} ready`;
      return;
    }
    game.status = `Lobby open: ${game.readyPlayerCount}/${minimumPlayers} ready, max ${maximumPlayers}`;
    return;
  }

  game.status = "Waiting for someone to start a game";
}

function countInGameDevices() {
  let count = 0;
  for (const client of clientsByDevice.values()) {
    if (client.ready && client.playerId > 0) {
      count++;
    }
  }
  return Math.min(count, maximumPlayers);
}

function countReadyDevices() {
  return Math.min(clients.getConnectedClients().filter((client) => client.ready).length, maximumPlayers);
}

function countReplayVotes() {
  let count = 0;
  for (const client of clientsByDevice.values()) {
    if (client.playerId > 0 && client.wantsReplay) {
      count++;
    }
  }
  return count;
}

function bounceOnPaddle(defender, impactAngle) {
  const impactDirection = circleMath.angleToDirection(impactAngle);
  const nextDirection = ballRules.calculateBounceDirection(
    { x: game.ballDirX, y: game.ballDirY },
    defender.paddleAngle,
    impactAngle,
    paddleArcDegrees,
    paddleAimInfluence);
  game.ballDirX = nextDirection.x;
  game.ballDirY = nextDirection.y;
  game.ballX = impactDirection.x * (arenaRadius - 0.12);
  game.ballY = impactDirection.y * (arenaRadius - 0.12);
}

function eliminatePlayer(player) {
  player.alive = false;

  // Score de survie 
  for (const survivor of game.players) {
    if (survivor.alive) {
      awardPoints(survivor, survivalPoints);
    }
  }

  const alivePlayers = game.players.filter((candidate) => candidate.alive);

  if (alivePlayers.length <= 1) {
    const winner = alivePlayers[0] || null;
    game.winnerId = winner ? winner.id : 0;
    if (winner) {
      awardPoints(winner, winBonusPoints);
      const owner = clients.clientForPlayerId(winner.id);
      if (owner) {
        scoreStore.recordWin(owner.deviceId, clients.clientLabel(owner));
      }
    }
    game.gameOver = true;
    game.gameStarted = false;
    game.startDeadline = 0;
    game.replayVoteCount = 0;
    game.postGameDeadline = Date.now() + 30000;
    for (const client of clientsByDevice.values()) {
      client.wantsReplay = false;
    }
    scoreStore.flush();
    updatePostGameStatus();
    return;
  }

  game.status = `Player ${player.id} eliminated`;
  redistributeAlivePlayers();
  resetBall();
}

function updatePostGameStatus() {
  if (!game.gameOver) {
    return;
  }

  game.replayVoteCount = countReplayVotes();
  const seconds = Math.max(0, Math.ceil((game.postGameDeadline - Date.now()) / 1000));
  const winnerText = game.winnerId > 0 ? `Player ${game.winnerId} wins!` : "No winner";
  game.status = `${winnerText} Replay ${game.replayVoteCount}/${minimumPlayers}, lobby in ${seconds}s`;
}

function redistributeAlivePlayers() {
  const alivePlayers = game.players.filter((player) => player.alive);
  if (alivePlayers.length === 0) {
    return;
  }

  const sectorSize = 360 / alivePlayers.length;
  alivePlayers.forEach((player, aliveIndex) => {
    const sectorCenter = aliveIndex * sectorSize;
    player.sectorStartAngle = sectorCenter - sectorSize * 0.5;
    player.sectorEndAngle = sectorCenter + sectorSize * 0.5;

    if (!player.hasPaddleAngle) {
      player.paddleAngle = sectorCenter;
      player.hasPaddleAngle = true;
    } else {
    player.paddleAngle = circleMath.clampPaddleAngle(player.paddleAngle, player.sectorStartAngle, player.sectorEndAngle, paddleArcDegrees);
    }
  });
}

function findPlayerAtAngle(angle) {
  return game.players.find((player) => circleMath.angleInsideSector(angle, player.sectorStartAngle, player.sectorEndAngle));
}

function resetBall() {
  game.ballX = 0;
  game.ballY = 0;
  const direction = ballRules.randomDirection();
  game.ballDirX = direction.x;
  game.ballDirY = direction.y;
}


let lastMetaSignature = "";
let lastFullBroadcastTime = 0;

function broadcastSnapshot() {
  const signature = snapshots.metaSignature();
  const now = Date.now();
  const full = signature !== lastMetaSignature || (now - lastFullBroadcastTime) >= 1000;
  if (full) {
    lastMetaSignature = signature;
    lastFullBroadcastTime = now;
  }

  for (const client of clientsByDevice.values()) {
    sendSnapshot(client, full);
  }
}

function sendSnapshot(client, full = true) {
  const message = Buffer.from(JSON.stringify(snapshots.buildSnapshot(client, full)));
  socket.send(message, client.port, client.address);
}

assignLobbyPlayers();
setInterval(tick, 1000 / 60);
setInterval(broadcastSnapshot, 1000 / 30);
setInterval(scoreStore.flush, 5000);
