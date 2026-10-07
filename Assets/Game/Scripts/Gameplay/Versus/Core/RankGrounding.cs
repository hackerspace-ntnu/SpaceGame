using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>Where the rank actually stands, once the ground has had its say.</summary>
    public readonly struct GroundedRank
    {
        /// <summary>One world position per seat, in the order they were handed in.</summary>
        public readonly Vector3[] Positions;

        /// <summary>The lowest and highest ground the rank stands on.</summary>
        public readonly float MinY;

        public readonly float MaxY;

        public GroundedRank(Vector3[] positions, float minY, float maxY)
        {
            Positions = positions;
            MinY = minY;
            MaxY = maxY;
        }

        /// <summary>
        /// How much the ground rises across the whole rank, in metres — the vertical extent the
        /// camera has to frame on top of the astronauts themselves.
        /// </summary>
        public float HeightSpread => MaxY - MinY;
    }

    /// <summary>
    /// Drops a rank of seats onto the ground.
    ///
    /// <para>
    /// <see cref="RankLayout"/> deliberately keeps every seat flat at local <c>y = 0</c> — it is
    /// pure geometry and knows nothing about a world. That is invisible for a six-metre line of four
    /// on an anchor somebody placed on flat sand, and wrong the moment the rank is twenty metres
    /// across and folded into two rows: half of it floats over a dip or stands buried in a rise.
    /// </para>
    ///
    /// <para>
    /// The probe arrives as a delegate rather than as a <c>Physics.Raycast</c> call, so the rule —
    /// use the ground if there is any, otherwise the anchor's own plane — can be tested without a
    /// scene, colliders or physics. The caller supplies the real cast and the layer mask that goes
    /// with it.
    /// </para>
    /// </summary>
    public static class RankGrounding
    {
        /// <summary>
        /// Answers what the ground height is under <paramref name="seat"/>. False when there is
        /// nothing under it at all, in which case <paramref name="groundY"/> is not read.
        /// </summary>
        public delegate bool GroundProbe(Vector3 seat, out float groundY);

        /// <summary>
        /// Puts every seat on the ground beneath it, leaving its x and z alone.
        ///
        /// <paramref name="fallbackY"/> is what a seat with no ground under it gets — the anchor's
        /// own height, which reproduces exactly what the rank did before it was ever grounded. A
        /// scene with no ground colliders therefore looks like it always did, rather than collapsing
        /// the whole rank to zero.
        /// </summary>
        public static GroundedRank Solve(IReadOnlyList<Vector3> worldSeats, float fallbackY, GroundProbe probe)
        {
            int count = worldSeats?.Count ?? 0;

            if (count == 0 || probe == null)
                return new GroundedRank(System.Array.Empty<Vector3>(), fallbackY, fallbackY);

            var positions = new Vector3[count];

            float min = float.MaxValue;
            float max = float.MinValue;

            for (int i = 0; i < count; i++)
            {
                Vector3 seat = worldSeats[i];
                float y = probe(seat, out float hit) ? hit : fallbackY;

                positions[i] = new Vector3(seat.x, y, seat.z);

                min = Mathf.Min(min, y);
                max = Mathf.Max(max, y);
            }

            return new GroundedRank(positions, min, max);
        }
    }
}
