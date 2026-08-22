using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;

namespace SpaceGame.Items
{
    /// <summary>
    /// Lasso artifact — extends ToolItem.
    ///
    /// First press  → plays a throw animation, then visibly throws the lasso forward.
    ///                If the traveling lasso head hits a valid rigidbody target, it
    ///                attaches a rope to the NPC's upper body.
    ///                Rope is tension-only: free when slack, pulls only when taut.
    /// Second press → releases the lasso, restores NavMesh on the target.
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
    /// It now splits three ways, and each part sits on the machine that can actually answer for it:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>The aim</b> — <see cref="OnRequestUse"/>, owner-side, because that is the one machine
    /// holding a camera. The point being thrown at travels in the message so that every machine
    /// throws the same arc instead of each guessing its own.
    /// </description></item>
    /// <item><description>
    /// <b>The arc, the rope and the loop</b> — <see cref="Present"/>, on every machine, so a peer
    /// sees a rope rather than a creature being pushed by nothing.
    /// </description></item>
    /// <item><description>
    /// <b>The catch and the pull</b> — the thrower decides WHAT was caught (see
    /// <see cref="NetMsg.LassoRope"/> for why that cannot be re-derived per machine), and the
    /// machine that simulates the roped creature is the only one that moves it.
    /// </description></item>
    /// </list>
    /// </summary>
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

        [Header("Firing")]
        [SerializeField] private float maxRange = 40f;
        [SerializeField] private LayerMask hookableLayers = ~0;
        [SerializeField] private float throwDelay = 0.3f;
        [SerializeField] private float throwSpeed = 30f;
        [SerializeField] private float throwRadius = 1.2f;      // generous — easy to snatch NPCs
        [SerializeField] private float reelSpeed = 18f;          // units/sec the rope pulls back on miss

        [Header("Rope / Joint")]
        [SerializeField] private float ropeSlack = 2f;
        [SerializeField] private float ropeTension = 600f;
        [SerializeField] private float reelInForce = 18f;   // units/sec speed when pulling target in
        [SerializeField] private InputActionReference reelInAction;   // assign RightClick in Inspector      // force applied when rope is taut
        [SerializeField] private float npcAttachHeightOffset = 1.2f; // world-units above NPC root to attach

        [Header("Animation")]
        [SerializeField] private string throwTrigger = "Throw";
        [SerializeField] private GameObject lassoModel;   // the held dummy mesh — hidden while rope is out

        [Header("Rope Visual")]
        [SerializeField] private LineRenderer lineRenderer;
        [SerializeField] private Transform muzzle;
        [SerializeField] private int ropeSegments = 20;
        [SerializeField] private float ropeGravity = 3f;
        [SerializeField] private float ropeWidth = 0.04f;

        [Header("Lasso Loop")]
        [SerializeField] private LineRenderer loopRenderer;
        [SerializeField] private int loopSegments = 24;
        [SerializeField] private float loopRadius = 0.35f;
        [SerializeField] private float loopSpinSpeed = 360f;
        [SerializeField] private float loopTiltAngle = 60f;
        [SerializeField] private float loopDistortAmount = 0.12f;  // max radius deviation
        [SerializeField] private float loopDistortSpeed = 1.8f;    // how fast the distortion drifts

        [Header("Throw Arc")]
        [SerializeField] private float throwArcHeight = 4f;
        [SerializeField] private float throwGravity = 18f;

        [Header("Rope Wobble")]
        [SerializeField] private float wobbleAmplitude = 0.3f;
        [SerializeField] private float wobbleFrequency = 2.5f;
        [SerializeField] private float wobbleDecay = 2f;

        // ── Runtime state ──────────────────────────────────────────────────────
        private bool _isLassoed;
        private bool _isThrowing;
        private Rigidbody _targetRb;
        private Transform _targetTransform;   // used when target has no Rigidbody
        private NavMeshAgent _targetAgent;
        private AgentController _targetAgentController;
        private float _currentRopeLength;
        private Coroutine _routine;
        private Vector3 _ropeEndPoint;   // world-space point drawn as rope tip (chest height)
        private Vector3 _attachOffset;   // local offset on NPC body where rope attaches
        private float _loopSpinCurrent;  // actual spin speed, wound down after attach

