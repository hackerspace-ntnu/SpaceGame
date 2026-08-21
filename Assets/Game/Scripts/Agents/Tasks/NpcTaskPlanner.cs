// Choosing what to do next and where to do it, with no MonoBehaviour anywhere near it.
//
// Static and pure because the same decision is made twice in this game and must come out the same
// both times: NpcTaskModule makes it for an NPC standing in the world, and NpcWorldSim makes it for
// a caravan that is currently 3 km away and has no GameObjects at all. Two implementations of
// "pick the next job" would drift, and the drift would only ever show up as a group that behaves
// differently in the ten seconds after it spawns than it did for the hour before.
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.World;

namespace SpaceGame.Agents
{
    public static class NpcTaskPlanner
    {
        /// <summary>
        /// Pick the next task by weight.
        ///
        /// <paramref name="avoidIndex"/> is discouraged rather than forbidden: an NPC with two
        /// tasks would otherwise alternate them perfectly, which reads as a rota rather than a
        /// person. It is skipped only when something else is available to pick.
        /// </summary>
        public static int PickTask(NpcTask[] tasks, int avoidIndex = -1)
        {
            if (tasks == null || tasks.Length == 0) return -1;
            if (tasks.Length == 1) return tasks[0] != null && tasks[0].weight > 0f ? 0 : -1;

            float total = 0f;
            for (int i = 0; i < tasks.Length; i++)
            {
                if (tasks[i] == null || tasks[i].weight <= 0f) continue;
                if (i == avoidIndex) continue;
                total += tasks[i].weight;
            }

            // Everything except the one we wanted to avoid is disabled — take it anyway rather than
            // returning "no task", which would leave the NPC inert forever.
            if (total <= 0f)
                return avoidIndex >= 0 && avoidIndex < tasks.Length && tasks[avoidIndex] != null ? avoidIndex : -1;

            float roll = UnityEngine.Random.value * total;
            for (int i = 0; i < tasks.Length; i++)
            {
                if (tasks[i] == null || tasks[i].weight <= 0f) continue;
                if (i == avoidIndex) continue;

                roll -= tasks[i].weight;
                if (roll <= 0f) return i;
            }

            // Float error at the very top of the range.
            for (int i = tasks.Length - 1; i >= 0; i--)
                if (tasks[i] != null && tasks[i].weight > 0f && i != avoidIndex) return i;

            return -1;
        }

        /// <summary>
        /// Where this task should send the NPC.
        ///
        /// <para>
        /// Registered sites first, at random rather than nearest — nearest is a stable answer, so a
        /// group sharing a home and a task list would all walk the same route to the same heap
        /// forever.
        /// </para>
        /// <para>
        /// The fallback is the important half. A world with no sites of the wanted kind is the
        /// normal state today and will be until procedural generation registers them, and a task
        /// that simply failed would leave every NPC standing on its spawn point with nothing to
        /// explain why. So it picks a point out at search range instead: the NPC still crosses the
        /// map, still has somewhere to be, and the day sites exist it starts going to real ones
        /// with no change here.
        /// </para>
        /// </summary>
        public static bool ResolveDestination(NpcTask task, Vector3 origin, string excludeSiteId,
                                              out Vector3 destination, out float arriveRadius,
                                              out string siteId, out string siteName)
        {
            destination = origin;
            arriveRadius = task != null ? task.arriveRadius : 6f;
            siteId = string.Empty;
            siteName = string.Empty;

            if (task == null) return false;

            if (WorldSiteRegistry.TryFindRandom(task.targetSite, origin, task.searchRadius,
                                                out WorldSite site, excludeSiteId))
            {
                destination = site.Position;
                arriveRadius = site.Radius;
                siteId = site.Id;
                siteName = site.Name;
                return true;
            }

            return TryRoamPoint(origin, task.searchRadius, out destination);
        }

        /// <summary>
        /// A point to head for when no site fits: a random bearing, somewhere between a third and
        /// the whole of the search radius out.
        ///
        /// The inner bound matters. Uniform sampling of a disc puts most points near the middle,
        /// which for a "roam far" fallback produces NPCs that shuffle a few metres and stop — the
        /// exact behaviour this is meant to avoid.
        /// </summary>
        public static bool TryRoamPoint(Vector3 origin, float radius, out Vector3 point)
        {
            float min = Mathf.Max(8f, radius * 0.33f);
            float max = Mathf.Max(min + 1f, radius);

            for (int attempt = 0; attempt < 8; attempt++)
            {
                float angle = UnityEngine.Random.value * Mathf.PI * 2f;
                float distance = UnityEngine.Random.Range(min, max);
                Vector3 candidate = origin + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * distance;

                // Sample generously — the world is a heightmap, so a point picked on the flat is
                // routinely tens of metres above or below the ground it lands on.
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, Mathf.Max(30f, radius * 0.25f),
                                           NavMesh.AllAreas))
                {
                    point = hit.position;
                    return true;
                }
            }

            point = origin;
            return false;
        }

        /// <summary>
        /// The same fallback without a NavMesh, for the virtual layer.
        ///
        /// A group being simulated as a record has no agent and does not path — it walks a straight
        /// line across a map that is mostly open desert. Asking the NavMesh about a point 3 km away
        /// would work, but it is a query per group per decision for an answer nobody uses until the
        /// group spawns, at which point the spawner samples anyway.
        /// </summary>
        public static Vector3 RoamPointUnsampled(Vector3 origin, float radius, System.Random rng)
        {
            float min = Mathf.Max(8f, radius * 0.33f);
            float max = Mathf.Max(min + 1f, radius);

            double angle = (rng?.NextDouble() ?? UnityEngine.Random.value) * Mathf.PI * 2.0;
            double t = rng?.NextDouble() ?? UnityEngine.Random.value;
            float distance = (float)(min + t * (max - min));

            return origin + new Vector3(Mathf.Sin((float)angle), 0f, Mathf.Cos((float)angle)) * distance;
        }
    }
}
