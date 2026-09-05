using System.Collections.Generic;
using SpaceGame.Teleporting;
using SpaceGame.World.Safety;
using UnityEngine;

namespace SpaceGame.Gameplay.Ragdoll
{
    /// <summary>
    /// A physical skeleton built at runtime from whatever rig this entity happens to have, and the
    /// state machine that takes it limp and hands it back.
    ///
    /// <para>
    /// Rig-agnostic on purpose. The project ships ten skeletons — a Mixamo humanoid, an ostrich, a
    /// six-legged hexapod, a rat, a golem, several robots — and no authored ragdolls at all. This
    /// walks the skinned meshes, asks <see cref="RagdollSkeleton"/> which bones carry enough of the
    /// mesh to be worth simulating, and wires capsules and <c>CharacterJoint</c>s through what
    /// survives. A rig re-exported tomorrow with different bone names still works.
    /// </para>
    ///
    /// <para>
    /// The skeleton is built on the FIRST limp rather than at spawn, because most bodies never fall
    /// over. Once built it is kept and switched kinematic rather than destroyed: rebuilding costs a
    /// mesh walk, and destroying a <c>Rigidbody</c> that a live <c>CharacterJoint</c> still
    /// references is an ordering problem there is no reason to have. <see cref="Freeze"/> is the
    /// one path that really tears it down.
    /// </para>
    ///
    /// <para>
    /// This component knows nothing about death, damage or the netcode. What it knows is bones.
    /// Deciding WHEN a body goes limp, and what else has to stop driving it while it is, belongs to
    /// <see cref="AgentRagdoll"/> and <see cref="PlayerRagdoll"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class RagdollRig : MonoBehaviour
    {
        [Header("Which bones get a body")]
        [Tooltip("Share of the mesh a bone must carry to be simulated, 0..1. The floor that " +
                 "separates a forearm from a finger. Raising it makes a coarser, more stable " +
                 "ragdoll; lowering it adds extremities that tend to buzz.")]
        [SerializeField, Range(0f, 0.2f)] private float minBoneWeightFraction = 0.015f;

        [Tooltip("Hard cap on simulated bones. A rig that survives the weight floor with fifty " +
                 "bones is a rig whose weights are unusual, not a body worth fifty joints.")]
        [SerializeField] private int maxBones = 20;

        [Tooltip("Fewest bones that count as a body.\n\n" +
                 "Below this the skinned measure is treated as having FAILED and the rig is rebuilt " +
                 "from its rigid mesh parts instead. Hard-surface models are why: a robot is often " +
                 "rigid pieces parented to bones plus one or two small skinned bits, so the vertex " +
                 "weight lands almost entirely on a couple of bones and the skinned measure " +
                 "confidently returns a two-bone 'skeleton' for a fifty-bone rig.\n\n" +
                 "Note this cannot rescue a model that simply has little geometry — see the " +
                 "remarks on the fallback in Build.")]
        [SerializeField] private int minimumUsefulBones = 4;

        [Header("Shape")]
        [Tooltip("Limb radius as a fraction of the bone's length.\n\n" +
                 "An estimate, not a measurement: reading the real girth needs the mesh vertices, " +
                 "and a mesh imported without Read/Write Enabled has none to read. Nobody sees " +
                 "these capsules — what they decide is how deep the body sinks into the sand and " +
                 "how tightly it can fold, so this is the field to tune when a corpse looks buried.")]
        [SerializeField, Range(0.05f, 0.6f)] private float limbAspect = 0.3f;

        [Tooltip("Thinnest a limb may be, metres. Below this PhysX tunnels a fast-moving bone " +
                 "straight through the ground — and the gauntlet launches at 48 m/s.")]
        [SerializeField] private float minRadius = 0.04f;

        [Tooltip("Mass of the whole body, kg, split between bones by how much mesh each carries.")]
        [SerializeField] private float totalMass = 70f;

        [Tooltip("Least mass any one bone may have, kg. Not for realism: a joint between bodies " +
                 "more than about ten to one apart is the classic ragdoll explosion.")]
        [SerializeField] private float minBoneMass = 0.6f;

        [Header("Settling down")]
        [Tooltip("Rotational drag on every bone.\n\n" +
                 "The single most important number for whether a body comes to rest. With no " +
                 "angular drag nothing removes energy from the system, so a chain of jointed bodies " +
                 "trades it back and forth through the joint limits and wobbles for as long as you " +
                 "care to watch. Raise it if bodies keep twitching, lower it if they land like wet " +
                 "cloth.")]
        [SerializeField] private float angularDamping = 0.6f;

        [Tooltip("Linear drag on every bone. Small — this is not air resistance, it is the last " +
                 "bit of sliding being taken out of a body that has already landed.")]
        [SerializeField] private float linearDamping = 0.05f;

        [Tooltip("Solver iterations per bone. Unity's project default is 6, which is meant for " +
                 "loose props rather than a twenty-body chain of joints — under-solved joints " +
                 "leave a residual correction every tick, which is visible as a body that never " +
                 "quite stops.")]
        [SerializeField, Range(4, 40)] private int solverIterations = 14;

        [Tooltip("Put the bones to sleep once the body is settled.\n\n" +
                 "The difference between 'mostly still' and STILL. A settled ragdoll is not a " +
                 "motionless one — it is one whose residual motion is under a threshold — and left " +
                 "awake it keeps shivering at that threshold indefinitely. Sleeping ends it " +
                 "outright, and anything that hits the body afterwards wakes it again by itself.")]
        [SerializeField] private bool sleepWhenSettled = true;

        [Header("Joints")]
        [Tooltip("Let the body's own bones collide with each other.\n\n" +
                 "OFF, and not as a shortcut. Colliders here are ESTIMATED from bone lengths and " +
                 "mesh bounds, so they cannot be trusted not to overlap — and the worst offenders " +
                 "are structural rather than sloppy. Two thighs are siblings: both jointed to the " +
                 "hips, neither jointed to each other, so the joint's own collision exclusion does " +
                 "not cover them, and at anatomically correct thickness they ALWAYS overlap at the " +
                 "hip. Measured on the Nomad: 15 cm of interpenetration between the thighs and 9 cm " +
                 "between the calves, which PhysX then tries to resolve on every single tick and " +
                 "cannot. That is what a jittering ragdoll is.\n\n" +
                 "Turning it on is only sensible for a rig whose colliders have been placed by hand.")]
        [SerializeField] private bool selfCollision;

        [Tooltip("How far a joint may bend away from its bind pose, degrees.")]
        [SerializeField, Range(0f, 177f)] private float swingLimit = 45f;

        [Tooltip("How far a joint may twist about its own bone, degrees. Kept well under the swing " +
                 "— a body that can twist as freely as it bends reads as boneless.")]
        [SerializeField, Range(0f, 177f)] private float twistLimit = 25f;

        [Header("Settling")]
        [Tooltip("Linear speed under which the body counts as slow, m/s.")]
        [SerializeField] private float settleLinearSpeed = 0.35f;
        [Tooltip("Angular speed under which the body counts as slow, rad/s.")]
        [SerializeField] private float settleAngularSpeed = 1.2f;
        [Tooltip("How long both must stay slow before the body is called settled. A tumbling body " +
                 "passes through zero at the top of every bounce, so without this it stands up mid-air.")]
        [SerializeField] private float settleSeconds = 0.45f;

        [Tooltip("Longest a knockdown may hold a body, seconds — settled or not.\n\n" +
                 "This is the GDC-L1-FEEL-0002 ceiling and it is not a tuning nicety: a body " +
                 "wedged against a rock never settles, and without a ceiling a knocked-down PLAYER " +
                 "never gets control back. Death ignores it, because a corpse has nowhere to be.")]
        [SerializeField] private float maxLimpSeconds = 4f;

        [Header("Recovery")]
        [Tooltip("Seconds to blend from the ragdoll's final pose into live animation. Display " +
                 "only — control is handed back at the START of this blend (GDC-L1-ANIM-0002).")]
        [SerializeField] private float recoverBlendSeconds = 0.35f;

        [Tooltip("How far to look down from the hips for the ground when standing a body back up.")]
        [SerializeField] private float groundProbeHeight = 3f;

        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Budget")]
        [Tooltip("How many bodies may be limp at once before the oldest is frozen where it lies " +
                 "(GDC-L1-PERF-0004). One blast into a crowd is what this is sized against.")]
        [SerializeField] private int maxConcurrentRagdolls = 12;

        /// <summary>One simulated bone, and everything hung off it.</summary>
        private sealed class Bone
        {
            public Transform Transform;
            public Rigidbody Body;
            public Collider Collider;

            /// <summary>Where this bone was pointing when the body went still — the blend's start.</summary>
            public Quaternion RecoverFrom;
        }

        private readonly List<Bone> bones = new List<Bone>();
        private readonly List<Joint> joints = new List<Joint>();

        private Animator animator;
        private bool built;
        private float slowSeconds;
        private float limpSeconds;
        private float blendRemaining;

        /// <summary>Hip height above the root in the standing pose — see <see cref="FollowHips"/>.</summary>
        private float standingHipHeight;

        public bool IsLimp { get; private set; }

        /// <summary>
        /// Does THIS machine decide where the body ends up?
        ///
        /// <para>
        /// The two answers need opposite plumbing, which is why this is a flag and not an
        /// assumption. On the machine that drives the body, the bones move and the root is dragged
        /// after them (<see cref="FollowHips"/>) — otherwise a corpse flung twenty metres leaves
        /// its transform, and therefore its save record and every peer's copy, standing at the spot
        /// it was hit. On a machine that is only watching, the root arrives over the wire and the
        /// body is pinned to it instead (<see cref="PinHipsToRoot"/>): a watcher that dragged its
        /// own root would spend every frame arguing with the NetworkTransform and lose.
        /// </para>
        ///
        /// <para>
        /// Set by the adapter, which is the layer that knows whether this entity is
        /// server-authoritative (a creature) or owner-authoritative (a player).
        /// </para>
        /// </summary>
        public bool Drives { get; set; } = true;

        /// <summary>
        /// Is this body limp because a gameplay system is HOLDING it there?
        ///
        /// <para>
        /// A corpse and a captive are both limp and <see cref="RagdollBudget"/> cannot otherwise
        /// tell them apart — so a firefight across the valley filling the budget would freeze a
        /// netted player, and <c>PlayerRagdoll.Update</c> restores control on <c>!IsLimp</c>, which
        /// stands them straight back up. The net is still drawn around them and still holding, and
        /// nothing is logged. Set for the duration of the hold and cleared on release.
        /// </para>
        ///
        /// <para>
        /// It is the HOLDER's to clear, not this component's: nothing here knows when a net tears.
        /// A holder that sets this and never clears it leaves a body the budget can never reclaim,
        /// which is the cost this flag is deliberately buying.
        /// </para>
        ///
        /// <para>
        /// Two routes clear it, not one. The release is the ordinary one; DEATH is the other, and
        /// both <c>PlayerRagdoll.OnDeath</c> and <c>AgentRagdoll.OnDeath</c> drop the claim on the
        /// spot. A corpse is exactly the thing the budget exists to reclaim, and it can no longer
        /// struggle out — so a captive who dies still netted must not take an un-evictable place in
        /// the budget with them and keep it for the rest of the session.
        /// </para>
        /// </summary>
        public bool BudgetExempt { get; set; }

        /// <summary>The bone the body hangs from. Null until the rig has been built.</summary>
        public Transform Hips { get; private set; }

        /// <summary>
        /// The simulated bones, for something that needs to ride the body without being part of it.
        ///
        /// <para>
        /// A fresh array rather than the live list, and transforms rather than the <c>Bone</c>
        /// records: a caller that could reach the Rigidbodies could add force to a ragdoll it does
        /// not own, and the one caller this exists for — a net binding its cord to a captive — has
        /// no business doing that.
        /// </para>
        /// </summary>
        public Transform[] BoneTransforms()
        {
            var found = new Transform[bones.Count];
            for (int i = 0; i < bones.Count; i++) found[i] = bones[i].Transform;
            return found;
        }

        /// <summary>Did the build find a skeleton worth simulating? False means this body cannot ragdoll.</summary>
        public bool HasSkeleton => built && bones.Count > 0;

        /// <summary>Simulated bones, and the joints between them. Diagnostics — see RagdollWiring's audit.</summary>
        public int BoneCount => bones.Count;
        public int JointCount => joints.Count;

        /// <summary>How many bones the measure pass OFFERED, before the weight floor and the cap.</summary>
        public int CandidateCount { get; private set; }

        /// <summary>Which measure the build used, for diagnostics: "skin", "length" or "parts".</summary>
        public string Measure { get; private set; } = "none";

        /// <summary>Where the root was standing when it went limp. The origin of the recovery move.</summary>
        public Vector3 PreLimpPosition { get; private set; }
        public Quaternion PreLimpRotation { get; private set; }

        /// <summary>
        /// Is the body at rest, or has it been limp long enough that the answer stops mattering?
        ///
        /// The timeout half is the ceiling described on <see cref="maxLimpSeconds"/>: a knockdown
        /// that never settles must still end.
        /// </summary>
        public bool IsSettled =>
            !IsLimp
            || limpSeconds >= maxLimpSeconds
            || RagdollSkeleton.IsSettled(FastestLinearSpeed, FastestAngularSpeed, slowSeconds,
                                         settleLinearSpeed, settleAngularSpeed, settleSeconds);

        /// <summary>
        /// The fastest bone, not the hips.
        ///
        /// Reading the hips alone was wrong in both directions. A body draped over a rock has still
        /// hips and an arm swinging free, which is not settled; and on a machine that is only
        /// watching, the hips are the one bone being driven from outside (see <see cref="Drives"/>),
        /// so their speed says more about the wire than about the body. The fastest bone answers
        /// the question actually being asked — has anything stopped moving yet.
        /// </summary>
        private float FastestLinearSpeed
        {
            get
            {
                float fastest = 0f;
                foreach (Bone bone in bones)
                    if (bone.Body != null && !bone.Body.isKinematic)
                        fastest = Mathf.Max(fastest, bone.Body.linearVelocity.magnitude);

                return fastest;
            }
        }

        private float FastestAngularSpeed
        {
            get
            {
                float fastest = 0f;
                foreach (Bone bone in bones)
                    if (bone.Body != null && !bone.Body.isKinematic)
                        fastest = Mathf.Max(fastest, bone.Body.angularVelocity.magnitude);

                return fastest;
            }
        }

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>(true);
            terrainGuard = GetComponent<UnderTerrainGuard>();
        }

