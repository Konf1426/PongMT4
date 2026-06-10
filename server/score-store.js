const fs = require("fs");

function createScoreStore(filePath) {
  const scores = loadScores(filePath);
  let dirty = false;

  function getEntry(deviceId, name) {
    let entry = scores.get(deviceId);
    if (!entry) {
      entry = { name: name || "", points: 0, wins: 0, games: 0 };
      scores.set(deviceId, entry);
    }
    if (name) {
      entry.name = name;
    }
    return entry;
  }

  function award(deviceId, name, amount) {
    getEntry(deviceId, name).points += amount;
    dirty = true;
  }

  function recordGame(deviceId, name) {
    getEntry(deviceId, name).games += 1;
    dirty = true;
  }

  function recordWin(deviceId, name) {
    getEntry(deviceId, name).wins += 1;
    dirty = true;
  }

  function pointsForDevice(deviceId) {
    const entry = scores.get(deviceId);
    return entry ? entry.points : 0;
  }

  function flush() {
    if (!dirty) {
      return;
    }
    dirty = false;
    const data = Array.from(scores.entries()).map(([deviceId, entry]) => ({
      deviceId,
      name: entry.name,
      points: entry.points,
      wins: entry.wins,
      games: entry.games
    }));
    fs.writeFile(filePath, JSON.stringify(data, null, 2), () => {});
  }

  function buildScoreboard() {
    return Array.from(scores.values())
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

  return {
    award,
    buildScoreboard,
    flush,
    getEntry,
    pointsForDevice,
    recordGame,
    recordWin
  };
}

function loadScores(filePath) {
  try {
    const data = JSON.parse(fs.readFileSync(filePath, "utf8"));
    const scores = new Map();
    if (Array.isArray(data)) {
      for (const entry of data) {
        if (entry && entry.deviceId) {
          scores.set(entry.deviceId, {
            name: entry.name || "",
            points: entry.points || 0,
            wins: entry.wins || 0,
            games: entry.games || 0
          });
        }
      }
    }
    return scores;
  } catch {
    return new Map();
  }
}

module.exports = { createScoreStore };
