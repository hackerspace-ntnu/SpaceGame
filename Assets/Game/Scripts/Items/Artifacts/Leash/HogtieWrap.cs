// The rope a hogtie is: wound round the wrists, the ankles and the middle, and joined at one knot.
//
// Until this existed a tied body and a netted one read the same on screen — a limp ragdoll, with
// nothing to say what was holding it — and the untie gesture clicked a body rather than a rope.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.Items
{
    /// <summary>
    /// How the rope round a tied body is laid. Serialized on whatever ties people up — the leash's
    /// <see cref="HogtieSettings"/> and the lasso — and handed to each <see cref="HogtieWrap"/>.
    /// </summary>
    [System.Serializable]
    public class HogtieWrapLook
    {
        [Tooltip("Turns of rope round the middle.")]
        [SerializeField, Range(1, 12)] private int torsoTurns = 6;

        [Tooltip("Turns of rope round each bound wrist and ankle.")]
        [SerializeField, Range(1, 6)] private int limbTurns = 3;

        [Tooltip("Thickness of the wound rope, in metres. Thinner than a leash: this is cord " +
                 "round a wrist, not a tow line — but thick enough to read on a 3 m body from a few metres off.")]
        [SerializeField, Min(0.002f)] private float width = 0.045f;

        [Tooltip("How far one part of a turn's cross-section may stand out past the rest before the rope is let through it " +
                 "rather than pulled round it, as a multiple of the cross-section's middle radius. Each turn is laid on the " +
                 "outermost surface it crosses — clothes and boots included — so this is what keeps a cape edge or a hanging " +
                 "strap from ballooning it.")]
        [SerializeField, Range(1.1f, 3f)] private float looseLimit = 1.5f;

        [Tooltip("How far out from the trunk an arm or a leg lying against it may reach and still be bound in with the " +
                 "turns round the middle, as a multiple of the trunk's own reach in that direction. Arms drawn back along " +
                 "a person's sides are wrapped with the body; a quadruped's legs hanging below its belly are not.")]
        [SerializeField, Range(1f, 2f)] private float limbHug = 1.4f;

        [Tooltip("Radius of the turns round the middle, as a fraction of the body's length — only for a body with no " +
                 "mesh to measure. Any body that draws something is wound round what it draws.")]
        [SerializeField, Range(0.02f, 0.4f)] private float torsoRadiusFraction = 0.11f;

        [Tooltip("How much of the trunk the turns round the middle cover, centred halfway from the pelvis to the neck, " +
                 "as a fraction of the body's length. About the waist to the chest on a person, the barrel on a quadruped.")]
        [SerializeField, Range(0.02f, 0.6f)] private float torsoSpanFraction = 0.25f;

        [Tooltip("Radius of the turns round a wrist or an ankle, as a fraction of the body's length — only for a body " +
                 "with no mesh to measure.")]
        [SerializeField, Range(0.005f, 0.2f)] private float limbRadiusFraction = 0.03f;

        [Tooltip("Where the knot sits between the pelvis (0) and the middle of the bound limbs (1).")]
        [SerializeField, Range(0f, 1f)] private float knotTuck = 0.5f;

        public int TorsoTurns => Mathf.Max(1, torsoTurns);
        public int LimbTurns => Mathf.Max(1, limbTurns);
        public float Width => Mathf.Max(width, 0.002f);
        public float LooseLimit => Mathf.Max(1f, looseLimit);
        public float LimbHug => Mathf.Max(1f, limbHug);

        /// <summary>How the cord is laid on the skin, for <see cref="SkinCoil"/>.</summary>
        public SkinFit Fit => new SkinFit(Width, LooseLimit, LimbHug);
        public float TorsoRadiusFraction => torsoRadiusFraction;
        public float TorsoSpanFraction => torsoSpanFraction;
        public float LimbRadiusFraction => limbRadiusFraction;
        public float KnotTuck => Mathf.Clamp01(knotTuck);
    }

    /// <summary>
    /// The visible half of a tie: coils round the wrists, the ankles and the middle, drawn on the
    /// body as it lies every frame.
    ///
    /// <para>
    /// <b>Drawn on every machine, and holds nothing worth keeping.</b> Like every rope here it is
    /// local presentation built from state every machine already shares: nothing in it travels and
    /// nothing in it is saved. A machine that loads, joins or streams the body in late winds its own.
    /// </para>
    /// <para>
    /// <b>Two kinds of body.</b> The leash ties a RAGDOLL (<see cref="Show"/>), whose limbs are
    /// physics and fly apart unless something holds them, so springs draw them in to the knot. The
    /// lasso ties a body that keeps its ANIMATION and is posed by <see cref="TiedPose"/>
    /// (<see cref="Wind"/>): the pose already brings the hands to the feet, and the rope only has
    /// to be laid round them.
    /// </para>
    /// <para>
    /// <b>Laid on the skin, measured in the pose it is tied in.</b> Every turn is a
    /// <see cref="SkinCoil"/>: shaped to the body's own cross-section where it crosses it — the
    /// outermost surface, clothes included, pulled tight over hollows like a real cord — and riding
    /// the bone under it, so it stays on when the body moves. The cross-sections are measured once as
    /// the tie lands and once more when the rope is all on (<see cref="Draw(float)"/> reaching 1),
    /// because the body is tied STANDING and the rope has to fit it LYING.
    /// </para>
    /// <para>
    /// <b>Any body, not only a person.</b> A humanoid's wrists and ankles come off its avatar; a
    /// creature's are the ends of its skeleton that hang below the pelvis. See <see cref="LimbsOf"/>.
    /// </para>
    /// </summary>
    public sealed class HogtieWrap
    {
        /// <summary>Points per turn of a coil. See <see cref="SkinCoil.PointsPerTurn"/>.</summary>
        public const int PointsPerTurn = SkinCoil.PointsPerTurn;

        /// <summary>The humanoid joints a hogtie binds, in the order the rope visits them.</summary>
        private static readonly HumanBodyBones[] HumanLimbEnds =
        {
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        };

        private readonly List<Vector3> points = new List<Vector3>();
        private readonly List<SpringJoint> pulls = new List<SpringJoint>();

        private HogtieWrapLook wrap;
        private LeashRope look;
        private GameObject host;
        private LineRenderer line;

        private Transform hips;
        private Transform[] limbs;
        private Transform[] skeleton;
        private System.Func<Transform, bool>[] parts;
        private List<Transform> trunk;
        private float bodyLength;
        private float torsoSpan;

        private BodySurface surface;
        private SkinCoil[] coils;
        private bool measuredAllOn;

        /// <summary>Is anything drawn? False for a body with no skeleton to wind round.</summary>
        public bool IsShown => line != null;

        /// <summary>The limbs this wrap binds. Empty while hidden. For tests.</summary>
        public IReadOnlyList<Transform> Limbs => limbs ?? System.Array.Empty<Transform>();

        /// <summary>Where the turns are joined, as last drawn: what a rope that leads the body is tied to.</summary>
        public Vector3 Knot { get; private set; }

        /// <summary>
        /// The end of the rope as last drawn — the turn being laid right now while the rope is
        /// still going on (see <see cref="Draw(float)"/>), the end of the middle once it is all on.
        /// What the binder's hands are working.
        /// </summary>
        public Vector3 Tip { get; private set; }

        /// <summary>
        /// The rope's points as last drawn, world space: one knot, then each bound limb's turns, then
        /// the knot again and the turns round the middle. For tests, which measure how it sits.
        /// </summary>
        public IReadOnlyList<Vector3> Points => points;

        /// <summary>
        /// Wind the rope round a RAGDOLL <paramref name="body"/>, which must already be limp, and
        /// spring its bound limbs in to the knot.
        ///
        /// <para>
        /// Called once per tie, on the frame the hold went through — before the first physics step,
        /// so the bones are still where the standing pose left them. That is what lets
        /// <see cref="LimbsOf"/> tell a creature's legs from its head by height.
        /// </para>
        /// </summary>
        public void Show(GameObject body, HogtieSettings settings, LeashRope ropeLook)
        {
            Hide();

            RagdollRig rig = body != null ? body.GetComponentInParent<RagdollRig>() : null;
            if (rig == null || rig.Hips == null) return;

            Wind(body, rig.Hips, rig.BoneTransforms(), settings.Wrap, ropeLook);
            DrawLimbsIn(hips.GetComponent<Rigidbody>(), settings.LimbPull, settings.LimbPullDamper);
        }

        /// <summary>
        /// Wind the rope round <paramref name="body"/> by its <paramref name="bones"/>, with nothing
        /// pulling: for a body whose limbs are posed rather than simulated.
        /// </summary>
        /// <param name="pelvis">
        /// Where the middle is. A transform that is not one of the bones — the root of a creature
        /// whose ragdoll has never been built — is replaced by the skeleton's own pelvis.
        /// </param>
        /// <param name="material">
        /// The cord's material, when the caller has one of its own to hand over. The leash's look
        /// is resolved from the item registry, and a build or a moment where the leash is not
        /// registered dresses the rope with no material at all — which URP draws magenta.
        /// </param>
        public void Wind(GameObject body, Transform pelvis, Transform[] bones, HogtieWrapLook wrapLook,
                         LeashRope ropeLook, Material material = null)
        {
            Hide();
            if (body == null || pelvis == null || bones == null) return;

            wrap = wrapLook;
            look = ropeLook;

            // The skin first: which bones actually carry the body is what tells a leg from an IK
            // target floating beside it, and a robot's skeleton is the bones its plates hang from.
            surface = new BodySurface(body.transform, bones);
            skeleton = surface.Skeleton;
            hips = surface.IsBone(pelvis) ? pelvis : PelvisOf(skeleton) ?? pelvis;
            if (System.Array.IndexOf(skeleton, hips) < 0) skeleton = Append(skeleton, hips);
            limbs = LimbsOf(body, hips, skeleton);

            bodyLength = BodyLength(skeleton);
            torsoSpan = bodyLength * wrap.TorsoSpanFraction;
            trunk = TrunkOf(hips, skeleton);

            parts = PartsOf(limbs, trunk);
            surface.Sample(parts);
            coils = new SkinCoil[limbs.Length + 1];
            Measure();
            measuredAllOn = false;

            host = new GameObject("HogtieRope");
            host.transform.SetParent(body.transform, false);
            line = look.Dress(host.AddComponent<LineRenderer>());
            if (material != null) line.sharedMaterial = material;
            line.startWidth = wrap.Width;
            line.endWidth = wrap.Width;

            Lay(1f);
        }

        /// <summary>Take the rope off and let the limbs go. Safe twice and safe on a destroyed body.</summary>
        public void Hide()
        {
            // The springs first: they are what holds the limbs, and a body standing up with a joint
            // still drawing its hands to its pelvis recovers into that pose.
            foreach (SpringJoint pull in pulls) RuntimeObjects.Discard(pull);
            pulls.Clear();

            RuntimeObjects.Discard(host);
            host = null;
            line = null;
            hips = null;
            limbs = null;
            skeleton = null;
            trunk = null;
            parts = null;
            surface = null;
            coils = null;
            points.Clear();
        }

        /// <summary>Lay the whole rope along the body as it lies this frame. Every machine, after physics and posing.</summary>
        public void Draw() => Draw(1f);

        /// <summary>
        /// Lay the first <paramref name="reveal"/> of the rope, 0 to 1, along the body as it lies this
        /// frame — wrists, then ankles, then the middle, the order the rope visits them. A tie being
        /// made is this run from 0 to 1 while the binder works, so the turns go on one after
        /// another instead of appearing all at once.
        ///
        /// <para>
        /// The first frame the whole rope is on, the turns are measured again round the body as it
        /// now lies: a tie lands on somebody standing, and the cross-section of a wrist hanging at
        /// the side is not the one it has drawn up behind the back.
        /// </para>
        /// </summary>
        public void Draw(float reveal)
        {
            if (line == null || hips == null) return;

            if (!measuredAllOn && reveal >= 1f)
            {
                Measure();
                measuredAllOn = true;
            }

            Lay(reveal);
        }

        private void Lay(float reveal)
        {
            points.Clear();

            // Where the bound limbs are NOW, not where they were when the rope went on: a posed body
            // brings them together after the tie lands, and a ragdoll's springs pull them in over a
            // second or two. A knot fixed at the start would hang in the air where they used to be.
            Vector3 knot = Vector3.Lerp(hips.position, Centroid(limbs, hips.position), wrap.KnotTuck);
            Knot = knot;

            for (int i = 0; i < limbs.Length; i++)
            {
                if (limbs[i] == null) continue;

                points.Add(knot);
                coils[i].AddTo(points);
            }

            points.Add(knot);
            coils[limbs.Length].AddTo(points);

            // By point rather than by length: the turns are evenly spaced, so the two agree closely
            // enough, and a rope that is still going on is never measured to the millimetre.
            int shown = Mathf.Clamp(Mathf.CeilToInt(points.Count * Mathf.Clamp01(reveal)), 0, points.Count);
            line.positionCount = shown;
            for (int i = 0; i < shown; i++) line.SetPosition(i, points[i]);
            Tip = shown > 0 ? points[shown - 1] : knot;

            // The braid keeps its proportions at this thinner gauge: a leash's strands laid along
            // cord half as thick would be stretched twice as long as they are wide.
            float travelled = 0f;
            for (int i = 1; i < shown; i++) travelled += Vector3.Distance(points[i - 1], points[i]);

            float braids = look.BraidsPerMetre * look.Width / wrap.Width;
            line.textureScale = new Vector2(Mathf.Max(0.01f, travelled * braids), 1f);
        }

        // ── Where the turns go ─────────────────────────────────────────────────

        /// <summary>
        /// Which bones each coil is measured round: one entry per bound limb, then the middle, then
        /// what the middle may also take in (<see cref="LyingAlongside"/>). A wrist's coil is measured
        /// by its forearm and hand — never the cape the hand rests on.
        ///
        /// <para>
        /// The middle's own outline is everything that is not a bound limb's chain — not the spine
        /// bones alone: a quadruped's belly and flanks are weighted to bones of their own, and a hull
        /// of the back alone ran the rope through its belly. A chain runs up from the limb until the
        /// trunk or a bone that carries another bound limb too: a quadruped's forelegs hang from a
        /// spine bone past the trunk's first few, and that bone, with the whole forebody, is no leg.
        /// </para>
        /// <para>
        /// The upper chain (upper arm, thigh — not the forearm and hand, shin and foot the limb coils
        /// are on) is what the middle may take in where it lies against the trunk: drawn back in the
        /// hogtie a person's arms lie along their sides, and a cord round the middle goes over them,
        /// not through them. A quadruped's legs hang below its belly and are left out by the same test.
        /// </para>
        /// </summary>
        private System.Func<Transform, bool>[] PartsOf(Transform[] bound, List<Transform> trunk)
        {
            var of = new System.Func<Transform, bool>[bound.Length + 2];
            var chains = new List<Transform>(bound.Length);
            var ends = new List<Transform>(bound.Length);

            for (int i = 0; i < bound.Length; i++)
            {
                Transform limb = bound[i];
                Transform above = CarrierOf(limb);
                of[i] = owner => owner == above || owner == limb || owner.IsChildOf(limb);

                Transform root = limb;
                for (Transform up = NearestBoneAbove(limb, skeleton);
                     up != null && !trunk.Contains(up) && !CarriesAnother(up, limb, bound);
                     up = NearestBoneAbove(up, skeleton))
                    root = up;
                chains.Add(root);
                ends.Add(trunk.Contains(above) ? limb : above);
            }

            of[bound.Length] = owner => !Under(owner, chains);
            of[bound.Length + 1] = owner => Under(owner, chains) && !Under(owner, ends);
            return of;
        }

        /// <summary>Index into the parts of what the middle's turns may also take in. See <see cref="PartsOf"/>.</summary>
        private int LyingAlongside => limbs.Length + 1;

        private static bool Under(Transform owner, List<Transform> roots)
        {
            foreach (Transform root in roots)
                if (owner == root || owner.IsChildOf(root)) return true;
            return false;
        }

        private static bool CarriesAnother(Transform bone, Transform limb, Transform[] bound)
        {
            foreach (Transform other in bound)
                if (other != null && other != limb && other.IsChildOf(bone)) return true;
            return false;
        }

        /// <summary>The bone a limb's coil rides on: the one the limb hangs from — a wrist's forearm.</summary>
        private Transform CarrierOf(Transform limb) => limb.parent != null ? limb.parent : hips;

        /// <summary>
        /// Shape every coil to the body as it is posed right now, and hang it on the bone under it.
        /// Cheap — a few thousand skinned points — so it runs as the tie lands and again once it is on.
        /// </summary>
        private void Measure()
        {
            surface.Pose();

            for (int i = 0; i < limbs.Length; i++)
            {
                Transform limb = limbs[i];
                if (limb == null) continue;

                // From the joint up the limb, not centred on it: a cord round a wrist sits on the
                // narrow end of the forearm, not over the palm and the fingers.
                Transform above = CarrierOf(limb);
                Vector3 along = limb.position - above.position;
                float span = wrap.LimbTurns * wrap.Width * LimbTurnPitch;
                Vector3 centre = limb.position - (along.sqrMagnitude > 1e-8f ? along.normalized : Vector3.up) * (span * 0.5f);

                coils[i] = SkinCoil.Round(surface, parts[i], null, above, centre, along, wrap.LimbTurns, span,
                                          bodyLength * wrap.LimbRadiusFraction, wrap.Fit);
            }

            // Round the middle of the trunk, pelvis to neck: on a person the waist to the chest, on a
            // quadruped the barrel between its legs. A band starting AT the pelvis runs, on anything
            // whose trunk lies level, between the haunches — through the thighs.
            Vector3 rise = trunk[trunk.Count - 1].position - hips.position;
            bool hasTrunk = rise.sqrMagnitude > 1e-6f;
            Vector3 up = hasTrunk ? rise.normalized : hips.up;
            Vector3 middle = hasTrunk ? hips.position + rise * 0.5f : hips.position + up * (torsoSpan * 0.5f);

            coils[limbs.Length] = SkinCoil.Round(surface, parts[limbs.Length], parts[LyingAlongside], NearestTo(middle, trunk),
                                                 middle, up, wrap.TorsoTurns, torsoSpan, bodyLength * wrap.TorsoRadiusFraction,
                                                 wrap.Fit);
        }

        private static Transform NearestTo(Vector3 point, List<Transform> among)
        {
            Transform nearest = among[0];
            foreach (Transform bone in among)
                if ((bone.position - point).sqrMagnitude < (nearest.position - point).sqrMagnitude) nearest = bone;
            return nearest;
        }

        /// <summary>Rope widths of the limb along each turn of a wrist or an ankle: a cord wound with a little air between its turns.</summary>
        private const float LimbTurnPitch = 1.5f;

        // ── The skeleton ───────────────────────────────────────────────────────

        /// <summary>
        /// The ends of the limbs a hogtie binds: hands and feet.
        ///
        /// <para>
        /// From the avatar when the body has a humanoid one, because that names them outright. A
        /// body without one — every creature, and any humanoid whose import lost its avatar
        /// (<c>isHuman</c> false, which the project has shipped before) — falls back to the leaves
        /// of its skeleton that hang well BELOW the pelvis (<see cref="LegReach"/>): legs, whatever
        /// their number, and not the head, a lowered tongue or a tail. If the body is already lying down there is no "below" worth
        /// trusting, and every leaf is bound instead.
        /// </para>
        /// </summary>
        public static Transform[] LimbsOf(GameObject body, Transform hips, Transform[] bones)
        {
            Animator animator = body != null ? body.GetComponentInChildren<Animator>(true) : null;

            if (animator != null && animator.isHuman)
            {
                var named = new List<Transform>(HumanLimbEnds.Length);
                foreach (HumanBodyBones end in HumanLimbEnds)
                {
                    Transform found = animator.GetBoneTransform(end);
                    if (found != null) named.Add(found);
                }

                if (named.Count >= 2) return named.ToArray();
            }

            var leaves = new List<Transform>();
            foreach (Transform bone in bones)
            {
                if (bone == null || bone == hips) continue;
                if (!HasBoneBelow(bone, bones)) leaves.Add(bone);
            }

            // Well below, not merely below: a grazer's lowered tongue and a tail curling down past
            // its rump are under the pelvis too, and only the legs reach the ground.
            float lowest = hips.position.y;
            foreach (Transform bone in bones)
                if (bone != null) lowest = Mathf.Min(lowest, bone.position.y);
            float reach = (hips.position.y - lowest) * LegReach;

            List<Transform> low = leaves.FindAll(leaf => leaf.position.y < hips.position.y - reach);
            return (low.Count >= 2 ? low : leaves).ToArray();
        }

        /// <summary>
        /// How far down from the pelvis toward the lowest bone a creature's leaf must hang to count as a
        /// leg's end, as a share of that drop. A leg reaches the ground; a head or a tail stops near the pelvis's height.
        /// </summary>
        private const float LegReach = 0.5f;

        private static bool HasBoneBelow(Transform bone, Transform[] bones)
        {
            foreach (Transform other in bones)
                if (other != null && other != bone && other.IsChildOf(bone)) return true;

            return false;
        }

        /// <summary>
        /// The pelvis to wind the middle round when the one handed in is not a bone — a creature's
        /// root, which sits on the ground under its feet: the bone the most of the skeleton hangs from,
        /// followed down any single-child chain (a root bone above the hips) to where it first
        /// branches into legs and a spine.
        /// </summary>
        private static Transform PelvisOf(Transform[] bones)
        {
            Transform top = null;
            int most = -1;
            foreach (Transform bone in bones)
            {
                int carried = Carried(bone, bones);
                if (bone == null || carried <= most) continue;
                most = carried;
                top = bone;
            }

            if (top == null) return null;

            while (true)
            {
                Transform only = null;
                int children = 0;
                foreach (Transform bone in bones)
                {
                    if (bone == null || NearestBoneAbove(bone, bones) != top) continue;
                    only = bone;
                    children++;
                }

                if (children != 1) return top;
                top = only;
            }
        }

        private static Transform[] Append(Transform[] bones, Transform bone)
        {
            var with = new Transform[bones.Length + 1];
            bones.CopyTo(with, 0);
            with[bones.Length] = bone;
            return with;
        }

        private static int Carried(Transform bone, Transform[] bones)
        {
            if (bone == null) return -1;

            int carried = 0;
            foreach (Transform other in bones)
                if (other != null && other != bone && other.IsChildOf(bone)) carried++;
            return carried;
        }

        /// <summary>
        /// Springs from each bound limb to the knot on the pelvis — a ragdoll's limbs only.
        ///
        /// <para>
        /// On the limb's own simulated bone — a hand is not one, its forearm is — and never on the
        /// pelvis itself, which is what the springs pull toward. A limb whose nearest simulated bone
        /// IS the pelvis has nothing to draw in and is wound round where it lies.
        /// </para>
        /// </summary>
        private void DrawLimbsIn(Rigidbody pelvis, float spring, float damper)
        {
            if (pelvis == null || spring <= 0f) return;

            Vector3 knotLocal = hips.InverseTransformPoint(
                Vector3.Lerp(hips.position, Centroid(limbs, hips.position), wrap.KnotTuck));

            foreach (Transform limb in limbs)
            {
                Rigidbody bone = limb.GetComponentInParent<Rigidbody>();
                if (bone == null || bone == pelvis) continue;

                SpringJoint pull = bone.gameObject.AddComponent<SpringJoint>();
                pull.connectedBody = pelvis;
                pull.autoConfigureConnectedAnchor = false;
                pull.anchor = bone.transform.InverseTransformPoint(limb.position);
                pull.connectedAnchor = knotLocal;
                pull.spring = spring;
                pull.damper = damper;
                pull.minDistance = 0f;
                pull.maxDistance = 0f;
                pull.enableCollision = false;
                pulls.Add(pull);
            }
        }

        /// <summary>
        /// The trunk, pelvis first: the chain up from it that carries the most of the skeleton — spine, chest, neck — not
        /// the shoulders and thighs that branch off it. Asked of the skeleton rather than of the avatar so a creature gets
        /// the same answer a person does: on a quadruped the chain that holds the forelegs and the neck.
        /// </summary>
        private static List<Transform> TrunkOf(Transform hips, Transform[] bones)
        {
            var trunk = new List<Transform> { hips };
            Transform at = hips;

            for (int step = 0; step < TrunkBones; step++)
            {
                Transform next = null;
                int most = -1;

                foreach (Transform bone in bones)
                {
                    if (bone == null || NearestBoneAbove(bone, bones) != at) continue;
                    if (at == hips && bone.position.y < hips.position.y) continue;

                    int carried = Carried(bone, bones);
                    if (carried <= most) continue;

                    most = carried;
                    next = bone;
                }

                if (next == null) break;
                trunk.Add(next);
                at = next;
            }

            return trunk;
        }

        /// <summary>Bones up the trunk from the pelvis: spine, chest, upper chest, neck.</summary>
        private const int TrunkBones = 4;

        /// <summary>The closest ancestor of <paramref name="bone"/> that is itself one of the bones.</summary>
        internal static Transform NearestBoneAbove(Transform bone, Transform[] bones)
        {
            for (Transform up = bone.parent; up != null; up = up.parent)
                if (System.Array.IndexOf(bones, up) >= 0) return up;

            return null;
        }

        private static Vector3 Centroid(Transform[] of, Vector3 fallback)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (Transform t in of)
            {
                if (t == null) continue;
                sum += t.position;
                count++;
            }

            return count > 0 ? sum / count : fallback;
        }

        /// <summary>The body's longest extent, from its bones. What the fallback radii and the middle's span are fractions of.</summary>
        private static float BodyLength(Transform[] bones)
        {
            var bounds = new Bounds();
            bool any = false;

            foreach (Transform bone in bones)
            {
                if (bone == null) continue;

                if (any) bounds.Encapsulate(bone.position);
                else bounds = new Bounds(bone.position, Vector3.zero);
                any = true;
            }

            return any ? Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) : 1f;
        }
    }
}
