// The vacuum canister.
//
// Hold Use on a creature, a mount or a loose prop and it is drawn, resisting, into the bottle;
// press Use with something inside and it comes back out. Almost none of that is this file's work.
// Containment (Assets/Game/Scripts/Gameplay/Containment) owns the captive as a saved record, the
// size gate, the rider rule, the struggle meter and the five-second ceiling on a bottled player.
// SupplyCharge owns the tank. What the canister owns is WHERE the beam points, WHEN the contest
// runs, and which of its two item identities the hotbar slot is holding.
//
// WHAT DOES NOT GO ON THE WIRE. Nothing of the canister's own. arg.A is the hotbar slot on the
// press and on every hold tick and belongs to the server's stale-slot guard; arg.B's low bit
// belongs to EquipmentController's active flag. The canister writes only the aim ray — origin in
// P, rotation in R — and every machine traces it for itself. The capture and the release announce
// themselves through Containment's own NetMsg.Contained / NetMsg.Released.
//
// WHAT IT DOES NOT DO. It does not move the captive. A "slide into the mouth" written as a
// transform on the body being drawn would be a server write to an owner-authoritative player, or a
// fight with the NavMeshAgent / LeggedLocomotion that already owns a creature's position — undone
// within a frame, silently, on the machine that matters. The draught draws the pull instead, and
// the fold-in is the moment the body actually leaves.
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay.Containment;

namespace SpaceGame.Items
{
    /// <summary>
    /// A wide-mouthed bottle that draws a living thing in and lets it back out.
    ///
    /// <para>
    /// <b>Authority is Server.</b> A capture despawns a networked entity and a release spawns one,
    /// which is exactly the shared world state that exactly one machine may decide
    /// (<c>GDC-L1-MP-0004</c>). The beam every other machine sees is drawn by
    /// <see cref="VacuumDraught"/> off the same ray, not by this.
    /// </para>
    /// <para>
    /// <b>Empty and full are two item identities</b>, and this class is on both prefabs. The slot's
    /// item follows the container's contents (<see cref="CanisterIdentity"/>), which is what makes
    /// a full canister read as full in somebody else's hands, on the ground and in the icon —
    /// <c>ItemState</c> does not replicate, but the hotbar's item id does. See
    /// <see cref="FollowContents"/>.
    /// </para>
    /// <para>
    /// <b>Why it HOLDS a <see cref="SupplyReservoir"/> rather than spending charges.</b> The
    /// canister runs out of pull, not of uses: a <c>maxUses</c> item is removed from the inventory
    /// when it empties, and an empty canister with a creature inside it must not be. A fraction on
    /// the item instance is also the only form that already survives an equip, a stow, a drop, a
    /// save and the wire — a client only receives a slot's charge byte for an item whose prefab
    /// <see cref="SupplyCharge.Carries"/> answers true for.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ContainerHold))]
    [RequireComponent(typeof(SupplyReservoir))]
    public sealed class VacuumCanisterArtifact : ToolItem
    {
        [Header("Suction")]
        [Tooltip("How far the draught reaches, in metres. Short on purpose: the canister is a " +
                 "thing you walk up to something with, and a long reach would make it a ranged " +
                 "delete button.")]
        [SerializeField, Min(1f)] private float range = 8f;

        [Tooltip("What the draught can take hold of. Triggers are always ignored — a mount's " +
                 "mount-me volume is an invitation, not a body.")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Tooltip("Where the draught leaves the canister. Falls back to the prefab root, which sits " +
                 "at the grip, so leaving this empty puts the mouth in the holder's fist.")]
        [SerializeField] private Transform mouth;

        [Header("Release")]
        [Tooltip("How far in front of the holder a captive is put back when the uncork hits " +
                 "nothing, in metres.")]
        [SerializeField, Min(0.5f)] private float releaseReach = 3f;

        [Tooltip("How far off the surface a captive is placed when the uncork lands on one, in " +
                 "metres. A body arrives at its authored scale and its collider is not measured " +
                 "here — the record has not been rebuilt yet — so this is the standoff that keeps " +
                 "an animal from being born inside the sand it was aimed at.")]
        [SerializeField, Min(0f)] private float releaseClearance = 0.6f;

