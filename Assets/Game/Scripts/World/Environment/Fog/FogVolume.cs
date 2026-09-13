// One body of fog, placed by hand.
//
// A FogVolume holds no runtime state at all. Where it is, how big it is and which way it faces are
// the transform's business; everything else is an authored number that never changes while the game
// runs. Its motion is a pure function of the shared weather clock, so what it saves is nothing and
// what it sends over the network is nothing — see the notes on both at the bottom of this file.
//
// The component's whole job is to describe itself to the renderer. It does not render, it does not
// look for a camera, and it does not know how many other volumes exist.
using SpaceGame.World.Weather;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    [AddComponentMenu("SpaceGame/Environment/Fog Volume")]
    [ExecuteAlways]
    public class FogVolume : MonoBehaviour
    {
        [Header("Shape")]
        [Tooltip("Which body the fog fills. All four are evaluated in this object's own space, so " +
                 "the transform's rotation and scale apply to every one of them.")]
        public FogShapeKind shape = FogShapeKind.Ellipsoid;

        [Tooltip("Half-extents in metres before the transform's own scale. A 20 x 6 x 20 box is a " +
                 "40 x 12 x 40 metre room.")]
        public Vector3 size = new Vector3(15f, 6f, 15f);

        [Tooltip("How much of the shape is edge rather than body, as a fraction of its radius. " +
                 "Small values give a fog bank with a discernible surface; large ones give " +
                 "something that is mostly haze and has no boundary you can point at.")]
        [Range(0.02f, 1f)] public float edgeFeather = 0.45f;

        [Tooltip("How much thinner the fog is at the top than at the floor. Anything suspended in " +
                 "air settles, so a volume with none of this reads as a solid object rather than " +
                 "as air. Ground layers ignore the shape's top face entirely and use this as the " +
                 "rate they fade out at.")]
        [Range(0f, 1f)] public float verticalFalloff = 0.35f;

        [Header("Look")]
        [Tooltip("The colour of the fog itself — what the light that scatters out of it is tinted " +
                 "by. This is a surface colour, not a light: a volume with this set and no light " +
                 "on it is black.")]
        [ColorUsage(false, false)] public Color color = new Color(0.72f, 0.78f, 0.85f);

        [Tooltip("Light the fog gives off on its own, before anything shines on it. This is what " +
                 "makes a fog that glows rather than one that is merely lit — spore clouds, " +
                 "coolant vapour, anything the player should read as active.")]
        [ColorUsage(false, true)] public Color emission = Color.black;

        [Tooltip("How much of the sky's light reaches the fog with no direct sun. Deep inside a " +
                 "thick volume this is most of the light there is, so a low value here is a " +
                 "volume whose interior is nearly black.")]
        [Range(0f, 2f)] public float ambient = 0.6f;

        [Tooltip("Multiplies the density the noise produces. Below 1 the volume is wispy and you " +
                 "can see the shapes inside it; above 1 it closes up into a solid mass.")]
        [Range(0.05f, 4f)] public float density = 1f;

        [Tooltip("Thin this volume out for a viewer standing under cover — inside a ship, a cave " +
                 "or anything else with a SandstormShelter. Right for weather: outdoor air does " +
                 "not follow you indoors. Turn it OFF for an authored interior atmosphere, which " +
                 "is exactly the fog that should still be there when you step inside.")]
        public bool thinsUnderShelter = true;

        [Tooltip("How fast light is absorbed, per metre at full density. This is the dial that " +
                 "decides how far you can see: at 0.1 you lose the view at roughly 30 metres.")]
        [Range(0.005f, 1f)] public float extinction = 0.08f;

        [Tooltip("Forward scattering. At 0 the fog looks the same in every direction; toward 1 it " +
                 "blazes when you look through it at a light and goes dull when the light is " +
                 "behind you. Most of what makes fog look like fog rather than like tinted glass.")]
        [Range(0f, 0.95f)] public float forwardScatter = 0.55f;

        [Header("Detail")]
        [Tooltip("Metres per tile of the billow noise. This is the size of the lumps: 8 m is " +
                 "churning steam, 60 m is a slow bank rolling through a valley.")]
        [Min(1f)] public float noiseScale = 24f;

        [Tooltip("How hard the fine detail carves the billows apart. High values shred the volume " +
                 "into torn wisps; zero leaves smooth blobs.")]
        [Range(0f, 1f)] public float erosion = 0.35f;

        [Tooltip("Squashes the noise vertically, which stretches the billows horizontally — what " +
                 "moving air does to anything suspended in it. At 1 the fog reads as static smoke.")]
        [Range(1f, 6f)] public float verticalSquash = 2.2f;

        [Tooltip("Frequency multiplier for the eroding octave, relative to the base. Below about " +
                 "3 the two octaves correlate and merely deepen the same lumps.")]
        [Range(1.5f, 12f)] public float detailScale = 4.1f;

        [Header("Motion")]
        [Tooltip("Which way the fog drifts. Normalised, so only the direction matters.")]
        public Vector3 wind = new Vector3(1f, -0.1f, 0.3f);

        [Tooltip("Metres per second the noise scrolls along the wind. Drift alone slides the whole " +
                 "mass past like a texture on a conveyor, which is why it is paired with the churn " +
                 "below rather than used on its own.")]
        [Range(0f, 20f)] public float windSpeed = 1.5f;

        [Tooltip("How far the fog stirs in place, in metres. This is what makes a volume look " +
                 "lived in rather than scrolled: real air turns over as well as travelling, and " +
                 "with this at zero a static camera sees fog sliding in one direction forever.")]
        [Range(0f, 30f)] public float churn = 6f;

        [Tooltip("Metres per turnover of the churn. Roughly the size of the eddies.")]
        [Min(1f)] public float churnScale = 40f;

        [Tooltip("How fast the churn turns over.")]
        [Range(0f, 2f)] public float churnSpeed = 0.08f;

        /// <summary>
        /// Radius of a world-space sphere containing the whole volume, feather included. Used to
        /// decide which volumes are worth uploading, so it must never under-estimate.
        /// </summary>
        public float BoundingRadius =>
            // The 0.15 matches the margin the shader's bounds test adds past the feather, so the
            // CPU never culls a volume whose billows the GPU would still have drawn.
            WorldSize.magnitude * (1f + edgeFeather + 0.15f);

        /// <summary>Half-extents in world metres, after the transform's own scale.</summary>
        public Vector3 WorldSize
        {
            get
            {
                Vector3 scale = transform.lossyScale;
                return new Vector3(Mathf.Max(0.01f, size.x * Mathf.Abs(scale.x)),
                                   Mathf.Max(0.01f, size.y * Mathf.Abs(scale.y)),
                                   Mathf.Max(0.01f, size.z * Mathf.Abs(scale.z)));
            }
        }

        /// <summary>
        /// The matrix the shader uses to put a world position into this volume's unit space.
        ///
        /// <para>
        /// Built from the transform's position and rotation with the half-extents folded into the
        /// scale, rather than from <c>worldToLocalMatrix</c>: the object's own scale is already in
        /// <see cref="WorldSize"/>, and using both would square it.
        /// </para>
        /// </summary>
        public Matrix4x4 WorldToVolume =>
            Matrix4x4.TRS(transform.position, transform.rotation, WorldSize).inverse;

        /// <summary>
        /// The density to upload for a viewer with this much cover over them, 0 (open) to 1
        /// (sealed). Weather volumes fade out indoors; an authored interior atmosphere
        /// (<see cref="thinsUnderShelter"/> off) is unaffected.
        /// </summary>
        public float DensityFor(float shelter) =>
            thinsUnderShelter ? density * Mathf.Clamp01(1f - shelter) : density;

        private void OnEnable() => FogVolumes.Register(this);

        private void OnDisable() => FogVolumes.Unregister(this);

        /// <summary>
        /// How far the noise has scrolled by now, in metres.
        ///
        /// <para>
        /// Derived from <see cref="Sandstorms.Now"/> — the clock every machine in a session agrees
        /// on — and not from <c>Time.time</c>, which counts from process start and so would have
        /// two players looking at visibly different fog in the same place. Nothing is sent to make
        /// that true; both machines evaluate the same function against the same number.
        /// </para>
        /// </summary>
        public Vector3 DriftAt(double clock)
        {
            Vector3 direction = wind.sqrMagnitude > 1e-6f ? wind.normalized : Vector3.right;
            return direction * (windSpeed * (float)clock);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, WorldSize);
            Gizmos.color = new Color(color.r, color.g, color.b, 0.85f);

            switch (shape)
            {
                case FogShapeKind.Ellipsoid:
                    Gizmos.DrawWireSphere(Vector3.zero, 1f);
                    break;

                case FogShapeKind.Cylinder:
                    DrawWireCylinder();
                    break;

                default:
                    Gizmos.DrawWireCube(Vector3.zero, Vector3.one * 2f);
                    break;
            }

            // A ground layer's box is a lie — the fog fades out inside it and is not clipped by the
            // top face — so the gizmo says where the fog actually thins out instead.
            if (shape == FogShapeKind.GroundLayer)
            {
                Gizmos.color = new Color(color.r, color.g, color.b, 0.35f);
                float top = Mathf.Lerp(1f, -0.4f, verticalFalloff);
                Gizmos.DrawWireCube(new Vector3(0f, (top - 1f) * 0.5f, 0f),
                                    new Vector3(2f, top + 1f, 2f));
            }
        }

        private static void DrawWireCylinder()
        {
            const int Segments = 24;
            Vector3 previousTop = Vector3.zero;
            Vector3 previousBottom = Vector3.zero;

            for (int i = 0; i <= Segments; i++)
            {
                float angle = i / (float)Segments * Mathf.PI * 2f;
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 top = offset + Vector3.up;
                Vector3 bottom = offset - Vector3.up;

                if (i > 0)
                {
                    Gizmos.DrawLine(previousTop, top);
                    Gizmos.DrawLine(previousBottom, bottom);
                }

                if (i % 6 == 0)
                    Gizmos.DrawLine(top, bottom);

                previousTop = top;
                previousBottom = bottom;
            }
        }
#endif
    }
}

// Multiplayer: nothing to replicate. A volume is authored in a scene, so every machine already has
// it, and its only moving part is derived from the shared weather clock. There is no ownership
// question because nothing is ever written, and no prefab to register because nothing is spawned.
//
// Persistence: nothing to save. The component holds no state that changes while the game runs, so a
// world reloaded from disk rebuilds identical fog from the scene and the clock anchor the save
// system already restores for the weather.
