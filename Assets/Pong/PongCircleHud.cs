using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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
    VisualElement panelJoin, panelLobby, panelHud, panelWin, panelEliminated, mobileControls, controlsHelp, lobbyChat, chatMessages;

    // Join panel
    Button btnJoin, btnCancelJoin, btnSpectate;
    Label joinTitle, joinSubtitle, joinYourPlayer, joinNetwork, joinPlayers, joinMin, joinHint, joinCountdown;
    VisualElement joinDevices, joinLobbyDevices, joinIdentity;

    // Local lobby panel
    Button btnLobbyStart, btnLobbyApply, btnLobbyMinus, btnLobbyPlus;
    TextField lobbyCountField;
    Toggle lobbyMouse;
    Label lobbyAllowed, lobbyStatus, lobbyControls;

    // In-game HUD panel
    Button btnRestart;
    Label hudLocalStats, hudPlayers, hudAlive, hudStatus, hudNetwork, hudRace;
    VisualElement hudDevices, hudLobbyDevices, hudPlayerBars;

    // Win panel
    Button btnReplay, btnReturn, btnEliminatedReturn;
    Label winTitle, winSubtitle, winVotes, winCountdown, winScores;

    // Mobile controls
    Button btnLeft, btnRight;
    float mobileDirection;

    // Lobby chat
    TextField chatInput;
    Button btnChatSend;
    ScrollView chatScroll;

    string sigJoinDevices, sigJoinLobby, sigHudDevices, sigHudLobby, sigChat, sigPlayerBars;

    TextField nameField;
    VisualElement colorRow;
    PongCircleIdentity identity;
    int lastIdentityTarget = -2;

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
        controlsHelp = root.Q<VisualElement>("controls-help");
        lobbyChat = root.Q<VisualElement>("lobby-chat");
        chatScroll = root.Q<ScrollView>("chat-scroll");
        chatMessages = root.Q<VisualElement>("chat-messages");
        chatInput = root.Q<TextField>("chat-input");
        btnChatSend = root.Q<Button>("btn-chat-send");
        root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
        if (btnChatSend != null) btnChatSend.clicked += OnChatSendClicked;
        if (chatInput != null) chatInput.RegisterCallback<KeyDownEvent>(OnChatKeyDown, TrickleDown.TrickleDown);
        if (chatInput != null) chatInput.RegisterValueChangedCallback(_ => UpdateChatSendButton());

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
        joinIdentity = root.Q<VisualElement>("join-identity");
        btnJoin = root.Q<Button>("btn-join");
        if (btnJoin != null) btnJoin.clicked += OnJoinClicked;
        btnCancelJoin = root.Q<Button>("btn-cancel-join");
        if (btnCancelJoin != null) btnCancelJoin.clicked += OnReturnLobbyClicked;
        btnSpectate = root.Q<Button>("btn-spectate");
        if (btnSpectate != null) btnSpectate.clicked += OnSpectateClicked;

        // Identité : nom + palette de couleurs
        nameField = root.Q<TextField>("join-name");
        colorRow = root.Q<VisualElement>("join-colors");
        identity = PongCircleIdentityStore.Load();
        if (nameField != null)
        {
            nameField.SetValueWithoutNotify(identity.Name);
            nameField.RegisterValueChangedCallback(e =>
            {
                identity = PongCircleIdentityStore.WithName(identity, e.newValue);
                PongCircleIdentityStore.Save(identity);
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
        hudLocalStats = root.Q<Label>("hud-local-stats");
        hudPlayers = root.Q<Label>("hud-players");
        hudAlive = root.Q<Label>("hud-alive");
        hudStatus = root.Q<Label>("hud-status");
        hudNetwork = root.Q<Label>("hud-network");
        hudDevices = root.Q<VisualElement>("hud-devices");
        hudLobbyDevices = root.Q<VisualElement>("hud-lobby-devices");
        btnRestart = root.Q<Button>("btn-restart");
        if (btnRestart != null) btnRestart.clicked += OnRestartClicked;

        hudRace = new Label();
        hudRace.style.position = Position.Absolute;
        hudRace.style.top = 14;
        hudRace.style.left = 0;
        hudRace.style.right = 0;
        hudRace.style.unityTextAlign = TextAnchor.UpperCenter;
        hudRace.style.fontSize = 22;
        hudRace.style.color = new StyleColor(Color.yellow);
        hudRace.style.unityFontStyleAndWeight = FontStyle.Bold;
        hudRace.visible = false;
        root.Add(hudRace);

        hudPlayerBars = new VisualElement();
        hudPlayerBars.style.position = Position.Absolute;
        hudPlayerBars.style.top = 50;
        hudPlayerBars.style.left = 0;
        hudPlayerBars.style.right = 0;
        hudPlayerBars.style.flexDirection = FlexDirection.Row;
        hudPlayerBars.style.justifyContent = Justify.Center;
        hudPlayerBars.style.alignItems = Align.Center;
        hudPlayerBars.visible = false;
        root.Add(hudPlayerBars);

        // Win
        winTitle = root.Q<Label>("win-title");
        winSubtitle = root.Q<Label>("win-subtitle");
        winVotes = root.Q<Label>("win-votes");
        winCountdown = root.Q<Label>("win-countdown");
        btnReplay = root.Q<Button>("btn-replay");
        btnReturn = root.Q<Button>("btn-return");
        btnEliminatedReturn = root.Q<Button>("btn-eliminated-return");
        if (btnReplay != null) btnReplay.clicked += OnReplayClicked;
        if (btnReturn != null) btnReturn.clicked += OnReturnLobbyClicked;
        if (btnEliminatedReturn != null) btnEliminatedReturn.clicked += OnJoinClicked;

        winScores = new Label();
        winScores.style.marginTop = 10;
        winScores.style.unityTextAlign = TextAnchor.MiddleCenter;
        winScores.style.fontSize = 16;
        winScores.style.color = new StyleColor(Color.white);
        if (panelWin != null)
        {
            int subIndex = winSubtitle != null ? panelWin.IndexOf(winSubtitle) : -1;
            if (subIndex >= 0) panelWin.Insert(subIndex + 1, winScores);
            else panelWin.Add(winScores);
        }

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

        if (IsChatFocused() && Keyboard.current != null
            && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
        {
            SendChatFromInput();
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
        Show(controlsHelp, showLobby || showHud || showWin || showEliminated);
        RefreshChat(network, showJoin, started, hasWinner);

        bool showMobile = showHud && network && localId > 0 && ShouldShowMobileControls();
        Show(mobileControls, showMobile);

        if (showJoin) RefreshJoin(localId);
        if (showWin) RefreshWin(network);
        if (showEliminated) RefreshEliminated();
        if (showLobby) RefreshLobby();
        if (showHud) RefreshHud(network);

        RefreshPlayerBars(!showJoin && (showHud || showEliminated) && network);

        TrackIdentity();
    }

    void RefreshJoin(int localId)
    {
        int countdown = StartCountdownSeconds();
        bool joinedPlayer = localId > 0;
        bool showCountdown = joinedPlayer && countdown > 0;

        Show(joinTitle, !showCountdown);
        Show(joinSubtitle, !showCountdown);
        Show(joinIdentity, !showCountdown);
        Show(btnJoin, !showCountdown && !joinedPlayer);
        Show(btnCancelJoin, joinedPlayer && !Game.IsGameStarted);
        Show(joinNetwork, !showCountdown);
        Show(joinPlayers, !showCountdown);
        Show(joinHint, !showCountdown);
        Show(joinYourPlayer, !showCountdown && joinedPlayer);
        Show(joinMin, !showCountdown && !Game.IsGameStarted);
        Show(joinCountdown, showCountdown);

        if (showCountdown)
        {
            SetText(joinCountdown, countdown.ToString());
            joinCountdown.style.fontSize = 96;
            joinCountdown.style.unityTextAlign = TextAnchor.MiddleCenter;
            return;
        }

        SetText(btnJoin, Game.IsGameStarted ? "Rejoindre la partie" : "Jouer / Rejoindre");
        Show(btnSpectate, ShouldUseNetwork() && Game.IsGameStarted && !joinedPlayer && !IsSpectator());
        SetText(joinNetwork, "Réseau : " + NetworkStatus());
        SetText(joinPlayers, "Joueurs : " + ConnectedPlayerCount() + " / " + Game.MaximumPlayers + "   Spectateurs : " + SpectatorCount());
        SetText(joinMin, "Minimum pour lancer : " + Game.MinimumPlayers);
        if (joinedPlayer) SetText(joinYourPlayer, "Votre joueur : " + localId);

        SetText(joinHint, joinedPlayer
            ? "La partie se lance quand assez de joueurs ont rejoint."
            : (Game.IsGameStarted ? "Choisis joueur pour entrer dans la partie, ou spectateur pour regarder." : Game.Status));

        // Listes "En jeu" / "Lobby" masquées dans le menu d'accueil.
        // RebuildInGameDevices(joinDevices, ref sigJoinDevices);
        // RebuildLobbyDevices(joinLobbyDevices, ref sigJoinLobby);
        RefreshSwatchAvailability();
    }

    void RefreshEliminated()
    {
        SetText(btnEliminatedReturn, "Rejoindre la partie");
    }

    // --- Identité du joueur (nom + couleur de zone) ---
    void BuildSwatches()
    {
        if (colorRow == null) return;
        colorRow.Clear();
        for (int i = 0; i < PongCircleIdentityStore.Palette.Length; i++)
        {
            int index = i;
            Button swatch = new Button();
            swatch.AddToClassList("color-swatch");
            swatch.style.backgroundColor = PongCircleIdentityStore.Palette[i];
            swatch.clicked += () => SelectColor(index);
            colorRow.Add(swatch);
        }
        UpdateSwatchSelection();
    }

    void SelectColor(int index)
    {
        identity = PongCircleIdentityStore.WithColor(identity, index);
        PongCircleIdentityStore.Save(identity);
        UpdateSwatchSelection();
        ApplyIdentity();
        SendIdentityToServer();
    }

    // Propage l'identité au serveur UDP (nom + couleur hex). No-op hors réseau.
    void SendIdentityToServer()
    {
        if (!ShouldUseUdp()) return;
        Udp.SetIdentity(identity.Name, identity.ColorHex);
    }

    void UpdateSwatchSelection()
    {
        if (colorRow == null) return;
        int i = 0;
        foreach (VisualElement child in colorRow.Children())
        {
            child.EnableInClassList("color-swatch--selected", i == identity.ColorIndex);
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
            string hex = ColorUtility.ToHtmlStringRGB(PongCircleIdentityStore.Palette[i]);
            bool blocked = i != identity.ColorIndex && taken.Contains(hex);
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

        if (!string.IsNullOrWhiteSpace(identity.Name))
        {
            Game.SetPlayerName(index, identity.Name);
        }
        if (identity.HasColor)
        {
            Game.SetPlayerColor(index, identity.Color);
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
        RefreshLocalStats();
        SetText(hudPlayers, "Joueurs : " + Game.CurrentPlayerCount);
        SetText(hudAlive, "En vie : " + Game.AlivePlayerCount);
        SetText(hudStatus, "Statut : " + Game.Status);
        Show(hudNetwork, network);
        if (network && IsSpectator()) SetText(hudStatus, "Statut : " + Game.Status + "   Mode spectateur");
        if (network) SetText(hudNetwork, "Réseau : " + NetworkStatus());

        RebuildInGameDevices(hudDevices, ref sigHudDevices);
        RebuildLobbyDevices(hudLobbyDevices, ref sigHudLobby);
        SetText(btnRestart, network ? "Retour lobby" : "Relancer");
        RefreshRace();
    }

    void RefreshLocalStats()
    {
        Show(hudLocalStats, false);
    }

    void RefreshRace()
    {
        if (hudRace == null || Game == null) return;

        if (Game.RaceActive)
        {
            int secs = Mathf.CeilToInt(Game.RaceRemainingMs / 1000f);
            hudRace.text = "COURSE ! Appuie sur SHIFT ! (" + secs + "s)";
            hudRace.visible = true;
        }
        else if (Game.RaceWinnerId > 0)
        {
            hudRace.text = Game.RaceWinnerName + " remporte la course ! +1 vie";
            hudRace.visible = true;
        }
        else
        {
            hudRace.visible = false;
        }
    }

    void RefreshPlayerBars(bool show)
    {
        if (hudPlayerBars == null) return;
        if (!show) { hudPlayerBars.visible = false; return; }

        PongCircleNetworkDeviceState[] devices = NetworkDevices();
        if (devices == null || devices.Length == 0) { hudPlayerBars.visible = false; return; }

        string sig = DeviceSignature(devices, false);
        if (sig == sigPlayerBars) { hudPlayerBars.visible = true; return; }
        sigPlayerBars = sig;
        hudPlayerBars.Clear();

        foreach (PongCircleNetworkDeviceState d in devices)
        {
            if (d == null || d.playerId <= 0) continue;

            Color c = Color.white;
            if (!string.IsNullOrEmpty(d.color)) ColorUtility.TryParseHtmlString("#" + d.color, out c);

            VisualElement bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.backgroundColor = new StyleColor(new Color(c.r, c.g, c.b, 0.20f));
            bar.style.borderTopColor = bar.style.borderBottomColor =
            bar.style.borderLeftColor = bar.style.borderRightColor = new StyleColor(c);
            bar.style.borderTopWidth = bar.style.borderBottomWidth =
            bar.style.borderLeftWidth = bar.style.borderRightWidth = 2f;
            bar.style.borderTopLeftRadius = bar.style.borderTopRightRadius =
            bar.style.borderBottomLeftRadius = bar.style.borderBottomRightRadius = 10f;
            bar.style.paddingTop = bar.style.paddingBottom = 6;
            bar.style.paddingLeft = bar.style.paddingRight = 14;
            bar.style.marginLeft = bar.style.marginRight = 6;

            Label lbl = new Label(d.lives + " ♥   " + d.points + " ★");
            lbl.style.fontSize = 22;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.color = new StyleColor(Color.white);
            bar.Add(lbl);
            hudPlayerBars.Add(bar);
        }
        hudPlayerBars.visible = true;
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
        RebuildWinScores();
    }

    void RebuildWinScores()
    {
        if (winScores == null || Game == null) return;
        var infos = Game.GetPlayerInfos();
        if (infos == null || infos.Count == 0) { winScores.text = ""; return; }

        infos.Sort((a, b) => b.Score.CompareTo(a.Score));
        var sb = new System.Text.StringBuilder();
        sb.Append("Scores :\n");
        for (int i = 0; i < infos.Count; i++)
        {
            string prefix = i == 0 ? "★ " : "  ";
            sb.Append(prefix + infos[i].Name + " — " + infos[i].Score + " pts");
            if (i < infos.Count - 1) sb.Append("\n");
        }
        winScores.text = sb.ToString();
    }

    void RefreshChat(bool network, bool showJoin, bool started, bool hasWinner)
    {
        bool show = network && showJoin && !started && !hasWinner && IsNetworkConnected();
        Show(lobbyChat, show);
        if (!show)
        {
            return;
        }

        RebuildChatMessages();
        UpdateChatSendButton();
    }

    void RebuildChatMessages()
    {
        if (chatMessages == null) return;
        PongCircleChatMessageState[] messages = NetworkChatMessages();
        string sig = ChatSignature(messages);
        if (sig == sigChat) return;
        sigChat = sig;

        chatMessages.Clear();
        if (messages == null || messages.Length == 0)
        {
            Label empty = new Label("Aucun message pour le moment.");
            empty.AddToClassList("chat-empty");
            chatMessages.Add(empty);
            return;
        }

        int start = 0;
        for (int i = start; i < messages.Length; i++)
        {
            PongCircleChatMessageState message = messages[i];
            if (message == null) continue;
            Label line = new Label((message.name ?? "Joueur") + " : " + (message.text ?? ""));
            line.AddToClassList("chat-line");
            chatMessages.Add(line);
        }

        ScrollChatToBottom();
    }

    void ScrollChatToBottom()
    {
        if (chatScroll == null || chatMessages == null || chatMessages.childCount == 0)
        {
            return;
        }

        VisualElement lastMessage = chatMessages[chatMessages.childCount - 1];
        chatScroll.schedule.Execute(() =>
        {
            chatScroll.ScrollTo(lastMessage);
        }).ExecuteLater(1);
    }

    static string ChatSignature(PongCircleChatMessageState[] messages)
    {
        if (messages == null) return "null";
        var sb = new System.Text.StringBuilder();
        foreach (PongCircleChatMessageState message in messages)
        {
            if (message == null) continue;
            sb.Append(message.id).Append(':').Append(message.name).Append(':').Append(message.text).Append(';');
        }
        return sb.ToString();
    }

    // --- Actions ---
    void OnChatSendClicked()
    {
        SendChatFromInput();
    }

    void OnChatKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
        {
            return;
        }

        SendChatFromInput();
        evt.StopPropagation();
        evt.PreventDefault();
    }

    void OnRootKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
        {
            return;
        }

        if (!IsChatFocused())
        {
            return;
        }

        SendChatFromInput();
        evt.StopPropagation();
        evt.PreventDefault();
    }

    void SendChatFromInput()
    {
        if (!ShouldUseUdp() || chatInput == null)
        {
            return;
        }

        string text = chatInput.value;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Udp.SendChatMessage(text);
        chatInput.SetValueWithoutNotify("");
        UpdateChatSendButton();
    }

    void UpdateChatSendButton()
    {
        SetEnabled(btnChatSend, chatInput != null && !string.IsNullOrWhiteSpace(chatInput.value));
    }

    bool IsChatFocused()
    {
        if (chatInput == null || chatInput.focusController == null)
        {
            return false;
        }

        Focusable focused = chatInput.focusController.focusedElement;
        if (focused == chatInput)
        {
            return true;
        }

        return focused is VisualElement element && chatInput.Contains(element);
    }

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
            if (ShouldUseUdp()) Udp.SendReturnLobby();
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

    PongCircleNetworkDeviceState FindDeviceByPlayerId(int playerId)
    {
        PongCircleNetworkDeviceState[] devices = NetworkDevices();
        if (devices == null) return null;
        foreach (PongCircleNetworkDeviceState d in devices)
        {
            if (d != null && d.playerId == playerId) return d;
        }
        return null;
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
    PongCircleChatMessageState[] NetworkChatMessages() => ShouldUseUdp() ? Udp.ChatMessages : null;

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
        Show(lobbyChat, false);
    }
}
