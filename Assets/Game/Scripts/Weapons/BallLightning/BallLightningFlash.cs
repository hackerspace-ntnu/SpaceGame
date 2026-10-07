using UnityEngine;

namespace SpaceGame.Weapons
{
    /// <summary>
    /// A light that falls to nothing and then removes itself.
    ///
    /// <para>
    /// The discharge burst has to outlive the orb that cast it — the projectile is destroyed as
    /// soon as its arc ends — so the light cannot be parented to it and cannot be faded by it. It
    /// is left in the world owning its own death instead. A plain <c>Destroy(obj, t)</c> would pop
    /// the light out at full brightness, which reads as the light being switched off rather than as
    /// a flash dying away.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class BallLightningFlash : MonoBehaviour
    {
        private Light flash;
        private float peakIntensity;
        private float duration;
        private float startTime;

        /// <summary>Start the fade. Intensity falls from <paramref name="peak"/> to zero across <paramref name="seconds"/>.</summary>
        public void Begin(float peak, float seconds)
        {
            flash = GetComponent<Light>();
            peakIntensity = peak;
            duration = Mathf.Max(0.01f, seconds);
            startTime = Time.time;
        }

        private void Update()
        {
            if (flash == null)
            {
                Destroy(gameObject);
                return;
            }

            float t = (Time.time - startTime) / duration;

            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            // Squared, so the burst dumps most of its brightness immediately and trails off, the
            // way a discharge does. Linear reads as a dimmer being turned down.
            float fade = 1f - t;
            flash.intensity = peakIntensity * fade * fade;
        }
    }
}