        [Header("Identities")]
        [Tooltip("The item asset for an EMPTY canister. Both prefabs carry both, because either " +
                 "one may have to become the other.")]
        [SerializeField] private InventoryItem emptyIdentity;

        [Tooltip("The item asset for a FULL canister — the variant whose glass shows the captive.")]
        [SerializeField] private InventoryItem fullIdentity;

        [Tooltip("Seconds between the contents changing and the hotbar slot being swapped for the " +
                 "other canister, so the fold-in and the latch are seen.\n\n" +
                 "Not a safety margin: the swap re-equips the hand, and everything the capture " +
                 "just drew lives on the object that re-equip destroys. Long enough to read the " +
                 "moment, short enough that the glass is not visibly late. A swap missed because " +
                 "the player scrolled away inside it costs nothing — the record is in the slot's " +
                 "bag, and the next equip notices and swaps then.")]
        [SerializeField, Min(0f)] private float swapSeconds = 0.35f;

        [Header("Stream")]
        [Tooltip("Seconds of silence after which the canister shuts itself off. The safety net " +
                 "for a release that never arrived — a dropped packet, or a player who " +
                 "disconnected mid-draw. Must comfortably exceed EquipmentController's 0.2 s " +
                 "hold keepalive.")]
        [SerializeField] private float holdTimeout = 0.5f;

        [Header("Presentation")]
        [Tooltip("The canister's own moving parts and its loop. Found under this object when left empty.")]
        [SerializeField] private VacuumCanisterView view;

        [Tooltip("The draught drawn between the mouth and what it has hold of. Found under this " +
                 "object when left empty.")]
        [SerializeField] private VacuumDraught draught;

        [Tooltip("The line that tells the holder why a target is being refused. Found under this " +
                 "object when left empty; without one a refusal is silent, which reads as a " +
                 "broken item (GDC-L1-UX-0003).")]
        [SerializeField] private VacuumCanisterReadout readout;

        /// <summary>The captive, the record and everything that survives a save. Never null on a wired prefab.</summary>
        private ContainerHold hold;

        /// <summary>This canister's tank. Drained while the draught is on, refilled while it is not.</summary>
        private SupplyReservoir tank;

        /// <summary>Is the trigger down? Set from the hold stream, on every machine.</summary>
        private bool drawing;

        /// <summary>Is the tank actually delivering? <see cref="drawing"/> minus an empty bottle.</summary>
        private bool flowing;

        /// <summary>
        /// Set when a captive comes out, cleared when the trigger comes up.
        ///
        /// <para>
        /// The uncork is a PRESS and the draught is a HOLD, and the press that opens the bottle
        /// leaves the button down — so without this, letting a creature out and not instantly
        /// lifting the finger draws it straight back in, from a metre away, which reads as the item
        /// refusing to work rather than as a rule. Raised from <see cref="OnUncorked"/> so it is
        /// raised on every machine: the block has to hold on the peer drawing the beam as well as
        /// on the authority deciding the contest, or the two would disagree about whether anything
        /// is happening.
        /// </para>
        /// </summary>
        private bool blockedUntilRelease;

        /// <summary>When this machine last heard a hold tick. See <see cref="holdTimeout"/>.</summary>
        private float lastHoldTime;

        /// <summary>The aim ray as last reported. On the owner it is refreshed every frame.</summary>
        private Vector3 rayOrigin;
        private Vector3 rayDirection = Vector3.forward;

        /// <summary>What the draught has hold of right now, remembered across the fold-in.</summary>
        private GameObject watchedCaptive;

        /// <summary>The captor's pull, while this canister is bound to one. See <see cref="Attach"/>.</summary>
        private ContainmentPull watchedPull;

        /// <summary>
        /// Whether the container held a record the last time <see cref="FollowContents"/> looked,
        /// and when that answer last changed. Together they are the beat <see cref="swapSeconds"/>
        /// measures.
        /// </summary>
        private bool sawRecord;
        private float contentsChangedAt;

        /// <summary>
        /// The trace buffer. An instance field rather than a static one: two canisters are one
        /// hotbar scroll apart, and a shared buffer is a surprise waiting for the day somebody
        /// draws with a second one in a turret.
        /// </summary>
        private readonly RaycastHit[] hits = new RaycastHit[32];

