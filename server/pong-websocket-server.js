const fs = require("fs");
const http = require("http");
const path = require("path");
const { WebSocket, WebSocketServer } = require("ws");

const PORT = Number(process.env.PORT || 8080);
const PUBLIC_DIR = path.resolve(__dirname, "..", "build-web");
const SERVER_VERSION = "state-reconcile-2026-06-03-04";

const arenaRadius = 5;
const paddleArcDegrees = 22;
const paddleAngularSpeed = 120;
const ballSpeed = 3.5;
const paddleAimInfluence = 0.45;
const minimumPlayers = 2;
const maximumPlayers = 10;

const clients = new Map();
const clientsByDevice = new Map();
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
  winnerId: 0,
  status: "Waiting for someone to start a game"
};

let nextClientId = 1;
let lastTick = Date.now();

function sendStatic(req, res) {
  if (req.url.split("?")[0] === "/health") {
    reconcileGameState();
    res.writeHead(200, { "Content-Type": "application/json; charset=utf-8" });
    res.end(JSON.stringify({
      ok: true,
      version: SERVER_VERSION,
      connectedPlayerCount: game.connectedPlayerCount,
      readyPlayerCount: game.readyPlayerCount,
      playerCount: game.playerCount,
      lobbyOpen: game.lobbyOpen,
      gameStarted: game.gameStarted,
      gameOver: game.gameOver,
      winnerId: game.winnerId,
      status: game.status,
      devices: buildDeviceList(),
      lobbyDevices: buildLobbyDeviceList()
    }));
    return;
  }

  const urlPath = decodeURIComponent(req.url.split("?")[0]);
  const relativePath = urlPath === "/" ? "index.html" : urlPath.replace(/^\/+/, "");
  const filePath = path.resolve(PUBLIC_DIR, relativePath);

  if (!filePath.startsWith(PUBLIC_DIR)) {
    res.writeHead(403);
    res.end("Forbidden");
    return;
  }

  fs.readFile(filePath, (error, content) => {
    if (error) {
      res.writeHead(404);
      res.end("Not found");
      return;
    }

    res.writeHead(200, { "Content-Type": getContentType(filePath) });
    res.end(content);
  });
}

function getContentType(filePath) {
  switch (path.extname(filePath)) {
    case ".html": return "text/html; charset=utf-8";
    case ".js": return "application/javascript; charset=utf-8";
    case ".wasm": return "application/wasm";
    case ".data": return "application/octet-stream";
    case ".css": return "text/css; charset=utf-8";
    case ".png": return "image/png";
    case ".ico": return "image/x-icon";
    default: return "application/octet-stream";
  }
}

const server = http.createServer(sendStatic);
const wss = new WebSocketServer({ server, path: "/ws" });

wss.on("connection", (socket) => {
  const client = {
    id: nextClientId++,
    deviceId: "",
    deviceName: "",
    playerId: 0,
    ready: false,
    input: 0,
    lastSeen: Date.now(),
    socket
  };

  clients.set(socket, client);
  broadcastSnapshot();

  socket.on("message", (rawMessage) => {
    let message;
    try {
      message = JSON.parse(rawMessage.toString());
    } catch {
      return;
    }

    if (message.type === "hello") {
      registerDevice(client, String(message.deviceId || ""), String(message.deviceName || ""));
      return;
    }

    if (message.type === "input") {
      client.input = clamp(Number(message.direction) || 0, -1, 1);
      client.lastSeen = Date.now();
    }

    if (message.type === "start" || message.type === "join") {
      joinGame(client);
      broadcastSnapshot();
    }

    if (message.type === "restart") {
      resetToLobby(client);
      broadcastSnapshot();
    }

    if (message.type === "replay") {
      voteReplay(client);
      broadcastSnapshot();
    }

    if (message.type === "lobby") {
      returnToLobby();
      broadcastSnapshot();
    }
  });

  socket.on("close", () => {
    if (client.deviceId && clientsByDevice.get(client.deviceId) === client) {
      clientsByDevice.delete(client.deviceId);
    }

    clients.delete(socket);
    if (!game.gameStarted) {
      assignLobbyPlayers();
    } else {
      updateConnectedPlayerCount();
      updateStatus();
    }
    broadcastSnapshot();
  });
});

