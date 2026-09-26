using System.Reflection;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Re-bakes a prefab's NetworkAnimator tables against the controller it now animates.
    ///
    /// <para>
    /// NGO's NetworkAnimator does not read the controller at runtime: it serializes a parameter
    /// table (<c>AnimatorParameterEntries</c>) and a transition table (<c>TransitionStateInfoList</c>)
    /// into the prefab, and only regenerates them in its editor-only <c>OnValidate</c> — which
    /// nothing runs when the controller asset changes underneath it. A stale table sends parameters
    /// the controller no longer has and never sends the new ones, and the first sign is a remote
    /// player whose actions play without their mirror or speed. So every rebuild that changes the
    /// controller calls this.
    /// </para>
    /// </summary>
    internal static class NetworkAnimatorRebake
    {
        public const string PlayerPrefab = "Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab";

        public static void Rebake(string prefabPath)
        {
            PrefabStage open = PrefabStageUtility.GetCurrentPrefabStage();
            if (open != null && open.assetPath == prefabPath)
            {
                throw new System.InvalidOperationException(
                    $"{prefabPath} is open in Prefab Mode. Close it (saving or discarding your edits) " +
                    "and rebuild again — writing under an open prefab stage loses one side's changes.");
            }

            MethodInfo validate = typeof(NetworkAnimator).GetMethod(
                "OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (validate == null)
            {
                throw new System.MissingMethodException(
                    "NetworkAnimator.OnValidate is gone — Netcode changed how it bakes its tables. " +
                    "Update NetworkAnimatorRebake before rebuilding the humanoid controller.");
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (NetworkAnimator networkAnimator in root.GetComponentsInChildren<NetworkAnimator>(true))
                    validate.Invoke(networkAnimator, null);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// The parameter names a prefab's NetworkAnimator will synchronise, read back from the
        /// saved asset.
        /// </summary>
        public static string[] BakedParameters(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var networkAnimator = prefab != null ? prefab.GetComponentInChildren<NetworkAnimator>(true) : null;
            if (networkAnimator == null) return System.Array.Empty<string>();

            SerializedProperty entries = new SerializedObject(networkAnimator)
                .FindProperty("AnimatorParameterEntries.ParameterEntries");
            var names = new string[entries.arraySize];
            for (int i = 0; i < names.Length; i++)
                names[i] = entries.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue;
            return names;
        }
    }
}
