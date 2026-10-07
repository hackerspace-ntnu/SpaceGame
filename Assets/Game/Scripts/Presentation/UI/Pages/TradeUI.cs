// The trading screen: their offers on the left, what you are carrying on the right.
//
// Built in code with UIBuilder rather than authored as a prefab, following DevInventoryUI — which
// solves the same problem (a full-screen inventory panel that can open over gameplay from
// anywhere) and gets the two things that are easy to get wrong for free: GameplayMenuScope, which
// frees the cursor and stops PlayerLook re-locking it every frame, and EnsureEventSystem, without
// which nothing on the panel is clickable in a gameplay scene.
//
// One click per row, because an offer is a whole swap. There is no quantity picker and no basket:
// the game has no currency and Inventory has no stacking, so a trade is "this for that" and the
// interface should not pretend otherwise.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Trading;
using SpaceGame.Items;

namespace SpaceGame.Presentation
{
    public class TradeUI : MonoBehaviour
    {
        private const float PanelWidth = 1180f;
        private const float PanelHeight = 700f;
        private const float RowHeight = 92f;
        private const float RowSpacing = 10f;
        private const float OpenSeconds = 0.14f;

        private static TradeUI instance;

        public static bool IsOpen => instance != null && instance.open;

        private bool open;
        private bool built;
        private float visibility;

        private CanvasGroup group;
        private RectTransform panel;
        private RectTransform offerColumn;
        private RectTransform bagColumn;
        private TextMeshProUGUI titleLabel;
        private TextMeshProUGUI statusLabel;

        private TraderInteraction trader;
        private IPlayerInventory inventory;
        private Action onClosed;

        private readonly List<OfferRow> rows = new();
        private readonly List<BagRow> bagRows = new();

        private struct OfferRow
        {
            public GameObject Host;
            public Button Button;
            public Image Frame;
            public TextMeshProUGUI Pitch;
            public TextMeshProUGUI Swap;
            public TextMeshProUGUI State;
            public int Index;
        }

        private struct BagRow
        {
            public GameObject Host;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Count;
        }

        // ── Opening ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Open the panel for one trader. <paramref name="onClosed"/> fires however the panel
        /// closes — button, Escape, or the trader being destroyed — so the caller can drop the
        /// session flag without having to poll for it.
        /// </summary>
        public static void Open(TraderInteraction trader, Interactor interactor, Action onClosed = null)
        {
            if (trader == null) return;

            if (instance == null)
            {
                var go = new GameObject("TradeUI");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<TradeUI>();
            }

            instance.OpenFor(trader, interactor, onClosed);
        }

        public static void Close()
        {
            if (instance != null) instance.CloseInternal();
        }

        private void OpenFor(TraderInteraction newTrader, Interactor interactor, Action closedCallback)
        {
            if (open) CloseInternal();

            PlayerController player = interactor != null
                ? interactor.GetComponentInParent<PlayerController>()
                : GameplayMenuScope.FindLocalPlayer();

            if (player == null) return;

            inventory = player.PlayerInventory;
            if (inventory == null) return;

            if (!GameplayMenuScope.Enter(this)) return;

            trader = newTrader;
            onClosed = closedCallback;
            open = true;

            UIBuilder.EnsureEventSystem();
            if (!built) Build();

            group.gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;

            // In a networked session the hotbar is server state, so a trade is a request whose
            // answer lands later. Redrawing on the inventory's own event means the panel updates
            // when the swap actually happens rather than after a guessed delay.
            inventory.OnSlotChanged += OnSlotChanged;

            titleLabel.text = trader.DisplayName.ToUpperInvariant();
            RebuildOffers();
            Refresh();
            Status("Click an offer to make the swap.");
        }

        private void CloseInternal()
        {
            if (!open) return;

            open = false;
            group.blocksRaycasts = false;
            group.interactable = false;

            if (inventory != null) inventory.OnSlotChanged -= OnSlotChanged;

            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            GameplayMenuScope.Exit(this);

            trader = null;
            inventory = null;

            Action callback = onClosed;
            onClosed = null;
            callback?.Invoke();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;

            if (open && inventory != null) inventory.OnSlotChanged -= OnSlotChanged;

            GameplayMenuScope.Exit(this);
        }

        private void OnSlotChanged(int index, InventorySlot slot) => Refresh();

