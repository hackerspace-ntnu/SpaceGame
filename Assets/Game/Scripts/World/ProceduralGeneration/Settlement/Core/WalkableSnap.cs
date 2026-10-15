// Where on the NavMesh a settlement point may stand.
//
// A settlement stacks walkable surfaces on one spot — sand, street slabs, a porch, and roofs and
// fire-pit tops the NavMesh covers too — and only some of them join the rest of the settlement.
// Sampling "the ground" from above lands on whichever is highest (a door once landed on a roof 18 m
// up), and sampling the terrain lands in the sand under a porch, off the mesh entirely. So a point is
// the HIGHEST surface under it that is walkable from the settlement's main walkable area, and that
// area is the largest set of candidate points that can all reach one another.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.World
{
    public static class WalkableSnap
    {
        private const float RayHeight = 60f;           // start this far above a point: taller than any building
        private const float SurfaceTolerance = 1f;     // a collider hit counts when the NavMesh lies this close to it

        /// <summary>Every NavMesh surface straight below and above <paramref name="point"/>, highest first.</summary>
        public static IEnumerable<Vector3> SurfacesAt(Vector3 point)
        {
            var origin = new Vector3(point.x, point.y + RayHeight, point.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, RayHeight * 2f, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits.OrderBy(h => h.distance))
                if (NavMesh.SamplePosition(hit.point, out NavMeshHit onMesh, SurfaceTolerance, NavMesh.AllAreas))
                    yield return onMesh.position;
        }

        /// <summary>The highest surface at <paramref name="point"/> a resident can walk to from <paramref name="heart"/>.</summary>
        public static bool TryReachable(Vector3 point, Vector3 heart, out Vector3 onMesh)
        {
            foreach (Vector3 surface in SurfacesAt(point))
                if (NavMeshReach.CanWalk(heart, surface))
                {
                    onMesh = surface;
                    return true;
                }
            onMesh = point;
            return false;
        }

        /// <summary>
        /// A point inside the largest group of <paramref name="points"/>' surfaces that can all walk to one
        /// another — the streets and porches, not an island on a roof. False when none is on the NavMesh.
        /// </summary>
        public static bool TryMainIsland(IEnumerable<Vector3> points, out Vector3 heart)
        {
            var remaining = points.SelectMany(SurfacesAt).ToList();
            var best = new List<Vector3>();
            while (remaining.Count > 0)
            {
                Vector3 seed = remaining[0];
                var island = remaining.Where(p => p == seed || NavMeshReach.CanWalk(seed, p)).ToList();
                remaining.RemoveAll(island.Contains);
                if (island.Count > best.Count) best = island;
            }
            heart = best.Count > 0 ? best[0] : default;
            return best.Count > 0;
        }
    }
}
