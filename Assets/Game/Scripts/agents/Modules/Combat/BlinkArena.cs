using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    /// <summary>
    /// The points a <see cref="BlinkModule"/> may reappear at — authored in the place the fight
    /// happens (the witch's glade), and found by the blinker at runtime because a spawned creature's
    /// prefab cannot reference a scene.
    /// </summary>
    public class BlinkArena : MonoBehaviour
    {
        private static readonly LiveSet<BlinkArena> Live = new();

        [Tooltip("Where a blinker may reappear. On the NavMesh, in the open.")]
        [SerializeField] private Transform[] points = System.Array.Empty<Transform>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

        /// <summary>The nearest arena within <paramref name="radius"/> of <paramref name="position"/>.</summary>
        public static bool TryFindNear(Vector3 position, float radius, out BlinkArena arena) =>
            Live.TryFindNearest(position, radius, out arena);

        /// <summary>
        /// The point furthest from <paramref name="away"/> among those within
        /// <paramref name="maxHop"/> of <paramref name="from"/>, or null when none is in reach.
        /// </summary>
        public Transform FurthestFrom(Vector3 away, Vector3 from, float maxHop)
        {
            Transform furthest = null;
            float bestDistance = -1f;
            float reach = maxHop * maxHop;
            foreach (Transform candidate in points)
            {
                if (candidate == null) continue;
                if ((candidate.position - from).sqrMagnitude > reach) continue;

                float d = (candidate.position - away).sqrMagnitude;
                if (d <= bestDistance) continue;

                bestDistance = d;
                furthest = candidate;
            }

            return furthest;
        }
    }
}