        /// <summary>
        /// The failsafe that lifts a body out from under the world, held off while physics owns it.
        ///
        /// <para>
        /// Its own header says it "never fires during normal play" and that the only way to reach
        /// the depth it reacts to is for something to have already gone wrong. A ragdoll is a state
        /// that did not exist when that was written, and it breaks the assumption: a body thrown at
        /// 48 m/s into a slope clips under the surface for a moment as an ordinary part of falling
        /// over. The guard would lift it 1.2 m into the air, zero its velocity, and do it again a
        /// quarter of a second later.
        /// </para>
        ///
        /// <para>
        /// Held off rather than removed — it comes back the moment the body is upright again, which
        /// is when its assumption is true once more. A corpse never gets it back, and does not need
        /// it: it despawns.
        /// </para>
        /// </summary>
        private UnderTerrainGuard terrainGuard;
        private bool terrainGuardWasEnabled;

        private void OnDestroy() => RagdollBudget.Unregister(this);

        // ── Going limp ────────────────────────────────────────────────────────

        /// <summary>
        /// Hand the body to physics.
        /// </summary>
        /// <param name="impulse">
        /// Velocity handed to the hips, m/s world space. The rest of the body follows through the
        /// joints, which is what makes a blast read as a body thrown rather than a body switched off.
        ///
        /// <para>
        /// The motion the body was ALREADY carrying belongs in here too, and it is the caller's to
        /// supply. This component cannot read it: an agent's root rigidbody is kinematic and its
        /// real speed lives on the motor, and a player's has already been switched kinematic by the
        /// adapter's suspend by the time this is called. A version of this that read the rigidbody
        /// itself would compile, look right and return zero every time.
        /// </para>
        /// </param>
        /// <param name="settled">
        /// True for a body that is ALREADY down — a corpse arriving from a save. Skips the impulse
        /// and starts the settle timer expired, so it lies where it is instead of being thrown
        /// again and instead of standing up while the timer runs. See AgentRagdoll's restore path.
        /// </param>
        public void GoLimp(Vector3 impulse, bool settled = false, bool drives = true)
        {
            Drives = drives;
            if (!built) Build();
            if (bones.Count == 0) return;

            if (!IsLimp)
            {
                PreLimpPosition = transform.position;
                PreLimpRotation = transform.rotation;
                IsLimp = true;
                limpSeconds = 0f;
                blendRemaining = 0f;

                // Before the bodies wake, or the animator spends this frame fighting them for the
                // same transforms.
                if (animator != null) animator.enabled = false;

                if (terrainGuard != null)
                {
                    terrainGuardWasEnabled = terrainGuard.enabled;
                    terrainGuard.enabled = false;
                }

                foreach (Bone bone in bones)
                {
                    bone.Collider.enabled = true;
                    bone.Body.detectCollisions = true;

                    // The hips are the one bone a watching machine does not simulate: they are
                    // driven from the replicated root instead (see Drives / PinHipsToRoot), and a
                    // kinematic body is how you drive one without the solver fighting you for it.
                    // Everything hanging off them is dynamic on every machine, which is what keeps
                    // the flail local and free.
                    bool pinned = !Drives && bone == bones[0];
                    bone.Body.isKinematic = pinned;
                    if (pinned) continue;

                    bone.Body.linearVelocity = Vector3.zero;
                    bone.Body.angularVelocity = Vector3.zero;
                }

                ApplySelfCollision();
                RagdollBudget.Register(this, maxConcurrentRagdolls);
            }

            slowSeconds = settled ? settleSeconds : 0f;

            if (settled) limpSeconds = maxLimpSeconds;
            else if (Drives && impulse != Vector3.zero)
                bones[0].Body.AddForce(impulse, ForceMode.VelocityChange);

            // Not applied on a watching machine, and that is not an omission. The impulse's whole
            // effect there arrives already baked into the replicated root — applying it locally as
            // well would carry the body the distance twice and land it at double the range.
        }

