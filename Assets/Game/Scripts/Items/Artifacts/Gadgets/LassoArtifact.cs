using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Agents;
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

        [Tooltip("Metres above the player's ROOT that the loop is spun while winding up.\n\n" +
                 "The capsule is 2 m tall centred on the root, so the top of the head is at 1.0 — " +
                 "anything near that value puts the loop on the player's ear. A full arm's length " +
                 "clear of that is what reads as a lasso being wound.")]
        [SerializeField] private float twirlHeight = 2.1f;

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
        [SerializeField] private float npcAttachHeightOffset = 1.2f;  // world-units above NPC root to attach

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

        [Header("Throw Arc")]
        [SerializeField] private float throwArcHeight = 4f;
        [SerializeField] private float throwGravity = 18f;

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
        private Rigidbody _targetRb;
        private Transform _targetTransform;   // used when target has no Rigidbody
        private LassoTether _tether;
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

            float reach = Mathf.Lerp(minThrowRange, maxRange, _twirlCharge);

            Ray aimRay = aimProvider.GetAimRay();
            Vector3 targetPoint = aimRay.origin + aimRay.direction * reach;

            if (Physics.Raycast(aimRay, out RaycastHit aimHit, reach, ~0, QueryTriggerInteraction.Ignore))
                targetPoint = aimHit.point;

            arg.P = targetPoint;
            arg.R = Quaternion.LookRotation(aimRay.direction);
        }

        /// <summary>
        /// Nothing. Both halves of this item are a rope being drawn and a creature being pulled,
        /// and both live where they can be seen — see the class summary.
        /// </summary>
        protected override void Use() { }

        // ── Every machine: the wind-up ─────────────────────────────────────────

        protected override void Present()
        {
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

            if (lassoModel != null) lassoModel.SetActive(false);

            rope.Bind(lineRenderer);
            loop.Bind(loopRenderer);
            loop.Show();

            Vector3 centre = TwirlCentre();
            rope.Show(GetRopeStart(), centre);
        }

        /// <summary>Put the rope away without throwing it. Unequip, death, or a hotbar scroll.</summary>
        private void CancelTwirl()
        {
            _isTwirling = false;
            _twirlCharge = 0f;

            rope.Hide();
            loop.Hide();
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
        private Vector3 TwirlCentre() =>
            (owner != null ? owner.transform.position : transform.position) + Vector3.up * twirlHeight;

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
        }

        // ── Throw sequence ─────────────────────────────────────────────────────

        private IEnumerator ThrowRoutine(Vector3 targetPoint, Vector3 aimDirection)
        {
            _isThrowing = true;

            if (lassoModel != null) lassoModel.SetActive(false);

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

            Vector3 delta = targetPoint - start;
            Vector3 flatDelta = new Vector3(delta.x, 0f, delta.z);
            float flatDist = Mathf.Max(flatDelta.magnitude, 0.01f);
            float timeToTarget = flatDist / throwSpeed;
            float vy = (delta.y / timeToTarget) + 0.5f * throwGravity * timeToTarget + throwArcHeight;
            Vector3 velocity = flatDelta.normalized * throwSpeed + Vector3.up * vy;

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
                    TryGetLatchTarget(headPos, out Rigidbody latchedRb, out Transform latchedTransform, out Vector3 latchPoint))
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

            _attachOffset = Vector3.up * npcAttachHeightOffset;

            Vector3 attachWorldPos = root.position + _attachOffset;
            _currentRopeLength = Vector3.Distance(GetRopeStart(), attachWorldPos) + ropeSlack;
            _ropeEndPoint = attachWorldPos;

            // Taking a creature's legs off its AI and driving them is a change to the creature, not
            // to the rope, so it belongs only on the machine that simulates it. On a peer the
            // replica is kinematic on purpose — NetworkRigidbody makes it so — and a second tether
            // there would be a second authority fighting the NetworkTransform.
            if (SimulatesTarget())
            {
                _tether = LassoTether.Ensure(root.gameObject);
                _tether.Bind(muzzle != null ? muzzle : owner.transform, _currentRopeLength, struggle);
            }

            rope.Bind(lineRenderer);
            loop.Bind(loopRenderer);

            rope.Show(GetRopeStart(), attachWorldPos);
            loop.Show();
            loop.BeginCinch();
            rope.Snap();

            _routine = StartCoroutine(RideRoutine());
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
            _twirlCharge = 0f;

            StopRoutine();

            // Only ever non-null on the machine that took the creature's legs — see Attach — so
            // this hands navigation back exactly where it was taken away.
            if (_tether != null) { _tether.Release(); _tether = null; }

            _targetRb        = null;
            _targetTransform = null;

            rope.Hide();
            loop.Hide();
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
        }

        private void Listen(Transform channel)
        {
            if (_channel == channel) return;

            if (_channel != null)
            {
                _channel.NetOff(NetMsg.LassoRope, OnRopeRequested);
                _channel.NetOff(NetMsg.LassoRoped, OnRopeAnnounced);
            }

            _channel = channel;
            if (_channel == null) return;

            _channel.NetOn(NetMsg.LassoRope, OnRopeRequested);
            _channel.NetOn(NetMsg.LassoRoped, OnRopeAnnounced);
        }

        /// <summary>Owner-side: tell the session what the rope just did.</summary>
        private void SendRope(int verb, GameObject subject)
        {
            if (owner == null) return;

            NetMessaging.NetSendTo(owner, NetMsg.LassoRope,
                new NetArg { B = verb }.With(subject), NetTo.Server);
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
                    return;

                case LassoVerb.ReelOn:
                    _reelHeld = true;
                    return;

                case LassoVerb.ReelOff:
                    _reelHeld = false;
                    return;
            }
        }

        /// <summary>True when the local player is the one holding this lasso.</summary>
        private bool OwnerIsLocal()
        {
            if (!Network.IsNetworked) return true;

            if (owner != null && owner.TryGetComponent(out NetworkObject netObj) && netObj.IsSpawned)
                return netObj.IsOwner;

            return true;
        }

        /// <summary>Is this the machine that decides what the rope does? Offline, or the server.</summary>
        private static bool IsAuthority => !Network.IsNetworked || Network.Server;

        /// <summary>
        /// May this machine move what is on the end of the rope?
        ///
        /// Asked of the TARGET, not of this item: a networked creature answers "the server only",
        /// while a prop nobody networked answers "yes" everywhere — which is right, because every
        /// machine then has its own unshared copy of it to move.
        /// </summary>
        private bool SimulatesTarget()
        {
            Component target = _targetRb != null ? _targetRb : (Component)_targetTransform;
            return target != null && Network.Simulates(target);
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
            _pendingOffset = state.GetVector3(OffsetKey, Vector3.up * npcAttachHeightOffset);
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

            // A load is restored on the authority, from a per-slot bag that PlayerInventoryNetwork
            // does not replicate — so without this the rope comes back on the server and on nobody
            // else's screen. Announced rather than re-derived for the same reason a catch is.
            if (IsAuthority) SendRope(LassoVerb.Caught, target);

            // After Attach, never before — it writes both of these itself, from the authored
            // defaults and from where the two ends are standing right now. That is the safe answer;
            // the saved pair is the better one, because the player may have reeled the creature
            // most of the way in already.
            _attachOffset = _pendingOffset;
            if (_pendingRopeLength > ropeSlack)
            {
                _currentRopeLength = _pendingRopeLength;
                _tether?.SetRopeLength(_currentRopeLength);
            }
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
                _currentRopeLength = Mathf.Max(ropeSlack, _currentRopeLength - reelInForce * Time.fixedDeltaTime);
                _tether?.SetRopeLength(_currentRopeLength);
            }

            ApplyOwnerPull();
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

            float targetMass = _tether != null ? _tether.Mass
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
        /// The best ropeable target within the loop's mouth.
        ///
        /// The radius is <see cref="LassoLoop.Radius"/> rather than a serialized number, so what the
        /// player sees the rope pass through and what the rope can catch are the same circle. A
        /// fully wound throw genuinely has a wider mouth than a flicked one.
        ///
        /// Prefers Rigidbody targets; falls back to any collider whose root has an AgentController.
        /// </summary>
        private bool TryGetLatchTarget(Vector3 headPos, out Rigidbody rb, out Transform hitTransform, out Vector3 latchPoint)
        {
            rb = null;
            hitTransform = null;
            latchPoint = headPos;

            float radius = Mathf.Max(loop.Radius, 0.2f);
            Collider[] nearby = Physics.OverlapSphere(headPos, radius, ~0, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;

            foreach (Collider col in nearby)
            {
                if (col == null) continue;
                if (col.transform.IsChildOf(owner.transform)) continue;
                if ((hookableLayers.value & (1 << col.gameObject.layer)) == 0) continue;

                float d = Vector3.Distance(headPos, col.ClosestPoint(headPos));
                if (d >= bestDist) continue;

                Rigidbody candidateRb = col.GetComponentInParent<Rigidbody>();
                AgentController candidateAgent = col.GetComponentInParent<AgentController>();

                if (candidateRb == null && candidateAgent == null) continue;

                bestDist = d;
                rb = candidateRb;
                hitTransform = candidateAgent != null ? candidateAgent.transform : candidateRb.transform;
                latchPoint = col.ClosestPoint(headPos);
            }

            return hitTransform != null || rb != null;
        }
    }
}
