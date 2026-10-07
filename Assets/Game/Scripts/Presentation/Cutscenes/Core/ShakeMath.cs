using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// How far to push a camera to sell a shake.
    ///
    /// <para>
    /// Pure, so the two properties that matter can be tested without a scene: the displacement is
    /// CAPPED, and a player who has turned shake off gets exactly zero rather than nearly zero.
    /// Both are easy to get subtly wrong and impossible to notice by eye — an uncapped shake looks
    /// fine until two sources overlap, and a "nearly off" shake still makes a susceptible player
    /// ill.
    /// </para>
    ///
    /// <para>
    /// Perlin rather than Random deliberately. Random gives an independent value per sample and
    /// reads as the camera glitching; Perlin is continuous, so the camera moves like a thing with
    /// mass.
    /// </para>
    /// </summary>
    public static class ShakeMath
    {
        // Arbitrary but fixed sampling lanes through the noise field. Three different offsets so
        // the axes do not move in lockstep, which would turn a rattle into a diagonal slide.
        private const float LaneX = 0f;
        private const float LaneY = 137f;
        private const float LaneZ = 311f;

        /// <summary>
        /// The offset to add to the camera's local position.
        ///
        /// <para>
        /// <paramref name="intensity"/> is the caller's own curve through the event — the arrival
        /// ramps it up and spikes it at impact. <paramref name="settingsScale"/> is the player's
        /// preference and is applied last, so nothing a caller does can shake a camera belonging to
        /// somebody who asked it not to.
        /// </para>
        /// </summary>
        public static Vector3 Displacement(float intensity, float settingsScale,
                                           float maxTranslation, float time, float frequency)
        {
            float scale = Mathf.Clamp01(intensity) * Mathf.Clamp01(settingsScale);

            // Not an optimisation. Perlin noise is 0.5 at its sample origin rather than 0, so the
            // arithmetic below yields a small CONSTANT offset at zero intensity — a camera sitting
            // permanently off-centre for the player who turned shake off to avoid exactly that.
            if (scale <= 0f) return Vector3.zero;

            float t = time * frequency;

            // Perlin returns roughly 0..1 with a mean of 0.5, so it is recentred to roughly -1..1.
            float x = Mathf.PerlinNoise(t, LaneX) * 2f - 1f;
            float y = Mathf.PerlinNoise(t, LaneY) * 2f - 1f;
            float z = Mathf.PerlinNoise(t, LaneZ) * 2f - 1f;

            float cap = maxTranslation * scale;

            // Clamped rather than normalised. The three axes are independent, so their combined
            // magnitude can reach root three even though each is in range — that is what the cap is
            // for. Normalising would instead pin every sample to the cap, turning a rattle into a
            // constant-radius orbit.
            return Vector3.ClampMagnitude(new Vector3(x, y, z) * cap, cap);
        }
    }
}
