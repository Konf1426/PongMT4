using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class PongCircleUdpClient : MonoBehaviour
{
    public PongCircleGame CircleGame;
    public string ServerHost = "pong.becop.fr";
    public int ServerPort = 41234;
    public bool AutoConnect = true;
    public float InputSendRate = 30;

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
    float onScreenDirection;
    float onScreenDirectionTime;
    float lastSentDirection = 999f;
    int redundantSendsLeft;

    void Awake() {
      EnsureCircleGame();
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

      if (localPlayerId <= 0) {
        return;
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

        serverEndPoint = new IPEndPoint(addresses[0], ServerPort);
        udp = new UdpClient();
        udp.Connect(serverEndPoint);
        stopping = false;
        connected = true;
        lastStatus = "UDP connected";

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
      lastStatus = "UDP join request sent";
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
      SendMessage("{\"type\":\"restart\"}");
    }

    public void SendReplayVote() {
      SendMessage("{\"type\":\"replay\"}");
    }

    public void SendReturnLobby() {
      SendMessage("{\"type\":\"lobby\"}");
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
        } catch {
          if (!stopping) {
            connected = false;
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
      SendMessage("{\"type\":\"hello\",\"deviceId\":\"" + Escape(deviceId) + "\",\"deviceName\":\"" + Escape(deviceName) + "\"}");
    }

    void SendJoin() {
      SendMessage("{\"type\":\"join\"}");
    }

    void SendSpectate() {
      SendMessage("{\"type\":\"spectate\"}");
    }

    void SendInput(float direction) {
      SendMessage("{\"type\":\"input\",\"direction\":" + direction.ToString("0.###", CultureInfo.InvariantCulture) + "}");
    }

    void SendMessage(string json) {
      if (udp == null) {
        Connect();
      }

      if (udp == null) {
        return;
      }

      string wrapped = "{\"seq\":" + (++sequence) + ",\"deviceId\":\"" + Escape(deviceId) + "\",\"deviceName\":\"" + Escape(deviceName) + "\",\"displayName\":\"" + Escape(chosenDisplayName) + "\",\"color\":\"" + Escape(chosenColorHex) + "\",\"payload\":" + json + "}";
      byte[] data = Encoding.UTF8.GetBytes(wrapped);
      try {
        udp.Send(data, data.Length);
      } catch (Exception exception) {
        connected = false;
        lastStatus = "UDP send failed: " + exception.Message;
      }
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

    string LoadOrCreateDeviceId() {
      string launcherDeviceId = Environment.GetEnvironmentVariable("PONG_UDP_DEVICE_ID");
      if (!string.IsNullOrEmpty(launcherDeviceId)) {
        return launcherDeviceId;
      }

      string key = "PongCircleUdpDeviceId";
      string existing = PlayerPrefs.GetString(key, "");
      if (!string.IsNullOrEmpty(existing)) {
        return existing;
      }

      string created = Guid.NewGuid().ToString("N");
      PlayerPrefs.SetString(key, created);
      PlayerPrefs.Save();
      return created;
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

    string Escape(string value) {
      return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
