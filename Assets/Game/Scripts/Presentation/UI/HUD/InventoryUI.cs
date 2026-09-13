using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The hotbar: four slots across the bottom of the screen, and the bridge that lets items
    /// move between them and an open backpack.
    ///
    /// <para>
    /// <b>The bar draws itself.</b> <c>Slot.prefab</c> is no longer instantiated — see
    /// <see cref="InventorySlotUI.Build"/> for why — so the only things this needs from the prefab
    /// are the rect it lives in and the layout group on it.
    /// </para>
    /// <para>
    /// <b>The click bridge.</b> One verb crosses this boundary in both directions and it is
    /// resolved by <c>PackHandController</c>, not here: a click on a slot either lifts its item
    /// into the player's hand or puts what is already in that hand into it. This side owns which
    /// slot the cursor is over, what the bar looks like while something is in hand, and a slot's
    /// refusal shake. What is IN the hand is drawn by the pack's own true-size copy, everywhere
    /// on screen — the bar never draws a stand-in icon. Every decision about whether a placement
    /// is legal, and every request that goes to the server, is the pack's.
    /// </para>
    /// <para>
    /// The static members exist because the pack has no way to reach this instance: the hotbar is a
    /// prefab under the player, and the hand is added at runtime to a camera that did not exist a
    /// frame earlier. There is one local player and therefore one of these.
    /// </para>
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        /// <summary>The bar on this machine's screen, or null before it has resolved an inventory.</summary>
        private static InventoryUI local;

        private InventorySlotUI[] slotUIs;

        [Tooltip("Whose hotbar this is. Left empty it is resolved from the parent chain, which is " +
                 "where the HUD lives on this project's player.")]
        [SerializeField] private PlayerController player;

        private IPlayerInventory playerInventory;

        // There is no slotPrefab field any more, and Slot.prefab is no longer instantiated: the
        // bar builds its own slots. InventoryUI.prefab still carries the old reference in its
        // YAML, which Unity drops the next time it saves the asset.

        [SerializeField] private Transform inventoryGrid;

        private int selectedIndex = -1;
        private int hoveredIndex = -1;

        /// <summary>The slot whose item is in the player's hand, drawn empty-but-reserved. -1 when
        /// the hand is empty.</summary>
        private int heldOriginIndex = -1;

        /// <summary>The slot a click would land the held item IN, drawn as a live target. -1 when
        /// there is none.</summary>
        private int dropTargetIndex = -1;

        private Canvas canvas;

        private void Start()
        {
            // Wired in the inspector on nothing, historically — the HUD prefab predates the field
            // and nobody noticed, because a null one only logs. Resolved from the hierarchy
            // instead: playerHUD is a child of the player object that owns it.
            if (player == null) player = GetComponentInParent<PlayerController>(true);

            if (player == null)
            {
                Debug.LogWarning($"{name}: InventoryUI found no PlayerController above it.", this);
                return;
            }

            playerInventory = player.PlayerInventory;
            if (playerInventory == null)
            {
                Debug.LogWarning($"{name}: PlayerController has no IPlayerInventory available.", this);
                return;
            }

            // A slot's click and hover come through the EventSystem, and the world scenes are not
            // all guaranteed to carry one — persistentScene does, the test scenes vary. Same guard
            // MinigameConfigUI and DevInventoryUI use, and a no-op when a scene already has one.
            UIBuilder.EnsureEventSystem();

            canvas = GetComponentInParent<Canvas>();
            if (canvas != null) canvas = canvas.rootCanvas;

            // Only this machine's own bar answers the pack's questions. Every player object in a
            // session carries a HUD, and a replica's is deactivated rather than absent — so the
            // ownership test is the guard that stops somebody else's four slots becoming the
            // drop targets on this screen if one is ever left switched on.
            if (local == null || Network.Owns(player)) local = this;

            playerInventory.OnSlotSelected += OnSlotSelected;
            playerInventory.OnSlotChanged += OnPlayerInventoryChanged;

            InitializeUI();

            // The selection is already set by the time this runs on a client that streamed in —
            // PlayerInventoryNetwork adopts the current state in OnNetworkSpawn, long before any
            // HUD Start — so reading it once here is what stops the bar showing nothing selected
            // until the player next presses a key.
            selectedIndex = playerInventory.SelectedSlotIndex;
            RefreshAll();
        }

        private void OnDestroy()
        {
            if (local == this) local = null;

            if (playerInventory != null)
            {
                playerInventory.OnSlotSelected -= OnSlotSelected;
                playerInventory.OnSlotChanged -= OnPlayerInventoryChanged;
            }
        }

        // ── Building the bar ─────────────────────────────────────────────────

        public void InitializeUI()
        {
            if (playerInventory == null) return;

            var grid = inventoryGrid as RectTransform;
            if (grid == null) grid = transform as RectTransform;
            if (grid == null) return;

            // Any slot authored INTO the prefab, from back when the bar instantiated Slot.prefab.
            // PlayerHUD already removes its copy, but InventoryUI.prefab still carries one, and a
            // leftover would be laid out as a fifth grey box that nothing ever refreshes.
            foreach (InventorySlotUI stale in grid.GetComponentsInChildren<InventorySlotUI>(true))
            {
                if (stale == null) continue;

                // Deactivated as well as destroyed: Destroy is deferred to the end of the frame,
                // and a layout group counts a doomed-but-active child for one full frame.
                stale.gameObject.SetActive(false);
                Destroy(stale.gameObject);
            }

            var layout = grid.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.spacing = HotbarStyle.SlotSpacing;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
                layout.childControlWidth = false;
                layout.childControlHeight = false;
            }

            int inventorySize = playerInventory.GetInventorySize();
            slotUIs = new InventorySlotUI[inventorySize];

            for (int i = 0; i < inventorySize; i++) slotUIs[i] = InventorySlotUI.Build(grid, i, this);

            RefreshAll();
        }

        // ── Inventory events ─────────────────────────────────────────────────

        private void OnSlotSelected(InventorySlot slot)
        {
            selectedIndex = slot == null ? -1 : slot.Index;
            RefreshAll();
        }

        private void OnPlayerInventoryChanged(int index, InventorySlot slot) => RefreshAll();

        public void RefreshAll()
        {
            if (slotUIs == null || playerInventory == null) return;

            for (int i = 0; i < slotUIs.Length; i++)
            {
                if (slotUIs[i] == null) continue;

                slotUIs[i].Refresh(playerInventory.GetSlot(i),
                                   i == selectedIndex,
                                   i == hoveredIndex,
                                   i == dropTargetIndex,
                                   i == heldOriginIndex);
            }
        }

        public void OnSlotHovered(int index)
        {
            if (hoveredIndex == index) return;

            hoveredIndex = index;
            RefreshAll();
        }

        public void OnSlotUnhovered(int index)
        {
            if (hoveredIndex != index) return;

            hoveredIndex = -1;
            RefreshAll();
        }

        // ── The bridge: what the pack asks this side ─────────────────────────

        /// <summary>
        /// Which hotbar slot the cursor is over, or -1.
        ///
        /// <para>
        /// A rectangle test against each slot rather than an EventSystem raycast, because the
        /// question is asked every frame from OUTSIDE the EventSystem: the pack's hand polls it to
        /// decide which slot to light up and — the load-bearing one — whether the click under the
        /// cursor belongs to the bar rather than to the mat behind it.
        /// </para>
        /// </summary>
        public static int SlotIndexUnder(Vector2 screenPosition)
        {
            if (local == null || local.slotUIs == null) return -1;
            if (!local.isActiveAndEnabled || !local.gameObject.activeInHierarchy) return -1;

            Camera cam = local.EventCamera();

            for (int i = 0; i < local.slotUIs.Length; i++)
            {
                InventorySlotUI slot = local.slotUIs[i];
                if (slot == null || slot.Rect == null) continue;

                if (RectTransformUtility.RectangleContainsScreenPoint(slot.Rect, screenPosition, cam))
                    return i;
            }

            return -1;
        }

        /// <summary>Lights one slot as the place a click would put the held item.  -1 clears it.</summary>
        public static void SetDropTarget(int index)
        {
            if (local == null || local.dropTargetIndex == index) return;

            local.dropTargetIndex = index;
            local.RefreshAll();
        }

        /// <summary>One slot refuses: the item has no room on the pack. Visual, never text.</summary>
        public static void ShakeSlot(int index)
        {
            if (local == null) return;

            // The same guard SlotIndexUnder carries: an inactive bar's InventorySlotUIs are
            // inactive too, and StartCoroutine on a disabled GameObject logs and does nothing —
            // which for this refusal means the player gets no feedback at all, silently.
            if (!local.isActiveAndEnabled || !local.gameObject.activeInHierarchy) return;

            local.ShakeLocal(index);
        }

        /// <summary>The instance half of a shake, shared by the static entry above and
        /// <see cref="ClickSlot"/> — which cannot safely call the static one, since <c>local</c>
        /// is not guaranteed to be <c>this</c> for every hotbar in a session.</summary>
        private void ShakeLocal(int index)
        {
            if (slotUIs == null) return;
            if (index < 0 || index >= slotUIs.Length) return;
            if (slotUIs[index] != null) slotUIs[index].Shake();
        }

        /// <summary>
        /// Takes back everything the pack asked the bar to draw.
        ///
        /// <para>
        /// Static and idempotent because the HUD outlives the focus session that drives it. Focus
        /// mode ends on any movement key, with something in hand included, and a bar left showing
        /// a reserved slot would never recover on its own.
        /// </para>
        /// </summary>
        public static void ClearPackFeedback()
        {
            if (local == null) return;

            if (local.heldOriginIndex < 0 && local.dropTargetIndex < 0) return;

            local.heldOriginIndex = -1;
            local.dropTargetIndex = -1;
            local.RefreshAll();
        }

        /// <summary>The slot whose item is in the player's hand. Its tile reads as empty, but as
        /// an empty tile that is spoken for. -1 clears it.</summary>
        public static void SetHeldOrigin(int index)
        {
            if (local == null || local.heldOriginIndex == index) return;

            local.heldOriginIndex = index;
            local.RefreshAll();
        }

        // ── The bridge: what a slot asks the pack ───────────────────────────

        /// <summary>
        /// A hotbar slot was clicked while the pack is open. The pack's hand resolves it: an empty
        /// hand lifts the slot's item, a full one puts what it is holding into the slot.
        ///
        /// <para>
        /// Outside focus mode there is no hand and this does nothing, which is correct — the cursor
        /// is locked out there and the bar is not clickable at all.
        /// </para>
        /// </summary>
        public void ClickSlot(int index)
        {
            PackHandController hand = PackHandController.Active;
            if (hand == null) return;

            if (hand.IsCarrying)
            {
                hand.PutIntoSlot(index);
                return;
            }

            if (playerInventory == null) return;

            InventorySlot slot = playerInventory.GetSlot(index);
            InventoryItem item = slot != null && !slot.IsEmpty ? slot.Item : null;
            if (item == null) return;

            // The hand declines when the same asset is already on the mat, where it could never be
            // put down. TryLiftFromSlot marks the slot reserved itself on success — see
            // SetHeldOrigin — so there is nothing to do on the way out.
            if (!hand.TryLiftFromSlot(index, item)) ShakeLocal(index);
        }

        /// <summary>
        /// The camera the canvas's screen points are measured against, which for a screen-space
        /// overlay canvas is null — passing one there gives an off-by-a-viewport answer.
        /// </summary>
        private Camera EventCamera() =>
            canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
    }
}
