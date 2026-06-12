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
const SERVER_VERSION = "udp-authoritative-2026-06-11-score";

const arenaRadius = 5;
const paddleArcDegrees = 22;
const paddleAngularSpeed = 120;
const ballSpeed = 3.5;
const paddleAimInfluence = 0.45;

const smashMultiplier = 2.2;    
const smashWindowMs = 2500;     

const deadlyIntervalMs = 7000;  
const deadlyJitterMs = 3000;    
const smashCooldownMs = 1500;   
const minimumPlayers = 2;
const maximumPlayers = 10;
const clientTimeoutMs = 10000;


const startCountdownMs = 5000;
const joinGraceMs = 2000;         
const startCountdownMaxMs = 15000;  

const startingLives = 3;
const postGameDurationMs = 5000;
const chatHistoryLimit = 30;
const chatMessageMaxLength = 140;

const raceIntervalMs = 20000;
const raceDurationMs = 5000;
const raceWinnerDisplayMs = 3000;
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
  forfeitWinnerIds: [],
  ballSpeedMul: 1,
  ballDeadly: false,
  nextDeadlyTime: 0,
  lastHitPlayerId: 0,
  status: "Waiting for someone to start a game",
  race: {
    active: false,
    deadline: 0,
    winnerId: 0,
    winnerName: "",
    winnerDisplayDeadline: 0,
    nextRaceTime: 0
  }
};

let nextChatMessageId = 1;
let lastTick = Date.now();
const lobbyChat = [];

const scoreStore = createScoreStore(scoresFilePath);
const clientRegistry = createClientRegistry({
  clientTimeoutMs,
  maximumPlayers,
  onChange: updateStatus
});
const {
  clientForPlayerId,
  clientLabel,
  clientsByDevice,
  cleanupClients,
  countInGameDevices,
  countReadyDevices,
  countSpectators,
  getConnectedClients,
  getReadyClients,
  isColorTakenByOther,
  registerClient,
  sanitizeColor,
  sanitizeDeviceName,
  sanitizeId
} = clientRegistry;
const {
  angleInsideSector,
  angleToDirection,
  clamp,
  deltaAngle,
  directionToAngle
} = circleMath;
const snapshotBuilder = createSnapshotBuilder({
  clientsByDevice,
  clientForPlayerId,
  clientLabel,
  countInGameDevices,
  countReadyDevices,
  countSpectators,
  game,
  lobbyChat,
  ballSpeed,
  maximumPlayers,
  minimumPlayers,
  scoreStore
});
const {
  buildDeviceList,
  buildLobbyDeviceList,
  buildSnapshot,
  metaSignature
} = snapshotBuilder;

function updateBestScores() {
  for (const player of game.players) {
    if (!player.gamePoints || player.gamePoints <= 0) continue;
    const owner = clientForPlayerId(player.id);
    if (!owner) continue;
    scoreStore.updateBestScore(owner.deviceId, clientLabel(owner), player.gamePoints);
  }
}

function topHighScore() {
  const top = scoreStore.buildScoreboard()[0];
  return top ? { name: top.name, score: top.bestScore } : { name: "", score: 0 };
}

