using UnityEngine;
using UnityEngine.InputSystem;

public enum PongPlayer {
  PlayerLeft = 1,
  PlayerRight = 2
}

public class PongPaddle : MonoBehaviour
{ 
    public PongPlayer Player = PongPlayer.PlayerLeft;
    public float Speed = 1;
    public float MinY = -4;
    public float MaxY = 4;
    public bool UseLocalInput = true;

    PongInput inputActions;
    InputAction PlayerAction;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        inputActions = new PongInput();
        switch (Player) {
          case PongPlayer.PlayerLeft:
            PlayerAction = inputActions.Pong.Player1;
            break;
          case PongPlayer.PlayerRight:
            PlayerAction = inputActions.Pong.Player2;
            break;
        }

        PlayerAction.Enable();
    }

    // Update is called once per frame
    void Update()
    {
      if (!UseLocalInput) {
        return;
      }

      float direction = PlayerAction.ReadValue<float>();

      Move(direction, Time.deltaTime);
    }

    public void Move(float direction, float deltaTime) {
      Vector3 newPos = transform.position + (Vector3.up * Speed * direction * deltaTime);
      newPos.y = Mathf.Clamp(newPos.y, MinY, MaxY);

      transform.position = newPos;
    }

    public void SetY(float y) {
      Vector3 newPos = transform.position;
      newPos.y = Mathf.Clamp(y, MinY, MaxY);
      transform.position = newPos;
    }

    void OnDisable() {
      if (PlayerAction != null) {
        PlayerAction.Disable();
      }
    }
}
