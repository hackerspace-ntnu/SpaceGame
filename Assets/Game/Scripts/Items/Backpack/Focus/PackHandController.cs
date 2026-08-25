using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

// UnityEngine.InputSystem is imported above for Mouse, and it has a PlayerInputManager of its own
// for local multiplayer join handling. This project has never used that one; the alias is what
// keeps the bare name meaning ours.
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Items
{
    /// <summary>
    /// The hands of focus mode: hover, pick up, turn, put down.
    ///
    /// <para>
    /// Lives on the focus camera's own GameObject and dies with it, so there is no path where an
    /// item is left in a hand belonging to a view that has gone.
    /// </para>
    /// <para>
    /// <b>One button, one verb, two states.</b> The hand is either empty or holding one item, and
    /// every action in focus mode is a left click resolved against which of the two it is. Nothing
    /// is ever held down: no press-and-hold, no threshold, no gesture that can be started and then
    /// left half-finished. The one exception is the leaf, which is a continuous pull on the pack's
    /// own board rather than anything to do with an item.
    /// </para>
    /// <para>
    /// <b>Nothing here is optimistic.</b> Putting an item down sends a request and the display does
    /// not move; what moves it is the layout change the server publishes back. Two players can be
    /// in one pack, and an item that appeared in your hand and then vanished again is worse than a
    /// round trip you can see coming. Lifting, by contrast, is purely local — nothing is sent and
    /// nothing has changed, which is why letting go of the hand needs no undo at all.
    /// </para>
    /// <para>
    /// Hit-testing is in <see cref="PackPointer"/> and everything drawn is in
    /// <see cref="PackHandVisuals"/>; what is left here is the state machine and the requests.
    /// </para>
    /// <para>
    /// <b>The hotbar is the fifth surface.</b> An item lifted out of a slot and one lifted off the
    /// mat run through the same hand and the same frame of hit-testing — see <c>HandSource</c>.
    /// The alternative was a second placement system living in the HUD with its own idea of what a
    /// legal spot is, which is two things to keep in agreement about a question with one answer.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PackHandController : MonoBehaviour
    {
        /// <summary>
        /// The hand of the session on screen, or null when there is no focus session.
        ///
        /// The HUD needs a way to reach this and has no reference to it: the hotbar is a prefab
        /// under the player, and the hand is a component added at runtime to a camera that
        /// did not exist a frame ago. There is at most one focus session on one machine — see
        /// <see cref="PackFocusSession.Active"/> — so there is at most one of these.
        /// </summary>
        public static PackHandController Active { get; private set; }

        /// <summary>
        /// Degrees of yaw per wheel notch.
        ///
        /// <para>
        /// A quarter turn, because the grid has four orientations and no others. It used to be 24
        /// degrees — fifteen notches to a circle, which made sense when a placement could sit at
        /// any angle. Keeping a fine notch under a grid would mean four notches out of every
        /// fifteen did something and eleven did nothing at all.
        /// </para>
        /// </summary>
        private const float YawPerNotch = 90f;

        /// <summary>Seconds a refusal stays red — see <see cref="ClearDeniedFlash"/>.</summary>
        private const float DeniedFlashSeconds = 0.3f;

        /// <summary>
        /// Metres along the cursor ray the carried copy sits when the ray misses the pack's
        /// horizontal plane entirely — cursor on the sky, or a ray running parallel to the
        /// ground. Roughly the rig's own distance from the focus camera, so the copy neither
        /// balloons in the player's face nor shrinks toward the horizon when it leaves the faces.
        /// </summary>
        private const float FreeCarryRayMetres = 2f;

        private float deniedUntil;

        private PackFocusCamera focusCamera;
        private BackpackController controller;
        private Interactor interactor;
        private PlayerInputManager input;

        private PackHandVisuals visuals;

        /// <summary>
        /// The cells the held item would occupy, one square each, plus the lattice of the whole
        /// hovered face underneath it. They carry the verdict: green where the placement is legal
        /// and red where it is not. Nothing corrects the player's aim, so those cells are the only
        /// thing telling them whether the click they are about to make will land.
        /// </summary>
        private PackGridVisual cellGrid;

        /// <summary>
        /// The layout the lattice is subscribed to, cached at <see cref="Attach"/> rather than
        /// re-read from <c>controller.Pack.Layout</c> at unsubscribe time. A plain C# reference,
        /// not a <c>UnityEngine.Object</c>, so it carries none of Unity's fake-null semantics —
        /// <see cref="OnDestroy"/> can always find it and unhook, even torn down in whatever order
        /// a scene unload happens to destroy this component and the pack in.
        /// </summary>
        private PackLayout subscribedLayout;

        // ── The hand ─────────────────────────────────────────────────────────
        //
        // Two states and nothing else: empty, or holding one item. Every verb in focus mode is a
        // left click resolved against which of the two it is, which is why there is no gesture
        // state here at all — no button-down origin, no drag threshold, no source enum for "which
        // machine started this". A lift is local and costs nothing; only putting it down is a
        // request.

        private bool carrying;
        private InventoryItem heldItem;

        /// <summary>Where the held item came from, and therefore what putting it down means.</summary>
        private enum HandSource
        {
            /// <summary>Lifted off the mat. Putting it down is a move; putting it in a slot is a take.</summary>
            Pack,

            /// <summary>Lifted out of a hotbar slot. Putting it down is a stow.</summary>
            Hotbar,
        }

        private HandSource heldFrom;

        /// <summary>The display copy the held item was lifted off, left rim-ghosted where it was.</summary>
        private GameObject originVisual;

        private PackSurfaceId originSurface;

        /// <summary>
        /// A cell the held item really filled, as opposed to its placement uv, which is where its
        /// block is centred. This is what names it to the server — see
        /// <see cref="PackLayout.TryAnchorUv"/> for why the two had to come apart.
        /// </summary>
        private Vector2 originGrab;

        /// <summary>The hotbar slot a <see cref="HandSource.Hotbar"/> item came out of.</summary>
        private int originSlot = -1;

        /// <summary>
        /// The turn the held item is being shown at. Written by the wheel and by any click that
        /// does not place — red cells and off the faces alike — and read by the preview, the
        /// cells and the request, so there is no second "the yaw it will actually use" any more:
        /// nothing turns the item on the player's behalf, and the item lands wherever its shown
        /// turn fits.
        /// </summary>
        private float yaw;

        /// <summary>
        /// Has the carried copy been built yet? Built on the same frame as ANY lift — off the mat
        /// and out of a hotbar slot alike — because the copy is the single readout of what is in
        /// the hand, everywhere on screen: no icon stands in for it over the bar any more, so a
        /// carry without a copy would be an invisible carry. A hotbar lift has no face under the
        /// cursor, so its copy is built against any wired face for scale and moved onto the
        /// cursor ray in the same call — see <see cref="TryLiftFromSlot"/>.
        ///
        /// <para>
        /// Written from what <see cref="PackHandVisuals.BeginCarry"/> ANSWERS, never from the fact
        /// that it was called: a prefab it cannot build leaves no copy, and a flag that recorded the
        /// attempt would leave the carry running invisibly while the cells went on promising a
        /// landing.
        /// </para>
        /// </summary>
        private bool proxyBuilt;

        /// <summary>The hotbar slot under the cursor this frame, or -1.</summary>
        private int hoveredSlot = -1;

        private PackSurface targetSurface;
        private Vector2 targetUv;

        /// <summary>Is the cursor on one of the rig's faces at all this frame?</summary>
        private bool overSurface;

        /// <summary>
        /// Would a click put the held item down where it is being shown?
        ///
        /// <para>
        /// Asked of <see cref="PackLayout.CanPlace"/> about the exact cells being drawn, at the
        /// exact turn being drawn — not about a nearby spot the item could be moved to. That is
        /// the whole difference from the magnet this replaced: the answer describes the player's
        /// aim rather than correcting it, so a red readout is information about where they are
        /// pointing instead of a spot that quietly moved out from under them.
        /// </para>
        /// </summary>
        private bool placementLegal;

        public bool IsCarrying => carrying;

        // ── Cached labels ────────────────────────────────────────────────────
        //
        // Both hints below are read on EVERY frame the cursor rests where they apply, and resting
        // is the steady state rather than the transient: a player reads the name of the thing under
        // the cursor by leaving the cursor on it. Building the string there means one garbage
        // string per frame for as long as they look, so each is built once and kept until the thing
        // it names changes.

        private InventoryItem hintItem;
        private string hintText;

        private int slotHintIndex = -1;
        private string slotHintText;

        // ── Flipping the leaf ────────────────────────────────────────────────
        //
        // The one thing in focus mode that is held down rather than clicked, and it carries no
        // item: the player grabs the front leaf's free edge and pulls it through its arc into the
        // rack, or pulls it back down. R still does it as a toggle — this is a gesture ADDED beside
        // the key, not a replacement for it.
        //
        // It cannot collide with picking gear up, because the item hit is resolved first and this
        // is only ever reached on the branch where there was nothing under the cursor. Grabbing the
        // board is what is left when you grab something that is not on the board.
        //
        // CONTINUOUS rather than a flick, and that is the point of the whole thing: the leaf tracks
        // the cursor through the arc and settles when released past halfway, so the player can see
        // exactly where the commit is and change their mind on the way. A flick would have been a
        // button with extra steps, and there is already a button.

        private bool draggingLeaf;

        /// <summary>Where the cursor was when the leaf was grabbed, and how far up the leaf was then.</summary>
        private Vector2 leafGrabCursor;
        private float leafStartProgress;

        /// <summary>Where the grabbed point on the board sits with the leaf all the way down, and up.</summary>
        private Vector3 leafFlatPoint;
        private Vector3 leafRackedPoint;

        /// <summary>Where the drag has pulled it to, 0 flat and 1 racked.</summary>
        private float leafProgress;

        public static PackHandController Attach(PackFocusCamera focusCamera, BackpackController controller,
                                                Interactor interactor, PlayerInputManager input)
        {
            if (focusCamera == null || controller == null) return null;

            var hand = focusCamera.gameObject.AddComponent<PackHandController>();
            hand.focusCamera = focusCamera;
            hand.controller = controller;
            hand.interactor = interactor;
            hand.input = input;

            // Subscribed here and not only in OnEnable, because AddComponent has already run both
            // Awake and OnEnable by the time these fields are set — OnEnable saw a null input and
            // hooked nothing, which is a scroll wheel that silently does not rotate anything.
            hand.Subscribe();

            // The lattice shows other items' cells, so any change to the layout — including one
            // published from another player's move — stales it. Not guarded on controller.Pack:
            // PackFocusSession.Enter already refused to call here at all with no Pack, so it is
            // guaranteed by the caller rather than checked again.
            hand.subscribedLayout = controller.Pack.Layout;
            hand.subscribedLayout.OnChanged += hand.OnLayoutChanged;

            Active = hand;
            return hand;
        }

        private void Awake()
        {
            visuals = new PackHandVisuals();
            cellGrid = new PackGridVisual();
        }

        private void OnEnable() => Subscribe();

        /// <summary>Idempotent, so the two callers above cannot double-hook the wheel.</summary>
        private void Subscribe()
        {
            if (input == null) return;

            input.OnPackYawScrolled -= OnYawScrolled;
            input.OnPackYawScrolled += OnYawScrolled;

            input.OnPackStowPressed -= OnHotbarKey;
            input.OnPackStowPressed += OnHotbarKey;
        }

        private void OnDisable()
        {
            if (input == null) return;

            input.OnPackYawScrolled -= OnYawScrolled;
            input.OnPackStowPressed -= OnHotbarKey;
        }

        /// <summary>
        /// The lattice shows other items' cells, so any layout change stales it — and a layout
        /// change is also the only thing that can invalidate what is in the player's hand.
        /// </summary>
        private void OnLayoutChanged()
        {
            if (cellGrid != null) cellGrid.MarkLatticeDirty();

            if (carrying && heldFrom == HandSource.Pack && !OriginStillThere()) ReturnToOrigin();
        }

        /// <summary>
        /// Is the placement an item was lifted OFF still sitting where it was left?
        ///
        /// <para>
        /// A lift off the mat is local: the item never moved, and the hand holds a copy of
        /// something still addressed by the cell it came from. Two players can be in one pack, so
        /// the other one can move or take that item while it is in this player's hand — and then
        /// both ways of putting it down name a placement that no longer exists. The server refuses
        /// those by publishing nothing, which is correct, but this side empties the hand either
        /// way, so the item appears to evaporate: the copy stops being drawn and nothing lands.
        /// </para>
        /// <para>
        /// The ID is checked as well as the cell, because a cell that has been vacated and refilled
        /// resolves perfectly well — to somebody else's item.
        /// </para>
        /// <para>
        /// One lookup per PUBLISHED change, not per frame. The subscription already exists for the
        /// lattice.
        /// </para>
        /// </summary>
        private bool OriginStillThere()
        {
            BackpackObject pack = controller != null ? controller.Pack : null;
            if (pack == null || heldItem == null) return false;

            return pack.TryFindAt(originSurface, originGrab, out PackPlacement placement)
                   && placement.ItemId == heldItem.ID;
        }

        private void OnDestroy()
        {
            // The leaf belongs to the pack, which outlives this. A controller torn down by a scene
            // load or a destroyed focus camera rather than through Cancel would otherwise leave the
            // board stranded part way up its arc, with nothing left holding it there.
            if (draggingLeaf) ReleaseLeaf(commit: false);

            if (subscribedLayout != null) subscribedLayout.OnChanged -= OnLayoutChanged;
            subscribedLayout = null;

            if (Active == this) Active = null;

            InventoryUI.ClearPackFeedback();

            visuals?.Dispose();
            cellGrid?.Dispose();
            cellGrid = null;
        }

        /// <summary>
        /// Puts whatever is in hand back where it came from, and lets go of the leaf. The
        /// component stays alive and usable.
        ///
        /// <para>
        /// There is nothing to undo. A lift is local — no request went out and no layout changed —
        /// so the item has been sitting where it always was for the whole time it looked like it
        /// was in the player's hand, and letting go is just no longer drawing the copy.
        /// </para>
        /// <para>
        /// The rack key needs this: the surface the ghost is tracking is about to swing through
        /// ninety degrees. Calling <see cref="Cancel"/> for that destroyed the controller for the
        /// rest of the session, so one press of R silently stopped the player picking anything up.
        /// </para>
        /// </summary>
        public void ReturnToOrigin()
        {
            if (draggingLeaf) ReleaseLeaf(commit: false);

            LetGo();
        }

        /// <summary>
        /// Stop drawing the hand and clear it. What letting go MEANS, whether or not a request is
        /// about to go out — the item has not moved either way, so this is the same teardown for a
        /// placement, a take and an abandon alike.
        ///
        /// <para>
        /// Every commit runs this BEFORE it sends, not after. On a host the request can come back
        /// through the layout's own OnChanged inside the same call, and a hand still holding
        /// something at that moment would see its origin gone and try to let go a second time.
        /// </para>
        /// </summary>
        private void LetGo()
        {
            if (!carrying) return;

            carrying = false;

            if (visuals != null) visuals.EndCarry();
            if (cellGrid != null) cellGrid.Hide();

            ClearHand();
        }

        /// <summary>
        /// Every field the carry wrote, put back the way an empty hand finds them.
        ///
        /// <para>
        /// All of them, including the several that both lift sites overwrite anyway. A reader
        /// auditing the exits should not have to work out which half of the state is stale and
        /// which is live, and the day a third way into the hand appears, the one field it forgets
        /// to initialise is the bug this makes impossible. <c>heldFrom</c> is the exception and has
        /// to be: it is an enum with no empty value, and it means nothing at all while
        /// <see cref="carrying"/> is false.
        /// </para>
        /// </summary>
        private void ClearHand()
        {
            heldItem = null;
            originVisual = null;
            originSurface = default;
            originGrab = Vector2.zero;
            originSlot = -1;

            yaw = 0f;

            proxyBuilt = false;
            hoveredSlot = -1;

            targetSurface = null;
            targetUv = Vector2.zero;
            overSurface = false;
            placementLegal = false;

            // The refusal flash belongs to the copy that has just stopped being drawn, so it ends
            // with the carry rather than outliving it on a material the next one will reuse.
            ClearDeniedFlash();

            InventoryUI.ClearPackFeedback();
        }

        /// <summary>
        /// Ends the refusal flash on the carried copy.
        ///
        /// <para>
        /// Called from the timer AND from every exit from the hand. The flash is an outline shell
        /// on the copy, so a copy destroyed mid-flash takes the shell with it — but the deadline
        /// would survive into the next carry with the timer that owned it gone, so every exit
        /// resets both halves of the state together.
        /// </para>
        /// </summary>
        private void ClearDeniedFlash()
        {
            deniedUntil = 0f;

            if (visuals != null) visuals.SetCarryDenied(false);
        }

        /// <summary>Lets go of whatever is in hand and tears the display down. The session's exit.</summary>
        public void Cancel()
        {
            // Before the fields are cleared: the leaf is the pack's own state, not this
            // component's, and leaving focus mid-flip must not leave it stranded halfway.
            if (draggingLeaf) ReleaseLeaf(commit: false);

            carrying = false;

            // The HUD is not ours and outlives the session, so anything we asked it to draw has to
            // be taken back explicitly — ClearHand does that. Exiting focus with something in hand
            // is a normal way out — any movement key does it — and a hotbar left showing a reserved
            // slot would never recover.
            ClearHand();

            if (visuals != null) visuals.Dispose();
            visuals = null;

            if (cellGrid != null) cellGrid.Dispose();
            cellGrid = null;

            if (Active == this) Active = null;

            Destroy(this);
        }

        private void Update()
        {
            if (visuals == null) return;

            // Unconditional, ahead of every other early return below: "the flash always ends" is
            // a promise about the WALL CLOCK, not about whichever state the rest of Update happens
            // to be in.
            if (deniedUntil > 0f && Time.unscaledTime >= deniedUntil) ClearDeniedFlash();

            Camera cam = focusCamera != null ? focusCamera.Camera : null;
            BackpackObject pack = controller != null ? controller.Pack : null;

            if (cam == null || pack == null) return;

            Mouse mouse = Mouse.current;

            if (draggingLeaf) UpdateLeafDrag(cam, pack, mouse);
            else if (carrying) UpdateCarry(cam, pack, mouse);
            else UpdateHover(cam, pack, mouse);
        }

        // ── Hovering ─────────────────────────────────────────────────────────

        private void UpdateHover(Camera cam, BackpackObject pack, Mouse mouse)
        {
            // Tracked with an empty hand as well as a full one, because the leaf grab is offered on
            // bare board — a question about the face under the cursor — and the bare-mat hint reads
            // it too. It is the same plane intersection UpdateCarry uses, arithmetic against each
            // face rather than a second raycast, so tracking it in both states costs nothing.
            overSurface = PackPointer.TryHitSurface(cam, pack.Surfaces,
                                                    out PackSurface hovered, out Vector2 hoveredUv);
            if (overSurface)
            {
                targetSurface = hovered;
                targetUv = hoveredUv;
            }

            bool over = PackPointer.TryHitItem(cam, out GameObject visual, out PackSurface surface, out Vector2 uv);

            // The bar is drawn over the same screen the rig is — that overlap is exactly why
            // InventoryUI.SlotIndexUnder exists. A click over a slot belongs to the HUD, which
            // resolves it through its own pointer handlers; it must never ALSO register here as a
            // click on the mat behind it, or one press over a slot would both lift that slot's item
            // and lift whatever pack item happens to be framed underneath.
            bool overBar = InventoryUI.SlotIndexUnder(PackPointer.CursorPosition) >= 0;

            if (!over || !pack.TryFindAt(surface.Id, uv, out PackPlacement placement))
            {
                visuals.SetHovered(null);

                // The leaf grab is offered only on the branch where nothing is under the cursor,
                // which is what keeps it from ever competing with picking gear up: a board with a
                // crate under the cursor is a crate, and the board is only grabbable where it is
                // bare.
                bool onBoard = overSurface && CanGrabLeaf(pack, targetSurface, targetUv);

                if (onBoard && !overBar && mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    BeginLeafDrag(pack, targetSurface, targetUv);
                    return;
                }

                // Clear mat under the cursor is the one place in focus mode where the label has
                // nothing to say, and it is exactly where the way IN wants to be named — a verb
                // with no on-screen affordance is one nobody will ever find.
                visuals.ShowName(onBoard ? LeafHint(pack.IsRacked)
                                         : overSurface ? "Click a hotbar item to take it in hand — or press 1-4"
                                         : null,
                                 PackPointer.CursorPosition);
                return;
            }

            InventoryItem item = pack.ItemFor(placement.ItemId);

            visuals.SetHovered(visual);

            visuals.ShowName(HoverHint(item), PackPointer.CursorPosition);

            if (mouse == null || overBar) return;

            if (mouse.leftButton.wasPressedThisFrame) Lift(pack, item, placement, visual);
        }

        // ── Flipping the leaf ────────────────────────────────────────────────

        // The board is bare, so the only verb it has is its own. Lifting gear out of the hotbar
        // is named on the bare-MAT hint instead, where there is no board to talk about.
        private static string LeafHint(bool racked) =>
            racked ? "Click to lay the board flat"
                   : "Click to stand the board up";

        /// <summary>
        /// Is the cursor on bare board, with the pack in a state where turning it means anything?
        ///
        /// <para>
        /// The whole face, not just a hem at the free edge. The grab is only ever offered where
        /// nothing is placed — an item under the cursor was resolved first — so widening it costs
        /// no gesture, and the old 0.16 m edge band was the single biggest reason the rack felt
        /// hard to reach: the obvious thing to click is the middle of the board.
        /// </para>
        /// </summary>
        private bool CanGrabLeaf(BackpackObject pack, PackSurface surface, Vector2 uv)
        {
            if (pack == null || surface == null) return false;

            // The same two conditions the R key is refused on. A pack mid-deploy has a leaf that
            // the unfold's beat sheet owns, and grabbing it would be two things turning one hinge.
            if (!pack.IsOpen || pack.IsWorn) return false;
            if (controller == null || controller.CurrentState != BackpackController.State.Open) return false;

            // The hinge is what BeginLeafDrag swings the grab point about; no hinge, no gesture.
            if (!pack.TryGetLeafHinge(out _, out _, out _)) return false;

            return PackLeafDrag.IsLeafFace(surface.Id);
        }

        /// <summary>
        /// Take hold of the board's edge.
        ///
        /// <para>
        /// The two ends of the arc are worked out once, here, rather than every frame: the grabbed
        /// point is swung back to where it would be with the leaf flat and forward to where it
        /// would be with the leaf up, and from then on the drag is a question about a fixed segment
        /// in the world. Recomputing them from the LIVE surface each frame would be a feedback loop
        /// — the surface moves because the drag moved it — and it would drift.
        /// </para>
        /// </summary>
        private void BeginLeafDrag(BackpackObject pack, PackSurface surface, Vector2 uv)
        {
            if (!pack.TryGetLeafHinge(out Vector3 origin, out Vector3 axis, out float rackDegrees)) return;

            Vector3 grab = surface.ToWorld(uv, 0f);
            float here = pack.RackProgress;

            leafFlatPoint = PackLeafDrag.Swing(grab, origin, axis, -here * rackDegrees);
            leafRackedPoint = PackLeafDrag.Swing(grab, origin, axis, (1f - here) * rackDegrees);

            leafStartProgress = here;
            leafProgress = here;
            leafGrabCursor = PackPointer.CursorPosition;
            draggingLeaf = true;

            visuals.SetHovered(null);
            visuals.ShowName(null, Vector2.zero);
        }

        private void UpdateLeafDrag(Camera cam, BackpackObject pack, Mouse mouse)
        {
            Vector2 flat = cam.WorldToScreenPoint(leafFlatPoint);
            Vector2 racked = cam.WorldToScreenPoint(leafRackedPoint);

            leafProgress = PackLeafDrag.Progress(flat, racked, leafGrabCursor,
                                                 PackPointer.CursorPosition, leafStartProgress);

            // Presentation only, and only on this screen. Nothing about the leaf is committed
            // anywhere until the button comes up — see ReleaseLeaf.
            pack.ScrubRack(leafProgress);

            visuals.ShowName(leafProgress >= PackLeafDrag.CommitAt
                                 ? "Release to stand the board up"
                                 : "Release to lay the board flat",
                             PackPointer.CursorPosition);

            // No mouse at all ends the drag rather than stranding the leaf under a cursor that
            // cannot let go of it.
            if (mouse == null || !mouse.leftButton.isPressed) ReleaseLeaf(commit: true);
        }

        /// <summary>
        /// Let go of the board.
        ///
        /// <para>
        /// The commit goes through <see cref="BackpackObject.RequestRack"/>, which is the same door
        /// the R key uses and therefore the same <c>NetworkVariable</c> on <c>BackpackNetwork</c>.
        /// Nothing in here writes <c>IsRacked</c>: the drag only ever asked, and what it asked for
        /// is a boolean, not the angle it happened to leave the leaf at.
        /// </para>
        /// <para>
        /// <see cref="BackpackObject.SettleRack"/> runs either way and runs LAST. Past the commit
        /// point it finishes the last few degrees of a flip that has already been agreed; short of
        /// it, or when the request was refused or never sent, it springs the leaf back to whatever
        /// the state still says. That is why the two calls are not an either/or.
        /// </para>
        /// </summary>
        /// <param name="commit">False for an abandon — leaving focus mode, the rack key arriving
        /// mid-drag — which puts the leaf back without asking for anything.</param>
        private void ReleaseLeaf(bool commit)
        {
            draggingLeaf = false;

            if (visuals != null) visuals.ShowName(null, Vector2.zero);

            BackpackObject pack = controller != null ? controller.Pack : null;
            if (pack == null) return;

            if (commit)
            {
                // A press that barely moved is a CLICK, and a click TOGGLES. Without this branch a
                // click was a zero-length drag: progress stayed where it started, the commit test
                // re-asked for the state the leaf was already in, and the board never moved.
                bool clicked = (PackPointer.CursorPosition - leafGrabCursor).sqrMagnitude
                               <= PackLeafDrag.ClickPixels * PackLeafDrag.ClickPixels;

                bool up = clicked ? !pack.IsRacked
                                  : leafProgress >= PackLeafDrag.CommitAt;

                if (up != pack.IsRacked) pack.RequestRack(up);
            }

            pack.SettleRack();
        }

        // ── Lifting out of the hotbar ────────────────────────────────────────

        /// <summary>
        /// Take a hotbar slot's item into the hand. Called by the HUD when a slot is clicked, and
        /// by the 1-4 keys, which are the same verb on a key.
        ///
        /// <para>
        /// Answers false when it will not take the item, so the slot can shake rather than start a
        /// carry that can only ever end in nothing happening.
        /// </para>
        /// </summary>
        public bool TryLiftFromSlot(int slotIndex, InventoryItem item)
        {
            if (carrying || draggingLeaf) return false;
            if (visuals == null || item == null || item.itemPrefab == null) return false;

            BackpackObject pack = controller != null ? controller.Pack : null;
            if (pack == null || interactor == null) return false;

            // The layout is keyed by id, so this asset already lying on the mat is going to be
            // refused wherever the player tries to put it down. Refusing the lift is honest about
            // that up front; the alternative is an item in hand with no legal cell anywhere.
            if (pack.Holds(item.ID)) return false;

            carrying = true;
            heldItem = item;
            heldFrom = HandSource.Hotbar;
            originSlot = slotIndex;

            originVisual = null;
            originSurface = default;
            originGrab = Vector2.zero;

            yaw = 0f;

            if (cellGrid != null) cellGrid.MarkLatticeDirty();

            // No target yet — the cursor is over the HUD, which is not a face, and TryHitSurface
            // is what will say otherwise on some later frame.
            targetSurface = null;
            overSurface = false;
            placementLegal = false;

            visuals.SetHovered(null);
            visuals.ShowName(null, Vector2.zero);

            // The copy is built NOW, not deferred until the cursor reaches the mat: it is the
            // only readout of what is in the hand, so it exists from the first frame of the
            // carry. There is no face under the cursor to seat it on, so any wired face serves as
            // the scale-and-orientation frame to build against, and the free move puts it under
            // the cursor in the same breath.
            PackSurface seat = FirstSurface(pack);

            proxyBuilt = seat != null &&
                         visuals.BeginCarry(item.itemPrefab, seat, seat.Size * 0.5f, yaw);

            if (proxyBuilt) visuals.MoveCarryFree(item.itemPrefab, FreeCarryPoint(pack), yaw);

            InventoryUI.SetHeldOrigin(slotIndex);

            return true;
        }

        /// <summary>
        /// A hotbar key while the pack is open, with an empty hand: that slot's item comes into it.
        ///
        /// The click verb on a key, and nothing more. It used to be an aimed stow that leaned on
        /// the magnet to find a spot, which is exactly the auto-placement this interaction removed.
        /// </summary>
        private void OnHotbarKey(int slotIndex)
        {
            if (carrying || draggingLeaf) return;

            IPlayerInventory hotbar = Hotbar();
            if (hotbar == null) return;
            if (slotIndex < 0 || slotIndex >= hotbar.GetInventorySize()) return;

            InventorySlot slot = hotbar.GetSlot(slotIndex);
            InventoryItem item = slot != null && !slot.IsEmpty ? slot.Item : null;
            if (item == null) return;

            if (!TryLiftFromSlot(slotIndex, item)) InventoryUI.ShakeSlot(slotIndex);
        }

        /// <summary>
        /// The stowing player's hotbar. Resolved from the Interactor upward, which is the lookup
        /// <see cref="BackpackObject.RequestTake"/> documents: on this project's player the
        /// Interactor lives on the camera rig and the inventory is on the body above it.
        /// </summary>
        private IPlayerInventory Hotbar() =>
            interactor != null ? interactor.GetComponentInParent<IPlayerInventory>() : null;

        // ── Lifting off the mat ──────────────────────────────────────────────

        /// <summary>Take a placed item off the mat and into the hand. Local only — nothing is
        /// sent, and the copy on the mat stays exactly where it is until a placement moves it.</summary>
        private void Lift(BackpackObject pack, InventoryItem item, PackPlacement placement,
                          GameObject visual)
        {
            if (item == null || item.itemPrefab == null) return;

            PackSurface surface = pack.SurfaceFor(placement.Surface);
            if (surface == null) return;

            carrying = true;
            heldItem = item;
            heldFrom = HandSource.Pack;
            originSlot = -1;

            originVisual = visual;
            originSurface = placement.Surface;

            // Kept apart from the placement's own uv on purpose. That is where the item VISUALLY
            // sits; this is a cell it actually fills, which is the only point the server can
            // resolve it from. For a rectangle they are the same; for a mask with a hole in the
            // middle of its block they are not.
            originGrab = pack.AnchorUv(placement);

            yaw = placement.Yaw;

            targetSurface = surface;
            targetUv = placement.Uv;

            // The lift happened ON a face, so that is the state until the first UpdateCarry says
            // otherwise — and the item is trivially legal where it already is.
            overSurface = true;
            placementLegal = true;

            if (cellGrid != null) cellGrid.MarkLatticeDirty();

            // The hover rim comes off before the ghost goes on: both are outline shells traced
            // over the same display copy, and the copy the player just lifted off is about to be
            // re-shelled as the origin ghost.
            visuals.SetHovered(null);
            visuals.ShowName(null, Vector2.zero);

            proxyBuilt = visuals.BeginCarry(item.itemPrefab, surface, placement.Uv, placement.Yaw);

            // AFTER the copy, never before: BeginCarry ends whatever carry was running, and ending
            // one clears the origin outline. Set first, the outline was wiped in the same breath by
            // the call meant to put the item in the player's hand — so the space they were about to
            // free was never actually marked.
            visuals.SetGhost(originVisual);
        }

        // ── Carrying ─────────────────────────────────────────────────────────

        private void UpdateCarry(Camera cam, BackpackObject pack, Mouse mouse)
        {
            overSurface = PackPointer.TryHitSurface(cam, pack.Surfaces,
                                                    out PackSurface surface, out Vector2 uv);

            PackShape shape = pack.ShapeFor(heldItem);

            // Where the hotbar is under the cursor. Asked every frame rather than only on the
            // click, because the slot has to light up while the player is still deciding.
            hoveredSlot = InventoryUI.SlotIndexUnder(PackPointer.CursorPosition);

            bool overHotbar = hoveredSlot >= 0;

            // The bar is drawn over the same screen the rig is, so a face under the cursor is not
            // enough on its own: seating and cells belong to the MAT, and while the cursor is
            // over the bar the click means something else entirely — there the copy free-follows
            // the cursor instead of snapping to the face behind the HUD.
            bool overMat = overSurface && !overHotbar;

            if (overMat)
            {
                targetSurface = surface;

                // Grid-snapped and NOTHING else. The item goes exactly where the cursor is
                // pointing, at exactly the turn it is being shown at, and the only question left
                // is whether that is legal — which is what the cells answer.
                targetUv = PackLayout.Snap(surface.Id, surface.Size, shape, uv, yaw);

                placementLegal = pack.Layout.CanPlace(surface.Id, surface.Size, shape,
                                                      targetUv, yaw, HeldItemId());
            }
            else
            {
                placementLegal = false;
            }

            InventoryUI.SetDropTarget(hoveredSlot);

            // Built at the lift for both sources; this is the safety net for the rare copy that
            // failed to build there, retried where a face gives it a seat. BeginCarry ends the
            // failed carry, which clears the origin outline — put back after, null for a hotbar
            // lift and a harmless no-op then.
            if (!proxyBuilt && overMat)
            {
                proxyBuilt = visuals.BeginCarry(heldItem.itemPrefab, targetSurface, targetUv, yaw);
                visuals.SetGhost(originVisual);
            }

            // The copy is the ONE readout of what is in the hand, and it is on screen wherever
            // the cursor goes: seated and grid-snapped over a face, riding the cursor ray at the
            // pack's height everywhere else — across the sand, past the rig's edge, under the
            // bar. Nothing hides it and nothing stands in for it.
            if (proxyBuilt)
            {
                if (overMat) visuals.MoveCarry(heldItem.itemPrefab, targetSurface, targetUv, yaw);
                else visuals.MoveCarryFree(heldItem.itemPrefab, FreeCarryPoint(pack), yaw);
            }

            // Cells only mean something on a face: they answer "can this land HERE", and off the
            // faces there is no here. Over the bar the click puts the item in the slot, so cells
            // drawn on the face behind it would promise a landing the click was never going to
            // honour.
            if (overMat)
            {
                cellGrid.ShowLattice(targetSurface, pack.Layout, HeldItemId());

                PackShape oriented = PackOverhang.Clamp(targetSurface.Id, targetSurface.Size,
                                                        shape.Rotated(PackGrid.QuarterTurns(yaw)));

                cellGrid.Show(targetSurface,
                              PackGrid.BlockOrigin(targetSurface.Size, targetUv, oriented.Size),
                              oriented, placementLegal);
            }
            else
            {
                cellGrid.Hide();
            }

            visuals.ShowName(overHotbar ? SlotHint(hoveredSlot) : null,
                             PackPointer.CursorPosition);

            // Over the bar the click belongs to the HUD, which resolves it on the pointer's RELEASE
            // through InventoryUI.ClickSlot — see PutIntoSlot. Polling the button here as well
            // would resolve one press twice: the placement on the press, and then, on the release,
            // a lift of whatever now sits in the slot it was just put into.
            if (!overHotbar && mouse != null && mouse.leftButton.wasPressedThisFrame)
                ClickWhileCarrying(pack);
        }

        /// <summary>
        /// The click that resolves a carry anywhere but over the bar. Three outcomes, and which
        /// one it is was settled by where the cursor is standing.
        ///
        /// <para>
        /// <b>Over a face, cells green</b> — put down there, at the turn shown.
        /// </para>
        /// <para>
        /// <b>Over a face, cells red</b> — the item turns a quarter and stays in hand. This is the
        /// refusal, and it is a useful one: the commonest reason a spot is refused is that the item
        /// is the wrong way round for it, so the refusal and the fix are the same click. The one
        /// shape with no turn to offer — a square, whose quarter turn occupies the identical
        /// cells — gets the refusal flash instead; see <see cref="Turn"/>.
        /// </para>
        /// <para>
        /// <b>Anywhere else — the sand, the sky, past the rig's edge</b> — the item turns a
        /// quarter too, silently. Rotation is the one verb a click off the faces can usefully
        /// mean, so it means it everywhere: the player squares an item up wherever they are and
        /// then aims it, and because legality is always judged at the turn being shown, the item
        /// lands wherever its rotated shape fits. A square turning out here is a silent no-op
        /// rather than a flash — nothing was refused.
        /// </para>
        /// <para>
        /// The fourth outcome — over a hotbar slot, where it goes in that slot and swaps with
        /// whatever was there — is not reached from here. That click is the HUD's, and it arrives
        /// at <see cref="PutIntoSlot"/> through <c>InventoryUI.ClickSlot</c>; see the note at the
        /// call site above for why exactly one of the two may own it.
        /// </para>
        /// </summary>
        private void ClickWhileCarrying(BackpackObject pack)
        {
            bool overFace = overSurface && targetSurface != null;

            if (overFace && placementLegal)
            {
                PutDown();
                return;
            }

            // On red cells the click was a refusal looking for a fix, so a turn that cannot
            // change anything flashes; off the faces nothing was refused and the same turn
            // passes silently.
            Turn(pack, flashIfUnchanged: overFace);
        }

        /// <summary>
        /// A quarter turn, in the player's hand. The answer to a refused click, and to any click
        /// off the faces.
        ///
        /// <para>
        /// Rotation in the hand is unconditional. The shape library's per-item
        /// <c>allowRotation</c> rows are deliberately NOT consulted here (nor in
        /// <see cref="OnYawScrolled"/>): being able to turn the thing you are holding is a hard
        /// interaction requirement, and a row that vetoed it produced a click that did nothing
        /// with nothing on screen to say why. The library itself is untouched — its rows still
        /// author shapes — it just no longer vetoes the hand.
        /// </para>
        /// <para>
        /// One shape has no answer to give: a SYMMETRIC one — a 1x1, a 2x2, any square — whose
        /// quarter turn occupies the very same cells. Turning it succeeds, changes the yaw, and
        /// changes nothing the player can see. Where the click was a refusal looking for a fix,
        /// that reads as a button that does not work, so <paramref name="flashIfUnchanged"/>
        /// makes it flash instead; off the faces the same turn just passes as the no-op it is.
        /// </para>
        /// </summary>
        private void Turn(BackpackObject pack, bool flashIfUnchanged)
        {
            if (flashIfUnchanged && !TurningWouldChangeAnything(pack))
            {
                deniedUntil = Time.unscaledTime + DeniedFlashSeconds;
                visuals.SetCarryDenied(true);
                return;
            }

            yaw = PackGrid.SnapYaw(Mathf.Repeat(yaw + YawPerNotch, 360f));
        }

        /// <summary>
        /// Would a quarter turn land the held item on a different set of cells?
        ///
        /// <para>
        /// <see cref="PackShape"/> has no equality operator, and adding one for this would be a
        /// public API for a private question — so the two orientations are compared cell by cell
        /// here. Cheap: this runs once per refused click, not per frame.
        /// </para>
        /// </summary>
        private bool TurningWouldChangeAnything(BackpackObject pack)
        {
            PackShape shape = pack.ShapeFor(heldItem);

            int turns = PackGrid.QuarterTurns(yaw);

            PackShape now = shape.Rotated(turns);
            PackShape next = shape.Rotated(turns + 1);

            if (now.Width != next.Width || now.Height != next.Height) return true;

            for (int y = 0; y < now.Height; y++)
                for (int x = 0; x < now.Width; x++)
                    if (now[x, y] != next[x, y]) return true;

            return false;
        }

        /// <summary>
        /// Put the held item down on the spot being shown.
        ///
        /// <para>
        /// A REQUEST and nothing else — no local move, no optimistic visual. The placed copy stays
        /// exactly where it is until the layout changes underneath it, which is what a server that
        /// allowed the action publishes; a server that refuses publishes nothing, and the item was
        /// never anywhere else.
        /// </para>
        /// </summary>
        private void PutDown()
        {
            // Read off before LetGo clears them, because letting go has to happen first — see
            // LetGo for why the request cannot be the thing that goes first.
            HandSource from = heldFrom;
            int slot = originSlot;
            PackSurfaceId fromSurface = originSurface;
            Vector2 grab = originGrab;

            PackSurfaceId toSurface = targetSurface.Id;
            Vector2 toUv = targetUv;
            float turn = yaw;

            // The stow names its source by INDEX — a hotbar slot is a numbered box — while every
            // part of what the player lined up came from the ITEM: its shape, its snap, its
            // legality, the copy under the cursor. A slot whose contents changed in between still
            // resolves, and would land a different item on cells chosen for this one's footprint.
            if (from == HandSource.Hotbar && !StillInOriginSlot())
            {
                ReturnToOrigin();
                return;
            }

            LetGo();

            if (from == HandSource.Pack)
                controller.RequestMove(fromSurface, grab, toSurface, toUv, turn);
            else
                controller.RequestStow(slot, toSurface, toUv, turn, interactor);
        }

        /// <summary>
        /// Is the item in hand still the one sitting in the slot it was lifted out of?
        ///
        /// Compared by REFERENCE, against the asset the whole preview was built from. This project
        /// addresses shared and saved things by identity rather than by position everywhere else,
        /// and a hotbar index is exactly the kind of name that keeps resolving after it has stopped
        /// meaning what it meant.
        /// </summary>
        private bool StillInOriginSlot()
        {
            IPlayerInventory hotbar = Hotbar();
            if (hotbar == null) return false;
            if (originSlot < 0 || originSlot >= hotbar.GetInventorySize()) return false;

            InventorySlot slot = hotbar.GetSlot(originSlot);

            return slot != null && !slot.IsEmpty && slot.Item == heldItem;
        }

        /// <summary>
        /// Put the held item into a hotbar slot. Called by the HUD, which resolves a click on a
        /// slot through <c>InventoryUI.ClickSlot</c>.
        /// </summary>
        public void PutIntoSlot(int slotIndex)
        {
            if (!carrying) return;

            BackpackObject pack = controller != null ? controller.Pack : null;
            if (pack == null) return;

            if (heldFrom == HandSource.Hotbar)
            {
                // Hotbar reordering is not this feature — IPlayerInventory has no move — so the
                // only slot a hotbar item may go back into is its own, and that is a cancel.
                if (slotIndex != originSlot)
                {
                    InventoryUI.ShakeSlot(slotIndex);
                    return;
                }

                ReturnToOrigin();
                return;
            }

            // The placement this take names is the one the item was lifted off, and another player
            // in the same pack can have moved or taken it since. CanTakeToHotbar answers false with
            // refused FALSE for that case — it cannot tell a vanished placement from a hotbar it
            // was handed null for — so the guard below would wave it through into a request that
            // can only be refused, with the hand already emptied. OnLayoutChanged normally catches
            // this the moment the change is published; this is the same question asked at the last
            // possible instant instead of the first.
            if (!OriginStillThere())
            {
                InventoryUI.ShakeSlot(slotIndex);
                ReturnToOrigin();
                return;
            }

            // Asked before the request goes out, and asked LOCALLY. A full hotbar is not a refusal
            // — TryTakeToHotbar swaps — but a swap with nowhere to put the displaced item is, and
            // the server refuses it by changing nothing, which on this screen is indistinguishable
            // from a lost packet.
            if (!pack.CanTakeToHotbar(originSurface, originGrab, Hotbar(), out bool refused, slotIndex)
                && refused)
            {
                InventoryUI.ShakeSlot(slotIndex);
                return;
            }

            PackSurfaceId from = originSurface;
            Vector2 grab = originGrab;

            LetGo();

            controller.RequestTake(from, grab, interactor, slotIndex);
        }

        /// <summary>
        /// The id a legality test should ignore, which is the one currently in the hand.
        ///
        /// <para>
        /// Null for a hotbar lift, and that is the whole point of the distinction: an item lifted
        /// off the mat is not in its own way, but one still sitting in the hotbar has never
        /// occupied anything, so there is no placement of its own to ignore.
        /// </para>
        /// </summary>
        private string HeldItemId() =>
            heldFrom == HandSource.Pack && heldItem != null ? heldItem.ID : null;

        /// <summary>The label for the item under the cursor, built once per item rather than once
        /// per frame — see the cached-label note on the fields.</summary>
        private string HoverHint(InventoryItem item)
        {
            if (item == null) return null;

            if (item != hintItem)
            {
                hintItem = item;
                hintText = item.itemName + "   (click to pick up)";
            }

            return hintText;
        }

        /// <summary>
        /// The label for the slot under the cursor, built once per slot.
        ///
        /// Keyed on the index rather than pre-built for the four keys the hotbar has today, because
        /// the bar's length is <c>IPlayerInventory.GetInventorySize</c>'s to decide: a fixed table
        /// would answer a fifth slot with no label at all, silently.
        /// </summary>
        private string SlotHint(int index)
        {
            if (index != slotHintIndex)
            {
                slotHintIndex = index;
                slotHintText = index >= 0 ? $"Click to put it in slot {index + 1}" : null;
            }

            return slotHintText;
        }

        /// <summary>
        /// The wheel turns what is in hand, in either direction — the click's rotate only goes one
        /// way, and three clicks to get back one quarter is a worse deal than a notch of scroll.
        /// Unconditional for the same reason the click's turn is — see <see cref="Turn"/>.
        /// </summary>
        private void OnYawScrolled(int notches)
        {
            if (!carrying) return;

            yaw = PackGrid.SnapYaw(Mathf.Repeat(yaw + notches * YawPerNotch, 360f));
        }

        // ── Carrying off the faces ───────────────────────────────────────────

        /// <summary>The first wired face: the frame a free-floating copy is built against, and
        /// the height reference when no face has been hovered yet. Null only for a rig with no
        /// surfaces at all — which has nothing to place on and never gets this far.</summary>
        private static PackSurface FirstSurface(BackpackObject pack)
        {
            IReadOnlyList<PackSurface> surfaces = pack != null ? pack.Surfaces : null;
            if (surfaces == null) return null;

            for (int i = 0; i < surfaces.Count; i++)
                if (surfaces[i] != null) return surfaces[i];

            return null;
        }

        /// <summary>
        /// Where the carried copy sits while the cursor is over no face: the cursor ray
        /// intersected with a horizontal plane at the pack's own surface height, so the copy
        /// slides across the sand at mat level rather than diving to the ground or pinning to
        /// the camera — with <see cref="FreeCarryRayMetres"/> along the ray as the fallback when
        /// the ray never meets that plane.
        /// </summary>
        private Vector3 FreeCarryPoint(BackpackObject pack)
        {
            Camera cam = focusCamera != null ? focusCamera.Camera : null;

            // The last face the carry was over keeps the height continuous as the copy slides
            // off its edge; before it has ever been over one, any wired face is the pack's level.
            PackSurface reference = targetSurface != null ? targetSurface : FirstSurface(pack);

            float height = reference != null
                ? reference.ToWorld(reference.Size * 0.5f, 0f).y
                : 0f;

            return PackPointer.CursorPointAtHeight(cam, height, FreeCarryRayMetres);
        }
    }
}
