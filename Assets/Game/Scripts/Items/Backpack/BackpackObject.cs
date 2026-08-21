using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// The pack itself: a world object that owns its contents.
    ///
    /// The contents live here rather than on the player because the pack outlives being carried —
    /// set it down and walk away and it keeps its gear, which is the whole point of the deploy.
    /// The same component therefore serves a worn pack, a pack lying where its owner left it, and
    /// (later) a pack found in a ruin, with no special cases.
    /// </summary>
    public class BackpackObject : MonoBehaviour, IInteractable
    {
        [Header("Rig")]
        [Tooltip("Every part that moves when the pack opens, in any order. The expedition pack " +
                 "wires PIVOT_Lid and PIVOT_Panel here; the older clamshell wired PIVOT_Door_L and " +
                 "PIVOT_Door_R. Leaving this empty is legal — a pack with no moving parts still " +
                 "shows and gives up its contents.")]
        [SerializeField] private BackpackHinge[] hinges = new BackpackHinge[0];

        [Tooltip("Exterior hang-off points, in index order. Their contents show whether the pack is " +
                 "worn or not, so a loaded pack visibly carries gear on the astronaut's back.")]
        [SerializeField] private Transform[] strapSockets = new Transform[BackpackContainer.StrapSlots];

        [Tooltip("Interior stow anchors, in index order. SOCK_Int_0 must be element 0 — a shuffled " +
                 "array silently scrambles which anchor an item appears on. Three across on each " +
                 "of four STATIC shelves, bottom tier first; they must never be children of a " +
                 "hinge, or their gear swings out into mid-air on the end of a panel.")]
        [SerializeField] private Transform[] pocketSockets = new Transform[BackpackContainer.MainSlots];

        [Header("Opening")]
        [SerializeField, Min(0.01f)] private float openSeconds = 0.5f;

        [Header("Item display")]
        [Tooltip("Metres the longest axis of a pocketed item is scaled to. Sized against the " +
                 "shelf pitch in the model — raise it past that and stowed gear clips the shelf above.")]
        [SerializeField] private float pocketFitSize = 0.3f;
        [Tooltip("Metres the longest axis of a strapped item is scaled to. Bedrolls read larger than pocket junk.")]
        [SerializeField] private float strapFitSize = 0.44f;

        [Header("Starting contents")]
        [SerializeField] private List<InventoryItem> startingStrapItems = new();
        [SerializeField] private List<InventoryItem> startingMainItems = new();

        /// <summary>
        /// The pack's contents. Built on first use rather than in Awake, because a pack's storage
        /// has to exist whenever someone asks for it — and Awake is not one of those moments you
        /// can count on. The editor never runs it on a component added to an object outside play
        /// mode, so an EditMode test, an inspector tool, or any code touching a pack before the
        /// first frame would otherwise be handed a null.
        /// </summary>
        public BackpackContainer Container => container ??= new BackpackContainer();

        public bool IsOpen { get; private set; }
        public bool IsWorn { get; private set; } = true;

        private BackpackContainer container;

        private BackpackController owner;
        private Coroutine doorRoutine;
        private Collider bodyCollider;

        // The hinges' authored rest orientations, captured once. The FBX does NOT hand empties back
        // at identity — PIVOT_Clamshell arrived at euler (270.02, 0, 0) — so the open angle has to
        // be applied RELATIVE to these. Treating it as absolute reorients the whole part, which on
        // the previous clamshell buried a door 0.4 m under the ground.
        private Quaternion[] closedRotations;

        // One live display object per occupied slot. Kept per compartment so a strap refresh never
        // walks the pocket array looking for something that was never built.
        private GameObject[] strapVisuals;
        private GameObject[] pocketVisuals;

        private void Awake()
        {
            bodyCollider = GetComponent<Collider>();

            strapVisuals = new GameObject[BackpackContainer.StrapSlots];
            pocketVisuals = new GameObject[BackpackContainer.MainSlots];

            foreach (InventoryItem item in startingStrapItems)
                Container.TryAdd(BackpackCompartment.Strap, item, out _);

            foreach (InventoryItem item in startingMainItems)
                Container.TryAdd(BackpackCompartment.Main, item, out _);

            Container.OnSlotChanged += HandleSlotChanged;

            CaptureClosedRotations();

            RefreshCompartment(BackpackCompartment.Strap);
        }

        private void OnDestroy()
        {
            // The field, not the property: a pack destroyed before anything ever asked for its
            // contents should not build a container on its way out.
            if (container != null)
                container.OnSlotChanged -= HandleSlotChanged;
        }

        /// <summary>Who is carrying this. Null once it is dropped for good.</summary>
        public void Bind(BackpackController controller) => owner = controller;

        /// <summary>
        /// The player this pack belongs to, and the channel every request about it travels on.
        ///
        /// Public because the pack has no NetworkObject of its own: a slot view that wants to ask
        /// the server for something has to ask through the wearer, who has both a channel and a
        /// relay. See the networking note in <see cref="BackpackController"/>.
        /// </summary>
        public BackpackController Owner => owner;

        /// <summary>
        /// Worn packs cannot be opened or interacted with — you cannot reach your own back. This is
        /// state, not just a visual: it is what stops the crosshair offering an interaction on the
        /// pack the player is wearing.
        /// </summary>
        public void SetWorn(bool worn)
        {
            IsWorn = worn;

            // Off while worn. Colliders under the same Rigidbody are one compound collider, so a
            // worn pack would otherwise bolt a 0.35 x 0.53 m box onto the player's own capsule and
            // wedge them in every doorway they fit through before.
            if (bodyCollider != null) bodyCollider.enabled = !worn;
        }

        public void SetOpen(bool open)
        {
            if (IsOpen == open) return;
            IsOpen = open;

            // Interior contents are built on open and torn down the moment closing starts. Every
            // interior anchor is a child of a door pivot, so leaving them up through the close would
            // sweep the whole stow load round through the frame in full view.
            if (open) RefreshCompartment(BackpackCompartment.Main);
            else ClearVisuals(pocketVisuals);

            if (doorRoutine != null) StopCoroutine(doorRoutine);
            doorRoutine = StartCoroutine(SwingDoors(open));
        }

        private void CaptureClosedRotations()
        {
            closedRotations = new Quaternion[hinges != null ? hinges.Length : 0];

            for (int i = 0; i < closedRotations.Length; i++)
                closedRotations[i] = hinges[i].pivot != null
                    ? hinges[i].pivot.localRotation
                    : Quaternion.identity;
        }

        /// <summary>
        /// Every hinge together, each about its own authored axis by its own signed angle. One
        /// loop rather than a hardcoded pair, so a lid that tips back and a panel that folds down
        /// are the same code as two doors that mirror each other.
        /// </summary>
        private IEnumerator SwingDoors(bool open)
        {
            int count = hinges != null ? hinges.Length : 0;

            // The pose the parts are in RIGHT NOW, not the closed pose: a press that interrupts a
            // half-finished swing has to continue from where the parts actually are, or the whole
            // rig snaps back to closed for one frame before setting off again.
            var from = new Quaternion[count];
            var to = new Quaternion[count];

            for (int i = 0; i < count; i++)
            {
                Transform pivot = hinges[i].pivot;
                from[i] = pivot != null ? pivot.localRotation : Quaternion.identity;
                to[i] = open ? closedRotations[i] * hinges[i].OpenOffset() : closedRotations[i];
            }

            for (float elapsed = 0f; elapsed < openSeconds; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / openSeconds);
                float eased = t * t * (3f - 2f * t);

                for (int i = 0; i < count; i++)
                    if (hinges[i].pivot != null)
                        hinges[i].pivot.localRotation = Quaternion.Slerp(from[i], to[i], eased);

                yield return null;
            }

            for (int i = 0; i < count; i++)
                if (hinges[i].pivot != null) hinges[i].pivot.localRotation = to[i];

            doorRoutine = null;
        }

        /// <summary>
        /// Ask for the item in one slot. <b>The taker's own machine must go through here, not
        /// through <see cref="TryTakeToHotbar"/>.</b>
        ///
        /// <para>
        /// Two players can be looking into the same open pack, so which of them gets the last thing
        /// in it is the server's to decide — the same rule that puts a trade on the trader's
        /// channel rather than the buyer's. Routed through the wearer, who owns the channel this
        /// pack has to borrow.
        /// </para>
        /// </summary>
        public void RequestTake(BackpackCompartment compartment, int index, Interactor interactor)
        {
            if (interactor == null) return;

            if (owner != null)
            {
                owner.RequestTake(compartment, index, interactor);
                return;
            }

            // A pack nobody owns has no channel to ask on, so it falls back to doing the transfer
            // here — single-player-style, which is the same degradation every unrelayed message in
            // this project takes. Unreachable today: every pack is bound to a wearer in
            // BackpackController.Awake and destroyed with them.
            IPlayerInventory hotbar = interactor.GetComponentInParent<IPlayerInventory>();
            if (hotbar != null) TryTakeToHotbar(compartment, index, hotbar);
        }

        /// <summary>
        /// Move one item from the pack into the given hotbar. <b>Server side only</b> — callers
        /// want <see cref="RequestTake"/>.
        ///
        /// <para>
        /// Both halves of this transfer replicate themselves, and neither of them from here. The
        /// hotbar is <see cref="PlayerInventoryNetwork"/>'s, which is server-authoritative and
        /// pushes every slot change out through its own NetworkList. The pack's half is
        /// <see cref="BackpackNetwork"/>'s, which is watching this container for exactly this.
        /// Doing anything else on the taker's machine as well would double the transfer up.
        /// </para>
        /// <para>
        /// A full hotbar is not a refusal — it is a SWAP: the pack item goes into the player's
        /// selected hotbar slot and whatever was in that slot takes its place in the pocket the
        /// player is aiming at. Refusing instead is what made a full hotbar feel like a broken
        /// interaction, because the only way out was to drop something on the ground first.
        /// </para>
        /// </summary>
        public bool TryTakeToHotbar(BackpackCompartment compartment, int index, IPlayerInventory hotbar)
        {
            if (hotbar == null) return false;

            InventorySlot slot = Container.GetSlot(compartment, index);
            if (slot == null || slot.IsEmpty) return false;

            // Tested BEFORE the item leaves the pack. TakeOut-then-add-back would work, but a failed
            // add in between would have already fired a change and destroyed the display object.
            if (hotbar.TryAddItem(slot.Item))
            {
                Container.TakeOut(compartment, index);
                return true;
            }

            return TrySwapWithHotbar(compartment, index, hotbar, slot.Item);
        }

        /// <summary>
        /// The full-hotbar path. Only ever called once TryAddItem has already refused, which is the
        /// proof that every hotbar slot is occupied — and that is what makes the middle of this
        /// safe: clearing one slot leaves EXACTLY one empty, so the following TryAddItem can only
        /// land in the slot just cleared. No IPlayerInventory member has to be added for it, which
        /// matters because PlayerInventoryNetwork, PickupableItem, ShipInteraction and
        /// RepairWorkstation all sit on that interface.
        /// </summary>
        private bool TrySwapWithHotbar(BackpackCompartment compartment, int index,
                                       IPlayerInventory hotbar, InventoryItem packItem)
        {
            // Nothing selected still has to do something. The player is aiming at an item and
            // pressing interact; "nothing happened" is the exact failure this method exists to
            // remove, so the swap falls back to the first slot rather than refusing.
            int target = hotbar.SelectedSlotIndex;
            if (target < 0 || target >= hotbar.GetInventorySize()) target = 0;

            InventorySlot heldSlot = hotbar.GetSlot(target);
            InventoryItem held = heldSlot != null && !heldSlot.IsEmpty ? heldSlot.Item : null;

            // Unreachable on a genuinely full hotbar, but a rogue IPlayerInventory that refuses adds
            // for its own reasons would otherwise have its item destroyed by the TakeOut below.
            if (held == null) return false;

            // Clears the slot. It does NOT spawn a world pickup — that is PlayerInventory.DropItem,
            // a different method — so nothing can leak onto the ground mid-swap.
            if (!hotbar.TryRemoveItem(target)) return false;

            if (!hotbar.TryAddItem(packItem))
            {
                // Cannot happen once a slot has been cleared, but leaving the hotbar a slot short
                // would be a silent item loss, so put the held item back and abandon the swap.
                hotbar.TryAddItem(held);
                if (hotbar.SelectedSlotIndex != target) hotbar.SelectSlot(target);
                return false;
            }

            Container.TakeOut(compartment, index);
            Container.PlaceAt(compartment, index, held);

            // Required, not cosmetic. PlayerInventory.TryRemoveItem nulls SelectedSlotIndex when it
            // removes the selected slot, so without this the player finishes the swap holding
            // nothing while the item they just took sits unselected in their hand slot.
            //
            // Guarded on the selection having actually moved, because the networked hotbar does NOT
            // clear it — and PlayerInventoryNetwork.SelectSlot is a TOGGLE, so re-selecting a slot
            // that is already selected deselects it. Unguarded, every swap left the player holding
            // nothing on exactly the implementation that ships.
            if (hotbar.SelectedSlotIndex != target) hotbar.SelectSlot(target);

            return true;
        }

        // ------------------------------------------------------------------ display

        private void HandleSlotChanged(BackpackCompartment compartment, int index, InventorySlot slot)
        {
            RefreshSocket(compartment, index);
        }

        private void RefreshCompartment(BackpackCompartment compartment)
        {
            int count = Container.SlotCount(compartment);
            for (int i = 0; i < count; i++) RefreshSocket(compartment, i);
        }

        private void RefreshSocket(BackpackCompartment compartment, int index)
        {
            bool isStrap = compartment == BackpackCompartment.Strap;
            GameObject[] visuals = isStrap ? strapVisuals : pocketVisuals;
            Transform[] sockets = isStrap ? strapSockets : pocketSockets;

            if (visuals == null || index < 0 || index >= visuals.Length) return;

            if (visuals[index] != null)
            {
                Destroy(visuals[index]);
                visuals[index] = null;
            }

            // Pocket contents exist only while the pack is open. Strap contents are always shown.
            if (!isStrap && !IsOpen) return;

            InventorySlot slot = Container.GetSlot(compartment, index);
            if (slot == null || slot.IsEmpty) return;

            if (index >= sockets.Length || sockets[index] == null)
            {
                Debug.LogWarning($"BackpackObject: no socket wired for {compartment} slot {index}.", this);
                return;
            }

            // Exterior gear lies flat under the cargo net; interior gear stands on its shelf.
            GameObject visual = BackpackItemVisual.Build(
                slot.Item.itemPrefab, sockets[index],
                isStrap ? strapFitSize : pocketFitSize,
                isStrap ? BackpackSeat.LieFlat : BackpackSeat.StandOn);

            if (visual == null) return;

            // The view goes on the same GameObject as the collider BackpackItemVisual just added, so
            // Interactor's GetComponent finds it before it ever walks up to the pack.
            visual.AddComponent<BackpackSlotView>().Bind(this, compartment, index);
            visuals[index] = visual;
        }

        private void ClearVisuals(GameObject[] visuals)
        {
            if (visuals == null) return;

            for (int i = 0; i < visuals.Length; i++)
            {
                if (visuals[i] == null) continue;
                Destroy(visuals[i]);
                visuals[i] = null;
            }
        }

        // ------------------------------------------------------------------ IInteractable

        public bool CanInteract()
        {
            if (IsWorn) return false;                            // cannot reach your own back
            if (owner == null) return true;                      // an orphaned pack can still be opened
            return owner.CurrentState == BackpackController.State.Open;
        }

        public void Interact(Interactor interactor)
        {
            if (!CanInteract()) return;

            // Aiming at the pack body: closed on the ground means open it, open means take it back.
            // Aiming at an ITEM never reaches here — BackpackSlotView sits on the item's own collider
            // and Interactor resolves the nearest hit first.
            //
            // Reshoulder only ASKS: where the pack is, is shared state, so the server decides and
            // tells everyone — including whoever pressed. The lid on an owner-less pack is the one
            // case with nobody to ask, and it stays local. Anybody may shut somebody else's pack,
            // deliberately: it is how you hand it back to them.
            if (!IsOpen) SetOpen(true);
            else if (owner != null) owner.Reshoulder();
        }
    }
}
