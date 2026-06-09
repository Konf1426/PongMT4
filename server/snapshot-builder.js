const circleMath = require("./circle-math");

function createSnapshotBuilder(options) {
  const {
    clientsByDevice,
    clientForPlayerId,
    clientLabel,
    countInGameDevices,
    countReadyDevices,
    game,
    maximumPlayers,
    minimumPlayers,
    pointsForPlayer,
    scoreStore
  } = options;

  function buildSnapshot(client, full = true) {
    const alivePlayerCount = game.players.filter((player) => player.alive).length;
    const snapshot = {
      type: "state",
      localPlayerId: client ? client.playerId : 0,
      lobbyOpen: game.lobbyOpen,
      connectedPlayerCount: countInGameDevices(),
      readyPlayerCount: countReadyDevices(),
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
      players: game.players.map((player) => {
        const owner = clientForPlayerId(player.id);
        return {
          id: player.id,
          alive: player.alive,
          lives: player.lives,
          points: pointsForPlayer(player),
          paddleAngle: circleMath.round(player.paddleAngle),
          name: full && owner ? clientLabel(owner) : "",
          color: full && owner ? owner.color : ""
        };
      })
    };

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
      .map((d) => d.name + ":" + d.color)
      .join("|");
    const identities = game.players
      .map((player) => {
        const owner = clientForPlayerId(player.id);
        return player.id + ":" + (owner ? clientLabel(owner) : "") + ":" + (owner ? owner.color : "");
      })
      .join("|");
    return devs + "#" + lobby + "#" + identities + "#" + game.status
      + "#" + game.lobbyOpen + game.gameStarted + game.gameOver + game.winnerId;
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
          color: client.color,
          lives: player ? player.lives : 0,
          points: scoreStore.pointsForDevice(client.deviceId)
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
