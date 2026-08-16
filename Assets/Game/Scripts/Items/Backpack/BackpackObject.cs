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
        [Tooltip("PIVOT_Door_L — the left rear vertical hinge on the back-supported frame. " +
                 "Interior anchors 0-4 are its children, so its gear swings out with it.")]
        [SerializeField] private Transform doorPivotLeft;

        [Tooltip("PIVOT_Door_R — the right rear vertical hinge. Interior anchors 5-9 are its children.")]
        [SerializeField] private Transform doorPivotRight;

        [Tooltip("Exterior hang-off points, in index order. Their contents show whether the pack is " +
                 "worn or not, so a loaded pack visibly carries gear on the astronaut's back.")]
        [SerializeField] private Transform[] strapSockets = new Transform[BackpackContainer.StrapSlots];

        [Tooltip("Interior stow anchors, in index order. SOCK_Int_0 must be element 0 — a shuffled " +
                 "array silently scrambles which anchor an item appears on. 0-4 are on the left door, " +
                 "5-9 on the right.")]
        [SerializeField] private Transform[] pocketSockets = new Transform[BackpackContainer.MainSlots];

        [Header("Opening")]
        [Tooltip("Degrees each door swings outward. The doors hinge on the REAR edges, so they have to " +
                 "come past 90 before their inner faces turn toward the player.")]
        [SerializeField] private float openAngle = 135f;
        [SerializeField, Min(0.01f)] private float openSeconds = 0.5f;

        [Header("Item display")]
        [Tooltip("Metres the longest axis of a pocketed item is scaled to.")]
        [SerializeField] private float pocketFitSize = 0.13f;
        [Tooltip("Metres the longest axis of a strapped item is scaled to. Bedrolls read larger than pocket junk.")]
        [SerializeField] private float strapFitSize = 0.22f;

        [Header("Starting contents")]
        [SerializeField] private List<InventoryItem> startingStrapItems = new();
        [SerializeField] private List<InventoryItem> startingMainItems = new();

        public BackpackContainer Container { get; private set; }
        public bool IsOpen { get; private set; }
        public bool IsWorn { get; private set; } = true;

        private BackpackController owner;
        private Coroutine doorRoutine;
        private Collider bodyCollider;

        // The hinges' authored rest orientations. The FBX does NOT hand these back at identity, so
        // the open angle has to be applied RELATIVE to them. Treating it as absolute reorients the
        // whole door — which, on the previous clamshell, buried it 0.4 m under the ground.
        private Quaternion closedRotationLeft = Quaternion.identity;
        private Quaternion closedRotationRight = Quaternion.identity;

        // One live display object per occupied slot. Kept per compartment so a strap refresh never
        // walks the pocket array looking for something that was never built.
        private GameObject[] strapVisuals;
        private GameObject[] pocketVisuals;

        private void Awake()
        {
            bodyCollider = GetComponent<Collider>();
            Container = new BackpackContainer();

            strapVisuals = new GameObject[BackpackContainer.StrapSlots];
            pocketVisuals = new GameObject[BackpackContainer.MainSlots];

            foreach (InventoryItem item in startingStrapItems)
                Container.TryAdd(BackpackCompartment.Strap, item, out _);

            foreach (InventoryItem item in startingMainItems)
                Container.TryAdd(BackpackCompartment.Main, item, out _);

            Container.OnSlotChanged += HandleSlotChanged;

            if (doorPivotLeft != null) closedRotationLeft = doorPivotLeft.localRotation;
            if (doorPivotRight != null) closedRotationRight = doorPivotRight.localRotation;

            RefreshCompartment(BackpackCompartment.Strap);
        }

        private void OnDestroy()
        {
            if (Container != null)
                Container.OnSlotChanged -= HandleSlotChanged;
        }

        /// <summary>Who is carrying this. Null once it is dropped for good.</summary>
        public void Bind(BackpackController controller) => owner = controller;

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
            doorRoutine = StartCoroutine(SwingDoors(open ? openAngle : 0f));
        }

        /// <summary>
        /// Both doors together, mirrored: the left swings -Z and the right +Z, so they open away
        /// from the centre seam instead of one chasing the other round.
        /// </summary>
        private IEnumerator SwingDoors(float targetAngle)
        {
            Quaternion fromL = doorPivotLeft != null ? doorPivotLeft.localRotation : Quaternion.identity;
            Quaternion fromR = doorPivotRight != null ? doorPivotRight.localRotation : Quaternion.identity;

            Quaternion toL = closedRotationLeft * Quaternion.Euler(0f, 0f, targetAngle);
            Quaternion toR = closedRotationRight * Quaternion.Euler(0f, 0f, -targetAngle);

            for (float elapsed = 0f; elapsed < openSeconds; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / openSeconds);
                float eased = t * t * (3f - 2f * t);
                if (doorPivotLeft != null) doorPivotLeft.localRotation = Quaternion.Slerp(fromL, toL, eased);
                if (doorPivotRight != null) doorPivotRight.localRotation = Quaternion.Slerp(fromR, toR, eased);
                yield return null;
            }

            if (doorPivotLeft != null) doorPivotLeft.localRotation = toL;
            if (doorPivotRight != null) doorPivotRight.localRotation = toR;
            doorRoutine = null;
        }

        /// <summary>
        /// Move one item from the pack into the given hotbar. Returns false and changes nothing if
        /// the hotbar is full — the item stays visibly in its pocket rather than vanishing.
        /// </summary>
        public bool TryTakeToHotbar(BackpackCompartment compartment, int index, IPlayerInventory hotbar)
        {
            if (hotbar == null) return false;

            InventorySlot slot = Container.GetSlot(compartment, index);
            if (slot == null || slot.IsEmpty) return false;

            // Tested BEFORE the item leaves the pack. TakeOut-then-add-back would work, but a failed
            // add in between would have already fired a change and destroyed the display object.
            if (hotbar.TryAddItem(slot.Item) == false) return false;

            Container.TakeOut(compartment, index);
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
            if (!IsOpen) SetOpen(true);
            else if (owner != null) owner.Reshoulder();
        }
    }
}
