// Imports the player's gesture clips and wires them into the astronaut's controller as
// one-shots on the Upper Body layer, and puts the emote component on the player prefab.
//
// Two kinds, both authored by
// Assets/Game/Art/Models/_Source~/models/characters/astronaut/gestures.py:
//
//   Aimed gestures (the Wrist Blade's stab, the Sucker Puncher's punch): three clips, one per
//   look pitch, blended on AimPitch exactly as the Point clips a firing gauntlet holds are, so the
//   thrust goes where the player is looking. The right-arm clips play MIRRORED for a device on
//   the left forearm, chosen by the same HoldMirror bool the hold poses use.
//
//   Emotes (wave, cheer, shrug, flex): one clip, both arms, no pitch and no twin. Played on a
//   chat command through PlayerEmotes.
//
// Re-running is safe and is the intended workflow: every gesture state and its transitions are
// removed and rebuilt, so this converges rather than accumulating copies. Same shape as
// PlayerPetGestureBuilder, which owns the one gesture not listed here.
//
// Re-run from: Tools > SpaceGame > Player > Build Gestures
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.EditorTools
{
    public static class PlayerGestureBuilder
    {
        private const string ClipDir = "Assets/Game/Art/Animations/Player/";
        private const string ControllerPath = "Assets/Game/Art/Animations/Player/AstronautArmature.controller";
        private const string PlayerPrefabPath = "Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab";
        private const string LayerName = "Upper Body";

        private const string GesturingParameter = "Gesturing";
        private const string HoldMirrorParameter = "HoldMirror";
        private const string AimPitchParameter = "AimPitch";

        /// <summary>An aimed gesture: `{Name} Right {Down,Level,Up}.fbx`, a mirrored twin.</summary>
        public static readonly string[] AimedGestures = { "Stab", "Punch" };

        /// <summary>An emote: `{Name}.fbx`, one state. The table is PlayerEmotes', so the two cannot drift.</summary>
        public static IEnumerable<string> EmoteGestures => PlayerEmotes.Table.Select(e => e.Trigger);

        /// <summary>The look pitch each aimed clip stands for -- the Point clips' own table.</summary>
        private static readonly (string Suffix, float Pitch)[] PitchClips =
        {
            ("Down", -45f),
            ("Level", 0f),
            ("Up", 45f),
        };

        /// <summary>
        /// Triggers that mean "another gesture": a transition entered on one of these is left
        /// alone by the guard. There is no controller handle on an AnimatorStateMachine, so
        /// triggers are recognised by name; the Pet, Throw and Meele ones predate this builder.
        /// </summary>
        private static HashSet<string> GestureTriggers =>
            new HashSet<string>(AimedGestures.Concat(EmoteGestures).Concat(new[] { "Pet", "Throw", "Meele" }));

        [MenuItem("Tools/SpaceGame/Player/Build Gestures")]
        public static void Build()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) { Debug.LogError($"No animator controller at {ControllerPath}."); return; }

            AnimatorControllerLayer layer = controller.layers.FirstOrDefault(l => l.name == LayerName);
            if (layer == null)
            {
                Debug.LogError($"'{ControllerPath}' has no '{LayerName}' layer. Run Tools/SpaceGame/Player/Build Upper Body Layer first.");
                return;
            }

            EnsureParameter(controller, GesturingParameter, AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, HoldMirrorParameter, AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, AimPitchParameter, AnimatorControllerParameterType.Float);

            AnimatorStateMachine machine = layer.stateMachine;
            var gestureStates = new List<AnimatorState>();
            var summary = new List<string>();

            foreach (string name in AimedGestures)
            {
                var clips = new Dictionary<string, AnimationClip>();
                foreach ((string suffix, _) in PitchClips)
                {
                    AnimationClip clip = ImportClip($"{name} Right {suffix}");
                    if (clip == null) return;
                    clips[suffix] = clip;
                }

                EnsureParameter(controller, name, AnimatorControllerParameterType.Trigger);
                Remove(machine, name + " Right");
                Remove(machine, name + " Left");
                gestureStates.Add(AddAimed(controller, machine, name, name + " Right", clips, mirror: false));
                gestureStates.Add(AddAimed(controller, machine, name, name + " Left", clips, mirror: true));
                summary.Add($"{name} {clips["Level"].length:F2}s");
            }

            foreach (string name in EmoteGestures)
            {
                AnimationClip clip = ImportClip(name);
                if (clip == null) return;

                EnsureParameter(controller, name, AnimatorControllerParameterType.Trigger);
                Remove(machine, name);
                gestureStates.Add(AddEmote(machine, name, clip));
                summary.Add($"{name} {clip.length:F2}s");
            }

            int guarded = GuardTransitions(machine, gestureStates);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            bool emotesAdded = EnsurePlayerEmotes();

            Debug.Log($"Gestures wired on '{LayerName}': {string.Join(", ", summary)}; {guarded} other " +
                      $"transition(s) stand down while {GesturingParameter} is set" +
                      (emotesAdded ? "; PlayerEmotes added to the player prefab." : "."));
        }

        /// <summary>
        /// Humanoid, avatar created from the file, root locked: the Pet gesture's recipe, for the
        /// reasons written on <c>PlayerPetGestureBuilder.ImportClip</c>.
        /// </summary>
        private static AnimationClip ImportClip(string name)
        {
            string path = ClipDir + name + ".fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"No model at {path}. Run gestures.py first:  blender --background astronaut.blend --python gestures.py");
                return null;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.importAnimation = true;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.SaveAndReimport();

            importer = AssetImporter.GetAtPath(path) as ModelImporter;
            ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
            if (takes == null || takes.Length == 0)
            {
                Debug.LogError($"{path} carries no animation take; the avatar probably failed to build.");
                return null;
            }

            ModelImporterClipAnimation take = takes[0];
            importer.clipAnimations = new[]
            {
                new ModelImporterClipAnimation
                {
                    name = name,
                    takeName = take.takeName,
                    firstFrame = take.firstFrame,
                    lastFrame = take.lastFrame,
                    loopTime = false,
                    lockRootRotation = true,
                    lockRootHeightY = true,
                    lockRootPositionXZ = true,
                    keepOriginalOrientation = true,
                    keepOriginalPositionY = true,
                    keepOriginalPositionXZ = true,
                },
            };
            importer.SaveAndReimport();

            AnimationClip clip = Find(path, name);
            if (clip == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                clip = Find(path, name);
            }
            if (clip == null) Debug.LogError($"Clip '{name}' missing from {path} after import.");
            return clip;
        }

        private static AnimationClip Find(string path, string name) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);

        /// <summary>
        /// One aimed state: the three pitches blended on AimPitch, entered from Any State on the
        /// trigger for the arm HoldMirror names, leaving for the layer's default state as the
        /// clip ends.
        /// </summary>
        private static AnimatorState AddAimed(AnimatorController controller, AnimatorStateMachine sm, string trigger,
                                              string stateName, Dictionary<string, AnimationClip> clips, bool mirror)
        {
            var tree = new BlendTree
            {
                name = stateName + " Tree",
                blendType = BlendTreeType.Simple1D,
                blendParameter = AimPitchParameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            foreach ((string suffix, float pitch) in PitchClips)
                tree.AddChild(clips[suffix], pitch);

            AnimatorState state = sm.AddState(stateName);
            state.motion = tree;
            state.mirror = mirror;
            state.writeDefaultValues = true;

            AnimatorStateTransition into = sm.AddAnyStateTransition(state);
            into.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            into.AddCondition(mirror ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, HoldMirrorParameter);
            into.duration = 0.06f;      // a strike starts now, not in a tenth of a second
            into.hasExitTime = false;
            into.hasFixedDuration = true;
            into.canTransitionToSelf = false;

            AddExit(sm, state);
            return state;
        }

        /// <summary>One emote state: a single clip, entered on its trigger whichever arm is worn.</summary>
        private static AnimatorState AddEmote(AnimatorStateMachine sm, string trigger, AnimationClip clip)
        {
            AnimatorState state = sm.AddState(trigger);
            state.motion = clip;
            state.writeDefaultValues = true;

            AnimatorStateTransition into = sm.AddAnyStateTransition(state);
            into.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            into.duration = 0.15f;
            into.hasExitTime = false;
            into.hasFixedDuration = true;
            into.canTransitionToSelf = false;

            AddExit(sm, state);
            return state;
        }

        private static void AddExit(AnimatorStateMachine sm, AnimatorState state)
        {
            AnimatorState back = sm.defaultState;
            if (back == null || back == state) return;

            AnimatorStateTransition exit = state.AddTransition(back);
            exit.hasExitTime = true;
            exit.exitTime = 0.95f;
            exit.duration = 0.15f;
        }

        /// <summary>
        /// Every other Any State transition on the layer stands down while a gesture runs — the
        /// hold poses and the raise states are entered on continuously-true compares and would
        /// otherwise throw the gesture out on its second frame. Idempotent. Transitions entered
        /// on another gesture's trigger are left as they are.
        /// </summary>
        private static int GuardTransitions(AnimatorStateMachine sm, List<AnimatorState> gestures)
        {
            HashSet<string> triggers = GestureTriggers;
            int guarded = 0;
            foreach (AnimatorStateTransition t in sm.anyStateTransitions)
            {
                if (gestures.Contains(t.destinationState)) continue;
                if (t.conditions.Any(c => triggers.Contains(c.parameter))) continue;

                var kept = t.conditions.Where(c => c.parameter != GesturingParameter).ToList();
                kept.Add(new AnimatorCondition { parameter = GesturingParameter, mode = AnimatorConditionMode.IfNot, threshold = 0f });
                t.conditions = kept.ToArray();
                guarded++;
            }
            return guarded;
        }

        private static void Remove(AnimatorStateMachine machine, string stateName)
        {
            foreach (AnimatorStateTransition t in machine.anyStateTransitions.ToArray())
                if (t.destinationState != null && t.destinationState.name == stateName)
                    machine.RemoveAnyStateTransition(t);

            foreach (ChildAnimatorState child in machine.states)
            {
                if (child.state == null) continue;
                foreach (AnimatorStateTransition t in child.state.transitions.ToArray())
                    if (t.destinationState != null && t.destinationState.name == stateName)
                        child.state.RemoveTransition(t);
            }

            foreach (ChildAnimatorState child in machine.states.ToArray())
                if (child.state != null && child.state.name == stateName)
                {
                    if (child.state.motion is BlendTree tree) Object.DestroyImmediate(tree, true);
                    machine.RemoveState(child.state);
                }
        }

        private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            if (controller.parameters.Any(p => p.name == name)) return;
            controller.AddParameter(name, type);
        }

        /// <summary>
        /// The emote component on the player. Added to the loaded contents' root, which is the
        /// nested PlayerCharacter.prefab instance, so — as RepulsorGauntletBuilder records — the
        /// component lands in the BASE prefab and every player built on it can emote.
        /// </summary>
        private static bool EnsurePlayerEmotes()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (contents == null) { Debug.LogError($"No player prefab at {PlayerPrefabPath}."); return false; }

            bool added = false;
            try
            {
                if (contents.GetComponent<PlayerEmotes>() == null)
                {
                    if (contents.GetComponent<PlayerAimRig>() == null)
                        Debug.LogError($"{PlayerPrefabPath} has no PlayerAimRig on its root; PlayerEmotes needs it.");
                    else
                    {
                        contents.AddComponent<PlayerEmotes>();
                        PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
                        added = true;
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return added;
        }
    }
}
