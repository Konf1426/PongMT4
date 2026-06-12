const circleMath = require("./circle-math");

function createSnapshotBuilder(options) {
  const {
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
    pointsForPlayer
  } = options;

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
      ballX: circleMath.round(game.ballX),
      ballY: circleMath.round(game.ballY),
      ballDirX: circleMath.round(game.ballDirX),
      ballDirY: circleMath.round(game.ballDirY),
      ballSpeed: circleMath.round(ballSpeed * game.ballSpeedMul),
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
          points: pointsForPlayer(player),
          paddleAngle: circleMath.round(player.paddleAngle),
          input: circleMath.round(player.input || 0),
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
      .map((player) => {
        const owner = clientForPlayerId(player.id);
        return player.id + ":" + (owner ? clientLabel(owner) : "") + ":" + (owner ? owner.color : "");
      })
      .join("|");
    const chat = lobbyChat.length > 0 ? lobbyChat[lobbyChat.length - 1].id : 0;
    return devs + "#" + lobby + "#" + identities + "#" + game.status
      + "#" + game.lobbyOpen + game.gameStarted + game.gameOver + game.winnerId + "#" + chat;
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

  return {
    buildDeviceList,
    buildLobbyDeviceList,
    buildSnapshot,
    metaSignature
  };
}

module.exports = { createSnapshotBuilder };
