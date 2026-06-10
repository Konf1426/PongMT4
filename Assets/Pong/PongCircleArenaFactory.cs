using System.Collections.Generic;
using UnityEngine;

public static class PongCircleArenaFactory
{
    public static GameObject CreateSector(
        Transform parent,
        List<GameObject> generatedObjects,
        string objectName,
        float startAngle,
        float endAngle,
        Color color,
        float arenaRadius,
        float sectorZ)
    {
      GameObject obj = new GameObject(objectName);
      obj.transform.SetParent(parent);

      MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
      MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
      meshFilter.mesh = PongCircleGeometry.BuildSectorMesh(startAngle, endAngle, arenaRadius, sectorZ);
      Color fill = color;
      fill.a = Mathf.Max(color.a, 0.5f);
      meshRenderer.material = PongCircleMaterialFactory.Create(fill, 1.1f);

      generatedObjects.Add(obj);
      return obj;
    }

    public static GameObject CreatePaddle(Transform parent, List<GameObject> generatedObjects, string objectName, Color color)
    {
      GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
      obj.name = objectName;
      obj.transform.SetParent(parent);
      obj.GetComponent<Renderer>().material = PongCircleMaterialFactory.Create(new Color(color.r, color.g, color.b, 1), 1.6f);
      Collider collider = obj.GetComponent<Collider>();
      if (collider != null) {
        DestroyGenerated(collider);
      }
      generatedObjects.Add(obj);
      return obj;
    }

    public static void BuildDecor(
        Transform parent,
        List<GameObject> generatedObjects,
        float arenaRadius,
        float sectorZ,
        Color neonAccent)
    {
      GameObject backdrop = new GameObject("ArenaBackdrop");
      backdrop.transform.SetParent(parent);
      backdrop.transform.localPosition = new Vector3(0, 0, 0.12f);
      MeshFilter backdropFilter = backdrop.AddComponent<MeshFilter>();
      MeshRenderer backdropRenderer = backdrop.AddComponent<MeshRenderer>();
      backdropFilter.mesh = PongCircleGeometry.BuildSectorMesh(0f, 360f, arenaRadius, sectorZ);
      backdropRenderer.material = PongCircleMaterialFactory.Create(new Color(0.07f, 0.09f, 0.12f, 1f));
      generatedObjects.Add(backdrop);

      GameObject ring = new GameObject("ArenaRing");
      ring.transform.SetParent(parent);
      LineRenderer line = ring.AddComponent<LineRenderer>();
      line.useWorldSpace = false;
      line.loop = true;
      line.widthMultiplier = 0.08f;
      line.numCapVertices = 4;
      int segments = 96;
      line.positionCount = segments;
      for (int i = 0; i < segments; i++) {
        float angle = 360f * i / segments;
        Vector3 p = PongCircleGeometry.AngleToDirection(angle) * arenaRadius;
        p.z = 0.05f;
        line.SetPosition(i, p);
      }
      line.material = PongCircleMaterialFactory.CreateUnlitHdr(neonAccent, 1.6f);
      generatedObjects.Add(ring);
    }

    public static void StyleBall(GameObject ball, Color neonAccent)
    {
      if (ball == null) {
        return;
      }

      Renderer renderer = ball.GetComponent<Renderer>();
      if (renderer != null) {
        renderer.material = PongCircleMaterialFactory.Create(new Color(0.75f, 1f, 0.92f, 1f), 2.5f);
      }

      TrailRenderer trail = ball.GetComponent<TrailRenderer>();
      if (trail == null) {
        trail = ball.AddComponent<TrailRenderer>();
      }
      trail.time = 0.22f;
      trail.startWidth = 0.28f;
      trail.endWidth = 0f;
      trail.numCapVertices = 2;
      trail.material = PongCircleMaterialFactory.CreateUnlitHdr(neonAccent, 2.2f);
      trail.startColor = new Color(neonAccent.r, neonAccent.g, neonAccent.b, 0.9f);
      trail.endColor = new Color(neonAccent.r, neonAccent.g, neonAccent.b, 0f);
    }

    static void DestroyGenerated(Object obj)
    {
      if (Application.isPlaying) {
        Object.Destroy(obj);
      } else {
        Object.DestroyImmediate(obj);
      }
    }
}
