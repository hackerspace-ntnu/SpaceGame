using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// What a taut rope costs: how much line it gives up, and when it lets go.
    ///
    /// <para>
    /// Before this the catch had no way to end except the thrower pressing the button, and the rope
    /// only ever got shorter — <c>reelInForce</c> subtracted from the length and nothing anywhere
    /// added to it. So an animal that had been reeled in once could never run again, and one that
    /// had spent its six seconds of struggle went limp and stayed limp for the rest of the session.
    /// A catch with no failure state is not a contest; it is an animation that has finished.
    /// </para>
    ///
    /// <para>
    /// <b>Two loops, pulling opposite ways.</b> Straining pays line out and tires the animal;
    /// slack winds the line back in reach and lets the animal get its wind back
    /// (<c>LassoTether</c> recovers on the same clock). Neither side can run away with it, which is
    /// what makes reeling a decision rather than a formality — pull too early and you spend the
    /// line you will want later (<c>GDC-L1-SYS-0004</c>: a negative loop is what keeps a contest
    /// stable enough to have a middle).
    /// </para>
    ///
    /// <para>
    /// <b>Nothing here is measured twice.</b> Strain is judged on one machine and published as an
    /// edge, exactly as the reel already is, and every machine then integrates the length from the
    /// same number at the same rate. Measuring the overshoot per machine instead would have each
    /// one paying out against its own interpolated copy of two moving ends, and a rope whose length
    /// disagreed would put the break in a different place on every screen.
    /// </para>
    ///
    /// <para>
    /// Serialized on the lasso prefab and handed across rather than living on the runtime tether,
    /// for the reason <see cref="LassoStruggle"/> gives: fields on a component nobody can select
    /// are constants wearing a costume.
    /// </para>
    /// </summary>
    [System.Serializable]
    public class LassoTension
    {
        [Tooltip("Metres past the rope's length that counts as pulling with everything it has. " +
                 "The rope is a constraint rather than a spring, so the far end never gets far " +
                 "outside it — this is small on purpose.")]
        [SerializeField] private float fullStrainOvershoot = 0.6f;

        [Tooltip("Seconds of full-strength strain a rope survives. Rope does not break because a " +
                 "load is heavy, it breaks because a heavy load was on it for a while.")]
        [SerializeField] private float breakSeconds = 7f;

        [Tooltip("Seconds of slack that undoes one second of strain. Above 1 a rope that has been " +
                 "rested is as good as new sooner than it was worn out, which is what lets a " +
                 "patient player hold something they could never simply out-pull.")]
        [SerializeField] private float recoverySeconds = 3.5f;

        [Tooltip("Metres per second the rope gives up while the far end is straining against it. " +
                 "This is the animal taking line, and it is the reason a heavy one gets away.")]
        [SerializeField] private float payOutSpeed = 3.2f;

        [Tooltip("Metres of line there are. The rope may be paid out to here and no further; past " +
                 "it the animal is pulling against the end of the rope rather than against a reel.")]
        [SerializeField] private float maxLength = 26f;

        public float FullStrainOvershoot => Mathf.Max(fullStrainOvershoot, 0.01f);
        public float BreakSeconds => Mathf.Max(breakSeconds, 0.1f);
        public float RecoverySeconds => Mathf.Max(recoverySeconds, 0.1f);
        public float PayOutSpeed => Mathf.Max(payOutSpeed, 0f);
        public float MaxLength => Mathf.Max(maxLength, 1f);

        /// <summary>
        /// How hard the far end is pulling, 0 to 1, from how far outside the rope it has got.
        ///
        /// Pure and static because the machine that judges the strain and the machine that shows
        /// the player how frayed their rope is are not reliably the same one.
        /// </summary>
        public static float Strain01(float overshoot, float fullStrainOvershoot) =>
            Mathf.Clamp01(overshoot / Mathf.Max(fullStrainOvershoot, 0.01f));

        /// <summary>
        /// Advance the wear clock by one step and answer where it now stands, 0 (new) to 1 (gone).
        ///
        /// <para>
        /// Static, and takes the clock rather than holding it, because the same arithmetic runs on
        /// the authority against a live rope and in an EditMode test against a made-up one. A
        /// method that owned the clock would need a frame to be tested at all.
        /// </para>
        /// </summary>
        public static float Wear(float wear, float strain01, float deltaTime,
                                 float breakSeconds, float recoverySeconds)
        {
            float change = strain01 > 0f
                ? strain01 * deltaTime / Mathf.Max(breakSeconds, 0.1f)
                : -deltaTime / Mathf.Max(recoverySeconds, 0.1f);

            return Mathf.Clamp01(wear + change);
        }
    }
}