        // Wobble state
        private float _wobbleTime;
        private float _wobbleStrength;
        private Vector3 _wobbleAxis;

        // Loop spin state
        private float _loopAngle;
        private float _loopSpinDecay = 180f; // deg/sec² wind-down rate after attach

        // ── Wire verbs, in NetArg.B of the use message ─────────────────────────
        //
        // A is already the hotbar slot, so B it is — the same convention the grapple uses.

        private const int MissVerb = 0;
        private const int ThrowVerb = 1;
        private const int ReleaseVerb = 2;

        // ── Owner side: describe the press ─────────────────────────────────────

        /// <summary>
        /// Owner-side: settle what this press means and, if it is a throw, where it is thrown.
        ///
        /// The raycast belongs here rather than inside the coroutine for two reasons. It is the
        /// only moment the aim is honest — this is the machine holding the camera, and a peer's
        /// copy of a remote player has an <see cref="AimProvider"/> with a dead camera behind it —
        /// and resolving it once means every machine throws at the same point instead of each
        /// picking its own.
        ///
        /// The cost is that the aim is now sampled when the button goes down rather than
        /// <see cref="throwDelay"/> seconds later, at the end of the wind-up. That is a change in
        /// feel, and the better one: the rope goes where you were looking when you threw it.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            base.OnRequestUse(ref arg);

            // Rope already out, or one already in the air: this press lets go, and there is
            // nothing to aim at.
            if (_isLassoed || _isThrowing)
            {
                arg.B = ReleaseVerb;
                return;
            }

            arg.B = MissVerb;

            // Present() is deliberately not gated on CanUse — see UsableItem — so a depleted lasso
            // has to refuse here, on the one machine that can still tell the difference.
            if (!CanUse()) return;
            if (aimProvider == null) return;

            Ray aimRay = aimProvider.GetAimRay();
            Vector3 targetPoint = aimRay.origin + aimRay.direction * maxRange;

            if (Physics.Raycast(aimRay, out RaycastHit aimHit, maxRange, ~0, QueryTriggerInteraction.Ignore))
                targetPoint = aimHit.point;

