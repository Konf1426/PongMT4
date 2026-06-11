const dgram = require("dgram");
const http = require("http");
const fs = require("fs");
const path = require("path");

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
const chatHistoryLimit = 30;
const chatMessageMaxLength = 140;

const raceIntervalMs = 20000;
const raceDurationMs = 5000;
const raceWinnerDisplayMs = 3000;
const scoresFilePath = path.join(__dirname, "scores.json");


const socket = dgram.createSocket("udp4");
const clientsByDevice = new Map();
const clientsByAddress = new Map();

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

let nextClientId = 1;
let nextChatMessageId = 1;
let lastTick = Date.now();
const lobbyChat = [];

const scoreboard = loadScores();
let scoresDirty = false;

function loadScores() {
  try {
    const data = JSON.parse(fs.readFileSync(scoresFilePath, "utf8"));
    const map = new Map();
    if (Array.isArray(data)) {
      for (const entry of data) {
        if (entry && entry.deviceId) {
          map.set(entry.deviceId, {
            name: entry.name || "",
            points: entry.points || 0,
            wins: entry.wins || 0,
            games: entry.games || 0
          });
        }
      }
    }
    return map;
  } catch {
    return new Map();
  }
}

function getScoreEntry(deviceId, name) {
  let entry = scoreboard.get(deviceId);
  if (!entry) {
    entry = { name: name || "", points: 0, wins: 0, games: 0 };
    scoreboard.set(deviceId, entry);
  }
  if (name) {
    entry.name = name;
  }
  return entry;
}

function awardPoints(player, amount) {
  const owner = clientForPlayerId(player.id);
  if (!owner) {
    return;
  }
  getScoreEntry(owner.deviceId, clientLabel(owner)).points += amount;
  scoresDirty = true;
}

function pointsForPlayer(player) {
  const owner = clientForPlayerId(player.id);
  if (!owner) {
    return 0;
  }
  const entry = scoreboard.get(owner.deviceId);
  return entry ? entry.points : 0;
}

function flushScores() {
  if (!scoresDirty) {
    return;
  }
  scoresDirty = false;
  const data = Array.from(scoreboard.entries()).map(([deviceId, entry]) => ({
    deviceId,
    name: entry.name,
    points: entry.points,
    wins: entry.wins,
    games: entry.games
  }));
  fs.writeFile(scoresFilePath, JSON.stringify(data, null, 2), () => {});
}

function buildScoreboard() {
  return Array.from(scoreboard.values())
    .filter((entry) => entry.games > 0 || entry.points > 0)
    .sort((a, b) => b.points - a.points || b.wins - a.wins)
    .slice(0, 10)
    .map((entry) => ({
      name: entry.name || "Anonyme",
      points: entry.points,
      wins: entry.wins,
      games: entry.games
    }));
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

  if (payload.type === "lobby") {
    leaveGameForClient(client);
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
    scoreboard: buildScoreboard()
  }));
}).listen(HEALTH_PORT, "0.0.0.0");

function registerClient(deviceId, deviceName, remote) {
  const addressKey = `${remote.address}:${remote.port}`;
  let client = clientsByDevice.get(deviceId);

  if (!client) {
    client = {
      id: nextClientId++,
      deviceId,
      deviceName: deviceName || "Device",
      displayName: "",
      color: "",
      address: remote.address,
      port: remote.port,
      playerId: 0,
      ready: false,
      spectator: false,
      input: 0,
      wantsReplay: false,
      smashArmedUntil: 0,
      smashCooldownUntil: 0,
      lastSeen: Date.now()
    };
    clientsByDevice.set(deviceId, client);
  }

  client.deviceName = getUniqueDeviceName(deviceName || client.deviceName, client);
  client.address = remote.address;
  client.port = remote.port;
  client.lastSeen = Date.now();
  clientsByAddress.set(addressKey, client);
  updateStatus();
  return client;
}