        /// <summary>
        /// Stop simulating a body that has come to rest.
        ///
        /// <para>
        /// "Settled" is a threshold, not a standstill — <see cref="RagdollSkeleton.IsSettled"/> asks
        /// whether the fastest bone has stayed UNDER a speed for long enough, and a body sitting
        /// just under that speed shivers there for as long as anyone watches. That is the
        /// half-second of twitching that reads as a bug in an otherwise finished ragdoll, and no
        /// amount of damping removes it because damping is asymptotic and the threshold is not.
        /// </para>
        ///
        /// <para>
        /// Sleeping ends it outright. It also bounds the whole thing, because IsSettled goes true at
        /// <see cref="maxLimpSeconds"/> whether the body agrees or not — so a corpse that lands
        /// badly and would otherwise grind against a rock is asleep by then rather than doing it for
        /// the rest of its despawn timer. Anything that hits the body afterwards wakes it again,
        /// which is PhysX's own behaviour and needs nothing from here.
        /// </para>
        /// </summary>
        private void SleepBones()
        {
            foreach (Bone bone in bones)
                if (bone.Body != null && !bone.Body.isKinematic && !bone.Body.IsSleeping())
                    bone.Body.Sleep();
        }

        /// <summary>
        /// Tell the physics engine which of this body's own colliders to stop caring about.
        ///
        /// <para>
        /// Re-applied on every limp rather than once at build, because the ignore state does not
        /// survive a collider being switched off and on again — and recovery does exactly that.
        /// Applied once at build, a body would fall correctly the first time and jitter every time
        /// after, which is a far worse bug to be handed than one that is wrong consistently.
        /// </para>
        ///
        /// <para>
        /// Quadratic in the bone count and that is fine: the cap is <see cref="maxBones"/>, so the
        /// worst case is a couple of hundred calls on the frame a body goes down, once.
        /// </para>
        /// </summary>
        private void ApplySelfCollision()
        {
            for (int i = 0; i < bones.Count; i++)
            for (int j = i + 1; j < bones.Count; j++)
            {
                Collider a = bones[i].Collider;
                Collider b = bones[j].Collider;
                if (a != null && b != null) Physics.IgnoreCollision(a, b, !selfCollision);
            }
        }

