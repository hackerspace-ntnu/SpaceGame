using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Agents;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Persistence;

namespace SpaceGame.Items
{
    /// <summary>
    /// Lasso artifact — extends ToolItem.
    ///
    /// <para>
    /// <b>Hold to twirl, release to throw.</b> The press starts the loop turning over the player's
    /// head and it opens as it winds; letting go throws it, as far as it was wound. That gesture is
    /// the item — a lasso that fires on the press is a rope gun — and it is visible to everyone,
    /// because the twirl is drawn by <see cref="Present"/> on every machine rather than by the
    /// thrower alone. A player winding a rope up across the canyon is a thing you can see coming.
    /// </para>
    /// <para>
    /// <b>The catch cinches.</b> The loop closes onto what it caught over a quarter of a second and
    /// the rope cracks taut. Then the animal fights it: <see cref="LassoTether"/> takes the
    /// creature's legs off its AI and drives them itself, pulling away and throwing its weight
    /// across the rope until it tires. The rope this replaces switched the creature's NavMeshAgent
    /// off, which made every catch look like a kill.
    /// </para>
    /// <para>
    /// <b>The heavier end wins.</b> A taut rope has to move something, and which end it moves is
    /// decided by <see cref="PlayerPullShare"/> from the two masses. A light creature comes to you.
    /// A heavy one plants its feet and takes you with it.
    /// </para>
    ///
    /// <para>
    /// <b>Across the network.</b> The whole item used to live in <see cref="Use"/>, which is the
    /// authority-only half of <see cref="UsableItem"/> — so the arc, the rope, the loop and the
    /// dragged creature all existed on exactly one machine. For the host that looked like a
    /// working lasso; for everybody else a creature occasionally slid sideways under an invisible
    /// force, and a lasso thrown BY a client did nothing at all, because the client's pull on a
    /// server-owned creature is overwritten by its NetworkTransform within a tick.
    /// </para>
    /// <para>
    /// It splits three ways, and each part sits on the machine that can actually answer for it:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>The aim</b> — <see cref="OnRequestHold"/>, owner-side, because that is the one machine
    /// holding a camera. The point being thrown at travels in the message so that every machine
    /// throws the same arc instead of each guessing its own.
    /// </description></item>
    /// <item><description>
    /// <b>The twirl, the arc, the rope and the loop</b> — <see cref="Present"/> and
    /// <see cref="PresentHold"/>, on every machine, so a peer sees a rope rather than a creature
    /// being pushed by nothing.
    /// </description></item>
    /// <item><description>
    /// <b>The catch and the pull</b> — the thrower decides WHAT was caught (see
    /// <see cref="NetMsg.LassoRope"/> for why that cannot be re-derived per machine), and the
    /// machine that simulates the roped creature is the only one that moves it.
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Execution order 200</b> — after <see cref="PlayerMovement"/>, and load-bearing. That
    /// component ASSIGNS the player's velocity while they are grounded rather than blending it, so
    /// a drag applied before it runs is deleted rather than damped, and dallying would silently do
    /// nothing on flat ground. Same reason <c>LeashedBody</c> carries the same attribute.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class LassoArtifact : ToolItem, IItemDeferredRestore
    {
        /// <summary>
        /// Owner-run, and nearly moot: <see cref="Use"/> does nothing, because the throw is built
        /// by <see cref="Present"/> on every machine and the constraint decides for itself which
        /// machine may run it. Left as Owner so the aim — the one thing this item genuinely owns —
        /// stays with the machine that has the camera. Same arrangement as
        /// <see cref="LeashArtifact"/>.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Owner;

        /// <summary>
        /// The press opens a hold stream, because the button is the wind-up. Nothing fires per
        /// tick; the stream exists so the release has somewhere to land — and the release is where
        /// this item throws.
        /// </summary>
        public override bool IsContinuous => true;

        [Header("Firing")]
        [SerializeField] private LayerMask hookableLayers = ~0;

        [Tooltip("Reach of an uncharged flick — a throw let go the instant it was pressed.")]
        [SerializeField] private float minThrowRange = 12f;

        [Tooltip("Reach of a fully wound throw.")]
        [SerializeField] private float maxRange = 40f;

        [Tooltip("Seconds of twirl to reach full reach and a fully open loop.")]
        [SerializeField] private float twirlChargeTime = 1.2f;

        [Tooltip("How far the player may reach to tie a catch off to a post or a rock. " +
                 "Deliberately short: a hitch is walking the animal somewhere and tying it, not " +
                 "lassoing a second thing from across the canyon.")]
        [SerializeField] private float hitchRange = 6f;

        [Tooltip("Seconds of silence after which a wind-up puts itself away. The safety net for a " +
                 "release that never arrived — a dropped packet, or a thrower who died mid-twirl. " +
                 "Must comfortably exceed EquipmentController's hold send interval.")]
        [SerializeField] private float holdTimeout = 0.5f;

        [Tooltip("Metres above the player's ROOT that the loop is spun while winding up.\n\n" +
                 "The capsule is 2 m tall centred on the root, so the top of the head is at 1.0 — " +
                 "anything near that value puts the loop on the player's ear. A full arm's length " +
                 "clear of that is what reads as a lasso being wound.")]
        [SerializeField] private float twirlHeight = 2.1f;

        [Tooltip("Metres AHEAD of the player the loop is spun.\n\n" +
                 "Not decoration. The first-person eye sits at 1.45 m on this same root, so a loop " +
                 "spun straight up at 2.1 m orbits 0.65 m directly above the camera — close enough " +
                 "for its lower arc to skim the near plane and far enough off-frame to be unreadable. " +
                 "Carrying it forward puts the sweep where a wound rope actually goes and stops it " +
                 "clipping the lens. What the thrower READS the charge from is the aim guide; see " +
                 "LassoAim.")]
        [SerializeField] private float twirlForward = 0.85f;

        [Tooltip("Ceiling on how fast a thrown loop may travel, m/s. A rail rather than the thing " +
                 "that sets the pace — the arc's flight time comes from how high it is lofted. See " +
                 "LassoThrow.")]
        [SerializeField] private float throwSpeed = 30f;
        [SerializeField] private float reelSpeed = 18f;          // units/sec the rope coils back on a miss

        [Header("Rope / Joint")]
        [SerializeField] private float ropeSlack = 2f;
        [SerializeField] private float reelInForce = 18f;   // units/sec the rope shortens while reeling

        [Tooltip("Ceiling on how fast a heavy animal may drag the player, m/s. Without it, one " +
                 "frame where the creature is far past the rope's length — a teleport, a chunk " +
                 "load, a physics hitch — is converted into a launch.")]
        [SerializeField] private float maxDragSpeed = 14f;
        [SerializeField] private InputActionReference reelInAction;   // assign RightClick in Inspector

        [Header("Animation")]
        [SerializeField] private string throwTrigger = "Throw";
        [SerializeField] private GameObject lassoModel;   // the held dummy mesh — hidden while rope is out

        [Header("Rope Visual")]
        [SerializeField] private LineRenderer lineRenderer;
        [SerializeField] private Transform muzzle;
        [SerializeField] private LassoRope rope = new LassoRope();

        [Header("Lasso Loop")]
        [SerializeField] private LineRenderer loopRenderer;
        [SerializeField] private LassoLoop loop = new LassoLoop();

        [Header("The Caught Creature")]
        [SerializeField] private LassoStruggle struggle = new LassoStruggle();

        [Header("Rope Under Load")]
        [SerializeField] private LassoTension tension = new LassoTension();

        [Header("The Thrower's Aim")]
        [SerializeField] private LassoAim aim = new LassoAim();

        [Tooltip("Degrees of extra field of view at full strain, eased in and out by PlayerLook. " +
                 "The one thing that tells a player through the screen that the rope is about to " +
                 "go — the geometry alone does not, because a rope at its limit looks like a rope.")]
        [SerializeField] private float tautFovKick = 6f;

        [Header("Throw Arc")]
        [Tooltip("Metres the loop peaks above the higher end on the shortest throw. Flat, because " +
                 "a flick across a camp that lobs reads as a lob.")]
        [SerializeField] private float minArcHeight = 1f;

        [Tooltip("Metres the loop peaks above the higher end at full reach.\n\n" +
                 "This is an APEX, not a bonus added to the launch. It used to be the latter, and " +
                 "the throw missed everything it was aimed at by exactly this much times the flight " +
                 "time — see LassoThrow, which is where the arc now lives.")]
        [SerializeField] private float maxArcHeight = 4f;

        [SerializeField] private float throwGravity = 18f;

        [Header("Sound")]
        [SerializeField] private SfxId twirlSound = SfxId.RopeTwirl;
        [SerializeField] private SfxId throwSound = SfxId.RopeThrow;
        [SerializeField] private SfxId catchSound = SfxId.RopeCatch;
        [SerializeField] private SfxId snapSound = SfxId.RopeSnap;
        [SerializeField] private SfxId coilSound = SfxId.RopeCoil;
        [SerializeField] private SfxId hitchSound = SfxId.RopeHitch;

        // ── How much rope is out, relative to the gap it is spanning ───────────
        //
        // Slack is the only thing a rope's shape comes from, so these three numbers ARE the look of
        // the rope in each of its states. They are deliberately small. Slack is also what a chain
        // of distance constraints buckles with — a rope given 8% more length than it needs has to
        // put that 8% somewhere, and left to itself it folds it into the sharpest zigzag the node
        // count allows. LassoRope.Unkink now refuses the sharp folds, and keeping the slack honest
        // here means it has far less to refuse.

        /// <summary>Wound up in the hand: held, so barely any give at all.</summary>
        private const float TwirlSlack = 1.06f;

        /// <summary>
        /// In the air, where the rope has to TRAIL.
        ///
        /// This was 1.02, and 1.02 is why the throw was a straight line. The head has always flown
        /// a proper ballistic arc — but at 2% slack the span is 98% of the rope's length, which
        /// lands inside <see cref="LassoRope"/>'s straightening band and snapped four fifths of the
        /// cable onto the chord between hand and head on every substep. The arc was real and the
        /// rope drawn across it was a ruler.
        ///
        /// A fifth again of rope means the cable lags behind the head, sags under its own weight
        /// and curves — which is what a thrown rope does, and it is only affordable now because
        /// bend resistance stops that slack folding into a zigzag.
        /// </summary>
        private const float FlightSlack = 1.2f;

        /// <summary>Coiling back after a miss, where the rope IS supposed to go loose and pile up.</summary>
        private const float CoilSlack = 1.06f;

        // ── Runtime state ──────────────────────────────────────────────────────
        private bool _isLassoed;
        private bool _isThrowing;
        private bool _isTwirling;
        private float _twirlCharge;

        /// <summary>
        /// When the last hold tick arrived. Meaningless while not twirling.
        ///
        /// EquipmentController.OnDisable ends a hold LOCALLY on death and teardown, and explicitly
        /// leaves the remote halves to the item's own timeout — the convention LaserStaffArtifact
        /// set. Without one, a thrower who dies mid-twirl leaves every other machine spinning a
        /// loop over their corpse for the rest of the session, and the stale _isTwirling then
        /// refuses their next throw after they respawn.
        /// </summary>
        private float _lastHoldTime;

        /// <summary>
        /// Is the far end taking line right now? Published by the authority, never measured here —
        /// see <see cref="JudgeTension"/>.
        /// </summary>
        private bool _strainHeld;

        /// <summary>
        /// How worn the rope is, 0 (new) to 1 (parted). Only the authority's copy decides anything;
        /// elsewhere it is what the field of view kick is scaled by.
        /// </summary>
        private float _wear;

        private Rigidbody _targetRb;
        private Transform _targetTransform;   // used when target has no Rigidbody
        private LassoTether _tether;

        /// <summary>Set instead of <see cref="_tether"/> when the far end is a player. See LassoedBody.</summary>
        private LassoedBody _caughtPlayer;
        private float _currentRopeLength;
        private Coroutine _routine;
        private Vector3 _ropeEndPoint;   // world-space point drawn as rope tip
        private Vector3 _attachOffset;   // local offset on the body where the rope attaches

        // ── Mass ───────────────────────────────────────────────────────────────

        /// <summary>
        /// What a player weighs, for deciding which end of a taut rope moves.
        ///
        /// A constant rather than a measurement, because every player in this game is the same
        /// prefab and the two ends of a rope are computed on two different machines. Measuring it
        /// would mean putting a number on the wire to keep them agreeing, for no gain at all.
        /// </summary>
        private const float AssumedPlayerMass = 80f;

        /// <summary>
        /// How much of a taut rope's correction the PLAYER absorbs, 0 to 1. The creature absorbs
        /// the rest.
        ///
        /// <para>
        /// This is the whole of dallying, and it is a pure function of two masses precisely so that
        /// both ends can compute it rather than agree about it — <see cref="LassoTether"/> runs on
        /// the machine that simulates the creature, and the player half runs on the machine that
        /// owns the body, and in an ordinary session those are two different computers.
        /// </para>
        /// </summary>
        public static float PlayerPullShare(float targetMass, float playerMass = AssumedPlayerMass) =>
            Mathf.Clamp01(targetMass / Mathf.Max(targetMass + playerMass, 0.001f));

        // ── Wire verbs, in NetArg.B of the USE message ─────────────────────────
        //
        // A is already the hotbar slot, so B it is — the same convention the grapple uses. Note
        // that B is unavailable on the HOLD stream, where EquipmentController uses it as the
        // active flag; the throw travels in P and R instead.

        private const int MissVerb = 0;
        private const int ThrowVerb = 1;
        private const int ReleaseVerb = 2;
        private const int HitchVerb = 3;

        /// <summary>
        /// Set alongside <see cref="HitchVerb"/> when the anchor is bare geometry rather than an
        /// object with an identity — so <c>P</c> is read as a world point rather than as an offset
        /// in a local space that does not exist. Well clear of the verbs below it; <c>B</c> is ours
        /// on a press (<c>EquipmentController</c> only owns it on hold ticks).
        /// </summary>
        private const int BareAnchorFlag = 1 << 8;

        // ── Owner side: describe the press ─────────────────────────────────────

        /// <summary>
        /// Owner-side: settle what this press means. Not where it is thrown — that is not known
        /// yet, and could not be: at the moment of the press the throw has not been wound up, and
        /// how far it reaches is what the winding decides. The aim rides the release instead, in
        /// <see cref="OnRequestHold"/>.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            base.OnRequestUse(ref arg);

            // Holding a catch and pointing at something a rope can be tied to: this press ties it
            // off rather than letting it go. See TryAimAtHitch — and note that pointing at nothing
            // still means "drop it", which is the same shape LeashArtifact gives the gesture: a
            // click on a thing ties, a click on nothing lets go, and no second key is needed for
            // either.
            if (_isLassoed && TryAimAtHitch(ref arg)) return;

            // Rope already out, one already in the air, or one already being wound: this press
            // lets go.
            if (_isLassoed || _isThrowing || _isTwirling)
            {
                arg.B = ReleaseVerb;
                return;
            }

            // Present() is deliberately not gated on CanUse — see UsableItem — so a depleted lasso
            // has to refuse here, on the one machine that can still tell the difference.
            arg.B = CanUse() ? ThrowVerb : MissVerb;
        }

