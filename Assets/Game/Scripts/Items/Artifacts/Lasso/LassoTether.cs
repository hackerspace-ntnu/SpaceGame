using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// The far end of a lasso: one roped creature, and everything being roped does to it.
    ///
    /// <para>
    /// Added at runtime, never authored — the same shape and the same reasoning as
    /// <see cref="LeashAttachable.GetOrAdd"/> and <see cref="LeashedBody.Ensure"/>. Any creature
    /// can be roped at any time, and the alternative is a component every creature in the game
    /// carries for a case most of them never hit.
    /// </para>
    /// <para>
    /// <b>It exists only on the machine that simulates the creature.</b> On a peer the replica is
    /// kinematic on purpose — NetworkRigidbody makes it so — and its transform is somebody else's
    /// to publish. A second copy of this fighting a NetworkTransform is the failure the artifact's
    /// <c>SimulatesTarget</c> gate exists to prevent.
    /// </para>
    /// <para>
    /// <b>What it replaces.</b> The lasso used to do <c>agent.enabled = false</c> on the catch and
    /// <c>agent.enabled = true</c> on the release, straight from the item. Three things were wrong
    /// with that. The creature became a statue on a string, which is the opposite of what roping
    /// something should look like. The restore was unconditional, so a creature whose agent was
    /// parked — <c>Awake</c> parks one that wakes before a NavMesh exists beneath it — came back
    /// switched ON and walked off a world that was not there yet. And the whole relationship lived
    /// in fields on an item instance that is destroyed on every equip, so switching hotbar slot
    /// freed the animal.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class LassoTether : MonoBehaviour
    {
        /// <summary>Density used to guess a mass from a body that has no Rigidbody. See <see cref="Mass"/>.</summary>
        private const float BoundsDensity = 220f;

        private const float MinMass = 2f;
        private const float MaxMass = 900f;

        /// <summary>How far off the NavMesh a dragged creature may be pulled before it is snapped back.</summary>
        private const float GroundProbe = 2f;

        private Transform anchor;
        private LassoStruggle settings;
        private float ropeLength;

        private AgentController agent;
        private NavMeshAgent navAgent;
        private Rigidbody body;
        private AgentAnimatorDriver animatorDriver;
        private ISelfDrivingMotor selfDriving;

        private bool bound;
        private float struggleClock;
        private float mass = MinMass;
        private Vector3 lastAnimatedPosition;

        /// <summary>The rope anchor that took hold. Only that rope may let go again.</summary>
        private Transform binder;

        /// <summary>Set when this tether — and not something else — made the body dynamic.</summary>
        private bool madeDynamic;

        /// <summary>Whether a rope currently holds this creature.</summary>
        public bool IsBound => bound;

        public static LassoTether Ensure(GameObject creature)
        {
            if (creature == null) return null;

            return creature.TryGetComponent(out LassoTether existing)
                ? existing
                : creature.AddComponent<LassoTether>();
        }

        /// <summary>Where the rope ties to — the creature's root, lifted to roughly neck height.</summary>
        public Vector3 AttachPoint => transform.position + Vector3.up * AttachHeight;

        /// <summary>
        /// How far up this creature the knot sits, in metres. Resolved once in <see cref="Bind"/>
        /// rather than per frame, because it comes from a collider scan.
        /// </summary>
        public float AttachHeight => attachHeight;

        private float attachHeight = 1.2f;

        /// <summary>
        /// Neck height on a creature of this size.
        ///
        /// <para>
        /// The old flat 1.2 m was a number about a person, applied to everything: it hung the
        /// collar in the air above anything small and put it round the ankle of a habitat-sized
        /// walker. A fraction of the creature's own height is right for both, and it is the
        /// artifact's drawn rope end as well as the tether's constraint point — one number, asked
        /// for in one place, because those two disagreeing is a rope that goes taut against a spot
        /// it is not drawn to.
        /// </para>
        /// <para>
        /// Static and public so the item can ask it of a creature it has not tethered yet — the
        /// throw has to know where its rope will end before it decides it has caught anything.
        /// </para>
        /// </summary>
        public static float AttachHeightFor(GameObject creature, float fraction, float fallbackMetres)
        {
            float height = LargestSolidBounds(creature).size.y;

            // No solid collider to read: a creature in a chunk whose colliders have not built yet,
            // or a target that is all triggers. The authored metres are the person-sized answer,
            // and being wrong about a rare case beats tying the rope to the ground.
            return height > 0.01f ? height * Mathf.Clamp(fraction, 0.1f, 1f) : fallbackMetres;
        }

        /// <summary>
        /// What this creature weighs, for deciding which end of a taut rope moves.
        ///
        /// <see cref="Rigidbody.mass"/> when there is a body. Otherwise estimated from the collider
        /// bounds, because most creatures in this game are NavMesh agents with no Rigidbody at all
        /// and a single shared default would have an ant drag the player exactly as hard as a
        /// six-legged habitat — which is dallying with the point removed.
        /// </summary>
        public float Mass => mass;

        /// <summary>1 for an animal that has just been caught, 0 for one that has given up.</summary>
        public float StruggleFraction =>
            settings == null ? 0f : Mathf.Clamp01(1f - struggleClock / settings.StruggleSeconds);

        /// <summary>
        /// Take hold of this creature. False when somebody else's rope already has it.
        ///
        /// <para>
        /// One rope at a time. <see cref="Ensure"/> returns whatever component is already here, so
        /// without this a second thrower rebound the SAME tether: the first rope's constraint
        /// vanished while its item went on drawing a rope and dragging its holder, and whichever
        /// thrower released first freed the creature from both.
        /// </para>
        /// </summary>
        public bool Bind(Transform ropeAnchor, float length, LassoStruggle struggleSettings)
        {
            if (bound && binder != ropeAnchor) return false;

            binder = ropeAnchor;

            // A rope on somebody riding an animal takes them off it first, before anything below
            // reads a position or a body: a seated rider's transform belongs to the mount, so the
            // rope would go taut against an animal that walks on regardless. See
            // NpcPassenger.UnseatRider.
            NpcPassenger.UnseatRider(gameObject);

            anchor = ropeAnchor;
            settings = struggleSettings ?? new LassoStruggle();
            ropeLength = length;
            struggleClock = 0f;
            lastAnimatedPosition = transform.position;

            agent = GetComponentInParent<AgentController>();
            navAgent = GetComponentInParent<NavMeshAgent>();
            body = GetComponentInParent<Rigidbody>();
            animatorDriver = GetComponentInChildren<AgentAnimatorDriver>();

            mass = EstimateMass();
            attachHeight = AttachHeightFor(gameObject, settings.AttachFraction, settings.AttachHeight);

            // Hand the transform over properly rather than switching a component off behind the
            // AI's back. SuspendSelfDrive records whether the agent was enabled to begin with,
            // which is the whole difference between this and what it replaces.
            //
            // The controller's own Motor reference is preferred but not trusted: it is resolved in
            // Awake, and a creature spawned this frame — or built by a test, where Awake does not
            // run at all outside play mode — has not had one yet. Falling back to the component
            // means the rope never silently fails to take hold of an animal that has a motor
            // sitting right there.
            selfDriving = agent != null ? agent.Motor as ISelfDrivingMotor : null;
            selfDriving ??= GetComponentInParent<ISelfDrivingMotor>();
            selfDriving?.SuspendSelfDrive();

            // Only a body this machine OWNS. A replica is kinematic on purpose, and flipping one
            // here handed it gravity and collisions it must not have — a corruption that outlived
            // the rope, because Release used not to put it back, and that a quit-time autosave
            // could capture. Simulates was the wrong question: it is true on the server for every
            // remote player's and every ridden mount's replica.
            if (body != null && body.isKinematic && Network.Owns(body))
            {
                body.isKinematic = false;
                madeDynamic = true;
            }

            bound = true;
            return true;
        }

        /// <summary>The rope was reeled in or paid out. Kept in step so the constraint agrees with the visual.</summary>
        public void SetRopeLength(float length) => ropeLength = length;

        /// <summary>
        /// Let the creature go. Safe to call twice, and safe on a creature being destroyed
        /// underneath it — this is reached from the press, from unequip, from the item's OnDestroy
        /// and from this component's own, and more than one of those fires for a single release.
        /// </summary>
        public void Release(Transform ropeAnchor)
        {
            // Addressed by the rope that took hold, so a release travelling from another thrower's
            // item — or from a stale instance being torn down — cannot free a creature somebody
            // else is still holding.
            if (!bound || (ropeAnchor != null && ropeAnchor != binder)) return;

            bound = false;
            binder = null;

            // Put back exactly what Bind changed, and only if Bind was the thing that changed it.
            if (madeDynamic && body != null) body.isKinematic = true;
            madeDynamic = false;

            selfDriving?.ResumeSelfDrive();
            selfDriving = null;

            // The creature has been dragged, possibly well off the NavMesh it started on. Warp only
            // takes effect on an ENABLED agent, so this has to come after ResumeSelfDrive — the
            // same ordering WorldStreamer.SnapAgentsToNavMesh documents.
            if (navAgent != null && navAgent.isActiveAndEnabled
                && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                navAgent.Warp(hit.position);

            anchor = null;
            agent = null;
            navAgent = null;
            body = null;
            animatorDriver = null;
        }

        /// <summary>Going away releases whatever holds this creature, whoever that is.</summary>
        private void OnDestroy() => Release(binder);

        /// <summary>Advance the struggle clock without a frame. The seam the EditMode tests use.</summary>
        public void AdvanceStruggle(float seconds) => struggleClock += Mathf.Max(0f, seconds);

        /// <summary>
        /// Wind the struggle clock back as slack would, without a frame. The other half of the
        /// seam above.
        ///
        /// <para>
        /// Two named methods rather than a signed <c>AdvanceStruggle</c>, because tiring and
        /// resting do not happen at the same rate — recovery is scaled by
        /// <see cref="LassoStruggle.RecoverySeconds"/> and a negative advance would quietly skip
        /// that.
        /// </para>
        /// </summary>
        public void RecoverStruggle(float seconds) => Rest(Mathf.Max(0f, seconds));

        // ── The fight ──────────────────────────────────────────────────────────

        private void FixedUpdate()
        {
            if (!bound || anchor == null) return;

            float dt = Time.fixedDeltaTime;

            Vector3 toCreature = transform.position - anchor.position;
            toCreature.y = 0f;

            Vector3 away = toCreature.sqrMagnitude > 1e-4f ? toCreature.normalized : transform.forward;
            Vector3 sideways = Vector3.Cross(away, Vector3.up);

            // Retreat plus thrash. A pure retreat is a straight line away from the player, which
            // reads as a spring rather than an animal — what sells it is the creature throwing its
            // weight across the rope and being turned by it.
            float thrash = Mathf.Sin(struggleClock * settings.ThrashFrequency * Mathf.PI * 2f);
            Vector3 pull = Vector3.Lerp(away, sideways * thrash, settings.ThrashShare).normalized;

            Vector3 before = transform.position;
            MoveOnGround(pull * (settings.StruggleSpeed * StruggleFraction * dt));

            TickStruggleClock(dt);

            Constrain();
            FaceTravel(transform.position - before, dt);
        }

        /// <summary>
        /// Tire the animal while it is pulling; let it get its wind back while it is not.
        ///
        /// <para>
        /// The clock used to run one way only, so six seconds after the catch every creature in the
        /// game went limp and stayed limp for as long as the session lasted. That is not a caught
        /// animal, it is a finished animation — and it left the player with nothing to do but walk
        /// away dragging a sack.
        /// </para>
        /// <para>
        /// Recovery is gated on SLACK rather than on time, which is what turns reeling into a
        /// decision: line taken back is line the animal is no longer fighting against, so it rests.
        /// Pull it in too early and you get a rested animal on a short rope. Together with the
        /// pay-out in <see cref="LassoTension"/> that is a pair of opposing negative loops, which
        /// is what a contest needs to have a middle at all (<c>GDC-L1-SYS-0004</c>).
        /// </para>
        /// </summary>
        private void TickStruggleClock(float dt)
        {
            if (IsTaut)
            {
                struggleClock += dt;
                return;
            }

            Rest(dt);
        }

        /// <summary>
        /// Give back <paramref name="seconds"/> of slack as fight.
        ///
        /// Scaled by the ratio of the two budgets rather than subtracted one-for-one, so
        /// <c>recoverySeconds</c> means what its tooltip says — seconds of rest per second of
        /// fight recovered — independently of how long the fight itself lasts.
        /// </summary>
        private void Rest(float seconds)
        {
            if (settings == null) return;

            struggleClock = Mathf.Max(
                0f, struggleClock - seconds * settings.StruggleSeconds / settings.RecoverySeconds);
        }

        /// <summary>How far the creature has got outside the rope, in metres. Zero when slack.</summary>
        public float Overshoot =>
            bound && anchor != null
                ? Mathf.Max(0f, Vector3.Distance(AttachPoint, anchor.position) - ropeLength)
                : 0f;

        /// <summary>Whether the rope is currently pulled tight against this creature.</summary>
        public bool IsTaut => Overshoot > 0f;

        /// <summary>
        /// Pull the creature back onto the rope, by however much of the correction its own weight
        /// does not win. The player absorbs the rest on their own machine — see
        /// <see cref="LassoArtifact.PlayerPullShare"/>, which both ends compute rather than send.
        /// </summary>
        private void Constrain()
        {
            Vector3 rope = AttachPoint - anchor.position;
            float distance = rope.magnitude;
            if (distance <= ropeLength || distance < 0.001f) return;

            Vector3 radial = rope / distance;
            float creatureShare = 1f - LassoArtifact.PlayerPullShare(mass);

            Vector3 onRope = anchor.position + radial * ropeLength;
            Vector3 correction = (onRope - AttachPoint) * creatureShare;

            // Cancel the velocity that is lengthening the rope before moving the body, or the next
            // physics step spends it going straight back out through the constraint and the rope
            // reads as elastic.
            if (body != null && !body.isKinematic)
            {
                float radialVelocity = Vector3.Dot(body.linearVelocity, radial);
                if (radialVelocity > 0f) body.linearVelocity -= radial * radialVelocity;
            }

            MoveOnGround(correction);
        }

        /// <summary>
        /// Move by <paramref name="delta"/>, keeping the creature on the ground it is allowed to
        /// stand on.
        ///
        /// <para>
        /// Writing world positions straight onto a creature whose NavMeshAgent has been switched
        /// off is how a roped animal ends up inside a rock or walking off a mesa — nothing is left
        /// to stop it, because the thing that used to was the agent. Sampling the NavMesh puts that
        /// back: the struggle can pull anywhere it likes, and what actually lands is the nearest
        /// point the creature could have walked to.
        /// </para>
        /// <para>
        /// A creature with no NavMesh under it at all — a physics prop, or one in a chunk whose
        /// mesh has not built yet — moves unclamped, which is the right answer for both: a prop was
        /// never on a NavMesh, and refusing to move the other would freeze it until the chunk
        /// caught up.
        /// </para>
        /// </summary>
        private void MoveOnGround(Vector3 delta)
        {
            Vector3 wanted = transform.position + delta;

            if (body != null && !body.isKinematic)
            {
                // A real body has its own collision. Let the physics engine be the thing that
                // stops it, rather than second-guessing it with a NavMesh it never used.
                body.position = wanted;
                return;
            }

            // Safe precisely BECAUSE self-drive is suspended: with the NavMeshAgent disabled there
            // is nothing left re-deriving this transform from its own pathPos, which is what undoes
            // a plain positional write on an agent that is still running.
            transform.position = NavMesh.SamplePosition(wanted, out NavMeshHit hit, GroundProbe, NavMesh.AllAreas)
                ? hit.position
                : wanted;
        }

        private void FaceTravel(Vector3 delta, float dt)
        {
            delta.y = 0f;
            if (delta.sqrMagnitude < 1e-6f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(delta.normalized, Vector3.up),
                settings.TurnSpeed * dt);
        }

        /// <summary>
        /// Drive the walk cycle from the ground the creature actually covered.
        ///
        /// <para>
        /// In LateUpdate, and that is the whole trick. <see cref="AgentAnimatorDriver"/> measures
        /// its own transform on any frame nobody drove it — but somebody does drive it here:
        /// <see cref="AgentController"/> ticks in Update and feeds it <c>Motor.Velocity</c>, which
        /// with the agent suspended is zero. So a struggling creature slid across the sand with an
        /// idle animation playing, which is the statue-on-a-string look wearing different clothes.
        /// Update runs before LateUpdate for every component, so measuring here is what lands.
        /// </para>
        /// </summary>
        private void LateUpdate()
        {
            if (!bound || animatorDriver == null) return;

            Vector3 delta = transform.position - lastAnimatedPosition;
            lastAnimatedPosition = transform.position;

            animatorDriver.Tick(
                AgentAnimatorDriver.MeasureVelocity(delta, Time.deltaTime),
                isImmobile: false,
                isRunning: StruggleFraction > 0.5f);
        }

        // ── Mass ───────────────────────────────────────────────────────────────

        private float EstimateMass() => EstimateMassOf(gameObject);

        /// <summary>
        /// What a creature weighs, without needing a tether on it.
        ///
        /// <para>
        /// Static and public because the estimate is a pure function of a GameObject and more than
        /// one system wants it — <see cref="SnareTether"/> weighs a netted creature the same way.
        /// Reaching it through <c>Ensure(creature).Mass</c> instead would do two wrong things:
        /// leave a <see cref="LassoTether"/> component on every creature that was merely weighed,
        /// and answer <see cref="MinMass"/> for any creature that has never been roped, because
        /// the instance field is only filled in by <see cref="Bind"/>.
        /// </para>
        /// </summary>
        public static float EstimateMassOf(GameObject creature)
        {
            if (creature == null) return MinMass;

            Rigidbody body = creature.GetComponentInParent<Rigidbody>();
            if (body != null) return Mathf.Clamp(body.mass, MinMass, MaxMass);

            Vector3 size = LargestSolidBounds(creature).size;
            float volume = size.x * size.y * size.z;

            if (volume <= 0f) return MinMass;

            // A bounding box is mostly air, so the density is well under water's — this is tuned to
            // put an ant at a couple of kilos and a habitat-sized walker in the high hundreds
            // without anyone authoring a number per creature.
            return Mathf.Clamp(volume * BoundsDensity, MinMass, MaxMass);
        }

        /// <summary>
        /// The bounds of the creature's BIGGEST solid collider.
        ///
        /// <para>
        /// The biggest, never the first one found. A creature's hierarchy is full of small
        /// triggers — a perception volume, a bite hitbox, an interaction prompt — and any of them
        /// can come back first, which would weigh a walker by its own mouth and tie the rope to it.
        /// </para>
        /// <para>
        /// One scan for both questions this file asks of a creature's shape, because they must
        /// agree: a rope tied by one collider's height and a mass read off another's volume would
        /// have the knot and the weight describing two different animals.
        /// </para>
        /// </summary>
        private static Bounds LargestSolidBounds(GameObject creature)
        {
            if (creature == null) return default;

            Bounds best = default;
            float bestVolume = 0f;

            foreach (Collider collider in creature.GetComponentsInChildren<Collider>())
            {
                if (collider == null || collider.isTrigger) continue;

                Bounds bounds = collider.bounds;
                Vector3 size = bounds.size;
                float volume = size.x * size.y * size.z;

                if (volume <= bestVolume) continue;

                bestVolume = volume;
                best = bounds;
            }

            return best;
        }
    }
}
