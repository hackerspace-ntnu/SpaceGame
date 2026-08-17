using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Developer artifact browser: every item in <see cref="Registry{T}"/>, in a grid, one click to
    /// put it on the hotbar and one to take it off. Opened with I while developer mode is on.
    /// <para>
    /// Gated behind <see cref="GameSettings.DevMode"/> rather than a build flag so it can be turned
    /// on during a live session — which is the point of it. The hotbar edits go through
    /// <see cref="IPlayerInventory"/>, not the inventory's internals, so in a networked session
    /// they take the same server-authoritative route a picked-up item does and every other peer
    /// sees the result.
    /// </para>
    /// </summary>
    public class DevInventoryUI : MonoBehaviour
    {
        private const float PanelWidth = 1180f;
        private const float PanelHeight = 740f;
        private const float CardWidth = 176f;
        private const float CardHeight = 196f;
        private const float CardSpacing = 14f;
        private const float OpenSeconds = 0.14f;

        private static DevInventoryUI instance;

        public static bool IsOpen => instance != null && instance.open;

        private InputControls inputs;

        private bool open;
        private bool built;
        private float visibility;

        private CanvasGroup group;
        private RectTransform panel;
        private RectTransform grid;
        private TMP_InputField search;
        private TextMeshProUGUI statusLabel;
        private RectTransform hotbarStrip;

        private readonly List<Card> cards = new();
        private readonly List<HotbarCell> hotbarCells = new();

        private IPlayerInventory inventory;

        private struct Card
        {
            public GameObject Host;
            public InventoryItem Item;
            public Image Frame;
            public Image Icon;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI State;
        }

        private struct HotbarCell
        {
            public Image Frame;
            public TextMeshProUGUI Index;
            public TextMeshProUGUI Name;
        }

        // ------------------------------------------------------------------ bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject("DevInventory");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DevInventoryUI>();
        }

        /// <summary>Opens the browser from elsewhere — the pause menu's dev page does this.</summary>
        public static void Show()
        {
            if (instance == null) Bootstrap();
            instance.Open();
        }

        /// <summary>Closes it from elsewhere, e.g. when developer mode is switched back off.</summary>
        public static void Hide() => instance?.Close();

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            inputs = new InputControls();
            inputs.UI.DevInventory.performed += _ => Toggle();
            inputs.UI.Enable();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;

            if (open && inventory != null)
            {
                inventory.OnSlotChanged -= OnSlotChanged;
                inventory.OnSlotSelected -= OnSlotSelected;
            }

            if (inputs != null)
            {
                inputs.UI.Disable();
                inputs.Dispose();
            }

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

        public void Open()
        {
            if (open || !GameSettings.DevMode) return;

            PlayerController player = GameplayMenuScope.FindLocalPlayer();
            if (player == null) return;

            inventory = player.PlayerInventory;
            if (inventory == null)
            {
                Debug.LogWarning("[DevInventory] Local player has no inventory to edit.", this);
                return;
            }

            if (!GameplayMenuScope.Enter(this)) return;

            open = true;

            UIBuilder.EnsureEventSystem();
            if (!built) Build();

            group.gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;

            // The hotbar is server state in a networked session, so a click here is a request and
            // the answer arrives later over the wire. Redrawing on the inventory's own change
            // event rather than after a fixed delay means the grid updates when the change
            // actually lands, and also when another system moves an item.
            inventory.OnSlotChanged += OnSlotChanged;
            inventory.OnSlotSelected += OnSlotSelected;

            RebuildCatalogue();
            Refresh();
        }

        public void Close()
        {
            if (!open) return;

            open = false;
            group.blocksRaycasts = false;
            group.interactable = false;

            if (inventory != null)
            {
                inventory.OnSlotChanged -= OnSlotChanged;
                inventory.OnSlotSelected -= OnSlotSelected;
            }

            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            GameplayMenuScope.Exit(this);
        }

        private void OnSlotChanged(int index, InventorySlot slot) => Refresh();

        private void OnSlotSelected(InventorySlot slot) => Refresh();

        private void Update()
        {
            if (!built) return;

            float target = open ? 1f : 0f;
            if (Mathf.Approximately(visibility, target)) return;

            visibility = Mathf.MoveTowards(visibility, target, Time.unscaledDeltaTime / OpenSeconds);

            float eased = visibility * visibility * (3f - 2f * visibility);
            group.alpha = eased;
            panel.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, eased);

            if (visibility <= 0f) group.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------- actions

        /// <summary>
        /// One click toggles: an item already on the hotbar comes off the slot it is in, one that
        /// is not goes into the first free slot. Nothing is ever silently overwritten — a full
        /// hotbar says so instead.
        /// </summary>
        private void ToggleItem(InventoryItem item)
        {
            if (item == null || inventory == null) return;

            int slot = FindSlot(item);
            if (slot >= 0)
            {
                inventory.TryRemoveItem(slot);
                Status($"Removed {DisplayName(item)} from slot {slot + 1}.");
            }
            else if (FindEmptySlot() < 0)
            {
                Status("Hotbar is full — take something off first.", UITheme.AccentWarm);
            }
            else
            {
                inventory.TryAddItem(item);
                Status($"Equipped {DisplayName(item)}.");
            }

            Refresh();
        }

        private void ClearHotbar()
        {
            if (inventory == null) return;

            for (int i = 0; i < inventory.GetInventorySize(); i++)
                inventory.TryRemoveItem(i);

            Status("Hotbar cleared.");
            Refresh();
        }

        private int FindSlot(InventoryItem item)
        {
            if (inventory == null || item == null) return -1;

            for (int i = 0; i < inventory.GetInventorySize(); i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (slot != null && !slot.IsEmpty && slot.Item == item) return i;
            }

            return -1;
        }

        private int FindEmptySlot()
        {
            for (int i = 0; i < inventory.GetInventorySize(); i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (slot != null && slot.IsEmpty) return i;
            }

            return -1;
        }

        private void Status(string message, Color? color = null)
        {
            if (statusLabel == null) return;

            statusLabel.text = message;
            statusLabel.color = color ?? UITheme.Faint;
        }

        // --------------------------------------------------------------------- build

        private void Build()
        {
            built = true;

            var canvasGo = new GameObject("DevInventoryCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2100; // above the pause menu, which can open it

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            group = canvasGo.GetComponent<CanvasGroup>();
            group.alpha = 0f;

            var root = (RectTransform)canvasGo.transform;
            UIBuilder.Solid(UIBuilder.Fill(UIBuilder.Rect("Backdrop", root)), UITheme.Backdrop).raycastTarget = true;

            panel = UIBuilder.Rect("Panel", root);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Fill", panel)), UITheme.PanelSprite, UITheme.Panel);

            BuildHeader(panel);
            BuildGrid(panel);
            BuildFooter(panel);

            visibility = 0f;
            group.gameObject.SetActive(false);
        }

        private void BuildHeader(RectTransform host)
        {
            var header = UIBuilder.Rect("Header", host);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, 108f);

            var titleRect = UIBuilder.LeftColumn(UIBuilder.Rect("Title", header), 34f, 460f);
            UIBuilder.Label(titleRect, "ARTIFACT BROWSER", 38, UITheme.Bright, TextAlignmentOptions.Left,
                FontStyles.Bold);

            var badgeRect = UIBuilder.Rect("Badge", header);
            badgeRect.anchorMin = new Vector2(0f, 0.5f);
            badgeRect.anchorMax = new Vector2(0f, 0.5f);
            badgeRect.pivot = new Vector2(0f, 0.5f);
            badgeRect.sizeDelta = new Vector2(78f, 26f);
            badgeRect.anchoredPosition = new Vector2(392f, 2f);
            UIBuilder.Sprite(badgeRect, UITheme.Rounded(6), new Color(UITheme.AccentWarm.r, UITheme.AccentWarm.g,
                UITheme.AccentWarm.b, 0.18f));
            UIBuilder.LabelIn(badgeRect, "Text", "DEV", UITheme.CaptionSize, UITheme.AccentWarm,
                TextAlignmentOptions.Center, FontStyles.Bold);

            // Search field, right of the title.
            var fieldRect = UIBuilder.Rect("Search", header);
            fieldRect.anchorMin = new Vector2(1f, 0.5f);
            fieldRect.anchorMax = new Vector2(1f, 0.5f);
            fieldRect.pivot = new Vector2(1f, 0.5f);
            fieldRect.sizeDelta = new Vector2(360f, 40f);
            fieldRect.anchoredPosition = new Vector2(-34f, 0f);

            Image fieldBackground = UIBuilder.Sprite(fieldRect, UITheme.ChipSprite, new Color(1f, 1f, 1f, 0.07f));
            fieldBackground.raycastTarget = true;

            var viewport = UIBuilder.Fill(UIBuilder.Rect("Text Area", fieldRect), 14f, 4f, 14f, 4f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var text = UIBuilder.Label(UIBuilder.Fill(UIBuilder.Rect("Text", viewport)), string.Empty,
                UITheme.ValueSize, UITheme.Bright);
            var placeholder = UIBuilder.Label(UIBuilder.Fill(UIBuilder.Rect("Placeholder", viewport)),
                "Search artifacts…", UITheme.ValueSize, UITheme.Faint);
            placeholder.fontStyle = FontStyles.Italic;

            search = fieldRect.gameObject.AddComponent<TMP_InputField>();
            search.textViewport = viewport;
            search.textComponent = text;
            search.placeholder = placeholder;
            search.targetGraphic = fieldBackground;
            search.lineType = TMP_InputField.LineType.SingleLine;
            search.caretColor = UITheme.Accent;
            search.customCaretColor = true;
            search.selectionColor = UITheme.AccentSoft;
            search.onValueChanged.AddListener(_ => Refresh());

            UIBuilder.Solid(UIBuilder.Fill(UIBuilder.Rect("Rule", header), 34f, 0f, 34f, 107f), UITheme.Hairline);
        }

        private void BuildGrid(RectTransform host)
        {
            var viewport = UIBuilder.Fill(UIBuilder.Rect("Viewport", host), 26f, 168f, 26f, 112f);
            viewport.gameObject.AddComponent<RectMask2D>();

            grid = UIBuilder.Rect("Grid", viewport);
            grid.anchorMin = new Vector2(0f, 1f);
            grid.anchorMax = new Vector2(1f, 1f);
            grid.pivot = new Vector2(0.5f, 1f);

            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(CardWidth, CardHeight);
            layout.spacing = new Vector2(CardSpacing, CardSpacing);
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.constraint = GridLayoutGroup.Constraint.Flexible;

            var fitter = grid.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = host.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = grid;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;
        }

        private void BuildFooter(RectTransform host)
        {
            hotbarStrip = UIBuilder.Rect("Hotbar", host);
            hotbarStrip.anchorMin = new Vector2(0f, 0f);
            hotbarStrip.anchorMax = new Vector2(1f, 0f);
            hotbarStrip.pivot = new Vector2(0.5f, 0f);
            hotbarStrip.sizeDelta = new Vector2(-68f, 62f);
            hotbarStrip.anchoredPosition = new Vector2(0f, 46f);

            var strip = hotbarStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
            strip.spacing = 8f;
            strip.childControlWidth = true;
            strip.childControlHeight = true;
            strip.childForceExpandWidth = true;
            strip.childForceExpandHeight = true;

            var status = UIBuilder.Rect("Status", host);
            status.anchorMin = new Vector2(0f, 0f);
            status.anchorMax = new Vector2(1f, 0f);
            status.pivot = new Vector2(0.5f, 0f);
            status.sizeDelta = new Vector2(-68f, 30f);
            status.anchoredPosition = new Vector2(0f, 12f);
            statusLabel = UIBuilder.LabelIn(status, "Text",
                "Click an artifact to equip it, click it again to take it off.", UITheme.CaptionSize, UITheme.Faint);

            var clear = UIBuilder.Rect("Clear", host);
            clear.anchorMin = new Vector2(1f, 0f);
            clear.anchorMax = new Vector2(1f, 0f);
            clear.pivot = new Vector2(1f, 0f);
            clear.sizeDelta = new Vector2(150f, 30f);
            clear.anchoredPosition = new Vector2(-34f, 12f);
            Image clearBackground = UIBuilder.Sprite(clear, UITheme.ChipSprite, Color.white);
            UIBuilder.LabelIn(clear, "Text", "Clear hotbar", UITheme.CaptionSize, UITheme.Danger,
                TextAlignmentOptions.Center, FontStyles.Bold);
            UIBuilder.Clickable(clear, clearBackground,
                    new Color(UITheme.Danger.r, UITheme.Danger.g, UITheme.Danger.b, 0.12f),
                    new Color(UITheme.Danger.r, UITheme.Danger.g, UITheme.Danger.b, 0.28f))
                .onClick.AddListener(ClearHotbar);

            var close = UIBuilder.Rect("Close", host);
            close.anchorMin = new Vector2(1f, 1f);
            close.anchorMax = new Vector2(1f, 1f);
            close.pivot = new Vector2(1f, 1f);
            close.sizeDelta = new Vector2(150f, 34f);
            close.anchoredPosition = new Vector2(-34f, -58f);
            Image closeBackground = UIBuilder.Sprite(close, UITheme.ChipSprite, Color.white);
            UIBuilder.LabelIn(close, "Text", "I  ·  Close", UITheme.CaptionSize, UITheme.Muted,
                TextAlignmentOptions.Center, FontStyles.Bold);
            UIBuilder.Clickable(close, closeBackground, new Color(1f, 1f, 1f, 0.06f), new Color(1f, 1f, 1f, 0.16f))
                .onClick.AddListener(Close);
        }

        // ------------------------------------------------------------------ contents

        /// <summary>
        /// Builds one card per registered item. The registry is filled once at startup by
        /// RegistryLoader, so this only has to run when the browser is opened rather than on every
        /// refresh.
        /// </summary>
        private void RebuildCatalogue()
        {
            foreach (Card card in cards)
                Destroy(card.Host);

            cards.Clear();

            var items = new List<InventoryItem>(Registry<InventoryItem>.All);
            items.Sort((a, b) => string.Compare(DisplayName(a), DisplayName(b), System.StringComparison.OrdinalIgnoreCase));

            foreach (InventoryItem item in items)
            {
                if (item == null) continue;
                cards.Add(BuildCard(item));
            }

            if (cards.Count == 0)
                Status("No items registered — RegistryLoader loads them from Resources/Items.", UITheme.AccentWarm);

            BuildHotbarCells();
        }

        private Card BuildCard(InventoryItem item)
        {
            var rect = UIBuilder.Rect(DisplayName(item), grid);

            Image frame = UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Frame", rect)), UITheme.ChipSprite,
                new Color(1f, 1f, 1f, 0.05f));

            var iconRect = UIBuilder.Rect("Icon", rect);
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.sizeDelta = new Vector2(96f, 96f);
            iconRect.anchoredPosition = new Vector2(0f, -18f);

            Image icon = item.icon != null
                ? UIBuilder.Sprite(iconRect, item.icon, Color.white, Image.Type.Simple)
                : UIBuilder.Sprite(iconRect, UITheme.Rounded(10), new Color(1f, 1f, 1f, 0.08f));

            // Without an icon the card would be a blank square, so the item's initial stands in.
            if (item.icon == null)
            {
                UIBuilder.LabelIn(iconRect, "Initial", InitialOf(item), 44, UITheme.Faint,
                    TextAlignmentOptions.Center, FontStyles.Bold);
            }

            var nameRect = UIBuilder.Rect("Name", rect);
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 0f);
            nameRect.pivot = new Vector2(0.5f, 0f);
            nameRect.sizeDelta = new Vector2(-16f, 40f);
            nameRect.anchoredPosition = new Vector2(0f, 34f);
            TextMeshProUGUI name = UIBuilder.Label(nameRect, DisplayName(item), UITheme.CaptionSize, UITheme.Bright,
                TextAlignmentOptions.Center, FontStyles.Bold);
            name.enableWordWrapping = true;

            var stateRect = UIBuilder.Rect("State", rect);
            stateRect.anchorMin = new Vector2(0f, 0f);
            stateRect.anchorMax = new Vector2(1f, 0f);
            stateRect.pivot = new Vector2(0.5f, 0f);
            stateRect.sizeDelta = new Vector2(-16f, 26f);
            stateRect.anchoredPosition = new Vector2(0f, 10f);
            TextMeshProUGUI state = UIBuilder.Label(stateRect, string.Empty, UITheme.CaptionSize, UITheme.Faint,
                TextAlignmentOptions.Center);

            Image hit = UIBuilder.HitArea(rect);
            UIBuilder.Clickable(rect, hit, new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.07f))
                .onClick.AddListener(() => ToggleItem(item));

            return new Card { Host = rect.gameObject, Item = item, Frame = frame, Icon = icon, Name = name, State = state };
        }

        private void BuildHotbarCells()
        {
            foreach (Transform child in hotbarStrip)
                Destroy(child.gameObject);

            hotbarCells.Clear();
            if (inventory == null) return;

            for (int i = 0; i < inventory.GetInventorySize(); i++)
            {
                int index = i;

                var cell = UIBuilder.Rect($"Slot{i + 1}", hotbarStrip);
                Image frame = UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Frame", cell)), UITheme.ChipSprite,
                    new Color(1f, 1f, 1f, 0.05f));

                TextMeshProUGUI number = UIBuilder.Label(
                    UIBuilder.LeftColumn(UIBuilder.Rect("Index", cell), 12f, 26f),
                    (i + 1).ToString(), UITheme.CaptionSize, UITheme.Faint);

                TextMeshProUGUI label = UIBuilder.Label(
                    UIBuilder.Fill(UIBuilder.Rect("Name", cell), 40f, 0f, 12f, 0f),
                    "empty", UITheme.CaptionSize, UITheme.Faint);

                Image hit = UIBuilder.HitArea(cell);
                UIBuilder.Clickable(cell, hit, new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.07f))
                    .onClick.AddListener(() => ClearSlot(index));

                hotbarCells.Add(new HotbarCell { Frame = frame, Index = number, Name = label });
            }
        }

        private void ClearSlot(int index)
        {
            InventorySlot slot = inventory?.GetSlot(index);
            if (slot == null || slot.IsEmpty) return;

            string name = DisplayName(slot.Item);
            inventory.TryRemoveItem(index);
            Status($"Removed {name} from slot {index + 1}.");
            Refresh();
        }

        /// <summary>Re-reads the hotbar and the search box, and restyles every card to match.</summary>
        private void Refresh()
        {
            if (!built || inventory == null) return;

            string filter = search != null ? search.text.Trim() : string.Empty;

            foreach (Card card in cards)
            {
                bool matches = filter.Length == 0 ||
                               DisplayName(card.Item).IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0;

                card.Host.SetActive(matches);
                if (!matches) continue;

                int slot = FindSlot(card.Item);
                bool equipped = slot >= 0;

                card.Frame.color = equipped ? UITheme.AccentSoft : new Color(1f, 1f, 1f, 0.05f);
                card.Name.color = equipped ? UITheme.Bright : UITheme.Muted;
                card.State.text = equipped ? $"SLOT {slot + 1}" : "not equipped";
                card.State.color = equipped ? UITheme.Accent : UITheme.Faint;

                // Only real icons are dimmed. The placeholder square carries its own tint and
                // washing it out again would make the initial unreadable.
                if (card.Item.icon != null)
                    card.Icon.color = equipped ? Color.white : new Color(1f, 1f, 1f, 0.7f);
            }

            for (int i = 0; i < hotbarCells.Count; i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                bool filled = slot != null && !slot.IsEmpty;

                HotbarCell cell = hotbarCells[i];
                cell.Name.text = filled ? DisplayName(slot.Item) : "empty";
                cell.Name.color = filled ? UITheme.Bright : UITheme.Faint;
                cell.Frame.color = filled ? UITheme.AccentSoft : new Color(1f, 1f, 1f, 0.05f);
                cell.Index.color = i == inventory.SelectedSlotIndex ? UITheme.AccentWarm : UITheme.Faint;
            }
        }

        private static string DisplayName(InventoryItem item)
        {
            if (item == null) return "—";
            return string.IsNullOrWhiteSpace(item.itemName) ? item.name : item.itemName;
        }

        private static string InitialOf(InventoryItem item)
        {
            string name = DisplayName(item);
            return string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant();
        }
    }
}
