using System.Collections.Generic;
using SpaceGame.Characters;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The emote wheel: hold V (d-pad down), point at an emote, let go to do it.
    ///
    /// <para>
    /// Eight wedges round the screen centre, one page of the <see cref="EmoteCatalog"/> at a time —
    /// a page per category, in catalog order, spilling onto another page past eight. The mouse, or
    /// the right stick, picks the wedge it points into; releasing the key plays that emote.
    /// Releasing in the dead centre, or Escape, cancels. Q / E, the shoulders or the scroll wheel
    /// turn the page, and the page is remembered for next time.
    /// </para>
    /// <para>
    /// <b>Multiplayer.</b> The wheel exists on every peer but only ever drives the local player,
    /// and the emote is played straight onto the owner's body through
    /// <see cref="PlayerEmotes.PlayAsOwner"/> — no request to the server, so the gesture starts on
    /// the frame the key comes up (<c>GDC-L1-FEEL-0002</c>). The body's NetworkAnimator carries it
    /// to everyone else, exactly as it does for a chat-typed emote. The wheel does not post the
    /// emote's chat line: that would need a server round trip for a line the people who can see
    /// the gesture do not need, and a wheel used every few seconds would flood the log. Typing the
    /// command still announces it.
    /// </para>
    /// <para>
    /// <b>Persistence:</b> none. The remembered page lasts for the session only.
    /// </para>
    /// <para>
    /// Bootstrapped from a static and kept across scene loads, like <see cref="ChatUI"/>; the canvas
    /// is built on first open. It has no raycaster: nothing on it is clicked.
    /// </para>
    /// </summary>
    public class EmoteWheelUI : MonoBehaviour
    {
        private const int Wedges = 8;

        private const float OuterRadius = 250f;
        private const float HubRadius = 88f;
        private const float SlotRadius = 170f;
        private const float SlotWidth = 124f;
        private const float SlotLabelHeight = 30f;
        private const float IconSize = 44f;
        private const float PointerSize = 14f;
        private const float DividerWidth = 2f;
        private const float HubLabelWidth = 160f;

        /// <summary>Distance from the hub's centre line (the emote's name) to the lines above and below it.</summary>
        private const float HubLineSpacing = 36f;

        /// <summary>Label rect height as a multiple of its font size.</summary>
        private const float LineHeight = 1.4f;
        private const float HintWidth = 900f;
        private const float HintGap = 40f;

        /// <summary>The soft dark bloom behind the wheel, as a multiple of the wheel's own size.</summary>
        private const float GlowScale = 1.5f;

        /// <summary>The wheel grows from this to full size as it fades in.</summary>
        private const float ClosedScale = 0.92f;

        private const float DiscAlpha = 0.88f;
        private const float HighlightAlpha = 0.30f;

        /// <summary>In front of the HUD (0) and world prompts (50), behind menu pages (900) and chat (1500).</summary>
        private const int SortingOrder = 800;

        private const string UncategorisedPage = "Emotes";
        private const string HintOnePage = "Release to play  ·  centre or Esc to cancel";
        private const string HintPages = "Release to play  ·  Q / E or scroll for more  ·  centre or Esc to cancel";

        [Tooltip("Seconds the wheel takes to fade in or out, on the unscaled clock.")]
        [SerializeField, Min(0f)] private float fadeSeconds = 0.08f;

        [Tooltip("Radius, in reference pixels round the screen centre, in which the mouse selects " +
                 "nothing. Releasing there cancels.")]
        [SerializeField, Min(0f)] private float pointerDeadZone = 48f;

        [Tooltip("How far the stick must be pushed, 0..1, before it picks a wedge.")]
        [SerializeField, Range(0f, 1f)] private float stickDeadZone = 0.5f;

        [Tooltip("Minimum seconds between page turns, so a trackpad's stream of small scrolls turns " +
                 "one page rather than all of them.")]
        [SerializeField, Min(0f)] private float pageRepeatSeconds = 0.15f;

        [Tooltip("Scale of the highlighted emote's label and icon.")]
        [SerializeField, Min(1f)] private float hoverScale = 1.12f;

        private static EmoteWheelUI instance;

        private InputControls inputs;

        private bool built;
        private bool open;

        /// <summary>
        /// Set when the wheel is cancelled; the close itself waits for <see cref="LateUpdate"/>.
        /// Escape is also the dismount key, and anything reading it in its own Update this frame
        /// must still find the controls taken.
        /// </summary>
        private bool cancelRequested;

        /// <summary>0 shut, 1 open. Eased on the unscaled clock so it works with the game stopped.</summary>
        private float openness;

        private float nextPageTime;

        private PlayerController player;
        private PlayerEmotes emotes;
        private EmoteCatalog catalog;
        private List<EmoteWheelLayout.Page> pages = new();
        private int page;
        private int hoveredWedge = -1;

        /// <summary>The catalog index shown in each wedge of the current page, -1 for an empty wedge.</summary>
        private readonly int[] entryInWedge = new int[Wedges];

        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private RectTransform wheel;
        private Image highlight;
        private RectTransform pointer;
        private TextMeshProUGUI pageLabel;
        private TextMeshProUGUI nameLabel;
        private TextMeshProUGUI pageCount;
        private TextMeshProUGUI hint;
        private readonly Slot[] slots = new Slot[Wedges];

        private sealed class Slot
        {
            public RectTransform Rect;
            public Image Icon;
            public TextMeshProUGUI Label;
        }

        private static Vector2 ScreenCentre => new(Screen.width * 0.5f, Screen.height * 0.5f);

        // ------------------------------------------------------------------- bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject("EmoteWheel");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<EmoteWheelUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            // Its own InputControls with only the UI map live, as ChatUI and PauseMenuUI do: opening
            // the wheel hands control over, which switches the player's whole asset off — and the
            // key being held is what closes it.
            inputs = new InputControls();
            inputs.UI.EmotePage.performed += OnPageInput;
            inputs.UI.Enable();

            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnActiveSceneChanged(Scene from, Scene to) => Close();

        private void OnDestroy()
        {
            if (instance == this) instance = null;

            SceneManager.activeSceneChanged -= OnActiveSceneChanged;

            if (inputs != null)
            {
                inputs.UI.Disable();
                inputs.Dispose();
            }

            // A wheel destroyed while open would otherwise keep the player's cursor and controls.
            GameplayMenuScope.Exit(this);
        }

        // ------------------------------------------------------------------------ tick

        private void Update()
        {
            if (open) Track();
            else if (inputs.UI.EmoteWheel.WasPressedThisFrame()) TryOpen();

            if (built) Animate();
        }

        private void LateUpdate()
        {
            if (open && cancelRequested) Close();
        }

        private void Track()
        {
            // Another screen came up over the wheel (pause, chat, a dialogue): the wheel steps aside
            // rather than play an emote behind it when the key comes up.
            if (GameplayMenuScope.Owners.Count > 1 || inputs.UI.Cancel.WasPressedThisFrame())
                cancelRequested = true;
            if (cancelRequested) return;

            if (!inputs.UI.EmoteWheel.IsPressed())
            {
                Commit();
                return;
            }

            int wedge = ReadWedge(out Vector2 direction);
            pointer.gameObject.SetActive(wedge >= 0);
            if (wedge >= 0) pointer.anchoredPosition = direction * (HubRadius - PointerSize);

            if (wedge == hoveredWedge) return;
            hoveredWedge = wedge;
            ApplyHover();
        }

        /// <summary>
        /// The wedge under the stick if it is pushed, else under the mouse — measured from the
        /// screen centre in reference pixels, so the dead zone is the same size on every screen.
        /// </summary>
        private int ReadWedge(out Vector2 direction)
        {
            Vector2 stick = inputs.UI.EmoteAim.ReadValue<Vector2>();
            if (stick.sqrMagnitude >= stickDeadZone * stickDeadZone)
            {
                direction = stick.normalized;
                return EmoteWheelLayout.WedgeAt(stick, Wedges, stickDeadZone);
            }

            Vector2 offset = (inputs.UI.Point.ReadValue<Vector2>() - ScreenCentre)
                             / UIScale.ScaleFactor(Screen.width, Screen.height);
            direction = offset.normalized;
            return EmoteWheelLayout.WedgeAt(offset, Wedges, pointerDeadZone);
        }

        private void OnPageInput(InputAction.CallbackContext context)
        {
            if (!open || cancelRequested || pages.Count < 2) return;

            float value = context.ReadValue<float>();
            if (Mathf.Approximately(value, 0f) || Time.unscaledTime < nextPageTime) return;

            nextPageTime = Time.unscaledTime + pageRepeatSeconds;
            page = EmoteWheelLayout.Wrap(page + (value > 0f ? 1 : -1), pages.Count);
            ShowPage();
        }

        // ------------------------------------------------------------------ open/close

        private void TryOpen()
        {
            // Not over chat, a menu, a terminal, a cutscene or death: every one of those has the
            // player's input switched off.
            if (!GameplayMenuScope.AcceptsGameplayInput) return;

            PlayerController local = GameplayMenuScope.FindLocalPlayer();
            var body = local.GetComponent<PlayerEmotes>();
            if (body == null)
            {
                Debug.LogError("[EmoteWheel] The local player has no PlayerEmotes component.", local);
                return;
            }

            EmoteCatalog emoteCatalog = EmoteCatalog.Default;
            if (emoteCatalog == null || emoteCatalog.Entries.Count == 0) return;

            EnsureBuilt();

            // Time runs on and the visor stays up: this is a flick of the wrist, not a menu.
            if (!GameplayMenuScope.Enter(this, freezeTime: false, hideHud: false)) return;

            player = local;
            emotes = body;
            catalog = emoteCatalog;
            pages = EmoteWheelLayout.Paginate(Categories(catalog), Wedges);
            page = EmoteWheelLayout.Wrap(page, pages.Count);

            open = true;
            cancelRequested = false;

            // The scope has just freed a cursor that was locked to the middle of the window; put it
            // there explicitly, so the wheel always opens with nothing selected.
            Mouse.current?.WarpCursorPosition(ScreenCentre);
            pointer.gameObject.SetActive(false);

            ShowPage();
            canvas.gameObject.SetActive(true);
        }

        private void Commit()
        {
            int entry = hoveredWedge >= 0 ? entryInWedge[hoveredWedge] : -1;
            PlayerEmotes body = emotes;
            bool alive = player != null && !player.IsDead;

            Close();

            if (entry >= 0 && body != null && alive) body.PlayAsOwner(entry);
        }

        private void Close()
        {
            if (!open) return;

            open = false;
            cancelRequested = false;
            player = null;
            emotes = null;

            GameplayMenuScope.Exit(this);
        }

        private static List<string> Categories(EmoteCatalog source)
        {
            IReadOnlyList<EmoteCatalog.Entry> entries = source.Entries;
            var categories = new List<string>(entries.Count);
            for (int i = 0; i < entries.Count; i++) categories.Add(entries[i].category);
            return categories;
        }

        // --------------------------------------------------------------------- drawing

        private void ShowPage()
        {
            EmoteWheelLayout.Page current = pages[page];

            for (int i = 0; i < Wedges; i++) entryInWedge[i] = -1;
            for (int k = 0; k < current.Entries.Count; k++)
                entryInWedge[EmoteWheelLayout.SlotOf(k, current.Entries.Count, Wedges)] = current.Entries[k];

            for (int i = 0; i < Wedges; i++)
            {
                Slot slot = slots[i];
                EmoteCatalog.Entry entry = catalog.At(entryInWedge[i]);
                slot.Rect.gameObject.SetActive(entry != null);
                if (entry == null) continue;

                slot.Label.text = DisplayName(entry.word);
                slot.Icon.sprite = entry.icon;
                slot.Icon.enabled = entry.icon != null;
                slot.Label.alignment = entry.icon != null ? TextAlignmentOptions.Bottom : TextAlignmentOptions.Center;
            }

            pageLabel.text = PageName(current);
            pageCount.text = pages.Count > 1 ? $"{page + 1} / {pages.Count}" : string.Empty;
            hint.text = pages.Count > 1 ? HintPages : HintOnePage;

            hoveredWedge = -1;
            ApplyHover();
        }

        private void ApplyHover()
        {
            int entry = hoveredWedge >= 0 ? entryInWedge[hoveredWedge] : -1;

            highlight.enabled = entry >= 0;
            if (entry >= 0)
            {
                // The fill runs clockwise from twelve o'clock for one wedge's width; turning it back
                // by half a wedge centres it on the wedge. UI rotation is counter-clockwise.
                float start = EmoteWheelLayout.WedgeAngle(hoveredWedge, Wedges) - 180f / Wedges;
                highlight.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -start);
            }

            for (int i = 0; i < Wedges; i++)
            {
                bool lit = i == hoveredWedge && entry >= 0;
                slots[i].Rect.localScale = Vector3.one * (lit ? hoverScale : 1f);
                slots[i].Label.color = lit ? UITheme.Bright : UITheme.Muted;
            }

            nameLabel.text = entry >= 0 ? DisplayName(catalog.At(entry).word) : string.Empty;
        }

        private void Animate()
        {
            float target = open && !cancelRequested ? 1f : 0f;
            openness = fadeSeconds <= 0f
                ? target
                : Mathf.MoveTowards(openness, target, Time.unscaledDeltaTime / fadeSeconds);

            canvasGroup.alpha = openness;
            wheel.localScale = Vector3.one * Mathf.Lerp(ClosedScale, 1f, openness);

            bool show = open || openness > 0f;
            if (canvas.gameObject.activeSelf != show) canvas.gameObject.SetActive(show);
        }

        private static string PageName(EmoteWheelLayout.Page current)
        {
            string name = string.IsNullOrWhiteSpace(current.Category) ? UncategorisedPage : current.Category;
            return current.PartCount > 1 ? $"{name} {current.Part + 1}" : name;
        }

        private static string DisplayName(string word) =>
            string.IsNullOrEmpty(word) ? string.Empty : char.ToUpperInvariant(word[0]) + word.Substring(1);

        // -------------------------------------------------------------------- building

        private void EnsureBuilt()
        {
            if (built) return;
            built = true;
            Build();
        }

        private void Build()
        {
            var canvasGo = new GameObject("EmoteWheelCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);

            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            UIScale.Configure(canvasGo.GetComponent<CanvasScaler>());

            canvasGroup = canvasGo.GetComponent<CanvasGroup>();
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            var root = (RectTransform)canvasGo.transform;
            wheel = Centred(UIBuilder.Rect("Wheel", root), Vector2.one * OuterRadius * 2f);

            Disc(wheel, "Glow", OuterRadius * 2f * GlowScale, UITheme.GlowSprite, UITheme.Backdrop);
            Disc(wheel, "Disc", OuterRadius * 2f, UITheme.CircleSprite, WithAlpha(UITheme.Panel, DiscAlpha));

            highlight = Disc(wheel, "Highlight", OuterRadius * 2f, UITheme.CircleSprite,
                             WithAlpha(UITheme.Accent, HighlightAlpha));
            highlight.type = Image.Type.Filled;
            highlight.fillMethod = Image.FillMethod.Radial360;
            highlight.fillOrigin = (int)Image.Origin360.Top;
            highlight.fillClockwise = true;
            highlight.fillAmount = 1f / Wedges;

            for (int i = 0; i < Wedges; i++) BuildDivider(i);
            for (int i = 0; i < Wedges; i++) slots[i] = BuildSlot(i);

            RectTransform hub = Disc(wheel, "Hub", HubRadius * 2f, UITheme.CircleSprite, UITheme.PanelRaised).rectTransform;
            pageLabel = HubLabel(hub, "Page", HubLineSpacing, UITheme.CaptionSize, UITheme.Muted);
            nameLabel = HubLabel(hub, "Name", 0f, UITheme.HeadingSize, UITheme.Bright);
            pageCount = HubLabel(hub, "Count", -HubLineSpacing, UITheme.CaptionSize, UITheme.Faint);

            pointer = Disc(wheel, "Pointer", PointerSize, UITheme.CircleSprite, UITheme.Accent).rectTransform;

            RectTransform hintRect = Centred(UIBuilder.Rect("Hint", root), new Vector2(HintWidth, UITheme.CaptionSize * LineHeight));
            hintRect.anchoredPosition = new Vector2(0f, -(OuterRadius + HintGap));
            hint = UIBuilder.Label(hintRect, string.Empty, UITheme.CaptionSize, UITheme.Faint, TextAlignmentOptions.Center);

            canvasGo.SetActive(false);
        }

        /// <summary>A hairline on the boundary after wedge <paramref name="wedge"/>, from the hub to the rim.</summary>
        private void BuildDivider(int wedge)
        {
            RectTransform line = Centred(UIBuilder.Rect("Divider", wheel), new Vector2(DividerWidth, OuterRadius - HubRadius));
            line.pivot = new Vector2(0.5f, 0f);

            float angle = EmoteWheelLayout.WedgeAngle(wedge, Wedges) + 180f / Wedges;
            line.localRotation = Quaternion.Euler(0f, 0f, -angle);
            line.anchoredPosition = line.localRotation * Vector3.up * HubRadius;
            UIBuilder.Solid(line, UITheme.Hairline);
        }

        private Slot BuildSlot(int wedge)
        {
            RectTransform rect = Centred(UIBuilder.Rect("Slot " + wedge, wheel), new Vector2(SlotWidth, IconSize + SlotLabelHeight));
            rect.anchoredPosition = EmoteWheelLayout.WedgeDirection(wedge, Wedges) * SlotRadius;

            RectTransform iconRect = UIBuilder.Rect("Icon", rect);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.sizeDelta = Vector2.one * IconSize;
            Image icon = UIBuilder.Sprite(iconRect, null, Color.white, Image.Type.Simple);
            icon.preserveAspect = true;

            TextMeshProUGUI label = UIBuilder.LabelIn(rect, "Label", string.Empty, UITheme.LabelSize, UITheme.Muted,
                                                      TextAlignmentOptions.Center);
            return new Slot { Rect = rect, Icon = icon, Label = label };
        }

        private static TextMeshProUGUI HubLabel(RectTransform hub, string name, float y, int size, Color color)
        {
            RectTransform rect = Centred(UIBuilder.Rect(name, hub), new Vector2(HubLabelWidth, size * LineHeight));
            rect.anchoredPosition = new Vector2(0f, y);
            return UIBuilder.Label(rect, string.Empty, size, color, TextAlignmentOptions.Center);
        }

        private static Image Disc(RectTransform parent, string name, float diameter, Sprite sprite, Color color)
        {
            RectTransform rect = Centred(UIBuilder.Rect(name, parent), Vector2.one * diameter);
            return UIBuilder.Sprite(rect, sprite, color, Image.Type.Simple);
        }

        private static RectTransform Centred(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private static Color WithAlpha(Color color, float alpha) => new(color.r, color.g, color.b, alpha);
    }
}
