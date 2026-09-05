// Imports the player's "pet a creature" gesture and wires it into the astronaut's controller.
//
// The clip comes out of Blender via
// Assets/Game/Art/Models/_Source~/models/characters/astronaut_pet.py.
// Everything Unity-side is done here rather than by hand, for the same reason AppaBuilder exists:
// AstronautArmature.controller is a large checked-in asset and a state added through the Animator
// window is a state nobody can reproduce.
//
// Re-running is safe and is the intended workflow. The Pet state and its transitions are removed
// and rebuilt, so this converges rather than accumulating a second copy each time.
//
// Re-run from: Tools > Creatures > Build Player Pet Gesture
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class PlayerPetGestureBuilder
    {
        private const string ClipFbx = "Assets/Game/Art/Animations/Player/PetCreature.fbx";
        private const string BodyFbx =
            "Assets/Game/Art/Models/Characters/Astronaut/AstronautArmature.fbx";
        private const string ControllerPath =
            "Assets/Game/Art/Animations/Player/AstronautArmature.controller";

        private const string ClipName = "PetCreature";
        private const string StateName = "Pet";
        private const string Trigger = "Pet";
        private const string LayerName = "Upper Body";

        // The layer's hold poses are entered from Any State on a continuously-true int compare,
        // so with empty hands "HoldStyle Equals 0" fires every frame and throws the gesture
        // straight back out. Every one of them also has to stand down while a gesture runs.
        private const string GesturingParameter = "Gesturing";

        // Frames are read off the take rather than hard-coded. astronaut_pet.py authors 0..60,
        // but Blender's exporter has its own opinion about where a baked range starts and a
        // one-frame disagreement silently drops the end of the gesture.
        [MenuItem("Tools/Creatures/Build Player Pet Gesture")]
        public static void Build()
        {
            if (!ImportClip()) return;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"No animator controller at {ControllerPath}.");
                return;
            }

            AnimationClip clip = LoadClip();
            if (clip == null) return;

            AnimatorControllerLayer layer = controller.layers
                .FirstOrDefault(l => l.name == LayerName);
            if (layer == null)
            {
                Debug.LogError($"'{ControllerPath}' has no '{LayerName}' layer. The gesture has to " +
                               "live on a masked layer or petting would stop the player walking.");
                return;
            }

            EnsureTrigger(controller, Trigger);
            EnsureBool(controller, GesturingParameter);
            AnimatorState state = RebuildState(controller, layer, clip);
            int guarded = GuardHoldTransitions(layer, state);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log($"Pet gesture wired: '{state.name}' on layer '{LayerName}', trigger " +
                      $"'{Trigger}', clip '{clip.name}' ({clip.length:F2}s); " +
                      $"{guarded} hold transition(s) now require {GesturingParameter} == false.");
        }

        /// <summary>
        /// Humanoid, so it retargets onto the astronaut's avatar the way every mocap clip beside
        /// it does.
        /// </summary>
        private static bool ImportClip()
        {
            var importer = AssetImporter.GetAtPath(ClipFbx) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"No model at {ClipFbx}. Run astronaut_pet.py first: " +
                               "  blender --background --python astronaut_pet.py");
                return false;
            }

            // Pass one: humanoid and animation on, so the importer will actually read the takes.
            // defaultClipAnimations is empty until it has, which is what made the first version
            // fall back to guessing a take name -- it guessed "PetCreature", the file said
            // "Scene", and Unity produced no clip and said nothing.
            importer.animationType = ModelImporterAnimationType.Human;
            importer.importAnimation = true;

            // **Create the avatar, do not copy it.** Copying the astronaut's looked right and is
            // what every mocap clip beside this one does, but it cannot work here: that avatar's
            // HumanDescription is rooted at a node called "AstronautArmature(Clone)", and Blender
            // names its armature object "Armature". Unity answers
            //     "Copied Avatar Rig Configuration mis-match. Transform hierarchy does not match:
            //      Transform 'Armature' not found in HumanDescription."
            // and then imports NO takes at all, so the next step reports an empty file and blames
            // the exporter. This is the ArtPipeline gotcha verbatim: Copy-From-Other-Avatar cannot
            // be configured on an armature-only FBX -- that one must be Create From This Model.
            //
            // Retargeting is unaffected. Both rigs are humanoid and Mixamo-named, and a humanoid
            // clip plays through the humanoid abstraction rather than through bone names.
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;

            importer.SaveAndReimport();

            // Pass two: now ask the file what it actually contains.
            importer = AssetImporter.GetAtPath(ClipFbx) as ModelImporter;
            ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
            if (takes == null || takes.Length == 0)
            {
                // Almost always a rig failure rather than a missing take: when the avatar cannot
                // be built Unity abandons the import and the take list comes back empty, so say
                // both things rather than sending the reader to the exporter first.
                Avatar built = AssetDatabase.LoadAllAssetsAtPath(ClipFbx).OfType<Avatar>().FirstOrDefault();
                string rig = built == null ? "no avatar was produced"
                    : $"avatar isValid={built.isValid} isHuman={built.isHuman}";
                Debug.LogError($"{ClipFbx} carries no animation take. Check the rig first ({rig}) " +
                               "— a failed avatar aborts the import and empties the take list. If " +
                               "the rig is fine, re-run astronaut_pet.py; the export needs " +
                               "bake_anim_use_all_actions.");
                return false;
            }

            ModelImporterClipAnimation take =
                takes.FirstOrDefault(t => t.takeName != null && t.takeName.Contains(ClipName))
                ?? takes[0];

            importer.clipAnimations = new[]
            {
                new ModelImporterClipAnimation
                {
                    name = ClipName,
                    takeName = take.takeName,
                    firstFrame = take.firstFrame,
                    lastFrame = take.lastFrame,
                    loopTime = false,
                    // Played over locomotion through a mask, so it must not move the player.
                    // These throw the root away and keep the arms.
                    lockRootRotation = true,
                    lockRootHeightY = true,
                    lockRootPositionXZ = true,
                    keepOriginalOrientation = true,
                    keepOriginalPositionY = true,
                    keepOriginalPositionXZ = true,
                },
            };

            importer.SaveAndReimport();

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ClipFbx).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                Debug.LogWarning($"{ClipFbx} did not produce a valid humanoid avatar " +
                                 $"(valid={avatar?.isValid}, human={avatar?.isHuman}). The clip " +
                                 "will import but may not retarget onto the player.");

            Debug.Log($"Imported '{ClipName}' from take '{take.takeName}' " +
                      $"(frames {take.firstFrame}-{take.lastFrame}).");
            return true;
        }

        /// <summary>
        /// Fetch the generated clip, forcing a synchronous reimport if it is not visible yet.
        ///
        /// <para>
        /// <c>SaveAndReimport</c> does not guarantee a freshly named clip is readable in the same
        /// run — the same trap <c>AppaBuilder.ClipsAreImported</c> guards against. Rather than tell
        /// the user to run it again, force the import and look once more; and if it still is not
        /// there, say what IS there, which is the thing that would have diagnosed this in one step
        /// instead of three.
        /// </para>
        /// </summary>
        private static AnimationClip LoadClip()
        {
            AnimationClip clip = Find();
            if (clip != null) return clip;

            AssetDatabase.ImportAsset(ClipFbx,
                                      ImportAssetOptions.ForceUpdate
                                      | ImportAssetOptions.ForceSynchronousImport);
            clip = Find();
            if (clip != null) return clip;

            string found = string.Join(", ", AssetDatabase.LoadAllAssetsAtPath(ClipFbx)
                .Select(o => $"{o.GetType().Name} '{o.name}'"));
            var importer = AssetImporter.GetAtPath(ClipFbx) as ModelImporter;
            string takes = importer == null ? "<no importer>" : string.Join(", ",
                importer.defaultClipAnimations.Select(t => $"'{t.takeName}'"));

            Debug.LogError($"Clip '{ClipName}' missing from {ClipFbx} even after a forced " +
                           $"reimport.  takes in the file: {takes}  |  assets produced: {found}");
            return null;
        }

        private static AnimationClip Find() => AssetDatabase.LoadAllAssetsAtPath(ClipFbx)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == ClipName);

        private static void EnsureBool(AnimatorController controller, string name)
        {
            if (System.Array.Exists(controller.parameters, p => p.name == name))
                return;
            controller.AddParameter(name, AnimatorControllerParameterType.Bool);
        }

        /// <summary>
        /// Make every Any State transition on this layer stand down while a gesture runs.
        ///
        /// Idempotent: the condition is removed first, so re-running does not stack duplicates.
        /// The gesture's own transition is skipped — it is the one that must still fire.
        /// </summary>
        private static int GuardHoldTransitions(AnimatorControllerLayer layer, AnimatorState gesture)
        {
            int guarded = 0;
            foreach (AnimatorStateTransition t in layer.stateMachine.anyStateTransitions)
            {
                if (t.destinationState == gesture) continue;

                var kept = new List<AnimatorCondition>();
                foreach (AnimatorCondition c in t.conditions)
                    if (c.parameter != GesturingParameter)
                        kept.Add(c);
                kept.Add(new AnimatorCondition
                {
                    parameter = GesturingParameter,
                    mode = AnimatorConditionMode.IfNot,
                    threshold = 0f,
                });
                t.conditions = kept.ToArray();
                guarded++;
            }
            return guarded;
        }

        private static void EnsureTrigger(AnimatorController controller, string name)
        {
            if (controller.parameters.Any(p => p.name == name))
                return;
            controller.AddParameter(name, AnimatorControllerParameterType.Trigger);
        }

        /// <summary>
        /// Remove any previous Pet state and its transitions, then build it again.
        ///
        /// Order matters: a transition holds a reference to its destination state, so the
        /// transitions go first or the state machine is left pointing at a deleted object.
        /// </summary>
        private static AnimatorState RebuildState(AnimatorController controller,
                                                  AnimatorControllerLayer layer,
                                                  AnimationClip clip)
        {
            AnimatorStateMachine machine = layer.stateMachine;

            foreach (AnimatorStateTransition t in machine.anyStateTransitions.ToArray())
                if (t.destinationState != null && t.destinationState.name == StateName)
                    machine.RemoveAnyStateTransition(t);

            var doomed = new List<ChildAnimatorState>();
            foreach (ChildAnimatorState child in machine.states)
                if (child.state != null && child.state.name == StateName)
                    doomed.Add(child);

            foreach (ChildAnimatorState child in machine.states)
            {
                if (child.state == null) continue;
                foreach (AnimatorStateTransition t in child.state.transitions.ToArray())
                    if (t.destinationState != null && t.destinationState.name == StateName)
                        child.state.RemoveTransition(t);
            }

            foreach (ChildAnimatorState child in doomed)
                machine.RemoveState(child.state);

            AnimatorState state = machine.AddState(StateName);
            state.motion = clip;

            AnimatorStateTransition into = machine.AddAnyStateTransition(state);
            into.AddCondition(AnimatorConditionMode.If, 0f, Trigger);
            into.duration = 0.15f;
            into.hasExitTime = false;
            // Pressing E again mid-reach should not restart the arm from his hip.
            into.canTransitionToSelf = false;

            AnimatorState back = machine.defaultState;
            if (back != null && back != state)
            {
                AnimatorStateTransition exit = state.AddTransition(back);
                exit.hasExitTime = true;
                exit.exitTime = 0.92f;
                exit.duration = 0.20f;
            }
            else
            {
                Debug.LogWarning($"Layer '{LayerName}' has no default state to return to; the " +
                                 "gesture will hold its last pose until something else claims " +
                                 "the layer.");
            }

            return state;
        }
    }
}
