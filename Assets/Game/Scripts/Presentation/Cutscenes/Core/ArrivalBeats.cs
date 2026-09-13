using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// When each beat of the arrival happens, measured from the instant the formation launched.
    ///
    /// <para>
    /// Pure arithmetic with no frame and no coroutine in it, separated from
    /// <see cref="ArrivalCutscene"/> for the reason <see cref="ShakeMath"/> is separated from the
    /// rigs that use it: the thing worth being sure about here is a set of instants, and instants
    /// can be asserted without a play mode, an Animator or a ship.
    /// </para>
    ///
    /// <para>
    /// <b>The contract is one line: the screen is fully black at first contact.</b> Not shortly
    /// after it, not once the wreck has stopped moving — at the frame the hull touches the ground,
    /// which is <see cref="Contact"/> and the end of the descent. Everything after that (the hold
    /// at the impact attitude, the topple onto the belly, the grounding) plays out behind the
    /// black. That ordering is why the fade has to be STARTED early: a fade begun at contact ends
    /// somewhere in the middle of the crash, which is the beat it exists to hide.
    /// </para>
    /// </summary>
    public readonly struct ArrivalBeats
    {
        /// <summary>How long the dive takes, from launch to first contact.</summary>
        public readonly float Descent;

        /// <summary>How long the fade to black takes — and therefore how early it must begin.</summary>
        public readonly float ImpactFade;

        /// <summary>
        /// How long the hull keeps moving after contact: the hold at the impact attitude plus the
        /// topple onto its belly. Told to the cutscene by the director, because it belongs to the
        /// hull and not to the screen.
        /// </summary>
        public readonly float Settle;

        /// <summary>How long the black holds after the wreck has finally stopped moving.</summary>
        public readonly float Blackout;

        public ArrivalBeats(float descent, float impactFade, float settle, float blackout)
        {
            // A zero-length descent is a divide by zero in the shake curve; the rest merely have to
            // be non-negative. Clamped here rather than at each use, so every reader gets the same
            // sanitised numbers.
            Descent = Mathf.Max(MinimumDescent, descent);
            ImpactFade = Mathf.Max(0f, impactFade);
            Settle = Mathf.Max(0f, settle);
            Blackout = Mathf.Max(0f, blackout);
        }

        /// <summary>The shortest descent that is still a descent, in seconds.</summary>
        private const float MinimumDescent = 0.1f;

        /// <summary>The instant the hull first touches the ground, and the instant the screen is black.</summary>
        public float Contact => Descent;

        /// <summary>
        /// When the fade to black has to start. Never negative: a fade lead-in longer than the whole
        /// descent starts at the top of the arc and takes the whole way down, which is a strange
        /// thing to author but not a broken one.
        /// </summary>
        public float FadeStart => Mathf.Max(0f, Descent - ImpactFade);

        /// <summary>
        /// How long the fade actually gets, which is the lead-in or the whole descent, whichever is
        /// shorter. <see cref="FadeStart"/> plus this is <see cref="Contact"/>, always — that
        /// equality IS the requirement.
        /// </summary>
        public float FadeDuration => Contact - FadeStart;

        /// <summary>
        /// How long to stay on black after contact: the rest of the crash, and then the beat the
        /// player spends out cold before coming round in the wreck.
        /// </summary>
        public float BlackHold => Settle + Blackout;

        /// <summary>
        /// Where in the descent a machine that is <paramref name="elapsed"/> seconds late should
        /// pick the shake curve up. Clamped, so a late joiner seated after the landing gets the end
        /// of the curve rather than an extrapolation off the end of it.
        /// </summary>
        public float DescentProgress(float elapsed) => Mathf.Clamp01(elapsed / Descent);
    }
}