        // ── Standing back up ──────────────────────────────────────────────────

        /// <summary>
        /// Take the body back from physics, put the root under it, and start the pose blend.
        ///
        /// <para>
        /// Returns the move the root just made, for the caller to hand to <see cref="ITeleportAware"/>.
        /// It cannot raise that itself: a legged machine holds its path position and every planted
        /// foot in WORLD space and rewrites the body transform from them each LateUpdate
        /// (LeggedLocomotion invariant I4), so resuming one without rebasing walks the creature
        /// straight back to where it was hit — but this component has no business knowing that, and
        /// the adapters do.
        /// </para>
        /// </summary>
        public TeleportMove Recover()
        {
            if (!IsLimp) return new TeleportMove(transform.position, transform.rotation,
                                                 transform.position, transform.rotation);

            Vector3 from = PreLimpPosition;
            Quaternion fromRotation = PreLimpRotation;

            // Snapshot before the bodies are switched off, or the blend starts from whatever the
            // animator writes on its first frame back — which is the standing pose, i.e. no blend
            // at all and a corpse that snaps upright.
            foreach (Bone bone in bones)
            {
                bone.RecoverFrom = bone.Transform.localRotation;
                bone.Body.isKinematic = true;
                bone.Body.detectCollisions = false;
                bone.Collider.enabled = false;
            }

            PlaceRootUnderHips();

            IsLimp = false;
            RagdollBudget.Unregister(this);

            if (animator != null) animator.enabled = true;
            if (terrainGuard != null && terrainGuardWasEnabled) terrainGuard.enabled = true;
            blendRemaining = recoverBlendSeconds;

            return new TeleportMove(from, fromRotation, transform.position, transform.rotation);
        }

        /// <summary>
        /// Stop simulating and leave the bones exactly where they lie.
        ///
        /// What <see cref="RagdollBudget"/> calls on the oldest corpse once too many are limp at
        /// once. Unlike <see cref="Recover"/> this really does tear the skeleton down — a body that
        /// has been frozen for cost reasons is one nobody is looking at closely, and keeping a
        /// dozen kinematic bodies and joints alive for it is the cost being avoided.
        /// </summary>
        public void Freeze()
        {
            if (!built) return;

            IsLimp = false;
            blendRemaining = 0f;

            // The guard comes back here too. A frozen body is no longer being driven by physics, so
            // the assumption it needs — that being under the world means something went wrong — is
            // true again, and a body evicted by the budget must not be the one body in the world
            // with its failsafe permanently switched off.
            if (terrainGuard != null && terrainGuardWasEnabled) terrainGuard.enabled = true;

            // Joints first. A Rigidbody destroyed while a live joint still points at it logs an
            // error, and Destroy processes in call order.
            foreach (Joint joint in joints)
                if (joint != null) Destroy(joint);
            joints.Clear();

            foreach (Bone bone in bones)
            {
                if (bone.Collider != null) Destroy(bone.Collider);
                if (bone.Body != null) Destroy(bone.Body);
            }
            bones.Clear();

            built = false;
            Hips = null;
        }

