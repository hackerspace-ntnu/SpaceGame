// What the sky's weather is doing, for whoever is rendering it.
//
// One per scene. It holds the numbers and pushes them; it does not render, and the renderer holds no
// numbers — so a scene with no CloudLayer has a clear sky at no cost, and changing the weather is
// editing a component in the scene rather than a render feature buried in a pipeline asset that
// every scene shares.
//
// It is the same split the sandstorm uses between SandstormVisuals and SandstormRenderFeature, for
// the same reason: a look that belongs to a place should live in that place.
using SpaceGame.World.Weather;
using UnityEngine;

namespace SpaceGame.World
{
    [AddComponentMenu("SpaceGame/Environment/Cloud Layer")]
    [ExecuteAlways]
    public class CloudLayer : MonoBehaviour
    {
        [Header("Layer")]
        [Tooltip("Metres above the camera where the cloud base sits. Low values put the player " +
                 "under an oppressive ceiling; 1200 or so reads as an ordinary fair-weather day.")]
        [Min(50f)] public float baseAltitude = 1200f;

        [Tooltip("Metres above the camera where the layer ends. The difference between this and the " +
                 "base is how tall the clouds can build — a thin layer can only ever make scraps.")]
        [Min(100f)] public float topAltitude = 2600f;

        [Tooltip("Radius of the sphere the layer wraps around, in metres. This is the dial for how " +
                 "curved the sky is, not a real planet size: smaller values bend the layer down to " +
                 "the horizon sooner and make the world feel smaller. Below about 20 km the " +
                 "curvature becomes obvious enough to read as a dome.")]
        [Min(1000f)] public float horizonRadius = 60000f;

        [Tooltip("How much of the sky has cloud in it. A threshold on the weather map rather than a " +
                 "fade, so raising it grows the clouds that exist and lets new ones appear instead " +
                 "of making the whole sky uniformly murky.")]
        [Range(0f, 1f)] public float coverage = 0.45f;

        [Tooltip("Metres per tile of the weather map — the size of a whole weather system, so this " +
                 "wants to be kilometres. Too small and the sky reads as a repeating pattern.")]
        [Min(500f)] public float weatherScale = 9000f;

        [Header("Look")]
        [Tooltip("The cloud's own colour. Near-white for daylight cumulus; tint it for an alien sky.")]
        [ColorUsage(false, false)] public Color color = new Color(0.95f, 0.94f, 0.92f);

        [Tooltip("How much of the sky's light reaches a cloud with no direct sun. This is what " +
                 "lights the shadowed undersides, and at zero they go black.")]
        [Range(0f, 3f)] public float ambient = 0.9f;

        [Tooltip("How fast light is absorbed per metre at full density. Clouds are hundreds of " +
                 "metres thick, so this is a much smaller number than a fog volume's.")]
        [Range(0.0005f, 0.05f)] public float extinction = 0.006f;

        [Tooltip("Forward scattering. High values make the clouds blaze around the sun.")]
        [Range(0f, 0.95f)] public float forwardScatter = 0.6f;

        [Tooltip("How strongly the edge of a cloud lights up when the sun is behind it. This is the " +
                 "silver lining, and it is most of what makes a backlit cloud look like a cloud.")]
        [Range(0f, 1f)] public float silverLining = 0.35f;

        [Tooltip("Darkening of dense edges seen from the lit side. Low values wash the sunlit face " +
                 "out into a flat card.")]
        [Range(1f, 20f)] public float powder = 8f;

        [Header("Shape")]
        [Tooltip("Metres per tile of the billow noise — the size of the individual puffs.")]
        [Min(50f)] public float billowScale = 900f;

        [Tooltip("How hard the fine detail tears the billows apart. Applied harder at the top than " +
                 "at the base, so the crown wisps while the bottom stays solid.")]
        [Range(0f, 1f)] public float erosion = 0.4f;

        [Tooltip("Frequency multiplier for the eroding octave relative to the base.")]
        [Range(1.5f, 12f)] public float detailScale = 5f;

        [Tooltip("Multiplies the density the noise produces.")]
        [Range(0.1f, 4f)] public float density = 1f;

        [Header("Motion")]
        [Tooltip("Which way the weather travels. The Y component tips the drift and is usually left " +
                 "near zero.")]
        public Vector3 wind = new Vector3(1f, 0f, 0.35f);

        [Tooltip("Metres per second the whole sky drifts. Clouds are far away, so this needs to be " +
                 "much larger than a fog volume's to read as moving at all — 20 m/s is a brisk " +
                 "afternoon.")]
        [Range(0f, 120f)] public float windSpeed = 18f;

