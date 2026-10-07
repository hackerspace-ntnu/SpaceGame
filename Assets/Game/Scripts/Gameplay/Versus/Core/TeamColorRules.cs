namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Which swatch each team wears, and how a member steps their own team's colour without
    /// landing on one another team already has.
    ///
    /// <para>
    /// The palette itself is not visible from here. <c>SuitPalette</c> lives in Assembly-CSharp,
    /// which an assembly definition cannot reference, so the swatch count arrives as an argument
    /// and the caller does the decoding. That is not a workaround — it is what makes the rule
    /// testable without Unity, and the rule is the part worth testing.
    /// </para>
    ///
    /// <para>
    /// Skipping matters more here than it looks. Two teams in the same orange is not a cosmetic
    /// annoyance in a game where the only thing telling you who to shoot is the colour of a suit.
    /// </para>
    /// </summary>
    public static class TeamColorRules
    {
        /// <summary>
        /// The next swatch in <paramref name="direction"/> that no other team is wearing.
        ///
        /// <para>
        /// <paramref name="current"/> is normalised into the palette before stepping, so a colour
        /// that arrived out of range — a lobby run by a build with a bigger palette, a swatch count
        /// that shrank underneath a saved value — still lands somewhere valid, including on the
        /// "give up" fallback below. Trusting an out-of-range value all the way to that fallback
        /// would hand the caller an index outside the palette it just asked about.
        /// </para>
        ///
        /// <para>
        /// Walks the palette at most once and gives up back on the (normalised) current swatch:
        /// with every other swatch taken there is genuinely nowhere to go, and standing still is
        /// the honest answer. Wraps, so the cycler is a loop rather than a slider with two dead
        /// ends. <paramref name="direction"/> only ever arrives as ±1 from the UI, but zero and any
        /// other non-negative value read as forward — there is no meaningful "stand still" request
        /// through this parameter, so it does not need one.
        /// </para>
        /// </summary>
        public static int Step(int current, int direction, int swatchCount, int[] takenByOtherTeams)
        {
            if (swatchCount <= 0) return 0;

            current = ((current % swatchCount) + swatchCount) % swatchCount;

            int stride = direction >= 0 ? 1 : -1;
            int candidate = current;

            for (int step = 0; step < swatchCount; step++)
            {
                candidate = ((candidate + stride) % swatchCount + swatchCount) % swatchCount;

                if (!IsTaken(candidate, takenByOtherTeams)) return candidate;
            }

            return current;
        }

        /// <summary>
        /// The swatch each team starts on, spread across the palette so neighbouring teams are as
        /// far apart on the wheel as the palette allows.
        ///
        /// <para>
        /// Distinct while the palette is large enough, and merely valid when it is not: more teams
        /// than swatches cannot all differ, and a host looking at the rules page needs a colour per
        /// team far more than they need this method to refuse.
        /// </para>
        /// </summary>
        public static int[] DefaultColors(int teams, int swatchCount)
        {
            var colors = new int[teams < 0 ? 0 : teams];
            if (colors.Length == 0 || swatchCount <= 0) return colors;

            for (int team = 0; team < colors.Length; team++)
                colors[team] = team < swatchCount
                    // No trailing "% swatchCount" here: team < colors.Length is the loop bound, so
                    // this quotient is already < swatchCount by floor division alone.
                    ? team * swatchCount / colors.Length
                    : team % swatchCount;

            return Distinguish(colors, swatchCount);
        }

        /// <summary>
        /// Pushes any duplicate onto the next free swatch, so an uneven spread cannot collide.
        ///
        /// <para>
        /// The spread above can repeat when the team count does not divide the palette; rather
        /// than reason about which cases those are, every result is walked once and fixed.
        /// </para>
        ///
        /// <para>
        /// The <c>team &gt;= swatchCount</c> guard is what keeps the inner loop from spinning: by
        /// the time team <c>t</c> is processed, induction gives pairwise-distinct values on
        /// <c>colors[0..t-1]</c> — at most <c>t</c> of the <c>swatchCount</c> slots are taken. While
        /// <c>t &lt; swatchCount</c> that leaves at least one free slot for <c>colors[t]</c> to walk
        /// onto, so the loop below always terminates. Once <c>t</c> reaches <c>swatchCount</c> that
        /// stops holding — there may be no free slot left at all — so those teams are left with
        /// their un-distinguished value instead of being spun forever looking for one that may not
        /// exist.
        /// </para>
        /// </summary>
        private static int[] Distinguish(int[] colors, int swatchCount)
        {
            for (int team = 1; team < colors.Length; team++)
            {
                if (team >= swatchCount) break;

                while (IsTakenBefore(colors, team))
                    colors[team] = (colors[team] + 1) % swatchCount;
            }

            return colors;
        }

        private static bool IsTakenBefore(int[] colors, int team)
        {
            for (int other = 0; other < team; other++)
                if (colors[other] == colors[team])
                    return true;

            return false;
        }

        /// <summary>
        /// Plain equality, deliberately: <paramref name="swatch"/> always arrives already inside
        /// this build's palette, so an entry in <paramref name="taken"/> from a peer with a longer
        /// palette can never equal it and is silently skipped rather than blocking a swatch that
        /// does not exist here. The same courtesy <see cref="Step"/> extends to an out-of-range
        /// <c>current</c>, applied to the other side of the comparison.
        /// </summary>
        private static bool IsTaken(int swatch, int[] taken)
        {
            if (taken == null) return false;

            foreach (int other in taken)
                if (other == swatch)
                    return true;

            return false;
        }
    }
}
