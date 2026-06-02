using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class PongCircleLauncher : MonoBehaviour
{
    public PongCircleGame CircleGame;

    string playerCount = "4";
    Vector2 lobbyScroll;

    void Reset() {
      EnsureCircleGame();
    }

    void OnEnable() {
      EnsureCircleGame();
    }

    void Awake() {
      EnsureCircleGame();
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

    void OnGUI() {
      if (!Application.isPlaying) {
        return;
      }

      if (CircleGame == null) {
        return;
      }

      if (CircleGame.WinnerId > 0) {
        DrawWinScreen();
        return;
      }

      if (!CircleGame.IsGameStarted) {
        DrawLobby();
        return;
      }

      DrawGameHud();
    }

    void DrawLobby() {
      GUILayout.BeginArea(new Rect(16, 16, 360, Screen.height - 32), GUI.skin.box);
      GUILayout.Label("Circle Pong — Lobby");
      GUILayout.Space(8);

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
      GUILayout.Label("Players (click the color square to change it):");
      lobbyScroll = GUILayout.BeginScrollView(lobbyScroll, GUILayout.Height(Mathf.Min(240, Screen.height - 320)));
      List<PongCircleGame.PlayerInfo> infos = CircleGame.GetPlayerInfos();
      for (int i = 0; i < infos.Count; i++) {
        PongCircleGame.PlayerInfo info = infos[i];
        GUILayout.BeginHorizontal();

        Color previous = GUI.backgroundColor;
        GUI.backgroundColor = new Color(info.Color.r, info.Color.g, info.Color.b, 1);
        if (GUILayout.Button("", GUILayout.Width(22), GUILayout.Height(20))) {
          CircleGame.CyclePlayerColor(i);
        }
        GUI.backgroundColor = previous;

        string editedName = GUILayout.TextField(info.Name, GUILayout.Width(200));
        if (editedName != info.Name) {
          CircleGame.SetPlayerName(i, editedName);
        }
        GUILayout.EndHorizontal();
      }
      GUILayout.EndScrollView();

      GUILayout.Space(6);
      GUILayout.Label("Current: " + CircleGame.CurrentPlayerCount + "   Status: " + CircleGame.Status);

      GUILayout.Space(8);
      if (CircleGame.IsCountingDown) {
        GUIStyle countdownStyle = new GUIStyle(GUI.skin.label);
        countdownStyle.fontSize = 22;
        countdownStyle.alignment = TextAnchor.MiddleCenter;
        GUILayout.Label("Starting in " + CircleGame.CountdownSeconds + "...", countdownStyle);
      } else if (CircleGame.CurrentPlayerCount < CircleGame.MinimumPlayers) {
        GUILayout.Label("Need at least " + CircleGame.MinimumPlayers + " players (auto-start).");
      }

      GUILayout.Space(6);
      GUILayout.Label("Controls: P1 Z/S  P2 Up/Down  P3 T/G  P4 I/K ...");

      GUILayout.EndArea();
    }

    void DrawGameHud() {
      GUILayout.BeginArea(new Rect(16, 16, 300, Screen.height - 32), GUI.skin.box);
      GUILayout.Label("Circle Pong");
      GUILayout.Space(8);
      GUILayout.Label("Current players: " + CircleGame.CurrentPlayerCount);
      GUILayout.Label("Alive players: " + CircleGame.AlivePlayerCount);
      GUILayout.Label("Status: " + CircleGame.Status);
      GUILayout.Space(8);

      if (GUILayout.Button("Restart Lobby")) {
        CircleGame.Replay();
        playerCount = CircleGame.CurrentPlayerCount.ToString();
      }

      GUILayout.EndArea();
    }

    void DrawWinScreen() {
      GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");

      float width = 360;
      float height = 180;
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

      if (GUILayout.Button("Replay", GUILayout.Height(42))) {
        CircleGame.Replay();
        playerCount = CircleGame.CurrentPlayerCount.ToString();
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