        [Tooltip("How far the skybox's own painted 2D cloud layer stands down for these. At 1 the " +
                 "sky's flat dust bands are gone entirely and every cloud you see is marched. " +
                 "Below 1 keeps some of the painted layer, which is a cheap way to add haze near " +
                 "the horizon where the march is thinnest.")]
        [Range(0f, 1f)] public float skyboxDustFade = 1f;

        /// <summary>
        /// The layer the renderer should draw, or null for a clear sky.
        ///
        /// <para>
        /// Last enabled wins. Two cloud layers in one scene is an authoring mistake rather than a
        /// feature — there is one sky — and silently picking one is friendlier than drawing both on
        /// top of each other, which looks like a bug in the shader rather than a bug in the scene.
        /// </para>
        /// </summary>
        public static CloudLayer Active { get; private set; }

        private static readonly Vector3[] SkyDirections = { Vector3.up };
        private static readonly Color[] SkySamples = new Color[1];

        private static readonly int AltitudeId = Shader.PropertyToID("_CloudAltitude");
        private static readonly int ColorId = Shader.PropertyToID("_CloudColor");
        private static readonly int ShapeId = Shader.PropertyToID("_CloudShape");
        private static readonly int MotionId = Shader.PropertyToID("_CloudMotion");
        private static readonly int LightingId = Shader.PropertyToID("_CloudLighting");
        private static readonly int SkyLightId = Shader.PropertyToID("_CloudSkyLight");
        private static readonly int SkyboxFadeId = Shader.PropertyToID("_VolumetricCloudFade");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = null;
            Shader.SetGlobalFloat(SkyboxFadeId, 0f);
        }

        private void OnEnable() => Active = this;

        private void OnDisable()
        {
            if (Active != this)
                return;

            Active = null;

            // Hand the sky back. Without this the skybox keeps its painted dust suppressed after
            // the only thing that was replacing it has gone, and the result is a bare gradient
            // that looks like the sky failed to load.
            Shader.SetGlobalFloat(SkyboxFadeId, 0f);
        }

        private void LateUpdate() => Push();

        /// <summary>
        /// Uploads the layer.
        ///
        /// <para>
        /// Public because the render feature calls it too: in the editor a scene view can render
        /// with no play mode running, so LateUpdate is not guaranteed to have happened before the
        /// first frame that needs these values.
        /// </para>
        /// </summary>
        public void Push()
        {
            // The shared weather clock, not Time.time — the same reason the fog volumes use it. Two
            // players in one session must be under the same sky, and neither sends anything to make
            // that true.
            float clock = (float)Sandstorms.Now;

            float top = Mathf.Max(baseAltitude + 50f, topAltitude);

            Shader.SetGlobalVector(AltitudeId, new Vector4(baseAltitude, top, horizonRadius, coverage));

            Color linear = color.linear;
            Shader.SetGlobalVector(ColorId, new Vector4(linear.r, linear.g, linear.b, ambient));

            Shader.SetGlobalVector(ShapeId, new Vector4(billowScale, erosion, detailScale, density));

            Vector3 direction = wind.sqrMagnitude > 1e-6f ? wind.normalized : Vector3.right;
            Vector3 drift = direction * (windSpeed * clock);
            Shader.SetGlobalVector(MotionId, new Vector4(drift.x, drift.y, drift.z, forwardScatter));

            Shader.SetGlobalVector(LightingId, new Vector4(extinction, powder, silverLining, weatherScale));

            // Scaled by coverage as well as by the dial: a nearly clear sky has no marched clouds
            // to replace the painted ones with, so suppressing them would just empty the sky.
            Shader.SetGlobalFloat(SkyboxFadeId, skyboxDustFade * Mathf.Clamp01(coverage * 3f));

            PushSkyLight();
        }

        /// <summary>
        /// The sky's own radiance. A fullscreen blit has no per-object SH constants, so the shader
        /// cannot read this for itself — see the same note in <c>FogVolumes</c>.
        /// </summary>
        private static void PushSkyLight()
        {
            RenderSettings.ambientProbe.Evaluate(SkyDirections, SkySamples);

            Color sky = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? SkySamples[0]
                : SkySamples[0].gamma;

            Shader.SetGlobalVector(SkyLightId, new Vector4(Mathf.Max(0f, sky.r),
                                                           Mathf.Max(0f, sky.g),
                                                           Mathf.Max(0f, sky.b), 0f));
        }

        private void OnValidate()
        {
            if (topAltitude < baseAltitude + 50f)
                topAltitude = baseAltitude + 50f;
        }
    }
}

// Multiplayer: nothing to replicate. The layer is authored in a scene and its only moving part is
// derived from the shared weather clock, exactly as the fog volumes and the sun already are.
//
// Persistence: nothing to save. No field changes while the game runs.
