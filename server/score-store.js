const fs = require("fs");

function createScoreStore(filePath) {
  const scores = loadScores(filePath);
  let dirty = false;

  function getEntry(deviceId, name) {
    let entry = scores.get(deviceId);
    if (!entry) {
      entry = { name: name || "", bestScore: 0 };
      scores.set(deviceId, entry);
    }
    if (name) entry.name = name;
    return entry;
  }

  function updateBestScore(deviceId, name, score) {
    const entry = getEntry(deviceId, name);
    if (score > entry.bestScore) {
      entry.bestScore = score;
      dirty = true;
    }
  }

  function flush() {
    if (!dirty) return;
    dirty = false;
    const data = Array.from(scores.entries())
      .map(([deviceId, entry]) => ({ deviceId, name: entry.name, bestScore: entry.bestScore }))
      .filter((e) => e.bestScore > 0)
      .sort((a, b) => b.bestScore - a.bestScore)
      .slice(0, 10);
    fs.writeFile(filePath, JSON.stringify(data, null, 2), () => {});
  }

  function buildScoreboard() {
    return Array.from(scores.values())
      .filter((entry) => entry.bestScore > 0)
      .sort((a, b) => b.bestScore - a.bestScore)
      .slice(0, 10)
      .map((entry) => ({
        name: entry.name || "Anonyme",
        bestScore: entry.bestScore
      }));
  }

  return { updateBestScore, buildScoreboard, flush, getEntry };
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
            bestScore: entry.bestScore || 0
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