        /// <summary>
        /// Owner-side, on the release tick: where this throw is going.
        ///
        /// <para>
        /// The raycast belongs here rather than inside the coroutine for two reasons. It is the
        /// only moment the aim is honest — this is the machine holding the camera, and a peer's
        /// copy of a remote player has an <see cref="AimProvider"/> with a dead camera behind it —
        /// and resolving it once means every machine throws at the same point instead of each
        /// picking its own.
        /// </para>
        /// <para>
        /// The reach comes from how long the loop was wound, so the charge never has to be sent:
        /// it is already baked into the point. Which is just as well, because there is no field
        /// left to send it in — <c>A</c> is the slot index and <c>B</c> is the hold stream's own
        /// active flag.
        /// </para>
        /// </summary>
        public override void OnRequestHold(ref NetArg arg, bool active)
        {
            base.OnRequestHold(ref arg, active);

            if (active || !_isTwirling) return;
            if (aimProvider == null) return;

            // The same resolution the aim guide has been drawing for the whole wind-up — see
            // ResolveThrowTarget. Two copies of this is how a preview and a throw drift apart, and
            // a guide that lies is worse than no guide.
            Vector3 targetPoint = ResolveThrowTarget(out _);

            arg.P = targetPoint;
            arg.R = Quaternion.LookRotation(aimProvider.GetAimRay().direction);
        }

