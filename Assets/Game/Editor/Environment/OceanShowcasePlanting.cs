using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Zone = SpaceGame.EditorTools.Environment.OceanShowcaseTerrain.Zone;

namespace SpaceGame.EditorTools.Environment
{
    /// <summary>
    /// Plants the ocean showcase with every aquatic prefab (AquaticVegetation.md) as terrain trees — instanced,
    /// far cheaper per plant than GameObjects — except the floating ones, which are GameObjects on the water
    /// line. Each species grows in its own zones, depth band and kind of ground, in noise-shaped patches
    /// with gaps between them, so the bed reads as a place rather than a catalogue.
    /// </summary>
    internal static class OceanShowcasePlanting
    {
        private const string PrefabDir = "Assets/Game/Prefabs/Environment/Nature/Vegetation/Aquatic/";
        private const int Seed = 4711;

        // Kelp is sized for the water it stands in: scaled to put its canopy on the surface, within these bounds.
        private const float MinSurfaceScale = 0.85f, MaxSurfaceScale = 1.12f;
        private const float MaxSlopeDegrees = 30f;
        // Side of the spatial-hash buckets the spacing test looks plants up in.
        private const float Bucket = 16f;

        private enum Ground { Any, Hard, Soft }

        private readonly struct Rule
        {
            public readonly string Prefab;
            public readonly Zone[] Zones;
            public readonly float PerHectare, MinDepth, MaxDepth, Spacing, PatchCut;
            public readonly Ground Ground;
            public readonly bool ReachesSurface, Floats;

            public Rule(string prefab, float perHectare, float minDepth, float maxDepth, float spacing, Ground ground,
                        float patchCut, params Zone[] zones)
                : this(prefab, perHectare, minDepth, maxDepth, spacing, ground, patchCut, false, false, zones) { }

            public Rule(string prefab, float perHectare, float minDepth, float maxDepth, float spacing, Ground ground,
                        float patchCut, bool reachesSurface, bool floats, params Zone[] zones)
            {
                Prefab = prefab; PerHectare = perHectare; MinDepth = minDepth; MaxDepth = maxDepth; Spacing = spacing;
                Ground = ground; PatchCut = patchCut; ReachesSurface = reachesSurface; Floats = floats; Zones = zones;
            }
        }

