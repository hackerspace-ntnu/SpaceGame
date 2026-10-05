using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.World
{
    /// <summary>
    /// The height of the walkable ground at a point, read off the world NavMesh.
    ///
    /// <para>
    /// For questions about ground far from any player. Chunk heightmaps and colliders exist only in
    /// the loaded chunks round the players, so a caller asking about a place two kilometres off gets
    /// nothing from either; the world NavMesh is baked whole at author time and added on every
    /// machine at startup (NavMeshSystem.md), so it answers anywhere in the world. It is the
    /// walkable surface rather than the terrain — holes where rocks and buildings stand — which is
    /// what a caller choosing somewhere to walk to wants anyway.
    /// </para>
    /// </summary>
    public static class NavMeshGround
    {
        /// <summary>
        /// The NavMesh height at <paramref name="xz"/>, searched from <paramref name="probeY"/> up to
        /// <paramref name="reach"/> away. False when there is no NavMesh in reach, or the nearest is
        /// more than <paramref name="horizontalTolerance"/> sideways of the point — the edge of a hole
        /// or the lip of a ledge, which is not the ground asked about.
        /// </summary>
        public static bool TryHeight(Vector2 xz, float probeY, float reach, float horizontalTolerance,
                                     out float groundY)
        {
            groundY = 0f;

            var probe = new Vector3(xz.x, probeY, xz.y);
            if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, reach, NavMesh.AllAreas)) return false;

            if (new Vector2(hit.position.x - xz.x, hit.position.z - xz.y).sqrMagnitude
                > horizontalTolerance * horizontalTolerance)
                return false;

            groundY = hit.position.y;
            return true;
        }
    }
}