        // ── Per-frame ─────────────────────────────────────────────────────────

        /// <summary>
        /// A watcher's half of the split described on <see cref="Drives"/>: hold the body at the
        /// root the wire is writing, and let physics do everything else.
        ///
        /// <para>
        /// The hips are kinematic here and everything below them is not, so this drags one bone and
        /// the body flails from it. That is the division wanted: position comes from the machine
        /// that owns the truth, and the tumble — the part a watcher can derive perfectly well on
        /// its own, and the part that makes a corpse read as a corpse — stays local and free.
        ///
        /// <para>
        /// MovePosition rather than a direct assignment, because a kinematic body moved by
        /// assignment teleports without telling the solver it moved: the limbs hanging off it get
        /// no sweep between the two positions and are left behind, snapping after the pelvis a step
        /// later. MovePosition is the interpolated move the joints can follow.
        /// </para>
        /// </para>
        ///
        /// <para>
        /// In FixedUpdate because it is a physics write, and the correction has to land in the same
        /// step the solver reads it.
        /// </para>
        /// </summary>
        private void FixedUpdate() => PinHipsToRoot();

        private void PinHipsToRoot()
        {
            if (!IsLimp || Drives || Hips == null) return;
            if (bones.Count == 0 || bones[0].Body == null) return;

            // Same reason the driver stops following once settled: driving the pelvis every step
            // keeps the limbs jointed to it awake, so a watcher would be the one machine whose copy
            // of a corpse never stops shivering. A settled body's root is not moving either, so
            // there is nothing to keep up with.
            if (sleepWhenSettled && IsSettled) return;

            // The root IS the hips while a body is limp — see FollowHips for why there is no offset
            // between them. Reintroducing one here would put every watcher's copy of the body at a
            // different height from the machine that owns it.
            bones[0].Body.MovePosition(transform.position);
        }

        private void LateUpdate()
        {
            if (IsLimp)
            {
                limpSeconds += Time.deltaTime;

                bool slow = FastestLinearSpeed <= settleLinearSpeed
                            && FastestAngularSpeed <= settleAngularSpeed;
                slowSeconds = slow ? slowSeconds + Time.deltaTime : 0f;

                // Sleep BEFORE the follow, and skip the follow once asleep. Writing a transform
                // wakes the Rigidbody it belongs to, so a FollowHips that kept running would put
                // the body straight back to sleep and wake it again on every single frame — which
                // is not sleeping at all, just a more elaborate way of never settling. Once the
                // body is asleep it is not moving, so there is nothing left for the root to follow.
                if (sleepWhenSettled && IsSettled)
                {
                    SleepBones();
                    return;
                }

                if (Drives) FollowHips();
                return;
            }

            if (blendRemaining > 0f) BlendRecovery();
        }

        /// <summary>
        /// Keep the ROOT where the body actually is.
        ///
        /// <para>
        /// The bones move; the root does not. Left alone, a body flung twenty metres leaves its
        /// transform standing at the spot it was hit — and the transform is what the
        /// NetworkTransform replicates to every other machine and what the save file records. The
        /// corpse would come back on the next load, and appear to every peer, exactly where it was
        /// standing when it died.
        /// </para>
        ///
        /// <para>
        /// The hips are put back afterwards because moving the root drags them: they are its child.
        /// </para>
        ///
        /// <para>
        /// The root goes to the hips EXACTLY, with no attempt to drop it to where the feet would be.
        /// Subtracting a standing hip height looks more correct and is the bug that made ragdolls
        /// unusable: that offset is measured while the creature is upright, so once the body is
        /// lying down — hips a quarter of a metre off the ground — it plants the root the better
        /// part of a metre UNDERGROUND. <c>UnderTerrainGuard</c> then does exactly what it exists to
        /// do, teleporting the root to 1.2 m above the surface and taking the whole bone hierarchy
        /// with it, whereupon the body falls, lands, goes under again, and is lifted again a quarter
        /// of a second later. Forever.
        /// </para>
        ///
        /// <para>
        /// Landing on the hips also happens to be the only choice the WATCHER can mirror: it has to
        /// reconstruct the hips from the replicated root (see <see cref="PinHipsToRoot"/>), and any
        /// offset that varies with pose is one it cannot know. Root-is-hips needs no shared constant
        /// and cannot drift.
        /// </para>
        /// </summary>
        private void FollowHips()
        {
            if (Hips == null) return;

            Vector3 hipWorld = Hips.position;
            Quaternion hipRotation = Hips.rotation;

            transform.position = hipWorld;

            Hips.position = hipWorld;
            Hips.rotation = hipRotation;
        }

        /// <summary>
        /// Stand the root on the ground under the settled body, facing the way the body ended up.
        ///
        /// The probe matters on a slope: a corpse that slid down a dune is metres below the height
        /// its own hip offset implies, and a creature resumed at that height either hovers or is
        /// pushed out of the ground by its own capsule on the first physics tick.
        /// </summary>
        private void PlaceRootUnderHips()
        {
            if (Hips == null) return;

            Vector3 hipWorld = Hips.position;
            Quaternion hipRotation = Hips.rotation;

            Vector3 grounded = hipWorld - Vector3.up * standingHipHeight;
            if (Physics.Raycast(hipWorld + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit,
                                groundProbeHeight, groundMask, QueryTriggerInteraction.Ignore))
            {
                grounded = hit.point;
            }

            // Yaw only. The body's own tilt is where it fell, and carrying that into the root would
            // stand the creature up sideways.
            Vector3 facing = Vector3.ProjectOnPlane(Hips.forward, Vector3.up);
            if (facing.sqrMagnitude < 1e-4f)
                facing = Vector3.ProjectOnPlane(Hips.up, Vector3.up);

            transform.position = grounded;
            if (facing.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);

            Hips.position = hipWorld;
            Hips.rotation = hipRotation;
        }

