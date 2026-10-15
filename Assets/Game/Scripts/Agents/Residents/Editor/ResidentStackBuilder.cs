// Puts the resident component stack on the character prefabs a settlement places. The inspector's Generate
// runs it on the config's characters and special characters before laying anything out, so a character
// is a resident the moment it is placed.
//
// Components go on the ORIGINAL-SOURCE prefab asset, never on the scene instances: an added-component
// override per placed character is thirteen copies of one fact, invisible until somebody re-places a
// character and it silently stops being a resident. Values are Generate's job; this only adds.
using System;
using System.Collections.Generic;
using SpaceGame.Core;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceGame.Agents.Residents.EditorTools
{
    public static class ResidentStackBuilder
    {
        private static readonly Type[] CharacterStack =
        {
            typeof(Resident), typeof(AgentGoal), typeof(GoalTravelModule), typeof(ResidentRoutine),
            typeof(ResidentPresence), typeof(ResidentAwareness), typeof(ResidentVoice),
            typeof(ResidentSaveable), typeof(NetworkedTeleport), typeof(JostleSensor),
        };

        /// <summary>
        /// Adds whatever part of the stack each prefab's source lacks. Returns how many prefabs changed —
        /// a networked component added means the network prefab list must be re-synced before a client test.
        /// </summary>
        public static int EnsureStack(IEnumerable<GameObject> characterPrefabs)
        {
            var paths = new SortedSet<string>();
            foreach (GameObject prefab in characterPrefabs) CollectSource(prefab, paths);

            int changed = 0;
            foreach (string path in paths)
                if (AddCharacterStack(path)) changed++;
            if (changed > 0)
                Debug.Log($"[Residents] Resident stack added to {changed} character prefab(s). " +
                          "Run Tools/SpaceGame/Multiplayer/Sync Network Prefabs before testing on a client.");
            return changed;
        }

        private static void CollectSource(GameObject character, ISet<string> paths)
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(character);
            if (source == null || source.transform.parent != null)
            {
                Debug.LogWarning($"[Residents] '{character.name}' is not the root of a prefab; add its stack by hand.", character);
                return;
            }

            paths.Add(AssetDatabase.GetAssetPath(source));
        }

        private static bool AddCharacterStack(string path)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (Array.TrueForAll(CharacterStack, type => asset.GetComponent(type) != null)) return false;

            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == path)
            {
                Debug.LogWarning($"[Residents] Skipped {path}: it is open in Prefab Mode and lacks the resident stack. Close it and Generate again.");
                return false;
            }

            var added = new List<Type>();
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool networked = contents.GetComponent<NetworkObject>() != null;
                foreach (Type type in CharacterStack)
                {
                    if (contents.GetComponent(type) != null) continue;
                    if (!networked && typeof(NetworkBehaviour).IsAssignableFrom(type))
                    {
                        Debug.LogWarning($"[Residents] {path} has no root NetworkObject; {type.Name} not added.");
                        continue;
                    }

                    contents.AddComponent(type);
                    added.Add(type);
                }

                if (added.Count > 0) PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // The AssetDatabase can discard a save without a word; read it back.
            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (Type type in added)
                if (saved.GetComponent(type) == null)
                    Debug.LogError($"[Residents] {type.Name} did not persist on {path}.", saved);

            return added.Count > 0;
        }
    }
}