        /// <summary>
        /// Owner-side: is the player pointing at something they can tie this catch off to?
        ///
        /// <para>
        /// The one machine with a camera decides, and the answer travels — the anchor as
        /// <c>NetArg.Target</c>, the knot in <c>P</c> as an offset in the anchor's own space, and a
        /// flag in <c>B</c> saying whether that offset means anything. Bare geometry has no local
        /// space and no identity, and its world point is the same on every machine by definition,
        /// so it travels as a world point instead. This is the encoding
        /// <see cref="LeashArtifact"/> already uses for the same problem, and it exists because a
        /// world point re-projected per machine names a different part of anything that moves.
        /// </para>
        /// </summary>
        private bool TryAimAtHitch(ref NetArg arg)
        {
            if (aimProvider == null) return false;

            Ray aimRay = aimProvider.GetAimRay();
            if (!Physics.Raycast(aimRay, out RaycastHit hit, hitchRange, ~0, QueryTriggerInteraction.Ignore))
                return false;

            Transform caught = _targetRb != null ? _targetRb.transform : _targetTransform;
            if (!LassoHitch.IsHitchable(hit.collider, owner.transform, caught)) return false;

            // The anchor's ROOT, via its Rigidbody where it has one — the same reduction every
            // other query in this file makes, and the reason is the invariant: RaycastHit.transform
            // is the rigidbody's, so a crate's lid and the crate are the same anchor.
            Rigidbody anchorBody = hit.collider.GetComponentInParent<Rigidbody>();
            GameObject anchorRoot = anchorBody != null ? anchorBody.gameObject : hit.collider.gameObject;

            arg = arg.With(anchorRoot);

            // Minted by With() above, so this is the first moment we can tell whether the anchor
            // has an identity to send. Ground and scenery do not, and need none.
            bool bare = arg.Target == 0 && Network.IsNetworked;

            arg.P = bare ? hit.point : LassoHitch.EncodeKnot(anchorRoot, hit.point);
            arg.B = HitchVerb | (bare ? BareAnchorFlag : 0);
            return true;
        }

        /// <summary>
        /// Nothing. Both halves of this item are a rope being drawn and a creature being pulled,
        /// and both live where they can be seen — see the class summary.
        /// </summary>
        protected override void Use() { }

        // ── Every machine: the wind-up ─────────────────────────────────────────

        protected override void Present()
        {
            if ((UseArg.B & ~BareAnchorFlag) == HitchVerb)
            {
                Hitch(UseArg);
                return;
            }

            if (UseArg.B == ReleaseVerb)
            {
                Release();
                return;
            }

            if (UseArg.B != ThrowVerb) return;

            // A second throw with no release in between means a message arrived twice or out of
            // order. Keep the rope that is already out rather than starting a rival coroutine.
            if (_isLassoed || _isThrowing || _isTwirling) return;
            if (owner == null) return;

            BeginTwirl();
        }

        /// <summary>
        /// Every machine: the release, which is where this item throws.
        ///
        /// <para>
        /// A release carrying no orientation is a cancel, not a throw, and telling those apart is
        /// what <see cref="NetArg.HasOrientation"/> is for. <c>EquipmentController.EndHold(send:
        /// false)</c> delivers a default NetArg on unequip, on disable and on death — and treating
        /// that as a throw would fling a rope along whatever direction an all-zero quaternion
        /// decodes to every time the player scrolled the hotbar mid-wind-up.
        /// </para>
        /// </summary>
        protected override void PresentHold(NetArg arg, bool active)
        {
            _lastHoldTime = Time.time;

            if (active || !_isTwirling) return;

            if (!arg.HasOrientation)
            {
                CancelTwirl();
                return;
            }

            _isTwirling = false;
            _routine = StartCoroutine(ThrowRoutine(arg.P, arg.R * Vector3.forward));
        }

        protected override bool CanUse()
        {
            return base.CanUse() || _isLassoed;
        }

        // ── The wind-up ────────────────────────────────────────────────────────

        private void BeginTwirl()
        {
            _isTwirling = true;
            _twirlCharge = 0f;

            // Stamped here or the timeout fires before the first hold tick has had time to arrive.
            _lastHoldTime = Time.time;

            if (lassoModel != null) lassoModel.SetActive(false);

            rope.Bind(lineRenderer);
            loop.Bind(loopRenderer);
            loop.Show();

            Vector3 centre = TwirlCentre();
            rope.Show(GetRopeStart(), centre);

            // The wind-up, on every machine — it is a thing you hear somebody doing across a camp.
            // This is where the item's one sound used to land by accident, via UsableItem.PlayUse
            // firing `useSound` on the press; now the press has a sound because a press means
            // something, and the throw and the catch have their own.
            Sfx.Play(twirlSound, TwirlCentre(), GetInstanceID());
        }

        /// <summary>Put the rope away without throwing it. Unequip, death, or a hotbar scroll.</summary>
        private void CancelTwirl()
        {
            _isTwirling = false;
            _twirlCharge = 0f;

            rope.Hide();
            loop.Hide();
            aim.Hide();
            if (lassoModel != null) lassoModel.SetActive(true);
        }

        /// <summary>
        /// Where the loop spins while winding — well clear of the head.
        ///
        /// Measured from the player's ROOT rather than from the hand, and that is the whole fix for
        /// a loop that span at ear level. The player capsule is 2 m tall with its centre on the
        /// root, so the top of the head is only 1 m up and the muzzle sits near the hip: hand plus
        /// three quarters of a metre landed the loop exactly on the head. The root is a fixed
        /// reference, so this is a height above the player rather than a height above whatever the
        /// throw animation is doing with their arm.
        /// </summary>
        private Vector3 TwirlCentre()
        {
            Transform root = owner != null ? owner.transform : transform;

            // Forward as well as up, and the forward is not decoration — see twirlForward. Taken
            // from the body's facing rather than from the aim, because the loop is being wound by
            // an arm and does not swing about wherever the player happens to be looking.
            return root.position + Vector3.up * twirlHeight + root.forward * twirlForward;
        }

        /// <summary>How high a throw between these two points arcs. See <see cref="LassoThrow"/>.</summary>
        private float ApexFor(Vector3 start, Vector3 target)
        {
            Vector3 delta = target - start;
            float flat = new Vector3(delta.x, 0f, delta.z).magnitude;

            return LassoThrow.ApexFor(flat, maxRange, minArcHeight, maxArcHeight);
        }

        /// <summary>
        /// The twirl, per frame, on every machine.
        ///
        /// The charge is accumulated locally rather than replicated. Every machine received the
        /// press, so every machine knows when the winding started; and what the charge decides —
        /// the reach — is already resolved into the aimed point by the time anyone needs it. What
        /// is left is how wide the loop is drawn, which is cosmetic and may drift by a frame of
        /// latency without anybody being able to tell.
        /// </summary>
        private void TickTwirl(float deltaTime)
        {
            if (!_isTwirling) return;

            _twirlCharge = Mathf.Clamp01(_twirlCharge + deltaTime / Mathf.Max(twirlChargeTime, 0.01f));

            Vector3 centre = TwirlCentre();
            loop.Twirl(centre, Vector3.up, _twirlCharge, deltaTime);

            // Rope length tracks the gap exactly, so the coil between hand and loop hangs with a
            // little slack and no more — a wound rope is held, not dangled.
            rope.Simulate(GetRopeStart(), centre, Vector3.Distance(GetRopeStart(), centre) * TwirlSlack, deltaTime);

            DrawAimGuide();
        }

        /// <summary>
        /// The thrower's own preview of the throw they are winding, on their machine only.
        ///
        /// <para>
        /// Owner-gated because it is drawn from a charge and an aim ray, and a peer's copy of a
        /// remote player has neither a live camera nor any business drawing one player's crosshair
        /// into everybody else's world. Every other machine shows the wind-up as the loop turning
        /// over the thrower's head, which is what that is for.
        /// </para>
        /// </summary>
        private void DrawAimGuide()
        {
            if (!OwnerIsLocal() || aimProvider == null)
            {
                aim.Hide();
                return;
            }

            Vector3 start = GetRopeStart();
            Vector3 target = ResolveThrowTarget(out bool blocked);

            // The same circle the catch will be judged by — see TryGetLatchTarget. Drawing a ring
            // the loop's mouth did not match would be telling the player they had it when they did
            // not, which is the failure this whole guide exists to end.
            aim.Draw(start, target, throwGravity, ApexFor(start, target), throwSpeed,
                     _twirlCharge, loop.Radius, blocked);
        }

