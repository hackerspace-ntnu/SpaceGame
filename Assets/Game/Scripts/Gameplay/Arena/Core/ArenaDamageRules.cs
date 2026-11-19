using System;

namespace SpaceGame.Gameplay.Arena
{
    /// <summary>
    /// What a hit is worth inside an arena. Unity-free so the EditMode suite can pin it.
    ///
    /// <para>
    /// Arena balance is a property of the match, not of the weapons: the same artifacts are tuned
    /// for the story world, where a pellet gun meets a raider with no squad behind them. So rather
    /// than retune every item, an arena hit is scaled and then capped — the cap is what guarantees
    /// nobody is ever one-shot, the scale is what stretches a firefight out to a few seconds.
    /// </para>
    /// </summary>
    public static class ArenaDamageRules
    {
        /// <summary>A player who is on no team — -1 on <c>PlayerIdentity.Team</c> — or a source that is not a player at all.</summary>
        public const int NoTeam = -1;

        /// <summary>
        /// The damage to actually apply.
        ///
        /// <list type="bullet">
        /// <item>Outside a live round nothing lands, so the countdown and the result screen are safe.</item>
        /// <item>A teammate's hit lands as nothing. Your own does land — a gun that backfires is still a gun that backfires.</item>
        /// <item>Everything else is scaled by <paramref name="scale"/>, then capped at <paramref name="maxHit"/>.</item>
        /// </list>
        ///
        /// A hit that scales to below one point but was worth something rounds up to one, so a weak
        /// weapon at long range still chips rather than silently doing nothing.
        /// </summary>
        public static int Resolve(int amount, int attackerTeam, int victimTeam, bool isSelf,
                                  bool fighting, float scale, int maxHit)
        {
            if (amount <= 0 || !fighting) return 0;

            if (!isSelf && attackerTeam != NoTeam && attackerTeam == victimTeam) return 0;

            int scaled = (int)Math.Round(amount * Math.Max(0f, scale), MidpointRounding.AwayFromZero);
            if (scaled < 1 && scale > 0f) scaled = 1;

            return maxHit > 0 && scaled > maxHit ? maxHit : scaled;
        }
    }
}