        /// <summary>Capture and release are shared world state, so exactly one machine decides them.</summary>
        public override UseAuthority Authority => UseAuthority.Server;

        /// <summary>The draught runs for as long as the button is down. See <c>UsableItem</c>.</summary>
        public override bool IsContinuous => true;

        /// <summary>
        /// Nothing self-timed: the draught stops when the finger comes up.
        ///
        /// A dry tank deliberately does NOT end the hold. The player keeps holding, the suction
        /// stops, and the gauge up the side of the canister is what tells them why — the reading is
        /// on the object in their hand so the cost can be watched rather than guessed at
        /// (<c>GDC-L1-SYS-0006</c>).
        /// </summary>
        public override bool WantsHold => false;

        // ── Owner side: the aim ────────────────────────────────────────────────

        /// <summary>
        /// Owner-side, on the press: the ray the uncork is aimed down.
        ///
        /// Sent even though hold ticks carry the same thing, because the press is the whole of an
        /// uncork and the first hold tick is a frame away.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg) => WriteAim(ref arg);

        /// <summary>Owner-side, once per tick: where the mouth is pointed now.</summary>
        public override void OnRequestHold(ref NetArg arg, bool active)
        {
            if (active) WriteAim(ref arg);
        }

        /// <summary>
        /// The aim RAY, from the one machine that has a live camera.
        ///
        /// The ray rather than the point it lands on, so every machine traces its own and the beam
        /// a peer draws ends where the server is actually pulling. <c>AimProvider</c> rather than
        /// the mouth's forward, because mounted the eye is pitched with the seat and the view the
        /// player is aiming down is the mount's.
        /// </summary>
        private void WriteAim(ref NetArg arg)
        {
            AimProvider aim = aimProvider;
            if (aim == null || aim.AimTransform == null) return;

            Ray ray = aim.GetAimRay();
            arg.P = ray.origin;
            arg.R = Quaternion.LookRotation(ray.direction);
        }

        // ── The press: the uncork ──────────────────────────────────────────────

        /// <summary>
        /// Authority-side. A press on a full canister lets the captive out; on an empty one it does
        /// nothing at all, because the draught is the hold stream's job.
        ///
        /// <para>
        /// <b>Clearing and spawning are one step</b>, and that step is
        /// <see cref="ContainerHold.TryUncork"/>'s: it takes the record out before it builds the
        /// body, so a doubled input on the same frame finds an empty bottle rather than making a
        /// second creature.
        /// </para>
        /// <para>
        /// A bottled PLAYER cannot be uncorked and the press does nothing for them either. They are
        /// held rather than recorded, and their own clock is what lets them out — see
        /// <c>BottledPlayer</c>. Uncorking them early would be a courtesy the mechanic does not
        /// promise, and it would have to reach a body this machine does not own.
        /// </para>
        /// </summary>
        protected override void Use()
        {
            if (hold == null || !hold.IsFull) return;

            hold.TryUncork(ReleasePoint(UseArg), ReleaseRotation(UseArg));
        }

        /// <summary>
        /// Where the captive is put back: on the surface the holder aimed at, or in front of them
        /// when the aim reaches nothing.
        ///
        /// The trace skips the holder and the machine they are riding for
        /// <see cref="Trace"/>'s reason — without it an uncork at point blank puts the creature
        /// inside the person letting it out.
        /// </summary>
        private Vector3 ReleasePoint(NetArg arg)
        {
            Vector3 origin = arg.HasOrientation ? arg.P : MouthPoint();
            Vector3 direction = AimDirection(arg);

            return Trace(origin, direction, releaseReach, out RaycastHit hit)
                ? hit.point + hit.normal * releaseClearance
                : origin + direction * releaseReach;
        }

        /// <summary>
        /// Which way the captive faces: away from the holder, and level.
        ///
        /// Flattened deliberately. The record carries the pose the body went in with and
        /// <c>Captivity.Release</c> replaces it with this one, so a creature uncorked at the ground
        /// under the player's feet would otherwise come back standing on its nose.
        /// </summary>
        private Quaternion ReleaseRotation(NetArg arg)
        {
            Vector3 flat = Vector3.ProjectOnPlane(AimDirection(arg), Vector3.up);

            return flat.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(flat.normalized, Vector3.up)
                : Quaternion.identity;
        }

