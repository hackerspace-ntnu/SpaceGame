// Prepares Assets/Game/Scenes/Core/MainMenu.unity for the runtime-built world screens.
//
// It replaces WorldSelectBuilder, which authored the old world panel into the scene as a canvas full
// of boxed buttons. The screens are built from code now (WorldSelectUI), so there is nothing left to
// author — but two things still have to happen inside the scene file, and neither can happen from
// runtime code:
//
//   1. The old WorldSelect canvas is still sitting in MainMenu.unity and would draw over everything.
//   2. WorldSelectUI clones the menu's own button prefab so its entries carry the menu's hover
//      animation and its two FMOD sounds, and a runtime-built screen has no Inspector to be handed
//      that prefab in. MainMenuUI holds it instead.
//
// Idempotent and safe to re-run: it strips the canvas if present, assigns the prefab if missing, and
// says what it did either way.
//
// Run from: Tools ▸ SpaceGame ▸ Menus ▸ Setup World Select
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    public static class WorldSelectSetup
    {
        private const string ScenePath = "Assets/Game/Scenes/Core/MainMenu.unity";
        private const string ButtonPrefabPath = "Assets/Game/Prefabs/UI/Buttons/Menu Button.prefab";

        /// <summary>The root the deleted builder created. Matched by name because that is what it controlled.</summary>
        private const string LegacyRootName = "WorldSelect";

        [MenuItem("Tools/SpaceGame/Menus/Setup World Select")]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[WorldSelectSetup] Exit Play mode first — a scene edited during play " +
                               "mode is discarded when play mode ends.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var menu = Object.FindFirstObjectByType<MainMenuUI>();
            if (menu == null)
            {
                Debug.LogError($"[WorldSelectSetup] No MainMenuUI in {ScenePath}. Nothing to wire.");
                return;
            }

            int stripped = StripLegacyPanel(scene);
            bool wired = AssignButtonPrefab(menu);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[WorldSelectSetup] Removed {stripped} legacy '{LegacyRootName}' root(s); " +
                      $"menu button prefab {(wired ? "assigned" : "NOT assigned — see errors above")}.");
        }

        private static int StripLegacyPanel(Scene scene)
        {
            int removed = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != LegacyRootName) continue;

                Object.DestroyImmediate(root);
                removed++;
            }

            return removed;
        }

        private static bool AssignButtonPrefab(MainMenuUI menu)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[WorldSelectSetup] No button prefab at {ButtonPrefabPath}. The world " +
                               "screens will fall back to plain text entries with no sound.");
                return false;
            }

            var so = new SerializedObject(menu);
            SerializedProperty field = so.FindProperty("menuButtonPrefab");

            if (field == null)
            {
                Debug.LogError("[WorldSelectSetup] MainMenuUI has no menuButtonPrefab field. " +
                               "Is the script compiled?");
                return false;
            }

            field.objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }
    }
}
