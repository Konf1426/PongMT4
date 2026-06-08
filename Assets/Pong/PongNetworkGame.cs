using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class PongNetworkGame : MonoBehaviour
{
    public bool AutoStart = false;
    public PongNetworkRole Role = PongNetworkRole.Server;
    public int ListenPort = 25000;
    public string ServerIP = "127.0.0.1";
    public int ServerPort = 25000;
    public PongPlayer ClientTeam = PongPlayer.PlayerLeft;
    public int HostLocalPlayers = 4;

    public PongBall Ball;
    public PongPaddle PaddleLeft;
    public PongPaddle PaddleRight;

    public float StateSendRate = 30;
    public float InputSendRate = 30;
    public float JoinRetryRate = 1;
    public float MaxAggregatedInput = 8;
    public float ClientTimeout = 10;

    public bool IsRunning {
      get {
        return udp != null;
      }
    }

    public bool IsJoined {
      get {
        return Role == PongNetworkRole.Server || joined;
      }
    }

    public int LocalPlayerId {
      get {
        return localPlayerId;
      }
    }

    public int LeftPlayerCount {
      get {
        return leftPlayerCount;
      }
    }

    public int RightPlayerCount {
      get {
        return rightPlayerCount;
      }
    }

    public int HostLocalPlayerCount {
      get {
        return hostLocalPlayers.Count;
      }
    }

    public string LastStatus {
      get {
        return lastStatus;
      }
    }

    UdpClient udp;
    IPEndPoint receiveEndPoint;
    IPEndPoint serverEndPoint;
    float nextStateSendTime;
    float nextInputSendTime;
    float nextJoinSendTime;
    int nextPlayerId = 1;
    int localPlayerId;
    int leftPlayerCount;
    int rightPlayerCount;
    bool joined;
    string lastStatus = "Not connected";

    readonly Dictionary<string, PongNetworkPlayer> players = new Dictionary<string, PongNetworkPlayer>();
    readonly List<PongLocalPlayer> hostLocalPlayers = new List<PongLocalPlayer>();

    void Start() {
      FindSceneObjects();
      DisableLocalSimulation();

      if (AutoStart) {
        StartConfiguredGame();
      }
    }

    void Update() {
      if (!IsRunning) {
        return;
      }

      ReceiveMessages();

      switch (Role) {
        case PongNetworkRole.Server:
          UpdateServer();
          break;
        case PongNetworkRole.Client:
          UpdateClient();
          break;
      }
    }

    void OnDisable() {
      StopNetwork();
    }

    public bool StartServer(int port) {
      Role = PongNetworkRole.Server;
      ListenPort = port;
      HostLocalPlayers = 0;
      return StartConfiguredGame();
    }

    public bool StartHost(int port, int localPlayerCount) {
      Role = PongNetworkRole.Server;
      ListenPort = port;
      HostLocalPlayers = localPlayerCount;
      return StartConfiguredGame();
    }

    public bool StartClient(string serverIP, int serverPort, PongPlayer team) {
      Role = PongNetworkRole.Client;
      ServerIP = serverIP;
      ServerPort = serverPort;
      ClientTeam = team;
      return StartConfiguredGame();
    }

    public bool StartConfiguredGame() {
      FindSceneObjects();
      ConfigureSceneForRole();
      ResetNetworkState();
      return OpenSocket();
    }

    public void StopNetwork() {
      CloseSocket();
      ResetNetworkState();
      DisableLocalSimulation();
      lastStatus = "Stopped";
    }

    void FindSceneObjects() {
      if (Ball == null) {
        Ball = GameObject.FindFirstObjectByType<PongBall>();
      }

      if (PaddleLeft == null || PaddleRight == null) {
        PongPaddle[] paddles = GameObject.FindObjectsByType<PongPaddle>(FindObjectsInactive.Exclude);
        foreach (PongPaddle paddle in paddles) {
          if (paddle.Player == PongPlayer.PlayerLeft) {
            PaddleLeft = paddle;
          } else if (paddle.Player == PongPlayer.PlayerRight) {
            PaddleRight = paddle;
          }
        }
      }
    }

    void ConfigureSceneForRole() {
      if (Ball != null) {
        Ball.AutoMove = Role == PongNetworkRole.Server;
      }

      if (PaddleLeft != null) {
        PaddleLeft.UseLocalInput = false;
      }

      if (PaddleRight != null) {
        PaddleRight.UseLocalInput = false;
      }
    }

    void DisableLocalSimulation() {
      if (Ball != null) {
        Ball.AutoMove = false;
      }

      if (PaddleLeft != null) {
        PaddleLeft.UseLocalInput = false;
      }

      if (PaddleRight != null) {
        PaddleRight.UseLocalInput = false;
      }
    }

    void ResetNetworkState() {
      players.Clear();
      serverEndPoint = null;
      nextPlayerId = 1;
      localPlayerId = 0;
      leftPlayerCount = 0;
      rightPlayerCount = 0;
      joined = false;
      nextStateSendTime = 0;
      nextInputSendTime = 0;
      nextJoinSendTime = 0;
      hostLocalPlayers.Clear();
    }

    bool OpenSocket() {
      CloseSocket();

      try {
        udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.ExclusiveAddressUse = false;

        if (Role == PongNetworkRole.Server) {
          receiveEndPoint = new IPEndPoint(IPAddress.Any, ListenPort);
        udp.Client.Bind(receiveEndPoint);
          CreateHostLocalPlayers();
          lastStatus = "Server listening on UDP port " + ListenPort;
          if (hostLocalPlayers.Count > 0) {
            lastStatus += " with " + hostLocalPlayers.Count + " local players";
          }
          Debug.Log("Pong " + lastStatus);
        } else {
          receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
          udp.Client.Bind(receiveEndPoint);
          serverEndPoint = new IPEndPoint(IPAddress.Parse(ServerIP), ServerPort);
          lastStatus = "Connecting to " + ServerIP + ":" + ServerPort;
          SendJoin();
        }

        return true;
      } catch (System.Exception ex) {
        lastStatus = "Network error: " + ex.Message;
        Debug.LogWarning(lastStatus);
        CloseSocket();
        DisableLocalSimulation();
        return false;
      }
    }

    void CloseSocket() {
      if (udp != null) {
        udp.Close();
        udp = null;
      }
    }

    void ReceiveMessages() {
      if (udp == null) {
        return;
      }

      while (udp.Available > 0) {
        IPEndPoint source = new IPEndPoint(IPAddress.Any, 0);
        byte[] data;

        try {
          data = udp.Receive(ref source);
        } catch (SocketException ex) {
          if (Role == PongNetworkRole.Client && !joined) {
            lastStatus = "Waiting for server on " + ServerIP + ":" + ServerPort;
            return;
          }

          lastStatus = "Network receive error: " + ex.Message;
          Debug.LogWarning(lastStatus);
          return;
        }

        string message = Encoding.UTF8.GetString(data);

        if (Role == PongNetworkRole.Server) {
          HandleServerMessage(source, message);
        } else {
          HandleClientMessage(message);
        }
      }
    }

    void HandleServerMessage(IPEndPoint source, string message) {
      string[] parts = message.Split('|');
      if (parts.Length == 0) {
        return;
      }

      switch (parts[0]) {
        case PongNetworkProtocol.Join:
          HandleJoin(source, parts);
          break;
        case PongNetworkProtocol.Input:
          HandleInput(source, parts);
          break;
      }
    }

    void HandleJoin(IPEndPoint source, string[] parts) {
      if (parts.Length < 2 || !PongNetworkProtocol.TryParsePlayer(parts[1], out PongPlayer team)) {
        return;
      }

      string key = EndPointKey(source);
      if (!players.TryGetValue(key, out PongNetworkPlayer player)) {
        player = new PongNetworkPlayer(nextPlayerId++, source, team);
        players.Add(key, player);
        Debug.Log("Pong player " + player.Id + " joined " + team + " from " + key);
      } else {
        player.Team = team;
        player.EndPoint = source;
      }

      UpdateServerPlayerCounts();
      SendTo(source, PongNetworkProtocol.BuildWelcome(player.Id, player.Team));
      BroadcastState();
    }

    void HandleInput(IPEndPoint source, string[] parts) {
      if (parts.Length < 3 || !PongNetworkProtocol.TryParseFloat(parts[2], out float direction)) {
        return;
      }

      string key = EndPointKey(source);
      if (!players.TryGetValue(key, out PongNetworkPlayer player)) {
        return;
      }

      player.InputDirection = Mathf.Clamp(direction, -1, 1);
      player.LastSeenTime = Time.time;
    }

    void HandleClientMessage(string message) {
      string[] parts = message.Split('|');
      if (parts.Length == 0) {
        return;
      }

      switch (parts[0]) {
        case PongNetworkProtocol.Welcome:
          HandleWelcome(parts);
          break;
        case PongNetworkProtocol.State:
          HandleState(parts);
          break;
      }
    }

    void HandleWelcome(string[] parts) {
      if (parts.Length < 3 || !PongNetworkProtocol.TryParseInt(parts[1], out localPlayerId)) {
        return;
      }

      joined = true;
      lastStatus = "Joined as player " + localPlayerId + " on " + parts[2];
      Debug.Log("Pong " + lastStatus);
    }

    void HandleState(string[] parts) {
      if (parts.Length < 8) {
        return;
      }

      if (!PongNetworkProtocol.TryParseFloat(parts[1], out float ballX)
        || !PongNetworkProtocol.TryParseFloat(parts[2], out float ballY)
        || !PongNetworkProtocol.TryParseFloat(parts[3], out float leftY)
        || !PongNetworkProtocol.TryParseFloat(parts[4], out float rightY)
        || !PongNetworkProtocol.TryParseBallState(parts[5], out PongBallState state)
        || !PongNetworkProtocol.TryParseInt(parts[6], out leftPlayerCount)
        || !PongNetworkProtocol.TryParseInt(parts[7], out rightPlayerCount)) {
        return;
      }

      if (Ball != null) {
        Ball.SetNetworkState(new Vector3(ballX, ballY, Ball.transform.position.z), state);
      }

      if (PaddleLeft != null) {
        PaddleLeft.SetY(leftY);
      }

      if (PaddleRight != null) {
        PaddleRight.SetY(rightY);
      }
    }

    void UpdateServer() {
      RemoveInactivePlayers();

      float leftInput = GetAggregatedInput(PongPlayer.PlayerLeft);
      float rightInput = GetAggregatedInput(PongPlayer.PlayerRight);

      foreach (PongLocalPlayer player in hostLocalPlayers) {
        if (player.Team == PongPlayer.PlayerLeft) {
          leftInput += PongDirectionalInput.ReadClassicHostDirection(player.ControlIndex);
        } else {
          rightInput += PongDirectionalInput.ReadClassicHostDirection(player.ControlIndex);
        }
      }

      if (PaddleLeft != null) {
        PaddleLeft.Move(Mathf.Clamp(leftInput, -MaxAggregatedInput, MaxAggregatedInput), Time.deltaTime);
      }

      if (PaddleRight != null) {
        PaddleRight.Move(Mathf.Clamp(rightInput, -MaxAggregatedInput, MaxAggregatedInput), Time.deltaTime);
      }

      if (Time.time >= nextStateSendTime) {
        nextStateSendTime = Time.time + GetRateDelay(StateSendRate);
        BroadcastState();
      }
    }

    void UpdateClient() {
      if (!joined && Time.time >= nextJoinSendTime) {
        SendJoin();
      }

      if (Time.time >= nextInputSendTime) {
        nextInputSendTime = Time.time + GetRateDelay(InputSendRate);
        SendInput(ReadDefaultLocalDirection());
      }
    }

    float GetAggregatedInput(PongPlayer team) {
      float direction = 0;
      foreach (PongNetworkPlayer player in players.Values) {
        if (player.Team == team) {
          direction += player.InputDirection;
        }
      }

      return Mathf.Clamp(direction, -MaxAggregatedInput, MaxAggregatedInput);
    }

    int CountPlayers(PongPlayer team) {
      int count = 0;

      foreach (PongLocalPlayer player in hostLocalPlayers) {
        if (player.Team == team) {
          count++;
        }
      }

      foreach (PongNetworkPlayer player in players.Values) {
        if (player.Team == team) {
          count++;
        }
      }

      return count;
    }

    void RemoveInactivePlayers() {
      List<string> inactivePlayers = new List<string>();
      foreach (KeyValuePair<string, PongNetworkPlayer> entry in players) {
        if (Time.time - entry.Value.LastSeenTime > ClientTimeout) {
          inactivePlayers.Add(entry.Key);
        }
      }

      foreach (string key in inactivePlayers) {
        Debug.Log("Pong player disconnected: " + key);
        players.Remove(key);
      }

      if (inactivePlayers.Count > 0) {
        UpdateServerPlayerCounts();
      }
    }

    float ReadDefaultLocalDirection() {
      return PongDirectionalInput.ReadClassicHostDirection(0);
    }

    void SendJoin() {
      nextJoinSendTime = Time.time + GetRateDelay(JoinRetryRate);
      SendToServer(PongNetworkProtocol.BuildJoin(ClientTeam));
    }

    void SendInput(float direction) {
      if (!joined) {
        return;
      }

      SendToServer(PongNetworkProtocol.BuildInput(localPlayerId, direction));
    }

    void BroadcastState() {
      if (Ball == null || PaddleLeft == null || PaddleRight == null) {
        return;
      }

      UpdateServerPlayerCounts();
      string message = PongNetworkProtocol.BuildState(
        Ball.transform.position,
        PaddleLeft.transform.position.y,
        PaddleRight.transform.position.y,
        Ball.State,
        leftPlayerCount,
        rightPlayerCount
      );

      foreach (PongNetworkPlayer player in players.Values) {
        SendTo(player.EndPoint, message);
      }
    }

    void SendToServer(string message) {
      if (serverEndPoint == null) {
        return;
      }

      SendTo(serverEndPoint, message);
    }

    void SendTo(IPEndPoint endPoint, string message) {
      if (udp == null || endPoint == null) {
        return;
      }

      byte[] data = Encoding.UTF8.GetBytes(message);
      try {
        udp.Send(data, data.Length, endPoint);
      } catch (SocketException ex) {
        lastStatus = "Network send error: " + ex.Message;
        Debug.LogWarning(lastStatus);
      }
    }

    static string EndPointKey(IPEndPoint endPoint) {
      return endPoint.Address + ":" + endPoint.Port;
    }

    void UpdateServerPlayerCounts() {
      leftPlayerCount = CountPlayers(PongPlayer.PlayerLeft);
      rightPlayerCount = CountPlayers(PongPlayer.PlayerRight);
    }

    void CreateHostLocalPlayers() {
      hostLocalPlayers.Clear();
      int count = Mathf.Clamp(HostLocalPlayers, 0, 4);

      for (int i = 0; i < count; i++) {
        PongPlayer team = i % 2 == 0 ? PongPlayer.PlayerLeft : PongPlayer.PlayerRight;
        hostLocalPlayers.Add(new PongLocalPlayer(team, i));
      }

      UpdateServerPlayerCounts();
    }

    static float GetRateDelay(float rate) {
      return 1 / Mathf.Max(1, rate);
    }

    class PongNetworkPlayer
    {
      public int Id;
      public IPEndPoint EndPoint;
      public PongPlayer Team;
      public float InputDirection;
      public float LastSeenTime;

      public PongNetworkPlayer(int id, IPEndPoint endPoint, PongPlayer team) {
        Id = id;
        EndPoint = endPoint;
        Team = team;
        LastSeenTime = Time.time;
      }
    }

    class PongLocalPlayer
    {
      public PongPlayer Team;
      public int ControlIndex;

      public PongLocalPlayer(PongPlayer team, int controlIndex) {
        Team = team;
        ControlIndex = controlIndex;
      }
    }
}