        private Vector3 AimDirection(NetArg arg) =>
            arg.HasOrientation ? arg.R * Vector3.forward : transform.forward;

        // ── The hold: the draught ──────────────────────────────────────────────

        /// <summary>
        /// Authority-side, once per tick. Records the aim and nothing else — the contest runs in
        /// <see cref="Update"/>, because it advances by real seconds and the ticks arrive at
        /// fifteen a second.
        ///
        /// <para>
        /// The final tick arrives with <paramref name="active"/> false and shuts the valve on a
        /// machine that never saw the ticks before it, which is why <see cref="ApplyHold"/> is a
        /// plain assignment rather than anything that reads the current state first.
        /// </para>
        /// </summary>
        protected override void Hold(NetArg arg, bool active) => ApplyHold(arg, active);

        /// <summary>
        /// Every machine: the beam and the loop. See <see cref="ApplyHold"/>.
        ///
        /// A dedicated server never receives this at all, being no part of the "others" its own
        /// broadcast goes to — which is why the aim is recorded on the authority path too, or the
        /// one machine that decides what is being drawn in would be the one that does not know
        /// where the player is pointing.
        /// </summary>
        protected override void PresentHold(NetArg arg, bool active) => ApplyHold(arg, active);

        /// <summary>Shared by both halves, and idempotent, because on a host both halves run.</summary>
        private void ApplyHold(NetArg arg, bool active)
        {
            if (!active)
            {
                ShutValve();
                return;
            }

            drawing = true;
            lastHoldTime = Time.time;

            if (!arg.HasOrientation) return;

            rayOrigin = arg.P;
            rayDirection = arg.R * Vector3.forward;
        }

        private void ShutValve()
        {
            drawing = false;
            flowing = false;

            // The trigger is up, so the uncork that blocked this hold is spent. Cleared here rather
            // than at the release tick alone, so a hold that ended by timing out re-arms too.
            blockedUntilRelease = false;

            // Pushed straight through rather than left to the next Update: the two paths that shut
            // a canister for good — an unequip and a disable — both run on a frame this object may
            // not see the end of.
            if (view != null) view.SetSucking(false);
            if (draught != null) draught.Clear();
            if (readout != null) readout.Clear();
        }

        // ── The frame ──────────────────────────────────────────────────────────

        /// <summary>
        /// Drive the tank, the contest and everything drawn. Runs on every machine off its own
        /// clock, which is what keeps the tank's rates per SECOND rather than per tick — a
        /// fifteen-hertz stream would otherwise make an empty canister a function of the frame rate.
        ///
        /// <para>
        /// Safe to declare: nothing in this hierarchy has an <c>Update</c> for this one to hide.
        /// </para>
        /// </summary>
        private void Update()
        {
            // A release is one message, and one message is exactly the kind of thing that goes
            // missing — along with the player who was holding the button.
            if (drawing && Time.time - lastHoldTime > holdTimeout) ShutValve();

            if (drawing) RefreshOwnerAim();

            // The trigger held down on an empty tank neither drains nor refills — see
            // SupplyReservoir.Tick, which is where that rule lives for every tank in the game. The
            // finger still down after an uncork is not a draw either, and must not cost anything.
            bool wanted = drawing && !blockedUntilRelease;
            flowing = tank == null ? wanted : tank.Tick(Time.deltaTime, wanted);

            GameObject body = null;
            Vector3 end = rayOrigin + rayDirection * range;

            if (flowing && Trace(rayOrigin, rayDirection, range, out RaycastHit hit))
            {
                end = hit.point;
                body = ContainmentFit.BodyOf(hit.collider);
            }

            if (hold != null && hold.Decides) Contest(body);

            if (readout != null && hold != null)
                readout.Show(OwnerIsLocal() && flowing ? body : null, hold.Settings);
            if (view != null) view.SetSucking(flowing);
            if (draught != null) draught.Aim(MouthPoint(), end, flowing, body != null);

            // LAST, and nothing may follow it: a swap re-equips the hand, which destroys this
            // object. See FollowContents.
            FollowContents();
        }

