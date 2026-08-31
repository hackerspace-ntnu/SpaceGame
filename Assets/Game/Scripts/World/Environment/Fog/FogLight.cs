// A lamp the fog can see.
//
// Scattering a scene's lights through a volume is the single biggest thing separating fog that
// looks lit from fog that looks like coloured smoke — a lamp with a visible cone of light around it
// tells the player how far away it is, how thick the air is, and that the air is a place rather
// than a filter over the screen.
//
// It is opt-in, and that is the point. Reading every Light in the scene would put a fog cost on
// scenes that never asked for one and would silently get slower as an artist added lamps; a
// component on the handful of lights that are actually inside fog keeps the cost where the effect
// is. The renderer takes the nearest few and ignores the rest.
using UnityEngine;

namespace SpaceGame.World.Environment
{
    [AddComponentMenu("SpaceGame/Environment/Fog Light")]
    [RequireComponent(typeof(Light))]
    [ExecuteAlways]
    public class FogLight : MonoBehaviour
    {
        [Tooltip("Scales this light's contribution to the fog only, leaving what it does to " +
                 "surfaces alone. Below 1 for a lamp that is meant to light a room without filling " +
                 "it with glow; above 1 for one whose beam is the point.")]
        [Range(0f, 8f)] public float fogIntensity = 1f;

        private Light source;

        /// <summary>The light this drives. Cached because the renderer reads it every frame.</summary>
        public Light Source
        {
            get
            {
                if (source == null)
                    source = GetComponent<Light>();
                return source;
            }
        }

        /// <summary>
        /// Whether this light is worth uploading at all. A disabled or black light contributes
        /// nothing but still costs a slot, and there are only eight.
        /// </summary>
        public bool Contributes =>
            isActiveAndEnabled &&
            Source != null &&
            Source.enabled &&
            Source.range > 0.01f &&
            fogIntensity > 0f &&
            Source.intensity > 0f;

        /// <summary>Colour premultiplied by both intensities, in linear space — what the shader adds.</summary>
        public Color FogColor
        {
            get
            {
                Color linear = Source.color.linear;
                float scale = Source.intensity * fogIntensity;
                return new Color(linear.r * scale, linear.g * scale, linear.b * scale, 1f);
            }
        }

        private void OnEnable() => FogVolumes.Register(this);

        private void OnDisable() => FogVolumes.Unregister(this);
    }
}