        // prefab, plants per hectare inside its patches, depth band (m, negative = above water), spacing (m),
        // ground, patch cut (0 = everywhere, 0.6 = small scattered patches), zones.
        private static readonly Rule[] Rules =
        {
            // Shore: salt marsh above the tide line, seaweeds on the rocks, drift on the sand.
            new Rule("Plant_Glasswort_A", 14f, -1.5f, -0.2f, 2.5f, Ground.Soft, 0.45f, Zone.Land, Zone.Intertidal),
            new Rule("Plant_Cordgrass_A", 16f, -2.0f, -0.3f, 3f, Ground.Soft, 0.4f, Zone.Land, Zone.Intertidal),
            new Rule("Plant_Wrackmat_A", 5f, -0.8f, 0.3f, 8f, Ground.Soft, 0.3f, Zone.Land, Zone.Intertidal),
            new Rule("Plant_Seapalm_A", 10f, -0.8f, 0.8f, 4f, Ground.Hard, 0.4f, Zone.Intertidal),
            new Rule("Plant_Wrack_A", 16f, -0.6f, 1.2f, 3.5f, Ground.Hard, 0.35f, Zone.Intertidal),
            new Rule("Plant_Coralline_A", 10f, 0f, 1.5f, 4f, Ground.Hard, 0.4f, Zone.Intertidal),
            new Rule("Plant_Tidebell_A", 5f, 0.2f, 1.5f, 5f, Ground.Hard, 0.5f, Zone.Intertidal),
            new Rule("Plant_Sealettuce_A", 10f, 0.2f, 2.5f, 3f, Ground.Any, 0.45f, Zone.Intertidal, Zone.Lagoon),
            // Lagoon: seagrass meadows on the sand, small algae between them, drift on the surface.
            new Rule("Plant_Seagrass_Patch_A", 6f, 1.5f, 7f, 7f, Ground.Soft, 0.5f, Zone.Lagoon),
            new Rule("Plant_Seagrass_A", 8f, 1.5f, 7f, 3f, Ground.Soft, 0.45f, Zone.Lagoon),
            new Rule("Plant_Turtlegrass_A", 6f, 1.5f, 6f, 3.5f, Ground.Soft, 0.5f, Zone.Lagoon),
            new Rule("Plant_Seagrapes_A", 4f, 2f, 7f, 5f, Ground.Soft, 0.55f, Zone.Lagoon),
            new Rule("Plant_Mermaidcup_A", 4f, 2f, 7f, 3f, Ground.Any, 0.55f, Zone.Lagoon),
            new Rule("Plant_Coinweed_A", 4f, 2f, 8f, 3f, Ground.Any, 0.55f, Zone.Lagoon, Zone.ReefCrest),
            new Rule("Plant_Shoalfern_A", 2f, 2f, 7f, 6f, Ground.Soft, 0.6f, Zone.Lagoon),
            new Rule("Plant_Anemone_Carpet_A", 2f, 2f, 8f, 6f, Ground.Soft, 0.6f, Zone.Lagoon, Zone.ForeReef),
            new Rule("Plant_Sargassum_A", 1.2f, 3f, 70f, 12f, Ground.Any, 0.55f, false, true, Zone.Lagoon, Zone.ForeReef, Zone.DropOff),
            new Rule("Plant_Floatrosette_A", 1f, 2.5f, 8f, 10f, Ground.Any, 0.6f, false, true, Zone.Lagoon),
            // Coral reef: stony corals on the crest and bommies, soft corals and fans on the slope.
            new Rule("Plant_Braincoral_A", 12.5f, 1.5f, 14f, 5f, Ground.Hard, 0.3f, Zone.ReefCrest, Zone.ForeReef),
            new Rule("Bush_Staghorn_A", 20f, 1.5f, 10f, 4.5f, Ground.Any, 0.3f, Zone.ReefCrest, Zone.ForeReef),
            new Rule("Bush_Staghorn_B", 15f, 2f, 12f, 4f, Ground.Any, 0.32f, Zone.ReefCrest, Zone.ForeReef),
            new Rule("Plant_Tablecoral_A", 6.25f, 3f, 14f, 9f, Ground.Any, 0.35f, Zone.ForeReef),
            new Rule("Plant_Scrollcoral_A", 10f, 4f, 16f, 4f, Ground.Any, 0.35f, Zone.ForeReef),
            new Rule("Plant_Pillarcoral_A", 5f, 3f, 14f, 6f, Ground.Any, 0.4f, Zone.ForeReef),
            new Rule("Plant_Leathercoral_A", 6f, 4f, 16f, 7f, Ground.Any, 0.38f, Zone.ForeReef),
            new Rule("Plant_Anemone_A", 6f, 2f, 15f, 4f, Ground.Any, 0.38f, Zone.ReefCrest, Zone.ForeReef),
            new Rule("Plant_Seafan_A", 5f, 6f, 18f, 7f, Ground.Any, 0.38f, Zone.ForeReef, Zone.RockyReef),
            new Rule("Plant_Seafan_Lace_A", 4f, 8f, 20f, 7f, Ground.Any, 0.38f, Zone.ForeReef),
            new Rule("Plant_Seawhip_A", 6f, 5f, 22f, 5f, Ground.Any, 0.38f, Zone.ForeReef, Zone.RockyReef),
            new Rule("Plant_Barrelsponge_A", 4f, 8f, 25f, 6f, Ground.Any, 0.4f, Zone.ForeReef, Zone.RockyReef),
            // Kelp forest: canopy kelps chosen by depth so they reach the surface, understory beneath.
            new Rule("Tree_Giantkelp_A", 22f, 30f, 36f, 7f, Ground.Hard, 0.35f, true, false, Zone.KelpPlateau),
            new Rule("Tree_Giantkelp_A_Golden", 6f, 30f, 36f, 7f, Ground.Hard, 0.5f, true, false, Zone.KelpPlateau),
            new Rule("Tree_Giantkelp_C", 18f, 25f, 32f, 7f, Ground.Hard, 0.35f, true, false, Zone.KelpPlateau),
            new Rule("Tree_Giantkelp_B", 18f, 19.5f, 26f, 6f, Ground.Hard, 0.35f, true, false, Zone.KelpPlateau, Zone.RockyReef),
            new Rule("Tree_Giantkelp_B_Cold", 6f, 19.5f, 26f, 6f, Ground.Hard, 0.5f, true, false, Zone.RockyReef),
            new Rule("Tree_Bullkelp_A", 4f, 17.5f, 23f, 9f, Ground.Hard, 0.5f, true, false, Zone.KelpPlateau, Zone.RockyReef),
            new Rule("Tree_Elkkelp_A", 3f, 15.5f, 21f, 9f, Ground.Hard, 0.55f, true, false, Zone.RockyReef, Zone.ForeReef),
            new Rule("Plant_Kelp_Young_A", 10f, 15f, 34f, 4f, Ground.Hard, 0.4f, Zone.KelpPlateau),
            new Rule("Plant_Kelp_Young_A_Cold", 4f, 15f, 30f, 4f, Ground.Hard, 0.5f, Zone.RockyReef),
            new Rule("Plant_Oarweed_A", 6f, 12f, 30f, 5f, Ground.Hard, 0.45f, Zone.KelpPlateau, Zone.RockyReef),
            new Rule("Plant_Featherboa_A", 4f, 15f, 32f, 5f, Ground.Hard, 0.5f, Zone.KelpPlateau),
            new Rule("Plant_Sugarkelp_A", 5f, 15f, 34f, 4f, Ground.Any, 0.45f, Zone.KelpPlateau),
            new Rule("Plant_Kelp_Spiny_A", 6f, 14f, 32f, 4f, Ground.Hard, 0.45f, Zone.KelpPlateau, Zone.RockyReef),
            new Rule("Plant_Kelp_Palm_A", 3f, 14f, 30f, 6f, Ground.Hard, 0.5f, Zone.KelpPlateau),
            new Rule("Plant_Kelp_Ribbon_A", 4f, 15f, 32f, 4f, Ground.Any, 0.5f, Zone.KelpPlateau),
            new Rule("Plant_Kelp_Strap_A", 4f, 0.5f, 6f, 6f, Ground.Hard, 0.45f, Zone.Intertidal, Zone.ReefCrest),
            new Rule("Plant_Kelpholdfast_A", 2f, 18f, 36f, 8f, Ground.Any, 0.55f, Zone.KelpPlateau),
            // Rocky reef: red weeds, sponges and filter feeders.
            new Rule("Plant_Redweed_A", 10.4f, 12f, 30f, 3.5f, Ground.Hard, 0.35f, Zone.RockyReef),
            new Rule("Plant_Tubesponge_A", 6f, 12f, 32f, 5f, Ground.Any, 0.38f, Zone.RockyReef),
            new Rule("Plant_Seapen_A", 4.5f, 12f, 64f, 5f, Ground.Soft, 0.4f, Zone.RockyReef, Zone.DropOff, Zone.Basin),
            new Rule("Plant_Sealily_A", 6f, 15f, 50f, 5f, Ground.Any, 0.4f, Zone.RockyReef, Zone.DropOff),
            // Twilight slope and basin.
            new Rule("Plant_Glasssponge_A", 6f, 32f, 64f, 5f, Ground.Any, 0.35f, Zone.DropOff, Zone.Basin),
            new Rule("Plant_Glassrope_A", 5f, 34f, 64f, 6f, Ground.Any, 0.38f, Zone.DropOff, Zone.Basin),
            new Rule("Plant_Bamboocoral_A", 6.25f, 32f, 60f, 6f, Ground.Any, 0.35f, Zone.DropOff),
            new Rule("Plant_Blackcoral_A", 6.25f, 32f, 60f, 5f, Ground.Any, 0.35f, Zone.DropOff),
            new Rule("Plant_Harpsponge_A", 3.75f, 40f, 64f, 6f, Ground.Any, 0.38f, Zone.DropOff, Zone.Basin),
            // Vent field.
            new Rule("Plant_Ventfleece_A", 25f, 50f, 70f, 6f, Ground.Any, 0.2f, Zone.VentField),
            new Rule("Plant_Chimneyfern_A", 14f, 50f, 70f, 6f, Ground.Any, 0.3f, Zone.VentField),
            new Rule("Plant_Abyssbulb_A", 14f, 50f, 70f, 4f, Ground.Any, 0.3f, Zone.VentField),
        };

