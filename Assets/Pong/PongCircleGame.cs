using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class PongCircleGame : MonoBehaviour
{
    public int PlayerCount = 2;
    public int MinimumPlayers = 2;
    public int MaximumPlayers = 10;
    public bool PreviewInEditMode = true;
    public float ArenaRadius = 5;
    public float SectorAlpha = 0.22f;
    public float SectorZ = 0.12f;
    public float PaddleWidth = 0.22f;
    public float PaddleArcDegrees = 22;
    public float PaddleAngularSpeed = 120;
    public float BallSpeed = 3.5f;
    public float PaddleAimInfluence = 0.45f;
    public bool StartInLobby = true;
    public bool HideClassicPongObjects = true;
    public bool NetworkControlled = false;
    public GameObject Ball;

    public int CurrentPlayerCount {
      get {
        return players.Count;
      }
    }

    public int AlivePlayerCount {
      get {
        return CountAlivePlayers();
      }
    }

    public int WinnerId {
      get {
        return winnerId;
      }
    }

    public string Status {
      get {
        return status;
      }
    }

    public bool IsGameStarted {
      get {
        return gameStarted;
      }
    }

    readonly List<CirclePlayer> players = new List<CirclePlayer>();
    readonly List<GameObject> generatedObjects = new List<GameObject>();
    readonly List<GameObject> hiddenClassicObjects = new List<GameObject>();

    Vector3 ballDirection;
    Vector3 ballStartPosition;
    bool gameStarted;
    bool gameOver;
    int winnerId;
    string status = "Playing";

    void OnEnable() {
      if (!Application.isPlaying) {
        ScheduleEditorPreview();
      }
    }

    void Start() {
      if (!Application.isPlaying) {
        return;
      }

      SetupGame();
    }

    void OnValidate() {
      if (!Application.isPlaying && isActiveAndEnabled) {
        ScheduleEditorPreview();
      }
    }

    void ScheduleEditorPreview() {
#if UNITY_EDITOR
      EditorApplication.delayCall -= RefreshEditorPreviewDelayed;
      EditorApplication.delayCall += RefreshEditorPreviewDelayed;
#endif
    }

#if UNITY_EDITOR
    void RefreshEditorPreviewDelayed() {
      if (this == null || Application.isPlaying || !isActiveAndEnabled) {
        return;
      }

      RefreshEditorPreview();
    }
#endif

    void SetupGame() {
      FindOrCreateBall();
      DisableClassicPongControls();
      PlayerCount = Mathf.Clamp(PlayerCount, MinimumPlayers, MaximumPlayers);
      BuildArena(PlayerCount);

      if (StartInLobby) {
        EnterLobby();
      } else {
        StartGame();
      }
    }

    void Update() {
      if (!Application.isPlaying) {
        return;
      }

      if (!NetworkControlled) {
        UpdatePaddles();
        UpdateBall();
      }
    }

    public void SetNetworkControlled(bool networkControlled) {
      NetworkControlled = networkControlled;
    }

    public void EnterLobby() {
      gameStarted = false;
      gameOver = false;
      winnerId = 0;
      status = "Waiting for players";

      if (Ball != null) {
        Ball.SetActive(false);
        Ball.transform.position = ballStartPosition;
      }
    }

    public void StartGame() {
      gameStarted = true;
      gameOver = false;
      winnerId = 0;
      status = "Playing";
      ResetBall();
    }

    void OnDisable() {
      RestoreClassicPongObjects();
      ClearArena();
    }

    void RefreshEditorPreview() {
      if (!PreviewInEditMode) {
        ClearArena();
        return;
      }

      FindOrCreateBall();
      DisableClassicPongControls();
      PlayerCount = Mathf.Clamp(PlayerCount, MinimumPlayers, MaximumPlayers);
      BuildArena(PlayerCount);
      ResetBall();
    }

    void FindOrCreateBall() {
      if (Ball != null) {
        ballStartPosition = Ball.transform.position;
        return;
      }

      PongBall pongBall = GameObject.FindFirstObjectByType<PongBall>();
      if (pongBall != null) {
        Ball = pongBall.gameObject;
        ballStartPosition = Vector3.zero;
        return;
      }

      Ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
      Ball.name = "CircleBall";
      Ball.transform.localScale = Vector3.one * 0.35f;
      ballStartPosition = Vector3.zero;
    }

    void DisableClassicPongControls() {
      PongBall pongBall = Ball != null ? Ball.GetComponent<PongBall>() : null;
      if (pongBall != null) {
        pongBall.AutoMove = false;
      }

      PongPaddle[] paddles = GameObject.FindObjectsByType<PongPaddle>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
      foreach (PongPaddle paddle in paddles) {
        paddle.UseLocalInput = false;
      }

      if (!HideClassicPongObjects) {
        return;
      }

      HideClassicObject("PaddleLeft");
      HideClassicObject("PaddleRight");
      HideClassicObject("BoundTop");
      HideClassicObject("BoundBottom");
      HideClassicObject("BoundLeft");
      HideClassicObject("BoundRight");
      HideClassicObject("Canvas");
    }

    void HideClassicObject(string objectName) {
      GameObject obj = GameObject.Find(objectName);
      if (obj == null || !obj.activeSelf) {
        return;
      }

      hiddenClassicObjects.Add(obj);
      obj.SetActive(false);
    }

    void RestoreClassicPongObjects() {
      foreach (GameObject obj in hiddenClassicObjects) {
        if (obj != null) {
          obj.SetActive(true);
        }
      }

      hiddenClassicObjects.Clear();
    }

    public void SetPlayerCount(int playerCount) {
      PlayerCount = Mathf.Clamp(playerCount, MinimumPlayers, MaximumPlayers);
      BuildArena(PlayerCount);

      if (Application.isPlaying && StartInLobby) {
        EnterLobby();
      } else {
        ResetBall();
      }
    }

    public void AddPlayer() {
      SetPlayerCount(players.Count + 1);
    }

    public void RemovePlayer() {
      SetPlayerCount(Mathf.Max(MinimumPlayers, players.Count - 1));
    }

    public void Replay() {
      BuildArena(PlayerCount);
      EnterLobby();
    }

    public void ApplyNetworkSnapshot(PongCircleNetworkSnapshot snapshot) {
      if (snapshot == null) {
        return;
      }

      NetworkControlled = true;

      int snapshotPlayerCount = Mathf.Clamp(snapshot.playerCount, MinimumPlayers, MaximumPlayers);
      if (players.Count != snapshotPlayerCount) {
        PlayerCount = snapshotPlayerCount;
        BuildArena(PlayerCount);
      }

      gameStarted = snapshot.gameStarted;
      gameOver = snapshot.gameOver;
      winnerId = snapshot.winnerId;
      status = string.IsNullOrEmpty(snapshot.status) ? "Network sync" : snapshot.status;

      if (snapshot.players != null) {
        foreach (PongCircleNetworkPlayerState playerState in snapshot.players) {
          CirclePlayer player = FindPlayerById(playerState.id);
          if (player == null) {
            continue;
          }

          player.IsAlive = playerState.alive;
          player.PaddleAngle = playerState.paddleAngle;
          player.HasPaddleAngle = true;
          if (player.PaddleObject != null) {
            player.PaddleObject.SetActive(player.IsAlive);
          }
        }
      }

      RedistributeAlivePlayers();

      if (Ball != null) {
        Ball.SetActive(gameStarted && !gameOver);
        Ball.transform.position = new Vector3(snapshot.ballX, snapshot.ballY, ballStartPosition.z);
      }
    }

    void BuildArena(int playerCount) {
      ClearArena();
      gameOver = false;
      winnerId = 0;
      status = "Playing";

      for (int i = 0; i < playerCount; i++) {
        Color color = Color.HSVToRGB((float)i / playerCount, 0.75f, 1f);
        color.a = SectorAlpha;

        CirclePlayer player = new CirclePlayer();
        player.Id = i + 1;
        player.Color = color;
        player.PaddleObject = CreatePaddle("Paddle_Player_" + player.Id, player.Color);
        players.Add(player);
      }

      RedistributeAlivePlayers();
      UpdatePaddleTransforms();
    }

    void ClearArena() {
      foreach (GameObject obj in generatedObjects) {
        if (obj != null) {
          DestroyObject(obj);
        }
      }

      generatedObjects.Clear();
      players.Clear();
      ClearGeneratedChildren();
    }

    GameObject CreateSector(string objectName, float startAngle, float endAngle, Color color) {
      GameObject obj = new GameObject(objectName);
      obj.transform.SetParent(transform);

      MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
      MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
      meshFilter.mesh = BuildSectorMesh(startAngle, endAngle);
      meshRenderer.material = CreateMaterial(color);

      generatedObjects.Add(obj);
      return obj;
    }

    Mesh BuildSectorMesh(float startAngle, float endAngle) {
      int arcSteps = 16;
      Vector3[] vertices = new Vector3[arcSteps + 2];
      int[] triangles = new int[arcSteps * 3];

      vertices[0] = Vector3.zero;
      vertices[0].z = SectorZ;
      for (int i = 0; i <= arcSteps; i++) {
        float t = (float)i / arcSteps;
        float angle = Mathf.Lerp(startAngle, endAngle, t);
        vertices[i + 1] = AngleToDirection(angle) * ArenaRadius;
        vertices[i + 1].z = SectorZ;
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

    GameObject CreatePaddle(string objectName, Color color) {
      GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
      obj.name = objectName;
      obj.transform.SetParent(transform);
      obj.GetComponent<Renderer>().material = CreateMaterial(new Color(color.r, color.g, color.b, 1));
      Collider collider = obj.GetComponent<Collider>();
      if (collider != null) {
        DestroyObject(collider);
      }
      generatedObjects.Add(obj);
      return obj;
    }

    Material CreateMaterial(Color color) {
      Shader shader = Shader.Find("Universal Render Pipeline/Lit");
      if (shader == null) {
        shader = Shader.Find("Standard");
      }

      Material material = new Material(shader);
      material.color = color;
      if (material.HasProperty("_BaseColor")) {
        material.SetColor("_BaseColor", color);
      }

      if (color.a < 1f) {
        if (material.HasProperty("_Surface")) { material.SetFloat("_Surface", 1); }
        if (material.HasProperty("_Mode")) { material.SetFloat("_Mode", 3); }
        if (material.HasProperty("_SrcBlend")) { material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); }
        if (material.HasProperty("_DstBlend")) { material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); }
        if (material.HasProperty("_ZWrite")) { material.SetInt("_ZWrite", 0); }
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = 3000;
      }

      return material;
    }

    void UpdatePaddles() {
      if (!gameStarted) {
        return;
      }

      for (int i = 0; i < players.Count; i++) {
        CirclePlayer player = players[i];
        float direction = ReadLocalDirection(i);
        player.PaddleAngle += direction * PaddleAngularSpeed * Time.deltaTime;
        player.PaddleAngle = ClampPaddleAngle(player.PaddleAngle, player.SectorStartAngle, player.SectorEndAngle);
      }

      UpdatePaddleTransforms();
    }

    void UpdatePaddleTransforms() {
      float paddleLength = Mathf.Max(0.7f, ArenaRadius * Mathf.Deg2Rad * PaddleArcDegrees);

      foreach (CirclePlayer player in players) {
        if (!player.IsAlive) {
          continue;
        }

        Vector3 radial = AngleToDirection(player.PaddleAngle);
        Vector3 tangent = new Vector3(-radial.y, radial.x, 0);
        Transform paddle = player.PaddleObject.transform;
        paddle.position = radial * (ArenaRadius - PaddleWidth * 0.5f);
        paddle.rotation = Quaternion.LookRotation(Vector3.forward, tangent);
        paddle.localScale = new Vector3(PaddleWidth, paddleLength, 0.35f);
      }
    }

    void UpdateBall() {
      if (Ball == null) {
        return;
      }

      if (!gameStarted) {
        return;
      }

      if (gameOver) {
        return;
      }

      Ball.transform.position += ballDirection * BallSpeed * Time.deltaTime;

      Vector3 ballPosition = Ball.transform.position;
      if (ballPosition.magnitude < ArenaRadius) {
        return;
      }

      float angle = DirectionToAngle(ballPosition);
      CirclePlayer defender = FindPlayerAtAngle(angle);
      if (defender == null || !defender.IsAlive) {
        ResetBall();
        return;
      }

      float paddleDelta = Mathf.Abs(Mathf.DeltaAngle(angle, defender.PaddleAngle));
      if (paddleDelta <= PaddleArcDegrees * 0.5f) {
        BounceOnPaddle(defender, angle);
      } else {
        EliminatePlayer(defender);
      }
    }

    void BounceOnPaddle(CirclePlayer defender, float impactAngle) {
      Vector3 impactDirection = AngleToDirection(impactAngle);
      Vector3 paddleDirection = AngleToDirection(defender.PaddleAngle);
      Vector3 reflectedDirection = Vector3.Reflect(ballDirection, paddleDirection).normalized;

      float offset = Mathf.DeltaAngle(defender.PaddleAngle, impactAngle) / Mathf.Max(1, PaddleArcDegrees * 0.5f);
      Vector3 tangent = new Vector3(-paddleDirection.y, paddleDirection.x, 0);
      Vector3 aimedDirection = (reflectedDirection + tangent * offset * PaddleAimInfluence).normalized;

      if (Vector3.Dot(aimedDirection, -impactDirection) < 0.15f) {
        aimedDirection = Vector3.Slerp(aimedDirection, -impactDirection, 0.5f).normalized;
      }

      ballDirection = aimedDirection;
      Ball.transform.position = impactDirection * (ArenaRadius - 0.12f);
    }

    void EliminatePlayer(CirclePlayer player) {
      player.IsAlive = false;
      player.Score--;
      if (player.PaddleObject != null) {
        player.PaddleObject.SetActive(false);
      }

      int aliveCount = CountAlivePlayers();
      Debug.Log("Player " + player.Id + " eliminated. Alive players: " + aliveCount);

      if (aliveCount <= 1) {
        CirclePlayer winner = FindLastAlivePlayer();
        winnerId = winner != null ? winner.Id : 0;
        gameOver = true;
        status = winnerId > 0 ? "Player " + winnerId + " wins!" : "No winner";

        if (Ball != null) {
          Ball.SetActive(false);
        }

        Debug.Log(status);
        return;
      }

      RedistributeAlivePlayers();
      status = "Player " + player.Id + " eliminated";
      ResetBall();
    }

    CirclePlayer FindPlayerAtAngle(float angle) {
      foreach (CirclePlayer player in players) {
        if (AngleInsideSector(angle, player.SectorStartAngle, player.SectorEndAngle)) {
          return player;
        }
      }

      return null;
    }

    bool AngleInsideSector(float angle, float startAngle, float endAngle) {
      float center = Mathf.LerpAngle(startAngle, endAngle, 0.5f);
      float halfSize = Mathf.Abs(Mathf.DeltaAngle(startAngle, endAngle)) * 0.5f;
      return Mathf.Abs(Mathf.DeltaAngle(center, angle)) <= halfSize;
    }

    void ResetBall() {
      if (Ball == null) {
        return;
      }

      Ball.SetActive(true);
      Ball.transform.position = ballStartPosition;
      float angle = Random.Range(0f, 360f);
      ballDirection = AngleToDirection(angle).normalized;
    }

    float ReadLocalDirection(int playerIndex) {
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

    float ReadPair(KeyControl positive, KeyControl alternativePositive, KeyControl negative) {
      float direction = 0;
      if ((positive != null && positive.isPressed) || (alternativePositive != null && alternativePositive.isPressed)) {
        direction += 1;
      }

      if (negative != null && negative.isPressed) {
        direction -= 1;
      }

      return Mathf.Clamp(direction, -1, 1);
    }

    Vector3 AngleToDirection(float angle) {
      float radians = angle * Mathf.Deg2Rad;
      return new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0);
    }

    float DirectionToAngle(Vector3 direction) {
      float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
      if (angle < 0) {
        angle += 360f;
      }

      return angle;
    }

    float ClampAngle(float angle, float startAngle, float endAngle) {
      float center = Mathf.LerpAngle(startAngle, endAngle, 0.5f);
      float halfSize = Mathf.Abs(Mathf.DeltaAngle(startAngle, endAngle)) * 0.5f;
      float delta = Mathf.Clamp(Mathf.DeltaAngle(center, angle), -halfSize, halfSize);
      return center + delta;
    }

    float ClampPaddleAngle(float angle, float startAngle, float endAngle) {
      float center = Mathf.LerpAngle(startAngle, endAngle, 0.5f);
      float sectorHalfSize = Mathf.Abs(Mathf.DeltaAngle(startAngle, endAngle)) * 0.5f;
      float paddleHalfSize = PaddleArcDegrees * 0.5f;
      float allowedHalfSize = Mathf.Max(0, sectorHalfSize - paddleHalfSize);
      float delta = Mathf.Clamp(Mathf.DeltaAngle(center, angle), -allowedHalfSize, allowedHalfSize);
      return center + delta;
    }

    int CountAlivePlayers() {
      int count = 0;
      foreach (CirclePlayer player in players) {
        if (player.IsAlive) {
          count++;
        }
      }

      return count;
    }

    CirclePlayer FindLastAlivePlayer() {
      foreach (CirclePlayer player in players) {
        if (player.IsAlive) {
          return player;
        }
      }

      return null;
    }

    CirclePlayer FindPlayerById(int playerId) {
      foreach (CirclePlayer player in players) {
        if (player.Id == playerId) {
          return player;
        }
      }

      return null;
    }

    void RedistributeAlivePlayers() {
      ClearSectors();

      int aliveCount = CountAlivePlayers();
      if (aliveCount == 0) {
        return;
      }

      float sectorSize = 360f / aliveCount;
      int aliveIndex = 0;

      foreach (CirclePlayer player in players) {
        if (!player.IsAlive) {
          continue;
        }

        float sectorCenter = aliveIndex * sectorSize;
        player.SectorIndex = aliveIndex;
        player.SectorStartAngle = sectorCenter - sectorSize * 0.5f;
        player.SectorEndAngle = sectorCenter + sectorSize * 0.5f;
        if (player.PaddleObject != null && !player.PaddleObject.activeSelf) {
          player.PaddleObject.SetActive(true);
        }

        if (!player.HasPaddleAngle) {
          player.PaddleAngle = sectorCenter;
          player.HasPaddleAngle = true;
        } else {
          player.PaddleAngle = ClampPaddleAngle(player.PaddleAngle, player.SectorStartAngle, player.SectorEndAngle);
        }

        player.SectorObject = CreateSector("Sector_Player_" + player.Id, player.SectorStartAngle, player.SectorEndAngle, player.Color);
        aliveIndex++;
      }

      UpdatePaddleTransforms();
    }

    void ClearSectors() {
      for (int i = generatedObjects.Count - 1; i >= 0; i--) {
        GameObject obj = generatedObjects[i];
        if (obj != null && obj.name.StartsWith("Sector_Player_")) {
          DestroyObject(obj);
          generatedObjects.RemoveAt(i);
        }
      }

      foreach (CirclePlayer player in players) {
        player.SectorObject = null;
      }
    }

    void ClearGeneratedChildren() {
      for (int i = transform.childCount - 1; i >= 0; i--) {
        Transform child = transform.GetChild(i);
        if (IsGeneratedObjectName(child.name)) {
          DestroyObject(child.gameObject);
        }
      }
    }

    bool IsGeneratedObjectName(string objectName) {
      return objectName.StartsWith("Sector_Player_") || objectName.StartsWith("Paddle_Player_");
    }

    void DestroyObject(Object obj) {
      if (obj == null) {
        return;
      }

      if (Application.isPlaying) {
        Destroy(obj);
      } else {
        DestroyImmediate(obj);
      }
    }

    class CirclePlayer
    {
      public int Id;
      public int SectorIndex;
      public int Score;
      public bool IsAlive = true;
      public bool HasPaddleAngle;
      public float SectorStartAngle;
      public float SectorEndAngle;
      public float PaddleAngle;
      public Color Color;
      public GameObject SectorObject;
      public GameObject PaddleObject;
    }
}
