using UnityEngine;

public static class PongCircleGeometry
{
    public static Vector3 AngleToDirection(float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0);
    }

    public static float DirectionToAngle(Vector3 direction)
    {
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        if (angle < 0) {
            angle += 360f;
        }

        return angle;
    }

    public static bool AngleInsideSector(float angle, float startAngle, float endAngle)
    {
        float center = Mathf.LerpAngle(startAngle, endAngle, 0.5f);
        float halfSize = Mathf.Abs(Mathf.DeltaAngle(startAngle, endAngle)) * 0.5f;
        return Mathf.Abs(Mathf.DeltaAngle(center, angle)) <= halfSize;
    }

    public static float ClampPaddleAngle(float angle, float startAngle, float endAngle, float paddleArcDegrees)
    {
        float center = Mathf.LerpAngle(startAngle, endAngle, 0.5f);
        float sectorHalfSize = Mathf.Abs(Mathf.DeltaAngle(startAngle, endAngle)) * 0.5f;
        float paddleHalfSize = paddleArcDegrees * 0.5f;
        float allowedHalfSize = Mathf.Max(0, sectorHalfSize - paddleHalfSize);
        float delta = Mathf.Clamp(Mathf.DeltaAngle(center, angle), -allowedHalfSize, allowedHalfSize);
        return center + delta;
    }

    public static Vector3 BounceDirection(
        Vector3 currentDirection,
        float defenderPaddleAngle,
        float impactAngle,
        float paddleArcDegrees,
        float paddleAimInfluence)
    {
        Vector3 impactDirection = AngleToDirection(impactAngle);
        Vector3 paddleDirection = AngleToDirection(defenderPaddleAngle);
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
