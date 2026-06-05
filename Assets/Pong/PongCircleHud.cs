using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// HUD du Circle Pong en UI Toolkit (remplace l'UI IMGUI de PongCircleLauncher).
///
/// Ne touche pas au réseau : ce contrôleur LIT l'état public (PongCircleGame +
/// PongCircleUdpClient via le launcher) chaque frame et APPELLE les mêmes actions.
/// Il masque l'ancienne IMGUI via PongCircleLauncher.HideImguiUi.
///
/// 5 écrans, pilotés par visibilité :
///   - join   : menu réseau (rejoindre / lancer) + statut + devices
///   - lobby  : lobby local hors-réseau (+/- joueurs, souris)
///   - hud    : en jeu (joueurs, statut, restart)
///   - win    : écran de victoire (replay / retour lobby)
///   - mobile : flèches tactiles ←/→ pendant la partie
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class PongCircleHud : MonoBehaviour
{
    PongCircleLauncher launcher;
    VisualElement root;
    bool bound;

    // Panels
    VisualElement panelJoin, panelLobby, panelHud, panelWin, mobileControls;

    // Join panel
    Button btnJoin;
    Label joinTitle, joinSubtitle, joinYourPlayer, joinNetwork, joinPlayers, joinMin, joinHint;
    VisualElement joinDevices, joinLobbyDevices;

    // Local lobby panel
    Button btnLobbyStart, btnLobbyApply, btnLobbyMinus, btnLobbyPlus;
    TextField lobbyCountField;
    Toggle lobbyMouse;
    Label lobbyAllowed, lobbyStatus, lobbyControls;

    // In-game HUD panel
    Button btnRestart;
    Label hudPlayers, hudAlive, hudStatus, hudNetwork;
    VisualElement hudDevices, hudLobbyDevices;

    // Win panel
    Button btnReplay, btnReturn;
    Label winTitle, winSubtitle, winVotes, winCountdown;

    // Mobile controls
    Button btnLeft, btnRight;
    float mobileDirection;

    // Caches pour éviter de reconstruire les listes chaque frame
    string sigJoinDevices, sigJoinLobby, sigHudDevices, sigHudLobby;

    void OnEnable()
    {
        launcher = FindFirstObjectByType<PongCircleLauncher>(FindObjectsInactive.Include);
        if (launcher != null)
        {
            launcher.HideImguiUi = true;
        }
    }

    void Start()
    {
        Bind();
    }

    void Bind()
    {
        UIDocument document = GetComponent<UIDocument>();
        root = document != null ? document.rootVisualElement : null;
        if (root == null)
        {
            return;
        }

        panelJoin = root.Q<VisualElement>("panel-join");
        panelLobby = root.Q<VisualElement>("panel-lobby");
        panelHud = root.Q<VisualElement>("panel-hud");
        panelWin = root.Q<VisualElement>("panel-win");
        mobileControls = root.Q<VisualElement>("mobile-controls");

        // Join
        joinTitle = root.Q<Label>("join-title");
        joinSubtitle = root.Q<Label>("join-subtitle");
        joinYourPlayer = root.Q<Label>("join-yourplayer");
        joinNetwork = root.Q<Label>("join-network");
        joinPlayers = root.Q<Label>("join-players");
        joinMin = root.Q<Label>("join-min");
        joinHint = root.Q<Label>("join-hint");
        joinDevices = root.Q<VisualElement>("join-devices");
        joinLobbyDevices = root.Q<VisualElement>("join-lobby-devices");
        btnJoin = root.Q<Button>("btn-join");
        if (btnJoin != null) btnJoin.clicked += OnJoinClicked;

        // Local lobby
        btnLobbyStart = root.Q<Button>("btn-lobby-start");
        btnLobbyApply = root.Q<Button>("btn-lobby-apply");
        btnLobbyMinus = root.Q<Button>("btn-lobby-minus");
        btnLobbyPlus = root.Q<Button>("btn-lobby-plus");
        lobbyCountField = root.Q<TextField>("lobby-count-field");
        lobbyMouse = root.Q<Toggle>("lobby-mouse");
        lobbyAllowed = root.Q<Label>("lobby-allowed");
        lobbyStatus = root.Q<Label>("lobby-status");
        lobbyControls = root.Q<Label>("lobby-controls");
        if (btnLobbyStart != null) btnLobbyStart.clicked += OnLocalStartClicked;
        if (btnLobbyApply != null) btnLobbyApply.clicked += OnApplyCountClicked;
        if (btnLobbyMinus != null) btnLobbyMinus.clicked += () => { if (Game != null) Game.RemovePlayer(); SyncCountField(); };
        if (btnLobbyPlus != null) btnLobbyPlus.clicked += () => { if (Game != null) Game.AddPlayer(); SyncCountField(); };
        if (lobbyMouse != null) lobbyMouse.RegisterValueChangedCallback(e => { if (Game != null) Game.MouseControlEnabled = e.newValue; });

        // HUD
        hudPlayers = root.Q<Label>("hud-players");
        hudAlive = root.Q<Label>("hud-alive");
        hudStatus = root.Q<Label>("hud-status");
        hudNetwork = root.Q<Label>("hud-network");
        hudDevices = root.Q<VisualElement>("hud-devices");
        hudLobbyDevices = root.Q<VisualElement>("hud-lobby-devices");
        btnRestart = root.Q<Button>("btn-restart");
        if (btnRestart != null) btnRestart.clicked += OnRestartClicked;

        // Win
        winTitle = root.Q<Label>("win-title");
        winSubtitle = root.Q<Label>("win-subtitle");
        winVotes = root.Q<Label>("win-votes");
        winCountdown = root.Q<Label>("win-countdown");
        btnReplay = root.Q<Button>("btn-replay");
        btnReturn = root.Q<Button>("btn-return");
        if (btnReplay != null) btnReplay.clicked += OnReplayClicked;
        if (btnReturn != null) btnReturn.clicked += OnReturnLobbyClicked;

        // Mobile controls (maintien enfoncé)
        btnLeft = root.Q<Button>("btn-left");
        btnRight = root.Q<Button>("btn-right");
        RegisterHold(btnLeft, -1);
        RegisterHold(btnRight, 1);

        SyncCountField();
        bound = true;
    }

    void RegisterHold(Button button, float direction)
    {
        if (button == null) return;
        button.RegisterCallback<PointerDownEvent>(_ => mobileDirection = direction);
        button.RegisterCallback<PointerUpEvent>(_ => mobileDirection = 0);
        button.RegisterCallback<PointerLeaveEvent>(_ => mobileDirection = 0);
    }

    void Update()
    {
        if (!bound)
        {
            Bind();
            if (!bound) return;
        }

        if (launcher == null || Game == null)
        {
            HideAll();
            return;
        }

        // Maintien des flèches tactiles
        if (Mathf.Abs(mobileDirection) > 0 && ShouldUseUdp())
        {
            Udp.SetOnScreenDirection(mobileDirection);
        }

        Refresh();
    }

    // --- Machine à états (reproduit l'ordre de PongCircleLauncher.OnGUI) ---
    void Refresh()
    {
        bool network = ShouldUseNetwork();
        int localId = LocalPlayerId();
        bool started = Game.IsGameStarted;
        bool hasWinner = Game.WinnerId > 0;

        bool showJoin = (network && localId <= 0) || (network && !started && !hasWinner);
        bool showWin = !showJoin && hasWinner;
        bool showLobby = !showJoin && !showWin && !network && !started;
        bool showHud = !showJoin && !showWin && !showLobby;

        Show(panelJoin, showJoin);
        Show(panelWin, showWin);
        Show(panelLobby, showLobby);
        Show(panelHud, showHud);

        bool showMobile = showHud && network && localId > 0 && ShouldShowMobileControls();
        Show(mobileControls, showMobile);

        if (showJoin) RefreshJoin(localId);
        if (showWin) RefreshWin(network);
        if (showLobby) RefreshLobby();
        if (showHud) RefreshHud(network);
    }

    void RefreshJoin(int localId)
    {
        SetText(btnJoin, Game.IsGameStarted ? "Rejoindre la partie" : "Jouer / Rejoindre");
        SetText(joinNetwork, "Réseau : " + NetworkStatus());
        SetText(joinPlayers, "Joueurs : " + ConnectedPlayerCount() + " / " + Game.MaximumPlayers);
        SetText(joinMin, "Minimum pour lancer : " + Game.MinimumPlayers);
        Show(joinMin, !Game.IsGameStarted);

        Show(joinYourPlayer, localId > 0);
        if (localId > 0) SetText(joinYourPlayer, "Votre joueur : " + localId);

        SetText(joinHint, localId > 0
            ? "La partie se lance quand assez de joueurs ont rejoint."
            : "Tu es dans le menu tant que tu n'as pas rejoint la partie.");

        RebuildInGameDevices(joinDevices, ref sigJoinDevices);
        RebuildLobbyDevices(joinLobbyDevices, ref sigJoinLobby);
    }

    void RefreshLobby()
    {
        SetEnabled(btnLobbyMinus, Game.CurrentPlayerCount > Game.MinimumPlayers);
        SetEnabled(btnLobbyPlus, Game.CurrentPlayerCount < Game.MaximumPlayers);
        SetText(lobbyAllowed, "Autorisé : " + Game.MinimumPlayers + " à " + Game.MaximumPlayers);
        SetText(lobbyStatus, "Joueurs : " + Game.CurrentPlayerCount + "   Statut : " + Game.Status);
        if (lobbyMouse != null && lobbyMouse.value != Game.MouseControlEnabled)
        {
            lobbyMouse.SetValueWithoutNotify(Game.MouseControlEnabled);
        }
        SetText(lobbyControls, "Contrôles : P1 Z/S   P2 ↑/↓   P3 T/G   P4 I/K …");
    }

    void RefreshHud(bool network)
    {
        SetText(hudPlayers, "Joueurs : " + Game.CurrentPlayerCount);
        SetText(hudAlive, "En vie : " + Game.AlivePlayerCount);
        SetText(hudStatus, "Statut : " + Game.Status);
        Show(hudNetwork, network);
        if (network) SetText(hudNetwork, "Réseau : " + NetworkStatus());

        RebuildInGameDevices(hudDevices, ref sigHudDevices);
        RebuildLobbyDevices(hudLobbyDevices, ref sigHudLobby);
    }

    void RefreshWin(bool network)
    {
        SetText(winTitle, Game.GetPlayerName(Game.WinnerId) + " gagne !");
        SetText(winSubtitle, "Dernier joueur en vie");
        Show(winVotes, network);
        Show(winCountdown, network);
        Show(btnReturn, network);
        if (network)
        {
            SetText(winVotes, "Votes replay : " + ReplayVoteCount() + " / " + Game.MinimumPlayers);
            SetText(winCountdown, "Prochaine action dans : " + PostGameRemainingSeconds() + "s");
        }
    }

    // --- Actions ---
    void OnJoinClicked()
    {
        if (ShouldUseUdp()) Udp.SendStartGame();
        else if (Game != null) Game.StartGame();
    }

    void OnLocalStartClicked()
    {
        if (ShouldUseUdp()) Udp.SendStartGame();
        else if (Game != null) Game.StartGame();
    }

    void OnRestartClicked()
    {
        if (ShouldUseNetwork())
        {
            if (!IsNetworkConnected())
            {
                if (ShouldUseUdp()) Udp.Connect();
                return;
            }
            if (ShouldUseUdp()) Udp.SendRestartLobby();
        }
        else if (Game != null)
        {
            Game.Replay();
        }
        SyncCountField();
    }

    void OnReplayClicked()
    {
        if (ShouldUseUdp()) Udp.SendReplayVote();
        else if (Game != null) { Game.Replay(); SyncCountField(); }
    }

    void OnReturnLobbyClicked()
    {
        if (ShouldUseUdp()) Udp.SendReturnLobby();
    }

    void OnApplyCountClicked()
    {
        if (Game == null || lobbyCountField == null) return;
        if (int.TryParse(lobbyCountField.value, out int count))
        {
            Game.SetPlayerCount(count);
        }
        SyncCountField();
    }

    void SyncCountField()
    {
        if (lobbyCountField != null && Game != null)
        {
            lobbyCountField.SetValueWithoutNotify(Game.CurrentPlayerCount.ToString());
        }
    }

    // --- Reconstruction des listes de devices ---
    void RebuildInGameDevices(VisualElement container, ref string signature)
    {
        if (container == null) return;
        PongCircleNetworkDeviceState[] devices = NetworkDevices();
        string sig = DeviceSignature(devices, false);
        if (sig == signature) return;
        signature = sig;

        container.Clear();
        if (devices == null) return;
        container.Add(MakeListLabel("En jeu :", true));
        foreach (PongCircleNetworkDeviceState d in devices)
        {
            if (d == null || d.playerId <= 0) continue;
            container.Add(MakeListLabel("P" + d.playerId + " - " + d.name, false));
        }
    }

    void RebuildLobbyDevices(VisualElement container, ref string signature)
    {
        if (container == null) return;
        PongCircleNetworkDeviceState[] devices = NetworkLobbyDevices();
        string sig = DeviceSignature(devices, true);
        if (sig == signature) return;
        signature = sig;

        container.Clear();
        if (devices == null || devices.Length == 0) return;
        container.Add(MakeListLabel("Lobby :", true));
        foreach (PongCircleNetworkDeviceState d in devices)
        {
            if (d == null) continue;
            container.Add(MakeListLabel(d.name + " (pas en jeu)", false));
        }
    }

    static string DeviceSignature(PongCircleNetworkDeviceState[] devices, bool lobby)
    {
        if (devices == null) return "null";
        var sb = new System.Text.StringBuilder();
        foreach (PongCircleNetworkDeviceState d in devices)
        {
            if (d == null) continue;
            sb.Append(d.playerId).Append(':').Append(d.name).Append(';');
        }
        return sb.ToString();
    }

    Label MakeListLabel(string text, bool header)
    {
        Label label = new Label(text);
        label.AddToClassList(header ? "device-header" : "device-item");
        return label;
    }

    // --- Accès état (via le launcher, sans toucher au réseau) ---
    PongCircleGame Game => launcher != null ? launcher.CircleGame : null;
    PongCircleUdpClient Udp => launcher != null ? launcher.UdpClient : null;
    bool ShouldUseUdp() => launcher != null && launcher.EnableUdpSync && launcher.UdpClient != null;
    bool ShouldUseNetwork() => ShouldUseUdp();
    int LocalPlayerId() => ShouldUseUdp() ? Udp.LocalPlayerId : 0;
    int ConnectedPlayerCount() => ShouldUseUdp() ? Udp.ConnectedPlayerCount : 0;
    int ReplayVoteCount() => ShouldUseUdp() ? Udp.ReplayVoteCount : 0;
    int PostGameRemainingSeconds() => ShouldUseUdp() ? Udp.PostGameRemainingSeconds : 0;
    bool IsNetworkConnected() => ShouldUseUdp() && Udp.IsConnected;
    bool ShouldShowMobileControls() => ShouldUseUdp() && Udp.ShouldShowMobileControls();
    string NetworkStatus() => ShouldUseUdp() ? Udp.LastStatus : "Hors ligne";
    PongCircleNetworkDeviceState[] NetworkDevices() => ShouldUseUdp() ? Udp.Devices : null;
    PongCircleNetworkDeviceState[] NetworkLobbyDevices() => ShouldUseUdp() ? Udp.LobbyDevices : null;

    // --- Helpers UI ---
    static void Show(VisualElement element, bool show)
    {
        if (element != null) element.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }

    static void SetText(TextElement element, string text)
    {
        if (element != null && element.text != text) element.text = text;
    }

    static void SetEnabled(VisualElement element, bool enabled)
    {
        if (element != null) element.SetEnabled(enabled);
    }

    void HideAll()
    {
        Show(panelJoin, false);
        Show(panelLobby, false);
        Show(panelHud, false);
        Show(panelWin, false);
        Show(mobileControls, false);
    }
}