function sanitizeId(value) {
  return String(value || "").replace(/[^\w.-]/g, "").slice(0, 80);
}

function getUniqueDeviceName(deviceName, currentClient) {
  const baseName = sanitizeDeviceName(deviceName) || "Device";
  let sameTypeCount = 0;

  for (const client of clientsByDevice.values()) {
    if (client !== currentClient && sanitizeDeviceName(client.deviceName) === baseName) {
      sameTypeCount++;
    }
  }

  return sameTypeCount > 0 ? `${baseName} ${sameTypeCount + 1}` : baseName;
}

function sanitizeDeviceName(deviceName) {
  return String(deviceName || "")
    .replace(/[^\w .-]/g, "")
    .trim()
    .slice(0, 24);
}

function sanitizeColor(value) {
  return String(value || "").replace(/[^0-9a-fA-F]/g, "").slice(0, 6);
}

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

// Vrai si un autre joueur connecté utilise déjà cette couleur (comparaison insensible à la casse).
function isColorTakenByOther(color, self) {
  const target = color.toLowerCase();
  for (const client of getConnectedClients()) {
    if (client !== self && !client.spectator && client.color && client.color.toLowerCase() === target) {
      return true;
    }
  }
  return false;
}

function clientLabel(client) {
  return client.displayName && client.displayName.length > 0
    ? client.displayName
    : (client.deviceName || "Device");
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

function addReadyPlayerToRunningGame() {
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

  redistributeAlivePlayers();
  updateStatus();
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

  if (game.gameStarted && !game.gameOver && leavingPlayer && leavingPlayer.alive) {
    eliminatePlayer(leavingPlayer);
    return;
  }

  if (getReadyClients().length === 0) {
    game.lobbyOpen = false;
    game.gameStarted = false;
    game.gameOver = false;
    game.replayVoteCount = 0;
    game.postGameDeadline = 0;
    game.winnerId = 0;
  }
  updateStatus();
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
      getScoreEntry(owner.deviceId, clientLabel(owner)).games += 1;
      scoresDirty = true;
    }
  }

  redistributeAlivePlayers();
  resetBall();
  updateStatus();
}

function getConnectedClients() {
  return Array.from(clientsByDevice.values())
    .filter((client) => Date.now() - client.lastSeen <= clientTimeoutMs)
    .sort((a, b) => a.id - b.id);
}

