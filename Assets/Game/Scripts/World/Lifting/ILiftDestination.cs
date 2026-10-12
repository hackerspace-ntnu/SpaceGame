using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Somewhere a lifted load is carried TO: the lander's oxygen plant mount. The load's server checks every registered
    /// destination while it is carried or resting and hands itself over to the first one that wants it and that it has reached.
    /// </summary>
    public interface ILiftDestination
    {
        /// <summary>Where the load has to be brought, in world space.</summary>
        Vector3 Point { get; }

        /// <summary>How close, ignoring height, counts as arrived.</summary>
        float Radius { get; }

        /// <summary>Does this destination want this load right now?</summary>
        bool Accepts(Liftable load);

        /// <summary>SERVER: the load has arrived. The destination takes it — usually despawning it.</summary>
        void Receive(Liftable load);
    }

    public static class LiftDestinations
    {
        /// <summary>Has <paramref name="load"/> come within <paramref name="radius"/> of <paramref name="point"/>, ignoring height?</summary>
        public static bool Reached(Vector3 load, Vector3 point, float radius)
        {
            Vector3 flat = point - load;
            flat.y = 0f;
            return flat.sqrMagnitude <= radius * radius;
        }
    }
}
