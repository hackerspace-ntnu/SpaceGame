// The places a generated settlement's buildings and decorations brought along, as the generator checks
// them: where the walkable heart of the settlement is, where a resident stands at each dwelling's door, and
// which spots or doors no resident can walk to. Needs a NavMesh over the settlement — the throwaway one
// Generate bakes, or the world's at runtime.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public static class SettlementPlaces
    {
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
                else if (!WalkableSnap.TryReachable(spot.Position, heart, out _)) problems.Add($"{where} ({spot.Use.name}) cannot be walked to");
            }
            return problems;
        }

        /// <summary>The dwelling's door: its first SettlementEntrance, else the dwelling's own pivot facing out its front.</summary>
        public static Transform DoorwayOf(Dwelling dwelling)
        {
            SettlementEntrance door = dwelling.Door;
            return door != null ? door.transform : dwelling.transform;
        }

        public static Vector3 OutwardOf(Transform doorway) => Vector3.ProjectOnPlane(doorway.forward, Vector3.up).normalized;

        private static string PathBelow(Transform root, Transform item) =>
            item == null || item == root ? string.Empty
            : item.parent == root ? item.name
            : $"{PathBelow(root, item.parent)}/{item.name}";
    }
}