        /// <summary>
        /// Where this throw is going, from the aim and the charge. The one place that decides it.
        ///
        /// <para>
        /// Shared by the guide the thrower is reading and the release that actually throws, so the
        /// preview cannot promise a different point from the one the item then aims at. It is
        /// owner-side in both cases — the only machine with a camera behind its
        /// <see cref="AimProvider"/>.
        /// </para>
        /// </summary>
        /// <param name="obstructed">
        /// The aim is stopped well short of the reach this throw has been wound for — a rock a few
        /// metres away while the player holds a rope wound for thirty. The guide draws that in a
        /// different colour rather than silently drawing a short arc, because a wind-up that is
        /// about to be spent on a wall is worth being told about before the release, not after.
        /// </param>
        private Vector3 ResolveThrowTarget(out bool obstructed)
        {
            float reach = Mathf.Lerp(minThrowRange, maxRange, _twirlCharge);

            Ray aimRay = aimProvider.GetAimRay();

            if (!Physics.Raycast(aimRay, out RaycastHit aimHit, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                obstructed = false;
                return aimRay.origin + aimRay.direction * reach;
            }

            obstructed = aimHit.distance < reach * ObstructedFraction;
            return aimHit.point;
        }

        /// <summary>
        /// How much of a wound-up reach has to be left unused before the aim counts as obstructed.
        /// Half: a throw that lands past the middle of what it was wound for is a throw, not a
        /// wasted wind-up.
        /// </summary>
        private const float ObstructedFraction = 0.5f;

        // ── Throw sequence ─────────────────────────────────────────────────────

        private IEnumerator ThrowRoutine(Vector3 targetPoint, Vector3 aimDirection)
        {
            _isThrowing = true;

            aim.Hide();
            if (lassoModel != null) lassoModel.SetActive(false);

            Sfx.Play(throwSound, GetRopeStart(), GetInstanceID());

            // Here rather than at the press, which is where it used to be and where it no longer
            // belongs: the press is now the start of a wind-up that lasts as long as the player
            // holds it, so an arm that threw on the press played its throw seconds before the rope
            // left the hand. The wind-up is carried by the loop turning overhead instead, which is
            // what a wind-up actually looks like.
            Animator animator = owner.GetComponentInChildren<Animator>();
            if (animator != null) animator.SetTrigger(throwTrigger);

            Vector3 start = GetRopeStart();

            rope.Bind(lineRenderer);
            loop.Bind(loopRenderer);
            // A short line along the throw, not a point. Show(start, start) stacks all thirty nodes
            // on the muzzle with zero-length segments, which the constraint solver cannot give a
            // direction to — so the rope leaves the hand as a knot that unpicks itself over the
            // first few frames.
            rope.Show(start, start + aimDirection.normalized * 0.5f);
            loop.Show();

            // The arc, solved so it passes THROUGH the aimed point rather than over it. This was
            // four lines of inline ballistics that added throwArcHeight straight onto a correct
            // solution's vertical component; see LassoThrow for what that cost and why loft has to
            // be spent as flight time instead.
            Vector3 velocity = LassoThrow.SolveVelocity(start, targetPoint, throwGravity,
                                                        ApexFor(start, targetPoint), throwSpeed,
                                                        out float timeToTarget);

            Vector3 headPos = start;
            Vector3 prevHeadPos = start;
            _ropeEndPoint = start;

            float elapsed = 0f;

            while (true)
            {
                elapsed += Time.deltaTime;
                velocity += Vector3.down * throwGravity * Time.deltaTime;

                prevHeadPos = headPos;
                headPos += velocity * Time.deltaTime;

                Vector3 stepDir = headPos - prevHeadPos;
                Vector3 stepDirNorm = stepDir.sqrMagnitude > 1e-6f ? stepDir.normalized : velocity.normalized;

                // Only the thrower's machine may decide what was caught. Every machine runs this
                // same arc, but they integrate it with their own Time.deltaTime, so two of them
                // can pick different creatures out of a crowd — or one can catch where another
                // misses. A peer's head therefore flies on until the catch is announced, which is
                // the round trip made visible and is the correct thing to show: the alternative is
                // two players watching two different animals get roped.
                if (OwnerIsLocal() &&
                    TryGetLatchTarget(prevHeadPos, headPos, out Rigidbody latchedRb, out Transform latchedTransform, out Vector3 latchPoint))
                {
                    _ropeEndPoint = latchPoint;
                    DrawFlight(headPos, stepDirNorm, start);

                    _isThrowing = false;

                    GameObject caught = latchedTransform != null
                        ? latchedTransform.gameObject
                        : latchedRb.gameObject;

                    Attach(latchedRb, latchedTransform);
                    SendRope(LassoVerb.Caught, caught);
                    yield break;
                }

                bool pastTarget = Vector3.Dot(headPos - targetPoint, velocity) > 0f && elapsed > timeToTarget;
                bool tooFar = (headPos - start).magnitude > maxRange * 1.5f;

                _ropeEndPoint = headPos;
                DrawFlight(headPos, stepDirNorm, start);

                if (pastTarget || tooFar)
                {
                    yield return Miss(headPos, velocity, start);
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>The rope paying out behind a flying loop.</summary>
        private void DrawFlight(Vector3 headPos, Vector3 travelDir, Vector3 start)
        {
            loop.Fly(headPos, travelDir, _twirlCharge, Time.deltaTime);

            // Slightly more rope than the gap, which is what makes the cable trail and crack rather
            // than being a straight line that happens to be getting longer.
            rope.Simulate(GetRopeStart(), headPos, Vector3.Distance(start, headPos) * FlightSlack, Time.deltaTime);
        }

        /// <summary>
        /// Nothing was caught: let the loop fall, then coil the rope back into the hand.
        ///
        /// The coil is the cable's rest length shrinking toward nothing, which the Verlet chain
        /// turns into a rope piling up and swinging on its own. The rope this replaces lerped a
        /// straight line back to the muzzle.
        /// </summary>
        private IEnumerator Miss(Vector3 headPos, Vector3 velocity, Vector3 start)
        {
            while (true)
            {
                velocity += Vector3.down * throwGravity * Time.deltaTime;
                Vector3 prev = headPos;
                headPos += velocity * Time.deltaTime;

                Vector3 stepDir = headPos - prev;
                Vector3 stepDirNorm = stepDir.sqrMagnitude > 1e-6f ? stepDir.normalized : velocity.normalized;

                bool landed = Physics.Linecast(prev, headPos, out RaycastHit groundHit, ~0, QueryTriggerInteraction.Ignore)
                              && !groundHit.collider.transform.IsChildOf(owner.transform);
                if (landed) headPos = groundHit.point;

                _ropeEndPoint = headPos;
                loop.Fly(headPos, stepDirNorm, _twirlCharge, Time.deltaTime);
                rope.Simulate(GetRopeStart(), headPos, Vector3.Distance(start, headPos) * FlightSlack, Time.deltaTime);

                if (landed) break;
                if (headPos.y < start.y - maxRange) break;

                yield return null;
            }

            Vector3 coilFrom = _ropeEndPoint;
            float coilDist = Vector3.Distance(coilFrom, GetRopeStart());
            float coilElapsed = 0f;
            float coilDuration = coilDist / Mathf.Max(reelSpeed, 0.1f);

            while (coilElapsed < coilDuration)
            {
                coilElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(coilElapsed / coilDuration);

                _ropeEndPoint = Vector3.Lerp(coilFrom, GetRopeStart(), t);

                loop.Ride(_ropeEndPoint, GetRopeStart() - _ropeEndPoint, Time.deltaTime);
                rope.Simulate(GetRopeStart(), _ropeEndPoint, coilDist * (1f - t) * CoilSlack, Time.deltaTime);

                yield return null;
            }

            rope.Hide();
            loop.Hide();
            if (lassoModel != null) lassoModel.SetActive(true);
            _isThrowing = false;

            // The rope arriving back in the hand. A miss that ends in silence reads as the throw
            // having been cancelled rather than having failed.
            Sfx.Play(coilSound, GetRopeStart(), GetInstanceID());
        }

        // ── Attach / Release ───────────────────────────────────────────────────

        private void Attach(Rigidbody targetRb, Transform targetTransform)
        {
            if (targetRb == null && targetTransform == null) return;

            // The rope hangs off the holder, so there is no rope without one. A catch announced to
            // a machine whose copy of this item has not been equipped yet has nowhere to land.
            if (owner == null) return;

            // Idempotent, because both halves of the catch can land here: the thrower attaches from
            // inside its own arc, and then the server's relay of that same catch comes back round.
            if (_isLassoed) return;

            // Reachable from outside the arc now — the announced catch arrives while a peer's own
            // head is still in the air. Stop that coroutine before ReelRoutine takes the field.
            StopRoutine();

            _targetRb        = targetRb;
            _targetTransform = targetTransform;
            _isThrowing      = false;
            _isTwirling      = false;
            _isLassoed       = true;

            if (lassoModel != null) lassoModel.SetActive(false);

            Transform root = targetRb != null ? targetRb.transform : targetTransform;

            // Neck height on a creature of THIS size, from the one place that decides it. The rope
            // used to be drawn to a flat 1.2 m from a field on this class while the tether
            // constrained against a second flat 1.2 m from a field on LassoStruggle — two copies of
            // one number, on the same prefab, with nothing keeping them equal. Move either and the
            // rope goes taut against a point it is not drawn to.
            _attachOffset = Vector3.up * LassoTether.AttachHeightFor(root.gameObject,
                                                                    struggle.AttachFraction,
                                                                    struggle.AttachHeight);

            Vector3 attachWorldPos = root.position + _attachOffset;
            // Clamped to the line there is: a catch made at the very end of a fully wound throw
            // would otherwise start out longer than the rope, and the animal could then be paid out
            // to a length nothing in the item ever agreed to.
            _currentRopeLength = Mathf.Clamp(Vector3.Distance(GetRopeStart(), attachWorldPos) + ropeSlack,
                                             ropeSlack, tension.MaxLength);
            _ropeEndPoint = attachWorldPos;

            Transform ropeAnchor = muzzle != null ? muzzle : owner.transform;

            // A PLAYER is not a creature, and the difference is which machine may move them.
            //
            // A player's body is owner-authoritative, so the pull has to be applied by the machine
            // that owns them — which is never this one unless they happen to be the local player.
            // The component is created on EVERY machine and gates itself, exactly as FlungBody and
            // LeashedBody do, because a catch is announced to everybody and only one of them turns
            // out to own the victim. Before this, roping a player put a tether on the server, whose
            // every write was discarded: it worked on the host and did nothing to a client.
            if (root.CompareTag("Player"))
            {
                LassoedBody caught = LassoedBody.Ensure(root.gameObject);

                if (caught == null || !caught.Bind(ropeAnchor, _currentRopeLength, AssumedPlayerMass))
                {
                    _isLassoed = false;
                    return;
                }

                _caughtPlayer = caught;
            }

            // Taking a creature's legs off its AI and driving them is a change to the creature, not
            // to the rope, so it belongs only on the machine that OWNS it. On a peer the replica is
            // kinematic on purpose — NetworkRigidbody makes it so — and a second tether there would
            // be a second authority fighting the NetworkTransform.
            else if (OwnsTarget())
            {
                LassoTether tether = LassoTether.Ensure(root.gameObject);

                // A creature already on somebody else's rope is not catchable. Refusing here rather
                // than drawing a rope that constrains nothing is what stops a second thrower being
                // dragged around by an animal that cannot feel them.
                if (tether == null || !tether.Bind(ropeAnchor, _currentRopeLength, struggle))
                {
                    _isLassoed = false;
                    return;
                }

                _tether = tether;
            }

            rope.Bind(lineRenderer);
            loop.Bind(loopRenderer);

            rope.Show(GetRopeStart(), attachWorldPos);
            loop.Show();
            loop.BeginCinch();
            rope.Snap();

            // The loop closing and the rope cracking taut, at the far end where it happened. The
            // most legible moment the item has, and it had no sound at all.
            Sfx.Play(catchSound, attachWorldPos, GetInstanceID());

            _wear = 0f;
            _routine = StartCoroutine(RideRoutine());
        }

        /// <summary>
        /// Tie the catch off and let go of it: the lasso's rope becomes a <see cref="Leash"/>.
        ///
        /// <para>
        /// Every machine, from one announced hitch — a leash is a local <c>GameObject</c> like
        /// every other rope here, and each machine builds its own copy so that the two that own its
        /// two ends can resolve them. The creature is not sent: every machine already knows it from
        /// the <c>Caught</c> it applied, and re-sending a reference to name a thing both ends
        /// already agree on is how the two get a chance to disagree.
        /// </para>
        /// <para>
        /// Release is called last and unconditionally. A machine that could not build the leash —
        /// an anchor in a chunk it has not streamed — must still drop the lasso, or it goes on
        /// drawing a rope to a creature every other machine has handed over.
        /// </para>
        /// </summary>
        private void Hitch(NetArg arg)
        {
            if (!_isLassoed) return;

            Transform caught = _targetRb != null ? _targetRb.transform : _targetTransform;

            if (caught != null)
            {
                bool bare = (arg.B & BareAnchorFlag) != 0;
                GameObject anchor = bare ? null : arg.Resolve();

                // A named anchor that will not resolve here is one whose chunk this machine has not
                // got. Tying to the world point instead would put the knot wherever that object was
                // standing on somebody else's screen, so the hitch is simply refused and the rope
                // dropped — which is the state every other machine ends in anyway.
                if (anchor != null || bare)
                {
                    Leash tied = LassoHitch.TieOff(caught.gameObject, anchor, arg.P, _attachOffset.y);
                    if (tied != null) Sfx.Play(hitchSound, tied.B.Position, GetInstanceID());
                }
            }

            Release();
        }

        /// <summary>
        /// Drop everything. Safe to call from anywhere, including twice, and on a machine that
        /// never had a rope out — a press that missed presents a Release with nothing to release.
        /// </summary>
        private void Release()
        {
            _isLassoed = false;
            _isThrowing = false;
            _isTwirling = false;
            _reelHeld = false;
            _strainHeld = false;
            _twirlCharge = 0f;
            _wear = 0f;

            StopRoutine();
            ClearFovKick();

            // Only ever non-null on the machine that took the creature's legs — see Attach — so
            // this hands navigation back exactly where it was taken away.
            //
            // The anchor is passed so a release from THIS rope cannot free a creature another
            // thrower has since taken hold of. Written out rather than inlined because Release is
            // reached from OnDestroy, where owner may already be gone.
            Transform ropeAnchor = muzzle != null ? muzzle
                                 : owner != null ? owner.transform
                                 : null;

            if (_tether != null) { _tether.Release(ropeAnchor); _tether = null; }
            if (_caughtPlayer != null) { _caughtPlayer.Release(ropeAnchor); _caughtPlayer = null; }

            _targetRb        = null;
            _targetTransform = null;

            rope.Hide();
            loop.Hide();
            aim.Hide();
            if (lassoModel != null) lassoModel.SetActive(true);
        }

        private void StopRoutine()
        {
            if (_routine == null) return;

            StopCoroutine(_routine);
            _routine = null;
        }

        // ── Across the network ─────────────────────────────────────────────────
        //
        // Everything below rides the THROWER's channel rather than this item's. See
        // NetMsg.LassoRope for why: this prefab carries a NetworkObject of its own — it has to,
        // because dropping the item routes through World.Spawn — and that NetworkObject is never
        // spawned while the item is in a hand, so a send from here would resolve to a dormant relay
        // and quietly run on the local machine only. Which is exactly the bug this file is fixing.

        /// <summary>The transform whose channel we are registered on, so we unregister from it.</summary>
        private Transform _channel;

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);

            rope.Bind(lineRenderer);
            loop.Bind(loopRenderer);

            // Every machine equips from the replicated hotbar, so every machine — the thrower's,
            // the server's and every peer's — gets its own instance and its own registration.
            Listen(holder != null ? holder.transform : null);
        }

        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);

            // Putting the lasso away drops the rope. Without this the creature keeps the legs this
            // item took from it, on whichever machine took them, forever — and on the server that
            // is a permanently puppeted animal that no player can see a rope on. The slot's saved
            // bag is written BEFORE OnUnequipped (see EquipmentController), so re-equipping still
            // restores the rope.
            Release();
            Listen(null);
        }

