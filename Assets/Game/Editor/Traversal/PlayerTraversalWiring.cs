// Puts a traversal component — something that takes the player's body off PlayerMovement for a
// while, like LadderClimber or HatchCrawler — on the base player prefab, beside PlayerMovement, so
// every player built on it has it. Idempotent: finds an existing one and changes nothing.
//
// The things they traverse are wired where those are built (SkyCityBuilder adds Ladders,
// DuneBargeBuilder adds Ladders and HatchPassages); this is the one step that is about the player.
using SpaceGame.Characters;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class PlayerTraversalWiring
    {
        public const string PlayerPrefabPath = "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";

        [MenuItem("Tools/SpaceGame/Player/Wire Hatch Crawler")]
        public static void WireHatchCrawler() => Ensure<HatchCrawler>();

        [MenuItem("Tools/SpaceGame/Player/Wire Ledge Climbing")]
        public static void WireLedgeClimbing()
        {
            Ensure<LedgeClimber>();
            Ensure<LedgeAirGrab>();
        }

        /// <summary>Adds a T beside PlayerMovement on the player prefab if it has none. True if added.</summary>
        public static bool Ensure<T>() where T : Component
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                if (root.GetComponent<PlayerMovement>() == null)
                    throw new System.InvalidOperationException(
                        $"{PlayerPrefabPath} has no PlayerMovement on its root; {typeof(T).Name} belongs beside it.");

                if (root.GetComponent<T>() != null)
                {
                    Debug.Log($"[{typeof(T).Name}] Already on {PlayerPrefabPath}.");
                    return false;
                }

                root.AddComponent<T>();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log($"[{typeof(T).Name}] Added to {PlayerPrefabPath}.");
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
