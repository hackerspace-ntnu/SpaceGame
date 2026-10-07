// Puts LadderClimber on the base player prefab, beside PlayerMovement, so every player built on it
// can climb. Idempotent: running it again finds the component and changes nothing.
//
// Ladders themselves are wired where they are built -- SkyCityBuilder adds a Ladder to each
// LAD_SkyCity marker -- so this is the one step that is about the player.
//
// Re-run from: Tools > SpaceGame > Player > Wire Ladder Climber
using SpaceGame.Characters;
using UnityEditor;

namespace SpaceGame.EditorTools
{
    public static class LadderClimberWiring
    {
        public const string PlayerPrefabPath = PlayerTraversalWiring.PlayerPrefabPath;

        [MenuItem("Tools/SpaceGame/Player/Wire Ladder Climber")]
        public static void Wire() => PlayerTraversalWiring.Ensure<LadderClimber>();
    }
}