        /// <summary>
        /// On the machine holding the camera the aim is available right now and is better than
        /// anything that arrived over the wire — ticks reach a peer fifteen times a second, and a
        /// beam that turned only that often would smear visibly against a mouse.
        /// </summary>
        private void RefreshOwnerAim()
        {
            if (!OwnerIsLocal()) return;

            AimProvider aim = aimProvider;
            if (aim == null || aim.AimTransform == null) return;

            Ray ray = aim.GetAimRay();
            rayOrigin = ray.origin;
            rayDirection = ray.direction;
        }

        /// <summary>
        /// Authority-side: keep drawing <paramref name="body"/> in, or end the contest.
        ///
        /// <para>
        /// <see cref="ContainmentPull"/> owns everything about the fight — the size gate, the
        /// struggle, the fill rate and the moment the target actually goes in. This only says what
        /// the beam is on and how much time has passed. <c>Stop</c> is safe from anywhere and safe
        /// twice, which is what makes the idle path a plain call rather than a state machine.
        /// </para>
        /// <para>
        /// Only the deciding machine touches the pull. A peer that ended one locally would call
        /// <c>PulledBody.End</c> on its own copy of the captive — and on the captive's OWN machine
        /// that is the input path they struggle with, taken away while the authority is still
        /// drawing them in.
        /// </para>
        /// </summary>
        private void Contest(GameObject body)
        {
            ContainmentPull pull = ContainmentPull.Ensure(owner);
            if (pull == null) return;

            if (!flowing || body == null)
            {
                pull.Stop();
                return;
            }

            pull.Draw(hold, body, Time.deltaTime);
        }

        /// <summary>
        /// The nearest thing the draught can take hold of, skipping the holder's own body and the
        /// machine they are strapped into.
        ///
        /// <para>
        /// The ray starts at the holder's eye, inside the holder, so an unfiltered trace bottles
        /// the person drawing. <c>AimProvider.NearestOutside</c> is the shared rule for that and is
        /// static precisely so a trace like this one can borrow it; it also picks the nearest hit
        /// by hand, because <c>RaycastNonAlloc</c> neither sorts its buffer nor says when it
        /// truncated one.
        /// </para>
        /// <para>
        /// The carrier is the owner's transform ROOT. Mounting parents the rider under the mount,
        /// so the root IS the machine they are riding — and on their own feet the root is the owner
        /// themselves, which makes the second filter a repeat of the first rather than a case to
        /// branch on.
        /// </para>
        /// </summary>
        private bool Trace(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            hit = default;
            if (direction.sqrMagnitude < 1e-6f) return false;

            int count = Physics.RaycastNonAlloc(new Ray(origin, direction), hits, distance, hitMask,
                                                QueryTriggerInteraction.Ignore);

            Transform self = owner != null ? owner.transform : transform.root;

            return AimProvider.NearestOutside(hits, count, self, self.root, out hit);
        }

        private Vector3 MouthPoint() => mouth != null ? mouth.position : transform.position;

        // ── The two identities ─────────────────────────────────────────────────

