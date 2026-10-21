using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How a tied-up body goes down and lies. Serialized on the lasso prefab.
    ///
    /// <para>
    /// The muscle values are Unity's humanoid muscles (<c>HumanTrait.MuscleName</c>), -1 to 1, laid
    /// over a NEUTRAL body (every muscle 0) — never over whatever the animator happened to be
    /// playing, so the same numbers give the same hogtie on every humanoid. The defaults were found
    /// by search on Nomad and Raxy, scoring hands and feet up off the ground behind the back and
    /// meeting: on these rigs a positive "Front-Back" swings a limb BACK, the opposite of what the
    /// name suggests, and a guess at the signs bent the knees into the ground.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class TiedPoseLook
    {
        [Header("Hogtie (humanoids, muscle units)")]
        [Tooltip("'Arm Front-Back'. Positive takes the upper arms behind the back.")]
        [SerializeField, Range(-1f, 1f)] private float armsFrontBack = 0.5f;

        [Tooltip("'Arm Down-Up'. -1 is down at the sides.")]
        [SerializeField, Range(-1f, 1f)] private float armsDownUp = -1f;

        [Tooltip("'Forearm Stretch'. 1 is a straight arm, reaching back to the ankles.")]
        [SerializeField, Range(-1f, 1f)] private float forearmsStretch = 1f;

        [Tooltip("'Upper Leg Front-Back'. Positive lifts the thighs up behind.")]
        [SerializeField, Range(-1f, 1f)] private float thighsFrontBack = 1f;

        [Tooltip("'Lower Leg Stretch'. Negative bends the knees, bringing the feet over the back " +
                 "to the hands.")]
        [SerializeField, Range(-1f, 1f)] private float kneesStretch = -0.5f;

        [Tooltip("'Spine Front-Back' and 'Chest Front-Back'.")]
        [SerializeField, Range(-1f, 1f)] private float backFrontBack;

        [Tooltip("'Head Nod Down-Up'.")]
        [SerializeField, Range(-1f, 1f)] private float headNod;

        [Header("Going down (humanoids)")]
        [Tooltip("'Upper Leg Front-Back' while forced onto the knees. 0 is a thigh straight down.")]
        [SerializeField, Range(-1f, 1f)] private float kneelThighs;

        [Tooltip("'Lower Leg Stretch' while kneeling. -1 folds the shins flat behind.")]
        [SerializeField, Range(-1f, 1f)] private float kneelKnees = -1f;

        [Tooltip("Degrees the kneeling body leans forward, the moment before it goes over.")]
        [SerializeField, Range(0f, 45f)] private float kneelLean = 15f;

        [Tooltip("'Lower Leg Stretch' lying flat on the face, legs not yet drawn up.")]
        [SerializeField, Range(-1f, 1f)] private float flatKnees = 0.6f;

        [Tooltip("How far forward of where they stood the body lands, as a fraction of its height. " +
                 "A falling body pivots on its knees, so it lies ahead of its own feet.")]
        [SerializeField, Range(0f, 0.5f)] private float fallReach = 0.2f;

        [Tooltip("Where in the tie, 0-1, the body is on its knees.")]
        [SerializeField, Range(0.05f, 0.9f)] private float kneelEnds = 0.3f;

        [Tooltip("Where in the tie it has fallen onto its face. The fall accelerates, so it lands.")]
        [SerializeField, Range(0.1f, 0.95f)] private float fallEnds = 0.5f;

        [Tooltip("Where in the tie its wrists and ankles have been drawn together.")]
        [SerializeField, Range(0.2f, 1f)] private float drawnUpEnds = 0.9f;

        [Tooltip("Where in the tie the rope starts going on. It is all on at Drawn Up Ends.")]
        [SerializeField, Range(0f, 0.9f)] private float windStarts = 0.15f;

        [Header("Fighting the ropes")]
        [Tooltip("How hard a bout of struggling works the legs and arms, in muscle units. Zero is a " +
                 "body lying still.")]
        [SerializeField, Range(0f, 0.5f)] private float wriggle = 0.18f;

        [Tooltip("Heaves per second during a bout.")]
        [SerializeField, Min(0f)] private float wriggleRate = 1.8f;

        [Tooltip("Seconds a bout of struggling lasts.")]
        [SerializeField, Min(0.1f)] private float boutSeconds = 1.6f;

        [Tooltip("Seconds lying spent between bouts, on average. Each rest is a little different.")]
        [SerializeField, Min(0f)] private float restSeconds = 3.5f;

        [Tooltip("Degrees the body rocks from side to side during a bout.")]
        [SerializeField, Range(0f, 30f)] private float rollDegrees = 8f;

        [Tooltip("'Head Nod Down-Up' added at the height of a bout, and while being dragged: the head " +
                 "comes up off the ground.")]
        [SerializeField, Range(-1f, 1f)] private float headLift = 0.5f;

        [Tooltip("How much harder the body fights as it nears working free (left still), as a multiple of an ordinary bout.")]
        [SerializeField, Min(1f)] private float escapeEffort = 2f;

        [Tooltip("How far each breath moves the back, in muscle units.")]
        [SerializeField, Range(0f, 0.2f)] private float breath = 0.04f;

        [Tooltip("Breaths per second.")]
        [SerializeField, Min(0f)] private float breathRate = 0.3f;

        [Tooltip("Metres a dragged body has to travel to swing round behind its rope. Shorter turns it " +
                 "sooner, as if lighter.")]
        [SerializeField, Min(0.1f)] private float trailMetres = 1.2f;

        [Header("Over the shoulder (humanoids)")]
        [Tooltip("Degrees the pelvis is pitched head-down over the carrier's shoulder. With the back and the thighs " +
                 "folded about it, this is what puts the trunk hanging down the back and the legs out in front.")]
        [SerializeField, Range(0f, 90f)] private float shoulderHang = 50f;

        [Tooltip("Degrees below level the bound ankles lie from the hips, carried. The thighs are found per body to put " +
                 "them there, because one muscle value put a Raxy's feet at the carrier's chin and a Nomad's boots " +
                 "in front of the eye. Higher sinks the feet towards the bottom of the carrier's view.")]
        [SerializeField, Range(-30f, 60f)] private float shoulderFeetDrop = 8f;

        [Tooltip("'Lower Leg Stretch' carried. The hogtie's bend, about a right angle: the bound shins come up off the " +
                 "knees with the feet in the carrier's view. Straighter legs hang the feet out of sight below the chin.")]
        [SerializeField, Range(-1f, 1f)] private float shoulderKnees = -0.1f;

        [Tooltip("How far apart the bound ankles are carried, as a fraction of the width between the hip joints. The " +
                 "legs are drawn together per body to it: left alone they splay half a metre apart.")]
        [SerializeField, Range(0f, 1f)] private float shoulderAnkleGap = 0.6f;

        [Tooltip("'Foot Up-Down' carried. Positive points the feet along the shins, soles up, the way a hogtied body's " +
                 "feet are; zero leaves them flat out sideways from the shins.")]
        [SerializeField, Range(-1f, 1f)] private float shoulderFeet = 0.8f;

        [Tooltip("'Spine Front-Back' and 'Chest Front-Back' carried. Negative curls the back over the shoulder so the " +
                 "trunk hangs straight down the carrier's back rather than sticking out behind.")]
        [SerializeField, Range(-1f, 1f)] private float shoulderBack = -0.5f;

        [Tooltip("'Arm Down-Up' carried. Upside down, the tied arms would otherwise stand up past the hips; raised, " +
                 "they hang towards the head.")]
        [SerializeField, Range(-1f, 1f)] private float shoulderArmsDownUp = 1f;

        [Tooltip("'Arm Front-Back' carried. Further back puts the hanging hands out behind the carrier, but apart.")]
        [SerializeField, Range(-1f, 1f)] private float shoulderArmsFrontBack;

        [Tooltip("'Forearm Stretch' carried. A little bend brings the tied wrists together as the arms hang.")]
        [SerializeField, Range(-1f, 1f)] private float shoulderForearms = -0.4f;

        [Tooltip("'Head Nod Down-Up' carried.")]
        [SerializeField, Range(-1f, 1f)] private float shoulderHead;

        [Header("Getting up")]
        [Tooltip("Played on the body as the ropes come off, where this machine writes its animator. " +
                 "Should start face down. Empty: the body is blended straight back to standing.")]
        [SerializeField] private CharacterAction getUp;

        [Tooltip("Seconds the tied pose takes to hand the body back to its animation.")]
        [SerializeField, Min(0.05f)] private float releaseSeconds = 0.5f;

        public float ArmsFrontBack => armsFrontBack;
        public float ArmsDownUp => armsDownUp;
        public float ForearmsStretch => forearmsStretch;
        public float ThighsFrontBack => thighsFrontBack;
        public float KneesStretch => kneesStretch;
        public float BackFrontBack => backFrontBack;
        public float HeadNod => headNod;
        public float KneelThighs => kneelThighs;
        public float KneelKnees => kneelKnees;
        public float KneelLean => kneelLean;
        public float FlatKnees => flatKnees;
        public float FallReach => fallReach;
        public float KneelEnds => Mathf.Min(kneelEnds, FallEnds - 0.01f);
        public float FallEnds => Mathf.Min(fallEnds, DrawnUpEnds - 0.01f);
        public float DrawnUpEnds => drawnUpEnds;
        public float WindStarts => Mathf.Min(windStarts, DrawnUpEnds - 0.01f);
        public float Wriggle => wriggle;
        public float WriggleRate => wriggleRate;
        public float BoutSeconds => boutSeconds;
        public float RestSeconds => restSeconds;
        public float RollDegrees => rollDegrees;
        public float HeadLift => headLift;
        public float EscapeEffort => escapeEffort;
        public float Breath => breath;
        public float BreathRate => breathRate;
        public float TrailMetres => trailMetres;
        public float ShoulderHang => shoulderHang;
        public float ShoulderFeetDrop => shoulderFeetDrop;
        public float ShoulderKnees => shoulderKnees;
        public float ShoulderAnkleGap => shoulderAnkleGap;
        public float ShoulderFeet => shoulderFeet;
        public float ShoulderBack => shoulderBack;
        public float ShoulderArmsDownUp => shoulderArmsDownUp;
        public float ShoulderArmsFrontBack => shoulderArmsFrontBack;
        public float ShoulderForearms => shoulderForearms;
        public float ShoulderHead => shoulderHead;
        public CharacterAction GetUp => getUp;
        public float ReleaseSeconds => releaseSeconds;
    }

    /// <summary>
    /// A tied-up body taken down and laid on the ground, hogtied: forced onto its knees, over onto
    /// its face, wrists drawn back to the ankles — then fighting the ropes in bouts, breathing, and
    /// lifting its head while it is dragged. No ragdoll — the body keeps its animator, its agent and
    /// its root, and only its pose is overridden.
    ///
    /// <para>
    /// <b>A humanoid is posed in MUSCLE space</b> (<see cref="HumanPoseHandler"/>), not by writing
    /// bone rotations. This project's rigs face +Z, −Y or whatever their .blend did, so a bone's
    /// own axes mean nothing that could be written generically; a muscle means the same on every
    /// humanoid avatar. The takedown is four key poses — as the tie found the body, kneeling, flat
    /// on its face, hogtied — decided once when the tie lands and interpolated by the clock, so
    /// every frame is written as an absolute and nothing accumulates.
    /// </para>
    /// <para>
    /// <b>Each key pose is set down by MEASURING it, once, as the tie lands:</b> the meshes are baked
    /// standing (the feet give the ground) and again in each pose, and each is moved so its lowest
    /// point rests there. Every estimate tried first was wrong on some rig — a pose's body position
    /// is in the animator's own space, the models sit at different heights above their roots
    /// (Nomad's 0.56 m, Raxy's 0.11 m), and face down the chest hangs lower than the hips — and left
    /// Nomad 0.7 m into the ground while Raxy lay on it.
    /// </para>
    /// <para>
    /// <b>The body turns on the ground, the root never does.</b> <see cref="Yaw"/> lays the body at an
    /// angle to its root, because the root is what the agent, the network and the save move, and a
    /// body dragged on a rope has to swing round behind it.
    /// </para>
    /// <para>
    /// <b>Carried, it hangs over a shoulder</b> (<see cref="Shouldered"/>): one more key pose, belly
    /// on the shoulder, trunk down the carrier's back, legs out in front, blended in as it is heaved
    /// up. Measured like the others: its hips land where they stood (<see cref="CarryAnchor"/>), which
    /// is the point the carrier puts on its shoulder.
    /// </para>
    /// <para>
    /// <b>A creature with no humanoid avatar is tipped onto its side</b> about the root's forward
    /// axis — its model, never the root. Carried, it stands upright again and its carrier turns the
    /// root to lay it across the shoulders (<see cref="AcrossTurn"/>).
    /// </para>
    /// <para>
    /// Presentation, on every machine, from the replicated condition. Nothing here is sent or saved.
    /// </para>
    /// </summary>
    public sealed class TiedPose
    {
        /// <summary>The key poses a takedown passes through after the one it starts from, and the one it is carried in.</summary>
        private const int Kneeling = 0, FaceDown = 1, Hogtied = 2, OverShoulder = 3;

        private struct Key
        {
            public float[] Muscles;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        private TiedPoseLook look;

        private Animator animator;
        private HumanPoseHandler handler;
        private HumanPose pose;
        private HumanPose animated;
        private Key start;
        private readonly Key[] keys = new Key[4];
        private float began;
        private float takedownSeconds;
        private float releaseBegan = -1f;
        private float phase;

        private int[] arms, armsDown, forearms, thighs, knees, back, head, legsInOut, feet;

        private Transform model;
        private Vector3 modelRestPosition;
        private Quaternion modelRestRotation;
        private float tippedLift;

        /// <summary>Is a body laid down — or still getting up — right now?</summary>
        public bool IsPosed => handler != null || model != null;

        /// <summary>Are the ropes off and the body on its way back to its own animation?</summary>
        public bool IsReleasing => IsPosed && releaseBegan >= 0f;

        /// <summary>
        /// Degrees the lying body is turned about the vertical from its root's forward. Its head
        /// points along that heading. Eased in while the body goes to its knees.
        /// </summary>
        public float Yaw { get; set; }

        /// <summary>How far through the takedown, 0 (just tied) to 1 (down and drawn up).</summary>
        public float Progress { get; private set; }

        /// <summary>Metres ahead of the root, along <see cref="Yaw"/>, that the lying body's middle is.</summary>
        public float Reach { get; private set; }

        /// <summary>The body's standing height, measured as the tie landed.</summary>
        public float StandingHeight { get; private set; }

        /// <summary>The ground the body was standing on, as a height relative to its root.</summary>
        public float GroundBelowRoot { get; private set; }

        /// <summary>
        /// How far the body is over a carrier's shoulder, 0 (lying where it was tied) to 1 (carried).
        /// Set every frame by whoever carries it; the root is theirs to place.
        /// </summary>
        public float Shouldered { get; set; }

        /// <summary>
        /// The point of the body that rests on a carrier's shoulder, in the root's own space: the
        /// hips as the body stood, which is where the carried pose keeps them — or, for a body
        /// carried across the shoulders, its middle.
        /// </summary>
        public Vector3 CarryAnchor { get; private set; }

        /// <summary>
        /// A body with no humanoid avatar is carried across both shoulders behind the head, the way
        /// a sheep is, rather than draped over one.
        /// </summary>
        public bool CarriedAcross { get; private set; }

        /// <summary>
        /// The turn of the root, about the vertical, that lays such a body's LONG axis across a carrier's shoulders —
        /// whichever of the root's axes its model runs along, because a creature's model is not always authored facing
        /// the root's forward.
        /// </summary>
        public Quaternion AcrossTurn { get; private set; } = Quaternion.identity;

        /// <summary>Half the depth of a body carried across the shoulders, front to back, in metres.</summary>
        public float AcrossHalfDepth { get; private set; }

        /// <summary>
        /// Lay <paramref name="body"/> down. Measured from the body as it stands, which is how it
        /// stands when a tie lands.
        /// </summary>
        /// <param name="takedownSeconds">
        /// How long going down takes. Zero lays the body straight down — for a body whose tie
        /// landed while nobody here could see it: a late joiner, a chunk streamed back in.
        /// </param>
        public void Begin(GameObject body, TiedPoseLook poseLook, float takedownSeconds = 0f)
        {
            End();
            if (body == null || poseLook == null) return;

            look = poseLook;
            animator = body.GetComponentInChildren<Animator>();
            phase = Random.value * Mathf.PI * 2f;
            began = Time.time;
            releaseBegan = -1f;
            this.takedownSeconds = Mathf.Max(0f, takedownSeconds);
            Progress = this.takedownSeconds > 0f ? 0f : 1f;

            Bounds standing = BodyInLocalSpace(body.transform);
            StandingHeight = standing.size.y;
            Reach = look.FallReach * StandingHeight;

            if (animator != null && animator.isHuman && animator.avatar != null) BeginHumanoid(body.transform);
            else BeginTipped(body.transform, standing);
        }

        /// <summary>Hold the pose for this frame. After the animator, every frame.</summary>
        /// <param name="dragSpeed">How fast the body is being pulled along the ground, m/s.</param>
        /// <param name="effort">How far it is through working itself free, 0 to 1: it fights harder the nearer it gets.</param>
        public void Apply(float time, float dragSpeed = 0f, float effort = 0f)
        {
            if (!IsPosed) return;

            Progress = takedownSeconds > 0f ? Mathf.Clamp01((time - began) / takedownSeconds) : 1f;

            if (handler == null)
            {
                ApplyTipped(time);
                return;
            }

            float release = 1f;
            if (releaseBegan >= 0f)
            {
                release = 1f - Mathf.Clamp01((time - releaseBegan) / look.ReleaseSeconds);
                if (release <= 0f)
                {
                    End();
                    return;
                }

                // What the animator wrote this frame — the get-up clip, or standing — read before
                // this frame's tied pose overwrites it, so the two can be blended.
                handler.GetHumanPose(ref animated);
            }

            Interpolate(Progress);
            if (Shouldered > 0f) BlendTowards(keys[OverShoulder], Shouldered);
            if (Progress >= 1f || takedownSeconds <= 0f) Struggle(time, dragSpeed, effort);

            // Turned on the ground, about the root: rotation and the body's offset from it alike. A carried body
            // faces the way its carrier does, which is the root's way.
            Quaternion turn = Quaternion.AngleAxis(Yaw * TurnedIn(Progress) * (1f - Shouldered), Vector3.up);
            pose.bodyRotation = turn * pose.bodyRotation;
            pose.bodyPosition = turn * pose.bodyPosition;

            if (release < 1f)
            {
                for (int i = 0; i < pose.muscles.Length; i++)
                    pose.muscles[i] = Mathf.Lerp(animated.muscles[i], pose.muscles[i], release);
                pose.bodyPosition = Vector3.Lerp(animated.bodyPosition, pose.bodyPosition, release);
                pose.bodyRotation = Quaternion.Slerp(animated.bodyRotation, pose.bodyRotation, release);
            }

            handler.SetHumanPose(ref pose);
        }

        /// <summary>
        /// The ropes are off: play <see cref="TiedPoseLook.GetUp"/> where this machine may, and blend
        /// the body back to its animation over <see cref="TiedPoseLook.ReleaseSeconds"/>. Safe twice.
        /// </summary>
        public void Release(CharacterActions actions)
        {
            if (!IsPosed || IsReleasing) return;

            if (handler == null)
            {
                End();
                return;
            }

            releaseBegan = Time.time;
            if (actions != null && look.GetUp != null) actions.Play(look.GetUp);
        }

        /// <summary>Stand the body back up at once. Safe twice, and safe on a body that has been destroyed.</summary>
        public void End()
        {
            handler?.Dispose();
            handler = null;

            // A tipped model is put back exactly; a humanoid needs nothing, because its animator
            // writes its own pose again on the very next frame.
            if (model != null) model.SetLocalPositionAndRotation(modelRestPosition, modelRestRotation);
            model = null;
            animator = null;
            releaseBegan = -1f;
            Shouldered = 0f;
        }

        // ── Humanoid ───────────────────────────────────────────────────────────

        private void BeginHumanoid(Transform root)
        {
            CarriedAcross = false;
            handler = new HumanPoseHandler(animator.avatar, animator.transform);
            pose = new HumanPose();
            animated = new HumanPose();
            handler.GetHumanPose(ref pose);
            handler.GetHumanPose(ref animated);

            arms = Muscles("Left Arm Front-Back", "Right Arm Front-Back");
            armsDown = Muscles("Left Arm Down-Up", "Right Arm Down-Up");
            forearms = Muscles("Left Forearm Stretch", "Right Forearm Stretch");
            thighs = Muscles("Left Upper Leg Front-Back", "Right Upper Leg Front-Back");
            knees = Muscles("Left Lower Leg Stretch", "Right Lower Leg Stretch");
            back = Muscles("Spine Front-Back", "Chest Front-Back");
            head = Muscles("Head Nod Down-Up");
            legsInOut = Muscles("Left Upper Leg In-Out", "Right Upper Leg In-Out");
            feet = Muscles("Left Foot Up-Down", "Right Foot Up-Down");

            // As the tie found the body: whatever its animator was playing.
            start = new Key
            {
                Muscles = (float[])pose.muscles.Clone(),
                Position = pose.bodyPosition,
                Rotation = pose.bodyRotation,
            };

            float ground = LowestPoint(root);
            GroundBelowRoot = ground - root.position.y;

            // How far one unit of pose position moves the body, measured rather than derived from
            // humanScale: the body position is the centre of mass in the animator's scaled space,
            // and the conversion is simplest to trust by trying it.
            float perUnit = PerUnitHeight(animator.GetBoneTransform(HumanBodyBones.Hips), start);
            bool measured = Mathf.Abs(perUnit) > 1e-4f;

            Quaternion upright = start.Rotation;
            Quaternion faceDown = Quaternion.AngleAxis(90f, Vector3.right) * upright;
            Vector3 ahead = Vector3.forward * (measured ? Reach / perUnit : Reach);

            keys[Kneeling] = new Key
            {
                Muscles = Compose(look.KneelThighs, look.KneelKnees, look.ArmsFrontBack, look.ArmsDownUp,
                                  0f, look.BackFrontBack, look.HeadNod),
                Position = start.Position,
                Rotation = Quaternion.AngleAxis(look.KneelLean, Vector3.right) * upright,
            };
            keys[FaceDown] = new Key
            {
                Muscles = Compose(0f, look.FlatKnees, look.ArmsFrontBack, look.ArmsDownUp,
                                  look.ForearmsStretch, look.BackFrontBack, look.HeadNod),
                Position = start.Position + ahead,
                Rotation = faceDown,
            };
            keys[Hogtied] = new Key
            {
                Muscles = Compose(look.ThighsFrontBack, look.KneesStretch, look.ArmsFrontBack, look.ArmsDownUp,
                                  look.ForearmsStretch, look.BackFrontBack, look.HeadNod),
                Position = start.Position + ahead,
                Rotation = faceDown,
            };

            // Folded over a shoulder, head to the carrier's back: face down, turned end for end, the pelvis pitched
            // head-down by the hang and the back and thighs folded about it — trunk down the back, legs out in front,
            // bent at the knee with the bound ankles together and the feet pointed. Its hips kept where they stood,
            // which is the point the carrier lifts.
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Write(start);
            CarryAnchor = hips != null ? root.InverseTransformPoint(hips.position) : Vector3.up * (StandingHeight * 0.5f);
            float[] carried = Compose(0f, look.ShoulderKnees, look.ShoulderArmsFrontBack, look.ShoulderArmsDownUp,
                                      look.ShoulderForearms, look.ShoulderBack, look.ShoulderHead);
            Set(carried, feet, look.ShoulderFeet);
            keys[OverShoulder] = new Key
            {
                Muscles = carried,
                Position = start.Position,
                Rotation = Quaternion.AngleAxis(-look.ShoulderHang, Vector3.right) * Quaternion.AngleAxis(180f, Vector3.up) * faceDown,
            };
            FitCarriedLegs(keys[OverShoulder], root);
            if (measured && hips != null)
                keys[OverShoulder].Position = HipsAt(keys[OverShoulder], hips, root.TransformPoint(CarryAnchor), perUnit);

            // Each key on the ground, by its own lowest point. Only the one the body will hold is
            // measured for a body laid straight down.
            for (int k = takedownSeconds > 0f ? 0 : Hogtied; measured && k <= Hogtied; k++)
            {
                Write(keys[k]);
                keys[k].Position.y += (ground - LowestPoint(root)) / perUnit;
            }

            Interpolate(Progress);
            handler.SetHumanPose(ref pose);
        }

        /// <summary>
        /// The body position that puts <paramref name="hips"/> on <paramref name="target"/> in pose
        /// <paramref name="key"/>. The hips move linearly with the body position, so a correction or two converges.
        /// </summary>
        private Vector3 HipsAt(Key key, Transform hips, Vector3 target, float perUnit)
        {
            for (int pass = 0; pass < AnchorPasses; pass++)
            {
                Write(key);
                key.Position += Quaternion.Inverse(animator.transform.rotation) * (target - hips.position) / perUnit;
            }

            return key.Position;
        }

        /// <summary>Corrections <see cref="HipsAt"/> makes. The second takes up anything the first one's measure missed.</summary>
        private const int AnchorPasses = 2;

        /// <summary>
        /// The carried legs, fitted to this body by measuring them in <paramref name="key"/>: drawn together until the bound
        /// ankles are <see cref="TiedPoseLook.ShoulderAnkleGap"/> of the hips' width apart, then the thighs raised or lowered
        /// until the ankles lie <see cref="TiedPoseLook.ShoulderFeetDrop"/> below the hips. Muscle values alone do not
        /// carry over: the same ones put a Raxy's feet 8 cm under its hips and a Nomad's boots 8 cm over them, and splay
        /// the two legs differently. Directions only, so the key's position does not matter.
        /// </summary>
        private void FitCarriedLegs(Key key, Transform root)
        {
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform leftThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform rightThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);

            Write(key);
            float hipWidth = Vector3.Distance(leftThigh.position, rightThigh.position);
            float side = Mathf.Sign(Spread());
            float Spread() => root.InverseTransformVector(leftFoot.position - rightFoot.position).x;
            SolveMuscle(key, legsInOut, -1f, 1f, () => Spread() * side, look.ShoulderAnkleGap * hipWidth);

            float Drop()
            {
                Vector3 along = root.InverseTransformVector((leftFoot.position + rightFoot.position) * 0.5f - hips.position);
                return Mathf.Atan2(-along.y, new Vector2(along.x, along.z).magnitude) * Mathf.Rad2Deg;
            }
            SolveMuscle(key, thighs, -1f, 1f, Drop, look.ShoulderFeetDrop);
        }

        /// <summary>
        /// Sets the muscles <paramref name="indices"/> of <paramref name="key"/> to the value between <paramref name="from"/>
        /// and <paramref name="to"/> at which <paramref name="measure"/> reads <paramref name="target"/>, by halving — or to
        /// the end nearer it when the range never gets there. The measure must move one way across the range.
        /// </summary>
        private void SolveMuscle(Key key, int[] indices, float from, float to, System.Func<float> measure, float target)
        {
            float Miss(float value)
            {
                Set(key.Muscles, indices, value);
                Write(key);
                return measure() - target;
            }

            float atFrom = Miss(from), atTo = Miss(to);
            if (atFrom * atTo > 0f)
            {
                Set(key.Muscles, indices, Mathf.Abs(atFrom) < Mathf.Abs(atTo) ? from : to);
                return;
            }

            for (int pass = 0; pass < SolvePasses; pass++)
            {
                float middle = (from + to) * 0.5f;
                float atMiddle = Miss(middle);
                if (atMiddle * atFrom > 0f) { from = middle; atFrom = atMiddle; }
                else to = middle;
            }

            Set(key.Muscles, indices, (from + to) * 0.5f);
        }

        /// <summary>Halvings <see cref="SolveMuscle"/> makes: a two-unit range to within a thousandth of a muscle unit.</summary>
        private const int SolvePasses = 11;

        private float PerUnitHeight(Transform hips, Key key)
        {
            if (hips == null) return 0f;

            Write(key);
            float at = hips.position.y;

            key.Position.y += 1f;
            Write(key);
            return hips.position.y - at;
        }

        private void Write(Key key)
        {
            System.Array.Copy(key.Muscles, pose.muscles, pose.muscles.Length);
            pose.bodyPosition = key.Position;
            pose.bodyRotation = key.Rotation;
            handler.SetHumanPose(ref pose);
        }

        /// <summary>A key pose over a NEUTRAL body: every muscle not named here is zero.</summary>
        private float[] Compose(float thighsValue, float kneesValue, float armsValue, float armsDownValue,
                                float forearmsValue, float backValue, float headValue)
        {
            var muscles = new float[pose.muscles.Length];
            Set(muscles, arms, armsValue);
            Set(muscles, armsDown, armsDownValue);
            Set(muscles, forearms, forearmsValue);
            Set(muscles, thighs, thighsValue);
            Set(muscles, knees, kneesValue);
            Set(muscles, back, backValue);
            Set(muscles, head, headValue);
            return muscles;
        }

        /// <summary>
        /// The takedown at <paramref name="t"/>: eased onto the knees, an accelerating fall onto the
        /// face — a body going over lands, it does not settle — and eased up into the hogtie.
        /// </summary>
        private void Interpolate(float t)
        {
            if (takedownSeconds <= 0f || t >= look.DrawnUpEnds)
            {
                Blend(keys[Hogtied], keys[Hogtied], 0f);
                return;
            }

            if (t < look.KneelEnds)
            {
                Blend(start, keys[Kneeling], Mathf.SmoothStep(0f, 1f, t / look.KneelEnds));
                return;
            }

            if (t < look.FallEnds)
            {
                float fall = (t - look.KneelEnds) / (look.FallEnds - look.KneelEnds);
                Blend(keys[Kneeling], keys[FaceDown], fall * fall);
                return;
            }

            Blend(keys[FaceDown], keys[Hogtied],
                  Mathf.SmoothStep(0f, 1f, (t - look.FallEnds) / (look.DrawnUpEnds - look.FallEnds)));
        }

        /// <summary>The pose as it stands this frame, taken <paramref name="amount"/> of the way to <paramref name="to"/>.</summary>
        private void BlendTowards(Key to, float amount)
        {
            for (int i = 0; i < pose.muscles.Length; i++)
                pose.muscles[i] = Mathf.Lerp(pose.muscles[i], to.Muscles[i], amount);

            pose.bodyPosition = Vector3.Lerp(pose.bodyPosition, to.Position, amount);
            pose.bodyRotation = Quaternion.Slerp(pose.bodyRotation, to.Rotation, amount);
        }

        private void Blend(Key from, Key to, float amount)
        {
            for (int i = 0; i < pose.muscles.Length; i++)
                pose.muscles[i] = Mathf.Lerp(from.Muscles[i], to.Muscles[i], amount);

            pose.bodyPosition = Vector3.Lerp(from.Position, to.Position, amount);
            pose.bodyRotation = Quaternion.Slerp(from.Rotation, to.Rotation, amount);
        }

        /// <summary>How much of <see cref="Yaw"/> is turned in: none as the tie lands, all of it on the knees.</summary>
        private float TurnedIn(float t) =>
            takedownSeconds <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, t / look.KneelEnds);

        /// <summary>
        /// Lying hogtied is not lying still. Breathing all the time; bouts of heaving against the
        /// ropes — legs and arms working, the body rocking, the head coming up — with rests between
        /// them that are never quite the same length; and the head held up and the bout kept going
        /// while somebody drags the body along. The nearer it is to working free (<paramref name="effort"/>), the harder each
        /// bout and the less it rests between them, until at the end it never stops.
        /// </summary>
        private void Struggle(float time, float dragSpeed, float effort)
        {
            float lying = time - began - takedownSeconds * look.DrawnUpEnds;
            float bout = Mathf.Max(Bout(lying), effort * effort);
            if (dragSpeed > DraggedSpeed) bout = Mathf.Max(bout, DraggedBout);
            bout *= Mathf.Lerp(1f, look.EscapeEffort, effort);

            float heave = Mathf.Sin(time * look.WriggleRate * Mathf.PI * 2f + phase);
            float breathe = Mathf.Sin(time * look.BreathRate * Mathf.PI * 2f + phase * 0.5f);

            Add(knees, look.Wriggle * bout * heave);
            Add(thighs, look.Wriggle * 0.5f * bout * Mathf.Sin(time * look.WriggleRate * Mathf.PI * 2f + phase + 1.3f));
            Add(arms, look.Wriggle * 0.4f * bout * Mathf.Sin(time * look.WriggleRate * 1.3f * Mathf.PI * 2f + phase));
            Add(back, look.Breath * breathe);
            Add(head, look.HeadLift * bout);

            // Rocked about its own length, which face down is the root's forward.
            float roll = look.RollDegrees * bout * Mathf.Sin(time * look.WriggleRate * 0.5f * Mathf.PI * 2f + phase);
            pose.bodyRotation = Quaternion.AngleAxis(roll, Vector3.forward) * pose.bodyRotation;
        }

        /// <summary>
        /// How hard the body is fighting <paramref name="lying"/> seconds after it went down, 0 to 1:
        /// a bout in every cycle of bout plus rest, started up to 40% of a rest late so bodies tied
        /// together do not heave in step. Periodic, so a tie that lasts as long as it is kept moving
        /// keeps struggling.
        /// </summary>
        private float Bout(float lying)
        {
            if (lying < 0f) return 0f;

            float cycle = look.BoutSeconds + look.RestSeconds;
            int index = Mathf.FloorToInt(lying / cycle);
            float late = look.RestSeconds * 0.4f * (0.5f + 0.5f * Mathf.Sin(index * 2.399f + phase));
            float into = lying - index * cycle - late;

            if (into < 0f || into >= look.BoutSeconds) return 0f;

            float s = Mathf.Sin(into / look.BoutSeconds * Mathf.PI);
            return s * s;
        }

        /// <summary>A body moving faster than this, m/s, is being dragged rather than nudged.</summary>
        private const float DraggedSpeed = 0.3f;

        /// <summary>How hard a dragged body fights, at the least.</summary>
        private const float DraggedBout = 0.7f;

        private void Add(int[] indices, float value)
        {
            foreach (int index in indices)
                if (index >= 0) pose.muscles[index] = Mathf.Clamp(pose.muscles[index] + value, -1f, 1f);
        }

        private static void Set(float[] muscles, int[] indices, float value)
        {
            foreach (int index in indices)
                if (index >= 0) muscles[index] = Mathf.Clamp(value, -1f, 1f);
        }

        /// <summary>Muscle indices by Unity's own names. A name an avatar lacks maps to -1 and is skipped.</summary>
        private static int[] Muscles(params string[] names)
        {
            var found = new int[names.Length];
            for (int i = 0; i < names.Length; i++) found[i] = System.Array.IndexOf(HumanTrait.MuscleName, names[i]);
            return found;
        }

        /// <summary>
        /// The lowest point of the body's drawn surface, world height. Skinned meshes are baked, so
        /// this is the pose they hold right now rather than the bounds they were imported with.
        /// A handful of times per tie, on the frame it lands — never per frame.
        /// </summary>
        private static float LowestPoint(Transform root)
        {
            float lowest = float.PositiveInfinity;
            var baked = new UnityEngine.Mesh();
            var vertices = new System.Collections.Generic.List<Vector3>();

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (BodyAttachment.Covers(r.transform, root)) continue;

                if (r is SkinnedMeshRenderer skinned)
                {
                    skinned.BakeMesh(baked, true);
                    baked.GetVertices(vertices);

                    Transform at = skinned.transform;
                    Vector3 scale = at.lossyScale;
                    foreach (Vector3 v in vertices)
                        lowest = Mathf.Min(lowest, (at.position + at.rotation * Vector3.Scale(v, scale)).y);
                }
                else if (r is MeshRenderer)
                {
                    lowest = Mathf.Min(lowest, r.bounds.min.y);
                }
            }

            RuntimeObjects.Discard(baked);
            return float.IsPositiveInfinity(lowest) ? root.position.y : lowest;
        }

        // ── Anything else ──────────────────────────────────────────────────────

        /// <summary>
        /// Lay a body with no humanoid avatar on its side: its model tipped a quarter turn about the
        /// root's forward axis and lifted by half its width, so the side it falls on rests on the
        /// ground rather than half through it. The model only — never the root.
        /// </summary>
        private void BeginTipped(Transform root, Bounds extent)
        {
            GroundBelowRoot = extent.min.y;
            CarryAnchor = extent.center;
            CarriedAcross = true;
            AcrossTurn = Quaternion.AngleAxis(extent.size.z > extent.size.x ? 90f : 0f, Vector3.up);
            AcrossHalfDepth = Mathf.Min(extent.extents.x, extent.extents.z);

            Transform candidate = animator != null ? animator.transform : null;
            if (candidate == null || candidate == root) return;

            model = candidate;
            model.GetLocalPositionAndRotation(out modelRestPosition, out modelRestRotation);
            tippedLift = extent.extents.x;
            ApplyTipped(Time.time);
        }

        /// <summary>
        /// The tip, eased over the time a humanoid takes to reach its knees, and turned by <see cref="Yaw"/>; carried, the
        /// model stands upright again, on a root its carrier has turned across their shoulders.
        /// </summary>
        private void ApplyTipped(float time)
        {
            if (model == null) return;

            float over = takedownSeconds > 0f ? Mathf.SmoothStep(0f, 1f, Progress / look.FallEnds) : 1f;
            Quaternion tip = Quaternion.AngleAxis(90f * over, Vector3.forward);
            Quaternion turn = Quaternion.AngleAxis(Yaw * TurnedIn(Progress), Vector3.up);
            Vector3 lying = modelRestPosition + Vector3.up * (tippedLift * over);

            model.SetLocalPositionAndRotation(
                Vector3.Lerp(lying, modelRestPosition, Shouldered),
                Quaternion.Slerp(turn * tip * modelRestRotation, modelRestRotation, Shouldered));
        }

        /// <summary>
        /// The body's extent in its root's own space, from its drawn meshes. Anything merely
        /// attached (a held tool, worn gear) is left out, the rule <see cref="BodyFrame"/> measures by.
        ///
        /// <para>
        /// From each renderer's LOCAL bounds, never <c>Renderer.bounds</c>: that is a world-aligned
        /// box, and for a body standing at forty-five degrees it is the square round a rotated
        /// rectangle. A skinned mesh's local bounds are in its root bone's space.
        /// </para>
        /// </summary>
        public static Bounds BodyInLocalSpace(Transform root)
        {
            Bounds? local = null;

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer) || BodyAttachment.Covers(r.transform, root)) continue;

                Transform space = r is SkinnedMeshRenderer skinned && skinned.rootBone != null
                    ? skinned.rootBone
                    : r.transform;
                Bounds own = r.localBounds;

                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = root.InverseTransformPoint(space.TransformPoint(new Vector3(
                        (corner & 1) == 0 ? own.min.x : own.max.x,
                        (corner & 2) == 0 ? own.min.y : own.max.y,
                        (corner & 4) == 0 ? own.min.z : own.max.z)));

                    if (local is Bounds b) { b.Encapsulate(point); local = b; }
                    else local = new Bounds(point, Vector3.zero);
                }
            }

            // Nothing drawn to measure: a person-sized guess about the root.
            return local ?? new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(0.5f, 1.8f, 0.3f));
        }
    }
}
