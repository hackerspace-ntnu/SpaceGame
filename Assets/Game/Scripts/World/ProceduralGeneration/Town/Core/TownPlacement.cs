// Putting a prefab on the ground: how wide is it, how much room does it need, where is the ground
// under it, and does it need a pad to stand on.
//
// Lifted verbatim out of RobotSettlementGenerator when TownGenerator became a second caller. None
// of it draws a random number — that is what makes the extraction safe, and it is why the Clanker
// town still generates byte-identically afterwards. Keep it that way: anything here that needs
// randomness belongs in TownLayout, which owns the one seeded sequence.
//
// An instance rather than a static class, because of the footprint cache. Walking every MeshFilter
// in a prefab is the expensive part of placing a town, and the cache's lifetime has to match the
// generator's: a static cache would go on serving a stale footprint after someone edited the prefab
// mid-session, which is a wrong answer that looks like a correct one.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Towns
{
    public sealed class TownPlacement
    {
        private readonly LayerMask terrainMask;
        private readonly float defaultFootprint;
        private readonly float minStructureSpacing;
        private readonly float buildingPadding;
        private readonly float padThreshold;
        private readonly float padOverhang;
        private readonly Material padMaterial;

        private readonly Dictionary<GameObject, Vector2> footprintCache = new();

        public TownPlacement(LayerMask terrainMask, float defaultFootprint, float minStructureSpacing,
                             float buildingPadding, float padThreshold, float padOverhang,
                             Material padMaterial)
        {
            this.terrainMask = terrainMask;
            this.defaultFootprint = defaultFootprint;
            this.minStructureSpacing = minStructureSpacing;
            this.buildingPadding = buildingPadding;
            this.padThreshold = padThreshold;
            this.padOverhang = padOverhang;
            this.padMaterial = padMaterial;
        }

        /// <summary>
        /// The prefab's XZ extents, measured by walking every MeshFilter into the prefab root's
        /// local space. Falls back to the configured default for a prefab with no meshes at all —
        /// an empty spawner root, say — because a zero footprint would let it overlap anything.
        /// </summary>
        public Vector2 Footprint(GameObject prefab)
        {
            if (prefab == null) return new Vector2(defaultFootprint, defaultFootprint);
            if (footprintCache.TryGetValue(prefab, out Vector2 cached)) return cached;

            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0)
            {
                footprintCache[prefab] = new Vector2(defaultFootprint, defaultFootprint);
                return footprintCache[prefab];
            }

            Bounds? combined = null;
            Transform prefabRoot = prefab.transform;
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                Bounds local = mf.sharedMesh.bounds;
                Vector3 c = local.center;
                Vector3 e = local.extents;
                Bounds wb = new Bounds();
                bool init = false;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3(
                        (i & 1) == 0 ? -e.x : e.x,
                        (i & 2) == 0 ? -e.y : e.y,
                        (i & 4) == 0 ? -e.z : e.z);
                    Vector3 worldCorner = mf.transform.TransformPoint(corner);
                    Vector3 inRoot = prefabRoot.InverseTransformPoint(worldCorner);
                    if (!init) { wb = new Bounds(inRoot, Vector3.zero); init = true; }
                    else wb.Encapsulate(inRoot);
                }
                if (combined == null) combined = wb;
                else { var cb = combined.Value; cb.Encapsulate(wb); combined = cb; }
            }

            Vector2 size = combined.HasValue
                ? new Vector2(combined.Value.size.x, combined.Value.size.z)
                : new Vector2(defaultFootprint, defaultFootprint);

            footprintCache[prefab] = size;
            return size;
        }

        /// <summary>
        /// Half the room this prefab needs, centre to centre.
        ///
        /// The LONGEST XZ extent, not the average: a building is yawed after it is placed, and a
        /// long thin shed that fitted its slot east-west overlaps its neighbour once it turns.
        /// </summary>
        public float ClearanceRadius(GameObject prefab)
        {
            Vector2 fp = Footprint(prefab);
            float longest = Mathf.Max(fp.x, fp.y);
            float required = longest + buildingPadding;
            return Mathf.Max(required, minStructureSpacing) * 0.5f;
        }

        /// <summary>
        /// Ground height under a world XZ, by raycast from 500 m up.
        ///
        /// False means nothing was hit, and the caller must skip the placement rather than drop the
        /// prefab at the fallback height — a building at y=0 in a world whose terrain sits at 100 m
        /// is buried, silently. In the editor this most often means the chunk's terrain collider is
        /// not loaded.
        /// </summary>
        public bool SampleGround(Vector3 worldXZ, out float groundY)
        {
            Vector3 origin = new Vector3(worldXZ.x, worldXZ.y + 500f, worldXZ.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2000f, terrainMask,
                                QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                return true;
            }

            groundY = worldXZ.y;
            return false;
        }

        /// <summary>
        /// Put a slab under a building whose ground dips away on one side, so it does not stand on
        /// air. Does nothing when the dip is under the threshold, which is most placements.
        /// </summary>
        public void AddFoundationPad(Transform building, Vector3 worldXZ, float baseY, Transform root,
                                     Vector2 prefabFootprint, Quaternion buildingRot)
        {
            float padX = prefabFootprint.x + padOverhang * 2f;
            float padZ = prefabFootprint.y + padOverhang * 2f;
            float halfX = padX * 0.5f;
            float halfZ = padZ * 0.5f;

            Vector3 right = buildingRot * Vector3.right;
            Vector3 forward = buildingRot * Vector3.forward;

            Vector3[] samples =
            {
                worldXZ,
                worldXZ + right * +halfX + forward * +halfZ,
                worldXZ + right * -halfX + forward * +halfZ,
                worldXZ + right * +halfX + forward * -halfZ,
                worldXZ + right * -halfX + forward * -halfZ,
            };

            float minY = baseY;
            for (int i = 0; i < samples.Length; i++)
                if (SampleGround(samples[i], out float y) && y < minY)
                    minY = y;

            float dip = baseY - minY;
            if (dip < padThreshold) return;

            float padTop = baseY + 0.02f;
            float padBottom = minY - 0.1f;
            float padHeight = padTop - padBottom;
            float padCenterY = (padTop + padBottom) * 0.5f;

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = $"FoundationPad_{building.name}";
            pad.transform.SetParent(root, worldPositionStays: true);
            pad.transform.SetPositionAndRotation(new Vector3(worldXZ.x, padCenterY, worldXZ.z), buildingRot);
            pad.transform.localScale = new Vector3(padX, padHeight, padZ);

            var renderer = pad.GetComponent<MeshRenderer>();
            if (renderer)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (padMaterial) renderer.sharedMaterial = padMaterial;
            }
        }

        /// <summary>World-space bounds of every mesh under <paramref name="go"/>. False when it has none.</summary>
        public static bool TryGetWorldMeshBounds(GameObject go, out Bounds bounds)
        {
            var filters = go.GetComponentsInChildren<MeshFilter>();
            bounds = default;
            bool init = false;
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                Bounds local = mf.sharedMesh.bounds;
                Vector3 c = local.center;
                Vector3 e = local.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3(
                        (i & 1) == 0 ? -e.x : e.x,
                        (i & 2) == 0 ? -e.y : e.y,
                        (i & 4) == 0 ? -e.z : e.z);
                    Vector3 world = mf.transform.TransformPoint(corner);
                    if (!init) { bounds = new Bounds(world, Vector3.zero); init = true; }
                    else bounds.Encapsulate(world);
                }
            }

            return init;
        }

        /// <summary>
        /// Terrain height from the heightmap rather than by raycast.
        ///
        /// Deliberately separate from <see cref="SampleGround"/>: this reads only the Terrain, so it
        /// cannot be fooled by an already-placed building, which is what a rock embedding test needs.
        /// It also cannot see a terrain FEATURE — mesas are meshes spawned at bake time, not part of
        /// the heightmap — so anything choosing ground in the editor must consult
        /// TerrainFeatureSpawner.Area as well.
        /// </summary>
        public static bool TryGetTerrainHeight(Vector3 worldPos, out float height)
        {
            Terrain[] terrains = Terrain.activeTerrains;
            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null) continue;

                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                float relX = worldPos.x - origin.x;
                float relZ = worldPos.z - origin.z;
                if (relX < 0f || relZ < 0f || relX > size.x || relZ > size.z) continue;

                height = origin.y + terrain.SampleHeight(worldPos);
                return true;
            }

            height = 0f;
            return false;
        }
    }
}
