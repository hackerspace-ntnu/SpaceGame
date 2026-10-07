using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How much weight is hanging off a body's ropes. Asked by anything that has to lift its own
    /// load — the jetpack, so far.
    ///
    /// <para>
    /// Separate from <see cref="Leash"/> itself because it is a question ABOUT the ropes on a body
    /// rather than a part of resolving one, and separate from the jetpack because the jetpack
    /// lives behind an assembly definition that cannot see this one.
    /// </para>
    /// <para>
    /// Three tests decide whether a rope counts, and each of them exists to close a way of getting
    /// thrust for nothing (<c>GDC-L1-SYS-0007</c>): the rope must be TAUT, the far end must be
    /// able to move, and it must hang BELOW. Without them a slack rope coiled on the sand, a rope
    /// tied to a rock and a rope tied to something overhead would all read as cargo.
    /// </para>
    /// </summary>
    public static class LeashLoad
    {
        /// <summary>
        /// Kilograms hanging below <paramref name="body"/> on taut ropes. Zero when nothing is
        /// tied on, which is every player who is not towing anybody.
        ///
        /// <para>
        /// Several ropes add, because several ropes really are several loads. Nothing here is
        /// clamped — what to do about an absurd figure belongs to whoever asked, and the jetpack's
        /// answer is <c>JetpackConfig.MaxLiftRatio</c>.
        /// </para>
        /// </summary>
        public static float HangingMassOn(Rigidbody body)
        {
            if (body == null) return 0f;

            float total = 0f;
            var ropes = Leash.All;

            for (int i = 0; i < ropes.Count; i++)
            {
                Leash rope = ropes[i];

                // A slack rope carries nothing. This is also what makes putting the load down
                // give the thrust back with no further bookkeeping.
                if (rope == null || !rope.IsTaut) continue;

                LeashEnd mine = rope.PlayerEndOn(body);
                if (mine == null) continue;

                LeashEnd load = rope.Opposite(mine);

                // An end with no body is bare geometry — a rock, the ground, the lander's hull.
                // It is not cargo, it is an anchor, and treating it as cargo would hand a pilot
                // full lift thrust for tying themselves to a cliff.
                if (!load.IsAlive || !load.CanMove) continue;

                // And it has to be underneath. A rope pulling sideways is drag rather than
                // weight, and one tied to something overhead is the opposite of a load.
                if (load.Position.y >= mine.Position.y) continue;

                total += load.Mass;
            }

            return total;
        }
    }
}
