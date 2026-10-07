using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// A light that burns while its flame does: on, and flickering, while <see cref="flame"/> is emitting; switched off (never
    /// dimmed to zero, see <c>CabinAlert</c>) when it is not. Pure presentation, on every machine, from the particle system the
    /// sprayer's nozzle already drives — nothing is sent and nothing is decided here.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class FlameFlicker : MonoBehaviour
    {
        [Tooltip("The flame whose emission this light follows.")]
        [SerializeField] private ParticleSystem flame;

        [Tooltip("Intensity at the middle of the flicker.")]
        [SerializeField, Min(0f)] private float intensity = 2.5f;

        [Tooltip("How far the flicker swings either side of it, as a fraction of it.")]
        [SerializeField, Range(0f, 1f)] private float flicker = 0.35f;

        [Tooltip("How fast it flickers.")]
        [SerializeField, Min(0f)] private float flickerRate = 18f;

        private Light glow;
        private float seed;

        private void Awake()
        {
            glow = GetComponent<Light>();
            seed = Random.value * 100f;
            glow.enabled = false;
        }

        private void Update()
        {
            bool burning = flame != null && flame.isEmitting;
            if (glow.enabled != burning) glow.enabled = burning;
            if (!burning) return;

            float noise = Mathf.PerlinNoise(seed, Time.time * flickerRate) * 2f - 1f;
            glow.intensity = intensity * (1f + flicker * noise);
        }
    }
}
