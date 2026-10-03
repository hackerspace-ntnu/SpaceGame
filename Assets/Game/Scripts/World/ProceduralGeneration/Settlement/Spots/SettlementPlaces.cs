// The places a generated settlement's buildings and decorations brought along, as the generator checks
// them: where the walkable heart of the settlement is, where a resident stands at each dwelling's door, and
// which spots or doors no resident can walk to. Needs a NavMesh over the settlement — the throwaway one
// Generate bakes, or the world's at runtime.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Agents.Residents;

namespace SpaceGame.World
{
    public static class SettlementPlaces
    {
        // A hit counts as something to sit on when its normal points this far up (cos of ~45 degrees).
        private const float FlatSurface = 0.7f;

        /// <summary>
        /// A point in the largest walkable area joining the settlement's doors and spots — the streets and
        /// yards, not an island on a roof. <paramref name="fallback"/> when nothing is on the NavMesh.
        /// </summary>
        public static Vector3 FindHeart(Transform generated, IReadOnlyList<float> doorDistances, Vector3 fallback)
        {
            var landmarks = new List<Vector3>();
            foreach (Dwelling dwelling in generated.GetComponentsInChildren<Dwelling>())
            {
                Transform doorway = DoorwayOf(dwelling);
                landmarks.Add(doorway.position + OutwardOf(doorway) * doorDistances[0]);
            }
            foreach (SettlementSpot spot in generated.GetComponentsInChildren<SettlementSpot>()) landmarks.Add(spot.Position);
            if (landmarks.Count == 0) landmarks.Add(fallback);
            return WalkableSnap.TryMainIsland(landmarks, out Vector3 heart) ? heart : fallback;
        }

        /// <summary>The first point straight out from the dwelling's doorway, at <paramref name="doorDistances"/>, walkable from <paramref name="heart"/>.</summary>
        public static bool TryDoorStand(Dwelling dwelling, Vector3 heart, IReadOnlyList<float> doorDistances, out Vector3 stand)
        {
            Transform doorway = DoorwayOf(dwelling);
            Vector3 outward = OutwardOf(doorway);
            foreach (float distance in doorDistances)
                if (WalkableSnap.TryReachable(doorway.position + outward * distance, heart, out stand))
                    return true;
            stand = doorway.position + outward * doorDistances[0];
            return false;
        }

        /// <summary>One line per dwelling or spot a resident could not use, naming it — empty when all is well.</summary>
        public static List<string> Problems(Transform generated, Vector3 heart, IReadOnlyList<float> doorDistances)
        {
            var problems = new List<string>();
            foreach (Dwelling dwelling in generated.GetComponentsInChildren<Dwelling>())
            {
                if (dwelling.Door == null) problems.Add($"{dwelling.name} has no SettlementEntrance — its door is its own pivot");
                if (!TryDoorStand(dwelling, heart, doorDistances, out _)) problems.Add($"{dwelling.name}: nothing walkable out from its door");
            }
            foreach (SettlementSpot spot in generated.GetComponentsInChildren<SettlementSpot>())
            {
                string where = PathBelow(generated, spot.transform);
                if (spot.Use == null) problems.Add($"{where} has no SpotUse");
                else if (!TryStand(spot.Position, spot.Use.elevated, heart, out _, out string why))
                    problems.Add($"{where} ({spot.Use.name}) is unusable: {why}");
            }
            return problems;
        }

        /// <summary>
        /// Where a resident stands for a spot at <paramref name="point"/>: the NavMesh within the tuning's snap radius
        /// horizontally and snap height vertically — never the 12 m snap of a plain goal — and, unless the spot is
        /// <paramref name="elevated"/> (a deck the body may be put on), walkable from <paramref name="heart"/>.
        /// False, with <paramref name="why"/>, when the spot cannot be stood at.
        /// </summary>
        public static bool TryStand(Vector3 point, bool elevated, Vector3 heart, out Vector3 stand, out string why)
        {
            ResidentTuning tuning = ResidentTuning.Instance;
            stand = point;
            why = null;

            float reach = Mathf.Sqrt(tuning.standSnapRadius * tuning.standSnapRadius + tuning.standSnapHeight * tuning.standSnapHeight);
            if (!NavMesh.SamplePosition(point, out NavMeshHit hit, reach, NavMesh.AllAreas))
            {
                why = $"no NavMesh within {tuning.standSnapRadius:0.##} m";
                return false;
            }

            Vector3 offset = hit.position - point;
            if (Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z) > tuning.standSnapRadius || Mathf.Abs(offset.y) > tuning.standSnapHeight)
            {
                why = $"the NavMesh is {offset.x:0.##}, {offset.y:0.##}, {offset.z:0.##} m off it (limit {tuning.standSnapRadius:0.##} m across, {tuning.standSnapHeight:0.##} m up)";
                return false;
            }

            if (!elevated && !NavMeshReach.CanWalk(heart, hit.position))
            {
                why = "its NavMesh is an island the settlement cannot walk to";
                return false;
            }

            stand = hit.position;
            return true;
        }

        /// <summary>The doorway itself, on the NavMesh and walkable from <paramref name="heart"/>: where a resident steps into the building.</summary>
        public static bool TryThreshold(Dwelling dwelling, Vector3 heart, out Vector3 threshold)
        {
            threshold = default;
            Vector3 doorway = DoorwayOf(dwelling).position;
            if (!NavMesh.SamplePosition(doorway, out NavMeshHit hit, ResidentTuning.Instance.doorThresholdSnap, NavMesh.AllAreas) ||
                !NavMeshReach.CanWalk(heart, hit.position))
                return false;

            threshold = hit.position;
            return true;
        }

        /// <summary>
        /// World height of the surface a sitter at <paramref name="point"/> sits on: the highest upward-facing collider
        /// under the point (a bench, a stool, the floor), ignoring bodies and triggers. The point's own height when
        /// there is none.
        /// </summary>
        public static float SeatSurfaceY(Vector3 point)
        {
            float probe = ResidentTuning.Instance.seatProbeHeight;
            RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * probe, Vector3.down, probe * 2f, ~0, QueryTriggerInteraction.Ignore);
            float surface = point.y;
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                if (hit.normal.y < FlatSurface || hit.collider.GetComponentInParent<AgentController>() != null) continue;
                if (found && hit.point.y <= surface) continue;
                surface = hit.point.y;
                found = true;
            }
            return surface;
        }

        /// <summary>The dwelling's door: its first SettlementEntrance, else the dwelling's own pivot facing out its front.</summary>
        public static Transform DoorwayOf(Dwelling dwelling)
        {
            SettlementEntrance door = dwelling.Door;
            return door != null ? door.transform : dwelling.transform;
        }

        public static Vector3 OutwardOf(Transform doorway) => Vector3.ProjectOnPlane(doorway.forward, Vector3.up).normalized;

        /// <summary>The hierarchy path of <paramref name="item"/> below <paramref name="root"/>: the building, then the spot.</summary>
        public static string PathBelow(Transform root, Transform item) =>
            item == null || item == root ? string.Empty
            : item.parent == root ? item.name
            : $"{PathBelow(root, item.parent)}/{item.name}";
    }
}
