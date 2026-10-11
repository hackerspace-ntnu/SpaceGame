using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Gives a dressed drifter three levels of detail: a <c>LODGroup</c> on its root whose levels
    /// are chosen by what each renderer IS — the body, a garment by how much of the outline it
    /// makes (largest face of its rest bounds against the body's), an eye — tuned by a
    /// <see cref="CharacterLodProfile"/>.
    ///
    /// <list type="bullet">
    /// <item>LOD0: everything the prefab draws.</item>
    /// <item>LOD1: the body and every garment but the small accessories, on meshes reduced to
    /// <see cref="CharacterLodProfile.ReducedMeshLevel"/>, and the eyes; only the body and the large
    /// garments cast shadows.</item>
    /// <item>LOD2: the body and the garments that shape the silhouette, on meshes reduced further;
    /// only the body casts a shadow.</item>
    /// </list>
    ///
    /// <para>
    /// A reduced level is drawn by its own <c>SkinnedMeshRenderer</c> copies, kept under the model
    /// in <see cref="CopiesName"/>, bound to the same bones as the renderer they copy. Copies rather
    /// than Unity's per-renderer Mesh LOD because a skinned renderer with Mesh LOD still deforms its
    /// full mesh every frame, and Unity does not recommend Mesh LOD inside a <c>LODGroup</c>. A copy's
    /// mesh holds only the vertices its triangles use, so a distant character skins a quarter of the
    /// vertices, and the levels it is not on are not skinned at all. The eyes are not copied: the
    /// same renderers are listed again, so the blink and a corpse's shut lids still reach them.
    /// </para>
    ///
    /// <para>
    /// The reduced meshes are generated once per source mesh by <c>MeshLodUtility</c> and kept as
    /// assets in a folder beside the source model, stamped with the model's import hash, so a
    /// re-export regenerates them and every outfit of one body shares them.
    /// </para>
    ///
    /// <para>
    /// Idempotent: a run rebuilds the levels from what the character wears now, reuses the copies
    /// it still needs by name and deletes the rest, so a garment put on or taken off in Prefab Mode
    /// is picked up by the next run. Nothing here is networked or saved: which level draws is decided
    /// on each machine from its own camera.
    /// </para>
    /// </summary>
    public static class CharacterLodWiring
    {
        /// <summary>The child of the model that holds the reduced copies.</summary>
        public const string CopiesName = "LODs";

        private const string ModelName = "Model";

        [MenuItem("Tools/SpaceGame/Agents/Wire Character LODs")]
        public static void WireAll()
        {
            var profile = LoadProfile();
            if (profile == null) return;

            var paths = new List<string>();
            foreach (string folder in profile.CharacterFolders)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetDirectoryName(path).Replace('\\', '/') == folder) paths.Add(path);
                }
            }

            Wire(paths);
        }

        /// <summary>
        /// Wires each prefab in <paramref name="prefabPaths"/> that lies in one of the profile's
        /// character folders, in place, and reads each save back.
        /// </summary>
        public static void Wire(IEnumerable<string> prefabPaths)
        {
            var profile = LoadProfile();
            if (profile == null) return;

            int wired = 0;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            foreach (string path in prefabPaths)
            {
                if (!Covers(profile, path)) continue;

                // Saving under an open stage reloads it from disk and drops whatever is unsaved there.
                if (stage != null && stage.assetPath == path)
                {
                    Debug.LogWarning($"[CharacterLodWiring] {path} is open in Prefab Mode; skipped so " +
                                     "nothing unsaved there is lost. Close it and run the wiring again.");
                    continue;
                }

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (!Ensure(root, profile)) continue;

                    PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                    if (saved && HasLevels(path)) wired++;
                    else Debug.LogError($"[CharacterLodWiring] {path} did not save its LODGroup.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[CharacterLodWiring] Wired the levels of detail of {wired} character(s).");
        }

        /// <summary>
        /// Wires <paramref name="root"/>, the loaded contents of the prefab at
        /// <paramref name="prefabPath"/>, if that prefab is in one of the profile's character folders.
        /// For a builder that already holds the contents open.
        /// </summary>
        public static void EnsureIfCovered(GameObject root, string prefabPath)
        {
            var profile = LoadProfile();
            if (profile != null && Covers(profile, prefabPath)) Ensure(root, profile);
        }

        /// <summary>True for a reduced copy, which measures as the renderer it copies and is not body.</summary>
        public static bool IsLodCopy(Transform node)
        {
            for (Transform t = node; t != null; t = t.parent)
                if (t.name == CopiesName && t.parent != null && t.parent.name == ModelName) return true;

            return false;
        }

        private static bool Ensure(GameObject root, CharacterLodProfile profile)
        {
            Transform model = root.transform.Find(ModelName);
            if (model == null)
            {
                Debug.LogError($"[CharacterLodWiring] {root.name} has no '{ModelName}' child; not a drifter.");
                return false;
            }

            var bodies = new List<SkinnedMeshRenderer>();
            var garments = new List<SkinnedMeshRenderer>();
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsLodCopy(skin.transform) || !Draws(skin)) continue;
                (DrifterClothes.IsPartOfGarment(skin.transform) ? garments : bodies).Add(skin);
            }

            if (bodies.Count == 0)
            {
                Debug.LogError($"[CharacterLodWiring] {root.name} draws no skinned body; nothing to reduce.");
                return false;
            }

            var eyes = root.GetComponentsInChildren<MeshRenderer>(true)
                           .Where(r => StylizedEyeBuilder.WearsEyeShader(r) && r.enabled && r.gameObject.activeSelf)
                           .Cast<Renderer>().ToList();

            float bodySize = bodies.Max(Size);
            var full = new List<Renderer>();
            var reduced = new List<Renderer>();
            var silhouette = new List<Renderer>();
            var copies = new Copies(model, profile);

            foreach (var body in bodies)
            {
                full.Add(body);
                reduced.Add(copies.Of(body, Tier.Reduced, ShadowCastingMode.On));
                silhouette.Add(copies.Of(body, Tier.Silhouette,
                    profile.SilhouetteBodyCastsShadow ? ShadowCastingMode.On : ShadowCastingMode.Off));
            }

            foreach (var garment in garments)
            {
                full.Add(garment);
                float share = Size(garment) / bodySize;
                if (share < profile.AccessoryBelow) continue;

                reduced.Add(copies.Of(garment, Tier.Reduced,
                    share >= profile.ReducedShadowFrom ? ShadowCastingMode.On : ShadowCastingMode.Off));
                if (share >= profile.SilhouetteFrom)
                    silhouette.Add(copies.Of(garment, Tier.Silhouette, ShadowCastingMode.Off));
            }

            full.AddRange(eyes);
            if (profile.EyesInReduced) reduced.AddRange(eyes);
            copies.RemoveUnused();

            var group = root.GetComponent<LODGroup>();
            if (group == null) group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(new[]
            {
                new LOD(profile.FullDetailAbove, full.ToArray()),
                new LOD(profile.ReducedAbove, reduced.ToArray()),
                new LOD(profile.CulledBelow, silhouette.ToArray()),
            });
            group.RecalculateBounds();
            return true;
        }

        private static bool Draws(SkinnedMeshRenderer skin) =>
            skin.enabled && skin.gameObject.activeSelf && skin.sharedMesh != null && skin.bones.Length > 0;

        /// <summary>
        /// How much outline a renderer's mesh shows at rest: the largest face of its bounds. An area
        /// rather than a length, so a wide hat brim counts as much as a long sleeve and a long thin
        /// strap counts for little. Read off the mesh, not the renderer's transform: a skinned mesh
        /// does not deform through its own transform, and the body and its garments share one
        /// bind-pose space while their transforms differ (a garment hangs off the root, the body off
        /// the scaled model).
        /// </summary>
        private static float Size(SkinnedMeshRenderer skin)
        {
            Vector3 size = skin.sharedMesh.bounds.size;
            return Mathf.Max(size.x * size.y, size.y * size.z, size.x * size.z);
        }

        private static bool HasLevels(string path)
        {
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var group = saved != null ? saved.GetComponent<LODGroup>() : null;
            return group != null && group.lodCount == 3 && group.GetLODs().All(l => l.renderers.All(r => r != null));
        }

        private static bool Covers(CharacterLodProfile profile, string prefabPath)
        {
            string folder = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            return profile.CharacterFolders.Contains(folder);
        }

        private static CharacterLodProfile LoadProfile()
        {
            string guid = AssetDatabase.FindAssets("t:" + nameof(CharacterLodProfile)).FirstOrDefault();
            var profile = guid != null
                ? AssetDatabase.LoadAssetAtPath<CharacterLodProfile>(AssetDatabase.GUIDToAssetPath(guid))
                : null;
            if (profile == null)
                Debug.LogError("[CharacterLodWiring] No CharacterLodProfile asset. Create one with " +
                               "Create ▸ SpaceGame ▸ Art ▸ Character LOD Profile.");
            return profile;
        }

        private enum Tier { Reduced = 1, Silhouette = 2 }

        /// <summary>The reduced copies of one character, found or made by name under the model.</summary>
        private sealed class Copies
        {
            private readonly Transform holder;
            private readonly CharacterLodProfile profile;
            private readonly HashSet<Transform> used = new HashSet<Transform>();

            public Copies(Transform model, CharacterLodProfile profile)
            {
                this.profile = profile;
                holder = model.Find(CopiesName);
                if (holder != null) return;

                holder = new GameObject(CopiesName).transform;
                holder.SetParent(model, false);
                holder.gameObject.layer = model.gameObject.layer;
            }

            public SkinnedMeshRenderer Of(SkinnedMeshRenderer source, Tier tier, ShadowCastingMode shadows)
            {
                string name = $"{source.name}_LOD{(int)tier}";
                // Not holder.Find: two garments of one name must each keep their own copy across runs.
                Transform node = holder.Cast<Transform>().FirstOrDefault(c => c.name == name && !used.Contains(c));
                if (node == null)
                {
                    node = new GameObject(name, typeof(SkinnedMeshRenderer)).transform;
                    node.SetParent(holder, false);
                }
                used.Add(node);

                node.gameObject.layer = source.gameObject.layer;
                node.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                node.localScale = Vector3.Scale(source.transform.lossyScale, Inverse(holder.lossyScale));

                var copy = node.GetComponent<SkinnedMeshRenderer>();
                int level = tier == Tier.Reduced ? profile.ReducedMeshLevel : profile.SilhouetteMeshLevel;
                copy.sharedMesh = CharacterLodMeshes.Reduced(source.sharedMesh, level, profile);
                copy.sharedMaterials = source.sharedMaterials;
                copy.bones = source.bones;
                copy.rootBone = source.rootBone;
                copy.localBounds = source.localBounds;
                copy.updateWhenOffscreen = false;
                copy.quality = tier == Tier.Reduced ? profile.ReducedSkinQuality : profile.SilhouetteSkinQuality;
                copy.shadowCastingMode = shadows;
                copy.receiveShadows = source.receiveShadows;
                copy.skinnedMotionVectors = source.skinnedMotionVectors;
                copy.enabled = true;
                return copy;
            }

            public void RemoveUnused()
            {
                foreach (Transform child in holder.Cast<Transform>().ToList())
                    if (!used.Contains(child)) Object.DestroyImmediate(child.gameObject);
            }

            private static Vector3 Inverse(Vector3 v) => new Vector3(1f / v.x, 1f / v.y, 1f / v.z);
        }
    }
}
