using UnityEngine;

public class PongNetworkLauncher : MonoBehaviour
{
    public PongNetworkGame NetworkGame;

    string serverIP = "127.0.0.1";
    string serverPort = "25000";
    string listenPort = "25000";
    string localPlayers = "4";
    bool showDetails = true;

    void Awake() {
      if (NetworkGame == null) {
        NetworkGame = GameObject.FindFirstObjectByType<PongNetworkGame>();
      }

      if (NetworkGame == null) {
        NetworkGame = gameObject.AddComponent<PongNetworkGame>();
      }

      serverIP = NetworkGame.ServerIP;
      serverPort = NetworkGame.ServerPort.ToString();
      listenPort = NetworkGame.ListenPort.ToString();
      localPlayers = Mathf.Clamp(NetworkGame.HostLocalPlayers, 1, 4).ToString();
    }

    void OnGUI() {
      if (NetworkGame == null) {
        return;
      }

      GUILayout.BeginArea(new Rect(16, 16, 330, Screen.height - 32), GUI.skin.box);
      GUILayout.Label("MMPong Network");
      GUILayout.Space(8);

      if (!NetworkGame.IsRunning) {
        DrawStartMenu();
      } else {
        DrawRunningPanel();
      }

      GUILayout.EndArea();
    }

    void DrawStartMenu() {
      GUILayout.Label("Host");
      DrawTextField("Port", ref listenPort);
      DrawTextField("Local players", ref localPlayers);

      if (GUILayout.Button("Host Game")) {
        if (TryReadPort(listenPort, out int port) && TryReadLocalPlayers(localPlayers, out int playerCount)) {
          NetworkGame.StartHost(port, playerCount);
        }
      }

      GUILayout.Space(12);
      GUILayout.Label("Join");
      DrawTextField("Server IP", ref serverIP);
      DrawTextField("Port", ref serverPort);

      GUILayout.BeginHorizontal();
      if (GUILayout.Button("Join Left")) {
        Join(PongPlayer.PlayerLeft);
      }
      if (GUILayout.Button("Join Right")) {
        Join(PongPlayer.PlayerRight);
      }
      GUILayout.EndHorizontal();

      GUILayout.Space(8);
      GUILayout.Label(NetworkGame.LastStatus);
      GUILayout.Space(8);
      DrawControlsHelp();
    }

    void DrawRunningPanel() {
      GUILayout.BeginHorizontal();
      GUILayout.Label(NetworkGame.IsJoined ? "Connected" : "Connecting");
      showDetails = GUILayout.Toggle(showDetails, "Details");
      GUILayout.EndHorizontal();

      if (!showDetails) {
        return;
      }

      GUILayout.Label("Role: " + NetworkGame.Role);
      GUILayout.Label("Status: " + NetworkGame.LastStatus);
      GUILayout.Label("Left players: " + NetworkGame.LeftPlayerCount);
      GUILayout.Label("Right players: " + NetworkGame.RightPlayerCount);

      if (NetworkGame.Role == PongNetworkRole.Server) {
        GUILayout.Label("Local players: " + NetworkGame.HostLocalPlayerCount);
        DrawControlsHelp();
      } else {
        GUILayout.Label("Team: " + NetworkGame.ClientTeam);
        GUILayout.Label("Controls: Z/S");
      }

      GUILayout.Space(8);
      if (GUILayout.Button("Stop")) {
        NetworkGame.StopNetwork();
      }
    }

    void DrawTextField(string label, ref string value) {
      GUILayout.BeginHorizontal();
      GUILayout.Label(label, GUILayout.Width(95));
      value = GUILayout.TextField(value);
      GUILayout.EndHorizontal();
    }

    void DrawControlsHelp() {
      GUILayout.Label("Controls:");
      GUILayout.Label("P1 Left  : Z / S");
      GUILayout.Label("P2 Right : Up / Down");
      GUILayout.Label("P3 Left  : T / G");
      GUILayout.Label("P4 Right : I / K");
    }

    void Join(PongPlayer team) {
      if (TryReadPort(serverPort, out int port)) {
        NetworkGame.StartClient(serverIP, port, team);
      }
    }

    bool TryReadPort(string value, out int port) {
      if (int.TryParse(value, out port) && port > 0 && port <= 65535) {
        return true;
      }

      Debug.LogWarning("Invalid port: " + value);
      port = 0;
      return false;
    }

    bool TryReadLocalPlayers(string value, out int playerCount) {
      if (int.TryParse(value, out playerCount)) {
        playerCount = Mathf.Clamp(playerCount, 1, 4);
        localPlayers = playerCount.ToString();
        return true;
      }

      Debug.LogWarning("Invalid local player count: " + value);
      playerCount = 0;
      return false;
    }
}