        private void Update()
        {
            if (open)
            {
                // The trader walking off, dying or having its chunk unloaded closes the panel.
                // Without this the player is left holding a screen that can no longer trade with
                // anybody, and the only clue is that clicking does nothing.
                if (trader == null)
                {
                    CloseInternal();
                }
                else if (Keyboard.current != null &&
                         (Keyboard.current.escapeKey.wasPressedThisFrame ||
                          Keyboard.current.tabKey.wasPressedThisFrame))
                {
                    CloseInternal();
                }
            }

            if (!built) return;

            float target = open ? 1f : 0f;
            if (Mathf.Approximately(visibility, target)) return;

            // Unscaled, because GameplayMenuScope stops the clock in a solo session and a scaled
            // fade would therefore never finish.
            visibility = Mathf.MoveTowards(visibility, target, Time.unscaledDeltaTime / OpenSeconds);

            float eased = visibility * visibility * (3f - 2f * visibility);
            group.alpha = eased;
            panel.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, eased);

            if (visibility <= 0f) group.gameObject.SetActive(false);
        }

        // ── Trading ──────────────────────────────────────────────────────────────

        private void Accept(int offerIndex)
        {
            if (trader == null || inventory == null) return;

            if (!trader.TryGetOffer(offerIndex, out TradeOffer offer))
                return;

            if (!trader.CanAfford(offerIndex, inventory))
            {
                Status(offer.InStock
                        ? $"You need {Describe(offer.wants, offer.wantsCount)} — and room for what comes back."
                        : "They're out of those.",
                    UITheme.AccentWarm);
                return;
            }

            PlayerController player = GameplayMenuScope.FindLocalPlayer();

            if (trader.TryExecute(offerIndex, inventory, player != null ? player.gameObject : null))
                Status($"Traded {Describe(offer.wants, offer.wantsCount)} for {Describe(offer.gives, offer.givesCount)}.");
            else
                Status("That trade didn't go through.", UITheme.Danger);

            RebuildOffers();
            Refresh();
        }

        private static string Describe(InventoryItem item, int count)
        {
            if (item == null) return "nothing";
            return count > 1 ? $"{count}x {item.itemName}" : item.itemName;
        }

        private void Status(string message, Color? color = null)
        {
            if (statusLabel == null) return;

            statusLabel.text = message;
            statusLabel.color = color ?? UITheme.Faint;
        }

        // ── Redraw ───────────────────────────────────────────────────────────────

