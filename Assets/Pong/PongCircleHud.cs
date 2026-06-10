using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>

/// </summary>
[RequireComponent(typeof(UIDocument))]
public class PongCircleHud : MonoBehaviour
{
    PongCircleLauncher launcher;
    VisualElement root;
    bool bound;

    // Panels
    VisualElement panelJoin, panelLobby, panelHud, panelWin, panelEliminated, mobileControls;

    // Join panel
    Button btnJoin, btnSpectate;
    Label joinTitle, joinSubtitle, joinYourPlayer, joinNetwork, joinPlayers, joinMin, joinHint, joinCountdown;
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

    string sigJoinDevices, sigJoinLobby, sigHudDevices, sigHudLobby;

    TextField nameField;
    VisualElement colorRow;
    string chosenName = "";
    Color chosenColor;
    int chosenColorIndex = -1;
    int lastIdentityTarget = -2;

    static readonly Color[] Palette = {
        new Color(0.345f, 0.902f, 0.784f), // turquoise
        new Color(0.961f, 0.353f, 0.408f), // rouge
        new Color(0.984f, 0.686f, 0.243f), // orange
        new Color(0.969f, 0.878f, 0.318f), // jaune
        new Color(0.486f, 0.812f, 0.380f), // vert
        new Color(0.388f, 0.616f, 0.961f), // bleu
        new Color(0.706f, 0.514f, 0.961f), // violet
        new Color(0.961f, 0.510f, 0.776f), // rose
        new Color(0.380f, 0.835f, 0.961f), // cyan
        new Color(0.741f, 0.910f, 0.376f), // citron vert
    };

    void OnEnable()
    {
        launcher = FindFirstObjectByType<PongCircleLauncher>(FindObjectsInactive.Include);
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
        panelEliminated = root.Q<VisualElement>("panel-eliminated");
        mobileControls = root.Q<VisualElement>("mobile-controls");

        // Join
        joinTitle = root.Q<Label>("join-title");
        joinSubtitle = root.Q<Label>("join-subtitle");
        joinYourPlayer = root.Q<Label>("join-yourplayer");
        joinNetwork = root.Q<Label>("join-network");
        joinPlayers = root.Q<Label>("join-players");
        joinMin = root.Q<Label>("join-min");
        joinHint = root.Q<Label>("join-hint");
        joinCountdown = root.Q<Label>("join-countdown");
        joinDevices = root.Q<VisualElement>("join-devices");
        joinLobbyDevices = root.Q<VisualElement>("join-lobby-devices");
        btnJoin = root.Q<Button>("btn-join");
        if (btnJoin != null) btnJoin.clicked += OnJoinClicked;
        btnSpectate = root.Q<Button>("btn-spectate");
        if (btnSpectate != null) btnSpectate.clicked += OnSpectateClicked;

        // Identité : nom + palette de couleurs
        nameField = root.Q<TextField>("join-name");
        colorRow = root.Q<VisualElement>("join-colors");
        LoadIdentityPrefs();
        if (nameField != null)
        {
            nameField.SetValueWithoutNotify(chosenName);
            nameField.RegisterValueChangedCallback(e =>
            {
                chosenName = e.newValue;
                SaveIdentityPrefs();
                ApplyIdentity();
                SendIdentityToServer();
            });
        }
        BuildSwatches();
        SendIdentityToServer();

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
        HideAll();
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
        bool spectator = IsSpectator();
        bool started = Game.IsGameStarted;
        bool hasWinner = Game.WinnerId > 0;

        bool showJoin = (network && !spectator && localId <= 0) || (network && !started && !hasWinner);
        bool showWin = !showJoin && hasWinner;
        bool showEliminated = !showJoin && !showWin && network && started && localId > 0 && !Game.IsLocalPlayerAlive;
        bool showLobby = !showJoin && !showWin && !showEliminated && !network && !started;
        bool showHud = !showJoin && !showWin && !showEliminated && !showLobby;

        Show(panelJoin, showJoin);
        Show(panelWin, showWin);
        Show(panelEliminated, showEliminated);
        Show(panelLobby, showLobby);
        Show(panelHud, showHud);

        bool showMobile = showHud && network && localId > 0 && ShouldShowMobileControls();
        Show(mobileControls, showMobile);

        if (showJoin) RefreshJoin(localId);
        if (showWin) RefreshWin(network);
        if (showLobby) RefreshLobby();
        if (showHud) RefreshHud(network);

        TrackIdentity();
    }

