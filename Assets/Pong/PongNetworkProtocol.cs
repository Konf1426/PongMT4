using System.Globalization;
using UnityEngine;

public enum PongNetworkRole {
  Server = 0,
  Client = 1,
}

public static class PongNetworkProtocol
{
    public const string Join = "JOIN";
    public const string Welcome = "WELCOME";
    public const string Input = "INPUT";
    public const string State = "STATE";

    public static string BuildJoin(PongPlayer team) {
      return Join + "|" + team;
    }

    public static string BuildWelcome(int playerId, PongPlayer team) {
      return Welcome + "|" + playerId + "|" + team;
    }

    public static string BuildInput(int playerId, float direction) {
      return Input + "|" + playerId + "|" + ToText(direction);
    }

    public static string BuildState(Vector3 ballPosition, float leftY, float rightY, PongBallState state, int leftPlayers, int rightPlayers) {
      return State
        + "|" + ToText(ballPosition.x)
        + "|" + ToText(ballPosition.y)
        + "|" + ToText(leftY)
        + "|" + ToText(rightY)
        + "|" + state
        + "|" + leftPlayers
        + "|" + rightPlayers;
    }

    public static bool TryParseFloat(string value, out float result) {
      return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    public static bool TryParseInt(string value, out int result) {
      return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    public static bool TryParsePlayer(string value, out PongPlayer player) {
      return System.Enum.TryParse(value, out player);
    }

    public static bool TryParseBallState(string value, out PongBallState state) {
      return System.Enum.TryParse(value, out state);
    }

    static string ToText(float value) {
      return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
