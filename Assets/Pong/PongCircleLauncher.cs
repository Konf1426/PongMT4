using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class PongCircleLauncher : MonoBehaviour
{
    const string LauncherUiVersion = "UDP UI v2";

    public PongCircleGame CircleGame;
    public PongCircleUdpClient UdpClient;
    public bool EnableUdpSync = true;

    // Mis à true par PongCircleHud (UI Toolkit) pour masquer cette UI IMGUI héritée.
    // Reste false si le HUD UI Toolkit est absent (fallback).
    public bool HideImguiUi = false;

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
      EnsureUdpClient();
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

    void EnsureUdpClient() {
      if (!EnableUdpSync) {
        return;
      }

      if (UdpClient == null) {
        UdpClient = GetComponent<PongCircleUdpClient>();
      }

      if (UdpClient == null) {
        UdpClient = gameObject.AddComponent<PongCircleUdpClient>();
      }

      UdpClient.CircleGame = CircleGame;
    }

    void OnGUI() {
      if (!Application.isPlaying) {
        return;
      }

      if (HideImguiUi) {
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
      return ShouldUseNetwork()
        && GetLocalPlayerId() <= 0;
    }

    bool ShouldShowNetworkLobby() {
      return ShouldUseNetwork()
        && !CircleGame.IsGameStarted;
    }

    void DrawJoinMenu() {
      GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");

      bool canJoin = CircleGame.WinnerId <= 0;
      bool mobileLayout = Screen.width < 700 || Screen.height < 700 || ShouldShowNetworkMobileControls();
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
      GUILayout.Label(LauncherUiVersion, subtitleStyle);
      GUILayout.Space(8);
      GUILayout.Label("Menu", subtitleStyle);
      GUILayout.Space(mobileLayout ? 24 : 16);

      string actionLabel = CircleGame.IsGameStarted
        ? "Join Running Game"
        : "Join / Start Game";

      if (GUILayout.Button(actionLabel, joinButtonStyle, GUILayout.Height(mobileLayout ? 78 : 52))) {
        SendNetworkStartGame();
      }

      if (GetLocalPlayerId() > 0) {
        GUILayout.Label("Your player: " + GetLocalPlayerId(), subtitleStyle);
      }

      GUILayout.Space(mobileLayout ? 22 : 16);
      GUILayout.Label("Network: " + GetNetworkStatus(), subtitleStyle);
      GUILayout.Label("Players in game: " + GetConnectedPlayerCount() + "/" + CircleGame.MaximumPlayers, subtitleStyle);
      if (!CircleGame.IsGameStarted) {
        GUILayout.Label("Minimum to start: " + CircleGame.MinimumPlayers, subtitleStyle);
      }

      GUILayout.Space(16);
      DrawConnectedDevices();
      DrawLobbyDevices();

      GUILayout.FlexibleSpace();
      if (GetLocalPlayerId() <= 0) {
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
      SendNetworkStartGame();
    }

    void DrawLobby() {
      GUILayout.BeginArea(new Rect(16, 16, 360, Screen.height - 32), GUI.skin.box);
      GUILayout.Label("Circle Pong — Lobby");
      GUILayout.Space(8);

      if (GUILayout.Button("Join / Start Game", GUILayout.Height(42))) {
        if (ShouldUseUdp()) {
          SendNetworkStartGame();
        } else {
          CircleGame.StartGame();
        }
      }

      GUILayout.Space(12);

      // Nombre de joueurs (borné entre Min et Max par le moteur)
      GUILayout.BeginHorizontal();
      GUILayout.Label("Players", GUILayout.Width(60));
      playerCount = GUILayout.TextField(playerCount, GUILayout.Width(45));
      if (GUILayout.Button("Apply")) {
        ApplyPlayerCount();
      }

      GUI.enabled = CircleGame.CurrentPlayerCount > CircleGame.MinimumPlayers;
      if (GUILayout.Button("-")) {
        CircleGame.RemovePlayer();
        playerCount = CircleGame.CurrentPlayerCount.ToString();
      }

      GUI.enabled = CircleGame.CurrentPlayerCount < CircleGame.MaximumPlayers;
      if (GUILayout.Button("+")) {
        CircleGame.AddPlayer();
        playerCount = CircleGame.CurrentPlayerCount.ToString();
      }
      GUI.enabled = true;
      GUILayout.EndHorizontal();
      GUILayout.Label("Allowed: " + CircleGame.MinimumPlayers + " to " + CircleGame.MaximumPlayers);

      GUILayout.Space(6);
      CircleGame.MouseControlEnabled = GUILayout.Toggle(CircleGame.MouseControlEnabled, "Mouse controls Player 1");

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
        if (ShouldUseNetwork()) {
          if (!IsNetworkConnected()) {
            ConnectNetwork();
            return;
          }

          SendNetworkRestartLobby();
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
      if (!ShouldUseNetwork()) {
        return;
      }

      GUILayout.Label("Network: " + GetNetworkStatus());
      if (GetConnectedPlayerCount() > 0) {
        GUILayout.Label("Players in game: " + GetConnectedPlayerCount() + "/" + CircleGame.MaximumPlayers);
      }
      if (IsLobbyOpen()) {
        GUILayout.Label("Ready players: " + GetReadyPlayerCount() + "/" + CircleGame.MinimumPlayers);
      }
      if (IsLobbyOpen() && !CircleGame.IsGameStarted) {
        GUILayout.Label("Lobby opened, waiting for start clicks");
      }
      if (GetLocalPlayerId() > 0) {
        GUILayout.Label("Your player: " + GetLocalPlayerId());
      }
    }

    void DrawConnectedDevices() {
      PongCircleNetworkDeviceState[] networkDevices = GetNetworkDevices();
      if (networkDevices == null) {
        return;
      }

      GUILayout.Space(8);
      GUILayout.Label("Players in game:");

      foreach (PongCircleNetworkDeviceState device in networkDevices) {
        if (device.playerId <= 0) {
          continue;
        }

        GUILayout.Label("P" + device.playerId + " - " + device.name);
      }
    }

    void DrawLobbyDevices() {
      PongCircleNetworkDeviceState[] networkLobbyDevices = GetNetworkLobbyDevices();
      if (networkLobbyDevices == null) {
        return;
      }

      if (networkLobbyDevices.Length == 0) {
        return;
      }

      GUILayout.Space(8);
      GUILayout.Label("Lobby:");

      foreach (PongCircleNetworkDeviceState device in networkLobbyDevices) {
        GUILayout.Label(device.name + " (not in game)");
      }
    }

    bool ShouldUseNetwork() {
      return ShouldUseUdp();
    }

    bool ShouldUseUdp() {
      return EnableUdpSync
        && UdpClient != null;
    }

    void DrawMobileControls() {
      if (!ShouldUseNetwork() || !ShouldShowNetworkMobileControls()) {
        return;
      }

      if (!CircleGame.IsGameStarted || GetLocalPlayerId() <= 0) {
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
        SetNetworkOnScreenDirection(direction);
      }
    }

    void DrawWinScreen() {
      GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");

      float width = 360;
      float height = ShouldUseNetwork() ? 260 : 180;
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

      GUILayout.Label(CircleGame.GetPlayerName(CircleGame.WinnerId) + " wins!", titleStyle);
      GUILayout.Space(12);
      GUILayout.Label("Last player alive", subtitleStyle);
      GUILayout.Space(16);

      if (ShouldUseNetwork()) {
        GUILayout.Label("Replay votes: " + GetReplayVoteCount() + "/" + CircleGame.MinimumPlayers, subtitleStyle);
        GUILayout.Label("Next action in: " + GetPostGameRemainingSeconds() + "s", subtitleStyle);
        GUILayout.Space(10);

        if (GUILayout.Button("Replay", GUILayout.Height(42))) {
          SendNetworkReplayVote();
        }

        if (GUILayout.Button("Return Lobby", GUILayout.Height(42))) {
          SendNetworkReturnLobby();
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

    bool IsNetworkConnected() {
      if (ShouldUseUdp()) {
        return UdpClient.IsConnected;
      }

      return false;
    }

    void ConnectNetwork() {
      if (ShouldUseUdp()) {
        UdpClient.Connect();
      }
    }

    int GetLocalPlayerId() {
      if (ShouldUseUdp()) {
        return UdpClient.LocalPlayerId;
      }

      return 0;
    }

    int GetConnectedPlayerCount() {
      if (ShouldUseUdp()) {
        return UdpClient.ConnectedPlayerCount;
      }

      return 0;
    }

    int GetReadyPlayerCount() {
      if (ShouldUseUdp()) {
        return UdpClient.ReadyPlayerCount;
      }

      return 0;
    }

    int GetReplayVoteCount() {
      if (ShouldUseUdp()) {
        return UdpClient.ReplayVoteCount;
      }

      return 0;
    }

    int GetPostGameRemainingSeconds() {
      if (ShouldUseUdp()) {
        return UdpClient.PostGameRemainingSeconds;
      }

      return 0;
    }

    bool IsLobbyOpen() {
      if (ShouldUseUdp()) {
        return UdpClient.LobbyOpen;
      }

      return false;
    }

    string GetNetworkStatus() {
      if (ShouldUseUdp()) {
        return UdpClient.LastStatus;
      }

      return "Offline";
    }

    PongCircleNetworkDeviceState[] GetNetworkDevices() {
      if (ShouldUseUdp()) {
        return UdpClient.Devices;
      }

      return null;
    }

    PongCircleNetworkDeviceState[] GetNetworkLobbyDevices() {
      if (ShouldUseUdp()) {
        return UdpClient.LobbyDevices;
      }

      return null;
    }

    bool ShouldShowNetworkMobileControls() {
      if (ShouldUseUdp()) {
        return UdpClient.ShouldShowMobileControls();
      }

      return false;
    }

    void SetNetworkOnScreenDirection(float direction) {
      if (ShouldUseUdp()) {
        UdpClient.SetOnScreenDirection(direction);
      }
    }

    void SendNetworkStartGame() {
      if (ShouldUseUdp()) {
        UdpClient.SendStartGame();
      }
    }

    void SendNetworkRestartLobby() {
      if (ShouldUseUdp()) {
        UdpClient.SendRestartLobby();
      }
    }

    void SendNetworkReplayVote() {
      if (ShouldUseUdp()) {
        UdpClient.SendReplayVote();
      }
    }

    void SendNetworkReturnLobby() {
      if (ShouldUseUdp()) {
        UdpClient.SendReturnLobby();
      }
    }
}
