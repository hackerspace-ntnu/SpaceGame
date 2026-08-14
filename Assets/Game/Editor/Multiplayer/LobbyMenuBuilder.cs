// Rebuilds the multiplayer screen inside Assets/Game/Scenes/Menus/LobbyMenu.unity.
//
// Written as a builder rather than authored by hand for the reason the rest of this project's
// screens are: the old menu was seven panels toggled independently by button arrays, with the
// lobby title reached through lobbyScreen.GetChild(0) and the roster container found by name. Every
// one of those is invisible to the compiler, so the screen broke silently whenever anyone reordered
// a child. Building it from code means the wiring is reviewable, diffable, and re-runnable.
//
// The pre-July controllers are kept and driven, not replaced: LobbySystem, LobbyListSystem,
// LobbyWarningSystem, LobbyUIManager, OpenCloseUIElement, LobbyElementController,
// JoinLobbyByCodeController and JoinLobbyByPasswordButton all keep their jobs. This only builds the
// objects they point at and assigns their serialized fields.
//
// Two object names are load-bearing and must not change:
//   PlayerList                 found by name in LobbyListSystem.FindPlayerListContainer
//   JoinLobbyByPasswordPanel   fallback lookup in LobbyListSystem.ResolveJoinPrivateLobbyPanel
//
// Re-run from: Tools ▸ SpaceGame ▸ Multiplayer ▸ Rebuild Lobby Menu
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public static class LobbyMenuBuilder
    {
        private const string ScenePath = "Assets/Game/Scenes/Menus/LobbyMenu.unity";
        private const string LobbyElementPath = "Assets/Game/Prefabs/UI/LobbyMenu/LobbyElement.prefab";
        private const string PlayerElementPath = "Assets/Game/Prefabs/UI/LobbyMenu/PlayerDisplayElement.prefab";

        private const float Pad = 64f;
        private const float RowHeight = 62f;
        private const float FieldHeight = 54f;

        [MenuItem("Tools/SpaceGame/Multiplayer/Rebuild Lobby Menu")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[LobbyMenuBuilder] Exit Play mode first — building a scene during " +
                               "play mode discards the result when play mode ends.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            LobbySystem lobby = KeepManagers(scene, out LobbyListSystem list, out LobbyWarningSystem warn);
            StripOldCanvas(scene);

            RectTransform canvas = NewCanvas(scene);
            var refs = new Refs();

            BuildHeader(canvas);
            RectTransform bodies = BuildTabs(canvas, refs);
            BuildBrowse(bodies, refs);
            BuildCreate(bodies, refs);
            BuildJoinByCode(bodies, refs, lobby);
            BuildDirect(bodies, refs, lobby, warn);
            BuildLobbyScreen(canvas, refs, lobby);
            BuildStatusStrip(canvas, refs);

            Wire(list, warn, refs);
            EnsureEventSystem(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[LobbyMenuBuilder] Rebuilt LobbyMenu. Browse / Create / Join by code / Direct, " +
                      "one screen, no popups.");
        }

        /// <summary>Everything the rebuilt screen has to point back at, collected as it is created.</summary>
        private class Refs
        {
            public RectTransform BrowseBody, CreateBody, CodeBody, DirectBody;
            public Button BrowseTab, CreateTab, CodeTab, DirectTab;
            public GameObject GridContent, LobbyScreen, StartGameButton, PasswordGroup, PasswordPanel;
            public TextMeshProUGUI LobbyTitle, LobbyCode, Warning, LocalAddress;
            public TMP_InputField LobbyNameInput, PasswordInput, JoinCodeInput, JoinPasswordInput, AddressInput;
            public Toggle PrivateToggle;
            public DirectConnectController Direct;
        }

        // ───────────────────────────────────────────── managers

        /// <summary>
        /// Finds the three pre-July controllers, creating the host object only if it is missing.
        /// They are never destroyed: their serialized gameScene reference is authored data this
        /// builder must not invent.
        /// </summary>
        private static LobbySystem KeepManagers(Scene scene, out LobbyListSystem list, out LobbyWarningSystem warn)
        {
            LobbySystem lobby = Object.FindAnyObjectByType<LobbySystem>();

            if (lobby == null)
            {
                var host = new GameObject("LobbyManager");
                SceneManager.MoveGameObjectToScene(host, scene);
                lobby = host.AddComponent<LobbySystem>();

                Debug.LogWarning("[LobbyMenuBuilder] No LobbyManager found — created one. Assign its " +
                                 "gameScene field in the inspector, or Start will have nothing to load.");
            }

            list = lobby.GetComponent<LobbyListSystem>() ?? lobby.gameObject.AddComponent<LobbyListSystem>();
            warn = lobby.GetComponent<LobbyWarningSystem>() ?? lobby.gameObject.AddComponent<LobbyWarningSystem>();

            if (Object.FindAnyObjectByType<LobbyUIManager>() == null)
            {
                var ui = new GameObject("UIManager");
                SceneManager.MoveGameObjectToScene(ui, scene);
                ui.AddComponent<LobbyUIManager>();
            }

            return lobby;
        }

        /// <summary>Removes every Canvas root, leaving managers, camera, light and EventSystem.</summary>
        private static void StripOldCanvas(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<Canvas>(true) != null)
                    Object.DestroyImmediate(root);
        }

        private static RectTransform NewCanvas(Scene scene)
        {
            var go = new GameObject("LobbyMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(go, scene);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var rect = (RectTransform)go.transform;
            UIBuilder.Solid(UIBuilder.Fill(UIBuilder.Rect("Backdrop", rect)), UITheme.Backdrop);
            return rect;
        }

        // ───────────────────────────────────────────── header + tabs

        private static void BuildHeader(RectTransform canvas)
        {
            RectTransform header = Anchored(UIBuilder.Rect("Header", canvas), 0f, 1f, Pad, -Pad, 900f, 90f);
            UIBuilder.Label(header, "MULTIPLAYER", UITheme.TitleSize, UITheme.Bright);

            RectTransform back = Anchored(UIBuilder.Rect("BackButton", canvas), 1f, 1f, -Pad, -Pad, 180f, 54f);
            TextButton(back, "Back", UITheme.Muted, TextAlignmentOptions.Right);
        }

        private static RectTransform BuildTabs(RectTransform canvas, Refs refs)
        {
            RectTransform bar = Anchored(UIBuilder.Rect("Tabs", canvas), 0f, 1f, Pad, -Pad - 104f, 1000f, 48f);
            var row = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 34f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childAlignment = TextAnchor.MiddleLeft;

            refs.BrowseTab = Tab(bar, "BrowseTab", "Browse");
            refs.CreateTab = Tab(bar, "CreateTab", "Create");
            refs.CodeTab = Tab(bar, "CodeTab", "Join by code");
            refs.DirectTab = Tab(bar, "DirectTab", "Direct");

            RectTransform bodies = UIBuilder.Rect("Bodies", canvas);
            bodies.anchorMin = Vector2.zero;
            bodies.anchorMax = Vector2.one;
            bodies.offsetMin = new Vector2(Pad, Pad + 70f);
            bodies.offsetMax = new Vector2(-Pad, -Pad - 172f);
            return bodies;
        }

        private static Button Tab(RectTransform parent, string name, string label)
        {
            RectTransform rect = UIBuilder.Rect(name, parent);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = label.Length * 15f + 24f;
            element.preferredHeight = 44f;

            TextMeshProUGUI text = UIBuilder.Label(rect, label, UITheme.TabSize, UITheme.Muted);
            return UIBuilder.Clickable(rect, text, UITheme.Muted, UITheme.Accent);
        }

        /// <summary>
        /// A tab body. OpenCloseUIElement + LobbyUIManager already implement exactly this — its own
        /// tab opens it, the other three close it — so the pre-July pair drives the tab strip and no
        /// new switching code exists anywhere.
        /// </summary>
        private static RectTransform Body(RectTransform parent, string name, Refs refs, bool openByDefault)
        {
            RectTransform rect = UIBuilder.Fill(UIBuilder.Rect(name, parent));
            UIBuilder.Sprite(rect, UITheme.PanelSprite, UITheme.Panel);

            var element = rect.gameObject.AddComponent<OpenCloseUIElement>();
            var so = new SerializedObject(element);
            so.FindProperty("isOpenByDefault").boolValue = openByDefault;

            Button own = name == "BrowseBody" ? refs.BrowseTab
                : name == "CreateBody" ? refs.CreateTab
                : name == "CodeBody" ? refs.CodeTab
                : refs.DirectTab;

            SetArray(so.FindProperty("openButtons"), own);
            SetArray(so.FindProperty("closeButtons"),
                refs.BrowseTab != own ? refs.BrowseTab : null,
                refs.CreateTab != own ? refs.CreateTab : null,
                refs.CodeTab != own ? refs.CodeTab : null,
                refs.DirectTab != own ? refs.DirectTab : null);

            so.ApplyModifiedPropertiesWithoutUndo();
            return rect;
        }

        // ───────────────────────────────────────────── tab bodies

        private static void BuildBrowse(RectTransform parent, Refs refs)
        {
            refs.BrowseBody = Body(parent, "BrowseBody", refs, true);

            Heading(refs.BrowseBody, "Open sessions");

            RectTransform scroll = UIBuilder.Rect("Scroll", refs.BrowseBody);
            scroll.anchorMin = Vector2.zero;
            scroll.anchorMax = Vector2.one;
            scroll.offsetMin = new Vector2(24f, 84f);
            scroll.offsetMax = new Vector2(-24f, -76f);

            var view = scroll.gameObject.AddComponent<ScrollRect>();
            view.horizontal = false;
            view.movementType = ScrollRect.MovementType.Clamped;

            RectTransform viewport = UIBuilder.Fill(UIBuilder.Rect("Viewport", scroll));
            viewport.gameObject.AddComponent<RectMask2D>();
            view.viewport = viewport;

            RectTransform content = UIBuilder.Rect("GridContent", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            UIBuilder.Column(content, 8f);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            view.content = content;
            refs.GridContent = content.gameObject;

            RectTransform refresh = Anchored(UIBuilder.Rect("RefreshButton", refs.BrowseBody),
                1f, 0f, -24f, 24f, 190f, FieldHeight);
            Chip(refresh, "Refresh", UITheme.Accent);
        }

        private static void BuildCreate(RectTransform parent, Refs refs)
        {
            refs.CreateBody = Body(parent, "CreateBody", refs, false);
            RectTransform column = Column(refs.CreateBody, "Host a new session");

            refs.LobbyNameInput = Field(column, "LobbyNameInput", "Session name");

            RectTransform toggleRow = UIBuilder.Rect("IsPrivateInputToggle", column);
            UIBuilder.FixedHeight(toggleRow, RowHeight);
            refs.PrivateToggle = TogglePill(toggleRow, "Private (join by code only)");

            RectTransform group = UIBuilder.Rect("LobbyPassword", column);
            UIBuilder.FixedHeight(group, RowHeight);
            refs.PasswordInput = Field(group, "LobbyPasswordInput", "Password (optional)", fill: true);
            refs.PasswordGroup = group.gameObject;
            group.gameObject.SetActive(false);

            RectTransform create = UIBuilder.Rect("CreateLobbyButton", column);
            UIBuilder.FixedHeight(create, FieldHeight);
            Chip(create, "Create session", UITheme.Accent);
        }

        private static void BuildJoinByCode(RectTransform parent, Refs refs, LobbySystem lobby)
        {
            refs.CodeBody = Body(parent, "CodeBody", refs, false);
            RectTransform column = Column(refs.CodeBody, "Join with a code");

            refs.JoinCodeInput = Field(column, "JoinCodeInput", "Lobby code, e.g. ABC123");

            RectTransform join = UIBuilder.Rect("JoinByCodeButton", column);
            UIBuilder.FixedHeight(join, FieldHeight);
            Chip(join, "Join", UITheme.Accent);

            // Self-wiring, and therefore compiler-checked: it calls LobbySystem.JoinLobbyByCode
            // directly rather than through a UnityEvent resolved by string.
            var byCode = join.gameObject.AddComponent<JoinLobbyByCodeController>();
            Assign(byCode, "inputField", refs.JoinCodeInput);

            // Name is load-bearing: LobbyListSystem falls back to finding this panel by name.
            RectTransform panel = UIBuilder.Rect("JoinLobbyByPasswordPanel", column);
            UIBuilder.FixedHeight(panel, RowHeight * 2f);
            UIBuilder.Sprite(panel, UITheme.ChipSprite, UITheme.PanelRaised);
            refs.PasswordPanel = panel.gameObject;

            refs.JoinPasswordInput = Field(panel, "LobbyPasswordInput", "This lobby needs a password", fill: true);

            RectTransform confirm = Anchored(UIBuilder.Rect("JoinPrivateLobbyButton", panel),
                1f, 0f, -12f, 12f, 170f, 44f);
            Chip(confirm, "Unlock", UITheme.AccentWarm);

            var byPassword = confirm.gameObject.AddComponent<JoinLobbyByPasswordButton>();
            Assign(byPassword, "inputField", refs.JoinPasswordInput);

            panel.gameObject.SetActive(false);
        }

        private static void BuildDirect(RectTransform parent, Refs refs, LobbySystem lobby, LobbyWarningSystem warn)
        {
            refs.DirectBody = Body(parent, "DirectBody", refs, false);
            RectTransform column = Column(refs.DirectBody, "Direct connect — no Unity services");

            RectTransform mine = UIBuilder.Rect("LocalAddressRow", column);
            UIBuilder.FixedHeight(mine, RowHeight);
            refs.LocalAddress = UIBuilder.LabelIn(mine, "LocalAddressLabel", "…", UITheme.LabelSize, UITheme.Bright);

            RectTransform copy = Anchored(UIBuilder.Rect("CopyButton", mine), 1f, 0.5f, -12f, 0f, 150f, 44f);
            Chip(copy, "Copy", UITheme.Muted);

            refs.AddressInput = Field(column, "AddressInput", "Host address, e.g. 192.168.1.42");

            RectTransform hostRow = UIBuilder.Rect("DirectButtons", column);
            UIBuilder.FixedHeight(hostRow, FieldHeight);

            RectTransform host = Anchored(UIBuilder.Rect("HostButton", hostRow), 0f, 0.5f, 0f, 0f, 250f, FieldHeight);
            Chip(host, "Host on this machine", UITheme.Accent);

            RectTransform join = Anchored(UIBuilder.Rect("JoinButton", hostRow), 0f, 0.5f, 266f, 0f, 150f, FieldHeight);
            Chip(join, "Join", UITheme.Muted);

            refs.Direct = refs.DirectBody.gameObject.AddComponent<DirectConnectController>();
            Assign(refs.Direct, "localAddressLabel", refs.LocalAddress);
            Assign(refs.Direct, "addressInput", refs.AddressInput);
            Assign(refs.Direct, "warningSystem", warn);
            Assign(refs.Direct, "lobby", lobby);

            Bind(copy, refs.Direct, nameof(DirectConnectController.CopyLocalAddress));
            Bind(host, refs.Direct, nameof(DirectConnectController.HostDirect));
            Bind(join, refs.Direct, nameof(DirectConnectController.JoinDirect));
        }

        // ───────────────────────────────────────────── in-lobby screen

        private static void BuildLobbyScreen(RectTransform canvas, Refs refs, LobbySystem lobby)
        {
            RectTransform screen = UIBuilder.Rect("LobbyScreen", canvas);
            screen.anchorMin = Vector2.zero;
            screen.anchorMax = Vector2.one;
            screen.offsetMin = new Vector2(Pad, Pad + 70f);
            screen.offsetMax = new Vector2(-Pad, -Pad - 104f);
            UIBuilder.Sprite(screen, UITheme.PanelSprite, UITheme.Panel);
            refs.LobbyScreen = screen.gameObject;

            RectTransform title = Anchored(UIBuilder.Rect("LobbyScreenTitle", screen), 0f, 1f, 32f, -28f, 900f, 60f);
            refs.LobbyTitle = UIBuilder.Label(title, "Session", 42, UITheme.Bright);

            RectTransform code = Anchored(UIBuilder.Rect("LobbyScreenCode", screen), 0f, 1f, 32f, -92f, 700f, 40f);
            refs.LobbyCode = UIBuilder.Label(code, "Code: —", UITheme.LabelSize, UITheme.Accent);

            // Name is load-bearing: LobbyListSystem locates this container by name.
            RectTransform players = UIBuilder.Rect("PlayerList", screen);
            players.anchorMin = new Vector2(0f, 0f);
            players.anchorMax = new Vector2(1f, 1f);
            players.offsetMin = new Vector2(32f, 100f);
            players.offsetMax = new Vector2(-32f, -148f);
            UIBuilder.Column(players, 8f);

            RectTransform start = Anchored(UIBuilder.Rect("StartGameButton", screen), 0f, 0f, 32f, 28f, 250f, FieldHeight);
            Chip(start, "Start session", UITheme.Accent);
            refs.StartGameButton = start.gameObject;

            RectTransform leave = Anchored(UIBuilder.Rect("LeaveLobbyButton", screen), 1f, 0f, -32f, 28f, 190f, FieldHeight);
            Chip(leave, "Leave", UITheme.Danger);

            Bind(start, lobby, "StartLobbyGame");
            Bind(leave, lobby, "LeaveLobby");

            screen.gameObject.SetActive(false);
        }

        private static void BuildStatusStrip(RectTransform canvas, Refs refs)
        {
            RectTransform strip = Anchored(UIBuilder.Rect("StatusStrip", canvas), 0.5f, 0f, 0f, 26f, 1500f, 46f);
            refs.Warning = UIBuilder.Label(strip, string.Empty, UITheme.CaptionSize, UITheme.Danger,
                TextAlignmentOptions.Center);

            // The strip stays enabled and empty; LobbyWarningSystem toggles it and writes the text.
            strip.gameObject.SetActive(false);
        }

        // ───────────────────────────────────────────── wiring

        private static void Wire(LobbyListSystem list, LobbyWarningSystem warn, Refs refs)
        {
            Assign(list, "lobbyElementContainer", refs.GridContent);
            Assign(list, "lobbyElement", AssetDatabase.LoadAssetAtPath<GameObject>(LobbyElementPath));
            Assign(list, "playerDisplayElement", AssetDatabase.LoadAssetAtPath<GameObject>(PlayerElementPath));
            Assign(list, "lobbyNameInputField", refs.LobbyNameInput.textComponent);
            Assign(list, "lobbyPrivateToggle", refs.PrivateToggle);
            Assign(list, "passwordInputField", refs.PasswordInput);
            Assign(list, "lobbyPasswordObject", refs.PasswordGroup);
            Assign(list, "lobbyScreen", refs.LobbyScreen);
            Assign(list, "lobbyScreenTitle", refs.LobbyTitle);
            Assign(list, "lobbyScreenCode", refs.LobbyCode);
            Assign(list, "startGameButton", refs.StartGameButton);
            Assign(list, "joinPrivateLobbyPanel", refs.PasswordPanel);

            var warnSo = new SerializedObject(warn);
            warnSo.FindProperty("warningPanel").objectReferenceValue = refs.Warning.transform.parent.gameObject;
            warnSo.FindProperty("warningPanelErrorMessage").objectReferenceValue = refs.Warning;
            warnSo.ApplyModifiedPropertiesWithoutUndo();

            LobbySystem lobby = Object.FindAnyObjectByType<LobbySystem>();
            Bind(refs.BrowseTab.GetComponent<RectTransform>(), lobby, "listLobbies");

            Transform create = refs.CreateBody.Find("Column/CreateLobbyButton");
            if (create != null) Bind((RectTransform)create, lobby, "createLobbyWithGivenOptions");

            Transform refresh = refs.BrowseBody.Find("RefreshButton");
            if (refresh != null) Bind((RectTransform)refresh, lobby, "listLobbies");

            // Void mode against a UnityEvent<bool>: the toggle only needs to tell the view to show
            // or hide the password field, and the view reads the toggle itself.
            BindEvent(refs.PrivateToggle, "onValueChanged", list, "changeStateOfPasswordInputFieldCreateLobby");
        }

        // ───────────────────────────────────────────── primitives

        private static RectTransform Column(RectTransform body, string heading)
        {
            Heading(body, heading);

            RectTransform column = UIBuilder.Rect("Column", body);
            column.anchorMin = Vector2.zero;
            column.anchorMax = Vector2.one;
            column.offsetMin = new Vector2(24f, 24f);
            column.offsetMax = new Vector2(-24f, -76f);
            UIBuilder.Column(column, 12f);
            return column;
        }

        private static void Heading(RectTransform body, string text)
        {
            RectTransform rect = Anchored(UIBuilder.Rect("Heading", body), 0f, 1f, 24f, -20f, 900f, 44f);
            UIBuilder.Label(rect, text, UITheme.HeadingSize, UITheme.Bright);
        }

        private static TMP_InputField Field(RectTransform parent, string name, string placeholder, bool fill = false)
        {
            RectTransform rect = fill
                ? Anchored(UIBuilder.Rect(name, parent), 0f, 0.5f, 12f, 0f, 620f, FieldHeight)
                : UIBuilder.Rect(name, parent);

            if (!fill) UIBuilder.FixedHeight(rect, FieldHeight);

            UIBuilder.Sprite(rect, UITheme.ChipSprite, UITheme.PanelRaised);

            RectTransform textArea = UIBuilder.Fill(UIBuilder.Rect("Text Area", rect), 16f, 6f, 16f, 6f);
            textArea.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI text = UIBuilder.LabelIn(textArea, "Text", string.Empty, UITheme.LabelSize, UITheme.Bright);
            TextMeshProUGUI hint = UIBuilder.LabelIn(textArea, "Placeholder", placeholder, UITheme.LabelSize, UITheme.Faint);

            var field = rect.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = textArea;
            field.textComponent = text;
            field.placeholder = hint;
            field.targetGraphic = rect.GetComponent<Image>();
            field.GetComponent<Image>().raycastTarget = true;
            return field;
        }

        private static Toggle TogglePill(RectTransform rect, string label)
        {
            RectTransform box = Anchored(UIBuilder.Rect("Background", rect), 0f, 0.5f, 0f, 0f, 34f, 34f);
            Image background = UIBuilder.Sprite(box, UITheme.ChipSprite, UITheme.PanelRaised);
            background.raycastTarget = true;

            RectTransform tick = UIBuilder.Fill(UIBuilder.Rect("Checkmark", box), 7f, 7f, 7f, 7f);
            Image check = UIBuilder.Sprite(tick, UITheme.CheckSprite, UITheme.Accent, Image.Type.Simple);

            RectTransform text = Anchored(UIBuilder.Rect("Label", rect), 0f, 0.5f, 48f, 0f, 700f, 34f);
            UIBuilder.Label(text, label, UITheme.LabelSize, UITheme.Muted);

            var toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = check;
            toggle.isOn = false;
            return toggle;
        }

        private static void Chip(RectTransform rect, string label, Color tint)
        {
            UIBuilder.Sprite(rect, UITheme.ChipSprite, UITheme.AccentSoft);
            TextMeshProUGUI text = UIBuilder.LabelIn(rect, "Text", label, UITheme.LabelSize, tint,
                TextAlignmentOptions.Center);
            UIBuilder.Clickable(rect, text, tint, Color.Lerp(tint, Color.white, 0.4f));
        }

        private static void TextButton(RectTransform rect, string label, Color tint, TextAlignmentOptions align)
        {
            TextMeshProUGUI text = UIBuilder.Label(rect, label, UITheme.TabSize, tint, align);
            UIBuilder.Clickable(rect, text, tint, UITheme.Bright);
        }

        private static RectTransform Anchored(RectTransform rect, float ax, float ay, float x, float y,
            float width, float height)
        {
            rect.anchorMin = new Vector2(ax, ay);
            rect.anchorMax = new Vector2(ax, ay);
            rect.pivot = new Vector2(ax, ay);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static void Bind(RectTransform rect, Object target, string methodName)
        {
            Button button = rect != null ? rect.GetComponent<Button>() : null;
            if (button == null || target == null) return;

            BindEvent(button, "m_OnClick", target, methodName);
        }

        /// <summary>
        /// Adds a persistent call to a UnityEvent by writing its serialized form directly.
        ///
        /// UnityEventTools has no overload that takes a method NAME for a parameterless event —
        /// its typed helpers need a compile-time delegate, and the whole point of these bindings is
        /// that the scene stores the method by name. Writing the four fields Unity itself writes is
        /// the only way to author one from a script.
        ///
        /// Mode 1 is "Void" (call the method with no argument) and CallState 2 is "RuntimeOnly",
        /// which is what the inspector produces for a normal button binding.
        /// </summary>
        private static void BindEvent(Object owner, string eventField, Object target, string methodName)
        {
            var so = new SerializedObject(owner);
            SerializedProperty calls = so.FindProperty($"{eventField}.m_PersistentCalls.m_Calls");

            if (calls == null)
            {
                Debug.LogError($"[LobbyMenuBuilder] {owner.GetType().Name} has no event '{eventField}'.");
                return;
            }

            calls.arraySize = 0;
            calls.InsertArrayElementAtIndex(0);

            SerializedProperty call = calls.GetArrayElementAtIndex(0);
            call.FindPropertyRelative("m_Target").objectReferenceValue = target;
            call.FindPropertyRelative("m_MethodName").stringValue = methodName;
            call.FindPropertyRelative("m_Mode").enumValueIndex = 1;
            call.FindPropertyRelative("m_CallState").enumValueIndex = 2;

            SerializedProperty assembly = call.FindPropertyRelative("m_TargetAssemblyTypeName");
            if (assembly != null) assembly.stringValue = target.GetType().AssemblyQualifiedName;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Assign(Object component, string field, Object value)
        {
            var so = new SerializedObject(component);
            SerializedProperty property = so.FindProperty(field);

            if (property == null)
            {
                Debug.LogError($"[LobbyMenuBuilder] {component.GetType().Name} has no field '{field}'.");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(SerializedProperty array, params Button[] values)
        {
            int count = 0;
            foreach (Button b in values) if (b != null) count++;

            array.arraySize = count;

            int i = 0;
            foreach (Button b in values)
            {
                if (b == null) continue;
                array.GetArrayElementAtIndex(i++).objectReferenceValue = b;
            }
        }

        private static void EnsureEventSystem(Scene scene)
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            SceneManager.MoveGameObjectToScene(go, scene);
        }
    }
}
