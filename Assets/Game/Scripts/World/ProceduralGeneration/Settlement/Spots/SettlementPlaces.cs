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
        // How far from a named heart the NavMesh may lie, metres.
        private const float HeartSnap = 3f;

        /// <summary>
        /// A point in the largest walkable area joining the settlement's doors and spots — the streets and
        /// yards, not an island on a roof. <paramref name="fallback"/> when nothing is on the NavMesh.
        /// </summary>
        public static Vector3 FindHeart(Transform generated, IReadOnlyList<float> doorDistances, Vector3 fallback)
        {
            // A prop that names the open ground (a colony's rover pad) settles it: the biggest group of places may be a roof.
            SettlementHeart named = generated.GetComponentInChildren<SettlementHeart>();
            if (named != null && NavMesh.SamplePosition(named.transform.position, out NavMeshHit onGround, HeartSnap, NavMesh.AllAreas))
                return onGround.position;

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

        /// <summary>The world bounds of each building under a settlement's generated root: everything but its decorations, characters and streets.</summary>
        public static List<Bounds> BuildingBounds(Transform generated)
        {
            var footprints = new List<Bounds>();
            foreach (Transform child in generated)
            {
                if (child.name is "Decorations" or "Characters" or "Streets") continue;
                // Not a particle system's: its bounds sit at the world origin until it has run, and one such renderer (an airlock's mist) turns a
                // building into a box a kilometre across.
                Bounds box = default;
                bool any = false;
                foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>())
                {
                    if (renderer is ParticleSystemRenderer) continue;
                    if (any) box.Encapsulate(renderer.bounds);
                    else (box, any) = (renderer.bounds, true);
                }
                if (any) footprints.Add(box);
            }
            return footprints;
        }

        /// <summary>How many spots of a sleeping use (a bed) the dwelling holds.</summary>
        public static int BedSpotsOf(Dwelling dwelling)
        {
            int count = 0;
            foreach (SettlementSpot spot in dwelling.GetComponentsInChildren<SettlementSpot>())
                if (spot.Use != null && spot.Use.sleeps) count++;
            return count;
        }

        /// <summary>One line per dwelling or spot a resident could not use, naming it — empty when all is well.</summary>
        public static List<string> Problems(Transform generated, Vector3 heart, IReadOnlyList<float> doorDistances)
        {
            var problems = new List<string>();
            foreach (Dwelling dwelling in generated.GetComponentsInChildren<Dwelling>())
            {
                if (dwelling.Door == null) problems.Add($"{dwelling.name} has no SettlementEntrance — its door is its own pivot");
                if (!TryDoorStand(dwelling, heart, doorDistances, out _)) problems.Add($"{dwelling.name}: nothing walkable out from its door");

                int bedSpots = BedSpotsOf(dwelling);
                if (bedSpots > 0 && bedSpots != dwelling.Beds)
                    problems.Add($"{dwelling.name} has {dwelling.Beds} beds but {bedSpots} bed spots, so residents share a bed or one lies unused");
            }
            var sitSpots = new List<SettlementSpot>();
            foreach (SettlementSpot spot in generated.GetComponentsInChildren<SettlementSpot>())
            {
                string where = PathBelow(generated, spot.transform);
                if (spot.Use == null) problems.Add($"{where} has no SpotUse");
                else if (!TryStand(spot.Position, spot.Use.elevated, heart, out _, out string why))
                    problems.Add($"{where} ({spot.Use.name}) is unusable: {why}");
                else if (spot.Use.seated) sitSpots.Add(spot);
            }

            float reach = ResidentTuning.Instance.seatReach;
            List<int> seatless = SeatlessSpots(sitSpots.ConvertAll(spot => spot.Position), generated.GetComponentsInChildren<Seat>(), reach);
            foreach (int i in seatless)
                problems.Add($"{PathBelow(generated, sitSpots[i].transform)} ({sitSpots[i].Use.name}) is a sit with no free Seat within {reach:0.#} m, so nobody will sit there");
            return problems;
        }

        /// <summary>
        /// Which of the sit spots have no seat of their own: each seat serves ONE spot (the nearest that wants it, in order) and only
        /// within <paramref name="reach"/> metres across the ground, so two spots over a single seat leave the second without.
        /// Indices into <paramref name="spots"/>.
        /// </summary>
        public static List<int> SeatlessSpots(IReadOnlyList<Vector3> spots, IReadOnlyList<Seat> seats, float reach)
        {
            var taken = new HashSet<Seat>();
            var seatless = new List<int>();
            for (int i = 0; i < spots.Count; i++)
            {
                Seat nearest = null;
                float nearestSqr = reach * reach;
                foreach (Seat seat in seats)
                {
                    if (seat == null || taken.Contains(seat)) continue;

                    float sqr = seat.FlatSqrDistanceTo(spots[i]);
                    if (sqr > nearestSqr) continue;
                    nearest = seat;
                    nearestSqr = sqr;
                }

                if (nearest != null) taken.Add(nearest);
                else seatless.Add(i);
            }
            return seatless;
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
