using UnityEngine;

[ExecuteAlways]
public class PongCircleLauncher : MonoBehaviour
{
    public PongCircleGame CircleGame;

    string playerCount = "4";

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
      GUILayout.BeginArea(new Rect(16, 16, 300, Screen.height - 32), GUI.skin.box);
      GUILayout.Label("Circle Pong");
      GUILayout.Space(8);
      GUILayout.Label("Lobby");
      GUILayout.Space(8);

      if (GUILayout.Button("Start Game", GUILayout.Height(42))) {
        CircleGame.StartGame();
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
      GUILayout.Label("Status: " + CircleGame.Status);
      GUILayout.Space(8);
      GUILayout.Label("Controls:");
      GUILayout.Label("P1: Z/S");
      GUILayout.Label("P2: Up/Down");
      GUILayout.Label("P3: T/G");
      GUILayout.Label("P4: I/K");
      GUILayout.Label("P5+: network-ready later");

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

      GUILayout.Label("Player " + CircleGame.WinnerId + " wins!", titleStyle);
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
