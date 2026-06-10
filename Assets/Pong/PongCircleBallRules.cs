using UnityEngine;

public static class PongCircleBallRules
{
    public static Vector3 CalculateBounceDirection(
        Vector3 currentDirection,
        float defenderPaddleAngle,
        float impactAngle,
        float paddleArcDegrees,
        float paddleAimInfluence)
    {
      Vector3 impactDirection = PongCircleGeometry.AngleToDirection(impactAngle);
      Vector3 paddleDirection = PongCircleGeometry.AngleToDirection(defenderPaddleAngle);
      Vector3 reflectedDirection = Vector3.Reflect(currentDirection, paddleDirection).normalized;

      float offset = Mathf.DeltaAngle(defenderPaddleAngle, impactAngle) / Mathf.Max(1, paddleArcDegrees * 0.5f);
      Vector3 tangent = new Vector3(-paddleDirection.y, paddleDirection.x, 0);
      Vector3 aimedDirection = (reflectedDirection + tangent * offset * paddleAimInfluence).normalized;

      if (Vector3.Dot(aimedDirection, -impactDirection) < 0.15f) {
        aimedDirection = Vector3.Slerp(aimedDirection, -impactDirection, 0.5f).normalized;
      }

      return aimedDirection;
    }
}
