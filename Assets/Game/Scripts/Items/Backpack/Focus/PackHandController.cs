using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

// UnityEngine.InputSystem has a PlayerInputManager of its own, for local multiplayer join
// handling. This project has never used it; the one meant here is always ours.
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Items
{
    /// <summary>
    /// The hands of focus mode: hover, pick up, turn, put down, or send straight to the hotbar.
    ///
    /// <para>
    /// Lives on the focus camera's own GameObject and dies with it, so there is no path where a
    /// drag outlives the view it was being performed in.
    /// </para>
    /// <para>
    /// <b>Nothing here is optimistic.</b> A release sends a request and the display does not move;
    /// what moves it is the layout change the server publishes back. Two players can be in one
    /// pack, and an item that appeared in your hand and then vanished again is worse than a round
    /// trip you can see coming.
    /// </para>
    /// <para>
    /// Hit-testing is in <see cref="PackPointer"/> and everything drawn is in
    /// <see cref="PackDragVisuals"/>; what is left here is the state machine and the requests.
    /// </para>
    /// <para>
    /// <b>The hotbar is the fifth surface.</b> A drag that starts on a hotbar slot and a drag that
    /// starts on the mat run through the same state machine and the same frame of hit-testing —
    /// see <see cref="DragSource"/>. The alternative was a second drag system living in the HUD
    /// with its own idea of what a legal placement is, which is two things to keep in agreement
    /// about a question with one answer.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PackDragController : MonoBehaviour
    {
        /// <summary>
        /// The drag controller of the session on screen, or null when there is no focus session.
        ///
        /// The HUD needs a way to reach this and has no reference to it: the hotbar is a prefab
        /// under the player, and the controller is a component added at runtime to a camera that
        /// did not exist a frame ago. There is at most one focus session on one machine — see
        /// <see cref="PackFocusSession.Active"/> — so there is at most one of these.
        /// </summary>
        public static PackDragController Active { get; private set; }

        /// <summary>Where the thing in the player's hand came from.</summary>
        private enum DragSource
        {
            /// <summary>Lifted off the mat. Released by the mouse button, and it can be thrown away.</summary>
            Pack,

            /// <summary>Dragged off a hotbar slot. Released by the EventSystem, and it cannot.</summary>
            Hotbar,
        }

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

        /// <summary>Seconds a refused drop takes to slide back where it came from.</summary>
        private const float SpringBackSeconds = 0.14f;

        /// <summary>Seconds the hover rim stays refusal-red after a refused take.</summary>
        private const float DeniedFlashSeconds = 0.3f;

        private float deniedUntil;

        /// <summary>
        /// The visual <see cref="SetHovered"/> last rim-lit, so a change of target can be told
        /// apart from the same item being re-hit frame after frame — see <see cref="SetHovered"/>.
        /// </summary>
        private GameObject hoveredVisual;

        private PackFocusCamera focusCamera;
        private BackpackController controller;
        private Interactor interactor;
        private PlayerInputManager input;

        private PackDragVisuals visuals;

        /// <summary>
        /// The cells the magnet-snapped ghost will occupy, one square each, plus the lattice of the
        /// whole hovered face underneath it. One colour, not a verdict: <c>PackLayout.TryFindNearest</c>
        /// never hands back a spot the release cannot have, so there is nothing left for these cells
        /// to refuse.
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

        // ── Drag state ───────────────────────────────────────────────────────
        private bool dragging;
        private InventoryItem dragItem;
        private DragSource dragFrom;
        private GameObject originVisual;
        private PackSurfaceId originSurface;
        private Vector2 originUv;

        /// <summary>
        /// A cell the dragged item really fills, as opposed to <see cref="originUv"/>, which is
        /// where its block is centred. This is what goes on the wire — see
        /// <see cref="PackLayout.TryAnchorUv"/> for why the two had to come apart.
        /// </summary>
        private Vector2 originGrab;

        private float originYaw;

        /// <summary>The hotbar slot a <see cref="DragSource.Hotbar"/> drag came out of.</summary>
        private int originSlot = -1;

        /// <summary>
        /// Has the dragged copy been built yet?
        ///
        /// <para>
        /// A pack drag starts on a surface, so it has one from the first frame. A hotbar drag
        /// starts over the HUD, which is nowhere near the rig, and there is no face to seat a
        /// true-size proxy against until the cursor reaches one — so the copy appears the moment
        /// the player brings the item over the mat, and there is nothing floating in the corner of
        /// the screen before that.
        /// </para>
        /// </summary>
        private bool proxyBuilt;

        /// <summary>The hotbar slot under the cursor this frame, or -1. Only tracked mid-drag.</summary>
        private int hoveredSlot = -1;

        private PackSurface targetSurface;
        private Vector2 targetUv;
        private float yaw;

        /// <summary>
        /// The yaw the magnet-snapped spot actually uses. Usually equal to <see cref="yaw"/>,
        /// which is the player's intent and is only written by the wheel; they part when the
        /// held shape only fits the face turned, and everything drawn or requested reads this
        /// one so the preview and the placement stay the same cells.
        /// </summary>
        private float targetYaw;

        private bool dropIsLegal;

        // ── Caching the magnet search ────────────────────────────────────────
        //
        // TryFindNearest walks every legal cell on the face, and a still cursor asks it the exact
        // same question sixty times a second. Its answer only moves when the cursor crosses into a
        // new grid cell, the wheel turns the player's intent yaw, the drag reaches a different
        // face, or the layout underneath changes — the last of which reuses the OnChanged signal
        // OnLayoutChanged already answers for the lattice.
        //
        // Keyed on the RAW uv, not the cursor's cell, even though the grid is what the search
        // snaps to: TryFindNearest minimises distance to each candidate's BLOCK CENTRE, which for
        // an even-extent shape sits half a cell off the grid's own cell centres. Quantising the key
        // to a cell would then change answer twice per cell crossed, with the direction of travel
        // deciding which half got the stale one — a ghost that visibly lags the cursor. A raw-uv
        // key is exact by construction; it still hits every frame a genuinely still cursor asks the
        // same question, which is the case this cache exists for, and only re-searches on actual
        // mouse movement — no worse than before the cache existed.
        private PackSurface nearestSurfaceKey;
        private Vector2 nearestUvKey;
        private float nearestYawKey;
        private bool nearestSearchDirty = true;

        private bool nearestFound;
        private Vector2 nearestUv;
        private float nearestYaw;

        /// <summary>
        /// Is the cursor on one of the rig's faces AT ALL this frame?
        ///
        /// <para>
        /// Distinct from <see cref="dropIsLegal"/>, and the distinction is spec 5.1 versus 5.2.
        /// Clashing with something placed or hanging over the edge is no longer a refusal at all —
        /// the magnet moves the ghost to the nearest spot that clears both. The one refusal left is
        /// <see cref="dropIsLegal"/> false while still <c>overSurface</c>: this face has no room for
        /// the shape at any permitted turn, full stop. Off every face is a different thing again —
        /// it is the player throwing the thing on the ground, which is a different verb with a
        /// different request behind it.
        /// </para>
        /// <para>
        /// It cannot be inferred from <see cref="targetSurface"/>, which deliberately keeps the
        /// last face the cursor was over so the drag proxy has somewhere to sit while the cursor is
        /// off in the sand.
        /// </para>
        /// </summary>
        private bool overSurface;

        private Coroutine springBack;

        // ── Flipping the leaf ────────────────────────────────────────────────
        //
        // A second drag, running through the same button and the same frame of hit-testing as the
        // one above but carrying no item: the player grabs the front leaf's free edge and pulls it
        // through its arc into the rack, or pulls it back down. R still does it as a toggle — this
        // is a gesture ADDED beside the key, not a replacement for it.
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

        public static PackDragController Attach(PackFocusCamera focusCamera, BackpackController controller,
                                                Interactor interactor, PlayerInputManager input)
        {
            if (focusCamera == null || controller == null) return null;

            var drag = focusCamera.gameObject.AddComponent<PackDragController>();
            drag.focusCamera = focusCamera;
            drag.controller = controller;
            drag.interactor = interactor;
            drag.input = input;

            // Subscribed here and not only in OnEnable, because AddComponent has already run both
            // Awake and OnEnable by the time these fields are set — OnEnable saw a null input and
            // hooked nothing, which is a scroll wheel that silently does not rotate anything.
            drag.Subscribe();

            // The lattice shows other items' cells, so any change to the layout — including one
            // published from another player's move — stales it. Not guarded on controller.Pack:
            // PackFocusSession.Enter already refused to call here at all with no Pack, so it is
            // guaranteed by the caller rather than checked again.
            drag.subscribedLayout = controller.Pack.Layout;
            drag.subscribedLayout.OnChanged += drag.OnLayoutChanged;

            Active = drag;
            return drag;
        }

        private void Awake()
        {
            visuals = new PackDragVisuals();
            cellGrid = new PackGridVisual();
        }

        private void OnEnable() => Subscribe();

        /// <summary>Idempotent, so the two callers above cannot double-hook the wheel.</summary>
        private void Subscribe()
        {
            if (input == null) return;

            input.OnPackYawScrolled -= OnYawScrolled;
            input.OnPackYawScrolled += OnYawScrolled;

            input.OnPackStowPressed -= OnStowKey;
            input.OnPackStowPressed += OnStowKey;
        }

        private void OnDisable()
        {
            if (input == null) return;

            input.OnPackYawScrolled -= OnYawScrolled;
            input.OnPackStowPressed -= OnStowKey;
        }

        /// <summary>The lattice and the cached magnet search both show other items' cells, so any
        /// layout change stales both.</summary>
        private void OnLayoutChanged()
        {
            if (cellGrid != null) cellGrid.MarkLatticeDirty();
            nearestSearchDirty = true;
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

            InventoryUI.ClearDragFeedback();

            visuals?.Dispose();
            cellGrid?.Dispose();
            cellGrid = null;
        }

        /// <summary>
        /// Puts down whatever is in hand without resolving it, and lets go of the leaf. The
        /// component stays alive and usable.
        ///
        /// <para>
        /// The half of <see cref="Cancel"/> that is not the teardown. It exists because the rack
        /// key needs to abandon a drag mid-flight — the surface the ghost is tracking is about to
        /// swing through ninety degrees — and calling Cancel for that destroyed the drag controller
        /// for the rest of the session, so one press of R silently stopped the player picking
        /// anything up.
        /// </para>
        /// </summary>
        public void AbandonDrag()
        {
            if (springBack != null) { StopCoroutine(springBack); springBack = null; }

            if (draggingLeaf) ReleaseLeaf(commit: false);

            if (!dragging) return;

            dragging = false;

            if (visuals != null) visuals.EndDrag();
            if (cellGrid != null) cellGrid.Hide();

            dragItem = null;
            originVisual = null;
            originSlot = -1;
            proxyBuilt = false;
            hoveredSlot = -1;

            InventoryUI.ClearDragFeedback();
        }

        /// <summary>Abandons whatever is in hand and tears the display down. The session's exit.</summary>
        public void Cancel()
        {
            if (springBack != null) { StopCoroutine(springBack); springBack = null; }

            // Before the fields are cleared: the leaf is the pack's own state, not this
            // component's, and leaving focus mid-flip must not leave it stranded halfway.
            if (draggingLeaf) ReleaseLeaf(commit: false);

            dragging = false;
            dragItem = null;
            proxyBuilt = false;

            // The HUD is not ours and outlives the session, so anything we asked it to draw has to
            // be taken back explicitly. Exiting focus mid-drag is a normal way out — any movement
            // key does it — and a hotbar left showing a reserved slot would never recover.
            InventoryUI.ClearDragFeedback();

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
            // to be in. Behind the cam/pack/springBack return it used to depend on two cooperating
            // mechanisms — the timer AND the drag machine reaching this line — and a spring-back in
            // progress is exactly a moment that return would otherwise skip it for.
            if (deniedUntil > 0f && Time.unscaledTime >= deniedUntil)
            {
                deniedUntil = 0f;
                visuals.SetHoverDenied(false);
            }

            Camera cam = focusCamera != null ? focusCamera.Camera : null;
            BackpackObject pack = controller != null ? controller.Pack : null;

            if (cam == null || pack == null || springBack != null) return;

            Mouse mouse = Mouse.current;

            if (draggingLeaf) UpdateLeafDrag(cam, pack, mouse);
            else if (dragging) UpdateDrag(cam, pack, mouse);
            else UpdateHover(cam, pack, mouse);
        }

        // ── Hovering ─────────────────────────────────────────────────────────

        /// <summary>
        /// Rim-lights <paramref name="visual"/> and un-rims whatever was lit before — every caller
        /// that used to reach <see cref="PackDragVisuals.SetHovered"/> directly goes through here
        /// instead, so none of them can forget this part.
        ///
        /// <para>
        /// A denied flash is keyed to the item it was refused on, not to the hover rim material
        /// itself — the material is shared, so a flash left running would follow the rim onto
        /// whatever the cursor lands on next. The moment the target actually changes, not merely
        /// gets re-hit the same frame after frame, the flash ends here; the timed expiry in
        /// <see cref="Update"/> still covers the cursor sitting still on the refused item.
        /// </para>
        /// </summary>
        private void SetHovered(GameObject visual)
        {
            if (visual != hoveredVisual && deniedUntil > 0f)
            {
                deniedUntil = 0f;
                visuals.SetHoverDenied(false);
            }

            hoveredVisual = visual;
            visuals.SetHovered(visual);
        }

        private void UpdateHover(Camera cam, BackpackObject pack, Mouse mouse)
        {
            // Kept up to date while hovering as well as while dragging, because the stow key needs
            // to know where the cursor is and it arrives on an input callback rather than in here.
            // It is the same plane intersection UpdateDrag uses — arithmetic against each face, not
            // a second raycast — so tracking it in both states costs nothing.
            overSurface = PackPointer.TryHitSurface(cam, pack.Surfaces,
                                                    out PackSurface hovered, out Vector2 hoveredUv);
            if (overSurface)
            {
                targetSurface = hovered;
                targetUv = hoveredUv;
            }

            bool over = PackPointer.TryHitItem(cam, out GameObject visual, out PackSurface surface, out Vector2 uv);

            // The bar is drawn over the same screen the rig is — that overlap is exactly why
            // InventoryUI.SlotIndexUnder exists. A press over a slot belongs to the HUD, which
            // resolves it through its own pointer handlers; it must never ALSO register here as a
            // press on the mat behind it, or one right-click over a slot could both stow that slot
            // and take whatever pack item happens to be framed underneath.
            bool overBar = InventoryUI.SlotIndexUnder(PackPointer.CursorPosition) >= 0;

            if (!over || !pack.TryFindAt(surface.Id, uv, out PackPlacement placement))
            {
                SetHovered(null);

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
                // nothing to say, and it is exactly where the stow gesture wants to be offered —
                // a key with no on-screen affordance is otherwise a verb nobody will ever find.
                visuals.ShowName(onBoard ? LeafHint(pack.IsRacked)
                                         : overSurface ? "Drag or right-click a hotbar item to stow it — or press 1-4"
                                         : null,
                                 PackPointer.CursorPosition);
                return;
            }

            InventoryItem item = pack.ItemFor(placement.ItemId);

            SetHovered(visual);

            // The name with the verb after it. Right-click straight to the hotbar is the one thing
            // in focus mode a player cannot discover by trying — there is no cursor change and no
            // affordance drawn for it — and it was doing nothing visible on a full hotbar too,
            // which reads as a feature that is simply broken rather than one nobody found.
            visuals.ShowName(item != null ? item.itemName + "   (right-click to take)" : null,
                             PackPointer.CursorPosition);

            if (mouse == null || overBar) return;

            if (mouse.leftButton.wasPressedThisFrame) BeginDrag(pack, item, placement, visual);
            else if (mouse.rightButton.wasPressedThisFrame) SendToHotbar(pack, placement);
        }

        /// <summary>
        /// Right mouse: straight onto the hotbar, through the path a take already travels.
        ///
        /// <see cref="BackpackObject.RequestTake"/> forwards to the pack's owner, which asks the
        /// server — the same round trip a placement makes, and the same reason: it is the server
        /// that decides which of two players got the last water cell.
        /// </summary>
        private void SendToHotbar(BackpackObject pack, PackPlacement placement)
        {
            if (interactor == null) return;

            // Asked before the request goes out, and asked LOCALLY. A full hotbar is not a refusal
            // — BackpackObject.TryTakeToHotbar swaps, and that path is reached from right here —
            // but a swap with nowhere to put the displaced item is, and the server refuses it by
            // changing nothing, which on this screen is indistinguishable from a lost packet.
            // The ANCHOR, not the placement's own uv: an item is named to the server by a point on
            // a face, and the centre of an L-shaped item's block is the corner the L does not fill.
            // See PackLayout.TryAnchorUv.
            Vector2 grab = pack.AnchorUv(placement);

            if (!pack.CanTakeToHotbar(placement.Surface, grab, Hotbar(), out bool refused)
                && refused)
            {
                deniedUntil = Time.unscaledTime + DeniedFlashSeconds;
                visuals.SetHoverDenied(true);
                return;
            }

            pack.RequestTake(placement.Surface, grab, interactor);

            SetHovered(null);
            visuals.ShowName(null, Vector2.zero);
        }

        // ── Flipping the leaf ────────────────────────────────────────────────

        // Two verbs share the bare board — the flip and the stow — so the hint names both. The
        // click is deliberately first: it is the one players kept failing to find while the flip
        // was an edge-only drag. The stow half now names all three ways in, matching the bare-mat
        // label, rather than falling a version behind it.
        private static string LeafHint(bool racked) =>
            racked ? "Click to lay the board flat — drag, right-click or 1-4 to stow here"
                   : "Click to stand the board up — drag, right-click or 1-4 to stow here";

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

            SetHovered(null);
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

        // ── Stowing from the hotbar ──────────────────────────────────────────

        /// <summary>
        /// A hotbar key while the pack is open: that slot's item goes onto the pack, under the
        /// cursor when the cursor is over a spot that will take it.
        ///
        /// <para>
        /// A way IN, and the mirror of the right-click above. Dragging the pouch onto the pack is
        /// the other and does the same thing through <see cref="BeginHotbarDrag"/>; this is the
        /// shortcut, and it keeps working with the cursor nowhere in particular because an
        /// unaimed stow first-fits rather than failing.
        /// </para>
        /// </summary>
        private void OnStowKey(int slotIndex) => StowSlot(slotIndex, aimUnderCursor: true);

        /// <summary>
        /// Put one hotbar slot's item on the pack: the 1-4 keys aimed at the cursor's spot,
        /// and a right-click on the slot itself unaimed — the cursor is on the HUD then, which
        /// is nowhere on the pack, so first-fit is the only honest answer.
        /// </summary>
        public void StowSlot(int slotIndex, bool aimUnderCursor)
        {
            // Mid-drag the player already has something in hand, and the cursor's spot is
            // spoken for by it. Springing back is a moment where the layout is about to change
            // too, and mid-flip the face the cursor is aiming at is halfway through ninety
            // degrees.
            if (dragging || draggingLeaf || springBack != null) return;

            BackpackObject pack = controller != null ? controller.Pack : null;
            if (pack == null || interactor == null) return;

            IPlayerInventory hotbar = Hotbar();
            if (hotbar == null) return;

            if (slotIndex < 0 || slotIndex >= hotbar.GetInventorySize()) return;

            InventorySlot slot = hotbar.GetSlot(slotIndex);
            InventoryItem item = slot != null && !slot.IsEmpty ? slot.Item : null;
            if (item == null) return;

            // Over a face is not enough: the spot has to actually take this item, or the aim
            // is a worse answer than the pack's own first fit.
            PackShape shape = pack.ShapeFor(item);

            bool wantAim = aimUnderCursor && overSurface && targetSurface != null;

            // The same magnet the drag path snaps to, not a raw Snap+CanPlace pair: with only
            // those two, a cursor sitting on an occupied cell collapsed straight to unaimed and
            // the item teleported to the server's first-fit corner, while a drag released at the
            // same spot would have landed on the NEAREST free one instead — one intent, two
            // different spatial stories. Yaw 0 and no turns is what TryStowFromHotbar places at,
            // so the prediction stays exact.
            Vector2 aimedUv = default;
            bool aimed = wantAim &&
                         pack.Layout.TryFindNearest(targetSurface.Id, targetSurface.Size, shape,
                                                    targetUv, preferredYaw: 0f, allowTurns: false,
                                                    out aimedUv, out _);

            PackSurfaceId surface = aimed ? targetSurface.Id : default;
            Vector2 uv = aimed ? aimedUv : Vector2.zero;

            if (!pack.CanStow(item, aimed, surface, uv))
            {
                InventoryUI.ShakeSlot(slotIndex);
                return;
            }

            pack.RequestStow(slotIndex, aimed, surface, uv, interactor);
        }

        /// <summary>
        /// The stowing player's hotbar. Resolved from the Interactor upward, which is the lookup
        /// <see cref="BackpackObject.RequestTake"/> documents: on this project's player the
        /// Interactor lives on the camera rig and the inventory is on the body above it.
        /// </summary>
        private IPlayerInventory Hotbar() =>
            interactor != null ? interactor.GetComponentInParent<IPlayerInventory>() : null;

        // ── Dragging ─────────────────────────────────────────────────────────

        private void BeginDrag(BackpackObject pack, InventoryItem item, PackPlacement placement, GameObject visual)
        {
            if (item == null || item.itemPrefab == null) return;

            PackSurface surface = pack.SurfaceFor(placement.Surface);
            if (surface == null) return;

            dragging = true;
            dragItem = item;
            dragFrom = DragSource.Pack;
            originSlot = -1;

            originVisual = visual;
            originSurface = placement.Surface;
            originUv = placement.Uv;
            originYaw = placement.Yaw;

            // Kept apart from originUv on purpose. originUv is where the item VISUALLY sits, which
            // is what a spring-back has to slide home to; originGrab is a cell the item actually
            // fills, which is the only point the server can resolve it from. For a rectangle they
            // are the same; for a mask with a hole in the middle of its block they are not.
            originGrab = pack.AnchorUv(placement);

            targetSurface = surface;
            targetUv = placement.Uv;
            yaw = placement.Yaw;
            targetYaw = placement.Yaw;

            // The cached magnet search is keyed on surface/uv/yaw alone, and ShowLattice's own key
            // omits ignoreItemId the same way — neither knows WHICH item, so a new drag with a
            // different shape or ignoreItemId must not reuse the previous item's answer just
            // because the cursor happens to land back on the same spot.
            nearestSearchDirty = true;
            if (cellGrid != null) cellGrid.MarkLatticeDirty();

            // The grab happened ON a face, so that is the state until the first UpdateDrag says
            // otherwise. Left false, a press and release inside one frame would read as a throw.
            overSurface = true;

            // The hover rim comes off first: hover and ghost both append a material to the same
            // renderers, and the one that gets there first is the one that can be taken back off.
            SetHovered(null);
            visuals.ShowName(null, Vector2.zero);

            // Spec 5.2: an outline stays where the item was, so the player can see what they are
            // about to leave empty.
            visuals.SetGhost(originVisual);
            visuals.BeginDrag(item.itemPrefab, surface, placement.Uv, placement.Yaw);
            proxyBuilt = true;
        }

        // ── Dragging off the hotbar ──────────────────────────────────────────

        /// <summary>
        /// A hotbar slot has been picked up and is being carried towards the pack.
        ///
        /// <para>
        /// The drag GESTURE belongs to the EventSystem — it began on a <c>Graphic</c>, and Unity's
        /// pointer plumbing is what noticed — but everything that follows is this state machine's:
        /// the same hit-test against the same faces, the same magnet-snapped ghost cells, and on
        /// release the same request. That is why this is an entry point rather than a system of
        /// its own.
        /// </para>
        /// <para>
        /// Answers false when it will not take the item, so the HUD can decline the drag rather
        /// than start one that can only end in nothing happening.
        /// </para>
        /// </summary>
        public bool BeginHotbarDrag(int slotIndex, InventoryItem item)
        {
            if (dragging || draggingLeaf || springBack != null) return false;
            if (visuals == null || item == null || item.itemPrefab == null) return false;

            BackpackObject pack = controller != null ? controller.Pack : null;
            if (pack == null || interactor == null) return false;

            // Asked unaimed, which is "is there room for this ANYWHERE on the pack" — and it also
            // answers the one refusal a footprint test cannot see: the same asset already lying on
            // the mat. The layout is keyed by id, so that stow is going to be refused wherever the
            // player puts it, and a drag that can only ever end in nothing happening is worse than
            // a drag that refuses to start.
            if (!pack.CanStow(item, aimed: false, default, Vector2.zero))
                return false;

            dragging = true;
            dragItem = item;
            dragFrom = DragSource.Hotbar;
            originSlot = slotIndex;

            originVisual = null;
            originSurface = default;
            originUv = Vector2.zero;
            originGrab = Vector2.zero;
            originYaw = 0f;

            yaw = 0f;
            targetYaw = 0f;

            // See the matching note in BeginDrag: a different item means the cached magnet search
            // and the lattice both no longer apply, even if the cursor lands on the same spot.
            nearestSearchDirty = true;
            if (cellGrid != null) cellGrid.MarkLatticeDirty();

            // No proxy and no target yet. The cursor is over the HUD at the bottom of the screen,
            // which is not a face, and TryHitSurface is what will say otherwise on some later
            // frame — see proxyBuilt.
            targetSurface = null;
            overSurface = false;
            dropIsLegal = false;
            proxyBuilt = false;

            SetHovered(null);
            visuals.ShowName(null, Vector2.zero);

            return true;
        }

        /// <summary>
        /// The hotbar drag has been let go. Called by the HUD, never by the mouse poll.
        ///
        /// <para>
        /// A pack drag ends when the button comes up, read straight off the device. This one must
        /// not: the button comes up in the same frame the EventSystem raises its own end-of-drag,
        /// and which of the two this component sees first is not defined. Ending it in exactly one
        /// place means the gesture cannot be resolved twice — once as a stow and once as nothing —
        /// and it is the HUD's <c>OnEndDrag</c> that is guaranteed to arrive.
        /// </para>
        /// </summary>
        public void EndHotbarDrag()
        {
            if (!dragging || dragFrom != DragSource.Hotbar) return;

            dragging = false;

            BackpackObject pack = controller != null ? controller.Pack : null;

            if (pack != null && overSurface && dropIsLegal && targetSurface != null)
            {
                // The same request the 1-4 keys send, with the cursor's spot rather than the
                // cursor's last known spot. Nothing is applied locally: the item stays in the
                // hotbar until the server's answer arrives as a layout change and a slot change.
                controller.RequestStow(originSlot, aimed: true, targetSurface.Id, targetUv, interactor);
            }
            else if (pack != null && overSurface)
            {
                // Over a face with no room at THIS spot, at any turn — not the pack being full.
                // BeginHotbarDrag already proved with CanStow(aimed: false) that the pack has room
                // SOMEWHERE, and the keys and right-click both fall through to the server's own
                // first-fit rather than refusing outright — a drag aimed at a crowded corner must
                // land the same way, or it is the one stow verb on this whole feature that still
                // gives up instead of finding room itself.
                controller.RequestStow(originSlot, aimed: false, default, Vector2.zero, interactor);
            }

            // Off the pack entirely is a cancel, not a throw: dragging out of the PACK and letting
            // go over the sand is how gear is thrown away, but this item never left the hotbar, so
            // letting go halfway is just a player saying "no, not that one" — nothing to animate
            // back, since nothing left the slot in the first place.

            EndHotbarDragVisuals();
        }

        /// <summary>Drops the hotbar drag on the floor without resolving it. The session's exit.</summary>
        public void CancelHotbarDrag()
        {
            if (!dragging || dragFrom != DragSource.Hotbar) return;

            dragging = false;
            EndHotbarDragVisuals();
        }

        private void EndHotbarDragVisuals()
        {
            if (visuals != null) visuals.EndDrag();
            if (cellGrid != null) cellGrid.Hide();

            dragItem = null;
            originSlot = -1;
            proxyBuilt = false;
            hoveredSlot = -1;

            InventoryUI.ClearDragFeedback();
        }

        private void UpdateDrag(Camera cam, BackpackObject pack, Mouse mouse)
        {
            overSurface = PackPointer.TryHitSurface(cam, pack.Surfaces,
                                                    out PackSurface surface, out Vector2 uv);

            PackShape shape = pack.ShapeFor(dragItem);

            if (overSurface)
            {
                targetSurface = surface;

                // MAGNET-SNAPPED, not merely grid-snapped: the ghost is put on the nearest
                // spot the release can legally have, so everything after this line — the
                // proxy, the cells and the request — describes a placement that will succeed.
                // A hotbar drag is pinned to yaw 0 because NetMsg.PackStow carries no yaw and
                // the server places at zero; letting the preview turn would show cells the
                // placement then would not use.
                bool mayTurn = dragFrom == DragSource.Pack &&
                               PackShapes.AllowsRotation(dragItem, pack.Shapes);
                float preferredYaw = dragFrom == DragSource.Hotbar ? 0f : yaw;

                // Cached: see the field group above. Keyed on the raw uv rather than the cursor's
                // cell — TryFindNearest answers about block CENTRES, which for an even-extent shape
                // sit half a cell off the grid's own cell centres, so a cell-quantised key would
                // change answer twice per cell and lag the cursor by up to one. The raw uv is
                // exact and still hits on every frame a still cursor asks the same question again.
                if (nearestSearchDirty || targetSurface != nearestSurfaceKey ||
                    uv != nearestUvKey || !Mathf.Approximately(preferredYaw, nearestYawKey))
                {
                    nearestSurfaceKey = targetSurface;
                    nearestUvKey = uv;
                    nearestYawKey = preferredYaw;
                    nearestSearchDirty = false;

                    nearestFound = pack.Layout.TryFindNearest(
                        targetSurface.Id, targetSurface.Size, shape, uv,
                        preferredYaw, mayTurn,
                        out nearestUv, out nearestYaw,
                        ignoreItemId: DragItemId());
                }

                dropIsLegal = nearestFound;

                if (dropIsLegal)
                {
                    targetUv = nearestUv;
                    targetYaw = nearestYaw;
                }
                else
                {
                    // No room on this face at any turn: the proxy still has to follow the cursor, or
                    // it strands on the last face at coordinates that face does not have.
                    targetUv = PackLayout.Snap(targetSurface.Id, targetSurface.Size, shape, uv, yaw);
                    targetYaw = yaw;
                }
            }
            else
            {
                dropIsLegal = false;
            }

            // Where the hotbar is under the cursor, which is a drop target for a pack drag and the
            // way home for a hotbar one. Asked every frame rather than only on release, because
            // the slot has to light up while the player is still deciding.
            hoveredSlot = InventoryUI.SlotIndexUnder(PackPointer.CursorPosition);

            bool overHotbar = hoveredSlot >= 0;

            InventoryUI.SetDropTarget(dragFrom == DragSource.Pack ? hoveredSlot : -1);

            // The proxy is built the first time the cursor reaches a face. For a pack drag that is
            // the frame the drag began; for a hotbar drag it is whenever the player gets there.
            if (!proxyBuilt && overSurface && targetSurface != null)
            {
                visuals.BeginDrag(dragItem.itemPrefab, targetSurface, targetUv, targetYaw);
                proxyBuilt = true;
            }

            if (proxyBuilt)
            {
                visuals.MoveDrag(dragItem.itemPrefab, targetSurface, targetUv, targetYaw);

                // Red over a face with no room at all — the one refusal magnet snap cannot
                // remove — and never over the sand, where the release is a throw, not an error.
                visuals.SetDragTint(!dropIsLegal && overSurface);
            }

            // The drag-path twin of the overBar guard in UpdateHover: while a PACK drag hovers a
            // hotbar slot, Release puts the item in THAT SLOT, not on the face behind the bar —
            // but the raycast against the rig keeps hitting whatever is back there regardless, so
            // without this the ghost cells and lattice kept promising a landing the drop was never
            // going to honour, which is exactly the invariant magnet snap exists to guarantee. A
            // hotbar drag has no such conflict: it has nowhere else to land, so its own cells stay.
            bool hidingCellsForHotbar = overHotbar && dragFrom == DragSource.Pack;

            if (overSurface && targetSurface != null && cellGrid != null && !hidingCellsForHotbar)
                cellGrid.ShowLattice(targetSurface, pack.Layout, DragItemId());
            else if (cellGrid != null)
                cellGrid.HideLattice();

            if (overSurface && dropIsLegal && targetSurface != null && cellGrid != null && !hidingCellsForHotbar)
            {
                PackShape oriented = PackOverhang.Clamp(targetSurface.Id, targetSurface.Size,
                                                        shape.Rotated(PackGrid.QuarterTurns(targetYaw)));

                cellGrid.Show(targetSurface,
                              PackGrid.BlockOrigin(targetSurface.Size, targetUv, oriented.Size),
                              oriented);
            }
            else if (cellGrid != null)
            {
                // Only the ghost's own cells, not the whole overlay: the lattice's show/hide is
                // already decided above, on overSurface alone. A face with literally no room is
                // exactly where the lattice earns its keep — it is the "where WOULD this go"
                // readout while the red-tinted proxy says "not here" — so it must survive a legality
                // refusal that only ever concerns the ghost.
                cellGrid.HideGhost();
            }

            if (overHotbar && dragFrom == DragSource.Pack)
                visuals.ShowName($"Drop into slot {hoveredSlot + 1}", PackPointer.CursorPosition);

            // A hotbar drag is ended by the EventSystem, in EndHotbarDrag, and must not also be
            // ended here — see the note there. Only a drag that began on the mat is released by
            // the button coming up.
            if (dragFrom == DragSource.Pack && mouse != null && !mouse.leftButton.isPressed) Release(pack);
        }

        /// <summary>
        /// The id a legality test should ignore, which is the one currently in the air.
        ///
        /// <para>
        /// Null for a hotbar drag, and that is the whole point of the distinction: an item lifted
        /// off the mat is not in its own way, but an item still sitting in the hotbar has never
        /// occupied anything, so there is no placement of its own to ignore.
        /// </para>
        /// </summary>
        private string DragItemId() =>
            dragFrom == DragSource.Pack && dragItem != null ? dragItem.ID : null;

        /// <summary>
        /// The wheel turns what is in hand — but only when it came off the mat.
        ///
        /// <para>
        /// A hotbar drag ends in <see cref="NetMsg.PackStow"/>, and that message has no yaw field:
        /// <see cref="BackpackObject.TryStowFromHotbar"/> places at zero. Letting the preview
        /// rotate anyway would be worse than not rotating at all — the footprint the player lined
        /// up would be the one the legality test used and NOT the one the server places, so a drop
        /// that read clear could land clashing, or be refused with no explanation. Held at zero, the
        /// prediction and the placement are the same rectangle. Rotating a stowed item is then an
        /// ordinary in-pack drag.
        /// </para>
        /// </summary>
        private void OnYawScrolled(int notches)
        {
            if (!dragging || dragFrom != DragSource.Pack) return;

            // An item whose authored row forbids turning ignores the wheel outright, rather than
            // turning in the preview and straightening on release.
            BackpackObject pack = controller != null ? controller.Pack : null;
            PackShapeLibrary shapes = pack != null ? pack.Shapes : null;

            if (!PackShapes.AllowsRotation(dragItem, shapes)) return;

            yaw = PackGrid.SnapYaw(Mathf.Repeat(yaw + notches * YawPerNotch, 360f));
        }

        /// <summary>
        /// Let go. Four outcomes, and which one it is was settled by the last frame of the drag.
        ///
        /// <para>
        /// <b>Over a hotbar slot</b> — it goes in that slot, swapping with whatever was there, or
        /// refusing and springing back exactly like the face case below when that swap has
        /// nowhere to put the displaced item. Tested first, because the hotbar is drawn over the
        /// same screen the sand is.
        /// </para>
        /// <para>
        /// <b>Off the mat entirely</b> — spec 5.1's fourth verb. The item leaves the pack and lands
        /// on the ground. This is not the same as a refused placement and must not spring back:
        /// dragging something out of the pack and letting go over the sand is how you throw it
        /// away, and a pack that quietly put it back would have no way to get rid of anything.
        /// </para>
        /// <para>
        /// <b>Over a face with no room for this shape at any permitted turn</b> — refused. It
        /// slides home. This is the one refusal magnet snap cannot remove: clashing and overhanging
        /// are no longer possible outcomes, since the ghost was already sitting on the nearest spot
        /// that avoids both.
        /// </para>
        /// <para>
        /// <b>Over a face and clear</b> — a move.
        /// </para>
        /// <para>
        /// All three of them are REQUESTS and nothing else — no local move, no optimistic visual,
        /// and above all no locally spawned pickup. The placed copy stays exactly where it is until
        /// the layout changes underneath it, which is what a server that allowed the action
        /// publishes; a server that refuses publishes nothing and the item was never anywhere else.
        /// A drop taken locally would be worse than a move taken locally, because only the server
        /// may spawn: the thrower would be the only player who ever saw the thing hit the ground.
        /// </para>
        /// </summary>
        private void Release(BackpackObject pack)
        {
            dragging = false;

            // Asked BEFORE the off-the-mat test, and that order is the point. The hotbar is drawn
            // across the bottom of the same screen the sand is, so without this a drag onto the
            // hotbar is a drag off the pack — the item lands on the ground under the rig, which is
            // the opposite of what the player just did.
            if (hoveredSlot >= 0)
            {
                // The named-slot twin of SendToHotbar's own predicted refusal: a slot already
                // holding this asset, or holding something with nowhere else to go, refuses on
                // the server exactly like a full-hotbar swap does — and until now that refusal
                // was a silent snap back with nothing on screen to explain it, while the very same
                // refusal on the right-click path flashed the rim red.
                if (!pack.CanTakeToHotbar(originSurface, originGrab, Hotbar(), out bool refused, hoveredSlot)
                    && refused)
                {
                    InventoryUI.ShakeSlot(hoveredSlot);
                    springBack = StartCoroutine(SpringBack());
                    return;
                }

                controller.RequestTake(originSurface, originGrab, interactor, hoveredSlot);

                visuals.EndDrag();
                FinishPackDrag();
                return;
            }

            if (!overSurface)
            {
                controller.RequestDrop(originSurface, originGrab);

                visuals.EndDrag();
                FinishPackDrag();
                return;
            }

            if (!dropIsLegal || targetSurface == null)
            {
                springBack = StartCoroutine(SpringBack());
                return;
            }

            controller.RequestMove(originSurface, originGrab, targetSurface.Id, targetUv, targetYaw);

            visuals.EndDrag();
            FinishPackDrag();
        }

        /// <summary>Clears the per-drag state every exit from a pack drag shares.</summary>
        private void FinishPackDrag()
        {
            dragItem = null;
            originVisual = null;
            proxyBuilt = false;
            hoveredSlot = -1;

            if (cellGrid != null) cellGrid.Hide();

            InventoryUI.ClearDragFeedback();
        }

        /// <summary>
        /// A refused drop slides home rather than blinking out, so the player sees that the item
        /// went back rather than wondering whether it went somewhere.
        /// </summary>
        private IEnumerator SpringBack()
        {
            PackSurface home = controller.Pack != null ? controller.Pack.SurfaceFor(originSurface) : null;
            Vector2 from = targetUv;

            if (home != null && dragItem != null)
            {
                // Surface and yaw snap on the first step and the uv slides. Both of those rebuild
                // the proxy where the uv only translates it, so this is one rebuild rather than
                // one per frame.
                for (float elapsed = 0f; elapsed < SpringBackSeconds; elapsed += Time.unscaledDeltaTime)
                {
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / SpringBackSeconds));
                    visuals.MoveDrag(dragItem.itemPrefab, home, Vector2.Lerp(from, originUv, t), originYaw);
                    yield return null;
                }
            }

            visuals.EndDrag();

            FinishPackDrag();
            springBack = null;
        }
    }
}
