using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// A drifter's clothes: one prefab per garment, made from the skinned meshes in its FBX named
    /// <see cref="Prefix"/>*, kept in a <c>Clothes</c> folder beside the character's prefab.
    ///
    /// <para>
    /// The garment prefab is the one copy of a piece of clothing. A character wears it as a nested
    /// instance under its model, and <see cref="SkinnedGarment"/> binds it to that character's
    /// skeleton by bone name -- so a material changed on the poncho's prefab changes it on every
    /// Raxy wearing one, and dressing a Raxy by hand is dragging a garment onto its model.
    /// </para>
    ///
    /// <para>
    /// What a character wears is fixed per prefab: nothing about it changes in play, so there is
    /// nothing to replicate or save beyond the prefab's own identity.
    /// </para>
    /// </summary>
    public static class DrifterClothes
    {
        /// <summary>How a garment is named in the .blend, and so in the FBX (raxy.blend's Clothes collection).</summary>
        public const string Prefix = "Clothes_";

        public static bool IsGarment(Transform t) => t.name.StartsWith(Prefix, System.StringComparison.Ordinal);

        /// <summary>True for a garment and everything in it -- its rest-pose view included.</summary>
        public static bool IsPartOfGarment(Transform t) => t.GetComponentInParent<SkinnedGarment>(true) != null;

        /// <summary>The child of a garment prefab that draws it in its rest pose while nobody wears it.</summary>
        private const string RestPoseName = "RestPose";

        /// <summary>Where a character's garment prefabs live: a Clothes folder beside its own prefab.</summary>
        public static string FolderFor(string characterPrefabPath) =>
            Path.GetDirectoryName(characterPrefabPath).Replace('\\', '/') + "/Clothes";

        /// <summary>
        /// Makes a prefab of every garment in the FBX that has none yet, and keeps the mesh, bone list
        /// and bounds of those that do in step with the FBX. Materials are set only when a prefab is
        /// made; after that they are the prefab's own to change. Returns the prefabs it wrote.
        /// </summary>
        public static List<string> EnsurePrefabs(string fbxPath, string folder)
        {
            var written = new List<string>();
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null) return written;

            foreach (var source in fbx.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!IsGarment(source.transform)) continue;

                SculptCharacterBuilder.EnsureFolder(folder);
                string path = $"{folder}/{source.name}.prefab";
                bool isNew = AssetDatabase.LoadAssetAtPath<GameObject>(path) == null;
                GameObject root = isNew ? new GameObject(source.name) : PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (Configure(root, source, isNew))
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                        if (saved) written.Add(path);
                        else Debug.LogError($"[DrifterClothes] Failed to save {path}.");
                    }
                }
                finally
                {
                    if (isNew) Object.DestroyImmediate(root);
                    else PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return written;
        }

        /// <summary>
        /// Puts exactly the garments named in <paramref name="outfit"/> on the model, as instances of
        /// their prefabs in <paramref name="folder"/>, and takes off everything else it wears -- a
        /// worn garment not in the outfit, and the FBX's own copies, which a fresh model instance
        /// carries. Null or empty undresses it. A garment with no prefab is logged: one renamed in
        /// Blender would otherwise leave the outfit quietly one piece short.
        /// </summary>
        public static void Dress(Transform model, IReadOnlyCollection<string> outfit, string folder)
        {
            var wanted = new HashSet<string>(outfit ?? System.Array.Empty<string>());

            foreach (var child in model.Cast<Transform>().ToList())
            {
                if (!IsGarment(child)) continue;
                if (IsGarmentPrefabInstance(child.gameObject, folder) && wanted.Remove(child.name)) continue;
                Object.DestroyImmediate(child.gameObject);
            }

            foreach (string garment in wanted)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{garment}.prefab");
                if (prefab == null)
                {
                    Debug.LogError($"[DrifterClothes] No garment prefab {folder}/{garment}.prefab for " +
                                   $"{model.root.name}; the outfit is missing it. Check the object names " +
                                   "in the .blend's Clothes collection.");
                    continue;
                }

                var worn = (GameObject)PrefabUtility.InstantiatePrefab(prefab, model);
                worn.layer = model.gameObject.layer;
                worn.GetComponent<SkinnedGarment>().Bind();
            }
        }

        private static bool Configure(GameObject root, SkinnedMeshRenderer source, bool isNew)
        {
            var skin = root.GetComponent<SkinnedMeshRenderer>();
            if (skin == null) skin = root.AddComponent<SkinnedMeshRenderer>();
            var garment = root.GetComponent<SkinnedGarment>();
            if (garment == null) garment = root.AddComponent<SkinnedGarment>();

            bool changed = isNew;
            if (isNew)
            {
                // Hung under a model the way the FBX hangs it, so its bounds sit where the body's do.
                root.transform.localRotation = source.transform.localRotation;
                root.transform.localScale = source.transform.localScale;
                skin.sharedMaterials = source.sharedMaterials;
                skin.shadowCastingMode = source.shadowCastingMode;
                skin.receiveShadows = source.receiveShadows;
                skin.quality = source.quality;
                skin.skinnedMotionVectors = source.skinnedMotionVectors;
            }

            if (skin.sharedMesh != source.sharedMesh)
            {
                skin.sharedMesh = source.sharedMesh;
                changed = true;
            }

            if (skin.localBounds != source.localBounds)
            {
                skin.localBounds = source.localBounds;
                changed = true;
            }

            var so = new SerializedObject(garment);
            var names = so.FindProperty("boneNames");
            string[] wanted = source.bones.Select(b => b.name).ToArray();
            bool sameBones = names.arraySize == wanted.Length &&
                             Enumerable.Range(0, wanted.Length).All(i => names.GetArrayElementAtIndex(i).stringValue == wanted[i]);
            if (!sameBones)
            {
                names.arraySize = wanted.Length;
                for (int i = 0; i < wanted.Length; i++)
                    names.GetArrayElementAtIndex(i).stringValue = wanted[i];
                changed = true;
            }

            string rootBone = source.rootBone != null ? source.rootBone.name : string.Empty;
            var rootName = so.FindProperty("rootBoneName");
            if (rootName.stringValue != rootBone)
            {
                rootName.stringValue = rootBone;
                changed = true;
            }

            // Saved unworn: the rest-pose view drawing, the bone-less skin off. SkinnedGarment flips
            // both the moment the garment is put on someone.
            var restPose = EnsureRestPose(root, source, ref changed);
            var restPoseField = so.FindProperty("restPose");
            if (restPoseField.objectReferenceValue != restPose)
            {
                restPoseField.objectReferenceValue = restPose;
                changed = true;
            }

            if (skin.enabled)
            {
                skin.enabled = false;
                changed = true;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }

        /// <summary>The garment's mesh on a plain MeshRenderer child: its rest pose, drawn unskinned.</summary>
        private static MeshRenderer EnsureRestPose(GameObject root, SkinnedMeshRenderer source, ref bool changed)
        {
            Transform child = root.transform.Find(RestPoseName);
            if (child == null)
            {
                child = new GameObject(RestPoseName, typeof(MeshFilter), typeof(MeshRenderer)).transform;
                child.SetParent(root.transform, false);
                child.GetComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;
                changed = true;
            }

            var filter = child.GetComponent<MeshFilter>();
            if (filter.sharedMesh != source.sharedMesh)
            {
                filter.sharedMesh = source.sharedMesh;
                changed = true;
            }

            if (!child.gameObject.activeSelf)
            {
                child.gameObject.SetActive(true);
                changed = true;
            }

            return child.GetComponent<MeshRenderer>();
        }

        private static bool IsGarmentPrefabInstance(GameObject go, string folder)
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) return false;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            return path.StartsWith(folder + "/", System.StringComparison.Ordinal);
        }
    }
}
