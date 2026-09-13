// Relabels and rebinds the three ButtonRow entries in MainMenu.unity from the old
// Singleplayer/Multiplayer/Quit front menu to the new Story/VS/Quit front menu.
//
// Story and VS are choice pages now (MenuChoiceUI), and MainMenuUI.StartStory / StartVersus build
// those pages before eventually calling the original StartSinglePlayer / StartMultiPlayer. The front
// menu itself must stop calling those directly and call the new entry points instead.
//
// This has to be an editor tool, not a hand edit of the .unity YAML: every ButtonRow entry is a
// prefab instance of "Menu Button.prefab", and its label and onClick are property modifications
// recorded AGAINST that instance. Typing an override into the YAML by hand is not recorded against
// the prefab connection, so the next prefab change (or even just opening the scene in the Editor)
// can silently drop it. PrefabUtility.RecordPrefabInstancePropertyModifications is what makes an
// override actually stick.
//
// Entries are matched by the method they are CURRENTLY bound to, not by GameObject name — the
// binding is what decides where an entry sends the player, and a stale name would be exactly the
// kind of bug this tool exists to rule out. Persistent listeners are cleared and rebuilt from
// scratch rather than edited in place: a persistent call whose method name is overwritten keeps its
// old argument shape, and a UnityEvent that cannot resolve its target does nothing at all — silently.
//
// Idempotent and safe to re-run: an entry already bound to its NEW method matches too, so a second
// pass finds nothing to change.
//
// Run from: Tools ▸ SpaceGame ▸ Menus ▸ Setup Front Menu
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpaceGame.EditorTools
{
    public static class FrontMenuSetup
    {
        private const string ScenePath = "Assets/Game/Scenes/Core/MainMenu.unity";
        private const string ButtonRowName = "ButtonRow";

        private struct Entry
        {
            public string OldMethod;
            public string NewMethod;
            public string Label;
        }

        private static readonly Entry[] Entries =
        {
            new Entry { OldMethod = "StartSinglePlayer", NewMethod = "StartStory", Label = "Story" },
            new Entry { OldMethod = "StartMultiPlayer", NewMethod = "StartVersus", Label = "VS" },
            new Entry { OldMethod = "QuitGame", NewMethod = "QuitGame", Label = "Quit" },
        };

        [MenuItem("Tools/SpaceGame/Menus/Setup Front Menu")]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[FrontMenuSetup] Exit Play mode first — a scene edited during play " +
                               "mode is discarded when play mode ends.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var menu = Object.FindFirstObjectByType<MainMenuUI>();
            if (menu == null)
            {
                Debug.LogError($"[FrontMenuSetup] No MainMenuUI in {ScenePath}. Nothing to wire.");
                return;
            }

            Transform buttonRow = FindButtonRow(scene);
            if (buttonRow == null)
            {
                Debug.LogError($"[FrontMenuSetup] No '{ButtonRowName}' found in {ScenePath}.");
                return;
            }

            Button[] candidates = buttonRow.GetComponentsInChildren<Button>(true);
            var used = new HashSet<Button>();
            int rewired = 0;

            foreach (Entry entry in Entries)
            {
                Button button = FindButtonForEntry(candidates, used, entry);
                if (button == null)
                {
                    Debug.LogError($"[FrontMenuSetup] No button bound to '{entry.OldMethod}' or " +
                                    $"'{entry.NewMethod}' under {ButtonRowName}. Skipping '{entry.Label}'.");
                    continue;
                }

                used.Add(button);
                RewireButton(button, menu, entry);
                rewired++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[FrontMenuSetup] Rewired {rewired} of {Entries.Length} front menu entries in {ScenePath}.");
        }

        private static Transform FindButtonRow(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindChildByName(root.transform, ButtonRowName);
                if (found != null) return found;
            }

            return null;
        }

        private static Transform FindChildByName(Transform parent, string name)
        {
            if (parent.name == name) return parent;

            foreach (Transform child in parent)
            {
                Transform found = FindChildByName(child, name);
                if (found != null) return found;
            }

            return null;
        }

        private static Button FindButtonForEntry(Button[] candidates, HashSet<Button> used, Entry entry)
        {
            foreach (Button button in candidates)
            {
                if (used.Contains(button)) continue;
                if (button.onClick.GetPersistentEventCount() == 0) continue;

                string boundMethod = button.onClick.GetPersistentMethodName(0);
                if (boundMethod == entry.OldMethod || boundMethod == entry.NewMethod)
                {
                    return button;
                }
            }

            return null;
        }

        private static void RewireButton(Button button, MainMenuUI menu, Entry entry)
        {
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null)
            {
                Debug.LogError($"[FrontMenuSetup] '{button.name}' has no TextMeshProUGUI label. " +
                                "Binding rewired, label left unchanged.");
            }

            while (button.onClick.GetPersistentEventCount() > 0)
            {
                UnityEventTools.RemovePersistentListener(button.onClick, 0);
            }

            UnityEngine.Events.UnityAction action = entry.NewMethod switch
            {
                "StartStory" => menu.StartStory,
                "StartVersus" => menu.StartVersus,
                "QuitGame" => menu.QuitGame,
                _ => null,
            };

            if (action == null)
            {
                Debug.LogError($"[FrontMenuSetup] Unknown target method '{entry.NewMethod}'. Binding left empty.");
            }
            else
            {
                UnityEventTools.AddVoidPersistentListener(button.onClick, action);
            }

            button.name = entry.Label;

            if (label != null)
            {
                label.text = entry.Label;
                PrefabUtility.RecordPrefabInstancePropertyModifications(label);
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(button);
            PrefabUtility.RecordPrefabInstancePropertyModifications(button.gameObject);
        }
    }
}
