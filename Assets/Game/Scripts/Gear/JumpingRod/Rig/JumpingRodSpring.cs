using UnityEngine;

namespace SpaceGame.Gear.JumpingRod
{
    /// <summary>
    /// Squashes the rod's coil and slides its piston. Presentation only — it decides nothing, it is
    /// handed a compression and shows it.
    ///
    /// <para>
    /// Split from <c>JumpingRodItem</c> because the two answer to different machines. Only the
    /// holder's own machine bounces the holder, but every machine has to SEE the spring work — so
    /// the item hands this a compression on all of them, worked out from the one thing every
    /// machine already has: how much clearance the player has under their feet. Nothing about the
    /// squash travels over the wire, and nothing here decides anything.
    /// </para>
    /// <para>
    /// The model's own <c>TRAVEL</c> is 0.11 m — see
    /// <c>_Source~/models/gear/jumping_rod_BUILD.md</c>. Change it there and change
    /// <see cref="travel"/> here, or the coil passes through its own seat at full squash.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class JumpingRodSpring : MonoBehaviour
    {
        [Tooltip("The rod itself. Everything below is expressed relative to ITS up, so the rig " +
                 "works whatever rotation the FBX importer left on the nested model — which is the " +
                 "one number nobody should have to have measured correctly for the spring to move " +
                 "the right way.")]
        [SerializeField] private Transform rod;

        [Tooltip("The chromed inner shaft. Slides UP into the outer tube as the rod compresses. " +
                 "The foot and the spring seat are parented under it, so they travel with it.")]
        [SerializeField] private Transform piston;

        [Tooltip("The coil. Shortened along whichever of its own local axes points up the rod — " +
                 "the model authored its origin at the coil's TOP, the end bolted into the fixed " +
                 "collar, so it shortens downward from a fixed anchor rather than growing up " +
                 "through the shaft.")]
        [SerializeField] private Transform coil;

        [Tooltip("Piston stroke at full compression, metres. Must match TRAVEL in " +
                 "_Source~/models/gear/jumping_rod.py.")]
        [SerializeField, Min(0f)] private float travel = 0.11f;

        [Tooltip("How fast the shown compression chases the value it is given, 1/s. The clearance " +
                 "it is derived from jumps around at the top of a hop and on rough ground; without " +
                 "a little smoothing the coil twitches instead of springing.")]
        [SerializeField, Min(1f)] private float follow = 26f;

        private Vector3 pistonRest;
        private Vector3 slideAxis;
        private Vector3 coilRestScale;
        private int coilAxis;
        private float coilSolidFraction = 1f;
        private bool captured;

        /// <summary>Where the rig is being driven to, 0 (extended) to 1 (solid).</summary>
        private float target;

        /// <summary>What is currently on screen, after smoothing.</summary>
        private float shown;

        /// <summary>
        /// Work out, once, which way "up the rod" is in each driven part's own frame.
        ///
        /// Done by measurement rather than by assuming the model arrived on Unity's axes. A Blender
        /// export bakes the Z-up-to-Y-up conversion into the transform of every root object, so the
        /// nested model — and therefore every part under it — sits under a 90 degree rotation, and
        /// a rig that translated the piston along a literal <c>Vector3.up</c> would slide it out
        /// through the side of the tube. Measuring costs one dot product per axis at startup and
        /// cannot be got wrong by a re-export.
        ///
        /// Deferred to the first use rather than done in Awake, so a builder script that adds this
        /// component and positions the parts in the same pass cannot have its ordering matter.
        /// </summary>
        private void Capture()
        {
            if (captured) return;

            Vector3 up = rod != null ? rod.up : Vector3.up;

            if (piston != null)
            {
                pistonRest = piston.localPosition;
                slideAxis = piston.parent != null
                    ? piston.parent.InverseTransformDirection(up).normalized
                    : up;
            }

            if (coil != null)
            {
                coilRestScale = coil.localScale;
                coilAxis = MostAlignedLocalAxis(coil, up);
                coilSolidFraction = SolidFraction(FreeLength(coil, up), travel);
            }

            captured = true;
        }

        /// <summary>
        /// How short the coil must get for its lower end to stay put while its upper end travels
        /// down by <paramref name="travel"/>.
        ///
        /// Derived rather than authored. A hand-tuned "squash to 55%" is a number that is right for
        /// exactly one coil length, and it goes quietly wrong the moment the model is
        /// re-proportioned — the coil's lower end then lifts off its own seat, or drives through it.
        /// Floored, because a stroke as long as the coil would collapse it to nothing.
        /// </summary>
        public static float SolidFraction(float freeLength, float travel)
            => freeLength <= 1e-4f ? 1f : Mathf.Clamp((freeLength - travel) / freeLength, 0.1f, 1f);

        /// <summary>The coil's own extent along <paramref name="world"/>, in metres.</summary>
        private static float FreeLength(Transform coil, Vector3 world)
        {
            if (!coil.TryGetComponent(out Renderer renderer)) return 0f;

            Bounds b = renderer.bounds;
            return Mathf.Abs(Vector3.Dot(b.size, new Vector3(Mathf.Abs(world.x), Mathf.Abs(world.y),
                                                             Mathf.Abs(world.z))));
        }

        /// <summary>
        /// Which of <paramref name="t"/>'s own x/y/z points most nearly along <paramref name="world"/>.
        /// A non-uniform scale is only sound along a cardinal local axis, so the coil is squashed
        /// along whichever one that is rather than along a guessed Y.
        /// </summary>
        public static int MostAlignedLocalAxis(Transform t, Vector3 world)
        {
            float x = Mathf.Abs(Vector3.Dot(t.right, world));
            float y = Mathf.Abs(Vector3.Dot(t.up, world));
            float z = Mathf.Abs(Vector3.Dot(t.forward, world));

            if (x >= y && x >= z) return 0;
            return y >= z ? 1 : 2;
        }

        private void OnEnable()
        {
            Capture();
            shown = target;
            Apply(shown);
        }

        /// <summary>Drive the rig. Safe to call from anywhere and on any machine.</summary>
        public void SetCompression(float compression) => target = Mathf.Clamp01(compression);

        private void LateUpdate()
        {
            Capture();

            shown = Mathf.MoveTowards(shown, target, follow * Time.deltaTime);
            Apply(shown);
        }

        private void Apply(float compression)
        {
            if (piston != null)
                piston.localPosition = pistonRest + slideAxis * (travel * compression);

            if (coil != null)
            {
                Vector3 scale = coilRestScale;
                scale[coilAxis] = coilRestScale[coilAxis] *
                                  Mathf.Lerp(1f, coilSolidFraction, compression);
                coil.localScale = scale;
            }
        }

    }
}
