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
    public bool MouseControlEnabled = true;
    public int LocalPlayerIndex = 0;
    public float StartCountdownDuration = 3f;
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

    [Header("Réseau — lissage (réduction de latence ressentie)")]
    public bool NetworkSmoothing = true;
    public bool LocalPaddlePrediction = true;
    public float PaddleSmoothingSpeed = 14f;   
    public float PaddleReconcileSpeed = 2f;     
    public float BallSmoothingSpeed = 12f;      

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
    readonly List<PlayerProfile> profiles = new List<PlayerProfile>();
    readonly List<GameObject> generatedObjects = new List<GameObject>();
    readonly List<GameObject> hiddenClassicObjects = new List<GameObject>();

    Vector3 ballDirection;
    Vector3 ballStartPosition;
    bool gameStarted;
    bool gameOver;
    bool countdownActive;
    float countdownRemaining;
    int winnerId;
    string status = "Playing";

    // État réseau pour l'interpolation/prédiction (Update les consomme entre 2 snapshots).
    int networkLocalPlayerId;
    float networkLocalDirection;
    Vector2 netBallPosition;
    Vector2 netBallDirection;
    bool hasNetworkBall;

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
      MinimumPlayers = Mathf.Max(1, MinimumPlayers);
      MaximumPlayers = Mathf.Max(MinimumPlayers, MaximumPlayers);
      PlayerCount = Mathf.Clamp(PlayerCount, MinimumPlayers, MaximumPlayers);

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

      if (!gameStarted && !gameOver) {
        UpdateLobbyCountdown();
      }

      if (!NetworkControlled) {
        UpdatePaddles();
        UpdateBall();
      } else if (NetworkSmoothing) {
        UpdateNetworkInterpolation();
      }
    }

    public void SetNetworkControlled(bool networkControlled) {
      NetworkControlled = networkControlled;
    }

    public void SetLocalPredictedInput(float direction) {
      networkLocalDirection = Mathf.Clamp(direction, -1f, 1f);
    }

    void UpdateNetworkInterpolation() {
      if (!gameStarted || gameOver) {
        return;
      }

      float dt = Time.deltaTime;

      if (Ball != null && hasNetworkBall) {
        netBallPosition += netBallDirection * BallSpeed * dt;
        Vector3 target = new Vector3(netBallPosition.x, netBallPosition.y, ballStartPosition.z);
        float ballK = 1f - Mathf.Exp(-BallSmoothingSpeed * dt);
        Ball.transform.position = Vector3.Lerp(Ball.transform.position, target, ballK);
      }

      float remoteK = 1f - Mathf.Exp(-PaddleSmoothingSpeed * dt);
      float reconcileK = 1f - Mathf.Exp(-PaddleReconcileSpeed * dt);
      foreach (CirclePlayer player in players) {
        if (!player.IsAlive) {
          continue;
        }

        if (LocalPaddlePrediction && player.Id == networkLocalPlayerId) {
          player.PaddleAngle += networkLocalDirection * PaddleAngularSpeed * dt;
          player.PaddleAngle = Mathf.LerpAngle(player.PaddleAngle, player.PaddleAngleTarget, reconcileK);
          player.PaddleAngle = ClampPaddleAngle(player.PaddleAngle, player.SectorStartAngle, player.SectorEndAngle);
        } else {
          player.PaddleAngle = Mathf.LerpAngle(player.PaddleAngle, player.PaddleAngleTarget, remoteK);
        }
      }

      UpdatePaddleTransforms();
    }

    void UpdateLobbyCountdown() {
      if (!CanStart) {
        if (countdownActive) {
          countdownActive = false;
          status = "Waiting for players";
        }
        return;
      }

      if (!countdownActive) {
        countdownActive = true;
        countdownRemaining = StartCountdownDuration;
      }

      countdownRemaining -= Time.deltaTime;
      if (countdownRemaining <= 0f) {
        countdownActive = false;
        StartGame();
        return;
      }

      status = "Starting in " + Mathf.CeilToInt(countdownRemaining) + "...";
    }

    public void EnterLobby() {
      gameStarted = false;
      gameOver = false;
      countdownActive = false;
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
      countdownActive = false;
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
      } else {
        PongBall pongBall = GameObject.FindFirstObjectByType<PongBall>();
        if (pongBall != null) {
          Ball = pongBall.gameObject;
          ballStartPosition = Vector3.zero;
        } else {
          Ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
          Ball.name = "CircleBall";
          Ball.transform.localScale = Vector3.one * 0.35f;
          ballStartPosition = Vector3.zero;
        }
      }

      StyleBall();
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
      networkLocalPlayerId = snapshot.localPlayerId;

      int snapshotPlayerCount = Mathf.Clamp(snapshot.playerCount, MinimumPlayers, MaximumPlayers);
      if (players.Count != snapshotPlayerCount) {
        PlayerCount = snapshotPlayerCount;
        BuildArena(PlayerCount);
      }

      gameStarted = snapshot.gameStarted;
      gameOver = snapshot.gameOver;
      winnerId = snapshot.winnerId;
      if (!string.IsNullOrEmpty(snapshot.status)) {
        status = snapshot.status;
      }

      if (snapshot.players != null) {
        foreach (PongCircleNetworkPlayerState playerState in snapshot.players) {
          CirclePlayer player = FindPlayerById(playerState.id);
          if (player == null) {
            continue;
          }

          player.IsAlive = playerState.alive;
          // Cible réseau autoritative ; le lissage (Update) rapproche PaddleAngle de
          // cette cible. Au 1er snapshot (ou lissage désactivé), on cale directement.
          player.PaddleAngleTarget = playerState.paddleAngle;
          if (!player.HasPaddleAngle || !NetworkSmoothing) {
            player.PaddleAngle = playerState.paddleAngle;
          }
          player.HasPaddleAngle = true;
          if (player.PaddleObject != null) {
            player.PaddleObject.SetActive(player.IsAlive);
          }

          if (!string.IsNullOrEmpty(playerState.name)) {
            player.Name = playerState.name;
          }
          if (!string.IsNullOrEmpty(playerState.color)
              && ColorUtility.TryParseHtmlString("#" + playerState.color, out Color parsedColor)) {
            parsedColor.a = SectorAlpha;
            player.Color = parsedColor;
          }
        }
      }

      RedistributeAlivePlayers();

      Vector2 authoritativeBall = new Vector2(snapshot.ballX, snapshot.ballY);
      bool ballActive = gameStarted && !gameOver;
      if (Ball != null) {
        Ball.SetActive(ballActive);
        if (!NetworkSmoothing || !hasNetworkBall) {
          Ball.transform.position = new Vector3(authoritativeBall.x, authoritativeBall.y, ballStartPosition.z);
        }
      }
      netBallPosition = authoritativeBall;
      netBallDirection = new Vector2(snapshot.ballDirX, snapshot.ballDirY);
      hasNetworkBall = ballActive;
    }

    void BuildArena(int playerCount) {
      ClearArena();
      gameOver = false;
      winnerId = 0;
      status = "Playing";

      BuildArenaDecor();
      EnsureProfiles(playerCount);
      for (int i = 0; i < playerCount; i++) {
        PlayerProfile profile = profiles[i];

        CirclePlayer player = new CirclePlayer();
        player.Id = i + 1;
        player.Profile = profile;
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
      Color fill = color;
      fill.a = Mathf.Max(color.a, 0.5f);
      meshRenderer.material = CreateMaterial(fill, 1.1f);

      generatedObjects.Add(obj);
      return obj;
    }

    // Construit le maillage d'un secteur en éventail de triangles (triangle fan) :
    // portion de disque entre startAngle et endAngle.
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
      obj.GetComponent<Renderer>().material = CreateMaterial(new Color(color.r, color.g, color.b, 1), 1.6f);
      Collider collider = obj.GetComponent<Collider>();
      if (collider != null) {
        DestroyObject(collider);
      }
      generatedObjects.Add(obj);
      return obj;
    }

    Material CreateMaterial(Color color) {
      return CreateMaterial(color, 0f);
    }

    Material CreateMaterial(Color color, float emission) {
      Shader shader = Shader.Find("Universal Render Pipeline/Lit");
      if (shader == null) {
        shader = Shader.Find("Standard");
      }

      Material material = new Material(shader);
      material.color = color;
      if (material.HasProperty("_BaseColor")) {
        material.SetColor("_BaseColor", color);
      }

      if (emission > 0f) {
        material.EnableKeyword("_EMISSION");
        Color hdr = new Color(color.r, color.g, color.b, 1f).linear * emission;
        if (material.HasProperty("_EmissionColor")) {
          material.SetColor("_EmissionColor", hdr);
        }
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
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

    Material CreateUnlitHdrMaterial(Color color, float intensity) {
      Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
      if (shader == null) {
        shader = Shader.Find("Unlit/Color");
      }

      Material material = new Material(shader);
      Color hdr = color.linear * intensity;
      if (material.HasProperty("_BaseColor")) {
        material.SetColor("_BaseColor", hdr);
      }
      material.color = hdr;
      return material;
    }

    static readonly Color NeonAccent = new Color(0.345f, 0.902f, 0.784f, 1f);

    void BuildArenaDecor() {
      GameObject backdrop = new GameObject("ArenaBackdrop");
      backdrop.transform.SetParent(transform);
      backdrop.transform.localPosition = new Vector3(0, 0, 0.12f); // derrière (caméra en -Z)
      MeshFilter backdropFilter = backdrop.AddComponent<MeshFilter>();
      MeshRenderer backdropRenderer = backdrop.AddComponent<MeshRenderer>();
      backdropFilter.mesh = BuildSectorMesh(0f, 360f);
      backdropRenderer.material = CreateMaterial(new Color(0.07f, 0.09f, 0.12f, 1f));
      generatedObjects.Add(backdrop);

      GameObject ring = new GameObject("ArenaRing");
      ring.transform.SetParent(transform);
      LineRenderer line = ring.AddComponent<LineRenderer>();
      line.useWorldSpace = false;
      line.loop = true;
      line.widthMultiplier = 0.08f;
      line.numCapVertices = 4;
      int segments = 96;
      line.positionCount = segments;
      for (int i = 0; i < segments; i++) {
        float angle = 360f * i / segments;
        Vector3 p = AngleToDirection(angle) * ArenaRadius;
        p.z = 0.05f; 
        line.SetPosition(i, p);
      }
      line.material = CreateUnlitHdrMaterial(NeonAccent, 1.6f);
      generatedObjects.Add(ring);
    }

    void StyleBall() {
      if (Ball == null) {
        return;
      }

      Renderer renderer = Ball.GetComponent<Renderer>();
      if (renderer != null) {
        renderer.material = CreateMaterial(new Color(0.75f, 1f, 0.92f, 1f), 2.5f);
      }

      TrailRenderer trail = Ball.GetComponent<TrailRenderer>();
      if (trail == null) {
        trail = Ball.AddComponent<TrailRenderer>();
      }
      trail.time = 0.22f;
      trail.startWidth = 0.28f;
      trail.endWidth = 0f;
      trail.numCapVertices = 2;
      trail.material = CreateUnlitHdrMaterial(NeonAccent, 2.2f);
      trail.startColor = new Color(NeonAccent.r, NeonAccent.g, NeonAccent.b, 0.9f);
      trail.endColor = new Color(NeonAccent.r, NeonAccent.g, NeonAccent.b, 0f);
    }

    void UpdatePaddles() {
      if (!gameStarted) {
        return;
      }

      for (int i = 0; i < players.Count; i++) {
        CirclePlayer player = players[i];
        if (!player.IsAlive) {
          continue;
        }

        if (MouseControlEnabled && i == LocalPlayerIndex && TryReadMouseAngle(out float mouseAngle)) {
          player.PaddleAngle = ClampPaddleAngle(mouseAngle, player.SectorStartAngle, player.SectorEndAngle);
        } else {
          float direction = ReadLocalDirection(i);
          player.PaddleAngle += direction * PaddleAngularSpeed * Time.deltaTime;
          player.PaddleAngle = ClampPaddleAngle(player.PaddleAngle, player.SectorStartAngle, player.SectorEndAngle);
        }
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

      // Collision : la balle a atteint le bord du cercle (ballPosition.magnitude >= rayon).
      // 1) On convertit sa position en angle pour savoir par quel SECTEUR elle sort,
      //    donc quel joueur est censé défendre.
      // 2) On mesure l'écart angulaire entre le point d'impact et le centre de la raquette.
      //    Si cet écart est dans la demi-largeur de la raquette → rebond ; sinon le joueur
      //    a raté et est éliminé.
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

    // Rebond de la balle sur une raquette, avec effet de visée.
    // - La raquette est tangente au cercle ; sa normale est radiale (paddleDirection).
    //   On réfléchit donc le vecteur vitesse par rapport à cette normale (Vector3.Reflect).
    // - "offset" ∈ [-1, 1] indique où la balle a frappé sur la raquette (centre = 0,
    //   bords = ±1). On ajoute une composante tangentielle proportionnelle → le joueur
    //   peut orienter la balle selon le point de contact (PaddleAimInfluence = dosage).
    // - Garde-fou : si la balle ne repart pas assez vers l'intérieur, on la ré-incline
    //   vers le centre pour éviter qu'elle longe le bord.
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
      Debug.Log(player.Name + " eliminated. Alive players: " + aliveCount);

      if (aliveCount <= 1) {
        CirclePlayer winner = FindLastAlivePlayer();
        winnerId = winner != null ? winner.Id : 0;
        gameOver = true;
        status = winner != null ? winner.Name + " wins!" : "No winner";

        if (Ball != null) {
          Ball.SetActive(false);
        }

        Debug.Log(status);
        return;
      }

      RedistributeAlivePlayers();
      status = player.Name + " eliminated";
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

    // Teste si un angle tombe dans un secteur [start, end], en restant robuste au
    // passage par 0°/360° : on compare l'écart à la moitié de la largeur du secteur,
    // toujours via Mathf.DeltaAngle (qui gère le wraparound), jamais par simple <=.
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

    // Borne l'angle de la raquette pour qu'elle reste ENTIÈREMENT dans son secteur.
    // On retire la demi-largeur de la raquette (paddleHalfSize) à la demi-largeur du
    // secteur : le centre de la raquette ne peut donc pas s'approcher du bord à moins
    // d'une demi-raquette, sinon elle déborderait sur le secteur voisin.
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

    // Répartition équitable du cercle entre les joueurs vivants.
    // À chaque élimination, on recalcule : le cercle (360°) est divisé en parts
    // égales (360 / nbVivants), chaque joueur reçoit un secteur centré sur
    // aliveIndex * sectorSize. Les secteurs restants s'agrandissent donc à mesure
    // que des joueurs sont éliminés. La raquette est re-bornée dans son nouveau secteur.
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
        player.SectorStartAngle = sectorCenter - sectorSize * 0.5f;
        player.SectorEndAngle = sectorCenter + sectorSize * 0.5f;
        if (player.PaddleObject != null && !player.PaddleObject.activeSelf) {
          player.PaddleObject.SetActive(true);
        }

        if (!player.HasPaddleAngle) {
          player.PaddleAngle = sectorCenter;
          player.PaddleAngleTarget = sectorCenter;
          player.HasPaddleAngle = true;
        } else {
          player.PaddleAngle = ClampPaddleAngle(player.PaddleAngle, player.SectorStartAngle, player.SectorEndAngle);
          player.PaddleAngleTarget = ClampPaddleAngle(player.PaddleAngleTarget, player.SectorStartAngle, player.SectorEndAngle);
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

    void EnsureProfiles(int count) {
      while (profiles.Count < count) {
        int index = profiles.Count;
        Color color = Color.HSVToRGB((float)index / Mathf.Max(1, MaximumPlayers), 0.75f, 1f);
        color.a = SectorAlpha;
        profiles.Add(new PlayerProfile {
          Name = "Player " + (index + 1),
          Color = color
        });
      }
    }

    public List<PlayerInfo> GetPlayerInfos() {
      List<PlayerInfo> infos = new List<PlayerInfo>();
      foreach (CirclePlayer player in players) {
        infos.Add(new PlayerInfo {
          Id = player.Id,
          Name = player.Name,
          Color = player.Color,
          IsAlive = player.IsAlive,
          Score = player.Score
        });
      }

      return infos;
    }

    public string GetPlayerName(int id) {
      foreach (CirclePlayer player in players) {
        if (player.Id == id) {
          return player.Name;
        }
      }

      return "Player " + id;
    }

    public void SetPlayerName(int index, string name) {
      if (index < 0 || index >= players.Count) {
        return;
      }

      players[index].Name = name;
    }

    public void CyclePlayerColor(int index) {
      if (index < 0 || index >= players.Count) {
        return;
      }

      CirclePlayer player = players[index];
      Color.RGBToHSV(player.Color, out float h, out float s, out float v);

      float hue = FindFreeHue(h, index);
      Color color = Color.HSVToRGB(hue, 0.75f, 1f);
      color.a = SectorAlpha;

      player.Color = color;
      ApplyPlayerColor(player);
    }

    const float MinHueDistance = 0.04f;

    // Recherche gloutonne d'une teinte (hue) libre sur le cercle chromatique :
    // on avance par pas fixe (step) jusqu'à trouver une teinte distante d'au moins
    // MinHueDistance de toutes les couleurs déjà prises, pour que les zones des
    // joueurs restent visuellement distinctes. Bornée à 32 essais pour éviter toute
    // boucle infinie ; à défaut on renvoie simplement startHue + step.
    float FindFreeHue(float startHue, int excludeIndex) {
      const float step = 0.08f;
      float hue = startHue;
      for (int attempt = 0; attempt < 32; attempt++) {
        hue = Mathf.Repeat(hue + step, 1f);
        if (IsHueFree(hue, excludeIndex)) {
          return hue;
        }
      }

      return Mathf.Repeat(startHue + step, 1f);
    }

    bool IsHueFree(float hue, int excludeIndex) {
      for (int i = 0; i < players.Count; i++) {
        if (i == excludeIndex) {
          continue;
        }

        Color.RGBToHSV(players[i].Color, out float otherHue, out float s, out float v);
        float distance = Mathf.Abs(Mathf.DeltaAngle(hue * 360f, otherHue * 360f)) / 360f;
        if (distance < MinHueDistance) {
          return false;
        }
      }

      return true;
    }

    public bool CanStart {
      get {
        return CurrentPlayerCount >= MinimumPlayers;
      }
    }

    public bool IsCountingDown {
      get {
        return countdownActive;
      }
    }

    public int CountdownSeconds {
      get {
        return Mathf.CeilToInt(Mathf.Max(0f, countdownRemaining));
      }
    }

    void ApplyPlayerColor(CirclePlayer player) {
      if (player.PaddleObject != null) {
        Renderer renderer = player.PaddleObject.GetComponent<Renderer>();
        if (renderer != null) {
          renderer.material = CreateMaterial(new Color(player.Color.r, player.Color.g, player.Color.b, 1), 1.6f);
        }
      }

      if (player.SectorObject != null) {
        Renderer renderer = player.SectorObject.GetComponent<Renderer>();
        if (renderer != null) {
          Color fill = player.Color;
          fill.a = Mathf.Max(player.Color.a, 0.5f);
          renderer.material = CreateMaterial(fill, 1.1f);
        }
      }
    }

    // Applique une couleur de zone choisie par l'utilisateur.
    public void SetPlayerColor(int index, Color color) {
      if (index < 0 || index >= players.Count) {
        return;
      }

      color.a = SectorAlpha;
      CirclePlayer player = players[index];
      player.Color = color;
      ApplyPlayerColor(player);
    }

    bool TryReadMouseAngle(out float angle) {
      angle = 0;

      Mouse mouse = Mouse.current;
      Camera camera = Camera.main;
      if (mouse == null || camera == null) {
        return false;
      }

      Vector2 screenPosition = mouse.position.ReadValue();
      Ray ray = camera.ScreenPointToRay(screenPosition);
      Plane arenaPlane = new Plane(Vector3.forward, Vector3.zero);
      if (!arenaPlane.Raycast(ray, out float distance)) {
        return false;
      }

      Vector3 world = ray.GetPoint(distance);
      if (new Vector2(world.x, world.y).sqrMagnitude < 0.0001f) {
        return false;
      }

      angle = DirectionToAngle(world);
      return true;
    }

    public struct PlayerInfo
    {
      public int Id;
      public string Name;
      public Color Color;
      public bool IsAlive;
      public int Score;
    }

    class PlayerProfile
    {
      public string Name;
      public Color Color;
    }

    class CirclePlayer
    {
      public int Id;
      public PlayerProfile Profile;
      public int Score;
      public bool IsAlive = true;
      public bool HasPaddleAngle;
      public float SectorStartAngle;
      public float SectorEndAngle;
      public float PaddleAngle;
      public float PaddleAngleTarget;   
      public GameObject SectorObject;
      public GameObject PaddleObject;

      // Identité (nom + couleur) déléguée au profil persistant :
      public string Name {
        get { return Profile.Name; }
        set { Profile.Name = value; }
      }

      public Color Color {
        get { return Profile.Color; }
        set { Profile.Color = value; }
      }
    }
}
