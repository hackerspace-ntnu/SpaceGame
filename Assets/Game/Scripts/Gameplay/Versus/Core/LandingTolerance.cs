using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// How far a hull may be nudged off its authored point, and how uneven the ground under it
    /// may be, before it is worth looking somewhere else to set it down.
    ///
    /// <para>
    /// A struct rather than five loose arguments because these travel together everywhere and
    /// are authored together in one place — see <c>VersusShipSpawnConfig</c>.
    /// </para>
    /// </summary>
    public readonly struct LandingTolerance
    {
        public LandingTolerance(float maxGroundSpread, float searchRadius, float ringStep,
                                float bellyClearance)
        {
            MaxGroundSpread = Mathf.Max(0f, maxGroundSpread);
            SearchRadius = Mathf.Max(0f, searchRadius);
            RingStep = Mathf.Max(0.01f, ringStep);
            BellyClearance = Mathf.Max(0f, bellyClearance);
        }

        /// <summary>Metres the hull's low corner may hang before the spot is rejected.</summary>
        public float MaxGroundSpread { get; }

        /// <summary>How far from the authored point a flatter spot may be looked for.</summary>
        public float SearchRadius { get; }

        /// <summary>Spacing of the search rings.</summary>
        public float RingStep { get; }

        /// <summary>Gap left between the hull's lowest point and the ground it rests on.</summary>
        public float BellyClearance { get; }
    }
}
