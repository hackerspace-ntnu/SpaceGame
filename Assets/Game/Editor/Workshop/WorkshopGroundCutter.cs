// Digs the terrain out of a placed workshop's ship lane and levels an apron in front of its gate.
//
// The workshop's lane runs from the main room out to the gate at the main room's floor level, but a mountain placed
// on natural ground has the ground rising under it: in Chunk_4_3 the terrain stood up to 1.6 m over the lane floor
// and 2 m in front of the gate, burying the gate's lower pegs and leaving a step nobody can climb into the lane. This
// lowers the ground under the lane to just below its floor, levels it in front of the gate flush with the lane's
// mouth (out past the leaves' swing, the gear pile and the Shellback's stand), and blends back to the natural ground.
//
// Works on every workshop in the open scenes against every loaded terrain tile, so open the chunk scenes the
// workshop and its apron stand on first (the apron may cross into the next chunk). Writes the TerrainData assets;
// re-bake the world NavMesh afterwards.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class WorkshopGroundCutter
    {
        // Workshop-local, metres. The lane leaves the main room's wall at about 16 m; the gate's face is the GearDoor.
        private const float LaneStart = 14f;
        private const float LaneCentreZ = -3f;
        private const float LaneHalfWidth = 15f;
        // Below the lane's floor, so the ground never shows through it.
        private const float LaneSink = 0.25f;
        // In front of the gate: past the leaves' 14.6 m swing, the pile, and the Shellback 18 m out.
        private const float ApronLength = 30f;
        private const float ApronHalfWidth = 18f;
        private const float BlendDistance = 10f;
        // Where the lane's floor is measured, just inside the gate.
        private const float MouthProbeInset = 2f;
        private const float MouthProbeHeight = 3f;

        [MenuItem("Tools/SpaceGame/Workshop/Dig The Ground Out Of Placed Workshops")]
        private static void DigMenu() => Debug.Log("[Workshop] " + Dig());

        /// <summary>Dig every workshop in the open scenes out of the loaded terrain. Returns what was done.</summary>
        public static string Dig()
        {
            var lines = new List<string>();
            foreach (Workshop workshop in Object.FindObjectsByType<Workshop>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (EditorUtility.IsPersistent(workshop)) continue;
                lines.Add(DigOne(workshop));
            }
            return lines.Count == 0 ? "no workshop in the open scenes" : string.Join("\n", lines);
        }

        private static string DigOne(Workshop workshop)
        {
            Transform root = workshop.transform;
            GearDoor gate = GearDoor.GateOf(workshop.gameObject);
            if (gate == null) return $"{workshop.name}: no gate, nothing dug";

            float gateX = root.InverseTransformPoint(gate.transform.position).x;
            if (!TryMouthFloor(root, gateX, out float mouthY))
                return $"{workshop.name}: no lane floor found just inside the gate, nothing dug";

            var lane = Footprint(root, LaneStart, gateX, LaneCentreZ, LaneHalfWidth, mouthY - LaneSink);
            var apron = Footprint(root, gateX, gateX + ApronLength, 0f, ApronHalfWidth, mouthY);

            Vector3 centre = root.TransformPoint(new Vector3((LaneStart + gateX + ApronLength) * 0.5f, 0f, 0f));
            float radius = new Vector2((gateX + ApronLength - LaneStart) * 0.5f, ApronHalfWidth + Mathf.Abs(LaneCentreZ)).magnitude;
            List<SettlementTerrainSculptor.TerrainPatchBackup> touched = SettlementTerrainSculptor.Shape(
                centre, radius, BlendDistance, 0f, 0f, 0f, 0, new List<SettlementTerrainSculptor.BuildingFootprint> { lane, apron });

            var names = new List<string>();
            foreach (SettlementTerrainSculptor.TerrainPatchBackup patch in touched)
            {
                EditorUtility.SetDirty(patch.terrainData);
                AssetDatabase.SaveAssetIfDirty(patch.terrainData);
                names.Add(patch.terrainData.name);
            }

            return touched.Count == 0
                ? $"{workshop.name}: no loaded terrain under it. Open its chunk scenes first."
                : $"{workshop.name}: lane dug to {LaneSink} m under its floor, apron levelled at the lane's mouth " +
                  $"({mouthY:0.00} m) for {ApronLength} m; wrote {string.Join(", ", names)}. Re-bake the world NavMesh.";
        }

        /// <summary>The lane's floor just inside the gate, workshop-local: the workshop's own colliders only.</summary>
        private static bool TryMouthFloor(Transform root, float gateX, out float y)
        {
            Vector3 from = root.TransformPoint(new Vector3(gateX - MouthProbeInset, MouthProbeHeight, 0f));
            y = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(from, -root.up, MouthProbeHeight * 2f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.transform.IsChildOf(root) && !(hit.collider is TerrainCollider))
                    y = Mathf.Max(y, root.InverseTransformPoint(hit.point).y);
            return !float.IsNegativeInfinity(y);
        }

        private static SettlementTerrainSculptor.BuildingFootprint Footprint(Transform root, float fromX, float toX,
                                                                            float centreZ, float halfWidth, float localY)
        {
            Vector3 centre = root.TransformPoint(new Vector3((fromX + toX) * 0.5f, localY, centreZ));
            return new SettlementTerrainSculptor.BuildingFootprint
            {
                footprint = new SettlementFootprint(new Vector2(centre.x, centre.z), new Vector2(toX - fromX, 2f * halfWidth),
                                                    Quaternion.Euler(0f, root.eulerAngles.y, 0f)),
                baseY = centre.y,
            };
        }
    }
}
