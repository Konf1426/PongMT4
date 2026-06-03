using UnityEngine;

[ExecuteAlways]
public class PongCircleLauncher : MonoBehaviour
{
    public PongCircleGame CircleGame;
    public PongCircleWebSocketClient WebSocketClient;
    public bool EnableWebSocketSync = true;

    string playerCount = "4";
    float nextJoinTapTime;

    void Reset() {
      EnsureCircleGame();
    }

    void OnEnable() {
      EnsureCircleGame();
    }

    void Awake() {
      EnsureCircleGame();
      EnsureWebSocketClient();
      playerCount = Mathf.Max(CircleGame.MinimumPlayers, CircleGame.PlayerCount).ToString();
    }

    void EnsureCircleGame() {
      if (CircleGame != null) {
        return;
      }

      CircleGame = GetComponent<PongCircleGame>();
      if (CircleGame == null) {
        CircleGame = GameObject.FindFirstObjectByType<PongCircleGame>();
      }

      if (CircleGame == null) {
        CircleGame = gameObject.AddComponent<PongCircleGame>();
      }
    }

    void EnsureWebSocketClient() {
      if (!EnableWebSocketSync) {
        return;
      }

      if (WebSocketClient == null) {
        WebSocketClient = GetComponent<PongCircleWebSocketClient>();
      }

      if (WebSocketClient == null) {
        WebSocketClient = gameObject.AddComponent<PongCircleWebSocketClient>();
      }

      WebSocketClient.CircleGame = CircleGame;
    }

    void OnGUI() {
      if (!Application.isPlaying) {
        return;
      }

      if (CircleGame == null) {
        return;
      }

      if (ShouldShowJoinMenu()) {
        DrawJoinMenu();
        return;
      }

      if (CircleGame.WinnerId > 0) {
        DrawWinScreen();
        return;
      }

      if (ShouldShowNetworkLobby()) {
        DrawJoinMenu();
        return;
      }

      if (!CircleGame.IsGameStarted) {
        DrawLobby();
        return;
      }

      DrawGameHud();
    }

    bool ShouldShowJoinMenu() {
      return ShouldUseWebSocket()
        && WebSocketClient != null
        && WebSocketClient.LocalPlayerId <= 0;
    }

    bool ShouldShowNetworkLobby() {
      return ShouldUseWebSocket()
        && WebSocketClient != null
        && !CircleGame.IsGameStarted;
    }

    void DrawJoinMenu() {
      GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");

      bool canJoin = CircleGame.WinnerId <= 0
        && (!CircleGame.IsGameStarted || WebSocketClient.LocalPlayerId <= 0);
      bool mobileLayout = Screen.width < 700 || Screen.height < 700 || WebSocketClient.ShouldShowMobileControls();
      float width = mobileLayout ? Mathf.Min(Screen.width - 24, 560) : Mathf.Min(420, Screen.width - 32);
      float height = mobileLayout ? Mathf.Min(Screen.height - 48, 640) : Mathf.Min(520, Screen.height - 32);
      Rect panel = new Rect(
        (Screen.width - width) * 0.5f,
        (Screen.height - height) * 0.5f,
        width,
        height
      );

      if (canJoin) {
        HandleJoinMenuTap(panel);
      }

      GUILayout.BeginArea(panel, GUI.skin.box);
      GUILayout.Space(12);

      GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
      titleStyle.alignment = TextAnchor.MiddleCenter;
      titleStyle.fontSize = mobileLayout ? 34 : 28;

      GUIStyle subtitleStyle = new GUIStyle(GUI.skin.label);
      subtitleStyle.alignment = TextAnchor.MiddleCenter;
      subtitleStyle.fontSize = mobileLayout ? 20 : 16;
      subtitleStyle.wordWrap = true;

      GUIStyle joinButtonStyle = new GUIStyle(GUI.skin.button);
      joinButtonStyle.fontSize = mobileLayout ? 22 : 14;

      GUILayout.Label("Circle Pong", titleStyle);
      GUILayout.Space(8);
      GUILayout.Label("Menu", subtitleStyle);
      GUILayout.Space(mobileLayout ? 24 : 16);

      string actionLabel = CircleGame.IsGameStarted
        ? "Join Running Game"
        : "Join / Start Game";

      if (canJoin) {
        if (GUILayout.Button(actionLabel, joinButtonStyle, GUILayout.Height(mobileLayout ? 78 : 52))) {
          WebSocketClient.SendStartGame();
        }
      } else {
        GUILayout.Label("Waiting for players", subtitleStyle);
        if (WebSocketClient.LocalPlayerId > 0) {
          GUILayout.Label("Your player: " + WebSocketClient.LocalPlayerId, subtitleStyle);
        }
      }

      GUILayout.Space(mobileLayout ? 22 : 16);
      GUILayout.Label("Network: " + WebSocketClient.LastStatus, subtitleStyle);
      GUILayout.Label("Players in game: " + WebSocketClient.ConnectedPlayerCount + "/" + CircleGame.MaximumPlayers, subtitleStyle);
      if (!CircleGame.IsGameStarted) {
        GUILayout.Label("Minimum to start: " + CircleGame.MinimumPlayers, subtitleStyle);
      }

      GUILayout.Space(16);
      DrawConnectedDevices();
      DrawLobbyDevices();

      GUILayout.FlexibleSpace();
      if (canJoin) {
        GUILayout.Label("Tu es dans le menu tant que tu n'as pas rejoint la partie.", subtitleStyle);
      } else {
        GUILayout.Label("La partie se lance quand assez de joueurs ont rejoint.", subtitleStyle);
      }
      GUILayout.Space(12);
      GUILayout.EndArea();
    }