            arg.B = ThrowVerb;
            arg.P = targetPoint;
            arg.R = Quaternion.LookRotation(aimRay.direction);
        }

        /// <summary>
        /// Nothing. Both halves of this item are a rope being drawn and a creature being pulled,
        /// and both live where they can be seen — see the class summary.
        /// </summary>
        protected override void Use() { }

        // ── Every machine: the throw ───────────────────────────────────────────

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
            if (_isLassoed || _isThrowing) return;
            if (owner == null) return;

            _routine = StartCoroutine(ThrowRoutine(UseArg.P, UseArg.R * Vector3.forward));
        }

        protected override bool CanUse()
        {
            return base.CanUse() || _isLassoed;
        }

        // ── Throw sequence ─────────────────────────────────────────────────────

        private IEnumerator ThrowRoutine(Vector3 targetPoint, Vector3 aimDirection)
        {
            _isThrowing = true;

            if (lassoModel != null) lassoModel.SetActive(false);

            var animator = owner.GetComponentInChildren<Animator>();
            if (animator != null)
                animator.SetTrigger(throwTrigger);

            yield return new WaitForSeconds(throwDelay);

            Vector3 start = GetRopeStart();

            _wobbleStrength = 1f;
            _wobbleTime = 0f;
            _wobbleAxis = Vector3.Cross(aimDirection, Vector3.up).normalized;
            if (_wobbleAxis.sqrMagnitude < 0.01f)
                _wobbleAxis = Vector3.right;

            EnableRope();
            EnableLoop();

            Vector3 delta = targetPoint - start;
            Vector3 flatDelta = new Vector3(delta.x, 0f, delta.z);
            float flatDist = Mathf.Max(flatDelta.magnitude, 0.01f);
            float timeToTarget = flatDist / throwSpeed;

            float vy = (delta.y / timeToTarget) + 0.5f * throwGravity * timeToTarget + throwArcHeight;
            Vector3 velocity = flatDelta.normalized * throwSpeed + Vector3.up * vy;

            Vector3 headPos = start;
            Vector3 prevHeadPos = start;
            _ropeEndPoint = start;

            float estimatedFlightTime = timeToTarget * 1.5f;
            float elapsed = 0f;

            while (true)
            {
                elapsed += Time.deltaTime;
                velocity += Vector3.down * throwGravity * Time.deltaTime;

                prevHeadPos = headPos;
                headPos += velocity * Time.deltaTime;

                Vector3 stepDir = headPos - prevHeadPos;
                float stepDist = stepDir.magnitude;
                Vector3 stepDirNorm = stepDist > 0.001f ? stepDir / stepDist : velocity.normalized;

                _loopAngle += loopSpinSpeed * Time.deltaTime;
                _wobbleTime += Time.deltaTime;

                float progress = Mathf.Clamp01(elapsed / estimatedFlightTime);

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
                    UpdateRope(progress);
                    UpdateLoop(_ropeEndPoint, stepDirNorm);

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
                UpdateRope(progress);
                UpdateLoop(headPos, stepDirNorm);

                if (pastTarget || tooFar)
                {
                    // Let the head continue falling under gravity until it hits the ground
                    while (true)
                    {
                        velocity += Vector3.down * throwGravity * Time.deltaTime;
                        prevHeadPos = headPos;
                        headPos += velocity * Time.deltaTime;

                        _loopAngle += loopSpinSpeed * Time.deltaTime;
                        _wobbleTime += Time.deltaTime;

                        Vector3 stepDir2 = headPos - prevHeadPos;
                        Vector3 stepDirNorm2 = stepDir2.magnitude > 0.001f ? stepDir2.normalized : velocity.normalized;

                        bool landed = Physics.Linecast(prevHeadPos, headPos, out RaycastHit groundHit, ~0, QueryTriggerInteraction.Ignore)
                                      && !groundHit.collider.transform.IsChildOf(owner.transform);
                        if (landed)
                            headPos = groundHit.point;

                        _ropeEndPoint = headPos;
                        UpdateRope(1f);
                        UpdateLoop(headPos, stepDirNorm2);

                        if (landed) break;

                        if (headPos.y < start.y - maxRange)
                            break;

                        yield return null;
                    }

                    // Reel the rope end back toward the muzzle
                    Vector3 reelStart = _ropeEndPoint;
                    float reelDist = Vector3.Distance(reelStart, GetRopeStart());
                    float reelElapsed = 0f;
                    float reelDuration = reelDist / Mathf.Max(reelSpeed, 0.1f);

                    while (reelElapsed < reelDuration)
                    {
                        reelElapsed += Time.deltaTime;
                        float t = Mathf.Clamp01(reelElapsed / reelDuration);
                        _ropeEndPoint = Vector3.Lerp(reelStart, GetRopeStart(), t);
                        _loopAngle += loopSpinSpeed * Time.deltaTime;
                        _wobbleTime += Time.deltaTime;
                        UpdateRope(1f - t);
                        UpdateLoop(_ropeEndPoint, (GetRopeStart() - _ropeEndPoint).normalized);
                        yield return null;
                    }

                    DisableRope();
                    DisableLoop();
                    if (lassoModel != null) lassoModel.SetActive(true);
                    _isThrowing = false;
                    yield break;
                }

                yield return null;
            }
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
            _isLassoed       = true;
            _loopSpinCurrent = loopSpinSpeed;

            if (lassoModel != null) lassoModel.SetActive(false);

            Transform root = targetRb != null ? targetRb.transform : targetTransform;

            _targetAgentController = root.GetComponentInParent<AgentController>();

            // Taking a creature's navigation away and waking its body are changes to the creature,
            // not to the rope, so they belong only on the machine that simulates it. On a peer the
            // replica is kinematic on purpose — NetworkRigidbody makes it so — and clearing that
            // flag here would leave a body fighting its own NetworkTransform, while switching off
            // an agent this machine never drives would do nothing but make the restore wrong.
            if (SimulatesTarget())
            {
                _targetAgent = root.GetComponentInParent<NavMeshAgent>();
                if (_targetAgent != null) _targetAgent.enabled = false;

                if (targetRb != null) targetRb.isKinematic = false;
            }

            _attachOffset = Vector3.up * npcAttachHeightOffset;

            Vector3 attachWorldPos = root.position + _attachOffset;
            _currentRopeLength = Vector3.Distance(GetRopeStart(), attachWorldPos) + ropeSlack;
            _ropeEndPoint = attachWorldPos;

            _wobbleStrength = 1f;
            _wobbleTime     = 0f;
            _wobbleAxis = Vector3.Cross((root.position - owner.transform.position).normalized, Vector3.up).normalized;
            if (_wobbleAxis.sqrMagnitude < 0.01f) _wobbleAxis = Vector3.right;

            EnableRope();
            EnableLoop();
            _routine = StartCoroutine(ReelRoutine());
        }

        /// <summary>
        /// Drop everything. Safe to call from anywhere, including twice, and on a machine that
        /// never had a rope out — a press that missed presents a Release with nothing to release.
        /// </summary>
        private void Release()
        {
            _isLassoed = false;
            _isThrowing = false;
            _reelHeld = false;

            StopRoutine();

            // Only ever non-null on the machine that switched it off — see Attach — so this hands
            // navigation back exactly where it was taken away.
            if (_targetAgent != null) { _targetAgent.enabled = true; _targetAgent = null; }
            _targetAgentController = null;

            _targetRb        = null;
            _targetTransform = null;
            DisableRope();
            DisableLoop();
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

            // Every machine equips from the replicated hotbar, so every machine — the thrower's,
            // the server's and every peer's — gets its own instance and its own registration.
            Listen(holder != null ? holder.transform : null);
        }

        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);

            // Putting the lasso away drops the rope. Without this the creature keeps the
            // NavMeshAgent this item switched off, on whichever machine switched it off, forever —
            // and on the server that is a permanently frozen animal that no player can see a rope
            // on. The slot's saved bag is written BEFORE OnUnequipped (see EquipmentController),
            // so re-equipping still restores the rope.
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

        // ── Reel-in loop ───────────────────────────────────────────────────────

        private IEnumerator ReelRoutine()
        {
            Transform root = _targetRb != null ? _targetRb.transform : _targetTransform;

            while (_isLassoed && root != null)
            {
                _wobbleTime      += Time.deltaTime;
                _wobbleStrength   = Mathf.Max(0f, _wobbleStrength - wobbleDecay * Time.deltaTime);
                _loopSpinCurrent  = Mathf.Max(0f, _loopSpinCurrent - _loopSpinDecay * Time.deltaTime);
                _loopAngle       += _loopSpinCurrent * Time.deltaTime;

                Vector3 attachWorldPos = root.position + _attachOffset;
                _ropeEndPoint = attachWorldPos;

                UpdateRope(1f);
                UpdateLoop(attachWorldPos, (attachWorldPos - GetRopeStart()).normalized);

                yield return null;
            }

            if (_isLassoed) Release();
        }

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // A roped creature is a relationship between two objects, and it lived entirely in fields on
        // an item instance that is destroyed on every equip. So the creature was freed — and its
        // NavMeshAgent handed back to it — by switching hotbar slot, and by reloading.
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
            if (_pendingRopeLength > ropeSlack) _currentRopeLength = _pendingRopeLength;
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

            ReelTransformTarget(Time.deltaTime);
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

        /// <summary>
        /// Transform-only targets (e.g. the ant) — moved directly, with no physics to constrain.
        ///
        /// Runs where the target is simulated, which for anything networked is the server. Called
        /// from the owner's Update and from the server's, so a client reeling a creature in still
        /// moves it on the one machine whose answer replicates.
        /// </summary>
        private void ReelTransformTarget(float deltaTime)
        {
            if (!_reelHeld || !_isLassoed) return;
            if (_targetRb != null || _targetTransform == null) return;
            if (!SimulatesTarget()) return;

            _targetTransform.position = Vector3.MoveTowards(
                _targetTransform.position,
                GetRopeStart() - _attachOffset,
                reelInForce * deltaTime);
        }

        // ── Rope physics (FixedUpdate) ─────────────────────────────────────────
        //
        // Pendulum-style swinging rope:
        //   1. Hard constraint — strip any velocity component that would lengthen the rope
        //      beyond _currentRopeLength (inextensible rope, no spring bounce).
        //   2. Reel-in — when right-click held, shorten _currentRopeLength and remove the
        //      radial velocity component so the target swings inward rather than flying straight.
        //   3. Gravity acts freely every frame — the target arcs downward as it swings.

        // The two ends of the rope belong to two different machines, so this method does its two
        // jobs under two different gates rather than one:
        //
        //   • The CREATURE end is moved only where the creature is simulated — the server, for
        //     anything networked. A client running it too would be overwritten by the creature's
        //     NetworkTransform within a tick, and while it lasted it would drag that one screen's
        //     copy of the animal sideways.
        //   • The PLAYER end — the tug of weight on the rope — is applied only by the machine that
        //     owns the thrower. Their body is owner-authoritative, so a push applied to it on the
        //     server is thrown away by their next state update, silently.
        //
        // Neither gate subsumes the other: a client roping a server-owned creature is exactly the
        // case where the two are different machines, and it is the ordinary case in a session.
        private void FixedUpdate()
        {
            if (!_isLassoed || _targetRb == null || owner == null) return;

            Vector3 ropeStart   = GetRopeStart();
            Vector3 attachWorld = _targetRb.position + _attachOffset;
            Vector3 toTarget    = attachWorld - ropeStart;          // rope vector (anchor → target)
            float   dist        = toTarget.magnitude;
            Vector3 radial      = dist > 0.001f ? toTarget / dist : Vector3.up;  // unit rope direction

            // ── Shorten rope when reeling ──────────────────────────────────────
            // On every machine, from the same start length at the same rate, so the length the
            // server constrains by and the length the owner feels tension against agree without a
            // second message. _reelHeld is published, so they are all reeling or none are.
            if (_reelHeld)
                _currentRopeLength = Mathf.Max(ropeSlack, _currentRopeLength - reelInForce * Time.fixedDeltaTime);

            // ── Inextensible constraint ────────────────────────────────────────
            // If the target is beyond the rope length, cancel the outward radial velocity
            // and push the target back to the rope surface. No spring — hard constraint.
            if (dist <= _currentRopeLength) return;

            float radialVel = Vector3.Dot(_targetRb.linearVelocity, radial);

            if (SimulatesTarget())
            {
                // Cancel the velocity component pulling away from anchor
                if (radialVel > 0f)
                    _targetRb.linearVelocity -= radial * radialVel;

                // Snap position to rope length
                _targetRb.position = ropeStart + radial * _currentRopeLength - _attachOffset;
            }

            // Drag the player anchor slightly (feel of weight on the rope)
            if (!Network.Owns(owner.transform)) return;

            Rigidbody ownerRb = owner.GetComponent<Rigidbody>();
            if (ownerRb == null) return;

            float tensionScale = Mathf.Clamp01(ropeTension / 600f);
            float drag = Mathf.Abs(radialVel) * 0.15f * tensionScale;
            ownerRb.AddForce(radial * drag, ForceMode.VelocityChange);
        }

        // ── Rope visual ────────────────────────────────────────────────────────

        private void EnableRope()
        {
            if (lineRenderer == null) return;
            lineRenderer.positionCount = ropeSegments;
            lineRenderer.enabled = true;

            var widthCurve = new AnimationCurve();
            widthCurve.AddKey(0f, ropeWidth);
            widthCurve.AddKey(1f, ropeWidth * 0.35f);
            lineRenderer.widthCurve = widthCurve;
        }

        private void DisableRope()
        {
            if (lineRenderer == null) return;
            lineRenderer.enabled = false;
        }

        private void UpdateRope(float headProgress)
        {
            if (lineRenderer == null || !lineRenderer.enabled) return;

            Vector3 start = GetRopeStart();
            Vector3 end = _ropeEndPoint;
            float span = (end - start).magnitude;
            float sagFactor = Mathf.Clamp01(span / maxRange);

            Vector3 wobbleDir = _wobbleAxis;
            if (wobbleDir.sqrMagnitude < 0.01f)
                wobbleDir = Vector3.right;

            int activeSegments = Mathf.Max(2, Mathf.RoundToInt(headProgress * ropeSegments));
            lineRenderer.positionCount = activeSegments;

            for (int i = 0; i < activeSegments; i++)
            {
                float t = i / (float)(activeSegments - 1);
                Vector3 pos = Vector3.Lerp(start, end, t);

                pos.y -= Mathf.Sin(t * Mathf.PI) * ropeGravity * sagFactor;

                float wobble = Mathf.Sin(t * Mathf.PI * wobbleFrequency + _wobbleTime * 6f)
                               * wobbleAmplitude * _wobbleStrength
                               * Mathf.Sin(t * Mathf.PI);
                pos += wobbleDir * wobble;

                lineRenderer.SetPosition(i, pos);
            }
        }

        // ── Loop visual ────────────────────────────────────────────────────────

        private void EnableLoop()
        {
            if (loopRenderer == null) return;
            loopRenderer.positionCount = loopSegments + 1;
            loopRenderer.enabled = true;
        }

        private void DisableLoop()
        {
            if (loopRenderer == null) return;
            loopRenderer.enabled = false;
        }

        private void UpdateLoop(Vector3 center, Vector3 forward)
        {
            if (loopRenderer == null || !loopRenderer.enabled) return;
            if (forward.sqrMagnitude < 0.001f) return;

            Quaternion baseRot = Quaternion.LookRotation(forward);
            Quaternion tilt = Quaternion.AngleAxis(loopTiltAngle, baseRot * Vector3.right);
            Quaternion spin = Quaternion.AngleAxis(_loopAngle, forward);
            Quaternion loopRot = spin * tilt * baseRot;

            Vector3 right = loopRot * Vector3.right;
            Vector3 up    = loopRot * Vector3.up;

            for (int i = 0; i <= loopSegments; i++)
            {
                float angle = i / (float)loopSegments * Mathf.PI * 2f;

                float distort = Mathf.Sin(angle * 2f + _loopAngle * 0.03f * loopDistortSpeed) * 0.6f
                              + Mathf.Sin(angle * 3f - _loopAngle * 0.05f * loopDistortSpeed) * 0.4f;
                float r = loopRadius + distort * loopDistortAmount;

                Vector3 pt = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * r;
                loopRenderer.SetPosition(i, pt);
            }
        }

        private Vector3 GetRopeStart()
        {
            return muzzle != null ? muzzle.position : owner.transform.position;
        }

        // Returns the best hookable target within throwRadius of headPos.
        // Prefers Rigidbody targets; falls back to any collider whose root has AgentController.
        private bool TryGetLatchTarget(Vector3 headPos, out Rigidbody rb, out Transform hitTransform, out Vector3 latchPoint)
        {
            rb = null;
            hitTransform = null;
            latchPoint = headPos;

            Collider[] nearby = Physics.OverlapSphere(headPos, throwRadius, ~0, QueryTriggerInteraction.Ignore);
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