        private void OnDestroy()
        {
            // OnUnequipped is not guaranteed: the item is destroyed outright when the player dies
            // or the slot empties.
            Release();
            Listen(null);

            // The aim guide is a GameObject of its own, unparented so its world-space line is not
            // dragged about by the hand. Nothing else would ever take it with us.
            aim.Dispose();
        }

        private void Listen(Transform channel)
        {
            if (_channel == channel) return;

            NetworkManager manager = NetworkManager.Singleton;

            if (_channel != null)
            {
                _channel.NetOff(NetMsg.LassoRope, OnRopeRequested);
                _channel.NetOff(NetMsg.LassoRoped, OnRopeAnnounced);
                if (manager != null) manager.OnClientConnectedCallback -= OnPeerJoined;
            }

            _channel = channel;
            if (_channel == null) return;

            _channel.NetOn(NetMsg.LassoRope, OnRopeRequested);
            _channel.NetOn(NetMsg.LassoRoped, OnRopeAnnounced);
            if (manager != null) manager.OnClientConnectedCallback += OnPeerJoined;
        }

        /// <summary>Owner-side: tell the session what the rope just did.</summary>
        private void SendRope(int verb, GameObject subject)
        {
            if (owner == null) return;

            // A carries the rope's length in centimetres — NetArg has no float field, the same
            // convention CraftLaunch uses for its speeds.
            //
            // It has to travel because the two ends would otherwise disagree after a load: the
            // authority restores the length the player had reeled to, while every other machine
            // recomputes one from wherever the two ends happen to be standing now. Zero means "work
            // it out yourself", which is what a fresh catch sends.
            NetMessaging.NetSendTo(owner, NetMsg.LassoRope,
                new NetArg
                {
                    B = verb,
                    A = _isLassoed ? Mathf.RoundToInt(_currentRopeLength * 100f) : 0,
                }.With(subject), NetTo.Server);
        }

