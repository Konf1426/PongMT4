using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

public class PongCircleUdpClient : MonoBehaviour
{
    public PongCircleGame CircleGame;
#if UNITY_EDITOR
    public string ServerHost = "127.0.0.1";
#else
    public string ServerHost = "pong.becop.fr";
#endif
    public int ServerPort = 41234;
    public bool AutoConnect = true;
    public bool UseRemoteInEditor = false;
    public float InputSendRate = 30;
    public bool DebugNetworkLogging = false;

    public bool IsConnected {
      get { return connected; }
    }

    public int LocalPlayerId {
      get { return localPlayerId; }
    }

    public bool IsSpectator {
      get { return localIsSpectator; }
    }

    public int ConnectedPlayerCount {
      get { return connectedPlayerCount; }
    }

    public int ReadyPlayerCount {
      get { return readyPlayerCount; }
    }

    public int SpectatorCount {
      get { return spectatorCount; }
    }

    public bool LobbyOpen {
      get { return lobbyOpen; }
    }

    public string LastStatus {
      get { return lastStatus; }
    }

    public PongCircleNetworkDeviceState[] Devices {
      get { return devices; }
    }

    public PongCircleNetworkDeviceState[] LobbyDevices {
      get { return lobbyDevices; }
    }

    public int ReplayVoteCount {
      get { return replayVoteCount; }
    }

    public int PostGameRemainingSeconds {
      get { return postGameRemainingSeconds; }
    }

    public int StartCountdownSeconds {
      get { return startCountdownSeconds; }
    }

    UdpClient udp;
    IPEndPoint serverEndPoint;
    Thread receiveThread;
    // File producteur/consommateur : le thread réseau y dépose les datagrammes reçus,
    // le thread principal Unity les retire. Le verrou protège l'accès concurrent
    // (l'API Unity n'étant pas thread-safe, on ne la touche que sur le thread principal).
    readonly Queue<string> receivedMessages = new Queue<string>();
    readonly object receivedMessagesLock = new object();

    bool connected;
    bool stopping;
    bool lobbyOpen;
    bool localIsSpectator;
    int localPlayerId;
    int connectedPlayerCount;
    int readyPlayerCount;
    int spectatorCount;
    int replayVoteCount;
    int postGameRemainingSeconds;
    int startCountdownSeconds;
    int sequence;
    PongCircleNetworkDeviceState[] devices = new PongCircleNetworkDeviceState[0];
    PongCircleNetworkDeviceState[] lobbyDevices = new PongCircleNetworkDeviceState[0];
    string deviceId;
    string deviceName;
    string chosenDisplayName = "";
    string chosenColorHex = "";
    string lastStatus = "UDP offline";
    float nextHelloTime;
    float nextInputSendTime;
    float nextJoinRetryTime;
    float joinRetryEndTime;
    float lastSnapshotTime;
    float onScreenDirection;
    float onScreenDirectionTime;
    float lastSentDirection = 999f;
    int redundantSendsLeft;
    float smashCooldownUntil;

    void Awake() {
      Application.runInBackground = true;
      EnsureCircleGame();
      ApplyEditorDefaultHost();
      ApplyLauncherEnvironment();
      deviceId = LoadOrCreateDeviceId();
      deviceName = DetectDeviceName();
    }

    void Start() {
      if (AutoConnect) {
        Connect();
      }
    }

    void Update() {
      DrainMessages();

      if (!connected) {
        return;
      }

      if (Time.unscaledTime >= nextHelloTime) {
        nextHelloTime = Time.unscaledTime + 1f;
        SendHello();
      }

      RetryJoinIfNeeded();

      if (joinRetryEndTime > 0 && localPlayerId <= 0 && Time.unscaledTime - lastSnapshotTime > 3f) {
        lastStatus = "UDP waiting for server response";
      }

      if (localPlayerId <= 0) {
        return;
      }

      if (Keyboard.current != null && (Keyboard.current.leftShiftKey.wasPressedThisFrame || Keyboard.current.rightShiftKey.wasPressedThisFrame)) {
        SendMessage(PongCircleUdpProtocol.Simple("race"));
      }

      if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) {
        if (Time.unscaledTime >= smashCooldownUntil) {
          SendSmash();
          smashCooldownUntil = Time.unscaledTime + 1.5f;
        }
      }

      float direction = ReadLocalDirection();

      EnsureCircleGame();
      if (CircleGame != null) {
        CircleGame.SetLocalPredictedInput(direction);
      }

