using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public static class PongCircleKeyboardInput
{
    public static float ReadNetworkDirection()
    {
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

    public static float ReadLocalPlayerDirection(int playerIndex)
    {
      Keyboard keyboard = Keyboard.current;
      if (keyboard == null) {
        return 0;
      }

      switch (playerIndex % 8) {
        case 0:
          return ReadPair(keyboard.zKey, keyboard.wKey, keyboard.sKey);
        case 1:
          return ReadPair(keyboard.upArrowKey, null, keyboard.downArrowKey);
        case 2:
          return ReadPair(keyboard.tKey, null, keyboard.gKey);
        case 3:
          return ReadPair(keyboard.iKey, null, keyboard.kKey);
        case 4:
          return ReadPair(keyboard.fKey, null, keyboard.vKey);
        case 5:
          return ReadPair(keyboard.oKey, null, keyboard.lKey);
        case 6:
          return ReadPair(keyboard.aKey, null, keyboard.qKey);
        case 7:
          return ReadPair(keyboard.numpad8Key, null, keyboard.numpad5Key);
      }

      return 0;
    }

    static float ReadPair(KeyControl positive, KeyControl alternativePositive, KeyControl negative)
    {
      float direction = 0;
      if ((positive != null && positive.isPressed) || (alternativePositive != null && alternativePositive.isPressed)) {
        direction += 1;
      }

      if (negative != null && negative.isPressed) {
        direction -= 1;
      }

      return Mathf.Clamp(direction, -1, 1);
    }
}