        /// <summary>
        /// Somebody joined. If this rope is on something, say so again.
        ///
        /// <para>
        /// <see cref="LassoVerb.Caught"/> is an absolute state rather than an edge — it says "this
        /// is roped", not "this was just roped" — so re-sending it costs a joiner one Attach and
        /// costs everyone else one idempotent no-op. Without it a joiner watched the creature
        /// struggling under an invisible force with no rope on it, which is precisely the symptom
        /// this whole rework was written to remove.
        /// </para>
        /// <para>
        /// From the authority only: a client may not broadcast, and the server holds its own copy
        /// of every equipped item, so it can answer for any player's rope.
        /// </para>
        /// </summary>
        private void OnPeerJoined(ulong clientId)
        {
            if (!IsAuthority || !_isLassoed) return;
            if (clientId == NetworkManager.ServerClientId) return;

            Transform root = _targetRb != null ? _targetRb.transform : _targetTransform;
            if (root == null) return;

            SendRope(LassoVerb.Caught, root.gameObject);
        }

        /// <summary>
        /// Server side. <see cref="IsAuthority"/> rather than <c>Network.Simulates(this)</c>: an
        /// equipped artifact is instantiated into a hand and never spawned, so its own dormant
        /// NetworkObject would make Simulates answer "yes, you simulate it" on every machine at
        /// once — the same trap LaserStaffArtifact documents.
        /// </summary>
        private void OnRopeRequested(in NetArg arg, ulong sender)
        {
            if (!IsAuthority) return;

            ApplyRope(arg);

            // All, not Others: the verbs are absolute states rather than edges, so handing the
            // sender its own news back costs one no-op instead of needing an exception list.
            NetMessaging.NetSendTo(owner, NetMsg.LassoRoped, arg, NetTo.All);
        }

        /// <summary>Every machine, including the one that asked.</summary>
        private void OnRopeAnnounced(in NetArg arg, ulong sender) => ApplyRope(arg);

        /// <summary>
        /// One published rope state. Idempotent in both directions, because the host runs the
        /// request handler and the broadcast it makes inline, one inside the other.
        /// </summary>
        private void ApplyRope(in NetArg arg)
        {
            switch (arg.B)
            {
                case LassoVerb.Caught:
                    GameObject caught = arg.Resolve();
                    if (caught == null) return;

                    Attach(caught.GetComponentInParent<Rigidbody>(), caught.transform);

                    // AFTER Attach, which sets a length of its own from the gap it can see right
                    // now. That is the safe answer; an announced length is the better one, because
                    // the thrower may have reeled the creature most of the way in already. See
                    // SendRope.
                    if (arg.A > 0 && _isLassoed)
                    {
                        SetRopeLength(arg.A * 0.01f);
                    }
                    return;

                case LassoVerb.ReelOn:
                    _reelHeld = true;
                    return;

                case LassoVerb.ReelOff:
                    _reelHeld = false;
                    return;

                case LassoVerb.StrainOn:
                    _strainHeld = true;
                    return;

                case LassoVerb.StrainOff:
                    _strainHeld = false;
                    return;

                case LassoVerb.Snapped:
                    if (!_isLassoed) return;

                    // Heard at the rope's far end, which is where a rope parts and where the player
                    // is looking when it does.
                    Sfx.Play(snapSound, _ropeEndPoint, GetInstanceID());
                    Release();
                    return;
            }
        }

        /// <summary>Is this the machine that decides what the rope does? Offline, or the server.</summary>
        private static bool IsAuthority => !Network.IsNetworked || Network.Server;

        /// <summary>
        /// May this machine move what is on the end of the rope?
        ///
        /// <para>
        /// Ownership, asked of the TARGET rather than of this item. A loose creature is owned by
        /// the server, a ridden mount by its RIDER, and a prop nobody networked by everyone — and
        /// in each case that is the one machine whose writes to the transform survive. Asking
        /// Simulates, which this used to, put the tether on the server even for a client-ridden
        /// mount, where every write it made was overwritten within a tick.
        /// </para>
        /// </summary>
        private bool OwnsTarget()
        {
            Component target = _targetRb != null ? _targetRb : (Component)_targetTransform;
            return target != null && Network.Owns(target);
        }

        // ── Riding the catch ───────────────────────────────────────────────────

        private IEnumerator RideRoutine()
        {
            Transform root = _targetRb != null ? _targetRb.transform : _targetTransform;

            while (_isLassoed && root != null)
            {
                Vector3 attachWorldPos = root.position + _attachOffset;
                _ropeEndPoint = attachWorldPos;

                Vector3 start = GetRopeStart();

                loop.Ride(attachWorldPos, attachWorldPos - start, Time.deltaTime);
                rope.Simulate(start, attachWorldPos, _currentRopeLength, Time.deltaTime);

                yield return null;
            }

            if (_isLassoed) Release();
        }

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // A roped creature is a relationship between two objects, and it lived entirely in fields on
        // an item instance that is destroyed on every equip. So the creature was freed — and its
        // legs handed back to it — by switching hotbar slot, and by reloading.
        //
        // Deferred, because the target is the whole point. A rope with no creature on the end of it
        // is not worth restoring, so unlike the grapple there is nothing to apply early: the pending
        // reference is kept until the creature turns up, which for one in a chunk that has not
        // streamed in yet may be several passes later.

        private const string TargetKey = "target";
        private const string OffsetKey = "off";
        private const string RopeKey = "rope";

        private SaveRef _pendingTarget;
        private Vector3 _pendingOffset;
        private float _pendingRopeLength;
        private bool _pendingRestore;