    void RefreshJoin(int localId)
    {
        SetText(btnJoin, Game.IsGameStarted ? "Rejoindre la partie" : "Jouer / Rejoindre");
        Show(btnSpectate, ShouldUseNetwork() && Game.IsGameStarted && localId <= 0 && !IsSpectator());
        SetText(joinNetwork, "Réseau : " + NetworkStatus());
        SetText(joinPlayers, "Joueurs : " + ConnectedPlayerCount() + " / " + Game.MaximumPlayers + "   Spectateurs : " + SpectatorCount());
        SetText(joinMin, "Minimum pour lancer : " + Game.MinimumPlayers);
        Show(joinMin, !Game.IsGameStarted);

        int countdown = StartCountdownSeconds();
        Show(joinCountdown, countdown > 0);
        if (countdown > 0) SetText(joinCountdown, "Démarrage dans " + countdown + " s…");

        Show(joinYourPlayer, localId > 0);
        if (localId > 0) SetText(joinYourPlayer, "Votre joueur : " + localId);

        SetText(joinHint, localId > 0
            ? "La partie se lance quand assez de joueurs ont rejoint."
            : (Game.IsGameStarted ? "Choisis joueur pour entrer dans la partie, ou spectateur pour regarder." : "Tu es dans le menu tant que tu n'as pas rejoint la partie."));

        RebuildInGameDevices(joinDevices, ref sigJoinDevices);
        RebuildLobbyDevices(joinLobbyDevices, ref sigJoinLobby);
        RefreshSwatchAvailability();
    }

    // --- Identité du joueur (nom + couleur de zone) ---
    void BuildSwatches()
    {
        if (colorRow == null) return;
        colorRow.Clear();
        for (int i = 0; i < Palette.Length; i++)
        {
            int index = i;
            Button swatch = new Button();
            swatch.AddToClassList("color-swatch");
            swatch.style.backgroundColor = Palette[i];
            swatch.clicked += () => SelectColor(index);
            colorRow.Add(swatch);
        }
        UpdateSwatchSelection();
    }

    void SelectColor(int index)
    {
        chosenColorIndex = index;
        chosenColor = Palette[index];
        SaveIdentityPrefs();
        UpdateSwatchSelection();
        ApplyIdentity();
        SendIdentityToServer();
    }

    // Propage l'identité au serveur UDP (nom + couleur hex). No-op hors réseau.
    void SendIdentityToServer()
    {
        if (!ShouldUseUdp()) return;
        string hex = chosenColorIndex >= 0 ? ColorUtility.ToHtmlStringRGB(chosenColor) : "";
        Udp.SetIdentity(chosenName, hex);
    }

    void UpdateSwatchSelection()
    {
        if (colorRow == null) return;
        int i = 0;
        foreach (VisualElement child in colorRow.Children())
        {
            child.EnableInClassList("color-swatch--selected", i == chosenColorIndex);
            i++;
        }
    }

    // Grise les couleurs déjà utilisées par d'autres joueurs (la mienne reste cliquable).
    // Double sécurité avec le refus côté serveur, et empêche le clic en amont.
    void RefreshSwatchAvailability()
    {
        if (colorRow == null) return;

        HashSet<string> taken = new HashSet<string>();
        CollectColors(taken, NetworkDevices());
        CollectColors(taken, NetworkLobbyDevices());

        int i = 0;
        foreach (VisualElement child in colorRow.Children())
        {
            string hex = ColorUtility.ToHtmlStringRGB(Palette[i]);
            bool blocked = i != chosenColorIndex && taken.Contains(hex);
            child.SetEnabled(!blocked);
            i++;
        }
    }

    static void CollectColors(HashSet<string> set, PongCircleNetworkDeviceState[] devices)
    {
        if (devices == null) return;
        foreach (PongCircleNetworkDeviceState d in devices)
        {
            if (d != null && !d.spectator && !string.IsNullOrEmpty(d.color))
            {
                set.Add(d.color.ToUpperInvariant());
            }
        }
    }