        private struct Placed
        {
            public Vector2 XZ;
            public float Radius;
        }

        /// <summary>Plants the terrain; returns the number of plants, and parents floating ones under <paramref name="drift"/>.</summary>
        public static int Plant(Terrain terrain, Transform drift)
        {
            var random = new System.Random(Seed);
            var prototypes = new List<TreePrototype>();
            var prototypeIndex = new Dictionary<string, int>();
            var trees = new List<TreeInstance>();
            var placed = new Dictionary<Vector2Int, List<Placed>>();
            int floating = 0;
            float half = OceanShowcaseTerrain.Size * 0.5f;

            for (int r = 0; r < Rules.Length; r++)
            {
                Rule rule = Rules[r];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + rule.Prefab + ".prefab");
                if (prefab == null)
                {
                    Debug.LogError($"[OceanShowcase] Missing prefab {rule.Prefab}; it is not planted.");
                    continue;
                }
                float height = PrefabHeight(prefab);
                float cell = Mathf.Sqrt(10000f / rule.PerHectare);
                int cells = Mathf.CeilToInt(OceanShowcaseTerrain.Size / cell);
                // Each species patches on its own noise, so neighbouring species do not share the same clearings.
                var patchOffset = new Vector2(37.1f * r, 91.7f * r);
                for (int cz = 0; cz < cells; cz++)
                {
                    for (int cx = 0; cx < cells; cx++)
                    {
                        float x = -half + (cx + (float)random.NextDouble()) * cell;
                        float z = -half + (cz + (float)random.NextDouble()) * cell;
                        float scale = Mathf.Lerp(0.88f, 1.1f, (float)random.NextDouble());
                        float yaw = (float)random.NextDouble() * Mathf.PI * 2f;
                        if (!Suitable(rule, x, z, patchOffset, out float depth))
                            continue;
                        if (rule.ReachesSurface)
                        {
                            scale = (depth + 0.2f) / height;
                            if (scale < MinSurfaceScale || scale > MaxSurfaceScale)
                                continue;
                        }
                        float radius = rule.Spacing * 0.5f * scale;
                        if (!Free(placed, x, z, radius))
                            continue;
                        Remember(placed, x, z, radius);
                        if (rule.Floats)
                        {
                            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, drift.gameObject.scene);
                            go.transform.SetParent(drift, false);
                            go.transform.SetPositionAndRotation(new Vector3(x, 0f, z), Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f));
                            go.transform.localScale = Vector3.one * scale;
                            floating++;
                            continue;
                        }
                        if (!prototypeIndex.TryGetValue(rule.Prefab, out int index))
                        {
                            index = prototypes.Count;
                            prototypeIndex[rule.Prefab] = index;
                            prototypes.Add(new TreePrototype { prefab = prefab });
                        }
                        trees.Add(new TreeInstance
                        {
                            prototypeIndex = index,
                            position = new Vector3((x + half) / OceanShowcaseTerrain.Size,
                                                   (OceanShowcaseTerrain.HeightAt(x, z) - OceanShowcaseTerrain.Bottom) / OceanShowcaseTerrain.HeightRange,
                                                   (z + half) / OceanShowcaseTerrain.Size),
                            widthScale = scale,
                            heightScale = scale,
                            rotation = yaw,
                            color = Color.white,
                            lightmapColor = Color.white,
                        });
                    }
                }
            }

            TerrainData data = terrain.terrainData;
            data.treePrototypes = prototypes.ToArray();
            data.SetTreeInstances(trees.ToArray(), true);
            EditorUtility.SetDirty(data);
            return trees.Count + floating;
        }

        private static bool Suitable(Rule rule, float x, float z, Vector2 patchOffset, out float depth)
        {
            float y = OceanShowcaseTerrain.HeightAt(x, z);
            depth = -y;
            if (depth < rule.MinDepth || depth > rule.MaxDepth)
                return false;
            if (System.Array.IndexOf(rule.Zones, OceanShowcaseTerrain.ZoneAt(x, z)) < 0)
                return false;
            float steep = Vector3.Angle(OceanShowcaseTerrain.Normal(x, z), Vector3.up);
            if (!rule.Floats && steep > MaxSlopeDegrees)
                return false;
            int layer = OceanShowcaseTerrain.LayerAt(x, z, y, steep);
            bool soft = layer == OceanShowcaseTerrain.Sand || layer == OceanShowcaseTerrain.Seagrass || layer == OceanShowcaseTerrain.Dune;
            if ((rule.Ground == Ground.Hard && soft) || (rule.Ground == Ground.Soft && !soft))
                return false;
            // Patches: a broad noise per species, thresholded, so plants gather in groups with open bed between.
            float patch = OceanShowcaseTerrain.Fbm(x * 0.012f + patchOffset.x, z * 0.012f + patchOffset.y);
            return patch >= rule.PatchCut;
        }

        private static bool Free(Dictionary<Vector2Int, List<Placed>> placed, float x, float z, float radius)
        {
            var key = new Vector2Int(Mathf.FloorToInt(x / Bucket), Mathf.FloorToInt(z / Bucket));
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!placed.TryGetValue(key + new Vector2Int(dx, dz), out List<Placed> list))
                        continue;
                    foreach (Placed p in list)
                        if ((p.XZ - new Vector2(x, z)).magnitude < (p.Radius + radius) * 0.8f)
                            return false;
                }
            }
            return true;
        }

        private static void Remember(Dictionary<Vector2Int, List<Placed>> placed, float x, float z, float radius)
        {
            var key = new Vector2Int(Mathf.FloorToInt(x / Bucket), Mathf.FloorToInt(z / Bucket));
            if (!placed.TryGetValue(key, out List<Placed> list))
                placed[key] = list = new List<Placed>();
            list.Add(new Placed { XZ = new Vector2(x, z), Radius = radius });
        }

        /// <summary>Top of the LOD0 mesh above the prefab's origin, through the mesh object's own transform:
        /// the FBX import turns Blender's Z-up mesh with a rotation on that object, so the raw mesh y is not up.</summary>
        private static float PrefabHeight(GameObject prefab)
        {
            Renderer renderer = prefab.GetComponent<LODGroup>().GetLODs()[0].renderers[0];
            Bounds b = renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            float top = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y,
                                         i / 4 == 0 ? b.min.z : b.max.z);
                top = Mathf.Max(top, toRoot.MultiplyPoint3x4(corner).y);
            }
            return top;
        }
    }
}
