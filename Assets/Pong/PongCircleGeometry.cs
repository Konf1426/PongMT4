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
      return angle < 0 ? angle + 360f : angle;
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

    public static Mesh BuildSectorMesh(float startAngle, float endAngle, float arenaRadius, float sectorZ, int arcSteps = 16)
    {
      Vector3[] vertices = new Vector3[arcSteps + 2];
      int[] triangles = new int[arcSteps * 3];

      vertices[0] = new Vector3(0, 0, sectorZ);
      for (int i = 0; i <= arcSteps; i++) {
        float t = (float)i / arcSteps;
        float angle = Mathf.Lerp(startAngle, endAngle, t);
        vertices[i + 1] = AngleToDirection(angle) * arenaRadius;
        vertices[i + 1].z = sectorZ;
      }

      for (int i = 0; i < arcSteps; i++) {
        int tri = i * 3;
        triangles[tri] = 0;
        triangles[tri + 1] = i + 2;
        triangles[tri + 2] = i + 1;
      }

      Mesh mesh = new Mesh();
      mesh.vertices = vertices;
      mesh.triangles = triangles;
      mesh.RecalculateNormals();
      mesh.RecalculateBounds();
      return mesh;
    }
}
