mergeInto(LibraryManager.library, {
  PongWsConnect: function (urlPtr, gameObjectNamePtr) {
    var url = UTF8ToString(urlPtr);
    var gameObjectName = UTF8ToString(gameObjectNamePtr);

    if (window.__pongCircleSocket) {
      window.__pongCircleSocket.close();
    }

    var protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
    var candidates = [];
    if (url) {
      candidates.push(url);
    } else {
      candidates.push(protocol + "//" + window.location.host + "/ws");
      if (window.location.port !== "8080") {
        candidates.push(protocol + "//" + window.location.hostname + ":8080/ws");
      }
    }

    function buildHelloMessage() {
      var storageKey = "pongCircleDeviceId";
      var deviceId = "";
      try {
        deviceId = window.localStorage.getItem(storageKey);
        if (!deviceId) {
          var randomPart = "";
          if (window.crypto && window.crypto.getRandomValues) {
            var bytes = new Uint32Array(4);
            window.crypto.getRandomValues(bytes);
            randomPart = Array.prototype.map.call(bytes, function (value) {
              return value.toString(16);
            }).join("");
          } else {
            randomPart = Math.random().toString(16).slice(2) + Date.now().toString(16);
          }

          deviceId = "device-" + randomPart;
          window.localStorage.setItem(storageKey, deviceId);
        }
      } catch (error) {
        deviceId = "volatile-" + Math.random().toString(16).slice(2) + Date.now().toString(16);
      }

      var deviceName = "Browser";
      if (/iPhone|iPad|iPod/i.test(navigator.userAgent)) {
        deviceName = "iPhone";
      } else if (/Android/i.test(navigator.userAgent)) {
        deviceName = "Android";
      } else if (/Macintosh|Mac OS X/i.test(navigator.userAgent)) {
        deviceName = "Mac";
      } else if (/Windows/i.test(navigator.userAgent)) {
        deviceName = "Windows PC";
      } else if (/Linux/i.test(navigator.userAgent)) {
        deviceName = "Linux PC";
      }

      return JSON.stringify({ type: "hello", deviceId: deviceId, deviceName: deviceName });
    }

    function connectCandidate(index) {
      if (index >= candidates.length) {
        SendMessage(gameObjectName, "OnWebSocketClose", "all endpoints failed");
        return;
      }

      var candidate = candidates[index];
      var socket = new WebSocket(candidate);
      var opened = false;
      window.__pongCircleSocket = socket;
      SendMessage(gameObjectName, "OnWebSocketStatus", "Trying " + candidate);

      socket.onopen = function () {
        opened = true;
        socket.send(buildHelloMessage());
        SendMessage(gameObjectName, "OnWebSocketOpen", candidate);
      };

      socket.onmessage = function (event) {
        SendMessage(gameObjectName, "OnWebSocketMessage", event.data);
      };

      socket.onerror = function () {
        if (opened) {
          SendMessage(gameObjectName, "OnWebSocketError", "Browser WebSocket error on " + candidate);
        }
      };

      socket.onclose = function (event) {
        if (!opened && index + 1 < candidates.length) {
          connectCandidate(index + 1);
          return;
        }

        var reason = event.reason || ("code " + event.code + " on " + candidate);
        SendMessage(gameObjectName, "OnWebSocketClose", reason);
      };
    }

    connectCandidate(0);
  },

  PongWsSend: function (messagePtr) {
    var socket = window.__pongCircleSocket;
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      return;
    }

    socket.send(UTF8ToString(messagePtr));
  },

  PongWsClose: function () {
    if (window.__pongCircleSocket) {
      window.__pongCircleSocket.close();
      window.__pongCircleSocket = null;
    }
  }
});