    // Cible : index du joueur local (réseau : id-1 une fois rejoint ; local : LocalPlayerIndex).
    int IdentityTargetIndex()
    {
        if (Game == null) return -1;
        if (ShouldUseNetwork())
        {
            int id = LocalPlayerId();
            return id > 0 ? id - 1 : -1;
        }
        return Game.CurrentPlayerCount > 0
            ? Mathf.Clamp(Game.LocalPlayerIndex, 0, Game.CurrentPlayerCount - 1)
            : -1;
    }

    void ApplyIdentity()
    {
        int index = IdentityTargetIndex();
        if (Game == null || index < 0 || index >= Game.CurrentPlayerCount) return;

        if (!string.IsNullOrWhiteSpace(chosenName))
        {
            Game.SetPlayerName(index, chosenName);
        }
        if (chosenColorIndex >= 0)
        {
            Game.SetPlayerColor(index, chosenColor);
        }
    }

    // Applique l'identité dès que la cible devient valide (ou change).
    void TrackIdentity()
    {
        int index = IdentityTargetIndex();
        if (index != lastIdentityTarget)
        {
            lastIdentityTarget = index;
            if (index >= 0) ApplyIdentity();
        }
    }

    void LoadIdentityPrefs()
    {
        chosenName = PlayerPrefs.GetString("pong_name", "");
        chosenColorIndex = PlayerPrefs.GetInt("pong_color_index", -1);
        if (chosenColorIndex >= 0 && chosenColorIndex < Palette.Length)
        {
            chosenColor = Palette[chosenColorIndex];
        }
        else
        {
            chosenColorIndex = -1;
        }
    }

    void SaveIdentityPrefs()
    {
        PlayerPrefs.SetString("pong_name", chosenName ?? "");
        PlayerPrefs.SetInt("pong_color_index", chosenColorIndex);
        PlayerPrefs.Save();
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
        if (network && IsSpectator()) SetText(hudStatus, "Statut : " + Game.Status + "   Mode spectateur");
        if (network) SetText(hudNetwork, "Réseau : " + NetworkStatus());

        RebuildInGameDevices(hudDevices, ref sigHudDevices);
        RebuildLobbyDevices(hudLobbyDevices, ref sigHudLobby);
    }

    void RefreshWin(bool network)
    {
        bool iWon = network && Game.WinnerId > 0 && LocalPlayerId() == Game.WinnerId;
        SetText(winTitle, iWon ? "Vous avez gagné !" : Game.GetPlayerName(Game.WinnerId) + " gagne !");
        SetText(winSubtitle, iWon ? "Dernier joueur en vie" : "Vous avez perdu");
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

    void OnSpectateClicked()
    {
        if (ShouldUseUdp()) Udp.SendSpectateGame();
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
            container.Add(MakeListLabel(
                "P" + d.playerId + " - " + d.name + "   " + d.lives + " vies   " + d.points + " pts", false));
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
            container.Add(MakeListLabel(d.name + (d.spectator ? " (spectateur)" : " (pas en jeu)"), false));
        }
    }

    static string DeviceSignature(PongCircleNetworkDeviceState[] devices, bool lobby)
    {
        if (devices == null) return "null";
        var sb = new System.Text.StringBuilder();
        foreach (PongCircleNetworkDeviceState d in devices)
        {
            if (d == null) continue;
            sb.Append(d.playerId).Append(':').Append(d.name)
              .Append(':').Append(d.lives).Append(':').Append(d.points)
              .Append(':').Append(d.spectator).Append(';');
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
    bool IsSpectator() => ShouldUseUdp() && Udp.IsSpectator;
    int ConnectedPlayerCount() => ShouldUseUdp() ? Udp.ConnectedPlayerCount : 0;
    int SpectatorCount() => ShouldUseUdp() ? Udp.SpectatorCount : 0;
    int ReplayVoteCount() => ShouldUseUdp() ? Udp.ReplayVoteCount : 0;
    int PostGameRemainingSeconds() => ShouldUseUdp() ? Udp.PostGameRemainingSeconds : 0;
    int StartCountdownSeconds() => ShouldUseUdp() ? Udp.StartCountdownSeconds : 0;
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
        Show(panelEliminated, false);
        Show(mobileControls, false);
    }
}
