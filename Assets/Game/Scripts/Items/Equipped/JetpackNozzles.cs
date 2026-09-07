// Everything the jetpack throws at the senses: two pods of vectoring hardware, four flames, the
// red heat glow on the nozzle tips and the smoke that comes off them.
//
// Runs on EVERY machine. The owner drives it from its live flight; a peer drives it from the
// item's hold stream, which carries the same three numbers. One component, one appearance,
// whichever machine it is on.
using System.Collections.Generic;
using SpaceGame.Gear.Jetpack;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Points the pods where the flight says the nozzles are, and shows what that costs.
    ///
    /// <para>
    /// <b>A pod is a SIDE, not a parent.</b> The obvious grouping — everything under one mount
    /// empty — does not survive the export: `_exportlib` writes every mesh as a direct child of the
    /// model root with its world transform baked onto its own node, and `MOUNT_Jetpack_L`/`_R`
    /// arrive as empty SIBLINGS rather than as parents. So the side is read off the name suffix
    /// the export guarantees (`_R` / `_L`, and `_ItemR` / `_ItemL` for the carried arrangement),
    /// which is the same discipline `WornVisual` uses and for the same reason: names survive a
    /// re-export, hierarchy and fileIDs do not.
    /// </para>
    /// <para>
    /// Every rotation is relative to a rest pose captured in <c>Awake</c>, and the gimbal each pod
    /// swings about is MEASURED off that side's own nozzle group. The pods are hand-built and keep
    /// being re-arranged — they have already been yawed 90° once — so anything assumed about an
    /// axis or an origin here becomes silently wrong on the next save.
    /// </para>
    /// </summary>
    public class JetpackNozzles : MonoBehaviour
    {
        /// <summary>
        /// Name fragments of the parts that swing with the thrust.
        ///
        /// The exhausts are in this list, and have to be: the flame comes out of the nozzle, so a
        /// gimbal that moved the hardware and left the fire pointing straight down would be worse
        /// than not vectoring at all.
        /// </summary>
        private static readonly string[] VectoredRoles =
        {
            "NozzleYoke", "NozzleInnerCone", "NozzleInnerCollar",
            "NozzleOuterCone", "NozzleOuterCollar", "Exhaust",
        };

        /// <summary>Name fragments of the parts that glow when the pack is too hot.</summary>
        private static readonly string[] GlowRoles = { "NozzleInnerCone", "NozzleOuterCone" };

        /// <summary>Name fragments of the meshes the flame is drawn on. Two per pod, four total.</summary>
        private static readonly string[] ExhaustRoles = { "ExhaustInner", "ExhaustOuter" };

        /// <summary>
        /// What <c>JetpackBuilder</c> calls the generated plume cones.
        ///
        /// <para>
        /// They are SIBLINGS of the nozzles rather than children of them — the builder hangs them
        /// off the model root, because every left-hand part carries the mirror as a negative,
        /// non-uniform scale that a child would inherit. The name still carries the exhaust they
        /// belong to, so they sort into the right pod, and because the name contains "Exhaust" they
        /// land in <see cref="VectoredRoles"/> and swing with the hardware for free.
        /// </para>
        /// </summary>
        public const string FlamePrefix = "JetFlame_";

        [Header("Flame")]
        [Tooltip("Flame length while sinking with Space released, as a fraction of the full " +
                 "one. Well above zero: a pack coming down under power that showed nothing would " +
                 "read as switched off, and the flame is the one thing that separates letting go " +
                 "(lit, a steady sink, cooling) from an overheat (dark, a real fall). Six tenths " +
                 "rather than a third, so the difference is legible at a glance.")]
        [SerializeField, Range(0f, 1f)] private float idleFlame = 0.6f;

        [Header("Heat")]
        [Tooltip("Colour the nozzle tips glow at full heat. Driven through a property block, so " +
                 "no material is instanced and nothing leaks.")]
        [SerializeField] private Color glowColor = new Color(1f, 0.16f, 0.05f, 1f);

        [Tooltip("How bright the glow gets at full heat. Above 1 it blooms.")]
        [SerializeField, Min(0f)] private float glowIntensity = 3.5f;

        [Header("Smoke")]
        [Tooltip("One system for all four nozzles. Built playing with its emission DISABLED and " +
                 "culling set to AlwaysSimulate, so Emit() is the only thing that makes a puff " +
                 "and puffs already in the air keep rising while the pack is off screen.")]
        [SerializeField] private ParticleSystem smoke;

        [Tooltip("Puffs per second per nozzle at full throttle, cold. The trail — every lit motor " +
                 "leaves one, because a jet that burned clean would read as switched off from " +
                 "behind, which is the only angle anyone else ever sees it from.")]
        [SerializeField, Min(0f)] private float smokePerSecond = 10f;

        [Tooltip("Extra puffs per second per nozzle once the pack is past its warning fraction, " +
                 "on top of the trail. This is the part that is a WARNING rather than exhaust, so " +
                 "it has to be clearly heavier than the clean trail or it says nothing.")]
        [SerializeField, Min(0f)] private float overheatSmokePerSecond = 22f;

        [Tooltip("How fast a puff leaves the nozzle, m/s at full throttle. The system simulates " +
                 "in world space, so this is what the puff keeps while the player flies out from " +
                 "under it — the difference between a trail and a cloud that follows you.")]
        [SerializeField, Min(0f)] private float smokeSpeed = 5f;

        [Tooltip("How far past the end of the flame a puff is born, as a multiple of the flame's " +
                 "own length. Over 1 so smoke never appears inside the fire, where it is a grey " +
                 "smudge over the brightest thing on the machine.")]
        [SerializeField, Min(0f)] private float smokeStandoff = 1.15f;

        [Tooltip("Random scatter on where a puff is born, metres. Without it four nozzles produce " +
                 "four dead-straight strings of beads.")]
        [SerializeField, Min(0f)] private float smokeScatter = 0.09f;

        /// <summary>One pod: which side it is, what swings, and what it swings about.</summary>
        private class Pod
        {
            /// <summary>The frame the rest poses and the pivot are expressed in — the model root
            /// every part is a direct child of after the export flattens the hierarchy.</summary>
            public Transform Root;

            /// <summary>+1 for the right-hand pod, -1 for the left. Sets the differential roll.</summary>
            public float Side;

            /// <summary>The gimbal point, in <see cref="Root"/>'s local space. Measured.</summary>
            public Vector3 Pivot;

            public readonly List<Transform> Vectored = new();
            public readonly List<Vector3> RestPositions = new();
            public readonly List<Quaternion> RestRotations = new();

            public readonly List<Transform> Flames = new();
            public readonly List<Renderer> FlameRenderers = new();
            public readonly List<Vector3> FlameRestScales = new();
            public readonly List<Transform> Exhausts = new();
            public readonly List<Renderer> Glow = new();
        }

        private readonly List<Pod> pods = new();
        private MaterialPropertyBlock block;
        private Transform wearer;
        private float smokeDebt;
        private bool reported;

        private static readonly int ThrottleId = Shader.PropertyToID("_Throttle");
        private static readonly int HeatId = Shader.PropertyToID("_Heat");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        /// <summary>
        /// How many pods were resolved. Two per model, so four on a finished pack.
        ///
        /// Exposed because the resolution happens in <c>Awake</c> against NAMES in a hand-built
        /// model, which is precisely the kind of binding that breaks quietly on a re-export — and
        /// a check that can only be run by flying about is a check nobody runs. <c>JetpackBuilder</c>
        /// instantiates the built prefab and reads this.
        /// </summary>
        public int PodCount => pods.Count;

        /// <summary>How many plume cones were resolved. Two per pod, so eight on a finished pack.</summary>
        public int FlameCount
        {
            get
            {
                int total = 0;
                foreach (Pod pod in pods) total += pod.Flames.Count;
                return total;
            }
        }

        /// <summary>Where the nozzles are pointing. Written every frame by <c>JetpackItem</c>.</summary>
        public JetNozzle Nozzle { get; set; }

        /// <summary>Throttle 0..1. Zero means the motors are cut and the flames go out.</summary>
        public float Throttle { get; set; }

        /// <summary>Heat 0..1. Drives the tip glow and the smoke, whether or not the pack is lit.</summary>
        public float Heat { get; set; }

        /// <summary>
        /// Fraction of heat above which the tips glow and smoke. Handed in by the item from the
        /// flight config, so the warning the player sees and the warning the gauge draws are the
        /// same number.
        /// </summary>
        public float WarnFraction { get; set; } = 0.7f;

        /// <summary>
        /// Whose frame the nozzles deflect in. Set by <c>JetpackItem</c> when the pack is worn;
        /// null while it is lying on the sand or standing on the gear screen, where the pack's own
        /// transform is the only frame there is.
        /// </summary>
        public void SetWearer(Transform value) => wearer = value;

        private void Awake() => Resolve();

        /// <summary>
        /// Sort every part into its pod, capture the rest poses, and measure each gimbal.
        ///
        /// <para>
        /// <b>Public, and called by <c>Awake</c> rather than being <c>Awake</c>.</b> Everything it
        /// binds is a NAME in a hand-built model, which is the binding most likely to break on a
        /// re-export — and <c>Awake</c> does not run in the editor, so every edit-mode check of it
        /// lies. That is the trap the wingsuit paid for with a pack scaled to a sliver. Exposing
        /// the resolution lets <c>JetpackBuilder</c> instantiate the finished prefab and assert
        /// the counts without entering play mode; calling it twice is safe.
        /// </para>
        ///
        /// <para>
        /// Both of the item's models are walked, not just the visible one. <c>WornVisual</c> swaps
        /// which is active as the pack is picked up and put on, and it does that by enabling a
        /// GameObject rather than by rebuilding anything — so a component that had only found the
        /// form that happened to be on at <c>Awake</c> would stop vectoring the moment the pack was
        /// worn.
        /// </para>
        /// </summary>
        public void Resolve()
        {
            block ??= new MaterialPropertyBlock();
            pods.Clear();
            reported = false;

            var bySide = new Dictionary<float, Pod>();

            foreach (Transform part in GetComponentsInChildren<Transform>(true))
            {
                if (!MatchesAny(part.name, VectoredRoles)) continue;
                if (part.parent == null) continue;

                float side = SideOf(part.name);

                // Keyed by side AND kept per model root, so the carried pair and the worn pair do
                // not end up sharing a pod — they are different objects at different places and a
                // gimbal measured across both would sit between them.
                Pod pod = Resolve(bySide, side, part.parent);

                pod.Vectored.Add(part);
                pod.RestPositions.Add(part.localPosition);
                pod.RestRotations.Add(part.localRotation);

                if (MatchesAny(part.name, ExhaustRoles) && !IsFlame(part.name))
                    pod.Exhausts.Add(part);
            }

            foreach (Pod pod in pods)
            {
                pod.Pivot = GimbalOf(pod);
                CollectRenderers(pod);
            }

            Report();
        }

        /// <summary>
        /// The pod a part belongs to: its side, under its own model root. A second root — the item
        /// carries two — starts a second pair rather than joining the first.
        /// </summary>
        private Pod Resolve(Dictionary<float, Pod> bySide, float side, Transform root)
        {
            foreach (Pod existing in pods)
                if (existing.Side == side && existing.Root == root) return existing;

            var pod = new Pod { Root = root, Side = side };
            pods.Add(pod);
            bySide[side] = pod;

            return pod;
        }

        /// <summary>Find this pod's flames and glowing tips among the parts already sorted into it.</summary>
        private void CollectRenderers(Pod pod)
        {
            foreach (Transform part in pod.Vectored)
            {
                var renderer = part.GetComponent<Renderer>();
                if (renderer == null) continue;

                if (IsFlame(part.name))
                {
                    pod.Flames.Add(part);
                    pod.FlameRenderers.Add(renderer);

                    // Captured here rather than read live, because DrawFlames overwrites the scale
                    // every frame — one frame of that and "rest" would be whatever the throttle
                    // last left behind.
                    pod.FlameRestScales.Add(part.localScale);
                    continue;
                }

                if (MatchesAny(part.name, GlowRoles)) pod.Glow.Add(renderer);
            }
        }

        private static bool IsFlame(string name) => name.StartsWith(FlamePrefix);

        /// <summary>
        /// Where this pod's nozzles swing about, in the root's local space.
        ///
        /// <para>
        /// The pod hangs below its mount — the rail crosses the top of the housing, tank above,
        /// nozzles below (jetpack_BUILD.md) — so the gimbal is the TOP of the nozzle group along
        /// the root's own vertical, not its centre and not the mount empty, which sits a whole
        /// housing higher and would swing the nozzles through an arc twice as wide as the pod.
        /// </para>
        /// <para>
        /// Falls back to the group's centre if there is nothing to measure. A pod that swings
        /// about its middle looks odd; one that throws a NullReference does not draw at all.
        /// </para>
        /// </summary>
        private Vector3 GimbalOf(Pod pod)
        {
            Bounds group = default;
            bool any = false;

            foreach (Transform part in pod.Vectored)
            {
                // The hardware decides where the gimbal is. A flame hangs well below the nozzle it
                // comes out of, so measuring it in would drag the pivot down into the fire.
                if (IsFlame(part.name)) continue;

                var renderer = part.GetComponent<Renderer>();
                if (renderer == null) continue;

                Bounds local = LocalBounds(renderer, pod.Root);
                if (!any) { group = local; any = true; }
                else group.Encapsulate(local);
            }

            if (!any) return Vector3.zero;

            Vector3 pivot = group.center;
            pivot.y = group.max.y;

            return pivot;
        }

        /// <summary>A renderer's world bounds expressed in another transform's local space.</summary>
        private static Bounds LocalBounds(Renderer renderer, Transform frame)
        {
            Bounds world = renderer.bounds;
            Vector3 centre = frame.InverseTransformPoint(world.center);
            Vector3 extent = frame.InverseTransformVector(world.extents);

            return new Bounds(centre, new Vector3(Mathf.Abs(extent.x) * 2f,
                                                  Mathf.Abs(extent.y) * 2f,
                                                  Mathf.Abs(extent.z) * 2f));
        }

        private static bool MatchesAny(string name, string[] roles)
        {
            foreach (string role in roles)
                if (name.Contains(role)) return true;

            return false;
        }

        /// <summary>
        /// Which pod a part is on, from the suffix the export writes: <c>_R</c> / <c>_L</c> for the
        /// worn pair and <c>_ItemR</c> / <c>_ItemL</c> for the carried one, so the last character
        /// carries it in both.
        /// </summary>
        private static float SideOf(string name) => name.EndsWith("L") ? -1f : 1f;

        private void Report()
        {
            if (reported) return;
            reported = true;

            int flames = FlameCount;

            // Two models on the item — carried and worn — so two pairs of pods and eight flames.
            if (pods.Count == 4 && flames == 8) return;

            // Loud, once. A pack with no pods still flies — the physics is nowhere near here — so
            // the failure is a machine that shoves the player around with nothing moving on it,
            // which is exactly the sort of thing that gets blamed on the flight code.
            Debug.LogError(
                $"JetpackNozzles: found {pods.Count} pod(s) and {flames} flame(s); expected 4 and 8 " +
                "(a pair for the carried model and a pair for the worn one). Check the part names " +
                "against jetpack_export.py, then re-run Tools/SpaceGame/Items/Build Jetpack.", this);
        }

        private void LateUpdate()
        {
            Quaternion frame = FrameRotation();

            foreach (Pod pod in pods)
            {
                Aim(pod, frame);
                DrawFlames(pod);
                DrawGlow(pod);
            }

            Smoke(Time.deltaTime);
        }

        /// <summary>
        /// The deflection, as a world-space rotation.
        ///
        /// <para>
        /// The nozzles rake the OPPOSITE way to the push, which is the joke of a vectoring nozzle:
        /// to shove the pilot forward the exhaust has to point backwards. That falls out for free
        /// — a pod's rest exhaust points DOWN and the thrust points UP, so rotating the assembly
        /// by <see cref="JetNozzle.Rotation"/> swings the exhaust exactly as far aft as the thrust
        /// swings forward, with no sign to get wrong.
        /// </para>
        /// <para>
        /// Built in the WEARER's yaw frame rather than applied directly: the deflection is defined
        /// against the player's forward, and a pod bolted to a rail at some authored angle has no
        /// idea which way that is.
        /// </para>
        /// </summary>
        private Quaternion FrameRotation()
        {
            Transform frame = wearer != null ? wearer : transform;
            Quaternion yaw = Quaternion.Euler(0f, frame.eulerAngles.y, 0f);

            return yaw * Nozzle.Rotation * Quaternion.Inverse(yaw);
        }

        /// <summary>
        /// Swing one pod's parts about its gimbal.
        ///
        /// The differential roll is added per pod and is cosmetic in the strict sense that it does
        /// not change the thrust — the sideways rake already did that — but without it nothing on
        /// the machine says which way the pilot asked to go, and a strafe reads as a slide.
        /// </summary>
        private void Aim(Pod pod, Quaternion worldDelta)
        {
            Transform frame = wearer != null ? wearer : transform;

            Quaternion differential = Quaternion.AngleAxis(
                Nozzle.Roll * pod.Side * DifferentialShare, frame.forward);

            Quaternion world = differential * worldDelta;

            // Into the root's local space, where the rest poses were captured. Same frame on both
            // sides, so a pod authored at any angle behaves the same.
            Quaternion local = Quaternion.Inverse(pod.Root.rotation) * world * pod.Root.rotation;

            for (int i = 0; i < pod.Vectored.Count; i++)
            {
                Transform part = pod.Vectored[i];
                if (part == null) continue;

                part.localPosition = pod.Pivot + local * (pod.RestPositions[i] - pod.Pivot);
                part.localRotation = local * pod.RestRotations[i];
            }
        }

        /// <summary>
        /// How far the two pods roll away from each other per degree of sideways command. A
        /// constant rather than a config field: this is the pods leaning within the deflection the
        /// flight already chose, and it must not be tunable to the point of disagreeing with it.
        /// </summary>
        private const float DifferentialShare = 0.45f;

        /// <summary>
        /// The four flames. The cone is stretched along its own axis by the throttle and the
        /// shader is told the same number, so the silhouette and the shading can never disagree.
        /// </summary>
        private void DrawFlames(Pod pod)
        {
            float lit = Throttle <= 0f ? 0f : Mathf.Max(Throttle, idleFlame);

            for (int i = 0; i < pod.Flames.Count; i++)
            {
                Transform flame = pod.Flames[i];
                Renderer renderer = pod.FlameRenderers[i];
                if (flame == null || renderer == null) continue;

                bool on = lit > 0.001f;
                if (renderer.enabled != on) renderer.enabled = on;
                if (!on) continue;

                // The cone is authored along its own +Y, so the throttle stretches Y and pinches
                // the other two. Scaling the transform rather than only the shader keeps the
                // silhouette honest — a flame that shaded shorter but stayed the same size would
                // still light and occlude at full length.
                //
                // THIS IS THE ONLY PLACE THE THROTTLE SHORTENS THE FLAME. JetFlame.shader used to
                // divide its length coordinate by the same number, which threw away the far end of
                // a cone that had already been scaled down — a hover drew a stub of a stub, small
                // enough to be mistaken for no flame at all.
                Vector3 rest = pod.FlameRestScales[i];
                float pinch = Mathf.Lerp(0.65f, 1f, lit);
                flame.localScale = new Vector3(rest.x * pinch, rest.y * lit, rest.z * pinch);

                renderer.GetPropertyBlock(block);
                block.SetFloat(ThrottleId, lit);
                block.SetFloat(HeatId, Heat);
                renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// The tips going red. Zero below the warning fraction, then ramped to full — so the glow
        /// arriving IS the warning, and it cannot be confused with the flame's own light.
        /// </summary>
        private void DrawGlow(Pod pod)
        {
            float over = Mathf.InverseLerp(WarnFraction, 1f, Heat);
            Color emission = glowColor * (over * glowIntensity);

            foreach (Renderer tip in pod.Glow)
            {
                if (tip == null) continue;

                tip.GetPropertyBlock(block);
                block.SetColor(EmissionId, emission);
                tip.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// Puffs off the nozzles: a trail while the motors are lit, and much more once it is hot.
        ///
        /// <para>
        /// One system for every nozzle, emitted at a position rather than one system per nozzle:
        /// four emitters on a worn item is four more things for the player's own body to carry
        /// around, and the shipped pattern for this is a single system playing with its emission
        /// disabled (see <c>GravelBlastFx</c>).
        /// </para>
        /// <para>
        /// Only the ACTIVE model smokes. Both forms are wired, and emitting from the hidden one
        /// would leave a second column of smoke standing wherever the pack is not.
        /// </para>
        /// <para>
        /// The debt is carried across frames so the rate is honest at any frame rate — dropping
        /// the fraction each frame would make smoke thinner the faster the machine runs.
        /// </para>
        /// </summary>
        private void Smoke(float dt)
        {
            if (smoke == null || dt <= 0f) return;

            // Two sources, added: the trail every lit motor leaves, and the extra a hot one makes.
            // Separate on purpose — the first says "this thing is running" and the second says
            // "this thing is about to cut out", and one rate cannot say both.
            float over = Mathf.InverseLerp(WarnFraction, 1f, Heat);
            float rate = Throttle * smokePerSecond + over * overheatSmokePerSecond;

            if (rate <= 0f)
            {
                smokeDebt = 0f;
                return;
            }

            // Emitted off the FLAME cones rather than off the nozzle discs, because a cone
            // already knows which way its motor blows: the builder aimed its +Y down the outflow.
            // The discs would have needed that direction worked out a second time, differently.
            var live = new List<Transform>();
            foreach (Pod pod in pods)
                foreach (Transform flame in pod.Flames)
                    if (flame != null && flame.gameObject.activeInHierarchy) live.Add(flame);

            if (live.Count == 0) return;

            smokeDebt += rate * live.Count * dt;

            int puffs = Mathf.FloorToInt(smokeDebt);
            if (puffs <= 0) return;
            smokeDebt -= puffs;

            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };

            for (int i = 0; i < puffs; i++)
            {
                Transform flame = live[i % live.Count];
                Vector3 outflow = flame.up;

                // Started past the end of the flame, not at the nozzle: a puff born inside the
                // fire is a grey smudge over the brightest thing on the machine.
                emit.position = flame.position + outflow * (flame.lossyScale.y * smokeStandoff)
                                + Random.insideUnitSphere * smokeScatter;

                // Thrown out the back with the exhaust, then left behind — the system simulates in
                // WORLD space, so a puff keeps the velocity it was born with while the player flies
                // out from under it. That is what makes it a trail rather than a cloud that follows.
                emit.velocity = outflow * (smokeSpeed * Mathf.Max(Throttle, 0.35f));

                smoke.Emit(emit, 1);
            }
        }
    }
}
