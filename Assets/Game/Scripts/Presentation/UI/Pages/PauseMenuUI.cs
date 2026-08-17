using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The in-game pause screen: settings, the session's player list, and the ways out. Opened with
    /// M (also Start on a pad). Deliberately not Escape.
    /// <para>
    /// Bootstrapped from a static rather than placed in a scene. Gameplay is spread over a
    /// persistent scene, streamed world chunks and an additively loaded arena, none of which is
    /// guaranteed to be the scene a given session ends up in — a scene-authored pause menu would
    /// have to be duplicated into each one and kept in sync. One <see cref="DontDestroyOnLoad"/>
    /// listener that builds its canvas the first time it is actually opened covers all of them and
    /// costs nothing until used.
    /// </para>
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        private enum Tab
        {
            Audio,
            Video,
            Controls,
            Players,
            Dev,
        }

        private const float PanelWidth = 1280f;
        private const float PanelHeight = 780f;
        private const float RailWidth = 330f;
        private const float ContentPadding = 34f;
        private const float OpenSeconds = 0.16f;

        /// <summary>Seconds a destructive footer button stays armed after its first click.</summary>
        private const float ConfirmWindow = 3f;

        private static PauseMenuUI instance;

        public static bool IsOpen => instance != null && instance.open;

        private InputControls inputs;

        private bool open;
        private bool built;

        // Animation runs on unscaled time and drives these two, so the panel still eases in on the
        // frame the game clock is stopped.
        private float visibility;
        private CanvasGroup group;
        private RectTransform panel;

        private Tab activeTab = Tab.Audio;
        private readonly Dictionary<Tab, RectTransform> pages = new();
        private readonly Dictionary<Tab, TabWidgets> tabs = new();
        private readonly List<SettingsWidgets.Row> rows = new();

        private TextMeshProUGUI sessionLabel;
        private TextMeshProUGUI pageTitle;
        private TextMeshProUGUI menuButtonLabel;
        private ScrollRect scroll;

        private float confirmExpiry;

        private struct TabWidgets
        {
            public Image Chip;
            public Image Marker;
            public TextMeshProUGUI Label;
        }

        // ------------------------------------------------------------------ bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject("PauseMenu");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<PauseMenuUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            // Its own InputControls instance, with only the UI map live. PlayerInputManager owns
            // the player's copy and switches the whole asset off when the player hands over
            // control — which is exactly what happens when this menu opens, so sharing that
            // instance would mean the key that opened the menu stops working while it is up.
            inputs = new InputControls();
            inputs.UI.Pause.performed += _ => Toggle();
            inputs.UI.Enable();

            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;

            SceneManager.activeSceneChanged -= OnActiveSceneChanged;

            if (inputs != null)
            {
                inputs.UI.Disable();
                inputs.Dispose();
            }

            // A menu destroyed while paused would otherwise leave the game frozen with nothing
            // left able to unfreeze it.
            GameplayMenuScope.Exit(this);
        }

        // ---------------------------------------------------------------------- input

        public void Toggle()
        {
            // A name being typed owns the keyboard: M is a letter first and a shortcut second, and
            // Escape is the field's own "undo my edit".
            if (IsTypingInField()) return;

            // The artifact browser opens on top of this menu, so the pause key has to peel the top
            // screen off first. Without this it would close the menu underneath and leave the
            // browser floating over gameplay with nothing to dismiss it but I.
            if (DevInventoryUI.IsOpen)
            {
                DevInventoryUI.Hide();
                return;
            }

            if (open) Close();
            else Open();
        }

        private static bool IsTypingInField()
        {
            GameObject selected = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject
                : null;

            return selected != null
                   && selected.TryGetComponent(out TMP_InputField field)
                   && field.isFocused;
        }

        // ----------------------------------------------------------------- open/close

        /// <summary>
        /// Opens only where there is something to pause. The main menu and the lobby have no
        /// player object, and a settings screen drawn over them would sit on top of screens that
        /// already have their own navigation.
        /// </summary>
        public void Open()
        {
            if (open || !GameplayMenuScope.Enter(this)) return;

            open = true;
            confirmExpiry = 0f;

            UIBuilder.EnsureEventSystem();
            if (!built) Build();

            group.gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;

            RefreshAll();
            SelectTab(activeTab);
        }

        public void Close()
        {
            if (!open) return;

            open = false;
            group.blocksRaycasts = false;
            group.interactable = false;

            // Anything half-typed is committed by the deselect, so the name a player typed and
            // then hit M on is the name that ends up published.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            GameSettings.Save();
            GameplayMenuScope.Exit(this);
        }

        /// <summary>
        /// Teardown for when the thing being paused disappears underneath the menu — a scene load,
        /// a disconnect. Skips handing control back, because the player it took control from no
        /// longer exists.
        /// </summary>
        private void ForceClose()
        {
            if (!open) return;

            open = false;
            if (group != null)
            {
                group.blocksRaycasts = false;
                group.interactable = false;
            }

            GameplayMenuScope.Abandon();
        }

        private void OnActiveSceneChanged(Scene from, Scene to) => ForceClose();

        private void Update()
        {
            if (!built) return;

            float target = open ? 1f : 0f;
            if (!Mathf.Approximately(visibility, target))
            {
                visibility = Mathf.MoveTowards(visibility, target, Time.unscaledDeltaTime / OpenSeconds);

                float eased = visibility * visibility * (3f - 2f * visibility);
                group.alpha = eased;
                panel.localScale = Vector3.one * Mathf.Lerp(0.97f, 1f, eased);
                panel.anchoredPosition = new Vector2(0f, Mathf.Lerp(-18f, 0f, eased));

                if (visibility <= 0f) group.gameObject.SetActive(false);
            }

            if (open && confirmExpiry > 0f && Time.unscaledTime > confirmExpiry)
                DisarmConfirm();
        }

        // --------------------------------------------------------------------- build

        private void Build()
        {
            built = true;

            var canvasGo = new GameObject("PauseCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000; // above the HUD (0) and the match result screen (1000)

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            group = canvasGo.GetComponent<CanvasGroup>();
            group.alpha = 0f;

            var root = (RectTransform)canvasGo.transform;

            BuildBackdrop(root);

            panel = UIBuilder.Rect("Panel", root);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);

            UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Fill", panel)), UITheme.PanelSprite, UITheme.Panel);
            BuildPanelEdge(panel);

            BuildRail(panel);
            BuildContent(panel);
            BuildHint(root);

            visibility = 0f;
            group.gameObject.SetActive(false);
        }

        private static void BuildBackdrop(RectTransform root)
        {
            var backdrop = UIBuilder.Fill(UIBuilder.Rect("Backdrop", root));
            Image tint = UIBuilder.Solid(backdrop, UITheme.Backdrop);
            // The one raycast target that covers the screen: without it a click that misses the
            // panel falls through to whatever gameplay UI is underneath.
            tint.raycastTarget = true;

            var scanlines = UIBuilder.Fill(UIBuilder.Rect("Scanlines", root));
            Image lines = UIBuilder.Sprite(scanlines, UITheme.ScanlineSprite, new Color(1f, 1f, 1f, 0.025f),
                Image.Type.Tiled);
            lines.raycastTarget = false;

            // A wide, soft accent bloom behind the panel, so the screen has a light source rather
            // than a flat wash.
            var glow = UIBuilder.Rect("Glow", root);
            glow.anchorMin = new Vector2(0.5f, 0.5f);
            glow.anchorMax = new Vector2(0.5f, 0.5f);
            glow.sizeDelta = new Vector2(1900f, 1300f);
            UIBuilder.Sprite(glow, UITheme.GlowSprite, new Color(UITheme.Accent.r, UITheme.Accent.g,
                UITheme.Accent.b, 0.10f), Image.Type.Simple);
        }

        /// <summary>A hairline along the panel's rounded edge, which reads as a bevel.</summary>
        private static void BuildPanelEdge(RectTransform host)
        {
            var edge = UIBuilder.Fill(UIBuilder.Rect("Hairline", host));
            UIBuilder.Sprite(edge, UITheme.Edge((int)UITheme.PanelRadius), new Color(1f, 1f, 1f, 0.10f));
        }

        private void BuildRail(RectTransform host)
        {
            var rail = UIBuilder.LeftColumn(UIBuilder.Rect("Rail", host), 0f, RailWidth);

            UIBuilder.Solid(UIBuilder.RightColumn(UIBuilder.Rect("Seam", rail), 0f, 1f), UITheme.Hairline);

            // Title block
            var titleRect = UIBuilder.Rect("Title", rail);
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(0f, 76f);
            titleRect.anchoredPosition = new Vector2(0f, -46f);
            UIBuilder.Label(UIBuilder.Fill(UIBuilder.Rect("Text", titleRect), 34f, 0f, 0f, 0f), "PAUSED",
                UITheme.TitleSize, UITheme.Bright, TextAlignmentOptions.Left, FontStyles.Bold);

            var accentBar = UIBuilder.Rect("Accent", rail);
            accentBar.anchorMin = new Vector2(0f, 1f);
            accentBar.anchorMax = new Vector2(0f, 1f);
            accentBar.pivot = new Vector2(0f, 1f);
            accentBar.sizeDelta = new Vector2(56f, 4f);
            accentBar.anchoredPosition = new Vector2(34f, -128f);
            UIBuilder.Sprite(accentBar, UITheme.Rounded(2), UITheme.Accent);

            var sessionRect = UIBuilder.Rect("Session", rail);
            sessionRect.anchorMin = new Vector2(0f, 1f);
            sessionRect.anchorMax = new Vector2(1f, 1f);
            sessionRect.pivot = new Vector2(0.5f, 1f);
            sessionRect.sizeDelta = new Vector2(0f, 44f);
            sessionRect.anchoredPosition = new Vector2(0f, -146f);
            sessionLabel = UIBuilder.Label(UIBuilder.Fill(UIBuilder.Rect("Text", sessionRect), 34f, 0f, 20f, 0f),
                string.Empty, UITheme.CaptionSize, UITheme.Faint);
            sessionLabel.enableWordWrapping = true;
            sessionLabel.alignment = TextAlignmentOptions.TopLeft;

            // Tabs
            var tabColumn = UIBuilder.Rect("Tabs", rail);
            tabColumn.anchorMin = new Vector2(0f, 0f);
            tabColumn.anchorMax = new Vector2(1f, 1f);
            tabColumn.offsetMin = new Vector2(20f, 240f);
            tabColumn.offsetMax = new Vector2(-20f, -222f);
            UIBuilder.Column(tabColumn, 4f);

            AddTab(tabColumn, Tab.Audio, "Audio");
            AddTab(tabColumn, Tab.Video, "Video");
            AddTab(tabColumn, Tab.Controls, "Controls");
            AddTab(tabColumn, Tab.Players, "Players");
            AddTab(tabColumn, Tab.Dev, "Developer");

            // Footer actions
            var footer = UIBuilder.Rect("Actions", rail);
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.sizeDelta = new Vector2(-40f, 190f);
            footer.anchoredPosition = new Vector2(0f, 28f);
            var footerColumn = UIBuilder.Column(footer, 10f);
            footerColumn.childAlignment = TextAnchor.LowerCenter;

            AddFooterButton(footer, "Resume", UITheme.Accent, filled: true, Close);
            menuButtonLabel = AddFooterButton(footer, "Main Menu", UITheme.Muted, filled: false, RequestReturnToMenu);
            AddFooterButton(footer, "Quit Game", UITheme.Danger, filled: false, QuitGame);
        }

        private void AddTab(RectTransform parent, Tab tab, string label)
        {
            var row = UIBuilder.Rect(label, parent);
            UIBuilder.FixedHeight(row, 52f);

            Image chip = UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Chip", row)), UITheme.ChipSprite,
                new Color(1f, 1f, 1f, 0f));

            var markerRect = UIBuilder.Rect("Marker", row);
            markerRect.anchorMin = new Vector2(0f, 0.5f);
            markerRect.anchorMax = new Vector2(0f, 0.5f);
            markerRect.pivot = new Vector2(0f, 0.5f);
            markerRect.sizeDelta = new Vector2(3f, 22f);
            markerRect.anchoredPosition = new Vector2(6f, 0f);
            Image marker = UIBuilder.Sprite(markerRect, UITheme.Rounded(1), UITheme.Accent);

            var textRect = UIBuilder.LeftColumn(UIBuilder.Rect("Text", row), 26f, 240f);
            TextMeshProUGUI text = UIBuilder.Label(textRect, label, UITheme.TabSize, UITheme.Muted,
                TextAlignmentOptions.Left, FontStyles.Bold);

            Image hit = UIBuilder.HitArea(row);
            UIBuilder.Clickable(row, hit, new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.05f))
                .onClick.AddListener(() => SelectTab(tab));

            tabs[tab] = new TabWidgets { Chip = chip, Marker = marker, Label = text };
        }

        private TextMeshProUGUI AddFooterButton(RectTransform parent, string label, Color color, bool filled,
            UnityEngine.Events.UnityAction onClick)
        {
            var row = UIBuilder.Rect(label, parent);
            UIBuilder.FixedHeight(row, 50f);

            Image background = UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Chip", row)), UITheme.ChipSprite,
                Color.white);

            TextMeshProUGUI text = UIBuilder.LabelIn(row, "Text", label, UITheme.LabelSize,
                filled ? UITheme.Backdrop : color, TextAlignmentOptions.Center, FontStyles.Bold);

            Color normal = filled ? color : new Color(color.r, color.g, color.b, 0.10f);
            Color hovered = filled
                ? Color.Lerp(color, Color.white, 0.25f)
                : new Color(color.r, color.g, color.b, 0.24f);

            UIBuilder.Clickable(row, background, normal, hovered).onClick.AddListener(onClick);
            return text;
        }

        private void BuildContent(RectTransform host)
        {
            var content = UIBuilder.Fill(UIBuilder.Rect("Content", host), RailWidth, 0f, 0f, 0f);

            var titleRect = UIBuilder.Rect("PageTitle", content);
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(0f, 60f);
            titleRect.anchoredPosition = new Vector2(0f, -44f);
            pageTitle = UIBuilder.Label(
                UIBuilder.Fill(UIBuilder.Rect("Text", titleRect), ContentPadding, 0f, ContentPadding, 0f),
                string.Empty, UITheme.HeadingSize, UITheme.Bright, TextAlignmentOptions.Left, FontStyles.Bold);

            // A 1px rule pinned to the bottom of the title block, separating it from the page.
            UIBuilder.Solid(
                UIBuilder.Fill(UIBuilder.Rect("Rule", titleRect), ContentPadding, 0f, ContentPadding, 59f),
                UITheme.Hairline);

            // Scroll viewport
            var viewport = UIBuilder.Fill(UIBuilder.Rect("Viewport", content), ContentPadding - 8f, 30f,
                ContentPadding + 6f, 112f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var scrollContent = UIBuilder.Rect("Pages", viewport);
            scrollContent.anchorMin = new Vector2(0f, 1f);
            scrollContent.anchorMax = new Vector2(1f, 1f);
            scrollContent.pivot = new Vector2(0.5f, 1f);
            scrollContent.sizeDelta = new Vector2(0f, 0f);

            var pageColumn = UIBuilder.Column(scrollContent, 0f, new RectOffset(0, 0, 4, 24));
            pageColumn.childControlHeight = true;

            var fitter = scrollContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll = content.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = scrollContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;

            BuildAudioPage(NewPage(Tab.Audio, scrollContent));
            BuildVideoPage(NewPage(Tab.Video, scrollContent));
            BuildControlsPage(NewPage(Tab.Controls, scrollContent));
            BuildPlayersPage(NewPage(Tab.Players, scrollContent));
            BuildDevPage(NewPage(Tab.Dev, scrollContent));
        }

        /// <summary>
        /// One page per tab, all children of the scroll content and all but one inactive. An
        /// inactive child contributes nothing to a layout group's measurement, so the scroll height
        /// follows whichever page is showing without any of them needing to be rebuilt.
        /// </summary>
        private RectTransform NewPage(Tab tab, RectTransform parent)
        {
            var page = UIBuilder.Rect(tab.ToString(), parent);
            UIBuilder.Column(page, SettingsWidgets.RowSpacing);
            page.gameObject.SetActive(false);
            pages[tab] = page;
            return page;
        }

        private void BuildHint(RectTransform root)
        {
            var hint = UIBuilder.Rect("Hint", root);
            hint.anchorMin = new Vector2(0.5f, 0.5f);
            hint.anchorMax = new Vector2(0.5f, 0.5f);
            hint.pivot = new Vector2(0.5f, 1f);
            hint.sizeDelta = new Vector2(PanelWidth, 40f);
            hint.anchoredPosition = new Vector2(0f, -PanelHeight * 0.5f - 16f);

            // The label owns a child rect rather than the positioned one — filling the parent
            // would overwrite the anchors that put it under the panel.
            TextMeshProUGUI label = UIBuilder.LabelIn(hint, "Text", "M  ·  ESC   RESUME", UITheme.CaptionSize,
                UITheme.Faint, TextAlignmentOptions.Center);
            label.characterSpacing = 8f;
        }

        // --------------------------------------------------------------------- pages

        private void BuildAudioPage(RectTransform page)
        {
            SettingsWidgets.Heading(page, "Levels");

            Track(SettingsWidgets.Slider(page, "Master", 0f, 1f,
                () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, Percent, 0.01f));
            Track(SettingsWidgets.Slider(page, "Music", 0f, 1f,
                () => GameSettings.MusicVolume, v => GameSettings.MusicVolume = v, Percent, 0.01f));
            Track(SettingsWidgets.Slider(page, "Effects", 0f, 1f,
                () => GameSettings.SfxVolume, v => GameSettings.SfxVolume = v, Percent, 0.01f));
            Track(SettingsWidgets.Slider(page, "Interface", 0f, 1f,
                () => GameSettings.UiVolume, v => GameSettings.UiVolume = v, Percent, 0.01f));
            Track(SettingsWidgets.Slider(page, "Ambience", 0f, 1f,
                () => GameSettings.AmbienceVolume, v => GameSettings.AmbienceVolume = v, Percent, 0.01f));

            SettingsWidgets.Caption(page, "Levels apply live to the FMOD buses — drag one and listen.");
        }

        private void BuildVideoPage(RectTransform page)
        {
            SettingsWidgets.Heading(page, "Display");

            Track(SettingsWidgets.Cycler(page, "Resolution",
                () => GameSettings.DescribeResolution(GameSettings.ResolutionIndex),
                () => GameSettings.ResolutionIndex++,
                () => GameSettings.ResolutionIndex--));

            Track(SettingsWidgets.Toggle(page, "Fullscreen",
                () => GameSettings.Fullscreen, v => GameSettings.Fullscreen = v));

            SettingsWidgets.Heading(page, "Performance");

            Track(SettingsWidgets.Cycler(page, "Quality",
                DescribeQuality,
                () => GameSettings.QualityLevel++,
                () => GameSettings.QualityLevel--));

            Track(SettingsWidgets.Toggle(page, "VSync",
                () => GameSettings.VSync, v => GameSettings.VSync = v));

            Track(SettingsWidgets.Cycler(page, "Frame limit",
                () => GameSettings.DescribeFrameRateCap(GameSettings.FrameRateCap),
                () => StepFrameCap(1),
                () => StepFrameCap(-1)));

            SettingsWidgets.Heading(page, "Camera");

            Track(SettingsWidgets.Slider(page, "Field of view",
                GameSettings.MinFieldOfView, GameSettings.MaxFieldOfView,
                () => GameSettings.FieldOfView, v => GameSettings.FieldOfView = v,
                v => $"{Mathf.RoundToInt(v)}°", 1f));

            SettingsWidgets.Caption(page,
                Application.isEditor
                    ? "Resolution and fullscreen are ignored in the editor — the Game view sizes itself."
                    : "Frame limit is ignored while VSync is on.");
        }

        private void BuildControlsPage(RectTransform page)
        {
            SettingsWidgets.Heading(page, "Look");

            Track(SettingsWidgets.Slider(page, "Mouse sensitivity",
                GameSettings.MinSensitivity, GameSettings.MaxSensitivity,
                () => GameSettings.MouseSensitivity, v => GameSettings.MouseSensitivity = v,
                v => $"{v:0.00}x", 0.05f));

            Track(SettingsWidgets.Toggle(page, "Invert vertical look",
                () => GameSettings.InvertLookY, v => GameSettings.InvertLookY = v));

            SettingsWidgets.Heading(page, "Hotbar");

            Track(SettingsWidgets.Toggle(page, "Invert scroll direction",
                () => GameSettings.InvertHotbarScroll, v => GameSettings.InvertHotbarScroll = v));

            SettingsWidgets.Heading(page, "Bindings");

            Binding(page, "Move", "W A S D");
            Binding(page, "Jump", "Space");
            Binding(page, "Sprint", "Left Shift");
            Binding(page, "Dash", "Left Ctrl");
            Binding(page, "Interact", "E");
            Binding(page, "Use / fire", "Left Mouse");
            Binding(page, "Backpack", "B");
            Binding(page, "Hotbar", "1 – 0  ·  Scroll");
            Binding(page, "Pause", "M  ·  Esc");

            SettingsWidgets.Heading(page, "Reset");

            SettingsWidgets.Action(page, "Restore every setting to its default", "Reset All",
                ResetSettings, UITheme.Danger);
        }

        private void BuildPlayersPage(RectTransform page)
        {
            SettingsWidgets.Heading(page, "Your profile");

            Track(SettingsWidgets.TextField(page, "Display name",
                () => GameSettings.PlayerName,
                v => GameSettings.PlayerName = v,
                GameSettings.MaxNameLength,
                "Enter a callsign"));

            SettingsWidgets.Caption(page, "Changing your name updates it for everyone in the session.");

            SettingsWidgets.Heading(page, "In this session");

            PlayerListView.Create(page);
        }

        private void BuildDevPage(RectTransform page)
        {
            SettingsWidgets.Heading(page, "Developer mode");

            Track(SettingsWidgets.Toggle(page, "Enable developer mode",
                () => GameSettings.DevMode,
                v =>
                {
                    GameSettings.DevMode = v;
                    // Leaving the browser up after the switch that unlocked it was turned off
                    // would be a way to keep using a disabled tool.
                    if (!v) DevInventoryUI.Hide();
                    RefreshAll();
                }));

            SettingsWidgets.Caption(page, "Unlocks the artifact browser. Off by default, and remembered.");

            SettingsWidgets.Heading(page, "Artifact browser");

            SettingsWidgets.Action(page, "Every registered item, one click to equip", "Open Browser",
                OpenArtifactBrowser);

            SettingsWidgets.Caption(page, "Also on I during play, while developer mode is on.");

            Binding(page, "Artifact browser", "I");
        }

        /// <summary>
        /// Opens the browser on top of this menu rather than instead of it, so closing the browser
        /// comes back here. Both hold a claim on <see cref="GameplayMenuScope"/>, which is why the
        /// player stays handed over across the swap.
        /// </summary>
        private void OpenArtifactBrowser()
        {
            if (!GameSettings.DevMode)
            {
                GameSettings.DevMode = true;
                RefreshAll();
            }

            DevInventoryUI.Show();
        }

        /// <summary>A read-only "action — key" line for the bindings reference.</summary>
        private static void Binding(RectTransform page, string action, string key)
        {
            var row = UIBuilder.Rect(action, page);
            UIBuilder.FixedHeight(row, 38f);

            UIBuilder.Label(UIBuilder.LeftColumn(UIBuilder.Rect("Action", row), 20f, 320f), action,
                UITheme.LabelSize, UITheme.Muted);
            UIBuilder.Label(UIBuilder.RightColumn(UIBuilder.Rect("Key", row), 20f, 420f), key,
                UITheme.LabelSize, UITheme.Bright, TextAlignmentOptions.Right);
        }

        private void Track(SettingsWidgets.Row row) => rows.Add(row);

        // ------------------------------------------------------------------- actions

        private void SelectTab(Tab tab)
        {
            activeTab = tab;

            foreach (KeyValuePair<Tab, RectTransform> entry in pages)
                entry.Value.gameObject.SetActive(entry.Key == tab);

            foreach (KeyValuePair<Tab, TabWidgets> entry in tabs)
            {
                bool active = entry.Key == tab;
                TabWidgets widgets = entry.Value;

                widgets.Chip.color = active ? UITheme.AccentSoft : new Color(1f, 1f, 1f, 0f);
                widgets.Marker.color = active ? UITheme.Accent : new Color(1f, 1f, 1f, 0f);
                widgets.Label.color = active ? UITheme.Bright : UITheme.Muted;
            }

            pageTitle.text = TitleFor(tab);
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        private static string TitleFor(Tab tab) => tab switch
        {
            Tab.Audio => "Audio",
            Tab.Video => "Video",
            Tab.Controls => "Controls",
            Tab.Players => "Players",
            Tab.Dev => "Developer",
            _ => string.Empty,
        };

        private void RefreshAll()
        {
            foreach (SettingsWidgets.Row row in rows)
                row.Refresh();

            sessionLabel.text = DescribeSession();
        }

        private string DescribeSession()
        {
            NetworkManager manager = NetworkManager.Singleton;

            if (manager == null || !manager.IsListening)
                return "Solo session · simulation halted";

            int players = PlayerIdentity.All.Count;
            string who = manager.IsServer ? "Hosting" : "Connected";
            string clock = GameplayMenuScope.TimeFrozen ? "simulation halted" : "world still running";

            return $"{who} · {players} player{(players == 1 ? string.Empty : "s")} · {clock}";
        }

        private void ResetSettings()
        {
            GameSettings.ResetToDefaults();
            RefreshAll();
        }

        /// <summary>
        /// Leaving is armed by one click and committed by a second, because on a host it ends the
        /// session for everyone connected. Cheaper than a modal, and it cannot be dismissed by
        /// accident because it disarms itself.
        /// </summary>
        private void RequestReturnToMenu()
        {
            if (Time.unscaledTime <= confirmExpiry)
            {
                ReturnToMenu();
                return;
            }

            confirmExpiry = Time.unscaledTime + ConfirmWindow;
            menuButtonLabel.text = GameplayMenuScope.IsSoloSession()
                ? "Leave? Click again"
                : "End session? Click again";
            menuButtonLabel.color = UITheme.AccentWarm;
        }

        private void DisarmConfirm()
        {
            confirmExpiry = 0f;
            if (menuButtonLabel == null) return;

            menuButtonLabel.text = "Main Menu";
            menuButtonLabel.color = UITheme.Muted;
        }

        private void ReturnToMenu()
        {
            DisarmConfirm();

            // Abandon rather than a normal close: the player this menu took control from is about
            // to be unloaded with the scene, so there is nothing left to hand control back to. It
            // still restores the clock, because a scene that starts under a zero timescale never
            // finishes its own startup coroutines.
            ForceClose();
            GameSettings.Save();

            // The world, not just the settings. Before the shutdown below, which tears down the
            // stores the save reads from — leaving to the menu is otherwise indistinguishable
            // from losing the session.
            SaveManager.SaveOnExit();
            WorldSession.Clear();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
        }

        private void QuitGame()
        {
            GameSettings.Save();
            GameplayMenuScope.Abandon();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // -------------------------------------------------------------------- format

        private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

        private static string DescribeQuality()
        {
            string[] names = QualitySettings.names;
            if (names.Length == 0) return "—";

            return names[Mathf.Clamp(GameSettings.QualityLevel, 0, names.Length - 1)];
        }

        private static void StepFrameCap(int direction)
        {
            int[] caps = GameSettings.FrameRateCaps;
            int current = System.Array.IndexOf(caps, GameSettings.FrameRateCap);
            if (current < 0) current = 0;

            GameSettings.FrameRateCap = caps[Mathf.Clamp(current + direction, 0, caps.Length - 1)];
        }
    }
}