        /// <summary>
        /// Keep the hotbar slot naming whichever of the two canisters this one has become.
        ///
        /// <para>
        /// <b>This is the wire.</b> <c>ItemState</c> is the server's own bag and does not
        /// replicate, so a peer learns a container is full only from the <c>Contained</c>
        /// announcement — and a peer that re-equips the item gets a fresh instance and loses that
        /// bit. The hotbar's ITEM ID does replicate, server-owned, and survives a re-equip, a drop,
        /// a pickup by somebody else and a reload. So the container's contents are published by
        /// changing what the slot holds, and the difference between a full canister and an empty
        /// one is a difference between two prefabs rather than a flag every machine has to be told
        /// about.
        /// </para>
        /// <para>
        /// <b>A bottled player is deliberately not a swap.</b> <c>IsFull</c> is true while one is
        /// inside, but there is no record: swapping would destroy the <c>ContainerHold</c> that
        /// adopted them (the hand is re-equipped, and the held object with it), and the fresh
        /// instance would read empty and swap straight back. They are held for a few seconds and
        /// the glass is left alone for those seconds — the trade <c>BottledPlayer</c> makes
        /// everywhere else for the same reason.
        /// </para>
        /// <para>
        /// <b>The swap waits <see cref="swapSeconds"/>.</b> Re-equipping the hand destroys this
        /// object and every effect hanging off it, so a swap on the same frame as the capture would
        /// delete the fold-in in the act of playing it.
        /// </para>
        /// <para>
        /// <b>Nothing may run after this.</b> A swap re-equips the hand, which destroys this object;
        /// Unity defers the destruction to the end of the frame, so <c>this</c> survives the rest of
        /// the call and nothing more should be asked of it.
        /// </para>
        /// </summary>
        private void FollowContents()
        {
            if (owner == null || hold == null || !hold.Decides) return;

            bool holdsRecord = hold.IsFull && !hold.HoldsPlayer;

            if (holdsRecord != sawRecord)
            {
                sawRecord = holdsRecord;
                contentsChangedAt = Time.time;
            }

            if (Time.time - contentsChangedAt < swapSeconds) return;

            CanisterIdentity.Follow(owner,
                                    holdsRecord ? fullIdentity : emptyIdentity,
                                    holdsRecord ? emptyIdentity : fullIdentity);
        }

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // The captive rides the slot's bag under Captivity.StateKey, which is what carries it
        // through a hotbar scroll, a save, a drop (PickupableItem keeps the whole bag verbatim) and
        // a pickup by somebody else. The tank rides the same bag under SupplyCharge's key, written
        // by UsableItem for the sibling reservoir.

        /// <summary>
        /// Write the captive into the slot's bag.
        ///
        /// Runs BEFORE <see cref="OnUnequipped"/> — which is why nothing in this class's teardown
        /// may touch the record. A captive released or forgotten there would be the one state the
        /// save never sees.
        /// </summary>
        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (hold != null) hold.CaptureInto(state);
        }