function registerDevice(client, deviceId, deviceName) {
  if (!deviceId) {
    deviceId = `volatile-${client.id}`;
  }

  const previousClient = clientsByDevice.get(deviceId);
  if (previousClient && previousClient !== client) {
    previousClient.socket.close(4000, "Another tab from this device joined");
    clients.delete(previousClient.socket);
  }

  client.deviceId = deviceId;
  client.deviceName = getUniqueDeviceName(deviceName, client);
  clientsByDevice.set(deviceId, client);

  if (!game.gameStarted) {
    assignLobbyPlayers();
  } else {
    updateConnectedPlayerCount();
    updateStatus();
  }

  client.socket.send(JSON.stringify({ type: "welcome", playerId: client.playerId }));
  broadcastSnapshot();
}

function getUniqueDeviceName(deviceName, currentClient) {
  const baseName = sanitizeDeviceName(deviceName) || "Device";
  let sameTypeCount = 0;

  for (const client of clients.values()) {
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

function joinGame(client) {
  if (!client.deviceId) {
    return;
  }

  if (game.gameOver) {
    client.socket.send(JSON.stringify({ type: "welcome", playerId: client.playerId }));
    return;
  }

  if (client.playerId <= 0 && getReadyClients().length >= maximumPlayers) {
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

function addReadyPlayerToRunningGame(client) {
  client.ready = true;
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

  client.socket.send(JSON.stringify({ type: "welcome", playerId: client.playerId }));

  for (const readyClient of activeClients) {
    if (readyClient.socket.readyState === WebSocket.OPEN) {
      readyClient.socket.send(JSON.stringify({ type: "welcome", playerId: readyClient.playerId }));
    }
  }
}

function resetToLobby(requestingClient) {
  for (const client of clients.values()) {
    client.ready = false;
    client.input = 0;
  }

  if (requestingClient && requestingClient.deviceId) {
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
  for (const client of clients.values()) {
    client.ready = false;
    client.input = 0;
    client.wantsReplay = false;
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
  const connectedClients = getConnectedClients();
  const readyClients = getReadyClients();

  for (const client of clients.values()) {
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

  for (const client of connectedClients) {
    if (client.socket.readyState === WebSocket.OPEN) {
      client.socket.send(JSON.stringify({ type: "welcome", playerId: client.playerId }));
    }
  }

  if (game.lobbyOpen && readyClients.length >= minimumPlayers) {
    beginMatch();
  }
}

function beginMatch() {
  game.lobbyOpen = true;
  game.gameStarted = true;
  game.gameOver = false;
  game.replayVoteCount = 0;
  game.postGameDeadline = 0;
  game.winnerId = 0;
  game.status = "Playing";

  for (const player of game.players) {
    player.alive = true;
    player.hasPaddleAngle = false;
  }

  for (const client of clients.values()) {
    client.wantsReplay = false;
  }

  redistributeAlivePlayers();
  resetBall();
  updateStatus();
}

function getConnectedClients() {
  return Array.from(clients.values())
    .filter((client) => client.deviceId)
    .sort((a, b) => a.id - b.id);
}

function getReadyClients() {
  return getConnectedClients().filter((client) => client.ready).slice(0, maximumPlayers);
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
      paddleAngle: 0,
      input: 0,
      sectorStartAngle: 0,
      sectorEndAngle: 0
    });
  }
}

function resetRound() {
  if (countReadyDevices() >= minimumPlayers) {
    beginMatch();
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

function voteReplay(client) {
  if (!game.gameOver || !client.deviceId || client.playerId <= 0) {
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

  reconcileGameState();
  updateInputs();
  updatePostGameTimeout();
  updatePaddles(deltaTime);
  updateBall(deltaTime);
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

    for (const client of clients.values()) {
      if (client.deviceId && !client.ready && client.playerId !== 0) {
        client.playerId = 0;
        changed = true;
      }
    }

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

  for (const client of clients.values()) {
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

  game.ballX += game.ballDirX * ballSpeed * deltaTime;
  game.ballY += game.ballDirY * ballSpeed * deltaTime;

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
  if (paddleDelta <= paddleArcDegrees * 0.5) {
    bounceOnPaddle(defender, angle);
  } else {
    eliminatePlayer(defender);
  }
}

function updateConnectedPlayerCount() {
  game.connectedPlayerCount = countInGameDevices();
  game.readyPlayerCount = countReadyDevices();
}

function countConnectedDevices() {
  let count = 0;
  for (const client of clients.values()) {
    if (client.deviceId) {
      count++;
    }
  }
  return count;
}

function countInGameDevices() {
  let count = 0;
  for (const client of clients.values()) {
    if (client.deviceId && client.ready && client.playerId > 0) {
      count++;
    }
  }
  return Math.min(count, maximumPlayers);
}

function countReadyDevices() {
  let count = 0;
  for (const client of clients.values()) {
    if (client.deviceId && client.ready) {
      count++;
    }
  }
  return Math.min(count, maximumPlayers);
}

function countReplayVotes() {
  let count = 0;
  for (const client of clients.values()) {
    if (client.deviceId && client.playerId > 0 && client.wantsReplay) {
      count++;
    }
  }
  return count;
}

function updateStatus() {
  updateConnectedPlayerCount();

  if (game.gameOver) {
    return;
  }

  if (game.gameStarted) {
    game.status = `Playing ${game.connectedPlayerCount}/${maximumPlayers} players`;
    return;
  }

  if (game.lobbyOpen) {
    game.status = `Lobby open: ${game.readyPlayerCount}/${minimumPlayers} ready, max ${maximumPlayers}`;
    return;
  }

  game.status = "Waiting for someone to start a game";
}

function bounceOnPaddle(defender, impactAngle) {
  const impactDirection = angleToDirection(impactAngle);
  const paddleDirection = angleToDirection(defender.paddleAngle);
  const dot = game.ballDirX * paddleDirection.x + game.ballDirY * paddleDirection.y;
  let reflectedX = game.ballDirX - 2 * dot * paddleDirection.x;
  let reflectedY = game.ballDirY - 2 * dot * paddleDirection.y;

  const offset = deltaAngle(defender.paddleAngle, impactAngle) / Math.max(1, paddleArcDegrees * 0.5);
  const tangent = { x: -paddleDirection.y, y: paddleDirection.x };
  let aimedX = reflectedX + tangent.x * offset * paddleAimInfluence;
  let aimedY = reflectedY + tangent.y * offset * paddleAimInfluence;
  let length = Math.hypot(aimedX, aimedY) || 1;
  aimedX /= length;
  aimedY /= length;

  const antiImpactDot = aimedX * -impactDirection.x + aimedY * -impactDirection.y;
  if (antiImpactDot < 0.15) {
    aimedX = lerp(aimedX, -impactDirection.x, 0.5);
    aimedY = lerp(aimedY, -impactDirection.y, 0.5);
    length = Math.hypot(aimedX, aimedY) || 1;
    aimedX /= length;
    aimedY /= length;
  }

  game.ballDirX = aimedX;
  game.ballDirY = aimedY;
  game.ballX = impactDirection.x * (arenaRadius - 0.12);
  game.ballY = impactDirection.y * (arenaRadius - 0.12);
}

function eliminatePlayer(player) {
  player.alive = false;
  const alivePlayers = game.players.filter((candidate) => candidate.alive);

  if (alivePlayers.length <= 1) {
    game.winnerId = alivePlayers[0] ? alivePlayers[0].id : 0;
    game.gameOver = true;
    game.gameStarted = false;
    game.replayVoteCount = 0;
    game.postGameDeadline = Date.now() + 30000;
    for (const client of clients.values()) {
      client.wantsReplay = false;
    }
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
}

function repairRunningPlayerAssignments() {
  if (!game.gameStarted || game.gameOver) {
    return;
  }

  const activeClients = getReadyClients();
  let changed = activeClients.length !== game.players.length
    || activeClients.some((client) => client.playerId <= 0);

  activeClients.forEach((client, index) => {
    const expectedPlayerId = index + 1;
    if (client.playerId !== expectedPlayerId) {
      client.playerId = expectedPlayerId;
      changed = true;
    }
  });

  for (const client of clients.values()) {
    if (client.ready && !activeClients.includes(client) && client.playerId !== 0) {
      client.playerId = 0;
      changed = true;
    }
  }

  if (!changed) {
    return;
  }

  rebuildPlayersForReadyClients(activeClients, true);
  redistributeAlivePlayers();
  updateStatus();

  for (const client of clients.values()) {
    if (client.socket.readyState === WebSocket.OPEN) {
      client.socket.send(JSON.stringify({ type: "welcome", playerId: client.playerId }));
    }
  }
}

function buildSnapshot(client) {
  reconcileGameState();
  repairRunningPlayerAssignments();
  const alivePlayerCount = game.players.filter((player) => player.alive).length;
  const inGamePlayerCount = countInGameDevices();
  return {
    type: "state",
    localPlayerId: client ? client.playerId : 0,
    lobbyOpen: game.lobbyOpen,
    connectedPlayerCount: inGamePlayerCount,
    readyPlayerCount: game.readyPlayerCount,
    playerCount: Math.max(minimumPlayers, Math.min(maximumPlayers, game.players.length)),
    alivePlayerCount,
    winnerId: game.winnerId,
    gameStarted: game.gameStarted,
    gameOver: game.gameOver,
    replayVoteCount: game.replayVoteCount,
    postGameRemainingSeconds: game.gameOver && game.postGameDeadline > 0
      ? Math.max(0, Math.ceil((game.postGameDeadline - Date.now()) / 1000))
      : 0,
    status: game.status,
    ballX: round(game.ballX),
    ballY: round(game.ballY),
    ballDirX: round(game.ballDirX),
    ballDirY: round(game.ballDirY),
    devices: buildDeviceList(),
    lobbyDevices: buildLobbyDeviceList(),
    players: game.players.map((player) => ({
      id: player.id,
      alive: player.alive,
      paddleAngle: round(player.paddleAngle)
    }))
  };
}

function buildDeviceList() {
  return Array.from(clients.values())
    .filter((client) => client.deviceId && client.ready && client.playerId > 0)
    .sort((a, b) => {
      if (a.playerId !== b.playerId) {
        return a.playerId - b.playerId;
      }

      return a.id - b.id;
    })
    .map((client) => ({
      playerId: client.playerId,
      name: client.deviceName || "Device",
      ready: client.ready
    }));
}

function buildLobbyDeviceList() {
  return Array.from(clients.values())
    .filter((client) => client.deviceId && (!client.ready || client.playerId <= 0))
    .sort((a, b) => a.id - b.id)
    .map((client) => ({
      playerId: 0,
      name: client.deviceName || "Device",
      ready: false
    }));
}

function broadcastSnapshot() {
  for (const client of clients.values()) {
    if (client.socket.readyState === WebSocket.OPEN) {
      const message = JSON.stringify(buildSnapshot(client));
      client.socket.send(message);
    }
  }
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

server.listen(PORT, () => {
  console.log(`Pong WebSocket server listening on http://0.0.0.0:${PORT}`);
  console.log(`Serving ${PUBLIC_DIR}`);
});