    void HandleJoinMenuTap(Rect panel) {
      Event currentEvent = Event.current;
      if (currentEvent == null) {
        return;
      }

      bool isPointerEvent = currentEvent.type == EventType.MouseDown || currentEvent.type == EventType.MouseUp;
      if (!isPointerEvent || !panel.Contains(currentEvent.mousePosition)) {
        return;
      }

      RequestJoinFromMenu();
      currentEvent.Use();
    }

    void RequestJoinFromMenu() {
      if (Time.unscaledTime < nextJoinTapTime) {
        return;
      }

      nextJoinTapTime = Time.unscaledTime + 0.25f;
      WebSocketClient.SendStartGame();
    }

    void DrawLobby() {
      GUILayout.BeginArea(new Rect(16, 16, 300, Screen.height - 32), GUI.skin.box);
      GUILayout.Label("Circle Pong");
      GUILayout.Space(8);
      GUILayout.Label("Lobby");
      GUILayout.Space(8);

      if (GUILayout.Button("Join / Start Game", GUILayout.Height(42))) {
        if (ShouldUseWebSocket()) {
          WebSocketClient.SendStartGame();
        } else {
          CircleGame.StartGame();
        }
      }

      GUILayout.Space(12);

      GUILayout.BeginHorizontal();
      GUILayout.Label("Players", GUILayout.Width(80));
      playerCount = GUILayout.TextField(playerCount);
      GUILayout.EndHorizontal();

      if (GUILayout.Button("Apply Player Count")) {
        ApplyPlayerCount();
      }

      GUILayout.BeginHorizontal();
      if (GUILayout.Button("- Player")) {
        CircleGame.RemovePlayer();
        playerCount = CircleGame.CurrentPlayerCount.ToString();
      }

      if (GUILayout.Button("+ Player")) {
        CircleGame.AddPlayer();
        playerCount = CircleGame.CurrentPlayerCount.ToString();
      }
      GUILayout.EndHorizontal();

      GUILayout.Space(8);
      GUILayout.Label("Current players: " + CircleGame.CurrentPlayerCount);
      GUILayout.Label("Alive players: " + CircleGame.AlivePlayerCount);
      GUILayout.Label("Minimum players: " + CircleGame.MinimumPlayers);
      GUILayout.Label("Maximum players: " + CircleGame.MaximumPlayers);
      GUILayout.Label("Status: " + CircleGame.Status);
      DrawNetworkStatus();
      DrawConnectedDevices();
      DrawLobbyDevices();
      GUILayout.Space(8);
      GUILayout.Label("Controls:");
      GUILayout.Label("Online: Z/S");
      GUILayout.Label("Local debug P1: Z/S");
      GUILayout.Label("Local debug P2: Up/Down");
      GUILayout.Label("Local debug P3: T/G");
      GUILayout.Label("Local debug P4: I/K");

      GUILayout.EndArea();
    }

    void DrawGameHud() {
      GUILayout.BeginArea(new Rect(16, 16, 300, Screen.height - 32), GUI.skin.box);
      GUILayout.Label("Circle Pong");
      GUILayout.Space(8);
      GUILayout.Label("Current players: " + CircleGame.CurrentPlayerCount);
      GUILayout.Label("Alive players: " + CircleGame.AlivePlayerCount);
      GUILayout.Label("Status: " + CircleGame.Status);
      DrawNetworkStatus();
      GUILayout.Space(8);

      if (GUILayout.Button("Restart Lobby")) {
        if (ShouldUseWebSocket()) {
          if (!WebSocketClient.IsConnected) {
            WebSocketClient.Connect();
            return;
          }

          WebSocketClient.SendRestartLobby();
        } else {
          CircleGame.Replay();
        }
        playerCount = CircleGame.CurrentPlayerCount.ToString();
      }

      DrawConnectedDevices();
      DrawLobbyDevices();

      GUILayout.EndArea();

      DrawMobileControls();
    }