socket.on("message", (buffer, remote) => {
  let envelope;
  try {
    envelope = JSON.parse(buffer.toString("utf8"));
  } catch {
    return;
  }

  const payload = envelope.payload || envelope;
  const deviceId = sanitizeId(envelope.deviceId || payload.deviceId || "");
  const deviceName = sanitizeDeviceName(envelope.deviceName || payload.deviceName || "");
  if (!deviceId) {
    return;
  }

  const client = registerClient(deviceId, deviceName, remote);

  const displayName = sanitizeDeviceName(envelope.displayName || payload.displayName || "");
  if (displayName) {
    client.displayName = displayName;
  }
  // Unicité des couleurs : on n'applique une couleur demandée que si aucun autre
  // joueur connecté ne l'utilise déjà (sinon on garde l'actuelle).
  const requestedColor = sanitizeColor(envelope.color || payload.color || "");
  if (!requestedColor || !isColorTakenByOther(requestedColor, client)) {
    client.color = requestedColor;
  }

  if (payload.type === "hello") {
    sendSnapshot(client);
    return;
  }

  if (payload.type === "chat") {
    if (!game.gameStarted && !game.gameOver) {
      postChatMessage(client, payload.text);
      broadcastSnapshot();
    }
    return;
  }

  if (payload.type === "join" || payload.type === "start") {
    joinGame(client);
    broadcastSnapshot();
    return;
  }

  if (payload.type === "spectate") {
    spectateGame(client);
    broadcastSnapshot();
    return;
  }

  if (payload.type === "input") {
    if (client.spectator || client.playerId <= 0) {
      return;
    }
    client.input = clamp(Number(payload.direction) || 0, -1, 1);
    client.lastSeen = Date.now();
    return;
  }

  if (payload.type === "smash") {
    const now = Date.now();
    if (now >= (client.smashCooldownUntil || 0)) {
      client.smashArmedUntil = now + smashWindowMs; 
    }
    client.lastSeen = now;
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

  if (payload.type === "leave") {
    leaveGameForClient(client);
    broadcastSnapshot();
    return;
  }

  if (payload.type === "lobby") {
    if (game.gameOver) {
      returnToLobby();
    } else {
      leaveGameForClient(client);
    }
    broadcastSnapshot();
    return;
  }

  if (payload.type === "race") {
    handleRaceAction(client);
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
    spectatorCount: countSpectators(),
    playerCount: game.playerCount,
    lobbyOpen: game.lobbyOpen,
    gameStarted: game.gameStarted,
    gameOver: game.gameOver,
    winnerId: game.winnerId,
    status: game.status,
    devices: buildDeviceList(),
    lobbyDevices: buildLobbyDeviceList(),
    scoreboard: scoreStore.buildScoreboard()
  }));
}).listen(HEALTH_PORT, "0.0.0.0");

function sanitizeChatText(value) {
  return String(value || "")
    .replace(/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/g, "")
    .replace(/\s+/g, " ")
    .trim()
    .slice(0, chatMessageMaxLength);
}

function postChatMessage(client, text) {
  const clean = sanitizeChatText(text);
  if (!clean) {
    return;
  }

  lobbyChat.push({
    id: nextChatMessageId++,
    name: clientLabel(client),
    text: clean
  });

  while (lobbyChat.length > chatHistoryLimit) {
    lobbyChat.shift();
  }
}

function joinGame(client) {
  if (!client || game.gameOver) {
    return;
  }

  if (client.playerId <= 0 && getReadyClients().length >= maximumPlayers) {
    return;
  }

  client.ready = true;
  client.spectator = false;

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
  game.forfeitWinnerIds = [];
  assignLobbyPlayers();
}

function spectateGame(client) {
  if (!client) {
    return;
  }

  client.ready = false;
  client.spectator = true;
  client.input = 0;
  client.wantsReplay = false;
  client.playerId = 0;

  if (!game.gameStarted && !game.gameOver) {
    assignLobbyPlayers();
    return;
  }

  updateStatus();
}

function addReadyPlayerToRunningGame(joiningClient) {
  const activeClients = getReadyClients();
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

  if (joiningClient && joiningClient.playerId > 0) {
    const player = game.players[joiningClient.playerId - 1];
    if (player) {
      revivePlayer(player);
    }
  }

  redistributeAlivePlayers();
  resetBall();
  updateStatus();
}

function revivePlayer(player) {
  player.alive = true;
  player.lives = startingLives;
  player.input = 0;
  player.hasPaddleAngle = false;
  player.gamePoints = player.gamePoints || 0;
  game.winnerId = 0;
  game.forfeitWinnerIds = [];
  game.gameOver = false;
  game.gameStarted = true;
  game.lobbyOpen = true;
  game.status = `Player ${player.id} rejoined`;
}

