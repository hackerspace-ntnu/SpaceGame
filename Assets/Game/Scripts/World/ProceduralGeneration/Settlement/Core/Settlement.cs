// Drop this on a GameObject, assign a SettlementConfig, right-click the header -> Generate.
// Scatters buildings/decorations/characters around this transform, seeded off its own position so
// the same config reused at a different spot produces a different (but reproducible) layout, and
// sculpts the terrain so buildings sit flat without leaving a hard flattened disc. Edit-time only,
// like every other generator in ProceduralGeneration/Settlement.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public class Settlement : MonoBehaviour
    {
        [SerializeField] private SettlementConfig config;

        // Lets a later Clear/regenerate undo the previous terrain edit exactly. Internal state, not
        // configuration, so it stays hidden and doesn't count against "one field to fill in".
        [SerializeField, HideInInspector]
        private List<SettlementTerrainSculptor.TerrainPatchBackup> terrainBackup = new();

        private const string GeneratedRootName = "Generated";
        private static readonly Vector2 DefaultFootprint = new Vector2(8f, 8f);

        protected SettlementConfig Config => config;

        private struct Placement { public Vector2 xz; public float radius; }
        private struct SpawnResult { public Vector2 worldXZ; public float baseY; public float radius; public GameObject instance; }

        [ContextMenu("Generate")]
        public void Generate()
        {
            if (config == null)
            {
                Debug.LogError($"[{GetType().Name}] No config assigned.", this);
                return;
            }

            Clear();

            int seed = SettlementPlacementUtil.SeedFromPosition(transform.position);
            var rng = new SettlementPlacementUtil.SeededRng(seed);

            Transform root = new GameObject(GeneratedRootName).transform;
            root.SetParent(transform, worldPositionStays: false);
            root.localPosition = Vector3.zero;

            // Everything this component places renders at exactly its own prefab's authored
            // scale, always -- never adjusted here. Cancel out whatever scale this Settlement's
            // own transform happens to carry (some scenes scale it up to size an under-authored
            // building kit) so it can never leak into a placed instance. A prefab that reads the
            // wrong size must be corrected in the prefab itself (as NomadSail_* was -- see
            // ArtPipeline.md), never compensated for by scaling the container it's placed under.
            Vector3 lossyScale = transform.lossyScale;
            root.localScale = new Vector3(1f / lossyScale.x, 1f / lossyScale.y, 1f / lossyScale.z);

            // Layer 1: buildings, spaced against only each other, by their real mesh footprint --
            // solid architecture needs the room. Nothing placed in a later layer respects this
            // footprint circle; later layers check the buildings' actual colliders instead (see
            // OverlapsAnySolid below), so one building's inflated clearance can never starve
            // decoration/character placement the way it used to.
            var buildingPlaced = new List<Placement>();
            var buildings = SpawnEntries(config.buildings, root, ref rng, buildingPlaced, buildingPlaced);
            var footprints = new List<SettlementTerrainSculptor.BuildingFootprint>(buildings.Count);
            var buildingRoots = new List<Transform>(buildings.Count);
            foreach (var b in buildings)
            {
                footprints.Add(new SettlementTerrainSculptor.BuildingFootprint
                {
                    worldPos = new Vector3(b.worldXZ.x, b.baseY, b.worldXZ.y),
                    radius = b.radius,
                    baseY = b.baseY,
                });
                if (b.instance != null) buildingRoots.Add(b.instance.transform);
            }

            terrainBackup = SettlementTerrainSculptor.Shape(
                transform.position, config.radius, config.blendDistance, config.flattenPadding,
                config.ambientNoiseAmplitude, config.ambientNoiseScale, seed, footprints);

            // Layer 2: decorations, spawned after the terrain sculpt so they land on the real final
            // surface. Kept clear of each other by a flat spacing value (config.decorationSpacing)
            // instead of a mesh-derived footprint circle -- a large sail-tent canopy is meant to be
            // able to sit close to, or even overlap, its neighbours, unlike a building. Buildings
            // alone get a real collision check (their actual colliders, found by walking each
            // spawned building's hierarchy): a tent may stand right up against a wall, just not
            // inside it. Grouped under its own transform purely for a readable hierarchy -- root
            // already carries no scale, so this doesn't need to cancel anything itself.
            Transform decorationsRoot = new GameObject("Decorations").transform;
            decorationsRoot.SetParent(root, worldPositionStays: false);
            var decorationPlaced = new List<Placement>();
            SpawnEntries(config.decorations, decorationsRoot, ref rng, decorationPlaced, decorationPlaced, buildingRoots, config.decorationSpacing);

            // Layer 3: characters, spawned last so they get whatever room buildings/decorations
            // left. Same treatment as decorations -- a flat spacing value (config.characterSpacing)
            // between characters instead of a building-sized footprint circle, so a crowd can stand
            // close together, plus a real collision check against buildings only. Decorations get no
            // check at all -- the tent kit ships single-sided, walkable-under canopies with nothing
            // solid to collide with (see ArtPipeline.md).
            Transform charactersRoot = new GameObject("Characters").transform;
            charactersRoot.SetParent(root, worldPositionStays: false);
            var characterPlaced = new List<Placement>();
            SpawnEntries(config.characters, charactersRoot, ref rng, characterPlaced, characterPlaced, buildingRoots, config.characterSpacing);

            Debug.Log($"[{GetType().Name}] Generated settlement under {root.name}.", root);
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            SettlementTerrainSculptor.Restore(terrainBackup);
            terrainBackup.Clear();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name != GeneratedRootName) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>Rotation for a freshly placed item at this local XZ offset from the settlement center. Base: a random axis-aligned yaw.</summary>
        protected virtual Quaternion GetSpawnRotation(Vector2 localXZ, ref SettlementPlacementUtil.SeededRng rng)
        {
            int step = Mathf.Clamp(Mathf.FloorToInt(rng.NextFloat01() * 4f), 0, 3);
            return Quaternion.Euler(0f, step * 90f, 0f);
        }

        /// <param name="spacingAgainst">Existing placements a new one may not land inside the footprint circle of.</param>
        /// <param name="registerInto">Where this call's own successful placements get recorded, for later callers to avoid.</param>
        /// <param name="avoidSolidsOf">If given, a placement is also rejected when it overlaps a real collider under any of these transforms -- a literal collision check, not a footprint circle.</param>
        /// <param name="flatClearance">If given, used as the full gap kept between two of this call's own placements instead of a mesh-derived footprint circle -- for categories (decorations, characters) that don't need building-sized spacing.</param>
        private List<SpawnResult> SpawnEntries(
            SettlementConfig.SpawnEntry[] entries, Transform root, ref SettlementPlacementUtil.SeededRng rng,
            List<Placement> spacingAgainst, List<Placement> registerInto, List<Transform> avoidSolidsOf = null,
            float? flatClearance = null)
        {
            var results = new List<SpawnResult>();
            if (entries == null) return results;

            foreach (var entry in entries)
            {
                if (entry.prefab == null) continue;
                for (int i = 0; i < entry.count; i++)
                {
                    if (TryPlace(entry.prefab, root, ref rng, spacingAgainst, registerInto, avoidSolidsOf, flatClearance, out SpawnResult result))
                        results.Add(result);
                }
            }
            return results;
        }

        private bool TryPlace(
            GameObject prefab, Transform root, ref SettlementPlacementUtil.SeededRng rng,
            List<Placement> spacingAgainst, List<Placement> registerInto, List<Transform> avoidSolidsOf,
            float? flatClearance, out SpawnResult result)
        {
            float clearance = flatClearance.HasValue
                ? flatClearance.Value * 0.5f
                : SettlementPlacementUtil.ClearanceRadius(prefab, config.minSpacing, DefaultFootprint);
            const int maxAttempts = 30;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                Vector2 localXZ = rng.NextPointInDisk(config.radius);
                if (IsTooCloseToOthers(localXZ, clearance, spacingAgainst)) continue;

                Vector3 worldXZ = transform.TransformPoint(new Vector3(localXZ.x, 0f, localXZ.y));
                if (!SettlementPlacementUtil.SampleGround(worldXZ, out float groundY)) continue;

                Quaternion rotation = GetSpawnRotation(localXZ, ref rng);
                Vector3 spawnPos = new Vector3(worldXZ.x, groundY, worldXZ.z);

                if (avoidSolidsOf != null && OverlapsAnySolid(spawnPos, clearance, avoidSolidsOf)) continue;

                GameObject go;
#if UNITY_EDITOR
                go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, root);
                go.transform.SetPositionAndRotation(spawnPos, rotation);
#else
                go = Instantiate(prefab, spawnPos, rotation, root);
#endif

                registerInto.Add(new Placement { xz = localXZ, radius = clearance });
                result = new SpawnResult { worldXZ = new Vector2(worldXZ.x, worldXZ.z), baseY = groundY, radius = clearance, instance = go };
                return true;
            }

            result = default;
            return false;
        }

        private static bool IsTooCloseToOthers(Vector2 xz, float radius, List<Placement> placed)
        {
            foreach (var p in placed)
            {
                float minDist = p.radius + radius;
                if ((p.xz - xz).sqrMagnitude < minDist * minDist) return true;
            }
            return false;
        }

        /// <summary>True if any real collider under one of <paramref name="solids"/> overlaps a sphere at <paramref name="worldPos"/> -- a literal collision test, unlike the footprint-circle spacing check.</summary>
        private static bool OverlapsAnySolid(Vector3 worldPos, float radius, List<Transform> solids)
        {
            Collider[] hits = Physics.OverlapSphere(worldPos, radius, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                foreach (var solid in solids)
                {
                    if (hit.transform.IsChildOf(solid)) return true;
                }
            }
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            if (config == null) return;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.5f);
            DrawCircle(transform.position, config.radius);
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.3f);
            DrawCircle(transform.position, config.radius + config.blendDistance);
        }

        private static void DrawCircle(Vector3 center, float radius, int segments = 48)
        {
            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float t = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(Mathf.Cos(t) * radius, 0f, Mathf.Sin(t) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