function getReadyClients() {
  return getConnectedClients().filter((client) => client.ready && !client.spectator).slice(0, maximumPlayers);
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

function cleanupClients() {
  const now = Date.now();
  for (const [deviceId, client] of clientsByDevice.entries()) {
    if (now - client.lastSeen > clientTimeoutMs) {
      clientsByDevice.delete(deviceId);
    }
  }
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
    if (readyClients.length === 0) {
      returnToLobby();
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
    player.paddleAngle = clampPaddleAngle(player.paddleAngle, player.sectorStartAngle, player.sectorEndAngle);
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
  return Math.min(getConnectedClients().filter((client) => client.ready && !client.spectator).length, maximumPlayers);
}

function countSpectators() {
  return getConnectedClients().filter((client) => client.spectator).length;
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
  const impactDirection = angleToDirection(impactAngle); // radial sortant au point d'impact
  const inwardX = -impactDirection.x;
  const inwardY = -impactDirection.y;

  // Rebond propre : réflexion autour de la normale radiale AU POINT D'IMPACT (et non au
  // centre de la raquette) → angle prévisible et toujours orienté vers l'intérieur.
  const dot = game.ballDirX * inwardX + game.ballDirY * inwardY;
  let dirX = game.ballDirX - 2 * dot * inwardX;
  let dirY = game.ballDirY - 2 * dot * inwardY;

  // "Effet" : pousse tangentiellement selon l'endroit touché sur la raquette (offset borné).
  const offset = clamp(deltaAngle(defender.paddleAngle, impactAngle) / (paddleArcDegrees * 0.5), -1, 1);
  const tangent = { x: -impactDirection.y, y: impactDirection.x };
  dirX += tangent.x * offset * paddleAimInfluence;
  dirY += tangent.y * offset * paddleAimInfluence;

  // Garantit une vraie composante vers l'intérieur (anti-rasage du bord, plus de rebonds en chaîne).
  const inwardDot = dirX * inwardX + dirY * inwardY;
  if (inwardDot < 0.45) {
    dirX += inwardX * (0.45 - inwardDot);
    dirY += inwardY * (0.45 - inwardDot);
  }

  const length = Math.hypot(dirX, dirY) || 1;
  game.ballDirX = dirX / length;
  game.ballDirY = dirY / length;
  game.ballX = impactDirection.x * (arenaRadius - 0.12);
  game.ballY = impactDirection.y * (arenaRadius - 0.12);
}

function eliminatePlayer(player) {
  player.alive = false;

  const alivePlayers = game.players.filter((candidate) => candidate.alive);

  if (alivePlayers.length <= 1) {
    const winner = alivePlayers[0] || null;
    game.winnerId = winner ? winner.id : 0;
    if (winner) {
      const owner = clientForPlayerId(winner.id);
      if (owner) {
        getScoreEntry(owner.deviceId, clientLabel(owner)).wins += 1;
        scoresDirty = true;
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
    flushScores();
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
      player.paddleAngle = clampPaddleAngle(player.paddleAngle, player.sectorStartAngle, player.sectorEndAngle);
    }
  });
}

function findPlayerAtAngle(angle) {
  return game.players.find((player) => angleInsideSector(angle, player.sectorStartAngle, player.sectorEndAngle));
}

function angleInsideSector(angle, startAngle, endAngle) {
  const center = lerpAngle(startAngle, endAngle, 0.5);
  const halfSize = Math.abs(deltaAngle(startAngle, endAngle)) * 0.5;
  return Math.abs(deltaAngle(center, angle)) <= halfSize;
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


function buildSnapshot(client, full = true) {
  const alivePlayerCount = game.players.filter((player) => player.alive).length;
  const snapshot = {
    type: "state",
    localPlayerId: client && !client.spectator ? client.playerId : 0,
    localIsSpectator: !!(client && client.spectator),
    lobbyOpen: game.lobbyOpen,
    connectedPlayerCount: countInGameDevices(),
    readyPlayerCount: countReadyDevices(),
    spectatorCount: countSpectators(),
    playerCount: Math.max(minimumPlayers, Math.min(maximumPlayers, game.players.length)),
    alivePlayerCount,
    winnerId: game.winnerId,
    gameStarted: game.gameStarted,
    gameOver: game.gameOver,
    replayVoteCount: game.replayVoteCount,
    postGameRemainingSeconds: game.gameOver && game.postGameDeadline > 0
      ? Math.max(0, Math.ceil((game.postGameDeadline - Date.now()) / 1000))
      : 0,
    startCountdownSeconds: (!game.gameStarted && !game.gameOver && game.startDeadline > 0)
      ? Math.max(0, Math.ceil((game.startDeadline - Date.now()) / 1000))
      : 0,
    ballX: round(game.ballX),
    ballY: round(game.ballY),
    ballDirX: round(game.ballDirX),
    ballDirY: round(game.ballDirY),
    ballSpeed: round(ballSpeed * game.ballSpeedMul),
    ballDeadly: game.ballDeadly,
    raceActive: game.race.active,
    raceWinnerId: game.race.winnerId,
    raceWinnerName: game.race.winnerName,
    raceRemainingMs: game.race.active ? Math.max(0, game.race.deadline - Date.now()) : 0,
    players: game.players.map((player) => {
      const owner = clientForPlayerId(player.id);
      return {
        id: player.id,
        alive: player.alive,
        lives: player.lives,
        points: player.gamePoints || 0,
        paddleAngle: round(player.paddleAngle),
        input: round(player.input || 0),
        name: full && owner ? clientLabel(owner) : "",
        color: full && owner ? owner.color : ""
      };
    })
  };

  snapshot.chat = lobbyChat;

  if (full) {
    snapshot.status = game.status;
    snapshot.devices = buildDeviceList();
    snapshot.lobbyDevices = buildLobbyDeviceList();
  }

  return snapshot;
}

function metaSignature() {
  const devs = buildDeviceList()
    .map((d) => d.playerId + ":" + d.name + ":" + d.ready + ":" + d.color + ":" + d.lives + ":" + d.points)
    .join("|");
  const lobby = buildLobbyDeviceList()
    .map((d) => d.name + ":" + d.color + ":" + d.spectator)
    .join("|");
  const identities = game.players
    .map((p) => {
      const owner = clientForPlayerId(p.id);
      return p.id + ":" + (owner ? clientLabel(owner) : "") + ":" + (owner ? owner.color : "");
    })
    .join("|");
  const chat = lobbyChat.length > 0 ? lobbyChat[lobbyChat.length - 1].id : 0;
  return devs + "#" + lobby + "#" + identities + "#" + game.status
    + "#" + game.lobbyOpen + game.gameStarted + game.gameOver + game.winnerId + "#" + chat;
}

function clientForPlayerId(playerId) {
  for (const client of clientsByDevice.values()) {
    if (client.playerId === playerId) {
      return client;
    }
  }
  return null;
}

function buildDeviceList() {
  return Array.from(clientsByDevice.values())
    .filter((client) => client.ready && client.playerId > 0)
    .sort((a, b) => a.playerId - b.playerId)
    .map((client) => {
      const player = game.players[client.playerId - 1];
      return {
        playerId: client.playerId,
        name: clientLabel(client),
        ready: client.ready,
        spectator: false,
        color: client.color,
        lives: player ? player.lives : 0,
        points: player ? player.gamePoints || 0 : 0
      };
    });
}

function buildLobbyDeviceList() {
  return Array.from(clientsByDevice.values())
    .filter((client) => !client.ready || client.playerId <= 0)
    .sort((a, b) => a.id - b.id)
    .map((client) => ({
      playerId: 0,
      name: clientLabel(client),
      ready: false,
      spectator: !!client.spectator,
      color: client.color
    }));
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

function angleToDirection(angle) {
  const radians = angle * Math.PI / 180;
  return { x: Math.cos(radians), y: Math.sin(radians) };
}

function directionToAngle(x, y) {
  const angle = Math.atan2(y, x) * 180 / Math.PI;
  return angle < 0 ? angle + 360 : angle;
}

function clampPaddleAngle(angle, startAngle, endAngle) {
  const center = lerpAngle(startAngle, endAngle, 0.5);
  const sectorHalfSize = Math.abs(deltaAngle(startAngle, endAngle)) * 0.5;
  const allowedHalfSize = Math.max(0, sectorHalfSize - paddleArcDegrees * 0.5);
  const delta = clamp(deltaAngle(center, angle), -allowedHalfSize, allowedHalfSize);
  return center + delta;
}

function lerpAngle(a, b, t) {
  return a + deltaAngle(a, b) * t;
}

function deltaAngle(current, target) {
  let delta = repeat((target - current), 360);
  if (delta > 180) {
    delta -= 360;
  }
  return delta;
}

function repeat(value, length) {
  return clamp(value - Math.floor(value / length) * length, 0, length);
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function lerp(a, b, t) {
  return a + (b - a) * t;
}

function round(value) {
  return Math.round(value * 1000) / 1000;
}

assignLobbyPlayers();
setInterval(tick, 1000 / 60);
setInterval(broadcastSnapshot, 1000 / 30);
setInterval(flushScores, 5000);
