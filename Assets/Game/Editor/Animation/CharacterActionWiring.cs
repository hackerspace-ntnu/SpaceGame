// Getting a humanoid body's animation components onto every prefab that wears the humanoid
// controller, and handing its modules their default actions.
//
// Called from BOTH ends, like AgentGroundConformWiring: from the menu item here for every prefab
// that exists, and from inside SculptCharacterBuilder, which overwrites the drifters wholesale — a
// component added by hand would be gone on the next rebuild.
using System.Collections.Generic;
using System.Linq;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class CharacterActionWiring
    {
        private const string PrefabRoot = "Assets/Game/Prefabs";

        // The defaults handed out, by action name. Each is set only where the field is still empty,
        // so an action somebody chose in the Inspector is never replaced.
        public const string UnarmedStrike = "Unarmed Strike";
        public const string SwordStrike = "Sword Strike";
        public const string WarningPoint = "Point";
        public const string TalkingCue = "talk";
        public const string BrawlCue = "brawl";
        public const string SwordplayCue = "swordplay";

        /// <summary>
        /// Adds CharacterActions, BodyLanguage, IdleVariation and HurtReaction to
        /// <paramref name="root"/> — and SpeechGestures and MeleeDefense to an NPC: the player never
        /// speaks through the popup, and blocks with its own hands — and fills its modules' empty action fields. Returns whether anything
        /// changed, so a caller can skip a pointless prefab save.
        ///
        /// <para>
        /// Also empties the animator trigger strings a humanoid body's modules used to name
        /// (<c>Meele</c>, <c>Hurt</c>, <c>Death</c>): the generated controller has none of them, and
        /// a creature keeps its own because it is never wired.
        /// </para>
        /// </summary>
        public static bool Ensure(GameObject root)
        {
            if (root == null || !WearsHumanoidController(root)) return false;

            bool changed = false;
            bool player = root.GetComponent<PlayerController>() != null;

            AddIfMissing<CharacterActions>(root, ref changed);
            BodyLanguage body = AddIfMissing<BodyLanguage>(root, ref changed, out bool added);
            AddIfMissing<IdleVariation>(root, ref changed);
            AddIfMissing<HurtReaction>(root, ref changed);

            // The player's legs are its own input's; a full-body reaction would freeze them under it
            // (GDC-L1-ANIM-0002). Set only on a fresh component, so a later Inspector choice stands.
            if (added) changed |= SetBool(body, "fullBody", !player);

            if (!player)
            {
                SpeechGestures speech = AddIfMissing<SpeechGestures>(root, ref changed);
                changed |= SetCueIfEmpty(speech, "talking", TalkingCue);

                // The player defends with its own hands; an NPC's guard is this roll on the server.
                AddIfMissing<MeleeDefense>(root, ref changed);
            }

            bool sword = root.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Sword");
            foreach (CloseCombatModule melee in root.GetComponentsInChildren<CloseCombatModule>(true))
            {
                changed |= SetIfEmpty(melee, "attackAction", sword ? SwordStrike : UnarmedStrike);
                changed |= SetCueIfEmpty(melee, "attackCue", sword ? SwordplayCue : BrawlCue);
                changed |= SetString(melee, "attackAnimTrigger", string.Empty);
            }

            foreach (HealthReactionModule reaction in root.GetComponentsInChildren<HealthReactionModule>(true))
            {
                changed |= SetString(reaction, "hurtAnimTrigger", string.Empty);
                changed |= SetString(reaction, "dieAnimTrigger", string.Empty);
            }

            foreach (AggressionTelegraphModule telegraph in root.GetComponentsInChildren<AggressionTelegraphModule>(true))
                changed |= SetIfEmpty(telegraph, "waryAction", WarningPoint);

            return changed;
        }

        [MenuItem("Tools/SpaceGame/Animation/Wire Humanoid Prefabs")]
        public static void WireAll()
        {
            int changed = 0, seen = 0;
            string open = PrefabStageUtility.GetCurrentPrefabStage()?.assetPath;

            // Bases before variants: a variant inherits what its base was just given, and wiring
            // it first would write overrides for values it is about to inherit anyway.
            IEnumerable<string> paths = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(p))
                              == PrefabAssetType.Variant)
                .ThenBy(p => p, System.StringComparer.Ordinal);

            foreach (string path in paths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || !WearsHumanoidController(asset)) continue;

                seen++;
                if (path == open)
                {
                    Debug.LogWarning($"[CharacterActionWiring] Skipped {path}: it is open in Prefab Mode. " +
                                     "Close it and run this again.");
                    continue;
                }

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (!Ensure(contents)) continue;

                    PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);

                    // A read-only AssetDatabase discards a prefab save and says nothing at all.
                    if (!saved)
                    {
                        Debug.LogError($"[CharacterActionWiring] Could not save {path}. The AssetDatabase refused the write.");
                        continue;
                    }

                    changed++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[CharacterActionWiring] {changed} of {seen} humanoid prefabs updated.");
        }

        /// <summary>Does <paramref name="root"/> animate with the humanoid controller, directly or through an override?</summary>
        public static bool WearsHumanoidController(GameObject root)
        {
            var humanoid = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(HumanoidControllerBuilder.ControllerPath);
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            {
                RuntimeAnimatorController controller = animator.runtimeAnimatorController;
                if (controller is AnimatorOverrideController overrides) controller = overrides.runtimeAnimatorController;
                if (controller != null && controller == humanoid) return true;
            }
            return false;
        }

        private static T AddIfMissing<T>(GameObject root, ref bool changed) where T : Component =>
            AddIfMissing<T>(root, ref changed, out _);

        private static T AddIfMissing<T>(GameObject root, ref bool changed, out bool added) where T : Component
        {
            var existing = root.GetComponent<T>();
            added = existing == null;
            if (existing != null) return existing;
            changed = true;
            return root.AddComponent<T>();
        }

        private static bool SetCueIfEmpty(Component component, string field, string cueName)
        {
            var so = new SerializedObject(component);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[CharacterActionWiring] {component.GetType().Name} has no '{field}' field.", component);
                return false;
            }
            if (property.objectReferenceValue != null) return false;

            CharacterCue cue = HumanoidControllerBuilder.CollectCues().FirstOrDefault(c => c.name == cueName);
            if (cue == null)
            {
                Debug.LogError($"[CharacterActionWiring] No cue named '{cueName}' under " +
                               $"{HumanoidControllerBuilder.CuesFolder}; {component.GetType().Name}.{field} left empty.", component);
                return false;
            }

            property.objectReferenceValue = cue;
            return so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool SetBool(Component component, string field, bool value)
        {
            var so = new SerializedObject(component);
            SerializedProperty property = so.FindProperty(field);
            if (property == null || property.boolValue == value) return false;

            property.boolValue = value;
            return so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool SetIfEmpty(Component component, string field, string actionName)
        {
            var so = new SerializedObject(component);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[CharacterActionWiring] {component.GetType().Name} has no '{field}' field.", component);
                return false;
            }
            if (property.objectReferenceValue != null) return false;

            CharacterAction action = HumanoidControllerBuilder.CollectActions().FirstOrDefault(a => a.name == actionName);
            if (action == null)
            {
                Debug.LogError($"[CharacterActionWiring] No action named '{actionName}' under " +
                               $"{HumanoidControllerBuilder.ActionsFolder}; {component.GetType().Name}.{field} left empty.", component);
                return false;
            }

            property.objectReferenceValue = action;
            return so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool SetString(Component component, string field, string value)
        {
            var so = new SerializedObject(component);
            SerializedProperty property = so.FindProperty(field);
            if (property == null || property.stringValue == value) return false;

            property.stringValue = value;
            return so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
