using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The in-game chat: a log down the bottom left that fades away on its own, and a box you type
    /// into when you press T.
    /// <para>
    /// Bootstrapped from a static and kept across scene loads, for the same reason
    /// <see cref="PauseMenuUI"/> is: gameplay is spread over a persistent scene, streamed world
    /// chunks and an additively loaded arena, and a scene-authored chat window would have to be
    /// duplicated into each and kept in sync. The canvas is not built until the first line arrives
    /// or the box is first opened, so it costs nothing in a session where nobody talks.
    /// </para>
    /// </summary>
    public class ChatUI : MonoBehaviour
    {
        /// <summary>How long a line stays on screen when the box is shut.</summary>
        private const float VisibleSeconds = 20f;

        /// <summary>The tail of that life spent fading, rather than blinking out.</summary>
        private const float FadeSeconds = 1.5f;

        /// <summary>Lines kept on screen with the box shut. The rest are only a scroll away.</summary>
        private const int VisibleWhenClosed = 10;

        private const float PanelWidth = 660f;
        private const float LogHeight = 300f;
        private const float InputHeight = 42f;
        private const float RowGap = 8f;

        /// <summary>Left margin, and the height above the screen bottom the box sits at (clear of the hotbar).</summary>
        private const float MarginX = 28f;
        private const float MarginY = 130f;

        private const float OpenSeconds = 0.12f;

        /// <summary>Behind the pause menu (2000), in front of the HUD (0) and the match screens (1000).</summary>
        private const int SortingOrder = 1500;

        private static ChatUI instance;

        public static bool IsOpen => instance != null && instance.open;

        private InputControls inputs;

        private bool built;
        private bool open;

        /// <summary>0 shut, 1 open. Eased on the unscaled clock so it works with the game stopped.</summary>
        private float openness;

        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private GraphicRaycaster raycaster;
        private Image logBackdrop;
        private ScrollRect scroll;
        private RectTransform content;
        private RectTransform inputRow;
        private TMP_InputField field;

        private readonly List<Row> rows = new();

        private bool pinToBottom;

        /// <summary>The frame the box opened, so the keystroke that opened it can be kept out of the field.</summary>
        private int openedAtFrame = -1;
        private string seedText = string.Empty;

        private sealed class Row
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public ChatMessage Message;

            /// <summary>Last alpha written, so an unchanged row is left alone every frame.</summary>
            public float AppliedAlpha = -1f;

            public bool AppliedActive = true;
        }

        // ------------------------------------------------------------------- bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject("Chat");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ChatUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            // Its own InputControls instance with only the UI map live, exactly as PauseMenuUI
            // does: PlayerInputManager switches the player's whole asset off when control is handed
            // over — which is what opening this box does — so sharing that instance would mean the
            // key that opened the box stops working the moment it is open.
            inputs = new InputControls();
            inputs.UI.Chat.performed += _ => OnChatKey();
            inputs.UI.Enable();

            ChatLog.Added += OnMessageAdded;
            ChatLog.Cleared += OnLogCleared;

            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        /// <summary>
        /// A box left open across a scene change would keep the cursor and the player's controls
        /// on the far side of it. The log is deliberately not touched here — interiors and the
        /// minigame arena change the active scene mid-session, and walking into a building must
        /// not empty the chat. Ending the session is what clears it, in
        /// <see cref="ChatNetwork.OnDestroy"/>.
        /// </summary>
        private void OnActiveSceneChanged(Scene from, Scene to)
        {
            if (open) Close();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;

            ChatLog.Added -= OnMessageAdded;
            ChatLog.Cleared -= OnLogCleared;

            SceneManager.activeSceneChanged -= OnActiveSceneChanged;

            if (inputs != null)
            {
                inputs.UI.Disable();
                inputs.Dispose();
            }

            // A chat box destroyed while open would otherwise leave the player without their
            // cursor or their controls.
            GameplayMenuScope.Exit(this);
        }

        // ----------------------------------------------------------------------- input

        private void OnChatKey()
        {
            // T is a letter first and a shortcut second: while the box (or a settings field) has
            // the keyboard, it must type rather than toggle.
            if (open || IsTypingInField()) return;
            if (PauseMenuUI.IsOpen || DevInventoryUI.IsOpen) return;

            Open();
        }

        private static bool IsTypingInField()
        {
            GameObject selected = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject
                : null;

            return selected != null
                   && selected.TryGetComponent(out TMP_InputField typing)
                   && typing.isFocused;
        }

        // ------------------------------------------------------------------ open/close

        /// <summary>
        /// Shows the box. Refused where there is nothing to chat over — the main menu and the
        /// lobby have no player object, which is exactly the test
        /// <see cref="GameplayMenuScope.Enter(object, bool)"/> already makes.
        /// </summary>
        public void Open(string seed = "")
        {
            if (open) return;

            EnsureBuilt();

            if (!GameplayMenuScope.Enter(this, freezeTime: false)) return;

            open = true;
            openedAtFrame = Time.frameCount;
            seedText = seed ?? string.Empty;

            canvas.gameObject.SetActive(true);
            canvasGroup.blocksRaycasts = true;
            raycaster.enabled = true;
            scroll.enabled = true;
            inputRow.gameObject.SetActive(true);

            field.SetTextWithoutNotify(seedText);
            field.ActivateInputField();
            field.caretPosition = seedText.Length;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(field.gameObject);

            pinToBottom = true;
        }

        public void Close()
        {
            if (!open) return;

            open = false;

            field.SetTextWithoutNotify(string.Empty);
            field.DeactivateInputField();

            if (EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject == field.gameObject)
                EventSystem.current.SetSelectedGameObject(null);

            inputRow.gameObject.SetActive(false);
            canvasGroup.blocksRaycasts = false;
            raycaster.enabled = false;
            scroll.enabled = false;

            pinToBottom = true;

            GameplayMenuScope.Exit(this);
        }

        private void OnSubmit(string value)
        {
            ChatNetwork.Send(value);
            Close();
        }

        // ------------------------------------------------------------------------ tick

        private void Update()
        {
            if (!built) return;

            float target = open ? 1f : 0f;
            openness = OpenSeconds <= 0f
                ? target
                : Mathf.MoveTowards(openness, target, Time.unscaledDeltaTime / OpenSeconds);

            ApplyOpenness();
            RefreshRows();

            if (!open) return;

            // Polled rather than bound to UI/Cancel: the field consumes Escape for its own
            // "undo my edit", and this box wants Escape to mean "never mind" instead.
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            // Clicking the log to scroll it drops focus, and without this the box would stay open
            // with nowhere for the next keystroke to go. Not while a button is held, or the field
            // would steal the drag out from under a scroll.
            bool dragging = Mouse.current != null && Mouse.current.leftButton.isPressed;
            if (!field.isFocused && !dragging) field.ActivateInputField();
        }

        private void LateUpdate()
        {
            if (!built) return;

            // The keystroke that opened the box can still reach the field it just activated,
            // depending on the order the input module and this callback run in. Re-asserting the
            // seed for the opening frame and the one after is what keeps a stray "t" out of it.
            if (open && Time.frameCount <= openedAtFrame + 1 && field.text != seedText)
            {
                field.SetTextWithoutNotify(seedText);
                field.caretPosition = seedText.Length;
            }

            if (!pinToBottom) return;

            pinToBottom = false;
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 0f;
        }

        private void ApplyOpenness()
        {
            bool show = open || openness > 0.001f || AnyRowVisible();
            if (canvas.gameObject.activeSelf != show) canvas.gameObject.SetActive(show);

            logBackdrop.color = new Color(LogPanel.r, LogPanel.g, LogPanel.b, LogPanel.a * openness);
        }

        /// <summary>
        /// Alpha and visibility for every row, once a frame.
        /// <para>
        /// Written through a CanvasGroup rather than onto the label, because setting a
        /// TextMeshProUGUI's alpha dirties its vertex data — a hundred rows re-meshing every frame
        /// to fade two of them. Rows whose alpha has not moved are not written at all.
        /// </para>
        /// </summary>
        private void RefreshRows()
        {
            float now = Time.unscaledTime;
            int firstVisible = open ? 0 : Mathf.Max(0, rows.Count - VisibleWhenClosed);

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                float alpha;

                if (open)
                {
                    alpha = 1f;
                }
                else if (i < firstVisible)
                {
                    alpha = 0f;
                }
                else
                {
                    float age = now - row.Message.ArrivedUnscaled;
                    float remaining = VisibleSeconds - age;
                    alpha = remaining <= 0f ? 0f : Mathf.Clamp01(remaining / FadeSeconds);
                }

                bool active = alpha > 0.001f;

                // A row switched off leaves the vertical layout entirely, so the surviving lines
                // close up against the bottom instead of leaving a gap where it was.
                if (active != row.AppliedActive)
                {
                    row.Rect.gameObject.SetActive(active);
                    row.AppliedActive = active;
                }

                if (!active || Mathf.Abs(row.AppliedAlpha - alpha) < 0.004f) continue;

                row.Group.alpha = alpha;
                row.AppliedAlpha = alpha;
            }
        }

        private bool AnyRowVisible()
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].AppliedActive) return true;

            return false;
        }

        // ---------------------------------------------------------------------- the log

        private void OnMessageAdded(ChatMessage message)
        {
            // ChatLog appends before it raises this, and a build replays the whole log — so a
            // build triggered by this very message has already made its row. Adding it again here
            // is how the first line of a session ended up on screen twice.
            if (EnsureBuilt())
            {
                pinToBottom = true;
                return;
            }

            AddRow(message);

            // The log drops its oldest line at capacity; the view follows so the two stay the same
            // length and the same messages.
            while (rows.Count > ChatLog.Count) RemoveOldestRow();

            pinToBottom = true;
        }

        /// <summary>
        /// The log was dropped — which, in practice, means the session ended.
        /// <para>
        /// The canvas is switched off here rather than left to the next frame's visibility pass:
        /// <see cref="Destroy"/> does not take effect until the end of the frame, and the frame
        /// this runs on is the one the main menu appears on. Waiting even one frame is the
        /// difference between chat vanishing with the game and chat being briefly readable over
        /// the menu.
        /// </para>
        /// </summary>
        private void OnLogCleared()
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Rect != null) Destroy(rows[i].Rect.gameObject);

            rows.Clear();

            if (open) Close();

            openness = 0f;
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        private void RemoveOldestRow()
        {
            Row row = rows[0];
            rows.RemoveAt(0);
            if (row.Rect != null) Destroy(row.Rect.gameObject);
        }

        // -------------------------------------------------------------------- building

        /// <summary>Builds the canvas if it does not exist yet. True when this call is what built it.</summary>
        private bool EnsureBuilt()
        {
            if (built) return false;

            built = true;
            UIBuilder.EnsureEventSystem();
            Build();

            // Anything already said before the canvas existed — the join announcements, which
            // arrive before anyone has pressed a key.
            var history = ChatLog.Messages;
            for (int i = 0; i < history.Count; i++) AddRow(history[i]);

            pinToBottom = true;
            return true;
        }

        private void Build()
        {
            var canvasGo = new GameObject("ChatCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);

            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            UIScale.Configure(canvasGo.GetComponent<CanvasScaler>());

            canvasGroup = canvasGo.GetComponent<CanvasGroup>();
            canvasGroup.blocksRaycasts = false;

            raycaster = canvasGo.GetComponent<GraphicRaycaster>();
            raycaster.enabled = false;

            var root = UIBuilder.Rect("Root", (RectTransform)canvasGo.transform);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            root.anchoredPosition = new Vector2(MarginX, MarginY);
            root.sizeDelta = new Vector2(PanelWidth, LogHeight + RowGap + InputHeight);

            BuildLog(root);
            BuildInput(root);

            canvasGo.SetActive(false);
        }

        private void BuildLog(RectTransform root)
        {
            // Fills the root above the input row, so the log keeps the same place on screen
            // whether the box is open or shut — a log that jumped when you pressed T would make
            // the line you were reading move.
            var panel = UIBuilder.Rect("Log", root);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = new Vector2(0f, InputHeight + RowGap);
            panel.offsetMax = Vector2.zero;

            logBackdrop = UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Backdrop", panel)),
                UITheme.PanelSprite, new Color(LogPanel.r, LogPanel.g, LogPanel.b, 0f));

            var viewport = UIBuilder.Fill(UIBuilder.Rect("Viewport", panel), 10f, 8f, 10f, 8f);
            viewport.gameObject.AddComponent<RectMask2D>();

            // Anchored to the viewport's bottom edge and grown upwards by the size fitter, so a
            // log with three lines in it sits on the floor of the panel rather than hanging from
            // its ceiling — the default for a top-anchored scroll view.
            content = UIBuilder.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(1f, 0f);
            content.pivot = new Vector2(0.5f, 0f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            UIBuilder.Column(content, 3f);

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            scroll.inertia = false;
            scroll.enabled = false;
        }

        private void BuildInput(RectTransform root)
        {
            inputRow = UIBuilder.Rect("Input", root);
            inputRow.anchorMin = new Vector2(0f, 0f);
            inputRow.anchorMax = new Vector2(1f, 0f);
            inputRow.pivot = new Vector2(0.5f, 0f);
            inputRow.anchoredPosition = Vector2.zero;
            inputRow.sizeDelta = new Vector2(0f, InputHeight);

            Image background = UIBuilder.Sprite(inputRow, UITheme.ChipSprite,
                new Color(UITheme.PanelRaised.r, UITheme.PanelRaised.g, UITheme.PanelRaised.b, 0.96f));
            background.raycastTarget = true;

            // TMP_InputField clips its caret and selection to a viewport that is not the field
            // itself; without one, a long line draws outside the chip. Same shape SettingsWidgets
            // builds its name field with.
            var viewport = UIBuilder.Fill(UIBuilder.Rect("Text Area", inputRow), 14f, 4f, 14f, 4f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var text = UIBuilder.Label(UIBuilder.Fill(UIBuilder.Rect("Text", viewport)),
                string.Empty, UITheme.ValueSize, UITheme.Bright);

            var placeholder = UIBuilder.Label(UIBuilder.Fill(UIBuilder.Rect("Placeholder", viewport)),
                "Say something…  (/help for commands)", UITheme.ValueSize, UITheme.Faint);
            placeholder.fontStyle = FontStyles.Italic;

            field = inputRow.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = viewport;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.targetGraphic = background;
            field.characterLimit = ChatText.MaxCharacters;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.caretColor = UITheme.Accent;
            field.customCaretColor = true;
            field.selectionColor = UITheme.AccentSoft;

            // Escape is handled here, not by the field: it should abandon the whole box rather
            // than put back what was in it.
            field.restoreOriginalTextOnEscape = false;

            field.onSubmit.AddListener(OnSubmit);

            inputRow.gameObject.SetActive(false);
        }

        // ----------------------------------------------------------------------- a line

        private void AddRow(ChatMessage message)
        {
            var rect = UIBuilder.Rect("Line", content);

            // A strip behind every line, not just when the box is open: with the box shut there is
            // no panel behind the log, and light text over bright desert is unreadable.
            UIBuilder.Sprite(rect, UITheme.ChipSprite, new Color(0f, 0f, 0f, 0.42f));

            var group = rect.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // The row sizes itself to its text plus padding. It carries a layout group and no
            // LayoutElement, deliberately: a rect with both reports two competing preferred
            // heights at the same priority and silently inflates.
            var pad = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            pad.padding = new RectOffset(10, 10, 4, 4);
            pad.childControlWidth = true;
            pad.childControlHeight = true;
            pad.childForceExpandWidth = true;
            pad.childForceExpandHeight = false;

            var label = UIBuilder.Label(UIBuilder.Rect("Text", rect), Compose(message),
                VisorStyle.BodySize, BodyColor(message));
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.richText = true;

            rows.Add(new Row { Rect = rect, Group = group, Message = message });
        }

        /// <summary>
        /// The line as rich text.
        /// <para>
        /// Both the name and the message body go inside <c>&lt;noparse&gt;</c>, so markup a player
        /// types shows up as the characters they typed instead of resizing and recolouring
        /// everyone else's screen. <see cref="ChatText.Sanitize"/> has already broken any closing
        /// tag that would end the block early — the two halves only work together.
        /// </para>
        /// </summary>
        private static string Compose(ChatMessage message)
        {
            if (!message.HasSender) return $"<noparse>{message.Text}</noparse>";

            string hex = ColorUtility.ToHtmlStringRGB(NameColor(message.Sender));
            return $"<color=#{hex}><noparse>{message.Sender}</noparse></color>  <noparse>{message.Text}</noparse>";
        }

        /// <summary>
        /// The log's own backdrop: near-black carrying a trace of the visor's ink, rather than
        /// UITheme's menu navy. Chat is drawn on the helmet glass now, not in a menu.
        /// </summary>
        private static readonly Color LogPanel = new(0.02f, 0.045f, 0.06f, 0.9f);

        /// <summary>
        /// Chat sits one step quieter than the system channel: the suit's own messages are the
        /// thing you must read, and another player saying "heading east" is not.
        /// </summary>
        private static Color BodyColor(ChatMessage message) => message.Kind switch
        {
            ChatKind.System => VisorStyle.InkFaint,
            ChatKind.Notice => VisorStyle.Alarm,
            _ => VisorStyle.InkDim,
        };

        /// <summary>
        /// A stable colour per name, so you learn who is talking without reading the name.
        /// <para>
        /// Hashed here rather than with <see cref="string.GetHashCode"/>, which .NET is free to
        /// randomise per process — that would give the same player a different colour on every
        /// machine in the session, and a different one again next launch.
        /// </para>
        /// </summary>
        private static Color NameColor(string name)
        {
            unchecked
            {
                const uint offset = 2166136261;
                const uint prime = 16777619;

                uint hash = offset;
                for (int i = 0; i < name.Length; i++)
                {
                    hash ^= char.ToLowerInvariant(name[i]);
                    hash *= prime;
                }

                // Constrained to the cool band — cyan through blue to violet — rather than the
                // whole hue wheel. On the visor, warm means danger and nothing else: a player
                // whose name hashed to orange would read as an alarm every time they spoke.
                // 110 degrees is still plenty to tell a lobby of people apart by colour alone.
                const float bandStart = 170f / 360f;
                const float bandWidth = 110f / 360f;

                float hue = bandStart + (hash % 1000u / 1000f * bandWidth);

                // Light and not too saturated: these sit on a dark strip and have to stay legible
                // beside the body text.
                return Color.HSVToRGB(hue, 0.45f, 1f);
            }
        }
    }
}
