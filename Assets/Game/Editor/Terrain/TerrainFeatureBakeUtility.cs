using System.IO;
using UnityEditor;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Editor-only asset-writing helpers for the terrain-feature bake. Factored out of
    /// <see cref="TerrainFeatureSpawnerEditor"/> so the inspector class stays small and the
    /// single-mesh and MULTI-MESH bake-save paths share one tested code path.
    ///
    /// SINGLE-mesh features save one <c>&lt;Type&gt;_seed_&lt;N&gt;_Mesh.asset</c>. A MULTI-mesh
    /// feature (e.g. ArchingCave) saves one <c>&lt;Type&gt;_seed_&lt;N&gt;_Sub&lt;i&gt;.asset</c> per
    /// sub-mesh; stale sub-mesh assets from a previous bake are cleared first so a re-bake that
    /// produces fewer tiles leaves no orphans.
    /// </summary>
    public static class TerrainFeatureBakeUtility
    {
        /// <summary>Resolves (creating if needed) the <c>TerrainFeatureBakes</c> folder next to the
        /// spawner's scene, or under <c>Assets</c> when the scene is unsaved.</summary>
        public static string ResolveBakeFolder(TerrainFeatureSpawner spawner)
        {
            string scenePath = spawner.gameObject.scene.path;
            string parent = string.IsNullOrEmpty(scenePath)
                ? "Assets"
                : Path.GetDirectoryName(scenePath).Replace('\\', '/');
            string folder = $"{parent}/TerrainFeatureBakes";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(parent, "TerrainFeatureBakes");
            return folder;
        }

        /// <summary>
        /// A filename stem that is UNIQUE per spawner GameObject — feature type, seed, AND a stable
        /// per-object id. Keying on type+seed alone meant two same-type, same-seed features (e.g. two
        /// Mesas both left at the default seed 0) baked to the SAME asset path: the second bake's
        /// <see cref="ReplaceAsset"/> deleted the asset the first feature's MeshFilter still pointed at,
        /// leaving that feature with an empty mesh. The id disambiguates them.
        /// </summary>
        static string AssetStem(TerrainFeatureSpawner spawner)
        {
            // GlobalObjectId's scene-local file id is stable across domain reloads and unique within
            // the scene — exactly the per-object key we need. Fall back to the (less stable but still
            // per-object) instance id if no global id is available (e.g. a transient object).
            GlobalObjectId gid = GlobalObjectId.GetGlobalObjectIdSlow(spawner);
            ulong objId = gid.targetObjectId != 0 ? gid.targetObjectId
                                                  : (ulong)(uint)spawner.GetInstanceID();
            return $"{spawner.FeatureType}_seed_{spawner.Seed}_{objId:x}";
        }

        /// <summary>Saves a single-mesh bake and returns the persistent mesh asset.</summary>
        public static Mesh SaveSingle(Mesh mesh, string folder, TerrainFeatureSpawner spawner)
        {
            string path = $"{folder}/{AssetStem(spawner)}_Mesh.asset";
            ReplaceAsset(mesh, path);
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        /// <summary>
        /// Saves a MULTI-mesh bake: clears stale <c>_SubN</c> assets, then writes one asset per
        /// sub-mesh. Returns the array of persistent mesh assets.
        /// </summary>
        public static Mesh[] SaveSubMeshes(
            System.Collections.Generic.List<Mesh> subMeshes, string folder, TerrainFeatureSpawner spawner)
        {
            ClearOldSubMeshAssets(folder, spawner);
            string stem = AssetStem(spawner);
            var saved = new Mesh[subMeshes.Count];
            for (int i = 0; i < subMeshes.Count; i++)
            {
                string p = $"{folder}/{stem}_Sub{i}.asset";
                ReplaceAsset(subMeshes[i], p);
                saved[i] = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            }
            return saved;
        }

        /// <summary>Deletes previously-baked sub-mesh assets for THIS spawner only. The prefix carries
        /// the per-object id so it never touches another same-type, same-seed feature's sub-meshes.</summary>
        static void ClearOldSubMeshAssets(string folder, TerrainFeatureSpawner spawner)
        {
            string prefix = $"{AssetStem(spawner)}_Sub";
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path).StartsWith(prefix))
                    AssetDatabase.DeleteAsset(path);
            }
        }

        /// <summary>Writes <paramref name="asset"/> to <paramref name="path"/>, replacing any existing.</summary>
        static void ReplaceAsset(Object asset, string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
        }
    }
}
