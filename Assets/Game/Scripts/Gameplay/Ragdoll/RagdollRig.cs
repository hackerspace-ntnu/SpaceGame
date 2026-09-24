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
    /// finds the model's rig, asks <see cref="RagdollSkeleton"/> which of its bones carry enough of
    /// the creature to be worth simulating, and wires shapes and <c>CharacterJoint</c>s down the
    /// hierarchy the model was rigged with. A rig re-exported tomorrow with different bone names
    /// still works.
    /// </para>
    ///
    /// <para>
    /// Bodies go on the BONES, whichever of the project's two kinds of model this is — a skinned
    /// character binding one surface to a skeleton, or a hard-surface creature with rigid pieces
    /// parented onto one. Meshes are leaves and leaves cannot form a chain, so a ragdoll built on
    /// them has no articulation to follow and comes apart; see <see cref="Build"/>.
    /// </para>
    ///
    /// <para>
    /// The skeleton is built on the FIRST limp rather than at spawn, because most bodies never fall
    /// over. Once built its bodies and colliders are kept and switched kinematic rather than
    /// destroyed: rebuilding them costs a mesh walk, and destroying a <c>Rigidbody</c> that a live
    /// <c>CharacterJoint</c> still references is an ordering problem there is no reason to have.
    /// The joints alone are rebuilt on every knockdown, because a joint measures its limits from
    /// the pose it was made in; see <see cref="RebuildJoints"/>. <see cref="Freeze"/> is the one
    /// path that really tears the skeleton down.
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
                 "A body that comes out of the build with fewer than this folds at almost no joints, " +
                 "so it is worth a warning: from the prefab, a two-bone ragdoll and a good one look " +
                 "exactly alike. Usually an asset limit rather than a wiring mistake — PatrolRobot 1 " +
                 "genuinely has four mesh parts and near-rigid skinning, and no threshold here will " +
                 "change that.")]
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

        [Tooltip("Heaviest a body may be relative to a body jointed to it. Light children are raised " +
                 "to meet it. Around ten to one is where PhysX joint chains start to explode.")]
        [SerializeField, Range(2f, 12f)] private float maxJointMassRatio = 8f;

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

        [Tooltip("How far a joint may come apart before PhysX snaps it back, metres. The last line " +
                 "against a stretched mesh when a blast is stronger than the solver can resolve.")]
        [SerializeField] private float projectionDistance = 0.05f;

        [Tooltip("How far past its limit a joint may bend before PhysX snaps it back, degrees.")]
        [SerializeField, Range(1f, 45f)] private float projectionAngle = 10f;

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

        /// <summary>
        /// One collider a bone's body owns, and what it was before the ragdoll took it.
        ///
        /// <para>
        /// A bone does not always get to bring its own collider. Adding a Rigidbody to a transform
        /// makes PhysX adopt every collider beneath it, so a model with an authored collision proxy
        /// — the crab carries twenty-two hand-placed <c>COL_*</c> boxes — hands its whole proxy to
        /// the ragdoll whether or not anyone asked. Those boxes overlap each other by design, the
        /// way any collision hull does, and left out of the self-collision filter they are the
        /// contacts the solver fights every tick and can never win. They have to be known about, so
        /// they are recorded here rather than discovered.
        /// </para>
        ///
        /// <para>
        /// <see cref="WasEnabled"/> is what lets recovery be exact. An authored proxy is normally ON
        /// — it is how the creature blocks and is hit — and a ragdoll that switched everything off
        /// on the way out would take that with it. Only what the rig <see cref="Created"/> is the
        /// rig's to destroy, for the same reason.
        /// </para>
        /// </summary>
        private readonly struct OwnedCollider
        {
            public readonly Collider Collider;
            public readonly bool WasEnabled;
            public readonly bool Created;

            public OwnedCollider(Collider collider, bool created)
            {
                Collider = collider;
                Created = created;
                WasEnabled = collider != null && collider.enabled;
            }
        }

        /// <summary>One simulated bone, and everything hung off it.</summary>
        private sealed class Bone
        {
            public Transform Transform;
            public Rigidbody Body;
            public OwnedCollider[] Colliders;

            /// <summary>The body this bone is jointed to. Null for the root bone.</summary>
            public Rigidbody Parent;

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

        /// <summary>
        /// The root's rotation expressed in the hips' frame, taken the moment the body went limp.
        /// While limp the root is kept at <c>hips × hipsToRoot</c>, so the replicated root carries
        /// the body's orientation to every watcher and they can reconstruct the pelvis from it.
        /// </summary>
        private Quaternion hipsToRoot = Quaternion.identity;

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

        /// <summary>
        /// Is this body dead? Set by the adapter on death and cleared on revive.
        ///
        /// <para>
        /// Only corpses may be evicted by <see cref="RagdollBudget"/>. A living body is limp for a
        /// few seconds and then stands up; freezing one in between left it frozen in its ragdoll
        /// pose while its brain came back on.
        /// </para>
        /// </summary>
        public bool IsCorpse { get; set; }

        /// <summary>Did <see cref="Freeze"/> take this body off physics while it was limp?</summary>
        private bool frozen;

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
        /// Velocity handed to every bone, m/s world space — the whole body thrown at one speed, which
        /// is what makes a blast read as a body thrown rather than a body switched off. Handing it
        /// to the hips alone left the limbs at rest and tore the joints apart.
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
            frozen = false;
            DropLostBones();
            if (bones.Count == 0) return;

            if (!IsLimp)
            {
                PreLimpPosition = transform.position;
                PreLimpRotation = transform.rotation;
                if (Hips != null) hipsToRoot = Quaternion.Inverse(Hips.rotation) * transform.rotation;
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

                RebuildJoints();

                foreach (Bone bone in bones)
                {
                    foreach (OwnedCollider owned in bone.Colliders)
                        if (owned.Collider != null) owned.Collider.enabled = true;

                    bone.Body.detectCollisions = true;

                    // The hips are the one bone a watching machine does not simulate: they are
                    // driven from the replicated root instead (see Drives / PinHipsToRoot), and a
                    // kinematic body is how you drive one without the solver fighting you for it.
                    // Everything hanging off them is dynamic on every machine, which is what keeps
                    // the flail local and free.
                    bool pinned = !Drives && bone == bones[0];
                    bone.Body.isKinematic = pinned;
                    if (pinned) continue;

                    // The whole body starts at the same speed. Giving it all to the hips left every
                    // limb at rest for the solver to accelerate in one step, which it cannot do
                    // without pulling the joints apart — the stretched mesh after every blast.
                    //
                    // Not applied on a watching machine, and that is not an omission. The impulse's
                    // whole effect there arrives already baked into the replicated root — applying
                    // it locally as well would carry the body the distance twice and land it at
                    // double the range.
                    bone.Body.linearVelocity = settled || !Drives ? Vector3.zero : impulse;
                    bone.Body.angularVelocity = Vector3.zero;
                }

                ApplySelfCollision();
                RagdollBudget.Register(this, maxConcurrentRagdolls);
            }
            else if (Drives && !settled)
            {
                // A second knockdown on a body already down adds to the motion it has, and to every
                // bone for the same reason the first one sets every bone.
                foreach (Bone bone in bones)
                    if (!bone.Body.isKinematic) bone.Body.AddForce(impulse, ForceMode.VelocityChange);
            }

            slowSeconds = settled ? settleSeconds : 0f;

            if (settled) limpSeconds = maxLimpSeconds;
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
        /// Across EVERY collider each body owns, not one per bone. A bone that inherited a hand-authored
        /// collision proxy owns several, and an authored hull overlaps itself the way every hull does —
        /// the crab's twenty-two <c>COL_*</c> boxes sat outside a filter that only knew about one shape
        /// per bone, and were twenty-two contacts the solver fought every tick and could never win. They
        /// were invisible to the diagnostic for the same reason, so the audit called the body clean while
        /// it tore itself apart.
        /// </para>
        ///
        /// <para>
        /// Quadratic in the collider count and that is fine: the cap is <see cref="maxBones"/> bodies
        /// with a handful of shapes each, so the worst case is a few hundred calls on the frame a body
        /// goes down, once.
        /// </para>
        /// </summary>
        private void ApplySelfCollision()
        {
            var all = new List<Collider>();
            foreach (Bone bone in bones)
                foreach (OwnedCollider owned in bone.Colliders)
                    if (owned.Collider != null) all.Add(owned.Collider);

            for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
                Physics.IgnoreCollision(all[i], all[j], !selfCollision);
        }

        /// <summary>
        /// Throw away the joints and wire fresh ones from the pose the body is in right now.
        ///
        /// <para>
        /// A joint measures its limits from the pose it was created in. Built once, on the first
        /// knockdown, every later knockdown was judged against whatever the creature happened to be
        /// doing that first time — and a body already past a limit from there was snapped back on
        /// the first physics step. Rebuilding costs a couple of dozen component adds per knockdown.
        /// </para>
        ///
        /// <para>
        /// DestroyImmediate, not Destroy: this runs on the frame the body goes dynamic, and a
        /// deferred destroy would leave two joints on one bone for that frame's physics step.
        /// </para>
        /// </summary>
        private void RebuildJoints()
        {
            // The joint takes its rest frame from the body's physics pose, and the animator has
            // moved the transforms since physics last read them.
            Physics.SyncTransforms();

            foreach (Joint joint in joints)
                if (joint != null) DestroyImmediate(joint);
            joints.Clear();

            Transform[] simulated = BoneTransforms();
            foreach (Bone bone in bones)
            {
                if (bone == bones[0]) continue;

                // A parent destroyed with worn gear leaves the branch hanging off the root bone —
                // the same fallback Build uses for a branch with no simulated ancestor.
                Rigidbody parent = bone.Parent != null ? bone.Parent : bones[0].Body;
                joints.Add(BuildJoint(bone, parent, simulated));
            }
        }

        /// <summary>
        /// Forget bones whose transform has been destroyed since the skeleton was built.
        ///
        /// <para>
        /// The rig is built over transforms this component does not own, and a body wears things
        /// that come and go: a gauntlet is stripped, a backpack is swapped, a held item is
        /// unequipped — each one an <c>Instantiate</c> parented onto a bone and a <c>Destroy</c>
        /// later. <see cref="Build"/> takes any node under the root that carries geometry, so a
        /// skeleton built while gear was on can hold bodies on transforms that are gone by the time
        /// the body gets up.
        /// </para>
        ///
        /// <para>
        /// Reading one of those throws, and where it threw decides how bad it is: from
        /// <see cref="Recover"/> the exception escapes through the revive event, so the rest of the
        /// revive never runs and the player is left dead with their controls never handed back.
        /// That is what this prevents — dropping a lost bone costs the corpse one limb it can no
        /// longer blend, which is invisible next to a player who cannot respawn.
        /// </para>
        /// </summary>
        private void DropLostBones()
        {
            for (int i = bones.Count - 1; i >= 0; i--)
                if (bones[i].Transform == null || bones[i].Body == null) bones.RemoveAt(i);

            // The joints on a destroyed bone died with it, and Unity leaves the list entry null.
            for (int i = joints.Count - 1; i >= 0; i--)
                if (joints[i] == null) joints.RemoveAt(i);

            // The hips are bones[0] by construction, so losing them means the whole body is now
            // rooted at whatever survived. Everything that reads Hips — the root follow, the
            // watcher's pin — wants that same bone, not a null.
            if (Hips == null) Hips = bones.Count > 0 ? bones[0].Transform : null;
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
            if (frozen) return Thaw();
            if (!IsLimp) return new TeleportMove(transform.position, transform.rotation,
                                                 transform.position, transform.rotation);

            DropLostBones();

            Vector3 from = PreLimpPosition;
            Quaternion fromRotation = PreLimpRotation;

            // Snapshot before the bodies are switched off, or the blend starts from whatever the
            // animator writes on its first frame back — which is the standing pose, i.e. no blend
            // at all and a corpse that snaps upright.
            foreach (Bone bone in bones)
            {
                bone.RecoverFrom = bone.Transform.localRotation;
                bone.Body.isKinematic = true;

                // Each collider back to what it was, rather than the body's detectCollisions off
                // wholesale. A bone that inherited an authored proxy is holding the creature's own
                // collision — how it blocks, how it is shot — and switching that off with the
                // ragdoll would leave a creature that got up and could no longer be touched.
                // Everything this rig CREATED was born disabled and goes back to disabled, so a
                // purely skinned body is left exactly as it was before.
                foreach (OwnedCollider owned in bone.Colliders)
                    if (owned.Collider != null) owned.Collider.enabled = owned.WasEnabled;
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
        /// Give a frozen body back to its animation. The bones stay where they were frozen and the
        /// root does not move: there is no body left to measure, and a corpse revived out of the
        /// budget is the only way here.
        /// </summary>
        private TeleportMove Thaw()
        {
            frozen = false;
            if (animator != null) animator.enabled = true;

            return new TeleportMove(transform.position, transform.rotation,
                                    transform.position, transform.rotation);
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
            frozen = true;
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
                // Only what this rig made. An authored collider was here before the ragdoll and has
                // to still be here after it — destroying one would silently delete a piece of the
                // creature's collision the first time the budget evicted its corpse.
                foreach (OwnedCollider owned in bone.Colliders)
                {
                    if (owned.Collider == null) continue;

                    if (owned.Created) Destroy(owned.Collider);
                    else owned.Collider.enabled = owned.WasEnabled;
                }

                if (bone.Body != null) Destroy(bone.Body);
            }
            bones.Clear();

            built = false;
            Hips = null;
        }

        // ── Per-frame ─────────────────────────────────────────────────────────

        /// <summary>
        /// The one place a limp body's root and pelvis are written each physics step: the driver
        /// drags the root after the body (<see cref="FollowHips"/>), a watcher pins the body to the
        /// root the wire is writing (<see cref="PinHipsToRoot"/>).
        ///
        /// <para>
        /// In FixedUpdate because both are physics writes, and the correction has to land in the
        /// same step the solver reads it.
        /// </para>
        ///
        /// <para>
        /// Neither runs once the body has settled. Writing a transform or driving the pelvis wakes
        /// the Rigidbodies involved, so a follow or pin that kept running would put the body
        /// straight back to sleep and wake it again every step — which is not sleeping at all, just
        /// a more elaborate way of never settling, and on a watcher the one machine whose copy of a
        /// corpse never stops shivering. A settled body is not moving, and neither is its root, so
        /// there is nothing left to keep up with.
        /// </para>
        /// </summary>
        private void FixedUpdate()
        {
            if (!IsLimp || (sleepWhenSettled && IsSettled)) return;

            if (Drives) FollowHips();
            else PinHipsToRoot();
        }

        /// <summary>
        /// A watcher's half of the split described on <see cref="Drives"/>: hold the pelvis at the
        /// root the wire is writing — position AND orientation — and let physics do everything else.
        ///
        /// <para>
        /// The hips are kinematic here and everything below them is not, so this drags one bone and
        /// the body flails from it. That is the division wanted: where the body is and which way it
        /// lies come from the machine that owns the truth, and the tumble of the limbs — the part a
        /// watcher can derive perfectly well on its own, and the part that makes a corpse read as a
        /// corpse — stays local and free. Without the rotation a watcher sees an upright pelvis
        /// with the rest of the body hanging off it while the owner's copy lies on its side.
        /// </para>
        ///
        /// <para>
        /// The root IS the hips while a body is limp — see <see cref="FollowHips"/> for why there
        /// is no positional offset between them; reintroducing one here would put every watcher's
        /// copy of the body at a different height from the machine that owns it. The rotation is
        /// undone through <see cref="hipsToRoot"/>, the same offset the driver applied.
        /// </para>
        ///
        /// <para>
        /// MovePosition/MoveRotation rather than direct assignment, because a kinematic body moved
        /// by assignment teleports without telling the solver it moved: the limbs hanging off it
        /// get no sweep between the two poses and are left behind, snapping after the pelvis a step
        /// later. The Move calls are the interpolated move the joints can follow.
        /// </para>
        /// </summary>
        private void PinHipsToRoot()
        {
            if (Hips == null || bones.Count == 0 || bones[0].Body == null) return;

            bones[0].Body.MovePosition(transform.position);
            bones[0].Body.MoveRotation(transform.rotation * Quaternion.Inverse(hipsToRoot));
        }

        private void LateUpdate()
        {
            if (IsLimp)
            {
                limpSeconds += Time.deltaTime;

                bool slow = FastestLinearSpeed <= settleLinearSpeed
                            && FastestAngularSpeed <= settleAngularSpeed;
                slowSeconds = slow ? slowSeconds + Time.deltaTime : 0f;

                if (sleepWhenSettled && IsSettled) SleepBones();
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
        /// Everything is read from the bodies' PHYSICS poses, never from the transforms, and it runs
        /// in FixedUpdate so the write lands before the solver's next step. A transform is the
        /// interpolated pose — a fraction of a step behind the body — and writing it back into the
        /// body teleports the body into the past: every frame, which is jitter and a body that
        /// never settles.
        /// </para>
        ///
        /// <para>
        /// Moving the root moves every transform under it, not only the hips — including branches
        /// that are jointed to the hips without being their children. So every simulated bone is
        /// re-seated to its own body's pose afterwards, and the sync into PhysX before the next step
        /// is a no-op instead of a teleport.
        /// </para>
        ///
        /// <para>
        /// The root takes the hips' orientation too, through <see cref="hipsToRoot"/>, so the
        /// replicated root tells every watcher which way the body lies, not only where.
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
            if (Hips == null || bones.Count == 0 || bones[0].Body == null) return;

            Rigidbody hips = bones[0].Body;
            transform.SetPositionAndRotation(hips.position, hips.rotation * hipsToRoot);

            // Moving the root moved every transform under it. Put each simulated bone back where
            // its body actually is — parents first, which is the order bones are kept in — so the
            // sync into PhysX before the next step is a no-op instead of a teleport.
            foreach (Bone bone in bones)
                if (bone.Body != null)
                    bone.Transform.SetPositionAndRotation(bone.Body.position, bone.Body.rotation);
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
            // stand the creature up sideways. Measured through hipsToRoot, so "forward" is the
            // root's forward as the body now carries it, not whichever way the hip bone's own axes
            // happen to point on this rig.
            Vector3 facing = Vector3.ProjectOnPlane(hipRotation * hipsToRoot * Vector3.forward, Vector3.up);
            if (facing.sqrMagnitude < 1e-4f)
                facing = Vector3.ProjectOnPlane(hipRotation * hipsToRoot * Vector3.up, Vector3.up);

            // Moving the root drags every bone under it, not just the hips, so every bone's world
            // pose is taken first and put back after — the blend starts from where the body lay.
            var worldPoses = new (Vector3, Quaternion)[bones.Count];
            for (int i = 0; i < bones.Count; i++)
                worldPoses[i] = (bones[i].Transform.position, bones[i].Transform.rotation);

            transform.position = grounded;
            if (facing.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);

            for (int i = 0; i < bones.Count; i++)
                bones[i].Transform.SetPositionAndRotation(worldPoses[i].Item1, worldPoses[i].Item2);
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
        /// Find the model's rig, decide which of its bones are worth simulating, and wire bodies and
        /// shapes through what survives, noting which body each bone hangs from. Runs once, on the
        /// first limp; the joints themselves are made per knockdown by <see cref="RebuildJoints"/>.
        ///
        /// <para>
        /// Bodies go on the RIG, never on the pieces of geometry hanging off it, and that is the
        /// whole of this pass. Both kinds of model in this project have a rig — a skinned character
        /// binds its surface to one, a hard-surface creature parents rigid pieces onto one — and
        /// only the rig knows which end of a limb bends. Bodies on the meshes instead produced a
        /// star: no mesh is another mesh's ancestor, so nothing could find a parent to joint to and
        /// every limb ended up connected straight to one hub across the width of the body, while
        /// the hip-knee-ankle chain the model was actually rigged with sat unused beside it. The
        /// golem came out of that with seventeen of its eighteen joints on the pelvis, the crab with
        /// nineteen of twenty on the carapace, and both flew apart on the first blast.
        /// </para>
        /// </summary>
        private void Build()
        {
            built = true;

            Hierarchy rig = FlattenRig();
            Dictionary<Transform, float> importance = MeasureRig(rig);
            CandidateCount = importance.Count;

            if (importance.Count == 0)
            {
                Debug.LogWarning($"{name}: RagdollRig found no rig to build on — this body cannot " +
                                 "ragdoll. Expected a SkinnedMeshRenderer with bones, or bones with " +
                                 "MeshFilters parented onto them, somewhere under it.", this);
                return;
            }

            List<Transform> kept = Select(importance, rig);
            if (kept.Count == 0) return;

            // Not a failure to abort on — a model can genuinely have nothing more to give, and
            // PatrolRobot 1 ("Robert") maxes out at two bones because it has four mesh parts and
            // near-rigid skinning. Worth saying out loud, because from the prefab a two-bone
            // ragdoll and a good one look exactly alike.
            if (kept.Count < minimumUsefulBones)
                Debug.LogWarning($"{name}: RagdollRig kept only {kept.Count} bone(s) of " +
                                 $"{importance.Count} on the rig — this body will fold at almost " +
                                 "no joints. Usually an asset limit rather than a wiring mistake.",
                                 this);

            Hips = kept[0];
            standingHipHeight = Mathf.Max(Hips.position.y - transform.position.y, 0f);

            float keptWeight = 0f;
            foreach (Transform bone in kept) keptWeight += importance[bone];

            var bodies = new Dictionary<Transform, Rigidbody>();
            foreach (Transform bone in kept)
            {
                Bone made = BuildBone(bone, kept, rig, importance[bone], keptWeight);
                bones.Add(made);
                bodies[bone] = made.Body;

                Transform parent = NearestKeptAncestor(bone, bodies);

                // A branch whose own root is not below any other simulated bone hangs off the
                // ragdoll's root bone. Rigs meet at a node that draws nothing — the ostrich's legs,
                // spine and neck all hang off a Root with no mesh of its own, so it carries no bulk
                // and is not worth a body — and a leg jointed to the torso is what a hand-built
                // ragdoll does anyway. Giving that empty node the body instead makes the root of
                // the whole chain the lightest thing in it: 0.6 kg holding a 57 kg spine, which is
                // the mass ratio minBoneMass exists to prevent.
                if (parent == null && bone != Hips) parent = Hips;

                if (parent != null && bodies.TryGetValue(parent, out Rigidbody parentBody))
                    made.Parent = parentBody;
            }

            var masses = new float[bones.Count];
            var parents = new int[bones.Count];
            for (int i = 0; i < bones.Count; i++)
            {
                masses[i] = bones[i].Body.mass;
                parents[i] = bones[i].Parent != null
                    ? bones.FindIndex(b => b.Body == bones[i].Parent)
                    : (i == 0 ? -1 : 0);
            }

            float[] balanced = RagdollSkeleton.ClampMassRatios(masses, parents, maxJointMassRatio);
            for (int i = 0; i < bones.Count; i++) bones[i].Body.mass = balanced[i];
        }

        /// <summary>
        /// Every transform under this one, parents before children, with the rig picked out of it.
        ///
        /// <para>
        /// Flattened because the two questions this pass asks — which nodes articulate the model,
        /// and which of them carries each piece of geometry — are both answered in one sweep over a
        /// parent-index array, and both live in <see cref="RagdollSkeleton"/> where they can be
        /// tested without a scene.
        /// </para>
        /// </summary>
        private sealed class Hierarchy
        {
            public Transform[] Nodes;
            public Dictionary<Transform, int> Index;

            /// <summary>Parent index per node, <c>-1</c> for the root.</summary>
            public int[] Parents;

            /// <summary>The rig node at or above each node — whose body its geometry ends up on.</summary>
            public int[] Carrier;
        }

        private Hierarchy FlattenRig()
        {
            var nodes = new List<Transform>();
            Flatten(transform, nodes);

            var index = new Dictionary<Transform, int>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++) index[nodes[i]] = i;

            var parents = new int[nodes.Count];
            var draws = new bool[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                Transform parent = nodes[i].parent;
                parents[i] = parent != null && index.TryGetValue(parent, out int p) ? p : -1;
                draws[i] = MeshOf(nodes[i]) != null;
            }

            bool[] isRigNode = RagdollSkeleton.SelectRigNodes(parents, draws);

            // A skinned bone draws nothing and has no geometry beneath it either — the surface is
            // one mesh stretched over the whole skeleton, parented somewhere else entirely — so the
            // structural rule above cannot see it. The renderer names its own bones; take them.
            foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsAttachment(renderer)) continue;

                Transform[] rigBones = renderer.bones;
                if (rigBones == null) continue;

                foreach (Transform bone in rigBones)
                    if (bone != null && index.TryGetValue(bone, out int b)) isRigNode[b] = true;
            }

            // Never this component's own transform. It is the entity, not a bone: FollowHips moves
            // it to wherever the hips ended up and re-seats the bones afterwards, which is a no-op
            // if they are the same object — so a body whose root were its own hips would flail
            // twenty metres away and leave its transform, its NetworkTransform and its save record
            // standing where it died.
            isRigNode[0] = false;

            return new Hierarchy
            {
                Nodes = nodes.ToArray(),
                Index = index,
                Parents = parents,
                Carrier = RagdollSkeleton.NearestRigNode(parents, isRigNode),
            };
        }

        /// <summary>
        /// Every transform this body is made of.
        ///
        /// <para>
        /// <b>Subtrees marked <see cref="SpaceGame.Items.BodyAttachment"/> are skipped whole.</b>
        /// Worn and held gear is parented onto the skeleton, so from the hierarchy alone it looks
        /// exactly like a bone: a node that draws nothing with geometry beneath it, which is the
        /// rule <see cref="RagdollSkeleton.SelectRigNodes"/> selects on. On a player wearing the
        /// jetpack with the pack shouldered, nine of fourteen candidates were gear — the jetpack's
        /// root and models, and the pack's four flap hinges — so death gave the jetpack a joint and
        /// simulated the pack's flaps as limbs. Gear cut here is not lost: it stays parented to the
        /// bone it hangs off and rides that bone, which is what it did while the body was alive.
        /// </para>
        /// </summary>
        private void Flatten(Transform node, List<Transform> into)
        {
            into.Add(node);

            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                if (child.GetComponent<SpaceGame.Items.BodyAttachment>() != null) continue;

                Flatten(child, into);
            }
        }

        /// <summary>
        /// Whether this renderer belongs to something the body is carrying rather than to the body.
        ///
        /// <para>
        /// The two skinned passes reach for renderers with <c>GetComponentsInChildren</c> rather
        /// than through the flattened hierarchy, so skipping a subtree in <see cref="Flatten"/> does
        /// not hide it from them. <c>Select</c> takes its candidates straight from the importance
        /// map, so a bone that only ever appeared through a skinned renderer is still selectable —
        /// which is how a worn wingsuit could put its own bones in the wearer's ragdoll.
        /// </para>
        /// </summary>
        private bool IsAttachment(Component part) =>
            part != null && SpaceGame.Items.BodyAttachment.Covers(part.transform, transform);

        /// <summary>
        /// How much of the creature each bone is, in cubic metres.
        ///
        /// <para>
        /// One unit for both kinds of rig, which is what makes them comparable at all. A rigid part
        /// states its own bulk — the bounds of the mesh it draws. A skinned bone's share of its
        /// renderer's vertex weight, scaled by that renderer's bounds, states the same thing about
        /// the surface it carries. The two can then be added, so a bolted-on plate and the skin it
        /// is bolted to both count towards the bone that moves them.
        /// </para>
        ///
        /// <para>
        /// Raw vertex weight cannot do this, and the failure is not subtle. Weight measures SURFACE,
        /// which only stands in for mass while one density covers the model — and a model built from
        /// several meshes has no such thing. The ostrich's neck is eleven separate densely-tessellated
        /// vertebrae, so by weight a single vertebra outscored the whole torso, every body bone fell
        /// under the weight floor, and the bird's ragdoll came out as eleven neck segments and
        /// nothing else. Scaling by each renderer's own bounds is what fixes that.
        /// </para>
        /// </summary>
        private Dictionary<Transform, float> MeasureRig(Hierarchy rig)
        {
            var importance = new Dictionary<Transform, float>();
            bool skin = false, parts = false;

            foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsAttachment(renderer)) continue;

                Mesh mesh = renderer.sharedMesh;
                Transform[] rigBones = renderer.bones;
                if (mesh == null || rigBones == null || rigBones.Length == 0) continue;

                float volume = WorldVolume(mesh.bounds.size, renderer.transform.lossyScale);
                if (volume <= 0f) continue;

                // Shares within THIS renderer, so the volume they are scaled by is the one they
                // actually describe. The old measure summed raw weights across every renderer at
                // once and had to be all-or-nothing about readability because of it — a rig with
                // one unreadable mesh silently produced a skeleton out of whichever ones happened
                // to be readable. Per-renderer shares make that choice per-renderer too, and a
                // renderer measured by bone length still comes out in cubic metres.
                Dictionary<Transform, float> shares = mesh.isReadable
                    ? WeightShares(mesh, rigBones)
                    : LengthShares(rigBones);

                float total = 0f;
                foreach (float share in shares.Values) total += share;
                if (total <= 0f) continue;

                foreach (KeyValuePair<Transform, float> share in shares)
                {
                    importance.TryGetValue(share.Key, out float carried);
                    importance[share.Key] = carried +
                        RagdollSkeleton.CarriedVolume(share.Value, total, volume);
                }

                skin = true;
            }

            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;

                // Already covered by the index lookup below — a skipped subtree is not in it — but
                // said out loud, because the index is an implementation detail of the flatten and
                // this rule is not.
                if (IsAttachment(filter)) continue;
                if (!rig.Index.TryGetValue(filter.transform, out int node)) continue;

                int carrier = rig.Carrier[node];
                if (carrier < 0) continue;

                float volume = WorldVolume(mesh.bounds.size, filter.transform.lossyScale);
                if (volume <= 0f) continue;

                Transform bone = rig.Nodes[carrier];
                importance.TryGetValue(bone, out float carried);
                importance[bone] = carried + volume;
                parts = true;
            }

            Measure = skin && parts ? "skin+parts" : skin ? "skin" : parts ? "parts" : "none";

            return importance;
        }

        /// <summary>Each bone's share of a readable mesh, by the vertex weight bound to it.</summary>
        private static Dictionary<Transform, float> WeightShares(Mesh mesh, Transform[] rigBones)
        {
            var shares = new Dictionary<Transform, float>();

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

                    shares.TryGetValue(bone, out float carried);
                    shares[bone] = carried + weight.weight;
                }
            }

            return shares;
        }

        /// <summary>
        /// Each bone's share of a mesh that cannot be read, by how long it is.
        ///
        /// A mesh imported without Read/Write Enabled exposes no vertex data at runtime, and most
        /// of this project's FBXs are imported that way because nothing else needed to read them.
        /// Length stands in well: the reason weight works is that fingers are small, and the reason
        /// length works is the same one.
        /// </summary>
        private static Dictionary<Transform, float> LengthShares(Transform[] rigBones)
        {
            var shares = new Dictionary<Transform, float>();

            foreach (Transform bone in rigBones)
            {
                if (bone == null || shares.ContainsKey(bone)) continue;

                shares[bone] = bone.parent != null
                    ? Mathf.Max(Vector3.Distance(bone.position, bone.parent.position), 0.01f)
                    : 0.01f;
            }

            return shares;
        }

        private static float WorldVolume(Vector3 size, Vector3 scale) =>
            Mathf.Max(Mathf.Abs(size.x * scale.x), 1e-4f)
            * Mathf.Max(Mathf.Abs(size.y * scale.y), 1e-4f)
            * Mathf.Max(Mathf.Abs(size.z * scale.z), 1e-4f);

        /// <summary>
        /// The bones worth simulating, ordered so every bone comes after its ancestors — which is
        /// what lets the joint pass find each bone's parent body already built.
        ///
        /// <para>
        /// The first bone out is the ROOT of the ragdoll, which makes the order load-bearing rather
        /// than cosmetic. Branches that share no simulated ancestor hang off it (see
        /// <see cref="Build"/>), so picking it by accident is how a body ends up rooted at a neck
        /// vertebra with the rest of the animal treated as something else's branch.
        /// </para>
        /// </summary>
        private List<Transform> Select(Dictionary<Transform, float> importance, Hierarchy rig)
        {
            var candidates = new List<Transform>(importance.Keys);
            var weights = new float[candidates.Count];
            for (int i = 0; i < candidates.Count; i++) weights[i] = importance[candidates[i]];

            bool[] keep = RagdollSkeleton.SelectBones(weights, minBoneWeightFraction);

            var selected = new List<Transform>();
            for (int i = 0; i < candidates.Count; i++)
                if (keep[i]) selected.Add(candidates[i]);

            // Heaviest first, then cut to the cap — so a rig with unusual weights loses its least
            // significant bones rather than an arbitrary tail. A bone cut here is not a piece lost:
            // its geometry is still parented to it and it is still parented to a bone that IS
            // simulated, so it rides along instead of being left behind in mid-air.
            selected.Sort((a, b) => importance[b].CompareTo(importance[a]));
            if (selected.Count > maxBones) selected.RemoveRange(maxBones, selected.Count - maxBones);

            // Shallowest first, so the joint pass finds every bone's parent body already built —
            // and, among equals, the heaviest BRANCH first, so the bone the rest of the body hangs
            // from is the one carrying the creature. Its own bulk is the wrong tiebreak: a thigh
            // outweighs a chest, which is how PatrolRobot 1 came out rooted at its right leg with
            // the left leg jointed to it. See RagdollSkeleton.SubtreeBulk.
            float[] branch = BranchBulk(importance, rig);
            selected.Sort((a, b) =>
            {
                int byDepth = Depth(a).CompareTo(Depth(b));
                if (byDepth != 0) return byDepth;

                return Branch(branch, rig, b).CompareTo(Branch(branch, rig, a));
            });

            return selected;
        }

        /// <summary>What hangs below each node of the rig, so branch roots can be compared.</summary>
        private static float[] BranchBulk(Dictionary<Transform, float> importance, Hierarchy rig)
        {
            var bulk = new float[rig.Nodes.Length];
            for (int i = 0; i < rig.Nodes.Length; i++)
                bulk[i] = importance.TryGetValue(rig.Nodes[i], out float carried) ? carried : 0f;

            return RagdollSkeleton.SubtreeBulk(rig.Parents, bulk);
        }

        private static float Branch(float[] branch, Hierarchy rig, Transform bone) =>
            rig.Index.TryGetValue(bone, out int node) ? branch[node] : 0f;

        private Bone BuildBone(Transform bone, List<Transform> kept, Hierarchy rig,
                               float weight, float totalWeight)
        {
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

            return new Bone
            {
                Transform = bone,
                Body = body,
                Colliders = OwnColliders(bone, kept, rig),
                RecoverFrom = bone.localRotation,
            };
        }

        /// <summary>
        /// Every collider this bone's body will own — the authored ones it inherits, or one shaped
        /// for it if there are none.
        ///
        /// <para>
        /// Inherits rather than adds, wherever it can. Adding a Rigidbody makes PhysX adopt every
        /// collider beneath the transform regardless, so on a model with a hand-authored collision
        /// proxy the choice is not whether the ragdoll uses those shapes but whether it KNOWS about
        /// them. Left undiscovered they are outside the self-collision filter, and an authored hull
        /// overlaps itself the way every hull does — the crab's twenty-two <c>COL_*</c> boxes were
        /// twenty-two contacts the solver fought every tick and could never win. Discovered, they
        /// are better shapes than anything derived here, because somebody fitted them by hand.
        /// </para>
        ///
        /// <para>
        /// The set stops at the next simulated bone, because that is exactly where PhysX stops:
        /// a collider under a deeper bone belongs to that bone's body, not to this one.
        /// </para>
        /// </summary>
        private OwnedCollider[] OwnColliders(Transform bone, List<Transform> kept, Hierarchy rig)
        {
            var owned = new List<OwnedCollider>();
            bool shaped = false;

            foreach (Collider collider in bone.GetComponentsInChildren<Collider>(true))
            {
                // Triggers generate no contacts, so they cannot be what tears a body apart — and
                // they are usually somebody else's: an interaction volume, a mount's boarding zone.
                if (collider.isTrigger) continue;
                if (NearestBone(collider.transform, kept) != bone) continue;
                if (UnderAnotherBody(collider.transform, bone)) continue;

                owned.Add(new OwnedCollider(collider, created: false));

                // Adopted for FILTERING either way — PhysX attaches it to this body whatever this
                // thinks of it — but only a collider with nothing DRAWN at or below it is the
                // bone's own SHAPE. Anything that leads to a renderer is a prop riding along: the
                // patrol robots carry a sword and a gun under the right hand, and counting those
                // left four of them with a fourteen-kilo hand that had no collision of its own at
                // all. Below it, not on it — the sword's collider sits on a bare object whose mesh
                // hangs one level down, so asking only about the collider's own GameObject still
                // took the weapon for a hand. A hull authored by hand, like the crab's COL_* boxes,
                // draws nothing anywhere under it, which is exactly what this keeps.
                shaped |= collider.GetComponentInChildren<Renderer>(true) == null;
            }

            if (!shaped)
                owned.Add(new OwnedCollider(CreateCollider(bone, kept, rig), created: true));

            return owned.ToArray();
        }

        /// <summary>
        /// A shape for a bone that brought none: a box around the geometry it carries, or a capsule
        /// down its length if it carries none of its own.
        ///
        /// <para>
        /// The split is between the two kinds of rig and each side gets the better answer. A rigid
        /// bone moves specific pieces, so the box around those pieces is not an approximation at
        /// all. A skinned bone draws nothing — the mesh is one surface over the whole skeleton — so
        /// the only measurement available is how far it is to the next joint, and a capsule down
        /// that line is the honest guess.
        /// </para>
        /// </summary>
        private Collider CreateCollider(Transform bone, List<Transform> kept, Hierarchy rig)
        {
            if (TryCarriedBounds(bone, rig, out Bounds carried))
            {
                var box = bone.gameObject.AddComponent<BoxCollider>();
                box.center = carried.center;

                // Floored on every axis. Plenty of these parts are flat — a shroud, a plate, a
                // panel — and a box with a zero extent is one PhysX warns about and then tunnels
                // straight through at the speed the gauntlet throws bodies.
                float thinnest = minRadius / Mathf.Max(Mathf.Abs(bone.lossyScale.x),
                                 Mathf.Max(Mathf.Abs(bone.lossyScale.y),
                                           Mathf.Max(Mathf.Abs(bone.lossyScale.z), 1e-5f)));
                box.size = new Vector3(Mathf.Max(carried.size.x, thinnest),
                                       Mathf.Max(carried.size.y, thinnest),
                                       Mathf.Max(carried.size.z, thinnest));
                box.enabled = false;

                return box;
            }

            // Local units, not world. An FBX in this project can import at a lossyScale of 100, and
            // a collider is sized in local space and then scaled by it — so a capsule authored in
            // metres would come out a hundred times too big on exactly those rigs. The box above
            // needs no such correction: its corners were brought into this bone's local space,
            // which already removed the scale.
            float scale = Mathf.Max(Mathf.Abs(bone.lossyScale.x),
                          Mathf.Max(Mathf.Abs(bone.lossyScale.y), Mathf.Abs(bone.lossyScale.z)));
            if (scale < 1e-5f) scale = 1f;

            float length = SegmentLength(bone, kept);
            Vector2 capsule = RagdollSkeleton.CapsuleSize(length, length * limbAspect, minRadius);

            var capsuleCollider = bone.gameObject.AddComponent<CapsuleCollider>();
            capsuleCollider.radius = capsule.x / scale;
            capsuleCollider.height = Mathf.Max(capsule.y, capsule.x * 2f) / scale;
            capsuleCollider.direction = LongAxis(bone, kept, out float sign);
            capsuleCollider.center =
                AxisVector(capsuleCollider.direction) * (sign * capsuleCollider.height * 0.5f);
            capsuleCollider.enabled = false;

            return capsuleCollider;
        }

        /// <summary>
        /// The box around every mesh this bone moves, in the bone's own local space.
        ///
        /// Its own meshes and those of any node below it that is not itself simulated — the pieces
        /// that will travel with this body and nothing else. Corners rather than centre-and-extents,
        /// because a part is routinely rotated relative to the bone it hangs on and an axis-aligned
        /// box built from a rotated box's extents is not the same box.
        /// </summary>
        private bool TryCarriedBounds(Transform bone, Hierarchy rig, out Bounds local)
        {
            local = default;
            bool any = false;

            foreach (MeshFilter filter in bone.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                if (!rig.Index.TryGetValue(filter.transform, out int node)) continue;

                int carrier = rig.Carrier[node];
                if (carrier < 0 || rig.Nodes[carrier] != bone) continue;

                Bounds bounds = mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));

                    point = bone.InverseTransformPoint(filter.transform.TransformPoint(point));

                    if (any) local.Encapsulate(point);
                    else { local = new Bounds(point, Vector3.zero); any = true; }
                }
            }

            return any;
        }

        /// <summary>
        /// Is there another Rigidbody between this collider and the bone — something carried rather
        /// than something the bone is made of?
        ///
        /// <para>
        /// A held weapon is the case that matters. Its colliders sit under the hand bone but belong
        /// to the WEAPON's own body, so counting them as the hand's leaves the hand thinking it
        /// brought a shape and skipping the one it needed: four of the robots came out with a
        /// fourteen-kilo hand and no collision at all on it. PhysX decides this by the nearest
        /// Rigidbody above a collider, and so does this.
        /// </para>
        /// </summary>
        private static bool UnderAnotherBody(Transform node, Transform bone)
        {
            for (Transform at = node; at != null && at != bone; at = at.parent)
                if (at.GetComponent<Rigidbody>() != null) return true;

            return false;
        }

        /// <summary>The simulated bone this transform belongs to: itself if it is one, else the nearest above.</summary>
        private static Transform NearestBone(Transform node, List<Transform> kept)
        {
            for (Transform at = node; at != null; at = at.parent)
                if (kept.Contains(at)) return at;

            return null;
        }

        private static Mesh MeshOf(Transform node)
        {
            var filter = node.GetComponent<MeshFilter>();

            return filter != null ? filter.sharedMesh : null;
        }

        private Joint BuildJoint(Bone bone, Rigidbody parent, IList<Transform> simulated)
        {
            var joint = bone.Transform.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parent;
            joint.enablePreprocessing = false;
            joint.enableProjection = true;
            joint.projectionDistance = projectionDistance;
            joint.projectionAngle = projectionAngle;

            RagdollSkeleton.JointAxes(LocalBoneDirection(bone.Transform, simulated),
                                      out Vector3 twist, out Vector3 swing);
            joint.axis = twist;
            joint.swingAxis = swing;

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

        /// <summary>
        /// Closer than this, a child says nothing about which way its bone runs. Geometry, not a
        /// tunable: a millimetre is far below any bone length and far above float noise.
        /// </summary>
        private const float MinChildOffset = 1e-3f;

        /// <summary>
        /// Which way the bone runs, in its own local space: toward its first simulated child, else
        /// its first other child, else onward from its parent (a hand, a head, a foot).
        ///
        /// <para>
        /// Children sitting on the bone's own origin are skipped. A hard-surface rig usually hangs
        /// its mesh piece exactly at the bone, and that offset is zero — read as a direction it
        /// sends <see cref="RagdollSkeleton.JointAxes"/> to its fallback axis, which is the same
        /// across-the-limb twist this exists to prevent.
        /// </para>
        /// </summary>
        private static Vector3 LocalBoneDirection(Transform bone, IList<Transform> simulated)
        {
            if (TryChildDirection(bone, simulated, out Vector3 local)) return local;
            if (TryChildDirection(bone, null, out local)) return local;

            return bone.parent != null
                ? bone.InverseTransformDirection(bone.position - bone.parent.position)
                : Vector3.up;
        }

        /// <summary>
        /// Bone-local direction to the first child set off from the bone's origin, among the
        /// simulated ones when <paramref name="simulated"/> is given, else among all of them.
        /// </summary>
        private static bool TryChildDirection(Transform bone, IList<Transform> simulated, out Vector3 local)
        {
            for (int i = 0; i < bone.childCount; i++)
            {
                Transform child = bone.GetChild(i);
                if (simulated != null && !simulated.Contains(child)) continue;

                Vector3 offset = child.position - bone.position;
                if (offset.sqrMagnitude < MinChildOffset * MinChildOffset) continue;

                local = bone.InverseTransformDirection(offset);
                return true;
            }

            local = Vector3.zero;
            return false;
        }

        /// <summary>The bone's local axis pointing down its own segment, and which way along it.</summary>
        private static int LongAxis(Transform bone, IList<Transform> simulated, out float sign)
        {
            Vector3 local = LocalBoneDirection(bone, simulated);
            int axis = 0;
            if (Mathf.Abs(local.y) > Mathf.Abs(local[axis])) axis = 1;
            if (Mathf.Abs(local.z) > Mathf.Abs(local[axis])) axis = 2;

            sign = local[axis] < 0f ? -1f : 1f;
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
    }
}
