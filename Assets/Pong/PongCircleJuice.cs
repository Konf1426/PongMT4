using UnityEngine;

public class PongCircleJuice
{
    readonly MonoBehaviour owner;
    readonly Transform parent;

    Camera juiceCamera;
    Vector3 cameraBasePos;
    bool cameraCaptured;
    float shakeIntensity;
    Vector2 prevBallDir;
    bool hasPrevBallDir;
    int lastAlivePlayerCount = -1;
    int lastWinnerIdSeen;

    public PongCircleJuice(MonoBehaviour owner, Transform parent)
    {
      this.owner = owner;
      this.parent = parent;
    }

    public void UpdateShake(bool enabled)
    {
      if (!enabled) {
        return;
      }

      if (juiceCamera == null) {
        juiceCamera = Camera.main;
        if (juiceCamera == null) {
          return;
        }
        cameraBasePos = juiceCamera.transform.localPosition;
        cameraCaptured = true;
      }

      if (!cameraCaptured) {
        cameraBasePos = juiceCamera.transform.localPosition;
        cameraCaptured = true;
      }

      if (shakeIntensity > 0.0001f) {
        Vector3 offset = new Vector3(Random.value * 2f - 1f, Random.value * 2f - 1f, 0f) * shakeIntensity;
        juiceCamera.transform.localPosition = cameraBasePos + offset;
        shakeIntensity = Mathf.Max(0f, shakeIntensity - Time.deltaTime * 1.8f);
        if (shakeIntensity <= 0.0001f) {
          juiceCamera.transform.localPosition = cameraBasePos;
        }
      }
    }

    public void DetectStateEffects(bool enabled, int alivePlayerCount, bool gameStarted, int winnerId)
    {
      if (!enabled) {
        return;
      }

      if (lastAlivePlayerCount >= 0 && alivePlayerCount < lastAlivePlayerCount && gameStarted) {
        AddShake(0.18f);
      }
      lastAlivePlayerCount = alivePlayerCount;

      if (winnerId > 0 && winnerId != lastWinnerIdSeen) {
        AddShake(0.30f);
        lastWinnerIdSeen = winnerId;
      } else if (winnerId == 0) {
        lastWinnerIdSeen = 0;
      }
    }

    public void DetectBounceEffects(
        bool enabled,
        bool gameStarted,
        bool gameOver,
        Vector2 ballPos,
        Vector2 dir,
        float arenaRadius,
        float ballZ,
        System.Action<float> pulsePaddleAtAngle)
    {
      if (!Application.isPlaying || !enabled) {
        prevBallDir = dir;
        hasPrevBallDir = true;
        return;
      }

      if (hasPrevBallDir && gameStarted && !gameOver) {
        bool nearRim = ballPos.magnitude > arenaRadius * 0.7f;
        float d = Vector2.Dot(prevBallDir.normalized, dir.normalized);
        if (nearRim && d < 0.5f) {
          float angleDeg = Mathf.Atan2(ballPos.y, ballPos.x) * Mathf.Rad2Deg;
          Vector3 impact = PongCircleGeometry.AngleToDirection(angleDeg) * arenaRadius;
          impact.z = ballZ;
          PlayBounceEffect(impact);
          if (pulsePaddleAtAngle != null) {
            pulsePaddleAtAngle(angleDeg);
          }
        }
      }

      prevBallDir = dir;
      hasPrevBallDir = true;
    }

    void AddShake(float amount)
    {
      shakeIntensity = Mathf.Max(shakeIntensity, amount);
    }

    void PlayBounceEffect(Vector3 pos)
    {
      AddShake(0.05f);
      owner.StartCoroutine(PopRoutine(pos));
    }

    System.Collections.IEnumerator PopRoutine(Vector3 pos)
    {
      GameObject pop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
      pop.name = "BouncePop";
      Collider collider = pop.GetComponent<Collider>();
      if (collider != null) {
        Object.Destroy(collider);
      }
      pop.transform.SetParent(parent);
      pop.transform.position = pos;
      pop.GetComponent<Renderer>().material = PongCircleMaterialFactory.CreateUnlitHdr(PongCircleGame.NeonAccent, 2.6f);

      float t = 0f;
      const float duration = 0.22f;
      while (t < duration) {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / duration);
        float scale = Mathf.Sin(k * Mathf.PI) * 1.0f + 0.15f;
        pop.transform.localScale = Vector3.one * scale;
        yield return null;
      }
      Object.Destroy(pop);
    }
}
