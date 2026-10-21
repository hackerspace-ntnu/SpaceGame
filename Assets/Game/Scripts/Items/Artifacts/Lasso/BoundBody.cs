using System.Collections.Generic;
using System.Linq;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay.Status;
using SpaceGame.Presentation;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Items
{
    /// <summary>
    /// How a tied body behaves as a thing in the world: its weight, how it slides, and the rope it is dragged by. Serialized on
    /// the lasso prefab and handed to every body the lasso ties.
    /// </summary>
    [System.Serializable]
    public class TiedBodyPhysics
    {
        [Tooltip("The body's mass while tied, in kg. What the rope swings and what lands when it is dropped.")]
        [SerializeField, Min(1f)] private float mass = 70f;

        [Tooltip("How easily it slides over the ground when dragged. 0 is ice.")]
        [SerializeField, Range(0f, 1f)] private float friction = 0.35f;

        [Tooltip("How quickly it stops sliding and swinging once nothing pulls it.")]
        [SerializeField, Min(0f)] private float linearDamping = 0.3f;

        [SerializeField, Min(0f)] private float angularDamping = 2f;

        [Tooltip("How stiffly the rope holds at its length. 0 is a rope that cannot stretch at all; a little give takes the " +
                 "snap out of a drag starting.")]
        [SerializeField, Min(0f)] private float ropeSpring = 4000f;

        [Tooltip("How quickly the rope's give settles. Near 2·√(spring·mass) it settles without bouncing; well under it, a " +
                 "body yanked hard slingshots past the hand (played: 2500/250 threw a Nomad 3 m past its dragger).")]
        [SerializeField, Min(0f)] private float ropeDamper = 1000f;

        public float Mass => mass;
        public float Friction => friction;
        public float LinearDamping => linearDamping;
        public float AngularDamping => angularDamping;
        public float RopeSpring => ropeSpring;
        public float RopeDamper => ropeDamper;
    }

    /// <summary>
    /// A body under <see cref="StatusKind.Bound"/>: forced to its knees and over onto its face, hogtied (<see cref="TiedPose"/>),
    /// the rope going on round its wrists, its ankles and six turns round its middle while the binder works
    /// (<see cref="HogtieWrap"/>), struggling harder the longer it is left alone — and, while it lies, a real
    /// <see cref="Rigidbody"/> that a rope drags, a jetpack lifts and the ground stops.
    ///
    /// <para>
    /// <b>It listens rather than being told</b>, the shape <see cref="ShockedVisual"/> has: it follows its own receiver's
    /// <see cref="StatusReceiver.StatusChanged"/> and runs on every machine. Added by the lasso in <c>Present</c>, on every
    /// machine, before the server's announcement can arrive; it also puts the <see cref="TiedCaptive"/> interaction and the
    /// <see cref="CaptiveHaul"/> shoulder carry on the body.
    /// </para>
    /// <para>
    /// <b>Unity's physics moves it, on the one machine that owns the body.</b> There the standing capsule is swapped for a
    /// lying one along the drawn body, the agent's legs are taken (<see cref="ISelfDrivingMotor.SuspendSelfDrive"/>) and the
    /// root's <see cref="Rigidbody"/> is made dynamic; a rope is a <see cref="ConfigurableJoint"/> from the knot to the hand
    /// holding it (<see cref="Hold"/>). Everybody else sees the root through its NetworkTransform, which syncs all three
    /// rotation axes, and poses the body on it. Everything is given back exactly as found when the ropes come off.
    /// </para>
    /// <para>
    /// <b>Carried over a shoulder</b> (<see cref="BeginCarry"/>, every machine, from <see cref="CaptiveHaul"/>) the root is
    /// the carrier's to place: the body is made kinematic where it is owned, its colliders stop touching the carrier's, and
    /// the lying collider goes away — a body on a shoulder is not something to trip over.
    /// </para>
    /// <para>
    /// <b>After everything else in the frame</b> (execution order 1000): the animator has written its pose, and only then is
    /// the body laid down and the rope laid on it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class BoundBody : MonoBehaviour
    {
        private StatusReceiver receiver;
        private HogtieWrapLook wrapLook;
        private TiedPoseLook poseLook;
        private TiedBodyPhysics physics;
        private Material ropeMaterial;

        /// <summary>Seconds the next tie takes to go on, and who is putting it on. Spent by the next <see cref="Show"/>.</summary>
        private float takedownSeconds;
        private Transform takenDownBy;

        private readonly TiedPose pose = new TiedPose();
        private readonly HogtieWrap wrap = new HogtieWrap();

        /// <summary>Degrees the lying body is turned from its root's forward, fixed as it goes down.</summary>
        private float yaw;
        private Vector3 lastRoot;

        private GameObject lyingBody;
        private readonly List<Collider> standingColliders = new List<Collider>();

        // The root's body as it was before it was laid down, on the machine that owns it.
        private Rigidbody body;
        private bool physical;
        private bool wasKinematic, hadGravity;
        private float hadMass, hadLinearDamping, hadAngularDamping;
        private RigidbodyInterpolation hadInterpolation;
        private RigidbodyConstraints hadConstraints;
        private ISelfDrivingMotor legs;

        private ConfigurableJoint rope;
        private Rigidbody heldBy;

        // Over somebody's shoulder: who, the collider pairs told to ignore each other, and the root body as it was.
        private GameObject carriedBy;
        private readonly List<(Collider, Collider)> ghosted = new List<(Collider, Collider)>();
        private bool carryWasKinematic;
        private RigidbodyInterpolation carryInterpolation;

        /// <summary>Is the body tied and drawn tied right now? False once the ropes are off, even while it gets up.</summary>
        public bool IsShown => wrap.IsShown || (pose.IsPosed && !pose.IsReleasing);

        /// <summary>The rope round the body. For tests, and for the binder's rope while it goes on.</summary>
        public HogtieWrap Wrap => wrap;

        /// <summary>The pose. For tests.</summary>
        public TiedPose Pose => pose;

        /// <summary>The collider laid along the body while it lies. For tests.</summary>
        public Collider LyingCollider => lyingBody != null ? lyingBody.GetComponent<Collider>() : null;

        /// <summary>Is the body under Unity's physics here — tied, and this machine owns it?</summary>
        public bool IsPhysical => physical;

        /// <summary>The rope joint dragging the body, on the machine that owns it. For tests.</summary>
        public ConfigurableJoint RopeJoint => rope;

        /// <summary>Who has this body over their shoulder, or null.</summary>
        public GameObject CarriedBy => carriedBy;

        /// <summary>How far the body is over its carrier's shoulder, 0 to 1. Set every frame by whoever carries it.</summary>
        public float Shouldered
        {
            get => pose.Shouldered;
            set => pose.Shouldered = Mathf.Clamp01(value);
        }

        /// <summary>The point of the body, in the root's own space, that rests on a carrier's shoulder.</summary>
        public Vector3 CarryAnchor => pose.CarryAnchor;

        /// <summary>Where a rope that leads this body is tied: the knot, once there is one.</summary>
        public Vector3 RopePoint => wrap.IsShown ? wrap.Knot : transform.position;

        /// <summary>This body's look, if it has one.</summary>
        public static BoundBody Of(GameObject body) => body != null ? body.GetComponentInParent<BoundBody>() : null;

        /// <summary>Give <paramref name="body"/>'s receiver the look, unless it has it already.</summary>
        /// <param name="material">The tying rope's own material, so the coils match it. May be null.</param>
        /// <param name="tieSeconds">
        /// How long the tie about to land takes to put on: the takedown and the rope going on are
        /// played over it. Zero lays the body straight down.
        /// </param>
        /// <param name="binder">Who is tying them. The body goes down facing away from them, feet to their hands.</param>
        /// <param name="haul">How the tied body is carried over the shoulder (<see cref="CaptiveHaul"/>). Null: it cannot be.</param>
        /// <param name="bodyPhysics">How the tied body weighs, slides and hangs on a rope. Null: the defaults.</param>
        public static void EnsureOn(StatusReceiver body, HogtieWrapLook wrap, TiedPoseLook pose,
                                    Material material = null, float tieSeconds = 0f, Transform binder = null,
                                    CaptiveHaulSettings haul = null, TiedBodyPhysics bodyPhysics = null)
        {
            if (body == null) return;

            if (body.GetComponent<TiedCaptive>() == null) body.gameObject.AddComponent<TiedCaptive>();
            CaptiveHaul.EnsureOn(body.gameObject, haul);

            BoundBody bound = body.GetComponent<BoundBody>();
            if (bound == null)
            {
                bound = body.gameObject.AddComponent<BoundBody>();
                bound.wrapLook = wrap;
                bound.poseLook = pose;
                bound.physics = bodyPhysics ?? new TiedBodyPhysics();
                bound.ropeMaterial = material;
                bound.Expect(tieSeconds, binder);
                bound.Listen();
                return;
            }

            bound.wrapLook ??= wrap;
            bound.poseLook ??= pose;
            bound.physics ??= bodyPhysics ?? new TiedBodyPhysics();
            if (bound.ropeMaterial == null) bound.ropeMaterial = material;
            bound.Expect(tieSeconds, binder);
        }

        /// <summary>The next tie to land is this one. A tie already on is not replayed.</summary>
        private void Expect(float tieSeconds, Transform binder)
        {
            if (IsShown) return;

            takedownSeconds = tieSeconds;
            takenDownBy = binder;
        }

        private void OnEnable() => Listen();

        private void OnDisable()
        {
            if (receiver != null) receiver.StatusChanged -= OnStatusChanged;
            receiver = null;
            Hide();
            pose.End();
        }

        /// <summary>
        /// Subscribe, and lay the body down now if it is already tied — a reactivated chunk, or a
        /// look added after the condition landed. Called from <see cref="EnsureOn"/> as well as
        /// OnEnable, because AddComponent runs OnEnable before the looks are handed over.
        /// </summary>
        private void Listen()
        {
            if (receiver != null || wrapLook == null || poseLook == null) return;

            receiver = StatusReceiver.Of(gameObject);
            if (receiver == null) return;

            receiver.StatusChanged += OnStatusChanged;

            // Already tied when first seen: it went down where nobody here was watching.
            if (receiver.Has(StatusKind.Bound))
            {
                takedownSeconds = 0f;
                Show();
            }
        }

        private void OnStatusChanged(StatusKind kind, bool running)
        {
            if (kind != StatusKind.Bound) return;

            if (running)
            {
                Show();
                return;
            }

            Hide();
            pose.Release(GetComponentInChildren<CharacterActions>());
        }

        private void Show()
        {
            if (IsShown) return;

            // Away from the binder, so they are at its feet where the rope is being worked; as it
            // stood, for a tie nobody here saw being made.
            Vector3 away = takenDownBy != null ? transform.position - takenDownBy.position : transform.forward;
            away.y = 0f;
            yaw = away.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(Flat(transform.forward), away, Vector3.up) : 0f;
            lastRoot = transform.position;

            // Measured while the body still stands, which is how it stands the frame a tie lands:
            // the pose takes its standing height from it, and the rope tells legs from a head by it.
            pose.Begin(gameObject, poseLook, takedownSeconds);
            pose.Yaw = yaw;
            takedownSeconds = 0f;
            takenDownBy = null;

            LeashArtifact.TryResolveSettings(out Leash.Settings ropeSettings);
            wrap.Wind(gameObject, Pelvis(), BodyFrame.BonesOf(transform).ToArray(), wrapLook, ropeSettings.rope,
                      ropeMaterial);

            LieDown();
            TakeBody();
            LateUpdate();
        }

        private void Hide()
        {
            EndCarry();
            wrap.Hide();
            GiveBodyBack();
            StandUp();
        }

        private void LateUpdate()
        {
            if (!pose.IsPosed) return;

            Vector3 moved = transform.position - lastRoot;
            lastRoot = transform.position;
            float speed = Time.deltaTime > 0f ? moved.magnitude / Time.deltaTime : 0f;

            pose.Apply(Time.time, speed, EscapeProgress);
            wrap.Draw(Mathf.InverseLerp(poseLook.WindStarts, poseLook.DrawnUpEnds, pose.Progress));
        }

        /// <summary>How far the body is through working itself free, 0 to 1. See <see cref="BoundStatus"/>.</summary>
        public float EscapeProgress =>
            receiver != null && receiver.BehaviourOf(StatusKind.Bound) is BoundStatus bound ? bound.EscapeProgress : 0f;

        // ── The rope ───────────────────────────────────────────────────────────

        /// <summary>
        /// Every machine: <paramref name="holder"/> has a rope on this body, tied at its knot and held at
        /// <paramref name="holderAnchor"/> (in the holder's own space), <paramref name="length"/> long. Only the machine that
        /// owns the body builds the joint; one rope at a time, the latest replacing any other.
        /// </summary>
        public void Hold(Rigidbody holder, Vector3 holderAnchor, float length)
        {
            if (!physical || holder == null || carriedBy != null) return;

            LetGo(heldBy);

            rope = gameObject.AddComponent<ConfigurableJoint>();
            rope.connectedBody = holder;
            rope.autoConfigureConnectedAnchor = false;
            rope.anchor = transform.InverseTransformPoint(RopePoint);
            rope.connectedAnchor = holderAnchor;
            rope.xMotion = rope.yMotion = rope.zMotion = ConfigurableJointMotion.Limited;
            rope.angularXMotion = rope.angularYMotion = rope.angularZMotion = ConfigurableJointMotion.Free;
            rope.linearLimit = new SoftJointLimit { limit = length };
            rope.linearLimitSpring = new SoftJointLimitSpring { spring = physics.RopeSpring, damper = physics.RopeDamper };

            // The hand is not moved by what it holds: its body belongs to its own player, and the weight is felt as the
            // slowed walk instead — the same on every machine, whoever happens to own the captive.
            rope.connectedMassScale = 0f;
            rope.enableCollision = false;

            heldBy = holder;
            body.WakeUp();
        }

        /// <summary>Every machine: <paramref name="holder"/> lets go. Nothing if somebody else has the rope.</summary>
        public void LetGo(Rigidbody holder)
        {
            if (rope == null || (holder != null && holder != heldBy)) return;

            RuntimeObjects.Discard(rope);
            rope = null;
            heldBy = null;
        }

        // ── Over a shoulder ────────────────────────────────────────────────────

        /// <summary>
        /// Every machine: <paramref name="carrier"/> has the body over their shoulder and places its root from now on. The
        /// body stops being something physics moves (where it is owned), stops touching the carrier, and stops lying.
        /// </summary>
        public void BeginCarry(GameObject carrier)
        {
            if (carrier == null || carriedBy == carrier) return;

            EndCarry();
            LetGo(heldBy);
            carriedBy = carrier;

            if (lyingBody != null) lyingBody.SetActive(false);

            // Physics refuses to pair a collider that is switched off, so only the live ones are ghosted.
            foreach (Collider own in GetComponentsInChildren<Collider>())
            {
                if (own.isTrigger || !own.enabled) continue;
                foreach (Collider theirs in carrier.GetComponentsInChildren<Collider>())
                {
                    if (theirs.isTrigger || !theirs.enabled || Physics.GetIgnoreCollision(own, theirs)) continue;
                    Physics.IgnoreCollision(own, theirs, true);
                    ghosted.Add((own, theirs));
                }
            }

            // Placed by hand every frame, so nothing may move it in between: not gravity where it is owned, not
            // interpolation anywhere (an interpolating body restores its own position within the frame).
            Rigidbody root = GetComponent<Rigidbody>();
            if (root == null) return;

            carryWasKinematic = root.isKinematic;
            carryInterpolation = root.interpolation;
            if (physical)
            {
                root.linearVelocity = Vector3.zero;
                root.angularVelocity = Vector3.zero;
                root.isKinematic = true;
            }
            root.interpolation = RigidbodyInterpolation.None;
        }

        /// <summary>Every machine: the body is off the shoulder. Everything <see cref="BeginCarry"/> changed, given back. Safe twice.</summary>
        public void EndCarry()
        {
            if (carriedBy == null) return;
            carriedBy = null;
            Shouldered = 0f;

            foreach ((Collider own, Collider theirs) in ghosted)
                if (own != null && theirs != null) Physics.IgnoreCollision(own, theirs, false);
            ghosted.Clear();

            if (lyingBody != null) lyingBody.SetActive(true);

            Rigidbody root = GetComponent<Rigidbody>();
            if (root == null) return;

            root.isKinematic = carryWasKinematic;
            root.interpolation = carryInterpolation;
            if (physical) root.WakeUp();
        }

        // ── The body under physics ─────────────────────────────────────────────

        /// <summary>
        /// The owning machine hands the root to Unity's physics: legs off the agent, the body dynamic, heavy, damped. A player
        /// keeps upright rotation, or their camera would tumble with them.
        /// </summary>
        private void TakeBody()
        {
            if (physical || !Network.Owns(transform)) return;

            body = GetComponent<Rigidbody>();
            if (body == null) return;

            legs = GetComponentInParent<ISelfDrivingMotor>();
            legs?.SuspendSelfDrive();

            wasKinematic = body.isKinematic;
            hadGravity = body.useGravity;
            hadMass = body.mass;
            hadLinearDamping = body.linearDamping;
            hadAngularDamping = body.angularDamping;
            hadInterpolation = body.interpolation;
            hadConstraints = body.constraints;

            body.isKinematic = false;
            body.useGravity = true;
            body.mass = physics.Mass;
            body.linearDamping = physics.LinearDamping;
            body.angularDamping = physics.AngularDamping;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            if (CompareTag("Player")) body.constraints = RigidbodyConstraints.FreezeRotation;

            physical = true;
        }

        /// <summary>Everything <see cref="TakeBody"/> took, given back; the root stood upright again and the agent put back on its mesh.</summary>
        private void GiveBodyBack()
        {
            LetGo(heldBy);
            if (!physical) return;
            physical = false;

            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = wasKinematic;
                body.useGravity = hadGravity;
                body.mass = hadMass;
                body.linearDamping = hadLinearDamping;
                body.angularDamping = hadAngularDamping;
                body.interpolation = hadInterpolation;
                body.constraints = hadConstraints;
            }

            transform.rotation = Quaternion.LookRotation(Upright(transform), Vector3.up);

            legs?.ResumeSelfDrive();
            legs = null;

            // Dragged, possibly well off the NavMesh it started on. Warp only takes effect on an ENABLED agent, so this has to
            // come after ResumeSelfDrive — the same ordering WorldStreamer.SnapAgentsToNavMesh documents.
            NavMeshAgent agent = GetComponent<NavMeshAgent>();
            if (agent != null && agent.isActiveAndEnabled
                && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, RejoinMeshReach, NavMesh.AllAreas))
                agent.Warp(hit.position);
        }

        /// <summary>How far from where it was let up a body looks for its NavMesh, in metres.</summary>
        private const float RejoinMeshReach = 6f;

        /// <summary>A heading for a root that may have rolled over: its forward on the ground, or its up if it lies on its nose.</summary>
        private static Vector3 Upright(Transform root)
        {
            Vector3 forward = Flat(root.forward);
            return forward.sqrMagnitude > 1e-4f ? forward.normalized : Flat(root.up).normalized;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ── The body you can hit while it lies ─────────────────────────────────

        /// <summary>
        /// Swap the standing capsule for one lying along the drawn body, in the root's own space so it turns and slides with
        /// the root. Only the body's OWN solid capsules — those on its root rigidbody, or on the root when it has none — so a
        /// ragdoll's bones, and anything it wears or carries, are left alone.
        /// </summary>
        private void LieDown()
        {
            StandUp();

            Rigidbody own = GetComponent<Rigidbody>();
            float radius = 0f;

            foreach (CapsuleCollider capsule in GetComponentsInChildren<CapsuleCollider>())
            {
                if (!capsule.enabled || capsule.isTrigger) continue;
                if (own != null ? capsule.attachedRigidbody != own : capsule.gameObject != gameObject) continue;

                radius = Mathf.Max(radius, capsule.radius * MaxScale(capsule.transform));
                capsule.enabled = false;
                standingColliders.Add(capsule);
            }

            lyingBody = new GameObject("TiedBody") { layer = gameObject.layer };
            lyingBody.transform.SetParent(transform, false);

            var lying = lyingBody.AddComponent<CapsuleCollider>();
            lying.direction = 2;
            lying.radius = Mathf.Clamp(radius * LyingRadiusShare, MinLyingRadius, MaxLyingRadius);
            lying.height = Mathf.Max(pose.StandingHeight * LyingLengthShare, lying.radius * 2f);
            lying.sharedMaterial = Sliding(physics.Friction);

            Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
            lyingBody.transform.SetLocalPositionAndRotation(
                turn * (Vector3.forward * pose.Reach) + Vector3.up * (pose.GroundBelowRoot + lying.radius), turn);
        }

        /// <summary>Put the standing colliders back exactly as they were, and take the lying one away.</summary>
        private void StandUp()
        {
            foreach (Collider part in standingColliders)
                if (part != null) part.enabled = true;
            standingColliders.Clear();

            RuntimeObjects.Discard(lyingBody);
            lyingBody = null;
        }

        /// <summary>One shared material per friction: a body dragged over sand slides, it does not stick.</summary>
        private static PhysicsMaterial Sliding(float friction)
        {
            if (SlidingMaterials.TryGetValue(friction, out PhysicsMaterial known) && known != null) return known;

            var made = new PhysicsMaterial("TiedBody")
            {
                dynamicFriction = friction,
                staticFriction = friction,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f,
            };
            SlidingMaterials[friction] = made;
            return made;
        }

        private static readonly Dictionary<float, PhysicsMaterial> SlidingMaterials = new Dictionary<float, PhysicsMaterial>();

        private static float MaxScale(Transform t)
        {
            Vector3 s = t.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        }

        /// <summary>A lying body is narrower than the capsule a standing one needs to keep its elbows in.</summary>
        private const float LyingRadiusShare = 0.6f;
        private const float MinLyingRadius = 0.2f;
        private const float MaxLyingRadius = 0.45f;

        /// <summary>Hogtied, the feet are drawn up over the back: the body is shorter than it stood.</summary>
        private const float LyingLengthShare = 0.75f;

        /// <summary>The pelvis the rope is tied round: the avatar's hips, else the ragdoll's, else the root.</summary>
        private Transform Pelvis()
        {
            Animator animator = GetComponentInChildren<Animator>();
            Transform hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips != null) return hips;

            var rig = GetComponentInParent<Gameplay.Ragdoll.RagdollRig>();
            return rig != null && rig.Hips != null ? rig.Hips : transform;
        }
    }
}