        private void RebuildOffers()
        {
            foreach (OfferRow row in rows)
                if (row.Host != null) Destroy(row.Host);

            rows.Clear();

            if (trader == null) return;

            IReadOnlyList<TradeOffer> offers = trader.Offers;
            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i] == null || !offers[i].IsValid) continue;
                rows.Add(BuildOfferRow(offers[i], i));
            }

            if (rows.Count == 0)
                Status("They have nothing to trade.", UITheme.AccentWarm);
        }

        private void Refresh()
        {
            RefreshOffers();
            RefreshBag();
        }

        private void RefreshOffers()
        {
            if (trader == null || inventory == null) return;

            foreach (OfferRow row in rows)
            {
                if (!trader.TryGetOffer(row.Index, out TradeOffer offer)) continue;

                bool affordable = trader.CanAfford(row.Index, inventory);
                int held = TraderInteraction.CountHeld(inventory, offer.wants);

                row.Swap.text = offer.Summary();

                string stockText = offer.stock < 0 ? string.Empty : $"   ·   {offer.stock} left";

                row.State.text = !offer.InStock
                    ? "Out of stock"
                    : $"You have {held}/{offer.wantsCount}{stockText}";

                row.State.color = !offer.InStock
                    ? UITheme.Faint
                    : affordable ? UITheme.Accent : UITheme.AccentWarm;

                row.Frame.color = affordable
                    ? new Color(UITheme.Accent.r, UITheme.Accent.g, UITheme.Accent.b, 0.14f)
                    : new Color(1f, 1f, 1f, 0.05f);

                row.Button.interactable = affordable;
                row.Pitch.color = affordable ? UITheme.Bright : UITheme.Muted;
            }
        }

        private void RefreshBag()
        {
            foreach (BagRow row in bagRows)
                if (row.Host != null) Destroy(row.Host);

            bagRows.Clear();

            if (inventory == null) return;

            // Collapsed by item, because the bag is a list of slots and showing "Scrap Plate" three
            // times as three rows makes the one number the player is actually reading — how many
            // they have — something they have to count.
            var counts = new Dictionary<InventoryItem, int>();
            var order = new List<InventoryItem>();

            for (int i = 0; i < inventory.GetInventorySize(); i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (slot == null || slot.IsEmpty) continue;

                if (counts.TryGetValue(slot.Item, out int existing))
                {
                    counts[slot.Item] = existing + 1;
                }
                else
                {
                    counts[slot.Item] = 1;
                    order.Add(slot.Item);
                }
            }

            foreach (InventoryItem item in order)
                bagRows.Add(BuildBagRow(item, counts[item]));

            if (order.Count == 0)
                bagRows.Add(BuildBagRow(null, 0));
        }

        // ── Build ────────────────────────────────────────────────────────────────

        private void Build()
        {
            built = true;

            var canvasGo = new GameObject("TradeCanvas", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2050;

            UIScale.Configure(canvasGo.GetComponent<CanvasScaler>());

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
            BuildColumns(panel);
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
            header.sizeDelta = new Vector2(0f, 96f);

            var titleRect = UIBuilder.LeftColumn(UIBuilder.Rect("Title", header), 34f, 620f);
            titleLabel = UIBuilder.Label(titleRect, "TRADE", 38, UITheme.Bright,
                TextAlignmentOptions.Left, FontStyles.Bold);

            var closeRect = UIBuilder.Rect("Close", header);
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(150f, 42f);
            closeRect.anchoredPosition = new Vector2(-34f, 0f);

            Image closeFill = UIBuilder.Sprite(closeRect, UITheme.ChipSprite, Color.white);
            UIBuilder.LabelIn(closeRect, "Text", "DONE  (Esc)", UITheme.CaptionSize, UITheme.Bright,
                TextAlignmentOptions.Center, FontStyles.Bold);

            UIBuilder.Clickable(closeRect, closeFill,
                    new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.18f))
                .onClick.AddListener(CloseInternal);

            UIBuilder.Solid(UIBuilder.Fill(UIBuilder.Rect("Rule", header), 34f, 0f, 34f, 95f), UITheme.Hairline);
        }

        private void BuildColumns(RectTransform host)
        {
            // Their side.
            var offerViewport = UIBuilder.Rect("Offers", host);
            offerViewport.anchorMin = new Vector2(0f, 0f);
            offerViewport.anchorMax = new Vector2(0.66f, 1f);
            offerViewport.offsetMin = new Vector2(30f, 96f);
            offerViewport.offsetMax = new Vector2(-12f, -104f);
            offerViewport.gameObject.AddComponent<RectMask2D>();

            UIBuilder.LabelIn(HeadingRect(host, 0f, 0.66f, 30f, -12f), "Text", "THEY'RE OFFERING",
                UITheme.CaptionSize, UITheme.Faint, TextAlignmentOptions.Left, FontStyles.Bold);

            offerColumn = UIBuilder.Rect("Column", offerViewport);
            offerColumn.anchorMin = new Vector2(0f, 1f);
            offerColumn.anchorMax = new Vector2(1f, 1f);
            offerColumn.pivot = new Vector2(0.5f, 1f);
            offerColumn.sizeDelta = new Vector2(0f, 0f);
            UIBuilder.Column(offerColumn, RowSpacing);
            offerColumn.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            // Your side.
            var bagViewport = UIBuilder.Rect("Bag", host);
            bagViewport.anchorMin = new Vector2(0.66f, 0f);
            bagViewport.anchorMax = new Vector2(1f, 1f);
            bagViewport.offsetMin = new Vector2(12f, 96f);
            bagViewport.offsetMax = new Vector2(-30f, -104f);
            bagViewport.gameObject.AddComponent<RectMask2D>();

            UIBuilder.LabelIn(HeadingRect(host, 0.66f, 1f, 12f, -30f), "Text", "YOU'RE CARRYING",
                UITheme.CaptionSize, UITheme.Faint, TextAlignmentOptions.Left, FontStyles.Bold);

            bagColumn = UIBuilder.Rect("Column", bagViewport);
            bagColumn.anchorMin = new Vector2(0f, 1f);
            bagColumn.anchorMax = new Vector2(1f, 1f);
            bagColumn.pivot = new Vector2(0.5f, 1f);
            UIBuilder.Column(bagColumn, 6f);
            bagColumn.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>
        /// A section heading pinned under the header, spanning one column.
        ///
        /// offsetMin/offsetMax rather than sizeDelta + anchoredPosition: with stretched anchors
        /// those two describe the same rect through different arithmetic, and setting one after the
        /// other silently discards the first. The offsets say what is meant directly.
        /// </summary>
        private static RectTransform HeadingRect(RectTransform host, float anchorMinX, float anchorMaxX,
                                                 float padLeft, float padRight)
        {
            var rect = UIBuilder.Rect("Heading", host);
            rect.anchorMin = new Vector2(anchorMinX, 1f);
            rect.anchorMax = new Vector2(anchorMaxX, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(padLeft, -126f);
            rect.offsetMax = new Vector2(padRight, -100f);
            return rect;
        }

        private void BuildFooter(RectTransform host)
        {
            var footer = UIBuilder.Rect("Footer", host);
            footer.anchorMin = new Vector2(0f, 0f);
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.sizeDelta = new Vector2(0f, 74f);

            UIBuilder.Solid(UIBuilder.Fill(UIBuilder.Rect("Rule", footer), 34f, 73f, 34f, 0f), UITheme.Hairline);

            statusLabel = UIBuilder.Label(UIBuilder.LeftColumn(UIBuilder.Rect("Status", footer), 34f, 1000f),
                string.Empty, UITheme.CaptionSize, UITheme.Faint, TextAlignmentOptions.Left);
        }

        private OfferRow BuildOfferRow(TradeOffer offer, int index)
        {
            var rect = UIBuilder.Rect($"Offer{index}", offerColumn);
            UIBuilder.FixedHeight(rect, RowHeight);

            Image frame = UIBuilder.Sprite(UIBuilder.Fill(UIBuilder.Rect("Frame", rect)),
                UITheme.ChipSprite, new Color(1f, 1f, 1f, 0.05f));

            var pitchRect = UIBuilder.Rect("Pitch", rect);
            pitchRect.anchorMin = new Vector2(0f, 1f);
            pitchRect.anchorMax = new Vector2(1f, 1f);
            pitchRect.pivot = new Vector2(0.5f, 1f);
            pitchRect.sizeDelta = new Vector2(-36f, 30f);
            pitchRect.anchoredPosition = new Vector2(0f, -12f);

            string pitch = string.IsNullOrWhiteSpace(offer.pitch) ? offer.Summary() : offer.pitch;
            TextMeshProUGUI pitchLabel = UIBuilder.Label(pitchRect, pitch, UITheme.LabelSize,
                UITheme.Bright, TextAlignmentOptions.Left);

            var swapRect = UIBuilder.Rect("Swap", rect);
            swapRect.anchorMin = new Vector2(0f, 0f);
            swapRect.anchorMax = new Vector2(0.6f, 0f);
            swapRect.pivot = new Vector2(0f, 0f);
            swapRect.sizeDelta = new Vector2(0f, 26f);
            swapRect.anchoredPosition = new Vector2(18f, 14f);
            TextMeshProUGUI swapLabel = UIBuilder.Label(swapRect, offer.Summary(), UITheme.CaptionSize,
                UITheme.Muted, TextAlignmentOptions.Left);

            var stateRect = UIBuilder.Rect("State", rect);
            stateRect.anchorMin = new Vector2(0.6f, 0f);
            stateRect.anchorMax = new Vector2(1f, 0f);
            stateRect.pivot = new Vector2(1f, 0f);
            stateRect.sizeDelta = new Vector2(0f, 26f);
            stateRect.anchoredPosition = new Vector2(-18f, 14f);
            TextMeshProUGUI stateLabel = UIBuilder.Label(stateRect, string.Empty, UITheme.CaptionSize,
                UITheme.Faint, TextAlignmentOptions.Right);

            Button button = UIBuilder.Clickable(rect, frame,
                new Color(1f, 1f, 1f, 0.05f), new Color(UITheme.Accent.r, UITheme.Accent.g, UITheme.Accent.b, 0.24f));

            int captured = index;
            button.onClick.AddListener(() => Accept(captured));

            return new OfferRow
            {
                Host = rect.gameObject,
                Button = button,
                Frame = frame,
                Pitch = pitchLabel,
                Swap = swapLabel,
                State = stateLabel,
                Index = index,
            };
        }

        private BagRow BuildBagRow(InventoryItem item, int count)
        {
            var rect = UIBuilder.Rect(item != null ? item.itemName : "Empty", bagColumn);
            UIBuilder.FixedHeight(rect, 34f);

            var nameRect = UIBuilder.Rect("Name", rect);
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(0.75f, 1f);
            nameRect.offsetMin = new Vector2(14f, 0f);
            nameRect.offsetMax = Vector2.zero;

            TextMeshProUGUI nameLabel = UIBuilder.Label(nameRect,
                item != null ? item.itemName : "(nothing)", UITheme.CaptionSize,
                item != null ? UITheme.Muted : UITheme.Faint, TextAlignmentOptions.Left);

            var countRect = UIBuilder.Rect("Count", rect);
            countRect.anchorMin = new Vector2(0.75f, 0f);
            countRect.anchorMax = new Vector2(1f, 1f);
            countRect.offsetMin = Vector2.zero;
            countRect.offsetMax = new Vector2(-14f, 0f);

            TextMeshProUGUI countLabel = UIBuilder.Label(countRect,
                item != null ? $"x{count}" : string.Empty, UITheme.CaptionSize, UITheme.Faint,
                TextAlignmentOptions.Right);

            return new BagRow { Host = rect.gameObject, Name = nameLabel, Count = countLabel };
        }
    }
}
