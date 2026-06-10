function createClientRegistry(options) {
  const clientTimeoutMs = options.clientTimeoutMs;
  const maximumPlayers = options.maximumPlayers;
  const onChange = options.onChange || (() => {});

  const clientsByDevice = new Map();
  const clientsByAddress = new Map();
  let nextClientId = 1;

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
        lastSeen: Date.now()
      };
      clientsByDevice.set(deviceId, client);
    }

    client.deviceName = getUniqueDeviceName(deviceName || client.deviceName, client);
    client.address = remote.address;
    client.port = remote.port;
    client.lastSeen = Date.now();
    clientsByAddress.set(addressKey, client);
    onChange();
    return client;
  }

  function sanitizeId(value) {
    return String(value || "").replace(/[^\w.-]/g, "").slice(0, 80);
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

  function getConnectedClients() {
    return Array.from(clientsByDevice.values())
      .filter((client) => Date.now() - client.lastSeen <= clientTimeoutMs)
      .sort((a, b) => a.id - b.id);
  }

  function getReadyClients() {
    return getConnectedClients().filter((client) => client.ready && !client.spectator).slice(0, maximumPlayers);
  }

  function countSpectators() {
    return getConnectedClients().filter((client) => client.spectator).length;
  }

  function cleanupClients() {
    const now = Date.now();
    for (const [deviceId, client] of clientsByDevice.entries()) {
      if (now - client.lastSeen > clientTimeoutMs) {
        clientsByDevice.delete(deviceId);
      }
    }
  }

  function clientForPlayerId(playerId) {
    for (const client of clientsByDevice.values()) {
      if (client.playerId === playerId) {
        return client;
      }
    }
    return null;
  }

  return {
    clientForPlayerId,
    clientLabel,
    clientsByAddress,
    clientsByDevice,
    cleanupClients,
    getConnectedClients,
    getReadyClients,
    countSpectators,
    isColorTakenByOther,
    registerClient,
    sanitizeColor,
    sanitizeDeviceName,
    sanitizeId
  };
}

module.exports = { createClientRegistry };