        public bool HasPendingRestore => _pendingRestore;

        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state == null || !_isLassoed) return;

            Transform root = _targetRb != null ? _targetRb.transform : _targetTransform;
            if (root == null) return;

            SaveRef target = SaveRef.From(root.gameObject);

            // An unreferenceable target is one the save system has no identity for — a prop nobody
            // opted in. Storing the rope without it would restore a rope attached to nothing.
            if (!target.IsSet) return;

            state.Set(TargetKey, target);
            state.Set(OffsetKey, _attachOffset);
            state.Set(RopeKey, _currentRopeLength);
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);

            _pendingRestore = false;
            _pendingTarget = SaveRef.None;

            if (state == null) return;

            SaveRef target = state.GetRef(TargetKey);
            if (!target.IsSet) return;

            _pendingTarget = target;
            // The fallback is the person-sized authored height, not a measurement: the creature
            // this rope was on may not have streamed in yet, so there is nothing to measure. Attach
            // resolves the real one from the animal's own size the moment it turns up.
            _pendingOffset = state.GetVector3(OffsetKey, Vector3.up * struggle.AttachHeight);
            _pendingRopeLength = state.GetFloat(RopeKey);
            _pendingRestore = true;
        }

        /// <summary>
        /// Put the rope back on the creature, once the creature is here.
        ///
        /// Kept pending on failure and consumed on success: the target may be in a chunk that has
        /// not streamed in yet, and giving up on the first pass would quietly free it.
        /// </summary>
        public void TryCompleteRestore()
        {
            if (!_pendingRestore) return;
            if (_isLassoed || _isThrowing) { _pendingRestore = false; return; }
            if (!_pendingTarget.TryResolve(out GameObject target)) return;

            _pendingRestore = false;

            // Straight to Attach, skipping the throw: the rope was already on the creature, and
            // replaying the arc would give the player a second chance to miss.
            Attach(target.GetComponent<Rigidbody>(), target.transform);

            // After Attach, never before — it writes both of these itself, from the authored
            // defaults and from where the two ends are standing right now. That is the safe answer;
            // the saved pair is the better one, because the player may have reeled the creature
            // most of the way in already.
            _attachOffset = _pendingOffset;
            if (_pendingRopeLength > ropeSlack)
            {
                SetRopeLength(_pendingRopeLength);
            }

            // LAST, and the ordering is load-bearing: SendRope reads _currentRopeLength, so
            // announcing before the two lines above would publish the length Attach guessed from
            // the current gap rather than the one the player actually reeled to — which is the
            // divergence this carries the length to remove.
            //
            // A load is restored on the authority, from a per-slot bag that PlayerInventoryNetwork
            // does not replicate, so without this the rope comes back on the server and on nobody
            // else's screen.
            if (IsAuthority) SendRope(LassoVerb.Caught, target);
        }

        // ── Right-click reel-in ───────────────────────────────────────────────
        //
        // The input is read on ONE machine and the resulting state is published. An
        // InputActionReference is a shared asset, not a per-player one, so every machine in the
        // session holds a copy of every remote player's lasso reading its OWN right mouse button —
        // an ungated read here would have one player's click reel in somebody else's creature.

        private bool _reelHeld;

        private void Update()
        {
            if (OwnerIsLocal()) ReadReelInput();

            // The safety net. A release is one message, and one message is exactly the kind of
            // thing that goes missing — along with the player who was holding the button. See
            // _lastHoldTime, and LaserStaffArtifact, which is where this convention comes from.
            if (_isTwirling && Time.time - _lastHoldTime > holdTimeout) CancelTwirl();

            TickTwirl(Time.deltaTime);
        }

        /// <summary>
        /// Owner-side: watch the button, and publish only the changes.
        ///
        /// An edge rather than a stream, because the reel is a state that lasts seconds and there
        /// is nothing between "pulling" and "not pulling" for a per-tick message to carry.
        /// </summary>
        private void ReadReelInput()
        {
            bool reel = _isLassoed
                        && reelInAction != null
                        && reelInAction.action.ReadValue<float>() >= 0.5f;

            if (reel == _reelHeld) return;

            _reelHeld = reel;
            SendRope(reel ? LassoVerb.ReelOn : LassoVerb.ReelOff, null);
        }

        // ── Rope physics (FixedUpdate) ─────────────────────────────────────────
        //
        // The two ends of the rope belong to two different machines, so this method does its two
        // jobs under two different gates rather than one:
        //
        //   • The CREATURE end is LassoTether's, on the machine that simulates the creature. A
        //     client running it too would be overwritten by the creature's NetworkTransform within
        //     a tick, and while it lasted it would drag that one screen's copy of the animal
        //     sideways.
        //   • The PLAYER end — the weight of the animal on the rope — is applied only by the
        //     machine that owns the thrower. Their body is owner-authoritative, so a push applied
        //     to it on the server is thrown away by their next state update, silently.
        //
        // Neither gate subsumes the other: a client roping a server-owned creature is exactly the
        // case where the two are different machines, and it is the ordinary case in a session.
        private void FixedUpdate()
        {
            if (!_isLassoed || owner == null) return;

            // ── Shorten the rope while reeling ─────────────────────────────────
            // On every machine, from the same start length at the same rate, so the length the
            // creature is constrained by and the length the owner feels tension against agree
            // without a second message. _reelHeld is published, so they are all reeling or none.
            if (_reelHeld)
            {
                SetRopeLength(_currentRopeLength - reelInForce * Time.fixedDeltaTime);
            }

            // ── …and give it back while the far end is taking it ───────────────
            //
            // The rope used to ratchet: reelInForce subtracted and nothing anywhere added, so an
            // animal reeled in once could never run again and the whole contest was over the moment
            // the player first pressed the button. Paying out is what gives the animal a move.
            //
            // Never while reeling. A player pulling and a creature pulling would otherwise net out
            // to a rope that quietly did neither, which is the least legible outcome available.
            else if (_strainHeld)
            {
                SetRopeLength(_currentRopeLength + tension.PayOutSpeed * Time.fixedDeltaTime);
            }

            JudgeTension();
            ApplyOwnerPull();
        }

        /// <summary>
        /// Set the rope's length everywhere it is remembered, inside the line there is.
        ///
        /// Both ends are told rather than left to measure: the length is what the creature's
        /// constraint and the owner's tension are both judged against, and two ends disagreeing
        /// about it is a rope that is taut on one machine and slack on the other.
        /// </summary>
        private void SetRopeLength(float length)
        {
            _currentRopeLength = Mathf.Clamp(length, ropeSlack, tension.MaxLength);
            _tether?.SetRopeLength(_currentRopeLength);
            _caughtPlayer?.SetRopeLength(_currentRopeLength);
        }

        /// <summary>
        /// Wear the rope, and decide when it lets go.
        ///
        /// <para>
        /// <b>One machine judges.</b> The authority measures the strain from the two ends it can
        /// see and publishes the verdict as an edge, exactly as the reel already is. Every machine
        /// measuring its own would have each of them paying line out against its own interpolated
        /// copy of two moving objects, and the ropes would be different lengths within seconds —
        /// permanently, because the length is what the break is then measured against.
        /// </para>
        /// <para>
        /// The FOV kick is the exception and is deliberately local: it is a thing shown to one
        /// player about the rope in their own hands, so it runs wherever that player is and is
        /// driven by the published strain rather than by a measurement of its own.
        /// </para>
        /// </summary>
        private void JudgeTension()
        {
            ApplyFovKick();

            if (!IsAuthority) return;

            Transform root = _targetRb != null ? _targetRb.transform : _targetTransform;
            if (root == null) return;

            float overshoot = Vector3.Distance(root.position + _attachOffset, GetRopeStart()) - _currentRopeLength;
            float strain = LassoTension.Strain01(overshoot, tension.FullStrainOvershoot);

            _wear = LassoTension.Wear(_wear, strain, Time.fixedDeltaTime,
                                      tension.BreakSeconds, tension.RecoverySeconds);

            if (_wear >= 1f)
            {
                SendRope(LassoVerb.Snapped, null);
                return;
            }

            // Published as an edge, like the reel. A per-tick strain value would be a message every
            // fixed step for the whole time anything is roped.
            bool straining = strain > 0f;
            if (straining == _strainHeld) return;

            _strainHeld = straining;
            SendRope(straining ? LassoVerb.StrainOn : LassoVerb.StrainOff, null);
        }

        /// <summary>
        /// Widen this player's view while their rope is loaded, and put it back when it is not.
        ///
        /// <para>
        /// A rope at its limit looks exactly like a rope, so the geometry alone cannot tell anyone
        /// they are about to lose their catch. This is the same channel the grappling hook uses to
        /// sell speed and the same reason (<c>GDC-L1-FEEL-0004</c> — the event that matters gets a
        /// second sense), and it is an OFFSET that eases both ways, so setting it every step and
        /// zeroing it on release is the whole contract.
        /// </para>
        /// </summary>
        private void ApplyFovKick()
        {
            if (!OwnerIsLocal()) return;

            PlayerLook look = owner != null ? owner.GetComponentInChildren<PlayerLook>() : null;
            if (look == null) return;

            look.SetFovOffset(_strainHeld ? tautFovKick * Mathf.Clamp01(_wear + 0.35f) : 0f);
        }

        /// <summary>Put a lifted field of view back. Reached from Release, which runs on teardown.</summary>
        private void ClearFovKick()
        {
            if (!OwnerIsLocal()) return;

            PlayerLook look = owner != null ? owner.GetComponentInChildren<PlayerLook>() : null;
            look?.SetFovOffset(0f);
        }

        /// <summary>
        /// The animal's weight on the player's end of the rope — dallying.
        ///
        /// <para>
        /// What decides how much is <see cref="PlayerPullShare"/>: the same number the creature's
        /// end uses to work out how much it must give, computed rather than sent, because these
        /// two lines of code run on two different computers.
        /// </para>
        /// <para>
        /// Runs AFTER <c>PlayerMovement.FixedUpdate</c>, and that is load-bearing. While the player
        /// is grounded that method does not blend their velocity, it ASSIGNS it — so any push
        /// another system applies beforehand is deleted outright rather than merely damped, and
        /// this drag simply would not exist.
        /// </para>
        /// </summary>
        private void ApplyOwnerPull()
        {
            Transform root = _targetRb != null ? _targetRb.transform : _targetTransform;
            if (root == null || !Network.Owns(owner.transform)) return;

            Vector3 ropeStart = GetRopeStart();
            Vector3 attachWorld = root.position + _attachOffset;
            Vector3 toTarget = attachWorld - ropeStart;
            float distance = toTarget.magnitude;

            // A roped player weighs what every player weighs — see AssumedPlayerMass. Taken from
            // the constant rather than from their Rigidbody so that the thrower's machine and the
            // victim's reach the same split without exchanging a number.
            float targetMass = _tether != null ? _tether.Mass
                             : _caughtPlayer != null ? AssumedPlayerMass
                             : _targetRb != null ? _targetRb.mass
                             : AssumedPlayerMass;

            if (distance <= _currentRopeLength) return;

            Rigidbody ownerRb = owner.GetComponent<Rigidbody>();
            if (ownerRb == null || ownerRb.isKinematic) return;

            Vector3 radial = distance > 0.001f ? toTarget / distance : Vector3.up;
            float share = PlayerPullShare(targetMass);

            // How far past the rope's length the animal has got, as the speed that would close it
            // this step — capped, because one frame where the creature is far outside the rope
            // (a teleport, a chunk load, a physics hitch) is otherwise converted into a launch.
            float overshoot = distance - _currentRopeLength;
            float tow = Mathf.Min(overshoot / Mathf.Max(Time.fixedDeltaTime, 0.0001f), maxDragSpeed) * share;

            // SET the radial component up to the tow speed; never add to it.
            //
            // This ran as `linearVelocity += radial * tow` and got away with it only by accident:
            // PlayerMovement.FixedUpdate ASSIGNS linearVelocity outright while grounded, so every
            // addition made before it ran was deleted rather than reduced, and the drag survived
            // only when SetTethered happened to be on. Ordering this component after it (see the
            // class attribute) makes the additions land — and an addition that lands every step at
            // 50 Hz is not a drag, it is a rocket.
            //
            // Only ever upward, too: this is a rope pulling, so it may tow a player who is slower
            // than the pull and must never brake one who is already faster.
            float current = Vector3.Dot(ownerRb.linearVelocity, radial);
            if (tow > current) ownerRb.linearVelocity += radial * (tow - current);
        }

        // Deliberately NOT PlayerMovement.SetTethered, which is how this used to make the drag
        // survive.
        //
        // That flag hands the whole body to the rope, the way the grappling hook does for a swing —
        // and it suppresses fall damage for as long as it is set. A player roping the heaviest
        // animal they can find and then walking off a cliff would take none, which turns the lasso
        // into a parachute. The leash rework hit the same trap from the other side and the call
        // there was the same: a rope is not a way to get around.
        //
        // Ordering after PlayerMovement (see [DefaultExecutionOrder] on the class) gets the drag to
        // land without any of that, because the ground lerp has already assigned its velocity by
        // the time this adds to it.

        // ── Geometry ───────────────────────────────────────────────────────────

        private Vector3 GetRopeStart()
        {
            if (muzzle != null) return muzzle.position;
            return owner != null ? owner.transform.position : transform.position;
        }

        /// <summary>
        /// Room for one frame's worth of candidates. Reused rather than allocated per frame: the
        /// arc asks this question on every frame of every throw, and the old
        /// <c>Physics.OverlapSphere</c> handed back a fresh array each time.
        /// </summary>
        private static readonly Collider[] LatchCandidates = new Collider[32];

        /// <summary>The sweep's own buffer. Same reasoning as <see cref="LatchCandidates"/>.</summary>
        private static readonly RaycastHit[] SweepHits = new RaycastHit[32];

        /// <summary>
        /// The best ropeable target the loop passed through between two frames.
        ///
        /// <para>
        /// <b>Swept, not sampled.</b> This took a single <c>OverlapSphere</c> at the head's current
        /// position, which is a point sample of a thing moving up to 22 m/s — a third of a metre
        /// per frame at 60 Hz, against a mouth that is 0.22 m across on an uncharged flick. A thin
        /// target could pass clean between two frames' spheres and be reported a miss. The tell was
        /// in this same file: <c>Miss</c> already used a <c>Linecast</c> to find the ground,
        /// because a point sample was obviously not good enough to catch a whole planet with.
        /// </para>
        /// <para>
        /// The radius is <see cref="LassoLoop.Radius"/> rather than a serialized number, so what
        /// the player sees the rope pass through and what the rope can catch are the same circle. A
        /// fully wound throw genuinely has a wider mouth than a flicked one — and the aim guide
        /// draws that same circle where the throw will land.
        /// </para>
        /// <para>
        /// Prefers Rigidbody targets; falls back to any collider whose root has an AgentController.
        /// </para>
        /// </summary>
        private bool TryGetLatchTarget(Vector3 from, Vector3 to, out Rigidbody rb,
                                       out Transform hitTransform, out Vector3 latchPoint)
        {
            rb = null;
            hitTransform = null;
            latchPoint = to;

            float radius = Mathf.Max(loop.Radius, 0.2f);
            Vector3 step = to - from;
            float distance = step.magnitude;

            // The mask goes INTO the query rather than being checked afterwards. Filtering in the
            // loop made the physics engine gather every collider on every layer first, then threw
            // most of them away — and on a busy frame that is what overflowed the buffer.
            int count = distance > 1e-4f
                ? Physics.SphereCastNonAlloc(from, radius, step / distance, SweepHits, distance,
                                             hookableLayers, QueryTriggerInteraction.Ignore)
                : 0;

            float bestDist = float.MaxValue;

            for (int i = 0; i < count; i++)
                TryScoreCandidate(SweepHits[i].collider, to, ref bestDist, ref rb, ref hitTransform, ref latchPoint);

            // The destination is tested as an overlap as well, and not for symmetry: a SphereCast
            // that STARTS inside a collider reports it at distance zero with a meaningless contact
            // point, which is exactly the case of a loop arriving on top of an animal. Scoring off
            // ClosestPoint rather than the hit keeps that usable, and the overlap covers a
            // stationary head that the sweep skips entirely.
            int overlaps = Physics.OverlapSphereNonAlloc(to, radius, LatchCandidates, hookableLayers,
                                                         QueryTriggerInteraction.Ignore);

            for (int i = 0; i < overlaps; i++)
                TryScoreCandidate(LatchCandidates[i], to, ref bestDist, ref rb, ref hitTransform, ref latchPoint);

            return hitTransform != null || rb != null;
        }

        /// <summary>
        /// Keep <paramref name="col"/> if it is ropeable and nearer than the best so far.
        ///
        /// Split out because the sweep and the end-of-step overlap both have to judge a candidate
        /// the same way, and two copies of this would eventually disagree about what a lasso can
        /// catch.
        /// </summary>
        private bool TryScoreCandidate(Collider col, Vector3 headPos, ref float bestDist,
                                       ref Rigidbody rb, ref Transform hitTransform, ref Vector3 latchPoint)
        {
            if (col == null) return false;
            if (col.transform.IsChildOf(owner.transform)) return false;

            float d = Vector3.Distance(headPos, col.ClosestPoint(headPos));
            if (d >= bestDist) return false;

            Rigidbody candidateRb = col.GetComponentInParent<Rigidbody>();
            AgentController candidateAgent = col.GetComponentInParent<AgentController>();

            if (candidateRb == null && candidateAgent == null) return false;

            bestDist = d;
            rb = candidateRb;
            hitTransform = candidateAgent != null ? candidateAgent.transform : candidateRb.transform;
            latchPoint = col.ClosestPoint(headPos);
            return true;
        }
    }
}