    void DrawNetworkStatus() {
      if (!EnableWebSocketSync || WebSocketClient == null) {
        return;
      }

      GUILayout.Label("Network: " + WebSocketClient.LastStatus);
      if (WebSocketClient.ConnectedPlayerCount > 0) {
        GUILayout.Label("Players in game: " + WebSocketClient.ConnectedPlayerCount + "/" + CircleGame.MaximumPlayers);
      }
      if (WebSocketClient.LobbyOpen) {
        GUILayout.Label("Ready players: " + WebSocketClient.ReadyPlayerCount + "/" + CircleGame.MinimumPlayers);
      }
      if (WebSocketClient.LobbyOpen && !CircleGame.IsGameStarted) {
        GUILayout.Label("Lobby opened, waiting for start clicks");
      }
      if (WebSocketClient.LocalPlayerId > 0) {
        GUILayout.Label("Your player: " + WebSocketClient.LocalPlayerId);
      }
    }

    void DrawConnectedDevices() {
      if (!EnableWebSocketSync || WebSocketClient == null || WebSocketClient.Devices == null) {
        return;
      }

      GUILayout.Space(8);
      GUILayout.Label("Players in game:");

      foreach (PongCircleNetworkDeviceState device in WebSocketClient.Devices) {
        if (device.playerId <= 0) {
          continue;
        }

        GUILayout.Label("P" + device.playerId + " - " + device.name);
      }
    }

    void DrawLobbyDevices() {
      if (!EnableWebSocketSync || WebSocketClient == null || WebSocketClient.LobbyDevices == null) {
        return;
      }

      if (WebSocketClient.LobbyDevices.Length == 0) {
        return;
      }

      GUILayout.Space(8);
      GUILayout.Label("Lobby:");

      foreach (PongCircleNetworkDeviceState device in WebSocketClient.LobbyDevices) {
        GUILayout.Label(device.name + " (not in game)");
      }
    }

    bool ShouldUseWebSocket() {
      return EnableWebSocketSync
        && WebSocketClient != null
        && Application.platform == RuntimePlatform.WebGLPlayer;
    }

    void DrawMobileControls() {
      if (!EnableWebSocketSync || WebSocketClient == null || !WebSocketClient.ShouldShowMobileControls()) {
        return;
      }

      if (!CircleGame.IsGameStarted || WebSocketClient.LocalPlayerId <= 0) {
        return;
      }

      float buttonWidth = Mathf.Min(180, Screen.width * 0.32f);
      float buttonHeight = 72;
      float gap = 24;
      float totalWidth = buttonWidth * 2 + gap;
      float x = (Screen.width - totalWidth) * 0.5f;
      float y = Screen.height - buttonHeight - 24;

      GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
      buttonStyle.fontSize = 32;

      float direction = 0;
      if (GUI.RepeatButton(new Rect(x, y, buttonWidth, buttonHeight), "<-", buttonStyle)) {
        direction -= 1;
      }

      if (GUI.RepeatButton(new Rect(x + buttonWidth + gap, y, buttonWidth, buttonHeight), "->", buttonStyle)) {
        direction += 1;
      }

      if (Mathf.Abs(direction) > 0) {
        WebSocketClient.SetOnScreenDirection(direction);
      }
    }

    void DrawWinScreen() {
      GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");

      float width = 360;
      float height = ShouldUseWebSocket() ? 260 : 180;
      Rect panel = new Rect(
        (Screen.width - width) * 0.5f,
        (Screen.height - height) * 0.5f,
        width,
        height
      );

      GUILayout.BeginArea(panel, GUI.skin.box);
      GUILayout.FlexibleSpace();

      GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
      titleStyle.alignment = TextAnchor.MiddleCenter;
      titleStyle.fontSize = 28;

      GUIStyle subtitleStyle = new GUIStyle(GUI.skin.label);
      subtitleStyle.alignment = TextAnchor.MiddleCenter;
      subtitleStyle.fontSize = 18;

      GUILayout.Label("Player " + CircleGame.WinnerId + " wins!", titleStyle);
      GUILayout.Space(12);
      GUILayout.Label("Last player alive", subtitleStyle);
      GUILayout.Space(16);

      if (ShouldUseWebSocket()) {
        GUILayout.Label("Replay votes: " + WebSocketClient.ReplayVoteCount + "/" + CircleGame.MinimumPlayers, subtitleStyle);
        GUILayout.Label("Next action in: " + WebSocketClient.PostGameRemainingSeconds + "s", subtitleStyle);
        GUILayout.Space(10);

        if (GUILayout.Button("Replay", GUILayout.Height(42))) {
          WebSocketClient.SendReplayVote();
        }

        if (GUILayout.Button("Return Lobby", GUILayout.Height(42))) {
          WebSocketClient.SendReturnLobby();
        }
      } else {
        if (GUILayout.Button("Replay", GUILayout.Height(42))) {
          CircleGame.Replay();
          playerCount = CircleGame.CurrentPlayerCount.ToString();
        }
      }

      GUILayout.FlexibleSpace();
      GUILayout.EndArea();
    }

    void ApplyPlayerCount() {
      if (!int.TryParse(playerCount, out int count)) {
        Debug.LogWarning("Invalid player count: " + playerCount);
        return;
      }

      CircleGame.SetPlayerCount(count);
      playerCount = CircleGame.CurrentPlayerCount.ToString();
    }
}