        /// <summary>
        /// Ease the bones from where they came to rest into whatever is animating them now.
        ///
        /// <para>
        /// This is the whole of "getting up". There are no get-up clips in the project — four
        /// animator controllers exist and none has a recovery state — and authoring one per rig for
        /// a humanoid, an ostrich, a hexapod, a rat and a golem is a different piece of work. A
        /// blend is cruder and it covers every rig, including the procedurally-walked ones that
        /// have no Animator at all.
        /// </para>
        ///
        /// <para>
        /// Display only. The adapter hands control back when it calls <see cref="Recover"/>, at the
        /// start of this blend rather than the end (GDC-L1-ANIM-0002): the player is already
        /// driving while their body finishes standing up.
        /// </para>
        /// </summary>
        private void BlendRecovery()
        {
            blendRemaining -= Time.deltaTime;

            float t = recoverBlendSeconds > 0f
                ? Mathf.Clamp01(1f - blendRemaining / recoverBlendSeconds)
                : 1f;

            // Smoothstep rather than linear: the ragdoll pose and the animated pose can be far
            // apart, and a constant-rate rotation between two far-apart poses reads as the limb
            // being dragged rather than recovering.
            float eased = t * t * (3f - 2f * t);

            foreach (Bone bone in bones)
            {
                if (bone.Transform == null) continue;
                bone.Transform.localRotation =
                    Quaternion.Slerp(bone.RecoverFrom, bone.Transform.localRotation, eased);
            }

            if (blendRemaining <= 0f) blendRemaining = 0f;
        }

        // ── Building the skeleton ─────────────────────────────────────────────

        /// <summary>
        /// Walk the skinned meshes, decide which bones matter, and wire capsules and joints through
        /// what survives. Runs once, on the first limp.
        /// </summary>
        private void Build()
        {
            built = true;

            Dictionary<Transform, float> importance = MeasureBones();
            if (importance.Count == 0)
            {
                Debug.LogWarning($"{name}: RagdollRig found neither skinned bones nor rigid mesh " +
                                 "parts — this body cannot ragdoll. Expected a SkinnedMeshRenderer " +
                                 "with bones, or a hierarchy of MeshFilters, somewhere under it.", this);
                return;
            }

            List<Transform> kept = Select(importance);

            // A skinned measure that yields two bones out of fifty-four has found the RIG but not
            // the BODY: the weight sits on a couple of bones while the rest of the model is drawn
            // by rigid MeshRenderers this pass never looked at. That is the ordinary shape of a
            // hard-surface model and the part measure is the right answer for it.
            //
            // It is a fallback, not a repair, and the distinction is worth keeping in mind when
            // reading an audit. PatrolRobot 1 ("Robert") trips this guard and comes out of the part
            // measure with two bones as well — because that model genuinely has only four mesh
            // parts and near-rigidly bound skinning. Its two-bone ragdoll is the honest maximum for
            // the asset, not a failure of this code, and no threshold here will change it.
            if (!rigidParts && kept.Count < minimumUsefulBones)
            {
                Dictionary<Transform, float> parts = MeasureRigidParts();
                if (parts.Count > kept.Count)
                {
                    rigidParts = true;
                    Measure = "parts";
                    CandidateCount = parts.Count;
                    importance = parts;
                    kept = Select(importance);
                }
            }

            if (kept.Count == 0) return;

            Hips = kept[0];
            standingHipHeight = Mathf.Max(Hips.position.y - transform.position.y, 0f);

            float keptWeight = 0f;
            foreach (Transform bone in kept) keptWeight += importance[bone];

            var bodies = new Dictionary<Transform, Rigidbody>();
            foreach (Transform bone in kept)
            {
                Bone made = BuildBone(bone, kept, importance[bone], keptWeight);
                bones.Add(made);
                bodies[bone] = made.Body;

                Transform parent = NearestKeptAncestor(bone, bodies);

                // On the rigid path a part with no kept ancestor is not disconnected from the
                // creature, only from this particular branch of its hierarchy — hang it on the hub.
                if (parent == null && rigidParts && bone != Hips) parent = Hips;

                if (parent != null && bodies.TryGetValue(parent, out Rigidbody parentBody))
                    joints.Add(BuildJoint(made, parentBody));
            }
        }

        /// <summary>
        /// How much of the mesh each bone carries — or, when that cannot be read, how long it is.
        ///
        /// <para>
        /// Weight is the better signal and it is not always available: a mesh imported without
        /// Read/Write Enabled exposes no vertex data at runtime, and most of this project's FBXs
        /// are imported that way because nothing else needed to read them. Bone length stands in
        /// for it, and stands in well — the reason weight works is that fingers are small, and the
        /// reason length works is the same one.
        /// </para>
        /// </summary>
        private Dictionary<Transform, float> MeasureBones()
        {
            var importance = new Dictionary<Transform, float>();
            var candidates = new List<Transform>();
            bool allReadable = true;

            foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                Transform[] rigBones = renderer.bones;
                if (mesh == null || rigBones == null || rigBones.Length == 0) continue;

                foreach (Transform bone in rigBones)
                    if (bone != null && !candidates.Contains(bone)) candidates.Add(bone);

                if (!mesh.isReadable)
                {
                    allReadable = false;
                    continue;
                }

                var perVertex = mesh.GetBonesPerVertex();
                var weights = mesh.GetAllBoneWeights();
                int cursor = 0;

                for (int v = 0; v < perVertex.Length; v++)
                {
                    int influences = perVertex[v];
                    for (int i = 0; i < influences; i++, cursor++)
                    {
                        BoneWeight1 weight = weights[cursor];
                        if (weight.boneIndex < 0 || weight.boneIndex >= rigBones.Length) continue;

                        Transform bone = rigBones[weight.boneIndex];
                        if (bone == null) continue;

                        importance.TryGetValue(bone, out float accumulated);
                        importance[bone] = accumulated + weight.weight;
                    }
                }
            }

