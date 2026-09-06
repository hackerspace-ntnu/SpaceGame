// Stands the Crucible on the world, a short walk east of where the ship comes down.
//
// The room is a 9 m pit and the world is solid terrain, so it cannot simply be dropped in at ground
// level — it would be buried. It is raised instead until its lava clears the highest ground under
// its footprint, and this file builds the mesa that carries it: a walkway ring around the rim, a
// skirt down to the terrain so there is nothing to see under the lava, and one ramp up. The rim is a
// closed ring, so one ramp serves both players; they walk round it to their own side.
//
// Everything here is the GROUND the room stands on and none of it is the room, which is why it is
// not in CrucibleBuilder: the prefab is the same wherever it is placed, and this is only true of
// this spot in this world.
//
// Re-running is safe and is the intended workflow.
//
// Re-run from: Tools > SpaceGame > Place Crucible Near Ship
using System.Collections.Generic;
using SpaceGame.Gameplay;
using SpaceGame.World;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    public static class CruciblePlacement
    {
        private const string PersistentScenePath = "Assets/Game/Scenes/world/persistentScene.unity";
        private const string RoomPrefabPath = "Assets/Game/Prefabs/Gameplay/Crucible/CrucibleRoom.prefab";
        private const string StonePath = "Assets/Game/Prefabs/Gameplay/Crucible/CrucibleStone.mat";

        private const string RoomObjectName = "Crucible";
        private const string AccessObjectName = "CrucibleAccess";

        // Where it goes is searched for, not authored. The ground around the landing site is broken
        // enough that a fixed offset lands the mesa on a 30 m cliff — measured, not guessed — and a
        // mesa on a cliff is a 40 m tower on one side. So: rings around the ship, and the flattest
        // footprint wins, with a mild pull back towards the distance actually asked for.
        private const float PreferredDistance = 40f;
        private const float MinDistance = 30f;
        private const float MaxDistance = 90f;
        private const float DistanceStep = 5f;
        private const float BearingStep = 15f;

        /// <summary>Spread-metres a metre of extra walking is worth, when scoring a site.</summary>
        private const float DistancePenalty = 0.1f;

        // ── The mesa, in metres ────────────────────────────────────────────────

        private const float DeckWidth = 3f;        // walkway outside the rim
        private const float DeckThickness = 0.4f;
        private const float SkirtThickness = 0.4f;
        private const float SkirtBury = 3f;        // how far the skirt sinks into the terrain
        private const float LavaClearance = 0.5f;  // lava sits this far above the highest ground
        private const float RampAngle = 18f;       // shallow: a rigidbody player walks it, not slides
        private const float RampWidth = 4f;
        private const float RampThickness = 0.4f;
        private const float RampBury = 2f;         // how far past the ground the ramp foot runs on
        private const float RampMaxRun = 80f;
        private const float RampStep = 0.5f;       // resolution of the search for where it lands

        // Derived from the room the builder makes, so the two cannot drift apart.
        private const float RimOuterX = CrucibleBuilder.PitLength * 0.5f + CrucibleBuilder.RimThickness;
        private const float RimOuterZ = CrucibleBuilder.PitWidth * 0.5f + CrucibleBuilder.RimThickness;
        private const float DeckOuterX = RimOuterX + DeckWidth;
        private const float DeckOuterZ = RimOuterZ + DeckWidth;

        /// <summary>Local Y of the walking surface: the rim stands proud of it as a waist-high wall.</summary>
        private const float DeckY = -CrucibleBuilder.RimHeight;

        [MenuItem("Tools/SpaceGame/Place Crucible Near Ship")]
        public static void Place()
        {
            var room = AssetDatabase.LoadAssetAtPath<GameObject>(RoomPrefabPath);
            if (room == null)
            {
                Debug.LogError($"[Crucible] No room prefab at {RoomPrefabPath}. Run " +
                               "Tools > SpaceGame > Build Crucible (cell + room) first.");
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(PersistentScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Additive);

            try
            {
                if (!TryPlace(scene, room)) return;

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static bool TryPlace(Scene scene, GameObject roomPrefab)
        {
            if (!TryFindSpawnPoint(scene, out Vector3 spawn))
            {
                Debug.LogError("[Crucible] No SpawnPoint in the persistent scene to place against.");
                return false;
            }

            WorldStreamingConfig config = FindConfig(scene);
            if (config == null)
            {
                Debug.LogError("[Crucible] No WorldStreamingConfig — cannot find the terrain to " +
                               "stand the room on.");
                return false;
            }

            // Everything the search and the ramp could reach: the only ground worth loading.
            float span = (MaxDistance + DeckOuterX + RampMaxRun) * 2f;
            var reach = new Bounds(spawn, new Vector3(span, 1f, span));

            using (var ground = new TerrainSampler(config, reach))
            {
                if (!ground.Ready)
                {
                    Debug.LogError($"[Crucible] No terrain around {spawn}. The room needs ground to " +
                                   "stand on and there is none there.");
                    return false;
                }

                Vector3 centre = FlattestSite(spawn, ground);

                Footprint(centre, out float highest, out float lowest, ground);

                // High enough that the lava clears the worst of the ground beneath it. Measured
                // against the HIGHEST sample rather than the centre: one knoll under a corner is
                // enough to poke through the pit floor and into the room.
                float rootY = highest + LavaClearance - (CrucibleBuilder.LavaY - 0.1f);

                Clear(scene);

                GameObject placed = InstantiateRoom(scene, roomPrefab, new Vector3(centre.x, rootY, centre.z));
                BuildAccess(scene, new Vector3(centre.x, rootY, centre.z), lowest, ground);

                StampSceneHash(placed);

                Debug.Log($"[Crucible] Placed at {placed.transform.position}, " +
                          $"{Vector2.Distance(new Vector2(centre.x, centre.z), new Vector2(spawn.x, spawn.z)):0} " +
                          $"m from the spawn point. Deck at {rootY + DeckY:0.0} m, ground under it " +
                          $"{lowest:0.0}–{highest:0.0} m.", placed);
            }

            return true;
        }

        // ── Where it goes ──────────────────────────────────────────────────────

        private static bool TryFindSpawnPoint(Scene scene, out Vector3 position)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var point = root.GetComponentInChildren<SpawnPoint>(true);
                if (point == null) continue;

                position = point.transform.position;
                return true;
            }

            position = default;
            return false;
        }

        private static WorldStreamingConfig FindConfig(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var streamer = root.GetComponentInChildren<WorldStreamer>(true);
                if (streamer?.Config != null) return streamer.Config;
            }

            return null;
        }

        /// <summary>
        /// The least broken ground within walking distance of the ship.
        ///
        /// <para>
        /// Flatness is what is being bought: the mesa is raised to clear the HIGHEST ground under
        /// its footprint, so every metre of spread is a metre of skirt left standing proud on the
        /// low side. A cheap ring search beats an authored offset here because the answer changes
        /// whenever the terrain does.
        /// </para>
        /// </summary>
        private static Vector3 FlattestSite(Vector3 spawn, TerrainSampler ground)
        {
            Vector3 best = spawn + new Vector3(PreferredDistance, 0f, 0f);
            float bestScore = float.PositiveInfinity;

            for (float distance = MinDistance; distance <= MaxDistance; distance += DistanceStep)
            for (float bearing = 0f; bearing < 360f; bearing += BearingStep)
            {
                float radians = bearing * Mathf.Deg2Rad;
                var candidate = new Vector3(spawn.x + Mathf.Cos(radians) * distance, 0f,
                                            spawn.z + Mathf.Sin(radians) * distance);

                Footprint(candidate, out float highest, out float lowest, ground);

                float score = (highest - lowest)
                            + Mathf.Abs(distance - PreferredDistance) * DistancePenalty;

                if (score >= bestScore) continue;

                bestScore = score;
                best = candidate;
            }

            return best;
        }

        /// <summary>The highest and lowest ground anywhere under the mesa's outline.</summary>
        private static void Footprint(Vector3 centre, out float highest, out float lowest,
                                      TerrainSampler ground)
        {
            highest = float.NegativeInfinity;
            lowest = float.PositiveInfinity;

            const int Steps = 6;

            for (int ix = 0; ix <= Steps; ix++)
            for (int iz = 0; iz <= Steps; iz++)
            {
                float x = Mathf.Lerp(-DeckOuterX, DeckOuterX, ix / (float)Steps);
                float z = Mathf.Lerp(-DeckOuterZ, DeckOuterZ, iz / (float)Steps);

                float y = ground.HeightAt(centre.x + x, centre.z + z);

                highest = Mathf.Max(highest, y);
                lowest = Mathf.Min(lowest, y);
            }
        }

        // ── What gets built ────────────────────────────────────────────────────

        /// <summary>
        /// Remove what a previous run left, so re-running replaces rather than stacks.
        /// </summary>
        private static void Clear(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == RoomObjectName || root.name == AccessObjectName)
                    Object.DestroyImmediate(root);
        }

        /// <summary>
        /// The room goes in as a scene ROOT rather than under a holder.
        ///
        /// <para>
        /// It carries a NetworkObject, and nesting one under a plain transform is a trap this
        /// project has already paid for once. Two roots side by side cost nothing and avoid it.
        /// </para>
        /// </summary>
        private static GameObject InstantiateRoom(Scene scene, GameObject prefab, Vector3 at)
        {
            var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            placed.name = RoomObjectName;
            placed.transform.position = at;

            return placed;
        }

        /// <summary>
        /// Record the instance's own <c>GlobalObjectIdHash</c> as a prefab-instance override.
        ///
        /// <para>
        /// NGO gives a scene-placed NetworkObject a hash of its own in <c>OnValidate</c>, and that
        /// value reaches the scene file ONLY if it is registered as a property modification —
        /// <c>SaveScene</c> writes nothing for a prefab instance otherwise, returns true, and leaves
        /// the in-memory value looking correct. The instance then inherits the prefab's hash, which
        /// is how two of these end up claiming one key and all but one stop spawning.
        /// </para>
        /// </summary>
        private static void StampSceneHash(GameObject placed)
        {
            var netObj = placed.GetComponent<NetworkObject>();
            if (netObj == null) return;

            EditorUtility.SetDirty(netObj);
            PrefabUtility.RecordPrefabInstancePropertyModifications(netObj);

            foreach (PropertyModification change in PrefabUtility.GetPropertyModifications(placed))
                if (change.propertyPath == "GlobalObjectIdHash") return;

            Debug.LogError("[Crucible] The room's GlobalObjectIdHash was not recorded as a scene " +
                           "override. It will inherit the prefab's hash and may not spawn.", placed);
        }

        private static void BuildAccess(Scene scene, Vector3 root, float lowestGround,
                                        TerrainSampler ground)
        {
            var access = new GameObject(AccessObjectName);
            SceneManager.MoveGameObjectToScene(access, scene);
            access.transform.position = root;

            Material stone = Stone();

            BuildDeck(access.transform, stone);
            BuildSkirt(access.transform, root.y, lowestGround, stone);
            BuildRamp(access.transform, root, ground, stone);
        }

        /// <summary>The walkway: a ring outside the rim, so the rim reads as a parapet on it.</summary>
        private static void BuildDeck(Transform parent, Material stone)
        {
            float spanX = DeckOuterX * 2f;
            float centreZ = (RimOuterZ + DeckOuterZ) * 0.5f;
            float centreX = (RimOuterX + DeckOuterX) * 0.5f;
            float y = DeckY - DeckThickness * 0.5f;

            // North and south run the full width and own the corners; east and west fill between.
            Slab(parent, "Deck_North", new Vector3(0f, y, centreZ),
                 new Vector3(spanX, DeckThickness, DeckWidth), stone);
            Slab(parent, "Deck_South", new Vector3(0f, y, -centreZ),
                 new Vector3(spanX, DeckThickness, DeckWidth), stone);
            Slab(parent, "Deck_East", new Vector3(centreX, y, 0f),
                 new Vector3(DeckWidth, DeckThickness, RimOuterZ * 2f), stone);
            Slab(parent, "Deck_West", new Vector3(-centreX, y, 0f),
                 new Vector3(DeckWidth, DeckThickness, RimOuterZ * 2f), stone);
        }

        /// <summary>
        /// The outside face, from the deck down into the terrain. Hollow on purpose: a solid block
        /// would fill the pit it is holding up.
        /// </summary>
        private static void BuildSkirt(Transform parent, float rootY, float lowestGround, Material stone)
        {
            float bottom = lowestGround - SkirtBury - rootY;
            float height = DeckY - DeckThickness - bottom;
            float y = bottom + height * 0.5f;

            float x = DeckOuterX + SkirtThickness * 0.5f;
            float z = DeckOuterZ + SkirtThickness * 0.5f;
            float spanX = (x + SkirtThickness * 0.5f) * 2f;
            float spanZ = DeckOuterZ * 2f;

            // Split, not solid: the ramp comes out through the north face, and a wall across its
            // mouth is a ramp to a dead end.
            float gap = RampWidth + 1f;
            float segment = (spanX - gap) * 0.5f;
            float offset = (gap + segment) * 0.5f;

            Slab(parent, "Skirt_NorthWest", new Vector3(-offset, y, z),
                 new Vector3(segment, height, SkirtThickness), stone);
            Slab(parent, "Skirt_NorthEast", new Vector3(offset, y, z),
                 new Vector3(segment, height, SkirtThickness), stone);
            Slab(parent, "Skirt_South", new Vector3(0f, y, -z),
                 new Vector3(spanX, height, SkirtThickness), stone);
            Slab(parent, "Skirt_East", new Vector3(x, y, 0f),
                 new Vector3(SkirtThickness, height, spanZ), stone);
            Slab(parent, "Skirt_West", new Vector3(-x, y, 0f),
                 new Vector3(SkirtThickness, height, spanZ), stone);
        }

        /// <summary>
        /// One ramp, off the north deck.
        ///
        /// <para>
        /// Its length is not authored: it is walked outwards at the ramp's own pitch until the
        /// surface meets the terrain, so it lands on whatever the ground is doing out there rather
        /// than on an assumption that it is flat.
        /// </para>
        /// </summary>
        private static void BuildRamp(Transform parent, Vector3 root, TerrainSampler ground,
                                      Material stone)
        {
            float pitch = Mathf.Tan(RampAngle * Mathf.Deg2Rad);
            float topY = root.y + DeckY;
            float run = RampMaxRun;
            bool landed = false;

            for (float d = RampStep; d <= RampMaxRun; d += RampStep)
            {
                float surface = topY - d * pitch;
                if (surface > ground.HeightAt(root.x, root.z + DeckOuterZ + d)) continue;

                run = d + RampBury;
                landed = true;
                break;
            }

            if (!landed)
                Debug.LogWarning($"[Crucible] The ramp fell {RampMaxRun} m north without meeting " +
                                 "ground. The deck may not be reachable on foot.");

            float drop = run * pitch;
            float length = Mathf.Sqrt(run * run + drop * drop);

            // Sunk by its own half-thickness along the slope normal, so the walking surface — not
            // the box's centre line — comes out level with the deck.
            float sink = RampThickness * 0.5f / Mathf.Cos(RampAngle * Mathf.Deg2Rad);

            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "Ramp";
            ramp.transform.SetParent(parent, false);
            ramp.transform.localPosition =
                new Vector3(0f, DeckY - drop * 0.5f - sink, DeckOuterZ + run * 0.5f);
            ramp.transform.localRotation = Quaternion.Euler(RampAngle, 0f, 0f);
            ramp.transform.localScale = new Vector3(RampWidth, RampThickness, length);

            ramp.GetComponent<MeshRenderer>().sharedMaterial = stone;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static GameObject Slab(Transform parent, string name, Vector3 position,
                                       Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;

            return go;
        }

        /// <summary>
        /// One shared material as an asset, rather than a new one per slab.
        ///
        /// <para>
        /// A <c>new Material(...)</c> handed to an object in a scene is serialised INTO that scene,
        /// so building this rig that way would add a dozen nameless materials to the persistent
        /// scene file on every run.
        /// </para>
        /// </summary>
        private static Material Stone()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(StonePath);
            if (existing != null) return existing;

            var created = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.34f, 0.32f, 0.29f),
            };

            AssetDatabase.CreateAsset(created, StonePath);

            return created;
        }

        /// <summary>
        /// Terrain heights, from chunk scenes opened only for as long as the question takes.
        ///
        /// <para>
        /// The world's terrain lives in streamed chunk scenes, not in the persistent scene, so at
        /// edit time there is no terrain loaded to ask. This opens the chunks the footprint touches,
        /// answers, and closes them again — leaving whatever the user had open untouched.
        /// </para>
        /// </summary>
        private sealed class TerrainSampler : System.IDisposable
        {
            private readonly List<Scene> opened = new();
            private readonly List<Terrain> terrains = new();

            public TerrainSampler(WorldStreamingConfig config, Bounds reach)
            {
                foreach (ChunkInfo chunk in config.chunks)
                {
                    if (!chunk.hasTerrain || string.IsNullOrEmpty(chunk.scenePath)) continue;

                    // Flattened to XZ: the chunk's recorded bounds are only as tall as its terrain,
                    // and a reach box of the wrong height would reject every one of them.
                    if (!Flat(chunk.worldBounds).Intersects(Flat(reach))) continue;

                    Scene scene = SceneManager.GetSceneByPath(chunk.scenePath);

                    if (!scene.isLoaded)
                    {
                        scene = EditorSceneManager.OpenScene(chunk.scenePath, OpenSceneMode.Additive);
                        opened.Add(scene);
                    }

                    foreach (GameObject root in scene.GetRootGameObjects())
                        terrains.AddRange(root.GetComponentsInChildren<Terrain>(true));
                }
            }

            public bool Ready => terrains.Count > 0;

            private static Bounds Flat(Bounds bounds)
            {
                var flat = bounds;
                flat.center = new Vector3(flat.center.x, 0f, flat.center.z);
                flat.size = new Vector3(flat.size.x, 1f, flat.size.z);

                return flat;
            }

            /// <summary>
            /// The ground at an XZ, or the lowest terrain base if nothing covers it — an honest
            /// floor rather than a zero that would silently sink the room to the world origin.
            /// </summary>
            public float HeightAt(float x, float z)
            {
                var at = new Vector3(x, 0f, z);
                float fallback = float.PositiveInfinity;

                foreach (Terrain terrain in terrains)
                {
                    Vector3 origin = terrain.transform.position;
                    Vector3 size = terrain.terrainData.size;

                    fallback = Mathf.Min(fallback, origin.y);

                    if (x < origin.x || x > origin.x + size.x) continue;
                    if (z < origin.z || z > origin.z + size.z) continue;

                    return origin.y + terrain.SampleHeight(at);
                }

                return float.IsPositiveInfinity(fallback) ? 0f : fallback;
            }

            public void Dispose()
            {
                foreach (Scene scene in opened) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
