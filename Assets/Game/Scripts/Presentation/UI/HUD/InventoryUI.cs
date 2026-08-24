using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The hotbar: four slots across the bottom of the screen, and the bridge that lets items be
    /// dragged between them and an open backpack.
    ///
    /// <para>
    /// <b>The bar draws itself.</b> <c>Slot.prefab</c> is no longer instantiated — see
    /// <see cref="InventorySlotUI.Build"/> for why — so the only things this needs from the prefab
    /// are the rect it lives in and the layout group on it.
    /// </para>
    /// <para>
    /// <b>The drag bridge.</b> Two gestures cross this boundary in opposite directions and both are
    /// resolved by <c>PackDragController</c>, not here: a hotbar slot dragged onto the pack, and a
    /// pack item dragged onto a hotbar slot. This side owns three things and no more — which slot
    /// the cursor is over, what the bar looks like while a drag is in flight, and the icon
    /// following the cursor. Every decision about whether a drop is legal, and every request that
    /// goes to the server, is the pack's.
    /// </para>
    /// <para>
    /// The static members exist because the pack has no way to reach this instance: the hotbar is a
    /// prefab under the player, and the drag controller is added at runtime to a camera that did
    /// not exist a frame earlier. There is one local player and therefore one of these.
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

        /// <summary>The slot a drag came OUT of, drawn empty-but-reserved. -1 when nothing is in hand.</summary>
        private int dragOriginIndex = -1;

        /// <summary>The slot a drag would land IN, drawn as a live target. -1 when there is none.</summary>
        private int dropTargetIndex = -1;

        private Canvas canvas;
        private RectTransform ghost;
        private Image ghostIcon;

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

            // Dragging is EventSystem work, and the world scenes are not all guaranteed to carry
            // one — persistentScene does, the test scenes vary. Same guard MinigameConfigUI and
            // DevInventoryUI use, and it is a no-op when a scene already has one.
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
                                   i == dragOriginIndex);
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
        /// A rectangle test against each slot rather than an EventSystem raycast, and the reason
        /// is the direction that matters most: a drag that began on the PACK is not a UI drag at
        /// all — the EventSystem never saw it start, so it will never deliver an <c>OnDrop</c> —
        /// and the release still has to know whether it landed on the bar. One test that answers
        /// for both directions is one answer to keep straight.
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

        /// <summary>Lights one slot as the place a drag would land.  -1 clears it.</summary>
        public static void SetDropTarget(int index)
        {
            if (local == null || local.dropTargetIndex == index) return;

            local.dropTargetIndex = index;
            local.RefreshAll();
        }

        /// <summary>
        /// Takes back everything a drag asked the bar to draw.
        ///
        /// <para>
        /// Static and idempotent because the HUD outlives the focus session that drives it. Focus
        /// mode ends on any movement key, mid-drag included, and a bar left showing a reserved
        /// slot and a floating icon would never recover on its own.
        /// </para>
        /// </summary>
        public static void ClearDragFeedback()
        {
            if (local == null) return;

            local.HideGhost();

            if (local.dragOriginIndex < 0 && local.dropTargetIndex < 0) return;

            local.dragOriginIndex = -1;
            local.dropTargetIndex = -1;
            local.RefreshAll();
        }

        // ── The bridge: what a slot asks the pack ───────────────────────────

        /// <summary>
        /// A slot has been picked up. Answers false when there is nothing to carry or nowhere to
        /// carry it to, and the slot then declines the gesture outright.
        /// </summary>
        public bool BeginSlotDrag(int index, Vector2 screenPosition)
        {
            if (playerInventory == null) return false;

            InventorySlot slot = playerInventory.GetSlot(index);
            InventoryItem item = slot != null && !slot.IsEmpty ? slot.Item : null;
            if (item == null) return false;

            // The pack is the only place a hotbar item can be dragged TO. Outside focus mode there
            // is no drag, which is also why the cursor is locked out there.
            PackDragController pack = PackDragController.Active;
            if (pack == null || !pack.BeginHotbarDrag(index, item)) return false;

            dragOriginIndex = index;
            dropTargetIndex = -1;

            ShowGhost(item, screenPosition);
            RefreshAll();

            return true;
        }

        public void DragSlot(Vector2 screenPosition) => MoveGhost(screenPosition);

        /// <summary>
        /// The slot has been let go. The pack resolves it — over one of its faces this is a stow
        /// request, anywhere else it is a cancel — and this side only stops drawing.
        /// </summary>
        public void EndSlotDrag(Vector2 screenPosition)
        {
            PackDragController pack = PackDragController.Active;
            if (pack != null) pack.EndHotbarDrag();

            ClearDragFeedback();
        }

        // ── The icon under the cursor ────────────────────────────────────────

        /// <summary>
        /// The dragged item's icon, following the pointer.
        ///
        /// <para>
        /// Deliberately not the slot itself moving. The slot stays in the row showing that its
        /// place is being kept — see <c>isReserved</c> on <see cref="InventorySlotUI.Refresh"/> —
        /// and what travels is the thing being carried.
        /// </para>
        /// <para>
        /// Parented to the root canvas rather than to the bar, so it is not clipped by the row and
        /// not laid out by the group, and put last so it draws over the rest of the HUD.
        /// </para>
        /// </summary>
        private void ShowGhost(InventoryItem item, Vector2 screenPosition)
        {
            if (canvas == null || item == null || item.icon == null) return;

            if (ghost == null) BuildGhost();

            ghostIcon.sprite = item.icon;

            ghost.gameObject.SetActive(true);
            ghost.SetAsLastSibling();

            MoveGhost(screenPosition);
        }

        private void BuildGhost()
        {
            var go = new GameObject("HotbarDragGhost", typeof(RectTransform));
            go.layer = canvas.gameObject.layer;

            ghost = (RectTransform)go.transform;
            ghost.SetParent(canvas.transform, false);
            ghost.anchorMin = new Vector2(0.5f, 0.5f);
            ghost.anchorMax = new Vector2(0.5f, 0.5f);
            ghost.pivot = new Vector2(0.5f, 0.5f);
            // The same size the icon has in its slot, so picking an item up does not shrink it.
            ghost.sizeDelta = new Vector2(HotbarStyle.SlotWidth - HotbarStyle.IconInset * 2f,
                                          HotbarStyle.SlotHeight - HotbarStyle.IconInset * 2f);

            var halo = new GameObject("Halo", typeof(RectTransform));
            halo.layer = go.layer;

            var haloRect = (RectTransform)halo.transform;
            haloRect.SetParent(ghost, false);
            haloRect.anchorMin = Vector2.zero;
            haloRect.anchorMax = Vector2.one;
            haloRect.offsetMin = new Vector2(-16f, -16f);
            haloRect.offsetMax = new Vector2(16f, 16f);

            // The icon has to stay legible over sand, over the pack's canvas and over the sky.
            var glow = halo.AddComponent<Image>();
            glow.sprite = UITheme.GlowSprite;
            glow.color = new Color(0f, 0f, 0f, 0.38f);
            glow.raycastTarget = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.layer = go.layer;

            var iconRect = (RectTransform)iconGo.transform;
            iconRect.SetParent(ghost, false);
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;

            ghostIcon = iconGo.AddComponent<Image>();
            ghostIcon.preserveAspect = true;
            ghostIcon.color = new Color(1f, 1f, 1f, 0.92f);

            // Never a target. A ghost that swallowed the pointer would break the very drag it is
            // drawing, and would sit between the cursor and every slot it passes over.
            ghostIcon.raycastTarget = false;
        }

        private void MoveGhost(Vector2 screenPosition)
        {
            if (ghost == null || !ghost.gameObject.activeSelf || canvas == null) return;

            var canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenPosition, EventCamera(), out Vector2 point))
            {
                ghost.localPosition = point;
            }
        }

        private void HideGhost()
        {
            if (ghost != null) ghost.gameObject.SetActive(false);
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
