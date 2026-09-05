using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Items;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The body screen: your own character, seen from the front in the live world, with the three
    /// worn-gear sites on the body and a pyramid of six tiles down the left edge — the torso on
    /// top, the two gauntlets, then the hand hotbar's three. Opened with I.
    ///
    /// <para>
    /// <b>Click to carry.</b> Click a filled site or tile and its icon follows the cursor; click
    /// another site or tile to put it there, swapping if that one is full. While something is
    /// carried every site it can go to lights: a translucent copy of the item seated where it will
    /// sit on an empty site, an amber outline on a filled one. Hovering a site it cannot go to
    /// tints it red; clicking there shakes it. Nothing moves locally: a legal click sends one
    /// request and the slot-change events that come back redraw everything. The same gesture the
    /// backpack's hand uses, on the same button, so the two screens are one language.
    /// </para>
    /// <para>
    /// <b>Two routes to the same three worn slots.</b> Each site on the figure has a tile on the
    /// rail, and a click on either does exactly the same thing. Pointing at one of those tiles
    /// lights the site it names, so the rail is legible without hunting across the figure and the
    /// figure still answers what the rail means. Only a REFUSAL differs, and only in where it is
    /// shown: on whichever of the two was clicked.
    /// </para>
    /// <para>
    /// This class is the conductor. The world — the camera, the ghosts, what the cursor is over —
    /// is <see cref="BodyFocusSession"/> on the player; this owns the carry, the tiles, and the
    /// chips and captions drawn on <see cref="WorldOverlay"/>. A gameplay overlay in the
    /// <see cref="DevInventoryUI"/> mould: a singleton that lives across scene loads, builds its
    /// canvas lazily, and takes input, look and the cursor through <see cref="GameplayMenuScope"/>
    /// — without stopping the clock, and without the HUD, whose hotbar would otherwise be drawn
    /// twice. No panel and no backdrop: the world is the backdrop.
    /// </para>
    /// </summary>
    public class BodyInventoryUI : MonoBehaviour
    {
        private const float OpenSeconds = 0.14f;
        private const float ChipGap = 26f;
        private const float ChipHeight = 30f;
        private const float KeyChipWidth = 44f;
        private const float BackChipWidth = 116f;
        private const float ChipFontSize = 18f;

        /// <summary>The rail hangs off the left edge at half height; every rail y is signed from there.</summary>
        private static readonly Vector2 RailAnchor = new(0f, 0.5f);

        private static BodyInventoryUI instance;

        public static bool IsOpen => instance != null && instance.open;

        private InputControls inputs;

        private bool open;
        private bool built;
        private float visibility;

        private CanvasGroup group;
        private RectTransform carryRoot;
        private Image carryIcon;

        private readonly List<Tile> tiles = new();

        private PlayerController player;
        private IPlayerInventory hotbar;
        private IBodyEquipment body;
        private BodyFocusSession session;

        /// <summary>What the cursor is carrying, or none.</summary>
        private GearRef carried = GearRef.None;

        /// <summary>The tile under the cursor, or none.</summary>
        private GearRef hoveredTile = GearRef.None;

        /// <summary>The site under the cursor, or null.</summary>
        private BodySlot? hoveredSite;

        private readonly Chip[] chips = new Chip[GearRef.BodySlotCount];
        private TextMeshProUGUI caption;

        private sealed class Tile
        {
            public GearRef Slot;
            public GearTile View;
        }

        /// <summary>A key label pinned beside a site: Q, E, SPACE ×2.</summary>
        private sealed class Chip
        {
            public RectTransform Rect;
            public float HalfWidth;
        }

        // ------------------------------------------------------------------ bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject("BodyInventory");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BodyInventoryUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            // Its own copy of the UI map, because the player's own input asset is switched off
            // for as long as this screen holds the scope — see GameplayMenuScope.
            inputs = new InputControls();
            inputs.UI.BodyInventory.performed += _ => Toggle();
            inputs.UI.Cancel.performed += _ => { if (open) Close(); };
            inputs.UI.Enable();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;

            // Being destroyed is an exit like any other, and the two things this screen borrows —
            // the session's camera and hidden renderers, and the scope's cursor, look and controls
            // — both outlive the component that borrowed them. Close is guarded on `open`, so a
            // duplicate torn down in Awake and a screen that was never opened both do nothing.
            Close();

            // The chips are not ours to leave behind. They hang off WorldOverlay, which is
            // DontDestroyOnLoad and outlives this object, so hiding them is not enough: a chip
            // whose conductor has gone would sit in that canvas for the rest of the process.
            DestroyChips();

            if (inputs != null)
            {
                inputs.UI.Disable();
                inputs.Dispose();
            }

            // The last line of defence, and idempotent: Exit ignores an owner that holds no claim.
            // A player left with a free cursor, no controls and no camera is the worst thing this
            // screen can do to them, so the release is repeated on the one path that cannot retry.
            GameplayMenuScope.Exit(this);
        }

        // ---------------------------------------------------------------------- input

        public void Toggle()
        {
            // I is a letter before it is a shortcut, so a focused field owns it.
            if (IsTypingInField()) return;

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

        /// <summary>
        /// Opens the screen, or leaves everything exactly as it was.
        ///
        /// <para>
        /// All or nothing, deliberately: the scope is taken before the session because the session
        /// needs the cursor free, and the session can still refuse — this is not the owner of that
        /// body, the rig has no body slots, the camera failed to spawn. A refusal after the scope
        /// was taken must hand it straight back, or the player stands there with a cursor, no
        /// controls and nothing on screen to close.
        /// </para>
        /// </summary>
        public void Open()
        {
            if (open) return;

            player = GameplayMenuScope.FindLocalPlayer();
            if (player == null) return;

            hotbar = player.PlayerInventory;
            body = player.GetComponent<IBodyEquipment>();
            session = player.GetComponent<BodyFocusSession>();
            if (hotbar == null || body == null || session == null) return;

            // A rider's hands are on the controls, and nothing on the body may move mid-flight.
            if (body.IsMounted) return;

            // Something else already holds the controls — the pause menu, the chat box, a cutscene,
            // or the backpack's own focus mode. PackFocusSession refuses for the same reason and
            // says why; this is the other half of that guard. Without it, I over a deployed pack
            // takes the scope a SECOND time (so it is not handed back until both owners exit) and
            // spawns a second focus camera, and two enabled cameras at one depth render in no
            // defined order.
            if (GameplayMenuScope.IsActive) return;

            if (!GameplayMenuScope.Enter(this, freezeTime: false, hideHud: true)) return;

            if (!session.Enter())
            {
                GameplayMenuScope.Exit(this);
                return;
            }

            open = true;
            carried = GearRef.None;
            hoveredTile = GearRef.None;
            hoveredSite = null;

            UIBuilder.EnsureEventSystem();
            if (!built) Build();
            EnsureChips();

            group.gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;

            hotbar.OnSlotChanged += OnHotbarChanged;
            hotbar.OnSlotSelected += OnHotbarSelected;
            body.OnBodySlotChanged += OnBodyChanged;
            session.HoverChanged += OnSiteHover;
            session.SiteClicked += OnSiteClicked;
            session.NothingClicked += OnNothingClicked;

            Refresh();
        }

        /// <summary>
        /// Hands everything back: the carry, the chips, the six subscriptions, the world half and
        /// the scope. Safe to run twice, and safe to run from a teardown path — which is why the
        /// session poll in <see cref="Update"/>, I, Esc and <c>OnDestroy</c> all come through here
        /// rather than each unwinding their own part of it.
        /// </summary>
        public void Close()
        {
            if (!open) return;

            open = false;
            carried = GearRef.None;
            hoveredTile = GearRef.None;
            hoveredSite = null;

            group.blocksRaycasts = false;
            group.interactable = false;
            if (carryRoot != null) carryRoot.gameObject.SetActive(false);
            ShowChips(false);

            Unsubscribe();

            // Idempotent on the session's side too, and safe when it has already exited itself.
            if (session != null) session.Exit();

            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            GameplayMenuScope.Exit(this);
        }

        private void Unsubscribe()
        {
            if (hotbar != null)
            {
                hotbar.OnSlotChanged -= OnHotbarChanged;
                hotbar.OnSlotSelected -= OnHotbarSelected;
            }

            if (body != null) body.OnBodySlotChanged -= OnBodyChanged;

            if (session != null)
            {
                session.HoverChanged -= OnSiteHover;
                session.SiteClicked -= OnSiteClicked;
                session.NothingClicked -= OnNothingClicked;
            }
        }

        private void OnHotbarChanged(int index, InventorySlot slot) => OnAnySlotChanged();
        private void OnHotbarSelected(InventorySlot slot) => Refresh();
        private void OnBodyChanged(BodySlot slot, InventorySlot contents) => OnAnySlotChanged();

        /// <summary>
        /// The world moved something. Whatever was being carried may no longer be where it was,
        /// so the carry is dropped rather than left pointing at a slot that now holds something
        /// else — the same reason the pack's hand lets go on any change it did not make.
        /// </summary>
        private void OnAnySlotChanged()
        {
            carried = GearRef.None;
            Refresh();
        }

        private void Update()
        {
            if (!built) return;

            float target = open ? 1f : 0f;
            if (!Mathf.Approximately(visibility, target))
            {
                visibility = Mathf.MoveTowards(visibility, target, Time.unscaledDeltaTime / OpenSeconds);
                group.alpha = visibility * visibility * (3f - 2f * visibility);

                if (visibility <= 0f) group.gameObject.SetActive(false);
            }

            if (!open) return;

            // The session can end without us: death, the player being despawned, the component
            // being disabled. It tears down its own half — camera, ghosts, hidden renderers — and
            // deliberately calls nothing outward while doing it, because those are teardown paths
            // where re-entrancy is the thing that bites. So the screen asks instead. Without this
            // the UI would sit open holding GameplayMenuScope over a world with no focus camera,
            // and the player would be left with a cursor and no controls.
            if (session == null || !session.IsOpen) { Close(); return; }

            if (!carried.IsNone && carryRoot != null && Mouse.current != null)
                carryRoot.position = Mouse.current.position.ReadValue();

            PlaceChips();
        }

        // ------------------------------------------------------------------- actions

        /// <summary>
        /// A refusal answers on the tile that was clicked, not on the site across the screen. The
        /// beep is the site's own, because a worn slot is now reachable from either and a refusal
        /// that sounds on the body and stays silent on the rail would read as two different rules.
        /// </summary>
        private void OnTileClicked(Tile tile) => OnSlotClicked(tile.Slot, refused: () =>
        {
            tile.View.Shake();
            Sfx.Play2D(SfxId.UiError);
        });

        private void OnSiteClicked(BodySlot slot) => OnSlotClicked(GearRef.Body(slot), refused: () => session.Refuse(slot));

        /// <summary>
        /// One click, wherever it landed. Pick up, put back, or ask the server to move — the site
        /// and the tile differ only in how they show a refusal.
        /// </summary>
        private void OnSlotClicked(GearRef slot, System.Action refused)
        {
            if (!open) return;

            if (carried.IsNone)
            {
                if (KindAt(slot) == null) return;

                carried = slot;
                Refresh();
                return;
            }

            if (slot == carried)
            {
                carried = GearRef.None;
                Refresh();
                return;
            }

            MoveResult verdict = Predict(slot);

            if (!verdict.Allowed)
            {
                refused();
                return;
            }

            // The request is the whole action. The icon goes back to its origin now; the answer
            // arrives as slot-change events and redraws every tile and site. A site is lit as
            // "committing" first, so the click is acknowledged before the round trip.
            //
            // Lit BEFORE the request, not after. On a host the request is answered inside the
            // call — the NetworkList write raises OnBodySlotChanged synchronously — so a site lit
            // afterwards would be lit against an answer that had already arrived and cleared it,
            // and would sit stuck on its preview ghost until the session's commit timeout gave up
            // and shook it at a player whose move had in fact succeeded. Lit first, that same
            // synchronous answer clears it on its way past.
            if (slot.IsBody) session.Commit(slot.Slot);
            body.RequestMove(carried, slot);
            carried = GearRef.None;
            Refresh();
        }

        /// <summary>A click on the world with something in hand puts it back where it came from.</summary>
        private void OnNothingClicked()
        {
            if (!open || carried.IsNone) return;

            carried = GearRef.None;
            Refresh();
        }

        /// <summary>
        /// The cursor came over a tile, or left it. A tile naming a worn slot also lights that site
        /// on the figure — the ghost, the outline and the caption — so pointing at the rail answers
        /// "which one of those is that on me?" without a second gesture. The session hands the hover
        /// straight back out through <see cref="OnSiteHover"/>, so the caption needs nothing here.
        /// </summary>
        private void OnTileHover(Tile tile, bool over)
        {
            hoveredTile = over ? tile.Slot : hoveredTile == tile.Slot ? GearRef.None : hoveredTile;

            if (session != null)
                session.SetExternalHover(hoveredTile.IsBody ? hoveredTile.Slot : (BodySlot?)null);

            Refresh();
        }

        private void OnSiteHover(BodySlot? slot)
        {
            hoveredSite = slot;
            RefreshCaption();
        }

        private MoveResult Predict(GearRef target) =>
            GearMoves.Resolve(carried, KindAt(carried), target, KindAt(target), mounted: false);

        private InventoryItem ItemAt(GearRef slot)
        {
            if (slot.IsNone) return null;

            InventorySlot contents = slot.IsBody ? body.GetSlot(slot.Slot) : hotbar.GetSlot(slot.Index);
            return contents == null || contents.IsEmpty ? null : contents.Item;
        }

        private EquipKind? KindAt(GearRef slot)
        {
            InventoryItem item = ItemAt(slot);
            return item != null ? item.equipKind : null;
        }

        private void Refresh()
        {
            if (!built || hotbar == null || body == null) return;

            foreach (Tile tile in tiles)
            {
                InventoryItem item = ItemAt(tile.Slot);
                bool isCarried = tile.Slot == carried;
                bool isHovered = tile.Slot == hoveredTile;
                // Only a HAND slot can be the selected one, and GearRef.Index is the index within
                // its own list — Torso is 0 there, so an unguarded comparison lights the torso tile
                // whenever hotbar slot 1 is in hand.
                bool selected = tile.Slot.IsHotbar && tile.Slot.Index == hotbar.SelectedSlotIndex;

                bool dropTarget = false, refused = false;
                if (!carried.IsNone && isHovered && !isCarried)
                {
                    MoveResult verdict = Predict(tile.Slot);
                    dropTarget = verdict.Allowed;
                    refused = !verdict.Allowed;
                }

                // The badge means "worn gear lying in a hand slot", so it belongs on hotbar tiles
                // only: on a body tile the very same item is exactly where it should be.
                bool worn = tile.Slot.IsHotbar && item != null && !BodySlotRules.HandEquips(item.equipKind);

                tile.View.Refresh(item, selected, isHovered, dropTarget, refused, isReserved: isCarried, isWorn: worn);
            }

            InventoryItem carriedItem = ItemAt(carried);
            bool carrying = carriedItem != null;

            if (carryRoot != null) carryRoot.gameObject.SetActive(carrying);
            if (carryIcon != null)
            {
                carryIcon.sprite = carrying ? carriedItem.icon : null;
                carryIcon.enabled = carrying && carriedItem.icon != null;
            }

            if (session != null && open) session.SetCarry(carried, carriedItem);

            RefreshCaption();
        }

        // ---------------------------------------------------------------- captions

        /// <summary>
        /// The one line of text beside the hovered site: what is there and its key, or what a
        /// click would do. Refusals get no text — the red tint and the shake are the answer.
        /// </summary>
        private void RefreshCaption()
        {
            if (caption == null) return;

            string text = string.Empty;

            if (open && session != null && hoveredSite.HasValue)
            {
                BodySlot slot = hoveredSite.Value;
                InventoryItem here = ItemAt(GearRef.Body(slot));
                InventoryItem carriedItem = ItemAt(carried);

                switch (session.StateOf(slot))
                {
                    case SiteState.Empty:
                        text = SlotName(slot, session.PlaceOf(slot)) + "  ·  " + KeyOf(slot);
                        break;
                    case SiteState.Worn:
                    case SiteState.Reserved:
                        if (here != null) text = here.itemName + "  ·  " + KeyOf(slot);
                        break;
                    case SiteState.Preview:
                    case SiteState.Committing:
                        if (carriedItem != null) text = carriedItem.itemName + "  ·  " + KeyOf(slot);
                        break;
                    case SiteState.SwapOutline:
                        if (here != null && carriedItem != null) text = "Swap  ·  " + here.itemName + " ↔ " + carriedItem.itemName;
                        break;
                }
            }

            caption.text = text;
            caption.enabled = !string.IsNullOrEmpty(text);
        }

        /// <summary>
        /// What to call an empty site. The torso has two places and one slot, so it is named by the
        /// place it is currently offering — "Chest" while a chest device is on the cursor, "Back"
        /// otherwise. <paramref name="place"/> comes from the site itself; deriving it here again
        /// would be a second copy of the rule that decides where torso gear goes.
        /// </summary>
        private static string SlotName(BodySlot slot, EquipKind place) => slot switch
        {
            BodySlot.LeftGauntlet => "Left gauntlet",
            BodySlot.RightGauntlet => "Right gauntlet",
            _ => place == EquipKind.Chest ? "Chest" : "Back",
        };

        private static string KeyOf(BodySlot slot) => slot switch
        {
            BodySlot.LeftGauntlet => "Q",
            BodySlot.RightGauntlet => "E",
            _ => "SPACE ×2",
        };

        // --------------------------------------------------------------------- build

        private void Build()
        {
            built = true;

            var canvasGo = new GameObject("BodyInventoryCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2060; // above the HUD and the trade screen, below the dev browser

            UIScale.Configure(canvasGo.GetComponent<CanvasScaler>());

            group = canvasGo.GetComponent<CanvasGroup>();
            group.alpha = 0f;

            var root = (RectTransform)canvasGo.transform;

            BuildHeader(root);
            BuildRail(root);
            BuildCarry(root);

            visibility = 0f;
            group.gameObject.SetActive(false);
        }

        private void BuildHeader(RectTransform host)
        {
            var header = UIBuilder.Rect("Header", host);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, 72f);

            var titleRect = UIBuilder.LeftColumn(UIBuilder.Rect("Title", header), 34f, 400f);
            UIBuilder.Label(titleRect, "BODY GEAR", UITheme.HeadingSize, UITheme.Bright, TextAlignmentOptions.Left, FontStyles.Bold);

            var hintRect = UIBuilder.RightColumn(UIBuilder.Rect("Hint", header), 34f, 420f);
            UIBuilder.Label(hintRect, "click to pick up  ·  click to place  ·  I closes", UITheme.CaptionSize,
                UITheme.Faint, TextAlignmentOptions.Right);
        }

        /// <summary>
        /// All six slots, as a pyramid down the LEFT edge — the HUD's own tiles, since the HUD is
        /// hidden. A block and not the bar's row: the lens frames the WHOLE figure down the middle
        /// of the screen, so a row along the bottom lies across the legs, and worn torso gear reaches
        /// to the knees. The tiles keep their numbers, their look and their one gesture, so where
        /// they hang is the only convention this departs from (user, 2026-09-04).
        ///
        /// <para>
        /// The three worn slots join them here (user, 2026-09-06). They are still clickable on the
        /// body itself, ghosts and all — this is a second route to the same three slots, not a
        /// replacement, and one that is legible without hunting for a site on a figure. The rows
        /// read top-down the way the body does: trunk, arms, hands. Geometry is
        /// <see cref="GearRailLayout"/>.
        /// </para>
        /// </summary>
        private void BuildRail(RectTransform host)
        {
            int size = hotbar != null ? hotbar.GetInventorySize() : 3;

            foreach (GearRailLayout.Placement placement in GearRailLayout.Build(size))
                AddTile(host, placement);

            Caption(host, "Worn caption", "Worn", new Vector2(0.5f, 0f), GearRailLayout.CaptionAboveY);
            Caption(host, "Hands caption", "Hands  ·  1 – " + size, new Vector2(0.5f, 1f), GearRailLayout.CaptionBelowY);
        }

        /// <summary>One of the two muted labels bracketing the pyramid, naming the band it sits against.</summary>
        private static void Caption(RectTransform host, string name, string text, Vector2 pivot, float y)
        {
            var rect = UIBuilder.Rect(name, host);
            rect.anchorMin = RailAnchor;
            rect.anchorMax = RailAnchor;
            rect.pivot = pivot;
            rect.sizeDelta = new Vector2(HotbarStyle.SlotWidth * 3.2f, 24f);
            rect.anchoredPosition = new Vector2(GearRailLayout.CentreFromLeft, y);
            UIBuilder.Label(rect, text, UITheme.CaptionSize, UITheme.Muted, TextAlignmentOptions.Center);
        }

        private void AddTile(RectTransform host, GearRailLayout.Placement placement)
        {
            GearTile view = GearTile.Build(host, placement.Name, placement.Key);

            RectTransform rect = view.Rect;
            rect.anchorMin = RailAnchor;
            rect.anchorMax = RailAnchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = placement.At;

            var element = view.GetComponent<LayoutElement>();
            if (element != null) element.ignoreLayout = true;

            var tile = new Tile { Slot = placement.Slot, View = view };
            view.Clicked += _ => OnTileClicked(tile);
            view.HoverChanged += (_, over) => OnTileHover(tile, over);
            tiles.Add(tile);
        }

        /// <summary>The icon that follows the cursor while something is carried. Never a raycast target.</summary>
        private void BuildCarry(RectTransform root)
        {
            carryRoot = UIBuilder.Rect("Carry", root);
            carryRoot.anchorMin = Vector2.zero;
            carryRoot.anchorMax = Vector2.zero;
            carryRoot.pivot = new Vector2(0.5f, 0.5f);
            carryRoot.sizeDelta = new Vector2(HotbarStyle.SlotWidth - HotbarStyle.IconInset * 2f,
                                              HotbarStyle.SlotHeight - HotbarStyle.IconInset * 2f);

            carryIcon = UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Icon", carryRoot)), null, Color.white);
            carryIcon.preserveAspect = true;
            carryIcon.raycastTarget = false;
            carryIcon.enabled = false;

            carryRoot.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------------- chips

        /// <summary>
        /// The key chips and the caption live on <see cref="WorldOverlay"/>, not on this canvas:
        /// they track world points, and that layer is the one thing in the UI built to do that.
        /// </summary>
        private void EnsureChips()
        {
            if (caption != null) return;

            WorldOverlay overlay = WorldOverlay.Create();

            for (int i = 0; i < chips.Length; i++)
            {
                var slot = (BodySlot)i;
                float width = slot == BodySlot.Torso ? BackChipWidth : KeyChipWidth;

                RectTransform rect = UIBuilder.Rect("BodyChip " + slot, overlay.Layer);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(width, ChipHeight);
                UIBuilder.Sprite(rect, UITheme.ChipSprite, UITheme.Panel);

                TextMeshProUGUI text = WorldOverlay.CreateLabel(rect, "Key", ChipFontSize, width);
                text.text = KeyOf(slot);
                text.color = UITheme.Bright;
                text.fontStyle = FontStyles.Bold;

                chips[i] = new Chip { Rect = rect, HalfWidth = width * 0.5f };
                rect.gameObject.SetActive(false);
            }

            caption = WorldOverlay.CreateLabel(overlay.Layer, "BodyCaption", UITheme.CaptionSize, 460f);
            caption.color = UITheme.Muted;
            caption.enabled = false;
        }

        private void ShowChips(bool shown)
        {
            foreach (Chip chip in chips)
                if (chip != null && chip.Rect != null) chip.Rect.gameObject.SetActive(shown);

            if (caption != null && !shown) caption.enabled = false;
        }

        /// <summary>
        /// Takes the chips and the caption off <see cref="WorldOverlay"/> for good. Only for the
        /// destroy path: the overlay is DontDestroyOnLoad, so anything parented to it that is
        /// merely hidden survives the screen that made it.
        /// </summary>
        private void DestroyChips()
        {
            for (int i = 0; i < chips.Length; i++)
            {
                if (chips[i] != null && chips[i].Rect != null) Destroy(chips[i].Rect.gameObject);
                chips[i] = null;
            }

            if (caption != null) Destroy(caption.gameObject);
            caption = null;
        }

        /// <summary>
        /// Pin each chip beside its site: outward from the body's centre for the arms — the
        /// player's LEFT arm is on the RIGHT of the screen — and above the crest for the back. The
        /// caption hangs off the hovered site's chip.
        /// </summary>
        private void PlaceChips()
        {
            if (session == null || caption == null) return;

            WorldOverlay overlay = WorldOverlay.Instance;
            float screenCentreX = overlay != null ? overlay.Layer.rect.center.x : 0f;

            for (int i = 0; i < chips.Length; i++)
            {
                Chip chip = chips[i];
                if (chip == null || chip.Rect == null) continue;

                var slot = (BodySlot)i;
                bool shown = session.TryCanvasRect(slot, out Rect r);
                chip.Rect.gameObject.SetActive(shown);
                if (!shown) continue;

                Vector2 at;
                if (slot == BodySlot.Torso)
                    at = new Vector2(r.center.x, r.yMax + ChipGap + ChipHeight * 0.5f);
                else if (r.center.x < screenCentreX)
                    at = new Vector2(r.xMin - ChipGap - chip.HalfWidth, r.center.y);
                else
                    at = new Vector2(r.xMax + ChipGap + chip.HalfWidth, r.center.y);

                chip.Rect.anchoredPosition = at;

                if (hoveredSite == slot && caption.enabled)
                {
                    float y = slot == BodySlot.Torso ? at.y + ChipHeight * 0.5f + 16f : at.y - ChipHeight * 0.5f - 16f;
                    caption.rectTransform.anchoredPosition = new Vector2(at.x, y);
                }
            }
        }
    }
}
