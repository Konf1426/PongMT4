using UnityEngine;


[ExecuteAlways]
public class PongCircleLauncher : MonoBehaviour
{
    public PongCircleGame CircleGame;
    public PongCircleUdpClient UdpClient;
    public bool EnableUdpSync = true;

    void Reset() {
      EnsureCircleGame();
    }

    void OnEnable() {
      EnsureCircleGame();
    }

    void Awake() {
      EnsureCircleGame();
      EnsureUdpClient();
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
}