      float spacing = 1f / Mathf.Max(1f, InputSendRate);
      bool changed = Mathf.Abs(direction - lastSentDirection) > 0.01f;
      if (changed) {
        SendInput(direction);
        lastSentDirection = direction;
        redundantSendsLeft = 2;
        nextInputSendTime = Time.time + spacing;
      } else if (redundantSendsLeft > 0 && Time.time >= nextInputSendTime) {
        SendInput(direction);
        redundantSendsLeft--;
        nextInputSendTime = Time.time + spacing;
      }
    }

    void OnDisable() {
      Disconnect();
    }

    public void Connect() {
      if (connected) {
        return;
      }

      EnsureCircleGame();
      try {
        IPAddress[] addresses = Dns.GetHostAddresses(ServerHost);
        if (addresses.Length == 0) {
          lastStatus = "UDP DNS failed";
          return;
        }

        IPAddress serverAddress = null;
        for (int i = 0; i < addresses.Length; i++) {
          if (addresses[i].AddressFamily == AddressFamily.InterNetwork) {
            serverAddress = addresses[i];
            break;
          }
        }

        if (serverAddress == null) {
          lastStatus = "UDP DNS IPv4 failed";
          return;
        }

        serverEndPoint = new IPEndPoint(serverAddress, ServerPort);
        udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        stopping = false;
        connected = true;
        lastStatus = "UDP connected to " + ServerHost + ":" + ServerPort;
        LogNetwork("Connected endpoint " + serverEndPoint + " from " + udp.Client.LocalEndPoint);

        if (CircleGame != null) {
          CircleGame.SetNetworkControlled(true);
        }

        receiveThread = new Thread(ReceiveLoop);
        receiveThread.IsBackground = true;
        receiveThread.Start();
        SendHello();
      } catch (Exception exception) {
        connected = false;
        lastStatus = "UDP error: " + exception.Message;
        Debug.LogWarning(lastStatus);
      }
    }

    public void Disconnect() {
      stopping = true;
      connected = false;
      lastStatus = "UDP disconnected";

      try {
        if (udp != null) {
          udp.Close();
        }
      } catch {
      }

      udp = null;
    }

    public void SendStartGame() {
      localIsSpectator = false;
      joinRetryEndTime = Time.unscaledTime + 10f;
      nextJoinRetryTime = 0;
      lastSnapshotTime = Time.unscaledTime;
      lastStatus = "UDP join sent to " + ServerHost + ":" + ServerPort;
      SendJoin();
    }

    public void SendSpectateGame() {
      localIsSpectator = true;
      localPlayerId = 0;
      joinRetryEndTime = 0;
      nextJoinRetryTime = 0;
      lastStatus = "UDP spectator request sent";
      SendSpectate();
    }

    public void SendRestartLobby() {
      SendMessage(PongCircleUdpProtocol.Simple("restart"));
    }

    public void SendReplayVote() {
      SendMessage(PongCircleUdpProtocol.Simple("replay"));
    }

    public void SendReturnLobby() {
      SendMessage(PongCircleUdpProtocol.Simple("lobby"));
    }

    // Identité choisie par l'utilisateur, propagée au serveur
    public void SetIdentity(string name, string colorHex) {
      chosenDisplayName = name ?? "";
      chosenColorHex = (colorHex ?? "").Replace("#", "");
      if (connected) {
        SendHello();
      }
    }

    public void SetOnScreenDirection(float direction) {
      onScreenDirection = Mathf.Clamp(direction, -1, 1);
      onScreenDirectionTime = Time.unscaledTime;
    }

    public bool ShouldShowMobileControls() {
      return Application.isMobilePlatform || Input.touchSupported || Touchscreen.current != null;
    }

    // PRODUCTEUR (thread d'arrière-plan) : boucle bloquante sur udp.Receive().
    // Chaque message est empilé dans la file partagée ; aucune logique de jeu ici.
    void ReceiveLoop() {
      IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
      while (!stopping) {
        try {
          byte[] data = udp.Receive(ref remote);
          string message = Encoding.UTF8.GetString(data);
          lock (receivedMessagesLock) {
            receivedMessages.Enqueue(message);
          }
        } catch (Exception exception) {
          if (!stopping) {
            connected = false;
            lastStatus = "UDP receive failed: " + exception.Message;
          }
          return;
        }
      }
    }

    // CONSOMMATEUR (thread principal, appelé chaque frame) : vide la file et
    // traite les messages un par un. On ne garde le verrou que le temps du Dequeue
    // pour ne pas bloquer le thread réseau pendant le traitement.
    void DrainMessages() {
      while (true) {
        string message = null;
        lock (receivedMessagesLock) {
          if (receivedMessages.Count > 0) {
            message = receivedMessages.Dequeue();
          }
        }

        if (message == null) {
          return;
        }

        LogNetwork("Received " + message);
        HandleMessage(message);
      }
    }

    void HandleMessage(string message) {
      if (string.IsNullOrEmpty(message)) {
        return;
      }

      PongCircleNetworkSnapshot snapshot = JsonUtility.FromJson<PongCircleNetworkSnapshot>(message);
      if (snapshot == null || snapshot.type != "state") {
        return;
      }

      localPlayerId = snapshot.localPlayerId;
      localIsSpectator = snapshot.localIsSpectator;
      lastSnapshotTime = Time.unscaledTime;
      lobbyOpen = snapshot.lobbyOpen;
      connectedPlayerCount = snapshot.connectedPlayerCount;
      readyPlayerCount = snapshot.readyPlayerCount;
      spectatorCount = snapshot.spectatorCount;
      replayVoteCount = snapshot.replayVoteCount;
      postGameRemainingSeconds = snapshot.postGameRemainingSeconds;
      startCountdownSeconds = snapshot.startCountdownSeconds;
      if (snapshot.devices != null) {
        devices = snapshot.devices;
      }
      if (snapshot.lobbyDevices != null) {
        lobbyDevices = snapshot.lobbyDevices;
      }
      if (localIsSpectator) {
        lastStatus = "UDP spectator";
      } else {
        lastStatus = localPlayerId > 0 ? "UDP player " + localPlayerId : "UDP lobby";
      }

      if (localPlayerId > 0 || localIsSpectator) {
        joinRetryEndTime = 0;
      }

      EnsureCircleGame();
      if (CircleGame != null) {
        CircleGame.ApplyNetworkSnapshot(snapshot);
      }
    }

    void RetryJoinIfNeeded() {
      if (joinRetryEndTime <= 0 || localPlayerId > 0 || localIsSpectator) {
        return;
      }

      if (Time.unscaledTime > joinRetryEndTime) {
        joinRetryEndTime = 0;
        return;
      }

      if (Time.unscaledTime < nextJoinRetryTime) {
        return;
      }

      nextJoinRetryTime = Time.unscaledTime + 0.5f;
      SendJoin();
    }

    void SendHello() {
      SendMessage(PongCircleUdpProtocol.Hello(deviceId, deviceName));
    }

    void SendJoin() {
      SendMessage(PongCircleUdpProtocol.Simple("join"));
    }

    void SendSpectate() {
      SendMessage(PongCircleUdpProtocol.Simple("spectate"));
    }

    public void SendSmash() {
      SendMessage(PongCircleUdpProtocol.Simple("smash"));
    }

    void SendInput(float direction) {
      SendMessage(PongCircleUdpProtocol.Input(direction));
    }

    void SendMessage(PongCircleUdpPayload payload) {
      if (udp == null) {
        Connect();
      }

      if (udp == null) {
        return;
      }

      string wrapped = PongCircleUdpProtocol.BuildMessage(
        ++sequence,
        deviceId,
        deviceName,
        chosenDisplayName,
        chosenColorHex,
        payload);
      byte[] data = Encoding.UTF8.GetBytes(wrapped);
      try {
        LogNetwork("Sending " + data.Length + " bytes to " + serverEndPoint + " " + wrapped);
        int sent = udp.Send(data, data.Length, serverEndPoint);
        LogNetwork("Sent " + sent + " bytes from " + udp.Client.LocalEndPoint);
      } catch (Exception exception) {
        connected = false;
        lastStatus = "UDP send failed: " + exception.Message;
        Debug.LogWarning(lastStatus);
      }
    }

    void LogNetwork(string message) {
      if (!DebugNetworkLogging) {
        return;
      }

      Debug.Log("[Pong UDP] " + message);
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

    void ApplyLauncherEnvironment() {
      string host = Environment.GetEnvironmentVariable("PONG_UDP_HOST");
      if (!string.IsNullOrEmpty(host)) {
        ServerHost = host;
      }

      string port = Environment.GetEnvironmentVariable("PONG_UDP_PORT");
      if (int.TryParse(port, out int parsedPort)) {
        ServerPort = parsedPort;
      }
    }

    void ApplyEditorDefaultHost() {
#if UNITY_EDITOR
      if (UseRemoteInEditor) return;
      string launcherHost = Environment.GetEnvironmentVariable("PONG_UDP_HOST");
      if (string.IsNullOrEmpty(launcherHost) && ServerHost == "pong.becop.fr") {
        ServerHost = "127.0.0.1";
      }
#endif
    }

    float ReadLocalDirection() {
      if (Time.unscaledTime - onScreenDirectionTime < 0.2f) {
        return onScreenDirection;
      }

      return PongCircleKeyboardInput.ReadNetworkDirection();
    }

    string LoadOrCreateDeviceId() {
      string launcherDeviceId = Environment.GetEnvironmentVariable("PONG_UDP_DEVICE_ID");
      if (!string.IsNullOrEmpty(launcherDeviceId)) {
        return launcherDeviceId;
      }

      string key = "PongCircleUdpDeviceId";
      string existing = PlayerPrefs.GetString(key, "");
      if (!string.IsNullOrEmpty(existing)) {
#if UNITY_EDITOR
        return existing + "_" + System.Diagnostics.Process.GetCurrentProcess().Id;
#else
        return existing;
#endif
      }

      string created = Guid.NewGuid().ToString("N");
      PlayerPrefs.SetString(key, created);
      PlayerPrefs.Save();
#if UNITY_EDITOR
      return created + "_" + System.Diagnostics.Process.GetCurrentProcess().Id;
#else
      return created;
#endif
    }

    string DetectDeviceName() {
      string launcherDeviceName = Environment.GetEnvironmentVariable("PONG_UDP_DEVICE_NAME");
      if (!string.IsNullOrEmpty(launcherDeviceName)) {
        return launcherDeviceName;
      }

      string name = SystemInfo.deviceName;
      if (string.IsNullOrEmpty(name)) {
        name = SystemInfo.operatingSystemFamily.ToString();
      }

      return name;
    }

}
