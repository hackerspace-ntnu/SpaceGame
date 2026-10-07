using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Brings existing drifter prefabs up to date with a re-exported FBX, in place.
    ///
    /// <para>
    /// The builder unpacks the model into the prefab, so the prefab owns its own copy of every bone.
    /// A re-export brings new vertices and bindposes through the mesh asset, but a bone that MOVED
    /// stays where the prefab had it, a bone that was ADDED is missing (and a mesh bound to it then
    /// fails to render), and a new skinned mesh -- a garment -- never appears at all. Rebuilding is
    /// not the answer: it mints new object IDs and throws away hand edits, like the plain Raxy's
    /// odd pair of eyes.
    /// </para>
    ///
    /// <para>
    /// So this edits what the FBX owns and nothing else. Bones take the FBX's rest pose, and missing
    /// ones are added. Skinned meshes are rebound to the FBX's bone order, get a material for any
    /// new slot, and missing ones are added. A renderer that is not skinned (the eye spheres), a
    /// material already on a slot, and anything the prefab has that the FBX does not are left
    /// exactly as they are.
    /// </para>
    ///
    /// <para>
    /// Clothes are the exception: they are prefabs of their own (<see cref="DrifterClothes"/>), so the
    /// FBX's garments are never added to a character. Their prefabs are brought into step with the
    /// FBX instead, and every worn garment rebinds itself by bone name.
    /// </para>
    /// </summary>
    public static class DrifterRigSync
    {
        /// <summary>The model's GameObject under the drifter's root, as the builder names it.</summary>
        private const string ModelName = "Model";

        private const float PositionTolerance = 1e-5f;
        private const float RotationToleranceDegrees = 0.01f;

        [MenuItem("Tools/SpaceGame/Agents/Sync Drifter Rigs From FBX")]
        public static void SyncAll() => Run(apply: true);

        [MenuItem("Tools/SpaceGame/Agents/Sync Drifter Rigs From FBX (Dry Run)")]
        public static void PreviewAll() => Run(apply: false);

        private static void Run(bool apply)
        {
            int outOfDate = 0;
            var garmentFolders = new HashSet<string>();
            foreach (var recipe in SculptCharacterBuilder.Drifters)
            {
                string clothes = DrifterClothes.FolderFor(recipe.PrefabPath);
                if (apply && garmentFolders.Add(recipe.FbxPath + "|" + clothes))
                    foreach (string written in DrifterClothes.EnsurePrefabs(recipe.FbxPath, clothes))
                        Debug.Log($"[DrifterRigSync] Garment prefab {written} brought into step with {recipe.FbxPath}.");

                if (AssetDatabase.LoadAssetAtPath<GameObject>(recipe.PrefabPath) == null) continue;

                // Saving under an open stage reloads it from disk and drops whatever is unsaved there.
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (apply && stage != null && stage.assetPath == recipe.PrefabPath)
                {
                    Debug.LogWarning($"[DrifterRigSync] {recipe.Name} is open in Prefab Mode; skipped so " +
                                     "nothing unsaved there is lost. Close it and run the sync again.");
                    continue;
                }

                var changes = Sync(recipe.PrefabPath, recipe.FbxPath, apply);
                if (changes.Count == 0) continue;

                outOfDate++;
                Debug.Log($"[DrifterRigSync] {recipe.Name}: {(apply ? "" : "would make ")}{changes.Count} " +
                          $"change(s):\n  " + string.Join("\n  ", changes));
            }

            Debug.Log($"[DrifterRigSync] {outOfDate} drifter prefab(s) {(apply ? "synced to" : "out of step with")} " +
                      "their FBX.");
        }

        /// <summary>
        /// Brings the prefab at <paramref name="prefabPath"/> into step with <paramref name="fbxPath"/>,
        /// saving it only if <paramref name="apply"/> and something changed. Returns what changed.
        /// </summary>
        public static List<string> Sync(string prefabPath, string fbxPath, bool apply)
        {
            var changes = new List<string>();
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null)
            {
                Debug.LogError($"[DrifterRigSync] No model at {fbxPath}.");
                return changes;
            }

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var model = root.transform.Find(ModelName);
                if (model == null)
                {
                    Debug.LogError($"[DrifterRigSync] {prefabPath} has no '{ModelName}' child; not a drifter.");
                    return changes;
                }

                SyncBones(fbx.transform, fbx.transform, model, changes);
                SyncSkins(fbx.transform, model, changes);

                if (apply && changes.Count > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
                    if (!saved) Debug.LogError($"[DrifterRigSync] Failed to save {prefabPath}.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return changes;
        }

        private static void SyncBones(Transform fbxRoot, Transform source, Transform own, List<string> changes)
        {
            foreach (Transform sourceChild in source)
            {
                // A mesh is not a bone: skinned ones are SyncSkins' job, and a bone-parented prop keeps
                // whatever pose a person gave it.
                if (sourceChild.GetComponent<Renderer>() != null) continue;

                string path = AnimationUtility.CalculateTransformPath(sourceChild, fbxRoot);
                Transform ownChild = own.Find(sourceChild.name);
                if (ownChild == null)
                {
                    ownChild = new GameObject(sourceChild.name).transform;
                    ownChild.SetParent(own, false);
                    ownChild.gameObject.layer = own.gameObject.layer;
                    CopyLocalPose(sourceChild, ownChild);
                    changes.Add($"added bone {path}");
                }
                else if (!SamePose(sourceChild, ownChild))
                {
                    changes.Add($"moved bone {path} to its rest pose " +
                                $"({Vector3.Distance(sourceChild.localPosition, ownChild.localPosition) * 1000f:0.#} mm, " +
                                $"{Quaternion.Angle(sourceChild.localRotation, ownChild.localRotation):0.#} deg)");
                    CopyLocalPose(sourceChild, ownChild);
                }

                SyncBones(fbxRoot, sourceChild, ownChild, changes);
            }
        }

        private static void SyncSkins(Transform fbxRoot, Transform model, List<string> changes)
        {
            foreach (var source in fbxRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (DrifterClothes.IsGarment(source.transform)) continue;

                string path = AnimationUtility.CalculateTransformPath(source.transform, fbxRoot);
                Transform own = model.Find(path);
                SkinnedMeshRenderer target;
                if (own == null)
                {
                    target = AddSkin(source, fbxRoot, model);
                    if (target == null) continue;
                    changes.Add($"added {path}");
                }
                else if (!own.TryGetComponent(out target))
                {
                    Debug.LogError($"[DrifterRigSync] '{path}' is skinned in the FBX and not in the prefab; " +
                                   "left alone.");
                    continue;
                }

                if (!TryMapBones(source, fbxRoot, model, out Transform[] bones, out Transform rootBone))
                    continue;

                if (target.sharedMesh != source.sharedMesh)
                {
                    target.sharedMesh = source.sharedMesh;
                    changes.Add($"{path}: mesh reassigned");
                }

                if (!SameBones(target.bones, bones) || target.rootBone != rootBone)
                {
                    changes.Add($"{path}: rebound to the FBX's {bones.Length} bones (had {target.bones.Length})");
                    target.bones = bones;
                    target.rootBone = rootBone;
                    target.localBounds = source.localBounds;
                }

                // A slot the mesh gained -- the inside of Raxy's mouth -- takes the FBX's material. The
                // slots it already had keep what they wear.
                var materials = target.sharedMaterials;
                int slots = source.sharedMesh.subMeshCount;
                if (materials.Length < slots)
                {
                    var grown = new Material[slots];
                    for (int i = 0; i < slots; i++)
                        grown[i] = i < materials.Length ? materials[i] : source.sharedMaterials[i];
                    target.sharedMaterials = grown;
                    changes.Add($"{path}: {slots - materials.Length} new material slot(s) given the FBX's material");
                }
            }
        }

        private static SkinnedMeshRenderer AddSkin(SkinnedMeshRenderer source, Transform fbxRoot, Transform model)
        {
            string parentPath = AnimationUtility.CalculateTransformPath(source.transform.parent, fbxRoot);
            Transform parent = string.IsNullOrEmpty(parentPath) ? model : model.Find(parentPath);
            if (parent == null)
            {
                Debug.LogError($"[DrifterRigSync] No '{parentPath}' in the prefab to hang '{source.name}' from.");
                return null;
            }

            var go = new GameObject(source.name);
            go.transform.SetParent(parent, false);
            go.layer = model.gameObject.layer;
            CopyLocalPose(source.transform, go.transform);

            var target = go.AddComponent<SkinnedMeshRenderer>();
            target.sharedMaterials = source.sharedMaterials;
            target.shadowCastingMode = source.shadowCastingMode;
            target.receiveShadows = source.receiveShadows;
            target.quality = source.quality;
            target.updateWhenOffscreen = source.updateWhenOffscreen;
            target.skinnedMotionVectors = source.skinnedMotionVectors;
            return target;
        }

        private static bool TryMapBones(SkinnedMeshRenderer source, Transform fbxRoot, Transform model,
                                        out Transform[] bones, out Transform rootBone)
        {
            bones = new Transform[source.bones.Length];
            rootBone = null;
            for (int i = 0; i < bones.Length; i++)
            {
                string path = AnimationUtility.CalculateTransformPath(source.bones[i], fbxRoot);
                bones[i] = model.Find(path);
                if (bones[i] == null)
                {
                    Debug.LogError($"[DrifterRigSync] '{source.name}' is bound to '{path}', which the prefab " +
                                   "does not have; left unbound.");
                    return false;
                }
            }

            if (source.rootBone != null)
                rootBone = model.Find(AnimationUtility.CalculateTransformPath(source.rootBone, fbxRoot));
            return true;
        }

        private static bool SameBones(Transform[] a, Transform[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static bool SamePose(Transform a, Transform b) =>
            Vector3.Distance(a.localPosition, b.localPosition) < PositionTolerance &&
            Quaternion.Angle(a.localRotation, b.localRotation) < RotationToleranceDegrees &&
            Vector3.Distance(a.localScale, b.localScale) < PositionTolerance;

        private static void CopyLocalPose(Transform from, Transform to)
        {
            to.localPosition = from.localPosition;
            to.localRotation = from.localRotation;
            to.localScale = from.localScale;
        }
    }
}
