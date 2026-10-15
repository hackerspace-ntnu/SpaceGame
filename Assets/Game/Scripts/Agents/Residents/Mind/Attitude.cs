// How a resident regards one player, as a pure function of two temperament axes, a small memory and
// what the player is doing right now. Nothing here is stored: the stance is recomputed on every read, so
// a grudge expiring or a gun being lowered changes it the next time anyone asks — and the whole reaction
// matrix can be tested and previewed in edit mode without a scene.
//
// Nerve runs timid (0) → bold (1); temper runs warm (0) → prickly (1).
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    public static class Attitude
    {
        /// <summary>The line between timid and bold, and between warm and prickly.</summary>
        public const float Midpoint = 0.5f;

        /// <summary>Within this of <see cref="Midpoint"/> on BOTH axes a resident has no strong opening: Unsure.</summary>
        public const float UnsureBand = 0.15f;

        /// <summary>The first-meeting stance the two axes give: one quadrant each, Unsure in the middle.</summary>
        public static Stance Opening(float nerve, float temper)
        {
            if (Mathf.Abs(nerve - Midpoint) <= UnsureBand && Mathf.Abs(temper - Midpoint) <= UnsureBand)
                return Stance.Unsure;

            bool bold = IsBold(nerve);
            bool prickly = temper >= Midpoint;
            if (bold) return prickly ? Stance.Protective : Stance.Curious;
            return prickly ? Stance.Afraid : Stance.Asking;
        }

        /// <summary>
        /// The stance toward one player now. Checked from the most serious down, so a fight always reads
        /// Hostile and a personal grudge always outweighs regard — a friend who hit your brother is not Warm.
        /// <paramref name="regard"/> is familiarity plus favor: past <paramref name="warmAt"/> it is Warm, at or below
        /// minus <paramref name="coldAt"/> it is as cold as a grudge, however the favor was lost.
        /// </summary>
        public static Stance StanceFor(float nerve, float temper, float regard, bool grudge,
                                       in PlayerRead read, AggressionBand band, float warmAt, float coldAt)
        {
            if (band == AggressionBand.Grudge) return Stance.Hostile;
            if (band == AggressionBand.Drawn || grudge || regard <= -coldAt) return IsBold(nerve) ? Stance.Cold : Stance.Afraid;
            if (regard >= warmAt) return Stance.Warm;
            if (read.armed) return IsBold(nerve) ? Stance.Protective : Stance.Afraid;
            return Opening(nerve, temper);
        }

        /// <summary>The line-table bucket a stance belongs to.</summary>
        public static StanceFamily FamilyOf(Stance s) => s switch
        {
            Stance.Warm => StanceFamily.Friendly,
            Stance.Curious or Stance.Asking or Stance.Unsure => StanceFamily.Neutral,
            Stance.Protective or Stance.Afraid or Stance.Cold => StanceFamily.Wary,
            Stance.Hostile => StanceFamily.Hostile,
            _ => StanceFamily.Neutral,
        };

        private static bool IsBold(float nerve) => nerve >= Midpoint;
    }
}
