// Puts LadderClimber on the base player prefab, beside PlayerMovement, so every player built on it
// can climb. Idempotent: running it again finds the component and changes nothing.
//
// Ladders themselves are wired where they are built -- SkyCityBuilder adds a Ladder to each
// LAD_SkyCity marker -- so this is the one step that is about the player.
//
// Re-run from: Tools > SpaceGame > Player > Wire Ladder Climber
using SpaceGame.Characters;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class LadderClimberWiring
    {
        public const string PlayerPrefabPath = "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";

        [MenuItem("Tools/SpaceGame/Player/Wire Ladder Climber")]
        public static void Wire()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                if (root.GetComponent<PlayerMovement>() == null)
                    throw new System.InvalidOperationException(
                        $"{PlayerPrefabPath} has no PlayerMovement on its root; LadderClimber belongs beside it.");

                if (root.GetComponent<LadderClimber>() != null)
                {
                    Debug.Log($"[LadderClimber] Already on {PlayerPrefabPath}.");
                    return;
                }

                root.AddComponent<LadderClimber>();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log($"[LadderClimber] Added to {PlayerPrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
