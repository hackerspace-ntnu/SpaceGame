// Generated LODs for the settlements that move: the Strider city's vehicles and the Sky fleet
// (SettlementLods.md). Their cost is draw calls -- a walking house is 1024 renderers -- so the far level
// is every mesh renderer's mesh in the prefab's rest pose (skinned ones baked) combined into ONE mesh
// with one submesh per material, which then carries Unity's own Mesh LODs so it keeps simplifying as it
// recedes. LOD0 is the prefab's own renderers, untouched and animated. Particle systems, trails and lines
// are never in a level, so smoke and dust run at every distance. Colliders, scripts and network
// components are not touched: a LODGroup only switches renderers, on every machine, with nothing sent.
//
// Each builder bakes its scratch root just before saving it; Tools/SpaceGame/Art/Bake Settlement LODs
// re-bakes every prefab in place. The merged mesh lives beside the prefab and is overwritten in place,
// so a rebuild never keeps a stale level and never mints a new GUID.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceGame.Vehicles;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SpaceGame.EditorTools
{
    public static class SettlementLodBaker
    {
        public const string MergedSuffix = "_LOD1_Merged";

        /// <summary>A screen height of 1 fills the screen, and a transition above it is never reached.</summary>
        private const float LargestTransition = 0.999f;

        /// <summary>Every Strider city vehicle prefab (the player's monowheel too: it is built by the same pass).</summary>
        public static IEnumerable<string> StriderPrefabPaths =>
            new[] { StriderCityBuilder.HabitatPath, DesertCrawlerBuilder.PrefabPath, StriderCrabOutriderBuilder.PrefabPath }
                .Concat(StriderBargeBuilder.Barges.Select(b => StriderBargeBuilder.PrefabPath(b.Variant)))
                .Concat(StriderMonowheelBuilder.AllPrefabPaths);

        /// <summary>The Sky fleet: the city (nested in SkyCityFleet, which inherits its levels) and the escort hulls.</summary>
        public static IEnumerable<string> SkyPrefabPaths =>
            SkyFleetBuilder.Vessels.Select(v => v.PrefabPath).Prepend(SkyCityBuilder.PrefabPath);

        public static string MergedMeshPath(string prefabPath) =>
            $"{Path.GetDirectoryName(prefabPath).Replace('\\', '/')}/{Path.GetFileNameWithoutExtension(prefabPath)}{MergedSuffix}.asset";

        /// <summary>
        /// The LODGroup screen height at which something <paramref name="worldSize"/> metres across is
        /// <paramref name="distance"/> metres from a camera of vertical <paramref name="fovDegrees"/>.
        /// Unity multiplies an object's screen height by QualitySettings.lodBias before comparing, so the
        /// threshold carries it too and the switch lands at the distance asked for.
        /// </summary>
        public static float ScreenHeightAt(float worldSize, float distance, float fovDegrees, float lodBias) =>
            worldSize * 0.5f / (distance * Mathf.Tan(fovDegrees * 0.5f * Mathf.Deg2Rad)) * lodBias;

        [MenuItem("Tools/SpaceGame/Art/Bake Settlement LODs")]
        public static void BakeAll()
        {
            SettlementLodSettings settings = SettlementLodSettings.Load();
            var report = new System.Text.StringBuilder("[SettlementLodBaker]\n");
            foreach (string path in StriderPrefabPaths) report.AppendLine(BakeInPlace(path, settings.strider));
            foreach (string path in SkyPrefabPaths) report.AppendLine(BakeInPlace(path, settings.sky));
            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        private static string BakeInPlace(string prefabPath, SettlementLodSettings.Profile profile)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            string line;
            try
            {
                MergedLod lod = Bake(root, prefabPath, profile);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException($"The AssetDatabase refused {prefabPath}.");
                line = $"  {prefabPath}: {lod.Group.GetLODs()[0].renderers.Length} renderers -> " +
                       $"{lod.Mesh.subMeshCount} merged submeshes, {lod.Mesh.lodCount} mesh LODs";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // A builder's last write on a networked prefab (Multiplayer.md, NetworkObjectDefaults).
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath).GetComponent<NetworkObject>() != null)
                NetworkObjectDefaults.KeepSceneMigrationSync(prefabPath);
            return line;
        }

        /// <summary>
        /// Gives <paramref name="root"/> its generated levels: LOD0 its own mesh renderers, LOD1 one merged
        /// mesh (saved at <see cref="MergedMeshPath"/> beside <paramref name="prefabPath"/>), culled past
        /// the profile's distance. Replaces a previous bake and reuses the root's own LODGroup. Throws on
        /// a LODGroup below the root: a renderer in two groups draws twice.
        /// </summary>
        public static MergedLod Bake(GameObject root, string prefabPath, SettlementLodSettings.Profile profile)
        {
            if (profile.mergedBeyondMetres >= profile.cullBeyondMetres)
                throw new ArgumentException($"The merged level ({profile.mergedBeyondMetres} m) must start nearer than the cull " +
                                            $"({profile.cullBeyondMetres} m).");
            LODGroup nested = root.GetComponentsInChildren<LODGroup>(true).FirstOrDefault(g => g.gameObject != root);
            if (nested != null)
                throw new InvalidOperationException($"{root.name}: '{nested.name}' has its own LODGroup, and a renderer in two " +
                                                    "groups draws twice. Bake that prefab instead, or remove its group.");

            Transform previous = root.transform.Find(MergedLod.ChildName);
            if (previous != null) Object.DestroyImmediate(previous.gameObject);

            Renderer[] originals = root.GetComponentsInChildren<Renderer>(true)
                .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer)
                .ToArray();
            Mesh built = Merge(root.transform, originals, out Material[] materials);
            MeshLodUtility.GenerateMeshLods(built, profile.meshLodLimit);
            Mesh mesh = SaveMesh(built, MergedMeshPath(prefabPath));

            var child = new GameObject(MergedLod.ChildName) { layer = root.layer };
            child.transform.SetParent(root.transform, false);
            var filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;

            LODGroup group = root.GetComponent<LODGroup>();
            if (group == null) group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            // Bounds first (RecalculateBounds reads the levels), then the heights that need them.
            group.SetLODs(Levels(originals, renderer, LargestTransition, LargestTransition * 0.5f));
            group.RecalculateBounds();
            Vector3 scale = root.transform.lossyScale;
            float size = group.size * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            float bias = QualitySettings.lodBias;
            group.SetLODs(Levels(originals, renderer,
                                 Mathf.Min(LargestTransition, ScreenHeightAt(size, profile.mergedBeyondMetres, profile.referenceFovDegrees, bias)),
                                 ScreenHeightAt(size, profile.cullBeyondMetres, profile.referenceFovDegrees, bias)));

            MergedLod lod = root.GetComponent<MergedLod>();
            if (lod == null) lod = root.AddComponent<MergedLod>();
            lod.Configure(group, filter, renderer);
            return lod;
        }

        private static LOD[] Levels(Renderer[] originals, Renderer merged, float mergedFrom, float culledFrom) =>
            new[] { new LOD(mergedFrom, originals), new LOD(culledFrom, new[] { merged }) };

        /// <summary>
        /// Every enabled, active mesh renderer under <paramref name="root"/> in the root's own space, one
        /// submesh per distinct material in first-seen order. A submesh with no material slot is not drawn
        /// by Unity and is left out here too, as is an empty one: CombineMeshes drops an empty part, and
        /// its material would then shift every later one onto the wrong submesh.
        /// </summary>
        private static Mesh Merge(Transform root, IEnumerable<Renderer> renderers, out Material[] materials)
        {
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var order = new List<Material>();
            var temporaries = new List<Mesh>();
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            try
            {
                foreach (Renderer renderer in renderers)
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    if (!TrySource(renderer, toRoot, temporaries, out Mesh source, out Matrix4x4 matrix)) continue;

                    Material[] slots = renderer.sharedMaterials;
                    for (int s = 0; s < source.subMeshCount; s++)
                    {
                        Material material = s < slots.Length ? slots[s] : null;
                        if (material == null || source.GetSubMesh(s).indexCount == 0) continue;

                        if (!byMaterial.TryGetValue(material, out List<CombineInstance> parts))
                        {
                            parts = new List<CombineInstance>();
                            byMaterial[material] = parts;
                            order.Add(material);
                        }
                        parts.Add(new CombineInstance { mesh = source, subMeshIndex = s, transform = matrix });
                    }
                }

                if (order.Count == 0)
                    throw new InvalidOperationException($"{root.name} has no enabled mesh renderer with a material to merge.");

                var perMaterial = new CombineInstance[order.Count];
                for (int i = 0; i < order.Count; i++)
                {
                    var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                    part.CombineMeshes(byMaterial[order[i]].ToArray(), mergeSubMeshes: true, useMatrices: true);
                    temporaries.Add(part);
                    perMaterial[i] = new CombineInstance { mesh = part, transform = Matrix4x4.identity };
                }

                var merged = new Mesh { name = root.name + MergedSuffix, indexFormat = IndexFormat.UInt32 };
                merged.CombineMeshes(perMaterial, mergeSubMeshes: false, useMatrices: false);
                merged.RecalculateBounds();
                materials = order.ToArray();
                return merged;
            }
            finally
            {
                foreach (Mesh temporary in temporaries) Object.DestroyImmediate(temporary);
            }
        }

        /// <summary>
        /// The mesh a renderer draws and the matrix into the root's space: a skinned one baked in its
        /// current (rest) pose, with its scale. A mirrored matrix needs nothing here: CombineMeshes
        /// rewinds the triangles of an instance whose matrix has a negative determinant itself.
        /// </summary>
        private static bool TrySource(Renderer renderer, Matrix4x4 toRoot, List<Mesh> temporaries, out Mesh source, out Matrix4x4 matrix)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                source = null;
                matrix = default;
                if (skinned.sharedMesh == null) return false;

                var pose = new Mesh();
                skinned.BakeMesh(pose, true);   // with the renderer's scale; position and rotation come from the matrix
                temporaries.Add(pose);
                source = pose;
                matrix = toRoot * Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
            }
            else
            {
                var filter = renderer.GetComponent<MeshFilter>();
                source = filter != null ? filter.sharedMesh : null;
                matrix = toRoot * renderer.transform.localToWorldMatrix;
                if (source == null) return false;
            }

            return true;
        }

        /// <summary>Saves <paramref name="mesh"/> at <paramref name="path"/>, over the asset already there so its GUID survives.</summary>
        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
