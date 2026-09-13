using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// How level the ground is under a whole hull, rather than under the one point in its middle.
    ///
    /// <para>
    /// A ship is not a spawn point. <c>PlayerShip</c> measures 23 m across and 29 m fore-and-aft and
    /// its Rigidbody carries <see cref="RigidbodyConstraints.FreezeRotation"/>, so it can never tilt
    /// to match a slope: the hull always rests level, on the HIGHEST ground it spans, with the low
    /// side hanging in the air. Grounding it from a single sample under its origin therefore answers
    /// a question nobody asked — measured in the shipped world, terrain under the arrival hull's
    /// centre read 100.0 m while the terrain 11 m to starboard read 104.9 m, and the hull was left
    /// floating over the low side while the high side sat buried in the hillside.
    /// </para>
    ///
    /// <para>
    /// Pure, and sampling is injected, for the reason every other landing calculation in this area
    /// is: the arithmetic is what goes wrong, and it can only be tested at all with no terrain, no
    /// colliders and no scene in existence.
    /// </para>
    /// </summary>
    public static class HullFootprint
    {
        /// <summary>Centre, four mid-edges, four corners.</summary>
        public const int SampleCount = 9;

        /// <summary>
        /// The ground height at one point, or false where nothing can vouch for one — an unstreamed
        /// chunk, or open air. The contract <c>ShipGrounding.TryResolveGround</c> documents, handed
        /// in so the arithmetic below can be exercised without a world.
        /// </summary>
        public delegate bool GroundSampler(Vector2 at, out float groundY);

        /// <summary>What the ground under one hull pose looks like.</summary>
        public readonly struct Ground
        {
            public Ground(int found, float highest, float lowest)
            {
                Found = found;
                Highest = highest;
                Lowest = lowest;
            }

            /// <summary>How many of the <see cref="SampleCount"/> points had ground under them.</summary>
            public int Found { get; }

            /// <summary>The height a level hull comes to rest on — it cannot sink past its high corner.</summary>
            public float Highest { get; }

            /// <summary>The height the hull's low corner hangs above.</summary>
            public float Lowest { get; }

            /// <summary>True once anything at all was measurable.</summary>
            public bool Any => Found > 0;

            /// <summary>
            /// True only when the whole footprint was measurable. Anything less means part of the
            /// hull is over an unstreamed chunk, which is "ask again", never "land here" — the
            /// missing part is exactly where the ground might be higher than everything measured.
            /// </summary>
            public bool Complete => Found >= SampleCount;

            /// <summary>How far the low corner would hang. Zero on flat ground.</summary>
            public float Spread => Any ? Highest - Lowest : 0f;
        }

        /// <summary>
        /// Where a hull at this pose puts its weight: its own centre, the middle of each edge, and
        /// its four corners, in the hull's own axes so the long samples stay along the hull as it
        /// turns.
        ///
        /// <para>
        /// The corners are not decoration. A hull turned 45 degrees to a slope touches it at a
        /// corner first, and an edge-only ring measures the two sides that happen to be lower.
        /// </para>
        /// </summary>
        public static void Samples(Vector2 centreXZ, float yaw, Vector2 extents, Vector2[] into)
        {
            if (into == null || into.Length < SampleCount)
            {
                Debug.LogError($"[HullFootprint] Samples needs an array of at least {SampleCount}.");
                return;
            }

            float radians = yaw * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);

            // Yaw zero looks down +Z, matching Unity and ShipSpawnPoint.
            Vector2 across = new Vector2(cos, -sin) * Mathf.Max(0f, extents.x);
            Vector2 along = new Vector2(sin, cos) * Mathf.Max(0f, extents.y);

            into[0] = centreXZ;
            into[1] = centreXZ + across;
            into[2] = centreXZ - across;
            into[3] = centreXZ + along;
            into[4] = centreXZ - along;
            into[5] = centreXZ + across + along;
            into[6] = centreXZ + across - along;
            into[7] = centreXZ - across + along;
            into[8] = centreXZ - across - along;
        }

        /// <summary>
        /// The ground under a hull at this pose. <paramref name="scratch"/> is optional and exists
        /// only so a caller sweeping many candidate poses does not allocate one array per pose.
        /// </summary>
        public static Ground Measure(Vector2 centreXZ, float yaw, Vector2 extents,
                                     GroundSampler sample, Vector2[] scratch = null)
        {
            if (sample == null) return new Ground(0, 0f, 0f);

            Vector2[] points = scratch != null && scratch.Length >= SampleCount
                ? scratch
                : new Vector2[SampleCount];

            Samples(centreXZ, yaw, extents, points);

            int found = 0;
            float highest = float.NegativeInfinity;
            float lowest = float.PositiveInfinity;

            for (int i = 0; i < SampleCount; i++)
            {
                if (!sample(points[i], out float y)) continue;

                found++;
                if (y > highest) highest = y;
                if (y < lowest) lowest = y;
            }

            return found == 0 ? new Ground(0, 0f, 0f) : new Ground(found, highest, lowest);
        }
    }
}