        /// <summary>
        /// Take the captive back out of the slot's bag. Runs after <see cref="OnEquipped"/>, so it
        /// wins over whatever equipping set up.
        /// </summary>
        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);
            if (hold != null) hold.RestoreFrom(state);
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        /// <summary>
        /// Resolve the parts once.
        ///
        /// In <c>OnEnable</c> rather than <c>Awake</c> because a canister is enabled and disabled as
        /// it moves between the hand, the pack and the sand, and every lookup here is cheap to
        /// confirm. The reservoir and the hold keep their own <c>Awake</c> on their own components,
        /// so there is no base message here for this to hide.
        /// </summary>
        private void OnEnable()
        {
            if (hold == null) hold = GetComponent<ContainerHold>();
            if (tank == null) tank = SupplyReservoir.On(gameObject);
            if (view == null) view = GetComponentInChildren<VacuumCanisterView>(true);
            if (draught == null) draught = GetComponentInChildren<VacuumDraught>(true);
            if (readout == null) readout = GetComponentInChildren<VacuumCanisterReadout>(true);

            WarnAboutMissingIdentities();
        }

        /// <summary>
        /// Say so, loudly and once per object, when this prefab does not know what its other half
        /// is.
        ///
        /// <para>
        /// Without both assets the swap in <see cref="FollowContents"/> never fires, and the
        /// failure is entirely silent: the canister captures perfectly, the record saves and
        /// reloads, and the item simply never changes — so a full canister reads as empty on every
        /// machine including the one that filled it, and the two prefabs the art exists for are
        /// never seen. An error rather than a warning, because there is no version of this wiring
        /// that is deliberately half done.
        /// </para>
        /// </summary>
        private void WarnAboutMissingIdentities()
        {
            if (emptyIdentity != null && fullIdentity != null) return;

            Debug.LogError($"[VacuumCanister] '{name}' is missing its " +
                           (emptyIdentity == null ? "EMPTY" : "FULL") +
                           " item identity, so it can never become the other canister — a captive " +
                           "will be held correctly and shown nowhere. Assign both InventoryItem " +
                           "assets on this prefab.", this);
        }

        private void OnDisable()
        {
            ShutValve();
            Attach(null);
        }

        /// <summary>
        /// Bind the container to its carrier — on EVERY machine, because a pull that was bound only
        /// on the authority is a pull whose messages nobody else is subscribed for.
        /// </summary>
        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);

            if (hold != null) hold.Bind(holder);

            Attach(holder != null ? ContainmentPull.Find(holder) : null);
        }

        /// <summary>
        /// Let go. Nothing here touches the record: the slot's bag was captured before this ran.
        ///
        /// <c>Unbind</c> detaches the container from the captor's pull, which ends a draw that was
        /// running — putting a canister away has to put the beam out, including when what it leaves
        /// the hand as is an object lying in the sand.
        /// </summary>
        public override void OnUnequipped(GameObject holder)
        {
            ShutValve();
            Attach(null);

            if (hold != null) hold.Unbind();

            base.OnUnequipped(holder);
        }

        // ── The pull's events, forwarded to the presentation ───────────────────
        //
        // Subscribed here rather than in each presentation component so the bookkeeping exists
        // once. All four events reach every machine: PullBegan and PullEnded off the authority's
        // Snared/SnareFreed broadcasts, Folded off NetMsg.Contained while the body is still there
        // to be seen, and Uncorked off NetMsg.Released.

        /// <summary>Watch <paramref name="pull"/>, and stop watching whatever came before it.</summary>
        private void Attach(ContainmentPull pull)
        {
            if (watchedPull == pull) return;

            if (watchedPull != null)
            {
                watchedPull.PullBegan -= OnPullBegan;
                watchedPull.PullEnded -= OnPullEnded;
                watchedPull.Uncorked -= OnUncorked;
            }

            WatchCaptive(null);
            watchedPull = pull;

            if (watchedPull == null) return;

            watchedPull.PullBegan += OnPullBegan;
            watchedPull.PullEnded += OnPullEnded;
            watchedPull.Uncorked += OnUncorked;
        }

        private void OnPullBegan(GameObject body) => WatchCaptive(body);

        private void OnPullEnded(GameObject body) => WatchCaptive(null);

        /// <summary>
        /// Follow the body being drawn, so the fold-in can be played while it is still visible.
        ///
        /// <c>PulledBody.Folded</c> is raised on every machine from <c>NetMsg.Contained</c>, which
        /// is announced deliberately BEFORE the despawn — it is the one moment at which a peer can
        /// draw a creature going into a bottle rather than a creature that has already gone.
        /// </summary>
        private void WatchCaptive(GameObject body)
        {
            if (watchedCaptive == body) return;

            if (watchedCaptive != null)
            {
                PulledBody previous = PulledBody.Find(watchedCaptive);
                if (previous != null) previous.Folded -= OnFolded;
            }

            watchedCaptive = body;
            if (watchedCaptive == null) return;

            PulledBody pulled = PulledBody.Find(watchedCaptive);
            if (pulled != null) pulled.Folded += OnFolded;
        }

        /// <summary>A captive has gone in. Ignored for a container that is not this one.</summary>
        private void OnFolded(ContainerHold into)
        {
            if (into != hold || watchedCaptive == null) return;

            Vector3 at = watchedCaptive.transform.position;

            if (draught != null) draught.Burst(at, inward: true);
            if (view != null) view.PlayFold();
        }

        /// <summary>
        /// A captive has come out, at <paramref name="at"/>. Every machine, off
        /// <c>NetMsg.Released</c>.
        ///
        /// The block is the load-bearing half: see <see cref="blockedUntilRelease"/>. The burst is
        /// only the puff of air that goes with it.
        /// </summary>
        private void OnUncorked(Vector3 at)
        {
            blockedUntilRelease = true;

            if (draught != null) draught.Burst(at, inward: false);
            if (view != null) view.PlayUncork();
        }

        private void OnValidate()
        {
            // A timeout inside the send interval would cut the draught between two perfectly
            // ordinary ticks, so it is floored well clear of EquipmentController's 0.2 s keepalive
            // rather than left to whoever edits it.
            holdTimeout = Mathf.Max(0.3f, holdTimeout);
        }
    }
}
