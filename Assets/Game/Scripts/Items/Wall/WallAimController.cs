using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Items
{
    /// <summary>
    /// What the crosshair does when it is on an inventory wall.
    ///
    /// <para>
    /// <b>There is no mode.</b> No camera is spawned, no cursor is unlocked, nothing pauses and no
    /// menu scope is entered — the player walks up, looks at the wall, and the readout appears
    /// under their crosshair. That is the whole difference from the backpack, which takes the
    /// screen with <c>PackFocusSession</c> because a rig lying on the sand cannot be aimed at from
    /// standing height. A wall is at eye level, so the eye is enough.
    /// </para>
    /// <para>
    /// What it draws is the backpack's own readout, from the same two classes: the free/taken
    /// lattice and the green-or-red ghost cells (<see cref="PackGridVisual"/>), a ghost copy of the
    /// held item seated on the wall, and the hover rim on a placed one
    /// (<see cref="PackHandVisuals"/>). Only the aim and the input are new.
    /// </para>
    /// <para>
    /// Purely local and owner-only. Everything that changes the wall goes out as a request and
    /// nothing happens on this machine — see <see cref="WallInventory.RequestStow"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallAimController : MonoBehaviour
    {
        /// <summary>
        /// The controller whose wall currently owns the Use button, if any. At most one, on one
        /// machine.
        ///
        /// <para>
        /// Public because the Use button is shared: it fires the item in the player's hand AND,
        /// while this readout is up, acts on the wall instead. Those cannot both happen, so
        /// <c>EquipmentController.OnUse</c> asks this before firing — the same shape as
        /// <c>PackFocusSession.Active</c>, which exists so one screen has one owner.
        /// </para>
        /// <para>
        /// <b>Set for BOTH of the wall's verbs, not just the placing one.</b> It used to be set
        /// only while a placement ghost was up, which left the take verb sharing the button with
        /// the hand: a press that lifted gear off the wall also fired whatever the player was
        /// holding. An item lying on an inventory surface is stowed, not equipped — while the
        /// crosshair is on one, the press belongs to the wall and to nothing else. See
        /// <see cref="ClaimUse"/>.
        /// </para>
        /// <para>
        /// Claimed on the frame the readout appears rather than on the press, so the readout and
        /// the button can never disagree about which verb is live, and released the moment the
        /// crosshair leaves a wall this controller would act on.
        /// </para>
        /// </summary>
        public static WallAimController Aiming { get; private set; }

        [Tooltip("How far the player can reach into a wall. Zero — the default — takes the " +
                 "Interactor's own cast distance, so the preview and the E key always agree " +
                 "about what is in range. Set it only to make the wall shorter-ranged than " +
                 "ordinary interaction, never longer.")]
        [SerializeField, Min(0f)] private float reach;

        /// <summary>
        /// Sized for the worst case the look ray plausibly crosses inside a ship — the wall, the
        /// gear on it, a bulkhead and a fitting or two. Overflow only costs the furthest hits,
        /// which are behind whatever the player is looking at anyway.
        /// </summary>
        private readonly RaycastHit[] hits = new RaycastHit[12];

        private PlayerInputManager input;
        private PlayerController player;
        private Interactor interactor;
        private IPlayerInventory hotbar;

        private PackGridVisual cells;
        private PackHandVisuals visuals;

        /// <summary>The wall under the crosshair this frame, and where on it. Null when there is none.</summary>
        private WallInventory wall;
        private PackSurface surface;
        private Vector2 uv;

        /// <summary>
        /// The turn the player has dialled in, in degrees, kept across walls and across frames.
        ///
        /// Not reset when the crosshair leaves a wall: a player who turned a crate on its side,
        /// looked away to check something and looked back has not changed their mind about the
        /// crate, and re-straightening it would be the wall overruling them.
        /// </summary>
        private float yaw;

        /// <summary>Would a stow at the current aim land? Drives the cell colour and the E key.</summary>
        private bool placementLegal;

        /// <summary>The placed item under the crosshair, if the aim is on one rather than on canvas.</summary>
        private GameObject hovered;
        private PackPlacement hoveredPlacement;
        private bool hasHovered;

        /// <summary>The asset the ghost copy was built from, so it is rebuilt only when it changes.</summary>
        private InventoryItem proxyItem;

        /// <summary>
        /// The asset the ghost was last ATTEMPTED for, which is not the same thing.
        ///
        /// A copy that fails to build leaves <see cref="proxyItem"/> null, and a retry keyed on
        /// that alone is an Instantiate and a Destroy every frame for as long as the player keeps
        /// looking at the wall. Keyed on the attempt instead, a failure is tried once.
        /// </summary>
        private InventoryItem proxyTried;

        /// <summary>Whether the wheel is currently ours. Tracked so the hand-back happens once.</summary>
        private bool wheelTaken;

        private void Awake()
        {
            input = GetComponent<PlayerInputManager>();
            player = GetComponent<PlayerController>();

            // In children: on this project's player the Interactor lives on the camera rig, which
            // is where the look ray has to come from anyway, and the hotbar can sit on a child too.
            interactor = GetComponentInChildren<Interactor>(true);
            hotbar = GetComponentInChildren<IPlayerInventory>(true);

            cells = new PackGridVisual();
            visuals = new PackHandVisuals();
        }

        private void OnEnable()
        {
            if (input == null) return;

            input.OnInteractPressed += OnPressed;
            input.OnUsePressed += OnPressed;
            input.OnPackYawScrolled += OnYawScrolled;
        }

        private void OnDisable()
        {
            if (input != null)
            {
                input.OnInteractPressed -= OnPressed;
                input.OnUsePressed -= OnPressed;
                input.OnPackYawScrolled -= OnYawScrolled;
            }

            // Unconditionally, and before anything else: this runs on death, on mounting and on
            // teardown, and a wheel left switched to yaw would leave the player unable to change
            // hotbar slots with nothing on screen to explain why.
            ClearAim();
        }

        private void OnDestroy()
        {
            cells?.Dispose();
            visuals?.Dispose();
        }

        private void Update()
        {
            // A replica of somebody else's body must never draw a readout on this screen. Their
            // input component is disabled so this should be unreachable, and the cost of being
            // wrong is four players' previews at once.
            if (!Network.Owns(this) || (player != null && player.IsDead))
            {
                ClearAim();
                return;
            }

            // Input down means the body is not the player's to aim with right now — mounting
            // disables this component, and MountModule turns the camera over to the vehicle. It
            // also matters for the wheel: PlayerInputManager.OnEnable re-enables the whole Hotbar
            // map, so a wheel handed to us before a mount would be handed back to the hotbar
            // behind our back and then fire both handlers on every notch.
            if (input == null || !input.isActiveAndEnabled)
            {
                ClearAim();
                return;
            }

            // A pack has the screen, or a menu does. Its cursor and this crosshair would otherwise
            // both be drawing ghost cells, on two different containers, off one wheel.
            if (PackFocusSession.Active != null || GameplayMenuScope.IsActive)
            {
                ClearAim();
                return;
            }

            if (!Resolve())
            {
                ClearAim();
                return;
            }

            InventoryItem held = HeldItem();

            // Decided in ONE place, before either readout draws, because the two readouts used to
            // answer it differently and that was the bug: the placing one claimed the button and
            // the taking one handed it back, so lifting gear off a wall also fired whatever was in
            // the player's hand.
            if (WallOwnsUse(held != null, hasHovered)) ClaimUse();
            else ReleaseUse();

            if (held != null) ShowPlacement(held);
            else ShowTake();
        }

        /// <summary>
        /// Whether a Use press belongs to the wall rather than to the item in the hand, given that
        /// the crosshair is already on a wall.
        ///
        /// <para>
        /// Both of the wall's verbs count. With something in hand the press is a stow — including
        /// onto cells that are red, where the press is a refusal the wall owns and answers, not a
        /// fall-through to the trigger. With an empty hand it is a take, but only when there is
        /// actually gear under the crosshair; a press on bare canvas does nothing here and the
        /// button stays the hand's. <b>An item lying on an inventory surface is stowed, not
        /// equipped</b> — it is a stripped display copy with no <c>UsableItem</c> on it at all —
        /// so a click on one is a click on furniture and must never reach a trigger.
        /// </para>
        /// <para>
        /// Static, public and argument-only so the rule can be read and tested without a player, a
        /// wall or a physics scene — the EditMode tests compile into a different assembly, so
        /// <c>internal</c> would hide it from the only thing that checks it.
        /// <c>EquipmentController.OnUse</c> reads the result through <see cref="Aiming"/>.
        /// </para>
        /// </summary>
        public static bool WallOwnsUse(bool holdingSomething, bool overPlacedItem) =>
            holdingSomething || overPlacedItem;

        // ── Where the crosshair is ───────────────────────────────────────────

        /// <summary>
        /// Find the wall under the crosshair, and the point on it. False leaves nothing resolved.
        ///
        /// <para>
        /// A real raycast against real colliders, and not the plane intersection on its own, for
        /// one reason: <b>occlusion</b>. A face's plane runs on past its own rectangle and through
        /// everything in front of it, so a plane-only pick would let a player stow gear onto a wall
        /// through the bulkhead they are standing behind. The ray decides WHICH wall and whether
        /// anything is in the way; the plane then decides where on it, which is what lets the
        /// placement rectangle be an empty rather than a mesh, exactly as the rig's faces are.
        /// </para>
        /// <para>
        /// The item pick comes first, on the pack-item layer. A placed item stands proud of the
        /// face, and a player pointing at a canister means the canister, not the canvas behind it.
        /// </para>
        /// </summary>
        private bool Resolve()
        {
            wall = null;
            surface = null;
            hovered = null;
            hasHovered = false;

            if (interactor == null || !interactor.isActiveAndEnabled) return false;

            Ray ray = interactor.LookRay;
            if (ray.direction.sqrMagnitude < 1e-6f) return false;

            float range = reach > 0f ? reach : interactor.CastDistance;

            if (PackPointer.TryHitItem(ray, range, out GameObject visual,
                                       out PackSurface itemSurface, out Vector2 itemUv))
            {
                WallInventory owner = WallOf(itemSurface);

                if (owner != null)
                {
                    wall = owner;
                    surface = itemSurface;
                    uv = itemUv;
                    hovered = visual;
                    hasHovered = wall.TryFindAt(surface.Id, uv, out hoveredPlacement);
                    return true;
                }

                // Somebody else's gear on the pack-item layer — the rig deployed in front of the
                // wall, or a long item on the player's OWN worn pack, which rides the spine and is
                // documented to swing into this very camera. Falling through rather than giving up
                // is the whole point: TryHitItem is an unoccluded cast over one layer, so a hit on
                // it says nothing about whether the wall is under the crosshair. Returning false
                // here dropped the readout AND handed the Use button back to the weapon, so the
                // press the player meant for the wall fired the item in their hand instead.
            }

            int count = Physics.RaycastNonAlloc(ray, hits, range, ~LayerMask.GetMask("Player"),
                                                QueryTriggerInteraction.Ignore);
            if (count <= 0) return false;

            // Nearest first, and only the nearest: something between the player and the wall is a
            // wall they cannot reach. RaycastNonAlloc promises no order, so the minimum is taken
            // rather than hits[0].
            int nearest = -1;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == null) continue;

                // The player's own capsule is on the Default layer, not "Player", and the camera
                // sits just inside the top of a 3 m capsule — lean and the eye pokes out through
                // its own collider. Interactor documents the same trap.
                if (hits[i].collider.transform.IsChildOf(transform.root)) continue;

                if (nearest < 0 || hits[i].distance < hits[nearest].distance) nearest = i;
            }

            if (nearest < 0) return false;

            WallInventory candidate = hits[nearest].collider.GetComponentInParent<WallInventory>();
            if (candidate == null || !candidate.isActiveAndEnabled) return false;

            if (!PackPointer.TryHitSurface(ray, range, candidate.Surfaces,
                                           out PackSurface hit, out Vector2 hitUv)) return false;

            wall = candidate;
            surface = hit;
            uv = hitUv;
            return true;
        }

        private static WallInventory WallOf(PackSurface s) =>
            s != null ? s.GetComponentInParent<WallInventory>() : null;

        // ── The two readouts ─────────────────────────────────────────────────

        /// <summary>
        /// Something in hand: show where it would land, in green or red, and let the wheel turn it.
        ///
        /// <para>
        /// The ghost is drawn where the player is POINTING, snapped to the cell grid and nowhere
        /// else. It is not moved to the nearest legal spot: this readout is information about the
        /// player's aim rather than a correction of it, so red cells mean "not here", and the
        /// answers — aim elsewhere, or turn the item — are both one gesture away.
        /// </para>
        /// </summary>
        private void ShowPlacement(InventoryItem held)
        {
            TakeWheel();

            PackShape shape = wall.ShapeFor(held);

            // Asked of the item, not decided here: whether a thing may turn at all is a property of
            // its authored row, and a yaw this class invented would be straightened by the server.
            float turn = PackShapes.SnapYaw(held, wall.Shapes, yaw);

            uv = PackLayout.Snap(surface.Id, surface.Size, shape, uv, turn);

            // The wall's own answer, not a re-derivation of it: this is the same call the server
            // will make, so a green cell is a promise the server keeps unless somebody else fills
            // the space first. `Holds` is the one extra question — the layout is keyed by item id,
            // so an asset already on this wall can never be placed a second time, and offering it
            // green would be a press that silently does nothing.
            placementLegal =
                !wall.Holds(held.ID)
                && wall.Reaches(surface.Id)
                && surface.AcceptsItem(held)
                && wall.Layout.CanPlace(surface.Id, surface.Size, shape, uv, turn, null);

            if (proxyTried != held)
            {
                proxyTried = held;
                proxyItem = visuals.BeginCarry(held.itemPrefab, surface, uv, turn) ? held : null;
            }

            if (proxyItem != null) visuals.MoveCarry(held.itemPrefab, surface, uv, turn);

            visuals.SetHovered(null);
            visuals.SetCarryDenied(!placementLegal);

            cells.ShowLattice(surface, wall.Layout, ignoreItemId: null);

            // Through PackOverhang like every other consumer of an oriented shape, so the preview
            // and the placement cannot disagree about which cells are involved. The wall is strict
            // on both axes, so today this is the identity — but a face that stopped being strict
            // and a preview that had not heard about it is exactly the bug this indirection exists
            // to prevent.
            PackShape oriented = PackOverhang.Clamp(
                surface.Id, surface.Size, shape.Rotated(PackGrid.QuarterTurns(turn)));

            cells.Show(surface, PackGrid.BlockOrigin(surface.Size, uv, oriented.Size),
                       oriented, placementLegal);
        }

        /// <summary>
        /// Empty hand: rim whatever is under the crosshair, so E has something visible to act on.
        ///
        /// <para>
        /// No lattice and no ghost cells here. With nothing to place there is no shape to draw, and
        /// a grid over an idle wall is decoration the player cannot act on.
        /// </para>
        /// <para>
        /// The Use button is not touched here: <see cref="Update"/> has already settled who owns
        /// the press, for both verbs at once — see <see cref="WallOwnsUse"/>.
        /// </para>
        /// </summary>
        private void ShowTake()
        {
            GiveWheelBack();
            EndPreview();

            placementLegal = false;

            cells.Hide();
            visuals.SetHovered(hovered);
        }

        /// <summary>
        /// Everything off, and the wheel handed back. Idempotent — <see cref="Update"/> reaches it
        /// on every frame the player is looking elsewhere.
        /// </summary>
        private void ClearAim()
        {
            GiveWheelBack();
            EndPreview();
            ReleaseUse();

            wall = null;
            surface = null;
            hovered = null;
            hasHovered = false;
            placementLegal = false;

            cells?.Hide();
            visuals?.SetHovered(null);
        }

        /// <summary>Take the Use button for the wall. Idempotent.</summary>
        private void ClaimUse() => Aiming = this;

        /// <summary>Hand the Use button back to the item. Idempotent, and never takes it from
        /// somebody else's controller.</summary>
        private void ReleaseUse()
        {
            if (Aiming == this) Aiming = null;
        }

        private void EndPreview()
        {
            if (proxyItem == null && proxyTried == null) return;

            proxyItem = null;
            proxyTried = null;
            visuals.EndCarry();
        }

        // ── Input ────────────────────────────────────────────────────────────

        /// <summary>
        /// Put the held item on the wall, or take the one under the crosshair off it.
        ///
        /// <para>
        /// Bound to BOTH buttons, and that is deliberate rather than lazy. Use is the one the hand
        /// already means — you are looking at a rack holding a crate, and clicking puts the crate
        /// on the rack — and it is the button the backpack's placement uses too. Interact is bound
        /// as well because it is unambiguous: it is what a player presses at everything else in the
        /// world, it is the only one that works with an empty hand, and it never has to be taken
        /// off anything to work.
        /// </para>
        ///
        /// <para>
        /// Nothing happens locally on either branch. Two players can be standing at one wall, so
        /// which of them gets the cell — or the last item in it — is the server's to decide, and an
        /// optimistic transfer here would hand it to both and then take it back from one.
        /// </para>
        /// <para>
        /// A press on red is a miss, not a refusal that needs explaining: the cells have been red
        /// under the crosshair the whole time the player was aiming. It flashes the held copy and
        /// sends nothing, rather than turning the item the way the backpack's click does — the
        /// wheel is already the turn here, and a key that sometimes places and sometimes rotates is
        /// two verbs on one button.
        /// </para>
        /// </summary>
        private void OnPressed()
        {
            if (wall == null || surface == null || interactor == null) return;

            InventoryItem held = HeldItem();

            if (held != null)
            {
                if (!placementLegal)
                {
                    visuals.SetCarryDenied(true);
                    return;
                }

                wall.RequestStow(hotbar.SelectedSlotIndex, surface.Id, uv, yaw, interactor);
                return;
            }

            if (!hasHovered) return;

            // The ANCHOR uv, not the point the crosshair happens to be on. A shaped item's stored
            // centre is not guaranteed to be a cell it actually fills, and the server identifies
            // the item by the cell named here.
            wall.RequestTake(surface.Id, wall.AnchorUv(hoveredPlacement), interactor);
        }

        /// <summary>A wheel notch is a quarter turn, while the wheel is ours — see
        /// <see cref="TakeWheel"/> for when that is.</summary>
        private void OnYawScrolled(int notches) => yaw = PackGrid.SnapYaw(yaw + notches * 90f);

        /// <summary>
        /// Point the wheel at the item's turn instead of at the hotbar, and only while there is
        /// something in hand and a wall under the crosshair — which is exactly when a turn is a
        /// thing the player can see happening.
        /// </summary>
        private void TakeWheel()
        {
            if (wheelTaken || input == null) return;

            wheelTaken = true;
            input.SetWheelTurnsItems(true);
        }

        private void GiveWheelBack()
        {
            if (!wheelTaken || input == null) return;

            wheelTaken = false;
            input.SetWheelTurnsItems(false);
        }

        /// <summary>
        /// What the player is holding — the selected hotbar slot's item, which is the item in their
        /// hand and the one thing a wall can be given.
        /// </summary>
        private InventoryItem HeldItem()
        {
            InventoryItem item = hotbar?.GetSelectedItem();

            // A prefab-less item cannot be drawn as a ghost or built as a display copy, so it would
            // go onto the wall and then be invisible on it. Treated as an empty hand instead, which
            // at least leaves the take verb working.
            return item != null && item.itemPrefab != null ? item : null;
        }
    }
}