function resetToLobby(requestingClient) {
  for (const client of clientsByDevice.values()) {
    client.ready = false;
    client.spectator = false;
    client.input = 0;
  }

  if (requestingClient) {
    requestingClient.ready = true;
    requestingClient.spectator = false;
  }

  game.lobbyOpen = true;
  game.gameStarted = false;
  game.gameOver = false;
  game.replayVoteCount = 0;
  game.postGameDeadline = 0;
  game.winnerId = 0;
  game.forfeitWinnerIds = [];
  assignLobbyPlayers();
}

function returnToLobby() {
  for (const client of clientsByDevice.values()) {
    client.ready = false;
    client.spectator = false;
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
  game.forfeitWinnerIds = [];
  assignLobbyPlayers();
}

function leaveGameForClient(client) {
  const leavingPlayer = client && client.playerId > 0
    ? game.players[client.playerId - 1]
    : null;

  client.ready = false;
  client.spectator = false;
  client.input = 0;
  client.wantsReplay = false;
  client.playerId = 0;

  if (game.gameStarted && !game.gameOver && leavingPlayer) {
    leavingPlayer.alive = false;
    leavingPlayer.lives = 0;
    leavingPlayer.input = 0;

    if (getReadyClients().length < minimumPlayers) {
      endMatchBecauseBelowMinimum(leavingPlayer);
      return;
    }

    redistributeAlivePlayers();
  }

  if (!game.gameStarted && !game.gameOver) {
    if (getReadyClients().length === 0) {
      game.lobbyOpen = false;
      game.startDeadline = 0;
    }
    assignLobbyPlayers();
    return;
  }

  updateStatus();
}

function endMatchBecauseBelowMinimum(leavingPlayer) {
  const winners = findForfeitWinners(leavingPlayer);
  const winnerIds = winners.map((winner) => winner.id);
  game.winnerId = winnerIds.length > 0 ? winnerIds[0] : 0;
  game.forfeitWinnerIds = winnerIds;

  updateBestScores();

  
  for (const client of clientsByDevice.values()) {
    client.input = 0;
    client.wantsReplay = false;
  }

  game.lobbyOpen = false;
  game.gameStarted = false;
  game.gameOver = true;
  game.replayVoteCount = 0;
  game.postGameDeadline = Date.now() + postGameDurationMs;
  game.startDeadline = 0;
  game.status = winners.length > 0
    ? `${formatForfeitWinners(winners)}: not enough players to continue`
    : "Game stopped: not enough players to continue";
  scoreStore.flush();
}

function findForfeitWinners(leavingPlayer) {
  const readyPlayerIds = new Set(getReadyClients().map((client) => client.playerId));
  return game.players.filter((player) => player !== leavingPlayer && readyPlayerIds.has(player.id));
}

function formatForfeitWinners(players) {
  if (players.length === 1) {
    return `Player ${players[0].id} wins by forfeit`;
  }
  return `Players ${players.map((player) => player.id).join(", ")} win by forfeit`;
}

function assignLobbyPlayers() {
  const readyClients = getReadyClients();

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
  game.forfeitWinnerIds = [];
  game.ballDeadly = false;
  game.nextDeadlyTime = Date.now() + 4000;
  game.status = "Playing";

  for (const player of game.players) {
    player.alive = true;
    player.hasPaddleAngle = false;
    player.lives = startingLives;
    player.gamePoints = 0;
  }
  game.lastHitPlayerId = 0;

  for (const client of clientsByDevice.values()) {
    client.wantsReplay = false;
  }

  // Score persistant
  for (const player of game.players) {
    const owner = clientForPlayerId(player.id);
    if (owner) {
      scoreStore.getEntry(owner.deviceId, clientLabel(owner));
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
      gamePoints: 0,
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
  if (game.replayVoteCount >= minimumPlayers) {
    beginMatch();
    return;
  }

  updatePostGameStatus();
}

function updateRace(now) {
  const race = game.race;
  if (!game.gameStarted || game.gameOver) {
    race.active = false;
    race.winnerId = 0;
    race.winnerName = "";
    race.nextRaceTime = 0;
    race.winnerDisplayDeadline = 0;
    return;
  }

  if (race.winnerId > 0) {
    if (now >= race.winnerDisplayDeadline) {
      race.winnerId = 0;
      race.winnerName = "";
      race.nextRaceTime = now + raceIntervalMs;
    }
    return;
  }

  if (race.nextRaceTime === 0) {
    race.nextRaceTime = now + raceIntervalMs;
    return;
  }

  if (!race.active && now >= race.nextRaceTime) {
    race.active = true;
    race.deadline = now + raceDurationMs;
    return;
  }

  if (race.active && now >= race.deadline) {
    race.active = false;
    race.nextRaceTime = now + raceIntervalMs;
  }
}

function handleRaceAction(client) {
  const race = game.race;
  if (!race.active || race.winnerId > 0) return;
  const player = game.players.find((p) => clientForPlayerId(p.id) === client);
  if (!player?.alive) return;

  race.active = false;
  race.winnerId = player.id;
  race.winnerName = clientLabel(client);
  race.winnerDisplayDeadline = Date.now() + raceWinnerDisplayMs;
  race.nextRaceTime = race.winnerDisplayDeadline + raceIntervalMs;
  player.gamePoints = (player.gamePoints || 0) + 2;
  game.status = `${race.winnerName} remporte la course ! +2 pts`;
}

function tick() {
  const now = Date.now();
  const deltaTime = Math.min(0.05, (now - lastTick) / 1000);
  lastTick = now;

  cleanupClients();
  reconcileGameState();
  updateStartCountdown();
  updateInputs();
  updatePostGameTimeout();
  updatePaddles(deltaTime);
  updateDeadlyBall();
  updateBall(deltaTime);
  updateRace(now);
}

function updateDeadlyBall() {
  if (!game.gameStarted || game.gameOver) {
    return;
  }
  const now = Date.now();
  if (!game.ballDeadly && now >= game.nextDeadlyTime) {
    game.ballDeadly = true;
    game.nextDeadlyTime = now + deadlyIntervalMs + Math.random() * deadlyJitterMs;
    game.status = "Balle mortelle ! Esquive-la";
  }
}


function updateStartCountdown() {
  if (game.gameOver || game.gameStarted) {
    game.startDeadline = 0;
    return;
  }

  const readyCount = getReadyClients().length;
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

  const readyClients = getReadyClients();
  if (!game.gameStarted && readyClients.length >= minimumPlayers) {
    game.lobbyOpen = true;
    assignLobbyPlayers();
    return;
  }

  if (game.gameStarted) {
    if (readyClients.length < minimumPlayers) {
      endMatchBecauseBelowMinimum(null);
      return;
    }

    updateStatus();
    return;
  }

  updateStatus();
}

function updatePostGameTimeout() {
  if (!game.gameOver || game.postGameDeadline <= 0) {
    return;
  }

  game.replayVoteCount = countReplayVotes();
  if (game.replayVoteCount >= minimumPlayers) {
    beginMatch();
    broadcastSnapshot();
    return;
  }

  if (Date.now() >= game.postGameDeadline) {
    returnToLobby();
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
    player.paddleAngle = circleMath.clampPaddleAngle(
      player.paddleAngle,
      player.sectorStartAngle,
      player.sectorEndAngle,
      paddleArcDegrees
    );
  }
}

function updateBall(deltaTime) {
  if (!game.gameStarted || game.gameOver) {
    return;
  }

  const speed = ballSpeed * game.ballSpeedMul;
  game.ballX += game.ballDirX * speed * deltaTime;
  game.ballY += game.ballDirY * speed * deltaTime;

  const radius = Math.hypot(game.ballX, game.ballY);
  if (radius < arenaRadius) {
    return;
  }

  const angle = directionToAngle(game.ballX, game.ballY);
  const defender = findPlayerAtAngle(angle);
  if (!defender || !defender.alive) {
    resetBall();
    return;
  }

  const paddleDelta = Math.abs(deltaAngle(angle, defender.paddleAngle));
  const intercepts = paddleDelta <= paddleArcDegrees * 0.5;

  if (game.ballDeadly) {
    game.ballDeadly = false;
    if (intercepts) {
      game.status = `Player ${defender.id} touched the deadly ball! -1`;
      concedeGoal(defender);
    } else {
      game.status = `Player ${defender.id} dodged the deadly ball!`;
      defender.gamePoints = (defender.gamePoints || 0) + 2;
      resetBall();
    }
    return;
  }

  if (intercepts) {
    const owner = clientForPlayerId(defender.id);
    const now = Date.now();
    if (owner && now <= (owner.smashArmedUntil || 0)) {
      game.ballSpeedMul = smashMultiplier;
      owner.smashArmedUntil = 0;
      owner.smashCooldownUntil = now + smashCooldownMs;
      game.status = `Player ${defender.id} SMASH!`;
      defender.gamePoints = (defender.gamePoints || 0) + 3;
    } else {
      game.ballSpeedMul = 1;
    }
    bounceOnPaddle(defender, angle);
  } else {
    concedeGoal(defender);
  }
}

// Une balle ratée coûte une vie ; l'élimination réelle n'arrive qu'à 0 vie.
// Évite les parties qui se terminent dès le premier raté.
function concedeGoal(defender) {
  defender.lives -= 1;

  const scorer = game.lastHitPlayerId > 0 && game.lastHitPlayerId !== defender.id
    ? game.players.find((p) => p.id === game.lastHitPlayerId && p.alive)
    : null;
  if (scorer) scorer.gamePoints = (scorer.gamePoints || 0) + 1;
  game.lastHitPlayerId = 0;

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

  if (!game.gameStarted && game.winnerId > 0) {
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
  game.lastHitPlayerId = defender.id;
  const impactDirection = angleToDirection(impactAngle);
  const nextDirection = ballRules.calculateBounceDirection(
    { x: game.ballDirX, y: game.ballDirY },
    defender.paddleAngle,
    impactAngle,
    paddleArcDegrees,
    paddleAimInfluence
  );
  game.ballDirX = nextDirection.x;
  game.ballDirY = nextDirection.y;

  game.ballX = impactDirection.x * (arenaRadius - 0.12);
  game.ballY = impactDirection.y * (arenaRadius - 0.12);
}

function eliminatePlayer(player) {
  player.alive = false;

  if (countAliveReadyPlayers() < minimumPlayers) {
    endMatchBecauseBelowMinimum(null);
    return;
  }

  game.status = `Player ${player.id} eliminated`;
  redistributeAlivePlayers();
  resetBall();
}

function countAliveReadyPlayers() {
  const readyPlayerIds = new Set(getReadyClients().map((client) => client.playerId));
  return game.players.filter((player) => readyPlayerIds.has(player.id) && player.alive).length;
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
      player.paddleAngle = circleMath.clampPaddleAngle(
        player.paddleAngle,
        player.sectorStartAngle,
        player.sectorEndAngle,
        paddleArcDegrees
      );
    }
  });
}

function findPlayerAtAngle(angle) {
  return game.players.find((player) => angleInsideSector(angle, player.sectorStartAngle, player.sectorEndAngle));
}

function resetBall() {
  game.ballX = 0;
  game.ballY = 0;
  const angle = Math.random() * 360;
  const direction = angleToDirection(angle);
  game.ballDirX = direction.x;
  game.ballDirY = direction.y;
  game.ballSpeedMul = 1;
  game.ballDeadly = false;
  game.lastHitPlayerId = 0;
}

let lastMetaSignature = "";
let lastFullBroadcastTime = 0;

function broadcastSnapshot() {
  const signature = metaSignature();
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
  const message = Buffer.from(JSON.stringify(buildSnapshot(client, full)));
  socket.send(message, client.port, client.address);
}

assignLobbyPlayers();
setInterval(tick, 1000 / 60);
setInterval(broadcastSnapshot, 1000 / 30);
setInterval(() => scoreStore.flush(), 5000);
