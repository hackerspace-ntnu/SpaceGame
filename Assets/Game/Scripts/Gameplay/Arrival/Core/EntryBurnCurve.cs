using System;
using UnityEngine;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// How hard the hull is burning at each point of its descent, and how the fire flickers.
    ///
    /// <para>
    /// Pure arithmetic with no frame, no material and no light in it, separated from
    /// <see cref="EntryBurn"/> for the reason <c>ArrivalBeats</c> and <c>ShakeMath</c> are separated
    /// from the things that use them: the part worth being sure about is the SHAPE of the envelope,
    /// and a shape can be asserted without a play mode, a shader or a ship.
    /// </para>
    ///
    /// <para>
    /// <b>The burn is over before the ground rush, on purpose.</b> Peak heating happens high up,
    /// where the air first gets thick enough to matter, and it is gone by the time the hull is low
    /// and slow. That is also what the moment needs: the last third of the descent belongs to the
    /// ground coming up and to the shake that carries the impact, and a window still blown out to
    /// white orange through it hides the one thing the descent is building toward. So
    /// <see cref="Extinguish"/> lands well before <c>ArrivalBeats.FadeStart</c> — the fire and the
    /// crash never compete for the same seconds.
    /// </para>
    /// </summary>
    [Serializable]
    public struct EntryBurnCurve
    {
        [Tooltip("Fraction of the descent at which the sheath first lights. Not zero: the hull is " +
                 "still above the air that heats it, and igniting on the launch frame reads as an " +
                 "effect being switched on rather than as entering something.")]
        [Range(0f, 1f)] public float Ignite;

        [Tooltip("Fraction of the descent by which the burn is at full strength.")]
        [Range(0f, 1f)] public float Full;

        [Tooltip("Fraction of the descent at which it starts dying back.")]
        [Range(0f, 1f)] public float Fade;

        [Tooltip("Fraction of the descent by which the burn is out. Must clear the fade to black " +
                 "at the end — see the note on this type.")]
        [Range(0f, 1f)] public float Extinguish;

        /// <summary>
        /// The tuning the arrival actually flies: alight almost at once, full by a sixth of the way
        /// down, holding through the middle of the arc and out by seven tenths — which over a 26 s
        /// descent leaves the last eight seconds clear for the ground rush.
        /// </summary>
        public static EntryBurnCurve Default => new()
        {
            Ignite = 0.03f,
            Full = 0.16f,
            Fade = 0.42f,
            Extinguish = 0.70f,
        };

        /// <summary>
        /// How hard the sheath is burning at <paramref name="progress"/> through the descent, 0..1.
        ///
        /// <para>
        /// Clamped at both ends rather than extrapolated: a machine asking about a descent that has
        /// not started gets nothing, and a late joiner seated after the burn was over gets nothing
        /// either, instead of a curve running off its own end into a negative or a second ignition.
        /// </para>
        ///
        /// <para>
        /// Tolerant of a badly authored curve on purpose. The four points are Inspector fields and
        /// nothing stops somebody dragging <see cref="Fade"/> in front of <see cref="Full"/>; they
        /// are sorted here so the worst that can do is make the burn shorter, never make it invert
        /// and light the cabin up during the crash.
        /// </para>
        /// </summary>
        public float Intensity(float progress)
        {
            float ignite = Mathf.Clamp01(Ignite);
            float full = Mathf.Max(ignite, Mathf.Clamp01(Full));
            float fade = Mathf.Max(full, Mathf.Clamp01(Fade));
            float out_ = Mathf.Max(fade, Mathf.Clamp01(Extinguish));

            if (progress <= ignite || progress >= out_) return 0f;
            if (progress >= full && progress <= fade) return 1f;

            // Smoothstep answers 0 for a zero-width edge, which would put a hard on/off step
            // exactly where an author collapsed two points together. Held at full instead: a
            // burn told to reach full strength instantly should do that, not vanish.
            if (progress < full)
                return Mathf.Approximately(full, ignite) ? 1f : Mathf.SmoothStep(0f, 1f, (progress - ignite) / (full - ignite));

            return Mathf.Approximately(out_, fade) ? 1f : Mathf.SmoothStep(1f, 0f, (progress - fade) / (out_ - fade));
        }

        /// <summary>
        /// The whole-sheath luminance wobble at <paramref name="time"/> seconds, around 1.
        ///
        /// <para>
        /// <b>One flicker, shared.</b> The same number drives the plasma shader's <c>_Flicker</c>
        /// and the cabin glow lamp's intensity, so the light inside the cabin pulses with the fire
        /// outside it. Sampled on the GPU and on the CPU separately they would be two noises on two
        /// clocks, and a room whose light does not agree with what is out of the window reads as
        /// two unrelated faults rather than one event.
        /// </para>
        ///
        /// <para>
        /// Two frequencies beating against each other rather than one: a single sine is a pulse, and
        /// a pulse reads as machinery. Bounded to <paramref name="depth"/> either side of 1 so it
        /// stays a shimmer and can never strobe — the cap is deliberate, and is the accessibility
        /// half of this (GDC-L1-UX-0006): a crew member sits inside this light for eighteen seconds
        /// with no way to look away from it.
        /// </para>
        /// </summary>
        public static float Flicker(float time, float hz, float depth)
        {
            float a = Mathf.Sin(time * hz * Mathf.PI * 2f);
            float b = Mathf.Sin(time * hz * 2.7f * Mathf.PI * 2f + 1.3f);

            return 1f + Mathf.Clamp01(depth) * (a * 0.6f + b * 0.4f);
        }
    }
}
