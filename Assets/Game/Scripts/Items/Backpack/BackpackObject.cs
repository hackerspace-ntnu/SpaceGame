using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
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
    ///
    /// <para>
    /// What changed with free placement: there are no compartments and no slot indices. An item is
    /// somewhere on a <see cref="PackSurface"/>, at a uv in metres and a yaw, and the limit on what
    /// fits is surface area rather than a count. Every item is visible whenever the pack is —
    /// nothing is hidden inside a pocket any more, so there is no open-only half of the display.
    /// </para>
    /// </summary>
    public class BackpackObject : PackContainer, IInteractable
    {
        [Header("Rig")]
        [Tooltip("Every part that moves when the pack opens, in any order. The expedition rig " +
                 "wires PIVOT_Back, PIVOT_Leaf, PIVOT_Wing_L/R and PIVOT_Lid here and names each " +
                 "one's part so it gets its own beat. The wing and lid pivots (with the stakes " +
                 "and the lash rail) are CHILDREN of PIVOT_Leaf, so their folds are relative to " +
                 "the board and the closing flap carries them round to hug the pack's flanks and " +
                 "cap its top. The older clamshell wired PIVOT_Door_L and PIVOT_Door_R and left " +
                 "them Generic. Leaving this empty is legal — a pack with no moving parts still " +
                 "shows and gives up its contents.")]
        [SerializeField] private BackpackHinge[] hinges = new BackpackHinge[0];

        [Tooltip("The pins that pull the leaf's front corners down, if the rig has any — " +
                 "Mesh_Rig_Stake_L and _R. They ride the frame rather than a hinge, so they are " +
                 "driven by position: lifted clear while the pack is stowed, dropped on beat 5.")]
        [SerializeField] private Transform[] stakes = new Transform[0];

        [Tooltip("Metres the stakes sit above their authored position while the pack is stowed.")]
        [SerializeField, Min(0f)] private float stakeLift = 0.14f;

        [Tooltip("What the holders populate outward FROM on the last beat — the oxygen tank, " +
                 "which is the rig's fixed landmark and therefore the one place a player's eye is " +
                 "already resting when the pack finishes opening. Unwired, the pack's own origin " +
                 "stands in, which is close enough on any rig whose landmark is central.")]
        [SerializeField] private Transform holderOrigin;

        [Header("Opening")]
        [Tooltip("How long a hinge with no named part takes. The named parts are on the beat " +
                 "sheet instead and ignore this.")]
        [SerializeField, Min(0.01f)] private float openSeconds = 0.5f;

        [Header("Rack")]
        [Tooltip("Seconds the leaf takes to swing between lying flat and standing as a rack. " +
                 "Shorter than a deploy beat on purpose: this is a gesture the player makes and " +
                 "undoes while they are already looking at the pack, not an entrance.")]
        [SerializeField, Min(0.05f)] private float rackSeconds = 0.45f;

        public bool IsOpen { get; private set; }
        public bool IsWorn { get; private set; } = true;

        /// <summary>
        /// Is the front leaf standing up as a rack?
        ///
        /// <para>
        /// A mode of being open, not a fifth state on <see cref="BackpackController"/>. The
        /// controller's four states are about where the pack IS — on a back, in flight, on the sand
        /// — and every one of its request handlers already refuses anything that arrives while the
        /// state is not <c>Open</c>. Racked is not a different place; it is the same deployed pack
        /// with one member turned, the way a door being open is not a different room.
        /// </para>
        /// <para>
        /// Making it a state would also have cost two states, not one: the controller's own comment
        /// records that Deploying and Stowing exist because they animate and Open and Shouldered
        /// because they are settled, so a racked pose would need its own animating twin. And every
        /// one of the five <c>CurrentState != State.Open</c> guards would have had to learn about
        /// it, each of them a chance to forget.
        /// </para>
        /// </summary>
        public bool IsRacked { get; private set; }

        private BackpackController owner;
        private Coroutine doorRoutine;
        private Coroutine rackRoutine;
        private Collider bodyCollider;

        /// <summary>
        /// Where the leaf stands between the mat (0) and the rack (1).
        ///
        /// Separate from <see cref="sheetClock"/> and deliberately so: the two drive the SAME hinge
        /// and are combined in <see cref="LeafFromOpen"/>, but they answer different questions —
        /// the sheet is where the unfold has got to, this is what the player asked for.
        /// </summary>
        private float rackClock;

        // The hinges' authored rest orientations, captured once. The FBX does NOT hand empties back
        // at identity — PIVOT_Clamshell arrived at euler (270.02, 0, 0) — so every fold angle has
        // to be applied RELATIVE to these. Treating it as absolute reorients the whole part, which
        // on the previous clamshell buried a door 0.4 m under the ground.
        //
        // Named `rest`, not `closed`, because which pose rest IS depends on the model:
        // expedition_backpack is authored closed and expedition_rig is authored OPEN. That is
        // BackpackHinge.restIsOpen's whole job, and it is asked rather than assumed.
        private Quaternion[] restRotations;

        /// The stakes' authored (deployed) local positions, and the local offset that lifts them.
        private Vector3[] stakeRest;
        private Vector3[] stakeOffset;

        /// <summary>
        /// Where on the unfold's beat sheet the rig currently stands, in the sheet's own seconds.
        ///
        /// <para>
        /// A field rather than a coroutine local, because it is what makes an interrupted unfold
        /// continue from where the parts actually are. Driving the parts from a clock that restarts
        /// at one end of the sheet would snap the whole rig there for a frame before setting off
        /// again — the bug the old swing's "capture the CURRENT pose" comment was about, in the
        /// form it takes once every part has its own window.
        /// </para>
        /// </summary>
        private float sheetClock = SheetLanded;

        /// One entry per live holder, in pop order, with the scale HolderBuilder fitted it to.
        private readonly List<(Transform holder, Vector3 scale)> holderPop = new();

        private void Awake()
        {
            bodyCollider = GetComponent<Collider>();

            // The display subscription and the authored contents, both of which every container
            // needs and neither of which is about the rig. Ungated on Reaches, deliberately: a
            // pack in Awake is WORN, where the only reachable face is the exterior one, and
            // authored contents are a record of where the gear already is rather than somebody
            // choosing a face. See PackContainer.BeginContents.
            BeginContents();

            CaptureRestPose();

            // The pack starts on somebody's back, and expedition_rig is authored DEPLOYED — every
            // pivot at rotation zero in the open pose, because that is the pose whose measurements
            // the spec gives. Without this line a freshly instantiated rig is worn fully unfolded,
            // and it looks like the hinges are broken rather than like the pose was never applied.
            sheetClock = SheetLanded;
            ApplySheet(sheetClock);

            RebuildVisuals();
        }

        private void OnDestroy() => EndContents();

        /// <summary>Who is carrying this. Null once it is dropped for good.</summary>
        public void Bind(BackpackController controller) => owner = controller;

        /// <summary>
        /// The player this pack belongs to, and the channel every request about it travels on.
        ///
        /// Public because the pack has no NetworkObject of its own: anything that wants to ask the
        /// server for something has to ask through the wearer, who has both a channel and a relay.
        /// See the networking note in <see cref="BackpackController"/>.
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

            // A pack on a back is never racked. A stow normally gives the rack up before it gets
            // here — see ResolveRackForStow, which does it at the instant it costs no motion — so
            // this is the BACKSTOP, for the paths that arrive shouldered without a fold behind
            // them: a joiner told "shouldered", a save restore, Awake. It is the one
            // moment every machine agrees on regardless of what order it heard things in, and the
            // only one that cannot race a joiner's own restore. Announced as well as applied, or
            // the wire would go on telling the next joiner about a rack nobody is standing in
            // front of. On anything but the owner the announcement is a no-op and the local clear
            // is all that happens, which is the same answer.
            if (worn && IsRacked)
            {
                SnapRack(false);
                RackRequested?.Invoke(false);
            }
            else if (worn && rackClock != 0f)
            {
                // A drag that was still holding the leaf when the pack went back on. IsRacked was
                // never written — a scrub is a preview and nothing more — so the branch above does
                // not fire, and a clock left part way up would come back on the next deploy as a
                // leaf standing at an angle nobody asked for and no state accounts for.
                SnapRack(false);
            }

            // Off while worn. Colliders under the same Rigidbody are one compound collider, so a
            // worn pack would otherwise bolt a 0.35 x 0.53 m box onto the player's own capsule and
            // wedge them in every doorway they fit through before.
            if (bodyCollider != null) bodyCollider.enabled = !worn;
        }

        public void SetOpen(bool open)
        {
            if (IsOpen == open) return;
            IsOpen = open;

            // Deliberately nothing about the rack here, in EITHER direction.
            //
            // Closing must not touch it HERE: see LeafFromOpen. A leaf already standing at the
            // rack angle is already where stowing wants it, so the panel folds around it instead
            // of it dropping flat and being picked straight back up. The rack is given
            // up at the OTHER end of the fold, by ResolveRackForStow, which is the instant the two
            // angles coincide and the handover therefore costs no motion.
            //
            // Opening must not reset it either, and that one is a trap rather than a preference. A
            // joining client is told "this pack is Open" and reaches this line, while
            // BackpackNetwork is separately handing it the racked flag off the wire — two writes to
            // one leaf in an order Netcode does not promise. Whichever lost would decide whether
            // the joiner sees a rack, at random. The reset lives on SetWorn instead, which is the
            // one moment the two cannot disagree about: a pack on somebody's back is not racked.

            // No tear-down half any more. Every item is held by a strap or a bungee on a face of
            // the rig, so the display follows the hinges through the swing instead of being built
            // on open and destroyed on close.
            if (doorRoutine != null) StopCoroutine(doorRoutine);
            doorRoutine = null;

            // A pack that is not running cannot start a coroutine, and StartCoroutine THROWS
            // rather than returning null. Same guard, same reason, as SetRacked's: a prefab being
            // wired, an EditMode test or an object deactivated mid-swing gets the settled pose
            // instead of the animation. Without it BackpackController.RunStow would also be left
            // waiting on IsSwinging for a swing that never began.
            if (!isActiveAndEnabled)
            {
                if (open)
                {
                    sheetClock = SheetEnd;
                    ApplySheet(sheetClock);
                }
                else
                {
                    SnapStowed();
                }

                return;
            }

            doorRoutine = StartCoroutine(Unfold(open));
        }

        /// <summary>
        /// Is the rig still moving between its two poses?
        ///
        /// <para>
        /// What <see cref="BackpackController"/>'s stow waits on. The fold and the flight used to
        /// overlap and the fold was the longer of the two, so the last beat of it — the panel,
        /// which is the biggest member on the rig — finished after the pack was already glued to
        /// the player's back. Reversing the order needs somebody to be able to ask whether the
        /// fold is done, and this is that question.
        /// </para>
        /// </summary>
        public bool IsSwinging => doorRoutine != null;

        /// <summary>
        /// Put the whole rig in its stowed pose at once: side panels folded up to hug the flanks,
        /// the closed flap against the panel, panel down, stakes up, holders gone, rack given up.
        ///
        /// <para>
        /// <b>The one place that guarantees the closed pose</b>, and it exists because
        /// <see cref="SetOpen"/> cannot: <c>SetOpen(false)</c> on a pack whose
        /// <see cref="IsOpen"/> is already false returns without doing anything, so a fold that
        /// was interrupted — the player disabled mid-flight in a streaming world, a joiner told
        /// "shouldered" halfway through somebody else's stow — left the rig parked on a back at
        /// whatever angle it had reached. Snapping the sheet to its landed reading is exact,
        /// cheap, and idempotent, so it is safe to run at the end of every path that ends
        /// shouldered.
        /// </para>
        /// </summary>
        public void SnapStowed()
        {
            if (doorRoutine != null) StopCoroutine(doorRoutine);
            doorRoutine = null;

            IsOpen = false;
            sheetClock = SheetLanded;

            ResolveRackForStow();

            // ResolveRackForStow applies the sheet only when there was a rack to give up.
            ApplySheet(sheetClock);
        }

        /// <summary>
        /// Give the rack up, at the one instant in the fold when giving it up costs nothing.
        ///
        /// <para>
        /// The leaf's rack angle and its stow angle are the same number — that is the whole reason
        /// the rack needed no hinge of its own — so at <see cref="SheetLanded"/> the sheet is
        /// already asking for exactly the angle the rack is holding. Clearing the rack there moves
        /// the leaf by precisely zero degrees, which is why this is the right moment and the
        /// beginning of the fold is not: clearing it as the stow STARTS would drop the leaf flat
        /// and then pick it straight back up over the next second.
        /// </para>
        /// <para>
        /// Announced as well as applied, because a rack is replicated state: leaving the
        /// <c>NetworkVariable</c> set would go on telling the next joiner about a rack on a pack
        /// that is folded on somebody's back. On anything but the owner the announcement is a
        /// no-op and the local clear is the whole of it, which is the same answer.
        /// </para>
        /// </summary>
        private void ResolveRackForStow()
        {
            if (!IsRacked && rackClock == 0f) return;

            bool announce = IsRacked;

            SnapRack(false);

            if (announce) RackRequested?.Invoke(false);
        }

        // ------------------------------------------------------------------ the rack
        //
        // The front leaf flipped up into a vertical rack for the biggest gear — and the WHOLE
        // front comes with it, closing like a box. The wing and lid pivots, the stakes and the
        // lash rail are children of PIVOT_Leaf in the model, so nothing on the front can ever be
        // left behind on the sand (the redesign-by-playtest: ground-hinged wings read as the
        // board abandoning its sides). On top of that ride the flaps' own hinges, 90 relative
        // to the BOARD: closing stands them square up off it, and as the board rises the sides
        // come round to wrap the pack's flanks and the lid over its top, hugging the exterior.
        //
        // No extra hinge was added for the rack and that is the design, not a shortcut: every
        // part the rack moves is asked for the very angle its own stow fold already asks for —
        // the leaf X -90 from the authored deployed pose, each panel ±90 about the board's edge,
        // the lid -90 about its leading one, arriving as a hood over the raised board's top —
        // so stowed and racked are the same place for all four and the only difference is
        // what the panel and the sheet's other beats are doing. See LeafFromOpen and FlapFromOpen
        // for how the two demands meet on each hinge.
        //
        // Which face comes up is what decides everything else. Under X -90 the mat — SURF_Leaf,
        // the wings and the lash line — swings round to face the back panel, and the leaf's
        // UNDERSIDE rises to face the player. So the rack is the underside, PackSurfaceId.Rack
        // sits there, and the model puts its ladder frame and cradle horns there too.
        //
        // Gear already strapped to the mat or the wings rides round with them and is behind the
        // board until the leaf comes back down. That is deliberate rather than tolerated: it is
        // what a loaded flap does, the rectangles the layout reserved are unchanged because they
        // turned WITH their surfaces, and the alternatives are all worse — refusing to raise a
        // loaded leaf makes the feature unusable exactly when it is wanted, and sweeping the gear
        // somewhere else means an automatic rearrangement the player never asked for and cannot
        // undo.

        /// <summary>
        /// Ask for the leaf to go up or come down. <b>This is the way in from gameplay.</b>
        ///
        /// <para>
        /// Routed through <see cref="RackRequested"/> when something is listening, which in
        /// practice is <see cref="BackpackNetwork"/>: which way a shared container's members are
        /// folded is state two players can disagree about, so it is replicated rather than done
        /// locally. With nothing listening — single player on a body with no NetworkObject, an
        /// EditMode test — it happens here, which is the same degradation every unrelayed message
        /// in this project takes.
        /// </para>
        /// </summary>
        public void RequestRack(bool up)
        {
            // Nothing to rack on a pack that is shut or on somebody's back. Checked here rather
            // than at the call site so the key, the wire and a test all get the same answer.
            if (!IsOpen || IsWorn) return;
            if (IsRacked == up) return;

            if (RackRequested != null)
            {
                RackRequested(up);
                return;
            }

            SetRacked(up);
        }

        /// <summary>
        /// Raised when somebody asks for the rack. A listener owns the decision and is expected to
        /// call <see cref="SetRacked"/> on every machine once it has been made.
        /// </summary>
        public event System.Action<bool> RackRequested;

        /// <summary>
        /// Put the leaf up or down, with the swing. <b>Presentation</b> — call it on every machine,
        /// not just the one that asked.
        /// </summary>
        public void SetRacked(bool up)
        {
            if (IsRacked == up) return;

            IsRacked = up;

            if (rackRoutine != null) StopCoroutine(rackRoutine);
            rackRoutine = null;

            // A pack that is not running — a prefab being wired, an EditMode test, an object that
            // has been deactivated mid-swing — cannot start a coroutine, and StartCoroutine throws
            // rather than returning null. It gets the settled pose instead of the animation.
            if (!isActiveAndEnabled)
            {
                SnapRack(up);
                return;
            }

            rackRoutine = StartCoroutine(SwingRack());
        }

        // ── Dragging the leaf by hand ────────────────────────────────────────
        //
        // The rack is also a GESTURE: grab the board's free edge in focus mode and pull it through
        // its arc. That needs the leaf driven from the cursor rather than from a clock, which is
        // what the three members below are for — and they are deliberately the ONLY way in that
        // does not touch IsRacked. A drag is a preview on the dragging player's own screen and
        // nothing else; the commit at the end of it goes through RequestRack like the R key, so the
        // NetworkVariable on BackpackNetwork is still what every machine (including this one)
        // learns the answer from.

        /// <summary>
        /// How far up its arc the leaf is standing right now, 0 flat and 1 racked.
        ///
        /// <para>
        /// The EASED value — the fraction of the turn the hinge has actually been given, not the
        /// clock behind it. That is what a drag needs: it is matching a screen distance to a
        /// position on an arc, and the clock is a parametrisation of that arc rather than the arc.
        /// </para>
        /// </summary>
        public float RackProgress => RackEase(rackClock);

        /// <summary>
        /// Put the leaf at a point on its arc directly, with no swing and no change of state.
        ///
        /// <para>
        /// <b>Presentation, and local.</b> Nothing here is replicated and nothing here is
        /// remembered: <see cref="IsRacked"/> is untouched, so a drag abandoned halfway — the
        /// player lets go short of the commit, leaves focus mode, is disconnected — is undone by
        /// <see cref="SettleRack"/> putting the leaf back where the state says it should be.
        /// </para>
        /// </summary>
        /// <param name="progress">0 flat, 1 racked. Clamped.</param>
        public void ScrubRack(float progress)
        {
            // The same two conditions RequestRack refuses on, and for the same reason: a pack that
            // is shut or on a back has no leaf to turn, and the sheet owns the hinge there.
            if (!IsOpen || IsWorn) return;

            if (rackRoutine != null) StopCoroutine(rackRoutine);
            rackRoutine = null;

            // Stored UN-eased, so LeafFromOpen's ease undoes this exactly and the leaf ends up at
            // the angle the caller asked for. Without the inversion the hinge would sit up to 9.6%
            // of its arc — about 8.7 degrees — away from the cursor that is supposedly holding it,
            // which is the whole difference between dragging a thing and driving a slider.
            rackClock = RackUnEase(progress);

            ApplySheet(sheetClock);
        }

        /// <summary>
        /// Let a scrub go: swing the leaf from wherever the drag left it back to whatever
        /// <see cref="IsRacked"/> says it should be.
        ///
        /// <para>
        /// Called on EVERY release, including the one that committed. A commit that took —
        /// <see cref="RequestRack"/> came back through <see cref="SetRacked"/> with the new value,
        /// which is synchronous in single player and one owner-write later on a wire — finds
        /// <see cref="IsRacked"/> already agreeing and simply finishes the last few degrees. A
        /// commit that was refused, lost, or never made finds it disagreeing and springs the leaf
        /// back. One call covers both because the target is read from the state rather than from
        /// what the drag was hoping for.
        /// </para>
        /// </summary>
        public void SettleRack()
        {
            float target = IsRacked ? 1f : 0f;
            if (Mathf.Approximately(rackClock, target)) return;

            if (rackRoutine != null) StopCoroutine(rackRoutine);
            rackRoutine = null;

            if (!isActiveAndEnabled)
            {
                rackClock = target;
                ApplySheet(sheetClock);
                return;
            }

            rackRoutine = StartCoroutine(SwingRack());
        }

        /// <summary>
        /// The leaf hinge as a line in the world, and the signed degrees about it that take the
        /// leaf from lying flat to standing as a rack.
        ///
        /// <para>
        /// This is what lets a drag work out where a grabbed point on the board will BE at any
        /// point in the swing, without knowing anything about hinges. False when the rig has no
        /// leaf, which is the older clamshell and any pack with no rack to speak of.
        /// </para>
        /// <para>
        /// The axis is read off the live pivot rather than off the captured rest pose, and that is
        /// safe for the reason it is not obvious: <see cref="ApplySheet"/> POST-multiplies the turn
        /// onto the rest rotation, so the rotation's axis is fixed in the pivot's own rest frame —
        /// and a rotation never moves its own axis. <c>pivot.rotation * localAxis</c> therefore
        /// answers the same world vector at every point in the swing, which is exactly the property
        /// a drag needs while the thing is moving.
        /// </para>
        /// </summary>
        public bool TryGetLeafHinge(out Vector3 origin, out Vector3 axis, out float rackDegrees)
        {
            origin = Vector3.zero;
            axis = Vector3.right;
            rackDegrees = 0f;

            int count = hinges != null ? hinges.Length : 0;

            for (int i = 0; i < count; i++)
            {
                if (hinges[i].part != BackpackHingePart.Leaf || hinges[i].pivot == null) continue;

                Vector3 local = hinges[i].localAxis.sqrMagnitude > 1e-6f
                    ? hinges[i].localAxis.normalized
                    : Vector3.right;

                origin = hinges[i].pivot.position;
                axis = hinges[i].pivot.rotation * local;

                // Mirrors HingeOffset's `fromRest = restIsOpen ? fromOpen : fold - fromOpen`. Flat
                // is fromOpen 0 and racked is fromOpen == foldAngle, so the travel between them is
                // +foldAngle on a model authored open and -foldAngle on one authored closed. Both
                // signs are real — expedition_rig is authored open, expedition_backpack is not.
                rackDegrees = hinges[i].restIsOpen ? hinges[i].foldAngle : -hinges[i].foldAngle;
                return true;
            }

            return false;
        }

        /// <summary>The ease the rack's clock is read through. One symmetric smoothstep, both ways.</summary>
        private static float RackEase(float clock) => clock * clock * (3f - 2f * clock);

        /// <summary>
        /// The clock whose <see cref="RackEase"/> is <paramref name="eased"/>.
        ///
        /// <para>
        /// Smoothstep is a depressed cubic, so its inverse is the trigonometric root formula rather
        /// than an approximation: <c>3c² - 2c³ = e</c> solves to <c>c = ½ - sin(asin(1 - 2e) / 3)</c>
        /// on [0,1]. Exact at 0, ½ and 1, which is what keeps a drag from nudging a settled leaf.
        /// </para>
        /// </summary>
        private static float RackUnEase(float eased)
        {
            eased = Mathf.Clamp01(eased);

            return 0.5f - Mathf.Sin(Mathf.Asin(1f - 2f * eased) / 3f);
        }

        /// <summary>The rack where it is asked for, with no swing and no coroutine.</summary>
        private void SnapRack(bool up)
        {
            if (rackRoutine != null) StopCoroutine(rackRoutine);
            rackRoutine = null;

            IsRacked = up;
            rackClock = up ? 1f : 0f;

            ApplySheet(sheetClock);
        }

        private IEnumerator SwingRack()
        {
            float target = IsRacked ? 1f : 0f;
            float speed = 1f / Mathf.Max(0.05f, rackSeconds);

            while (!Mathf.Approximately(rackClock, target))
            {
                rackClock = Mathf.MoveTowards(rackClock, target, speed * Time.deltaTime);

                // The whole sheet, not just this hinge: the leaf's angle is a function of both
                // clocks, and re-deriving all of them from the two is cheaper than remembering
                // which of them the rack is allowed to touch.
                ApplySheet(sheetClock);
                yield return null;
            }

            rackClock = target;
            ApplySheet(sheetClock);

            rackRoutine = null;
        }

        private void CaptureRestPose()
        {
            restRotations = new Quaternion[hinges != null ? hinges.Length : 0];

            for (int i = 0; i < restRotations.Length; i++)
                restRotations[i] = hinges[i].pivot != null
                    ? hinges[i].pivot.localRotation
                    : Quaternion.identity;

            int stakeCount = stakes != null ? stakes.Length : 0;
            stakeRest = new Vector3[stakeCount];
            stakeOffset = new Vector3[stakeCount];

            for (int i = 0; i < stakeCount; i++)
            {
                if (stakes[i] == null) continue;

                stakeRest[i] = stakes[i].localPosition;

                // Through InverseTransformVector rather than as a bare local offset, so the lift
                // is stakeLift METRES whatever scale the FBX arrived on. The pack's own art comes
                // in on the centimetre convention — mesh data 100x small under transforms 100x
                // large — and a raw 0.14 in local units would be 14 m of lift on it.
                Transform parent = stakes[i].parent != null ? stakes[i].parent : transform;
                stakeOffset[i] = parent.InverseTransformVector(transform.up * stakeLift);
            }
        }

        // ------------------------------------------------------------------ the unfold
        //
        // Spec 3.4's beat sheet, in the sheet's own seconds. The clock starts when the pack leaves
        // the player's back, so beat 1 — the arc down, 0.00 to 0.35 — belongs to BackpackDeployArc
        // and the controller; this object is handed the rig at SheetLanded and runs the rest.
        //
        //   0.30-0.55  kickstands snap out, panel tips to 65 deg   (they ride PIVOT_Back)
        //   0.45-0.85  leaf FALLS forward — stakes and rail riding it — 8 deg overshoot
        //   0.60-0.96  left side panel folds down flat off the board, 90 deg about its edge
        //   0.64-1.00  right side panel, 40 ms behind it
        //   0.68-1.04  lid apron unfolds past the board's leading edge, 40 ms behind that
        //   0.90-1.20  stakes drop, cords go taut
        //   1.00-1.40  holders pop in, staggered outward from the tank, 0.12 s each
        //
        // Run backwards for the stow, this is a box being closed: the lid stands up off the
        // board's leading edge first, the side panels follow it up square off the board while the
        // flap is already rising, and as the flap lands against the pack the sides come round to
        // hug its flanks and the lid rides over the top to cap it. The wing and lid pivots are
        // CHILDREN of PIVOT_Leaf, so their folds are relative to the board and they ride it the
        // rest of the way.
        //
        // Three of those carry the feel and none of them is an ease:
        //
        //   * The leaf FALLS. Cloth does not lerp — it accelerates under gravity, hits the ground
        //     with the speed that accumulated on the way down, and rebounds. Slerping it to the
        //     open pose on a smoothstep, which is what every other hinge here does, makes the one
        //     soft part of the rig read as the stiffest.
        //   * The side panels are staggered 40 ms. Simultaneous motion reads as machinery and
        //     offset motion reads as physical, and 40 ms is the whole of that difference.
        //   * The holders populate OUTWARD FROM THE TANK over the last 0.4 s, which is what turns
        //     "the pack opened" into "my gear is here". The tank is the rig's fixed landmark, so
        //     it is where the player's eye already is.

        /// <summary>The sheet reading at which the pack has landed and this object takes over.</summary>
        private const float SheetLanded = 0.30f;

        /// <summary>The sheet reading at which everything has finished.</summary>
        private const float SheetDone = 1.40f;

        private const float PanelFrom = 0.30f, PanelTo = 0.55f;
        private const float LeafFrom = 0.45f, LeafTo = 0.85f;
        private const float WingFrom = 0.60f, WingSpan = 0.36f, WingStagger = 0.04f;
        private const float StakeFrom = 0.90f, StakeTo = 1.20f;
        private const float HolderFrom = 1.00f, HolderTo = 1.40f, HolderPopSeconds = 0.12f;

        /// <summary>Degrees the leaf carries past flat before it settles.</summary>
        private const float LeafOvershoot = 8f;

        /// <summary>
        /// Fraction of the leaf's window spent falling. The rest is the rebound, which is shorter
        /// than the fall for the same reason a dropped blanket's is: most of the energy went into
        /// the mat.
        /// </summary>
        private const float LeafFallFraction = 0.55f;

        /// <summary>
        /// The end of the sheet for THIS rig. A hinge with no named part is not on the sheet at
        /// all and swings over <see cref="openSeconds"/> instead, which is what keeps the older
        /// clamshell and <c>ExpeditionBackpack</c> opening exactly as they were tuned to.
        /// </summary>
        private float SheetEnd => Mathf.Max(SheetDone, SheetLanded + openSeconds);

        /// <summary>
        /// Walk the sheet clock to whichever end the pack is headed for, at real time.
        ///
        /// The clock is not restarted, so a press that reverses a half-finished unfold turns the
        /// rig round from exactly where it is.
        /// </summary>
        private IEnumerator Unfold(bool open)
        {
            float target = open ? SheetEnd : SheetLanded;

            while (!Mathf.Approximately(sheetClock, target))
            {
                sheetClock = Mathf.MoveTowards(sheetClock, target, Time.deltaTime);
                ApplySheet(sheetClock);
                yield return null;
            }

            sheetClock = target;
            ApplySheet(sheetClock);

            doorRoutine = null;

            // The fold has landed, so the rack — if there was one — is now standing at exactly the
            // angle the sheet is asking for anyway. See ResolveRackForStow: this is the instant it
            // is free to give up, and giving it up here is what makes a pack stowed from the rack
            // and a pack stowed flat the same pack.
            if (!open) ResolveRackForStow();
        }

        /// <summary>Put every moving part where the sheet says it is at <paramref name="sheet"/>.</summary>
        private void ApplySheet(float sheet)
        {
            int count = hinges != null ? hinges.Length : 0;

            // Captured on first use rather than trusted to have been captured in Awake, for the
            // reason the Layout property gives: Awake is not a moment you can count on. The editor
            // never runs it on a component added outside play mode, so an EditMode test or an
            // inspector tool driving the sheet would otherwise leave every hinge untouched and
            // silently report a rig that folded.
            if (restRotations == null || restRotations.Length != count) CaptureRestPose();

            for (int i = 0; i < count; i++)
            {
                Transform pivot = hinges[i].pivot;
                if (pivot == null || restRotations == null || i >= restRotations.Length) continue;

                pivot.localRotation = restRotations[i] * HingeOffset(hinges[i], sheet);
            }

            ApplyStakes(sheet);
            ApplyHolderPop(sheet);
        }

        /// <summary>
        /// One hinge's turn away from its authored rest rotation at this point on the sheet.
        ///
        /// <para>
        /// Everything is expressed as an angle about the hinge's own axis rather than as a Slerp
        /// between two poses, because the leaf's motion is not a path between them: it overshoots,
        /// which means it leaves the segment those two poses define. Once one part needs an angle
        /// the rest may as well use the same parametrisation.
        /// </para>
        /// </summary>
        private Quaternion HingeOffset(in BackpackHinge hinge, float sheet)
        {
            Window(hinge.part, out float from, out float to);

            float p = to > from ? Mathf.Clamp01((sheet - from) / (to - from)) : (sheet >= to ? 1f : 0f);

            // Measured from the DEPLOYED pose: hinge.foldAngle when stowed, zero when open, and a
            // little past zero mid-rebound. Which way round that has to be applied to the model's
            // rest rotation is the one thing restIsOpen decides.
            //
            // The leaf, the two side panels and the lid are the parts the rack also moves, so
            // each of them reconciles two demands on one hinge instead of reading the sheet
            // straight.
            float fromOpen;

            switch (hinge.part)
            {
                case BackpackHingePart.Leaf:
                    fromOpen = LeafFromOpen(p, hinge.foldAngle);
                    break;

                case BackpackHingePart.WingLeft:
                case BackpackHingePart.WingRight:
                case BackpackHingePart.Lid:
                    fromOpen = FlapFromOpen(hinge.part, p, hinge.foldAngle);
                    break;

                default:
                    fromOpen = hinge.foldAngle * (1f - Ease(hinge.part, p));
                    break;
            }

            float fromRest = hinge.restIsOpen ? fromOpen : hinge.foldAngle - fromOpen;

            Vector3 axis = hinge.localAxis.sqrMagnitude > 1e-6f
                ? hinge.localAxis.normalized
                : Vector3.right;

            return Quaternion.AngleAxis(fromRest, axis);
        }

        private void Window(BackpackHingePart part, out float from, out float to)
        {
            switch (part)
            {
                case BackpackHingePart.Panel:
                    from = PanelFrom; to = PanelTo; break;
                case BackpackHingePart.Leaf:
                    from = LeafFrom; to = LeafTo; break;
                case BackpackHingePart.WingLeft:
                case BackpackHingePart.WingRight:
                case BackpackHingePart.Lid:
                    // One window per slot in the flap chain, each 40 ms behind the one before.
                    // The stagger is not a rounding of "all at the same time" — it is the
                    // difference between a machine and a thing made of cloth and tube — and
                    // reading the slot off FlapStep is what makes the order STRICT: the lid sits
                    // past both wings, so the stow can never interleave it between them, however
                    // the tunables move.
                    from = WingFrom + FlapStep(part) * WingStagger;
                    to = from + WingSpan;
                    break;
                default:
                    from = SheetLanded; to = SheetLanded + openSeconds; break;
            }
        }

        private static float Ease(BackpackHingePart part, float p)
        {
            switch (part)
            {
                // Snap. The kickstands are steel legs going over centre, so the motion is almost
                // all in the first third of the window and then it is simply there.
                case BackpackHingePart.Panel:
                {
                    float u = 1f - p;
                    return 1f - u * u * u * u;
                }

                // Ribbed panels and the lid's quilted apron flipping over on a hinge: quick,
                // then set down.
                case BackpackHingePart.WingLeft:
                case BackpackHingePart.WingRight:
                case BackpackHingePart.Lid:
                {
                    float u = 1f - p;
                    return 1f - u * u * u;
                }

                // The shared smoothstep every pack before the beat sheet opened on.
                default:
                    return p * p * (3f - 2f * p);
            }
        }

        /// <summary>
        /// The leaf's angle from the deployed pose: a fall under constant angular acceleration,
        /// then a damped rebound <see cref="LeafOvershoot"/> degrees past flat.
        ///
        /// <para>
        /// <c>fold * (1 - u²)</c> is a body released from rest — it starts barely moving and
        /// arrives at its fastest, which is the difference a viewer reads as weight. A smoothstep
        /// arrives at zero speed and reads as a hinge being closed by hand.
        /// </para>
        /// </summary>
        private static float LeafAngle(float p, float fold)
        {
            if (p <= 0f) return fold;
            if (p >= 1f) return 0f;

            if (p < LeafFallFraction)
            {
                float u = p / LeafFallFraction;
                return fold * (1f - u * u);
            }

            float v = (p - LeafFallFraction) / (1f - LeafFallFraction);

            // Past flat, in the direction the leaf was already travelling — the fall runs fold
            // toward zero, so a negative fold is moving positive when it lands.
            return -Mathf.Sign(fold) * LeafOvershoot * Rebound(v);
        }

        /// <summary>
        /// The leaf's angle from the deployed pose, given BOTH the things that can turn it: the
        /// unfold beat sheet, and the rack.
        ///
        /// <para>
        /// They share one hinge, so they cannot both be applied. Whichever is further from the open
        /// pose wins, which is exact rather than a compromise because the two are measured from the
        /// same pose about the same axis with the same sign — the rack angle and the stow angle are
        /// literally the same number. That one rule covers every case worth naming:
        /// </para>
        /// <list type="bullet">
        /// <item>rack down, pack opening or closing — the sheet is bigger, and the unfold is
        /// exactly what it was before the rack existed;</item>
        /// <item>rack up, pack sitting open — the sheet is at zero, so the rack has the hinge;</item>
        /// <item>rack up and the player re-shoulders — the leaf is already where stowing wants it,
        /// so it simply stays there while the panel folds around it, instead of dropping flat and
        /// being picked straight back up;</item>
        /// <item>the leaf's rebound, which overshoots a few degrees the OTHER way — smaller in
        /// magnitude than any raised rack, so it survives untouched while the rack is down and is
        /// correctly suppressed while it is up.</item>
        /// </list>
        /// </summary>
        private float LeafFromOpen(float p, float fold)
        {
            float sheet = LeafAngle(p, fold);

            // One symmetric ease, both ways, and not the fall the deploy uses. The rack is
            // reversible mid-swing — the player can change their mind halfway — and two different
            // curves read through one clock do not meet at the point of reversal, so the leaf would
            // jump the instant the key was pressed again.
            //
            // A drag scrubs this by asking ScrubRack for a point on the arc, which stores the clock
            // whose ease is that point. RackEase and RackUnEase are therefore one pair and have to
            // stay one: change the curve here and the drag stops tracking the cursor.
            float rack = fold * RackEase(rackClock);

            return Mathf.Abs(rack) > Mathf.Abs(sheet) ? rack : sheet;
        }

        /// <summary>
        /// One board-riding flap's angle from the deployed pose — a side panel or the lid — given
        /// BOTH the things that can turn it: the unfold beat sheet, and the rack. The flap half of
        /// <see cref="LeafFromOpen"/>, and the same rule for the same reason — whichever demand is
        /// further from the open pose wins.
        ///
        /// <para>
        /// <b>The flaps fold UP off the board and ride it round the pack.</b> Their pivots are
        /// children of <c>PIVOT_Leaf</c>, so the 90° here is measured against the board: folded,
        /// each side panel stands perpendicular to it like the raised side of a tray and the lid
        /// stands up off its leading edge as the end wall — and because the board itself turns
        /// X -90 to the pack, the raised sides arrive wrapped round the pack's flanks and the lid
        /// arrives flat on top, capping the box. Closing is therefore two visible motions of one
        /// connected thing: the flaps fold up, the closing board carries them round the body. On
        /// the rack the very same relative fold is what turns the lid into a hood over the raised
        /// board's top edge instead of a flat apron prolonging it.
        /// </para>
        /// <para>
        /// <b>The racked angle is <see cref="BackpackHinge.foldAngle"/> itself, not a number of
        /// its own</b>, and that is load-bearing rather than a convenience. It is the same trick
        /// the leaf plays: because racked and stowed are the same place for the flaps too,
        /// <see cref="ResolveRackForStow"/> still costs exactly zero motion on all four parts, a
        /// pack stowed from the rack lands in the same pose as one stowed flat, and nothing has
        /// to unwind on the way to being shouldered.
        /// </para>
        /// </summary>
        private float FlapFromOpen(BackpackHingePart part, float p, float fold)
        {
            float sheet = fold * (1f - Ease(part, p));

            float rack = fold * RackEase(RackClockFor(part));

            return Mathf.Abs(rack) > Mathf.Abs(sheet) ? rack : sheet;
        }

        /// <summary>
        /// The rack's clock as ONE board-riding flap reads it: each slot in the chain
        /// <see cref="WingStagger"/> behind the one before, the same 40 ms the deploy and the
        /// stow already stagger them by and for the same reason — simultaneous motion reads as
        /// machinery.
        ///
        /// <para>
        /// Expressed as a lag in CLOCK rather than in seconds so the whole raise still takes
        /// <see cref="rackSeconds"/>: a lagging flap starts late and a leading one finishes
        /// early, by the same fraction, and every slot is exact at 0 and at 1. Exactness at the
        /// ends is what keeps <see cref="ResolveRackForStow"/> free — a flap sitting at 0.998 of
        /// its flip when the rack is given up would twitch.
        /// </para>
        /// <para>
        /// A pure function of the clock, deliberately, and not of which way the rack is
        /// travelling. The rack is reversible mid-swing and two direction-dependent curves read
        /// through one clock do not meet at the point of reversal. The visible consequence is
        /// that the flap that folded in last flips out first, which is what a row of hinged
        /// flaps does anyway.
        /// </para>
        /// </summary>
        private float RackClockFor(BackpackHingePart part)
        {
            // Half the clock is the hard ceiling for the WHOLE chain: rackSeconds can be tuned
            // down to 0.05, at which point two 40 ms lags are most of the swing and an unclamped
            // chain would leave the leading wing snapping through its whole arc in the first few
            // milliseconds.
            float lag = Mathf.Clamp(WingStagger / Mathf.Max(0.05f, rackSeconds),
                                    0f, 0.5f / LastFlapStep);

            float shifted = rackClock - FlapStep(part) * lag;

            return Mathf.Clamp01(shifted / (1f - LastFlapStep * lag));
        }

        /// <summary>
        /// Where a flap stands in the stagger chain the board's riders fold on: the left wing
        /// leads, the right wing follows, the lid brings up the rear. One list, read by the beat
        /// sheet's windows and the rack's clock alike, so the two choreographies can never order
        /// the flaps differently.
        /// </summary>
        private static int FlapStep(BackpackHingePart part) => part switch
        {
            BackpackHingePart.WingRight => 1,
            BackpackHingePart.Lid => LastFlapStep,
            _ => 0,
        };

        /// <summary>The last slot in the flap stagger chain — the lid's.</summary>
        private const int LastFlapStep = 2;

        /// <summary>
        /// A damped bounce over <c>v</c> in [0,1]: one hump to +1, a shallow dip past it, and
        /// exactly zero at both ends so the leaf leaves the beat sitting flat rather than a
        /// fraction of a degree out.
        /// </summary>
        private static float Rebound(float v)
        {
            // sin(2*pi*v) * e^(-4v) peaks at 0.4449 near v = 0.16; dividing by that makes the
            // first peak exactly LeafOvershoot degrees rather than "some fraction of it".
            const float Peak = 0.4449f;

            return Mathf.Sin(2f * Mathf.PI * v) * Mathf.Exp(-4f * v) / Peak;
        }

        /// <summary>
        /// Beat 5. The stakes fall rather than slide: <c>u²</c>, same reason as the leaf.
        /// </summary>
        private void ApplyStakes(float sheet)
        {
            if (stakes == null || stakeRest == null) return;

            float p = Mathf.Clamp01((sheet - StakeFrom) / (StakeTo - StakeFrom));
            float dropped = p * p;

            for (int i = 0; i < stakes.Length && i < stakeRest.Length; i++)
            {
                if (stakes[i] == null) continue;

                stakes[i].localPosition = stakeRest[i] + stakeOffset[i] * (1f - dropped);
            }
        }

        /// <summary>
        /// Beat 6. Each holder scales from nothing to the size <see cref="HolderBuilder"/> fitted
        /// it to, over <see cref="HolderPopSeconds"/>, in order of distance from the tank.
        ///
        /// <para>
        /// The stagger is the point of the beat. All of them at once is a pack that finished
        /// loading; one after another outward from the landmark is gear being laid out.
        /// </para>
        /// </summary>
        private void ApplyHolderPop(float sheet)
        {
            if (holderPop.Count == 0) return;

            // The last holder must still have its whole 0.12 s inside the beat, so the starts are
            // spread over the window MINUS one pop rather than over the whole of it.
            float spread = Mathf.Max(0f, (HolderTo - HolderPopSeconds) - HolderFrom);
            float step = holderPop.Count > 1 ? spread / (holderPop.Count - 1) : 0f;

            for (int i = 0; i < holderPop.Count; i++)
            {
                (Transform holder, Vector3 scale) = holderPop[i];
                if (holder == null) continue;

                float start = HolderFrom + step * i;
                float p = Mathf.Clamp01((sheet - start) / HolderPopSeconds);

                // Overshooting the scale was tried and read as a cartoon squash on hardware that
                // is meant to be webbing and steel. A plain ease-out is enough at 0.12 s.
                float u = 1f - p;
                holder.localScale = scale * (1f - u * u * u);
            }
        }

        /// <summary>
        /// Re-order and re-size the holders to wherever the unfold currently stands.
        ///
        /// Run after every display rebuild, because a holder built mid-unfold — or while the pack
        /// is stowed — would otherwise sit at full size through a beat that is supposed to be
        /// bringing it in. This is the rig's whole share of the shared rebuild.
        /// </summary>
        protected override void OnVisualsRebuilt()
        {
            CollectHolderPop();
            ApplyHolderPop(sheetClock);
        }

        /// <summary>
        /// Order the live holders for the pop and remember the size each was fitted to.
        ///
        /// Called after every display rebuild, because a holder built while the pack is stowed —
        /// a save restoring into a worn pack, an item stowed by a world pickup — must not be
        /// sitting at full size waiting for a beat that already played.
        /// </summary>
        private void CollectHolderPop()
        {
            holderPop.Clear();

            Vector3 origin = holderOrigin != null ? holderOrigin.position : transform.position;

            // Walked through the placements rather than straight down holderVisuals, because which
            // FACE a holder is on decides whether it is on the beat sheet at all.
            foreach (PackPlacement placement in Layout.Placements)
            {
                if (!holderVisuals.TryGetValue(placement.ItemId, out GameObject holder)) continue;
                if (holder == null) continue;

                // Exterior gear is never "laid out". It is lashed to the outside of the pack and it
                // is there whether the rig is open on the sand or folded on somebody's back — so
                // popping its straps in on beat 6 would mean they vanished the instant the pack
                // closed, which is the exact opposite of what an exterior face is for. Left off the
                // list, HolderBuilder's own fitted scale stands and nothing ever touches it.
                if (IsExteriorWhenStowed(placement.Surface)) continue;

                holderPop.Add((holder.transform, holder.transform.localScale));
            }

            holderPop.Sort((a, b) =>
            {
                float da = a.holder != null ? (a.holder.position - origin).sqrMagnitude : float.MaxValue;
                float db = b.holder != null ? (b.holder.position - origin).sqrMagnitude : float.MaxValue;
                return da.CompareTo(db);
            });
        }

        // ------------------------------------------------------------------ contents

        /// <summary>
        /// Can a player put something on this face right now?
        ///
        /// <para>
        /// Five of the rig's seven faces ride the leaf, and the leaf has two positions, so at any
        /// moment one set of them is against the sand. Down, the rack is the underside of a mat
        /// lying on the ground; up, the mat, the wings and the lash line have swung round behind
        /// the board. Neither is somewhere a player can reach or even see, and first-fit would
        /// happily use them — the rack sits after the board in the surface list, so an overflowing
        /// world pickup would slide an item under the mat with nothing at all to say where it went.
        /// </para>
        /// <para>
        /// <b>This gates player actions only, never a restore.</b> An explicit placement that names
        /// its surface — a save being read back, a client adopting the server's list — lands
        /// wherever it says, because the item really is there and refusing it would either lose the
        /// gear or silently move it. Something saved on the rack comes back on the rack, out of
        /// reach until the leaf goes up again, which is exactly where the player left it.
        /// </para>
        /// <para>
        /// <b>The shipped rig wires all seven faces</b> (2026-08-25 — an earlier pass wired only
        /// the board, and the unwired flanks read as broken rather than forbidden: the placement
        /// outline just died over them). The wing ids share the leaf's rule below because they
        /// ride <c>PIVOT_Leaf</c>: a racked wing has swung round behind the board, where the
        /// player can neither see nor reach it. The back panels are the open rig's fixed spine,
        /// so they take the default and are reachable whenever the pack is deployed.
        /// </para>
        /// </summary>
        public override bool Reaches(PackSurfaceId id)
        {
            // Worn, the rig is not a set of seven faces at all: it is a folded sandwich against
            // somebody's back, and six of the seven are inside it. Only the face the fold leaves
            // pointing out at the world can take anything — everything else would put gear where
            // it is invisible and where the leaf closes straight through it. This is the branch
            // that makes a world pickup overflowing into a WORN pack land on the outside of it,
            // which is the only place on a worn pack that anything can be.
            if (IsWorn) return IsExteriorWhenStowed(id);

            switch (id)
            {
                case PackSurfaceId.Rack:
                    return IsRacked;

                // All four ride PIVOT_Leaf and all four face the back panel once it is standing.
                case PackSurfaceId.Leaf:
                case PackSurfaceId.LongGoods:
                case PackSurfaceId.WingLeft:
                case PackSurfaceId.WingRight:
                    return !IsRacked;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Is this face on the OUTSIDE of the folded pack — carried on the wearer's back, in the
        /// open air, where gear on it is both usable and the thing anybody looking at them sees?
        ///
        /// <para>
        /// <b>Exactly one face qualifies, and it is a reading of the fold rather than a choice.</b>
        /// <c>PIVOT_Leaf</c>'s stow travel and its rack travel are the same X -90 — that is why the
        /// rack needed no hinge of its own — so a stowed leaf comes to rest against the back panel
        /// with its mat facing the panel and its UNDERSIDE, which is <see cref="PackSurfaceId.Rack"/>,
        /// facing away from it. The harness is sewn to the panel's other face, so that underside is
        /// the side pointing away from the wearer. Every other face is inside the sandwich: the two
        /// back panels are covered by the leaf, <see cref="PackSurfaceId.Leaf"/> and the lash
        /// line have turned to face the panel, and the side panels have folded round to hug the
        /// pack's flanks. Every one of them is inside the fold or pressed against the body.
        /// </para>
        /// <para>
        /// Which also settles a question that looks like it needs new geometry and does not. The
        /// fold is a clamshell: every face on the rig is exposed in exactly ONE of the two
        /// configurations, so there is no face that is usable both flat on the sand and folded on a
        /// back. The rack is the exception only because the pose it is usable in and the stowed
        /// pose are the same pose.
        /// </para>
        /// </summary>
        public static bool IsExteriorWhenStowed(PackSurfaceId id) => id == PackSurfaceId.Rack;

        // ------------------------------------------------------------------ taking

        /// <summary>
        /// Ask for whatever is at a point on one of the pack's faces. <b>The taker's own machine
        /// must go through here, not through <see cref="TryTakeToHotbar"/>.</b>
        ///
        /// <para>
        /// Two players can be looking into the same open pack, so which of them gets the last thing
        /// in it is the server's to decide — the same rule that puts a trade on the trader's
        /// channel rather than the buyer's. Routed through the wearer, who owns the channel this
        /// pack has to borrow.
        /// </para>
        /// </summary>
        public override void RequestTake(PackSurfaceId surface, Vector2 uv, Interactor interactor)
        {
            if (interactor == null) return;

            if (owner != null)
            {
                owner.RequestTake(surface, uv, interactor);
                return;
            }

            // A pack nobody owns has no channel to ask on, so it falls back to doing the transfer
            // here — single-player-style, which is the same degradation every unrelayed message in
            // this project takes. Unreachable today: every pack is bound to a wearer in
            // BackpackController.Awake and destroyed with them.
            IPlayerInventory hotbar = interactor.GetComponentInParent<IPlayerInventory>();
            if (hotbar != null) TryTakeToHotbar(surface, uv, hotbar);
        }

        /// <summary>
        /// The mirror of <see cref="RequestTake"/>: somebody wants one of their hotbar slots put on
        /// this pack. <b>The stower's own machine must go through here.</b>
        ///
        /// <para>
        /// A stow is contested in exactly the way a take is — the spot the cursor is over is space
        /// the other player looking into this pack may be about to fill — so it goes to the server
        /// on the pack owner's channel and nothing happens locally.
        /// </para>
        /// </summary>
        public override void RequestStow(int slotIndex, PackSurfaceId surfaceId, Vector2 uv, float yaw,
                                         Interactor interactor)
        {
            if (interactor == null) return;

            if (owner != null)
            {
                owner.RequestStow(slotIndex, surfaceId, uv, yaw, interactor);
                return;
            }

            // Same unowned-pack degradation RequestTake documents, and unreachable for the same
            // reason: every pack is bound to a wearer in BackpackController.Awake.
            IPlayerInventory hotbar = interactor.GetComponentInParent<IPlayerInventory>();
            if (hotbar != null) TryStowFromHotbar(hotbar, slotIndex, surfaceId, uv, yaw);
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

            // Aiming at the pack BODY: closed on the ground means open it, open means take it back.
            // Individual items are no longer reachable this way — BackpackSlotView is gone, and
            // picking one up is focus mode's job with a cursor. Keeping both would mean keeping two
            // request paths in sync for one action.
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
