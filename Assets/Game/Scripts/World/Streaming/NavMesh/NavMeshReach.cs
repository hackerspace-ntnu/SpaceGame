using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.World
{
    /// <summary>
    /// Whether an agent can walk from one NavMesh point to another, asked the only way that answers
    /// it: a complete path.
    ///
    /// <para>
    /// <c>NavMesh.SamplePosition</c> finds any NavMesh near a point, including an island nothing
    /// walks to — a Sky City roof, a cut-off arena ledge. An agent sent there gets a partial path,
    /// walks to the nearest edge of its own island and stands at the railing. Anything choosing a
    /// walk-to point on a NavMesh in pieces filters its candidates through this.
    /// </para>
    /// </summary>
    public static class NavMeshReach
    {
        // One scratch path for every caller: NavMesh queries are main-thread only, so nothing can
        // hold it across another call.
        private static NavMeshPath scratch;

        /// <summary>True when a complete path leads from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static bool CanWalk(Vector3 from, Vector3 to)
        {
            scratch ??= new NavMeshPath();
            return NavMesh.CalculatePath(from, to, NavMesh.AllAreas, scratch)
                   && scratch.status == NavMeshPathStatus.PathComplete;
        }
    }
}
