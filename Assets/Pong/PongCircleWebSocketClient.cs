using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class PongCircleWebSocketClient : MonoBehaviour
{
    public PongCircleGame CircleGame;
    public string WebSocketUrl = "";
    public bool AutoConnect = true;
    public float InputSendRate = 30;

    public bool IsConnected {
      get {
        return connected;
      }
    }

    public int LocalPlayerId {
      get {
        return localPlayerId;
      }
    }

    public int ConnectedPlayerCount {
      get {
        return connectedPlayerCount;
      }
    }

    public int ReadyPlayerCount {
      get {
        return readyPlayerCount;
      }
    }

    public bool LobbyOpen {
      get {
        return lobbyOpen;
      }
    }

    public string LastStatus {
      get {
        return lastStatus;
      }
    }

    public PongCircleNetworkDeviceState[] Devices {
      get {
        return devices;
      }
    }

    public PongCircleNetworkDeviceState[] LobbyDevices {
      get {
        return lobbyDevices;
      }
    }

    public int ReplayVoteCount {
      get {
        return replayVoteCount;
      }
    }

    public int PostGameRemainingSeconds {
      get {
        return postGameRemainingSeconds;
      }
    }

    bool connected;
    bool connecting;
    bool lobbyOpen;
    int localPlayerId;
    int connectedPlayerCount;
    int readyPlayerCount;
    int replayVoteCount;
    int postGameRemainingSeconds;
    PongCircleNetworkDeviceState[] devices = new PongCircleNetworkDeviceState[0];
    PongCircleNetworkDeviceState[] lobbyDevices = new PongCircleNetworkDeviceState[0];
    float nextInputSendTime;
    float onScreenDirection;
    float onScreenDirectionTime;
    float nextJoinRetryTime;
    float joinRetryEndTime;
    float nextReconnectTime;
    string lastStatus = "Offline";
    bool joinWhenConnected;
    bool disconnecting;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void PongWsConnect(string url, string gameObjectName);

    [DllImport("__Internal")]
    static extern void PongWsSend(string message);

    [DllImport("__Internal")]
    static extern void PongWsClose();
#endif

    void Awake() {
      EnsureCircleGame();
    }

    void Start() {
      EnsureCircleGame();

      if (AutoConnect) {
        Connect();
      }
    }

    void Update() {
      ReconnectIfNeeded();
      RetryJoinIfNeeded();

      if (!connected) {
        return;
      }

      if (localPlayerId <= 0) {
        return;
      }

      if (Time.time < nextInputSendTime) {
        return;
      }

      nextInputSendTime = Time.time + (1 / Mathf.Max(1, InputSendRate));
      SendInput(ReadLocalDirection());
    }

    void OnDisable() {
      Disconnect();
    }

    public void Connect() {
      EnsureCircleGame();

      if (connected || connecting) {
        return;
      }

      disconnecting = false;
      connecting = true;

#if UNITY_WEBGL && !UNITY_EDITOR
      if (CircleGame != null) {
        CircleGame.SetNetworkControlled(true);
      }

      lastStatus = string.IsNullOrEmpty(WebSocketUrl) ? "Connecting to page WebSocket" : "Connecting to " + WebSocketUrl;
      PongWsConnect(WebSocketUrl, gameObject.name);
#else
      lastStatus = "WebSocket actif uniquement dans le build WebGL";
      connecting = false;
      Debug.Log(lastStatus);
#endif
    }

    public void Disconnect() {
      disconnecting = true;
#if UNITY_WEBGL && !UNITY_EDITOR
      PongWsClose();
#endif
      connected = false;
      connecting = false;
      lastStatus = "Disconnected";
    }

    public void OnWebSocketOpen(string unused) {
      connected = true;
      connecting = false;
      disconnecting = false;
      lastStatus = string.IsNullOrEmpty(unused) ? "Connected" : "Connected: " + unused;
      if (joinWhenConnected) {
        joinWhenConnected = false;
        SendStartGame();
      }
    }

    public void OnWebSocketStatus(string status) {
      lastStatus = status;
    }

    public void OnWebSocketClose(string reason) {
      connected = false;
      connecting = false;
      localPlayerId = 0;
      connectedPlayerCount = 0;
      readyPlayerCount = 0;
      replayVoteCount = 0;
      postGameRemainingSeconds = 0;
      devices = new PongCircleNetworkDeviceState[0];
      lobbyDevices = new PongCircleNetworkDeviceState[0];
      lastStatus = string.IsNullOrEmpty(reason) ? "Disconnected" : "Disconnected: " + reason;
      if (CircleGame != null) {
        CircleGame.EnterLobby();
      }
      if (AutoConnect && !disconnecting) {
        nextReconnectTime = Time.unscaledTime + 1.5f;
      }
    }

    public void OnWebSocketError(string error) {
      connected = false;
      connecting = false;
      lastStatus = "WebSocket error: " + error;
      if (AutoConnect && !disconnecting) {
        nextReconnectTime = Time.unscaledTime + 1.5f;
      }
      Debug.LogWarning(lastStatus);
    }

    public void OnWebSocketMessage(string message) {
      if (string.IsNullOrEmpty(message)) {
        return;
      }

      if (message.Contains("\"type\":\"welcome\"")) {
        PongCircleWelcome welcome = JsonUtility.FromJson<PongCircleWelcome>(message);
        localPlayerId = welcome.playerId;
        lastStatus = localPlayerId > 0 ? "Connected as player " + localPlayerId : "Connected to lobby";
        if (localPlayerId > 0) {
          joinWhenConnected = false;
          joinRetryEndTime = 0;
        }
        return;
      }

      PongCircleNetworkSnapshot snapshot = JsonUtility.FromJson<PongCircleNetworkSnapshot>(message);
      if (snapshot == null || snapshot.type != "state") {
        return;
      }

      if (snapshot.localPlayerId != localPlayerId) {
        localPlayerId = snapshot.localPlayerId;
        if (localPlayerId > 0) {
          lastStatus = "Connected as player " + localPlayerId;
          joinWhenConnected = false;
          joinRetryEndTime = 0;
        } else if (!connecting && connected) {
          lastStatus = "Connected to lobby";
        }
      }

      lobbyOpen = snapshot.lobbyOpen;
      connectedPlayerCount = snapshot.connectedPlayerCount;
      readyPlayerCount = snapshot.readyPlayerCount;
      replayVoteCount = snapshot.replayVoteCount;
      postGameRemainingSeconds = snapshot.postGameRemainingSeconds;
      devices = snapshot.devices ?? new PongCircleNetworkDeviceState[0];
      lobbyDevices = snapshot.lobbyDevices ?? new PongCircleNetworkDeviceState[0];

      EnsureCircleGame();
      if (CircleGame != null) {
        CircleGame.ApplyNetworkSnapshot(snapshot);
      }
    }

    public void SendStartGame() {
      joinRetryEndTime = Time.unscaledTime + 10f;
      nextJoinRetryTime = 0;
      lastStatus = "Join request sent";
#if UNITY_WEBGL && !UNITY_EDITOR
      if (!connected) {
        joinWhenConnected = true;
        Connect();
        return;
      }

      SendJoinMessages();
#else
      if (CircleGame != null) {
        CircleGame.StartGame();
      }
#endif
    }

    void ReconnectIfNeeded() {
      if (!AutoConnect || connected || connecting || disconnecting) {
        return;
      }

      if (nextReconnectTime <= 0 || Time.unscaledTime < nextReconnectTime) {
        return;
      }

      nextReconnectTime = Time.unscaledTime + 2f;
      Connect();
    }

    void RetryJoinIfNeeded() {
      if (!joinWhenConnected && joinRetryEndTime <= 0) {
        return;
      }

      if (localPlayerId > 0) {
        joinWhenConnected = false;
        joinRetryEndTime = 0;
        return;
      }

      if (Time.unscaledTime > joinRetryEndTime) {
        joinWhenConnected = false;
        joinRetryEndTime = 0;
        return;
      }

      if (Time.unscaledTime < nextJoinRetryTime) {
        return;
      }

      nextJoinRetryTime = Time.unscaledTime + 0.5f;

#if UNITY_WEBGL && !UNITY_EDITOR
      if (!connected) {
        joinWhenConnected = true;
        Connect();
        return;
      }

      SendJoinMessages();
#endif
    }

    void SendJoinMessages() {
#if UNITY_WEBGL && !UNITY_EDITOR
      PongWsSend("{\"type\":\"join\"}");
      PongWsSend("{\"type\":\"start\"}");
#endif
    }

    public void SendRestartLobby() {
#if UNITY_WEBGL && !UNITY_EDITOR
      if (!connected) {
        Connect();
        return;
      }

      PongWsSend("{\"type\":\"restart\"}");
#else
      if (CircleGame != null) {
        CircleGame.Replay();
      }
#endif
    }

    public void SendReplayVote() {
#if UNITY_WEBGL && !UNITY_EDITOR
      if (!connected) {
        Connect();
        return;
      }

      PongWsSend("{\"type\":\"replay\"}");
#else
      if (CircleGame != null) {
        CircleGame.Replay();
      }
#endif
    }

    public void SendReturnLobby() {
#if UNITY_WEBGL && !UNITY_EDITOR
      if (!connected) {
        Connect();
        return;
      }

      PongWsSend("{\"type\":\"lobby\"}");
#else
      if (CircleGame != null) {
        CircleGame.Replay();
      }
#endif
    }

    public void SetOnScreenDirection(float direction) {
      onScreenDirection = Mathf.Clamp(direction, -1, 1);
      onScreenDirectionTime = Time.unscaledTime;
    }

    public bool ShouldShowMobileControls() {
      return Application.isMobilePlatform || Input.touchSupported || Touchscreen.current != null;
    }

    void SendInput(float direction) {
      string message = "{\"type\":\"input\",\"direction\":" + direction.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}";
#if UNITY_WEBGL && !UNITY_EDITOR
      PongWsSend(message);
#endif
    }

    void EnsureCircleGame() {
      if (CircleGame != null) {
        return;
      }

      CircleGame = GetComponent<PongCircleGame>();
      if (CircleGame == null) {
        CircleGame = GameObject.FindFirstObjectByType<PongCircleGame>();
      }
    }

    float ReadLocalDirection() {
      if (Time.unscaledTime - onScreenDirectionTime < 0.2f) {
        return onScreenDirection;
      }

      Keyboard keyboard = Keyboard.current;
      if (keyboard == null) {
        return 0;
      }

      float arrowDirection = ReadPair(keyboard.upArrowKey, null, keyboard.downArrowKey);
      if (Mathf.Abs(arrowDirection) > 0) {
        return arrowDirection;
      }

      return ReadPair(keyboard.zKey, keyboard.wKey, keyboard.sKey);
    }

    float ReadPair(KeyControl positive, KeyControl alternativePositive, KeyControl negative) {
      float direction = 0;
      if ((positive != null && positive.isPressed) || (alternativePositive != null && alternativePositive.isPressed)) {
        direction += 1;
      }

      if (negative != null && negative.isPressed) {
        direction -= 1;
      }

      return Mathf.Clamp(direction, -1, 1);
    }

    [System.Serializable]
    class PongCircleWelcome
    {
      public string type;
      public int playerId;
    }
}