            // All or nothing, deliberately. A rig whose meshes are only PARTLY readable produced a
            // skeleton out of whichever ones happened to be — and did it silently. PatrolRobot 1
            // came back with two bones and one joint while its siblings got seventeen, because the
            // one readable mesh on it was a small accessory carrying two bones; the body it is
            // bolted to was invisible to this and simply absent from the ragdoll.
            //
            // Weight and length are not comparable quantities (one sums to a vertex count, the
            // other is metres), so they cannot be blended to fill the gap. Using the measure that
            // covers EVERY bone is the answer that is right for the whole rig, and a coarser
            // ragdoll built from all the bones beats a precise one built from two.
            if (importance.Count > 0 && allReadable)
            {
                Measure = "skin";
                CandidateCount = importance.Count;
                return importance;
            }

            if (candidates.Count > 0)
            {
                importance.Clear();

                foreach (Transform bone in candidates)
                    importance[bone] = SegmentLength(bone, candidates);

                Measure = "length";
                CandidateCount = importance.Count;
                return importance;
            }

            rigidParts = true;
            Measure = "parts";
            Dictionary<Transform, float> parts = MeasureRigidParts();
            CandidateCount = parts.Count;

            return parts;
        }

        /// <summary>
        /// True when this body is a hierarchy of separate rigid meshes rather than one skinned
        /// surface. Decides how disconnected pieces are treated — see <see cref="Select"/>.
        /// </summary>
        private bool rigidParts;

        /// <summary>
        /// The fallback for a creature that is not skinned at all: a hierarchy of separate rigid
        /// meshes, one per part.
        ///
        /// <para>
        /// Not a rare shape here — it is how several of this project's creatures are built. The
        /// golem, the six-legged crab and the humanoid robot have ZERO SkinnedMeshRenderers between
        /// them; they are parts positioned every frame by <c>LeggedLocomotion</c>'s IK, and the
        /// skinned path above finds nothing to ragdoll on any of them. It found nothing silently,
        /// too, which is the worse half: they looked wired and would have done nothing on the first
        /// blast that hit them.
        /// </para>
        ///
        /// <para>
        /// A part hierarchy is in one way the EASIER ragdoll — each part already has real geometry
        /// with real bounds, so its collider is a box around the mesh it draws rather than a capsule
        /// estimated from a bone length. Importance is the part's volume, which plays the role
        /// vertex weight plays for a skinned rig: it is how much of the creature this piece is, and
        /// it drops the bolts and antennae for the same reason the skinned path drops fingers.
        /// </para>
        /// </summary>
        private Dictionary<Transform, float> MeasureRigidParts()
        {
            var importance = new Dictionary<Transform, float>();

            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || filter.transform == transform) continue;

                Vector3 size = mesh.bounds.size;
                float volume = Mathf.Max(size.x, 1e-3f) * Mathf.Max(size.y, 1e-3f) * Mathf.Max(size.z, 1e-3f);

                importance.TryGetValue(filter.transform, out float accumulated);
                importance[filter.transform] = accumulated + volume;
            }

            return importance;
        }

        /// <summary>
        /// The bones worth simulating, ordered so every bone comes after its ancestors — which is
        /// what lets the joint pass find each bone's parent body already built.
        ///
        /// <para>
        /// Everything outside the hips' branch is dropped rather than jointed to it. A rig can hold
        /// several weighted hierarchies (a cape, a mount's saddle rig, a detached prop) and
        /// connecting one to the body across a gap produces a joint with a metre of slack, which
        /// PhysX resolves by flinging both ends apart.
        /// </para>
        /// </summary>
        private List<Transform> Select(Dictionary<Transform, float> importance)
        {
            var candidates = new List<Transform>(importance.Keys);
            var weights = new float[candidates.Count];
            for (int i = 0; i < candidates.Count; i++) weights[i] = importance[candidates[i]];

            bool[] keep = RagdollSkeleton.SelectBones(weights, minBoneWeightFraction);

            var selected = new List<Transform>();
            for (int i = 0; i < candidates.Count; i++)
                if (keep[i]) selected.Add(candidates[i]);

            // Heaviest first, then cut to the cap — so a rig with unusual weights loses its least
            // significant bones rather than an arbitrary tail.
            selected.Sort((a, b) => importance[b].CompareTo(importance[a]));
            if (selected.Count > maxBones) selected.RemoveRange(maxBones, selected.Count - maxBones);

            selected.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
            if (selected.Count == 0) return selected;

            // A skinned rig's stray hierarchy really is stray — a cape, a saddle rig, a prop
            // parented on — and jointing one to the body across a gap gives PhysX a joint with a
            // metre of slack, which it resolves by flinging both ends apart. Drop them.
            //
            // A rigid-part rig is the opposite case and needs the opposite answer. Its pieces are
            // routinely FLAT: every part a sibling under one grouping node, so not one of them has
            // a kept ancestor and this same rule threw away all but the heaviest. The golem, the
            // crab and the humanoid robot each came out as a single box with no joints at all —
            // wired, built, and still not a ragdoll. Those pieces are not strays, they ARE the
            // creature, so they hang off the heaviest one instead.
            Transform root = selected[0];
            if (!rigidParts)
            {
                selected.RemoveAll(bone => bone != root && !IsDescendantOf(bone, root));
                return selected;
            }

            // Heaviest first among the shallowest, so the hub is the creature's bulk rather than
            // whichever part happened to sort first.
            int shallowest = Depth(root);
            Transform hub = root;
            foreach (Transform bone in selected)
                if (Depth(bone) == shallowest && importance[bone] > importance[hub]) hub = bone;

            selected.Remove(hub);
            selected.Insert(0, hub);

            return selected;
        }

        private Bone BuildBone(Transform bone, List<Transform> kept, float weight, float totalWeight)
        {
            float length = SegmentLength(bone, kept);

            // Local units, not world. An FBX in this project can import at a lossyScale of 100, and
            // a collider is sized in local space and then scaled by it — so a capsule authored in
            // metres would come out a hundred times too big on exactly those rigs.
            float scale = Mathf.Max(Mathf.Abs(bone.lossyScale.x),
                          Mathf.Max(Mathf.Abs(bone.lossyScale.y), Mathf.Abs(bone.lossyScale.z)));
            if (scale < 1e-5f) scale = 1f;

            Vector2 capsule = RagdollSkeleton.CapsuleSize(length, length * limbAspect, minRadius);

            var body = bone.gameObject.AddComponent<Rigidbody>();
            body.mass = RagdollSkeleton.MassFor(weight, totalWeight, totalMass, minBoneMass);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.angularDamping = angularDamping;
            body.linearDamping = linearDamping;
            body.solverIterations = solverIterations;

            // The gauntlet launches at 48 m/s. A discrete body at that speed is through the terrain
            // between two ticks and gone.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
            body.detectCollisions = false;

            Collider collider = BuildCollider(bone, kept, capsule, scale);
            collider.enabled = false;

            return new Bone
            {
                Transform = bone,
                Body = body,
                Collider = collider,
                RecoverFrom = bone.localRotation,
            };
        }

        /// <summary>
        /// The shape for one bone: a box around its own mesh if it has one, a capsule down its
        /// length if it does not.
        ///
        /// <para>
        /// The split is between the two kinds of rig, and each side gets the better answer. A
        /// skinned bone draws nothing of its own — the mesh is one surface stretched over the whole
        /// skeleton — so the only measurement available is how far it is to the next joint, and a
        /// capsule down that line is the honest approximation. A rigid PART draws exactly itself,
        /// so its own mesh bounds are not an approximation at all, and a box around them is both
        /// more accurate and cheaper than anything derived.
        /// </para>
        /// </summary>
        private Collider BuildCollider(Transform bone, List<Transform> kept, Vector2 capsule,
                                       float scale)
        {
            var filter = bone.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                var box = bone.gameObject.AddComponent<BoxCollider>();
                box.center = filter.sharedMesh.bounds.center;
                box.size = filter.sharedMesh.bounds.size;
                return box;
            }

            var capsuleCollider = bone.gameObject.AddComponent<CapsuleCollider>();
            capsuleCollider.radius = capsule.x / scale;
            capsuleCollider.height = Mathf.Max(capsule.y, capsule.x * 2f) / scale;
            capsuleCollider.direction = LongAxis(bone, kept, out float sign);
            capsuleCollider.center =
                AxisVector(capsuleCollider.direction) * (sign * capsuleCollider.height * 0.5f);

            return capsuleCollider;
        }

        private Joint BuildJoint(Bone bone, Rigidbody parent)
        {
            var joint = bone.Transform.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parent;
            joint.enablePreprocessing = false;

            joint.swing1Limit = new SoftJointLimit { limit = swingLimit };
            joint.swing2Limit = new SoftJointLimit { limit = swingLimit };
            joint.lowTwistLimit = new SoftJointLimit { limit = -twistLimit };
            joint.highTwistLimit = new SoftJointLimit { limit = twistLimit };

            return joint;
        }

        // ── Hierarchy helpers ─────────────────────────────────────────────────

        /// <summary>
        /// How long this bone's segment is: the distance to its nearest simulated child.
        ///
        /// Nearest rather than farthest, deliberately. A pelvis has three simulated children — a
        /// spine and two legs — and reaching for the farthest gives it a capsule that spans a whole
        /// thigh, overlapping the leg it is jointed to. Overlapping capsules on a joint chain are
        /// what the solver spends every frame pushing apart, and the visible result is a body that
        /// buzzes instead of lying still.
        /// </summary>
        private float SegmentLength(Transform bone, List<Transform> simulated)
        {
            float nearest = float.MaxValue;

            for (int i = 0; i < bone.childCount; i++)
            {
                Transform child = bone.GetChild(i);
                if (!simulated.Contains(child)) continue;

                float distance = Vector3.Distance(bone.position, child.position);
                if (distance > 1e-4f && distance < nearest) nearest = distance;
            }

            if (nearest < float.MaxValue) return nearest;

            // A leaf — a head, a foot, a hand. Half of what it hangs off is the only measurement
            // available and is about right for all three.
            return bone.parent != null
                ? Mathf.Max(Vector3.Distance(bone.position, bone.parent.position) * 0.5f, 0.05f)
                : 0.1f;
        }

        /// <summary>The bone's local axis pointing down its own segment, and which way along it.</summary>
        private int LongAxis(Transform bone, List<Transform> simulated, out float sign)
        {
            Vector3 target = Vector3.zero;
            bool found = false;

            for (int i = 0; i < bone.childCount && !found; i++)
            {
                Transform child = bone.GetChild(i);
                if (!simulated.Contains(child)) continue;
                target = child.position;
                found = true;
            }

            if (!found && bone.childCount > 0)
            {
                target = bone.GetChild(0).position;
                found = true;
            }

            if (!found)
            {
                sign = 1f;
                return 1;
            }

            Vector3 local = bone.InverseTransformDirection((target - bone.position).normalized);
            int axis = 0;
            if (Mathf.Abs(local.y) > Mathf.Abs(local[axis])) axis = 1;
            if (Mathf.Abs(local.z) > Mathf.Abs(local[axis])) axis = 2;

            sign = Mathf.Sign(local[axis]);
            return axis;
        }

        private static Vector3 AxisVector(int axis) =>
            axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

        private static Transform NearestKeptAncestor(Transform bone, Dictionary<Transform, Rigidbody> built)
        {
            for (Transform parent = bone.parent; parent != null; parent = parent.parent)
                if (built.ContainsKey(parent)) return parent;

            return null;
        }

        private static int Depth(Transform bone)
        {
            int depth = 0;
            for (Transform parent = bone.parent; parent != null; parent = parent.parent) depth++;

            return depth;
        }

        private static bool IsDescendantOf(Transform bone, Transform ancestor)
        {
            for (Transform parent = bone.parent; parent != null; parent = parent.parent)
                if (parent == ancestor) return true;

            return false;
        }
    }
}
