using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the player's masked Upper Body layer and the avatar mask it needs.
    ///
    /// <para>
    /// Idempotent: run it as often as you like. It creates what is missing and leaves what is
    /// already correct alone, so it is safe to re-run after someone has tuned a transition by
    /// hand — with the exception of the mask, which is rewritten wholesale because it has no
    /// tuning worth keeping.
    /// </para>
    /// <para>
    /// This exists as a script rather than as hand-edited YAML because a layer is a state machine,
    /// four states and four transitions, each with its own fileID, and inventing those by hand is
    /// how a controller ends up subtly corrupt in a way that only shows at runtime.
    /// </para>
    /// </summary>
    internal static class PlayerUpperBodySetup
    {
        private const string ControllerPath = "Assets/Game/Art/Animations/Player/AstronautArmature.controller";
        private const string MaskPath = "Assets/Game/Art/Animations/Player/UpperBody.mask";
        private const string LayerName = "Upper Body";
        private const string MaskName = "UpperBody";
        /// <summary>
        /// The pose parameter. Written by <c>PlayerAimRig</c> from what is in the hand — or, with
        /// empty hands, from a lit Flashlight Gauntlet, which borrows this same pose rather than
        /// carrying one of its own. One parameter, so the two can never both be on.
        /// </summary>
        private const string HoldStyleParameter = "HoldStyle";

        private const string RelaxedClip = "Assets/ThirdParty/Kevin Iglesias/Human Animations/Animations/Male/Combat/Gun/HumanM@Gun_Aim02.fbx";
        private const string OneHandedClip = "Assets/ThirdParty/Kevin Iglesias/Human Animations/Animations/Male/Combat/Gun/HumanM@Gun_Aim01.fbx";
        private const string TwoHandedClip = "Assets/ThirdParty/Kevin Iglesias/Human Animations/Animations/Male/Combat/AssaultRifle/AssultRifleIdle.fbx";

        [MenuItem("Tools/SpaceGame/Player/Build Upper Body Layer")]
        public static void Build()
        {
            AvatarMask mask = BuildMask();

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"PlayerUpperBodySetup: no AnimatorController at {ControllerPath}.");
                return;
            }

            EnsureIntParameter(controller, HoldStyleParameter);
            EnsureIntParameter(controller, ArmRaiseParameter);
            EnsureFloatParameter(controller, AimPitchParameter);

            int index = FindLayer(controller, LayerName);
            if (index >= 0)
            {
                AnimatorControllerLayer[] layers = controller.layers;
                layers[index].avatarMask = mask;
                controller.layers = layers;

                EnsureRaiseStates(controller, layers[index].stateMachine);

                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                Debug.Log($"PlayerUpperBodySetup: layer '{LayerName}' already exists at index " +
                          $"{index}. Mask refreshed; hold states left alone; raise states ensured.");
                return;
            }

            BuildLayer(controller, mask);
            EnsureRaiseStates(controller, controller.layers[controller.layers.Length - 1].stateMachine);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log($"PlayerUpperBodySetup: built '{LayerName}' at index {controller.layers.Length - 1}.");
        }

        /// <summary>
        /// Chest and both arms, nothing else.
        ///
        /// <para>
        /// The head is deliberately OFF. Death, damage and idle look-around clips all animate it
        /// on the Base Layer, and an Upper Body layer at weight 1 would override every one of
        /// them — a corpse whose head snapped level would be the visible result.
        /// </para>
        /// <para>
        /// The legs and root are off for the reason the whole layer exists: they must keep running
        /// the locomotion tree while the arms hold something.
        /// </para>
        /// </summary>
        private static AvatarMask BuildMask()
        {
            var mask = new AvatarMask();

            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);

            // Hand IK goals ON. Nothing writes a hand goal on this layer today — the scripted
            // right-hand aim that did was deleted with the ADS in Sep 2026 — but these flags govern
            // whether the layer carries IK goal data at all, so switching them off to match the
            // feet would silently rule out ever adding one back. The clips carry no IK curves of
            // their own, so leaving them on costs nothing.
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);

            // Named before it is written anywhere. CopySerialized copies the name too, and an
            // in-memory AvatarMask has none — refreshing the asset therefore blanked it, and Unity
            // warned that the main object name did not match the filename.
            mask.name = MaskName;

            var existing = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mask, existing);
                existing.name = MaskName;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            AssetDatabase.CreateAsset(mask, MaskPath);
            AssetDatabase.SaveAssets();
            return mask;
        }

        private static void BuildLayer(AnimatorController controller, AvatarMask mask)
        {
            var stateMachine = new AnimatorStateMachine
            {
                name = LayerName,
                hideFlags = HideFlags.HideInHierarchy
            };

            AssetDatabase.AddObjectToAsset(stateMachine, controller);

            AnimatorState empty = stateMachine.AddState("Empty");
            stateMachine.defaultState = empty;

            AddStyleState(stateMachine, "Hold Relaxed", RelaxedClip, 1);
            AddStyleState(stateMachine, "Hold OneHanded", OneHandedClip, 2);
            AddStyleState(stateMachine, "Hold TwoHanded", TwoHandedClip, 3);

            // Back to Empty when the hands are. Authored explicitly alongside the others so all
            // four values of HoldStyle are handled by the same mechanism.
            AnyStateTo(stateMachine, empty, 0);

            var layer = new AnimatorControllerLayer
            {
                name = LayerName,
                defaultWeight = 0f,          // PlayerAimRig owns the weight from the first frame.
                avatarMask = mask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                iKPass = true,               // Without this OnAnimatorIK never fires for this layer.
                stateMachine = stateMachine
            };

            controller.AddLayer(layer);
        }

        /// <summary>
        /// One state per hold style, entered from Any State on an integer match.
        ///
        /// <para>
        /// Any State rather than a web of pairwise transitions: with four states that would be
        /// twelve transitions to author and to keep in step, and every one of them would have to
        /// be revisited when a fifth style is added.
        /// </para>
        /// </summary>
        private static void AddStyleState(AnimatorStateMachine sm, string name, string clipPath, int style)
        {
            AnimationClip clip = LoadClip(clipPath);

            AnimatorState state = sm.AddState(name);
            state.motion = clip;
            state.writeDefaultValues = true;

            AnyStateTo(sm, state, style);
        }

        private static void AnyStateTo(AnimatorStateMachine sm, AnimatorState state, int style)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(state);
            t.AddCondition(AnimatorConditionMode.Equals, style, HoldStyleParameter);
            t.duration = 0.15f;
            t.hasExitTime = false;
            t.hasFixedDuration = true;

            // Without this the state re-enters itself every frame the condition holds, which
            // restarts the clip continuously and looks like the pose is frozen on frame one.
            t.canTransitionToSelf = false;
        }

        /// <summary>
        /// The first real AnimationClip inside an imported FBX.
        ///
        /// <para>
        /// <c>LoadAssetAtPath&lt;AnimationClip&gt;</c> on an FBX can return the preview clip rather
        /// than the real one, so the sub-assets are walked instead. The preview is named
        /// <c>__preview__Something</c> and animates nothing.
        /// </para>
        /// </summary>
        private static AnimationClip LoadClip(string path)
        {
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            }

            Debug.LogError($"PlayerUpperBodySetup: no AnimationClip inside '{path}'. " +
                           "The state will be created empty and the pose will not play.");
            return null;
        }

        // ── The gauntlet raise ────────────────────────────────────────────────
        //
        // Three more states on the same layer: the arm a gauntlet is on, extended at what the
        // player is looking at. Each is a 1D blend tree over the look pitch, so the forearm follows
        // the crosshair up and down without a scripted IK goal — which the layer cannot apply
        // while it sits in Empty, i.e. whenever the hands are empty, the ordinary case for a
        // player wearing gauntlets. The left arm plays the right arm's clips MIRRORED; there is
        // no Left set to drift from the Right one. Clips: gauntlet_point.py in the astronaut's
        // source folder.

        private const string ArmRaiseParameter = "ArmRaise";
        private const string AimPitchParameter = "AimPitch";
        private const string ClipDir = "Assets/Game/Art/Animations/Player/";

        /// <summary>The look pitch each of the three clips stands for; the tree blends between them.</summary>
        private static readonly (string suffix, float pitch)[] PitchClips =
        {
            ("Down", -45f),
            ("Level", 0f),
            ("Up", 45f),
        };

        /// <summary>Idempotent: adds the raise states once, and never touches them again.</summary>
        private static void EnsureRaiseStates(AnimatorController controller, AnimatorStateMachine sm)
        {
            if (FindState(sm, "Raise Right") != null) return;

            // The hold styles only apply while no arm is raised. With two parameters on Any State
            // and no such guard, a hold transition and a raise transition would both be true on
            // every frame of a raise, and the arm would flicker between the two states.
            foreach (AnimatorStateTransition t in sm.anyStateTransitions)
            {
                if (!Mentions(t, HoldStyleParameter) || Mentions(t, ArmRaiseParameter)) continue;
                t.AddCondition(AnimatorConditionMode.Equals, 0, ArmRaiseParameter);
            }

            AddRaiseState(controller, sm, "Raise Left", "Right", mirror: true, value: 1);
            AddRaiseState(controller, sm, "Raise Right", "Right", mirror: false, value: 2);
            AddRaiseState(controller, sm, "Raise Both", "Both", mirror: false, value: 3);
        }

        private static void AddRaiseState(AnimatorController controller, AnimatorStateMachine sm,
                                          string name, string arm, bool mirror, int value)
        {
            var tree = new BlendTree
            {
                name = name + " Tree",
                blendType = BlendTreeType.Simple1D,
                blendParameter = AimPitchParameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            foreach ((string suffix, float pitch) in PitchClips)
                tree.AddChild(LoadClip($"{ClipDir}Point {arm} {suffix}.fbx"), pitch);

            AnimatorState state = sm.AddState(name);
            state.motion = tree;
            state.mirror = mirror;
            state.writeDefaultValues = true;

            AnimatorStateTransition t = sm.AddAnyStateTransition(state);
            t.AddCondition(AnimatorConditionMode.Equals, value, ArmRaiseParameter);
            t.duration = 0.12f;
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.canTransitionToSelf = false;
        }

        private static bool Mentions(AnimatorStateTransition t, string parameter)
        {
            foreach (AnimatorCondition c in t.conditions)
                if (c.parameter == parameter) return true;
            return false;
        }

        private static AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            foreach (ChildAnimatorState child in sm.states)
                if (child.state != null && child.state.name == name) return child.state;
            return null;
        }

        private static void EnsureFloatParameter(AnimatorController controller, string name)
        {
            AnimatorControllerParameter[] ps = controller.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].name == name) return;
            controller.AddParameter(name, AnimatorControllerParameterType.Float);
        }

        private static void EnsureIntParameter(AnimatorController controller, string name)
        {
            AnimatorControllerParameter[] ps = controller.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].name == name) return;

            controller.AddParameter(name, AnimatorControllerParameterType.Int);
        }

        private static int FindLayer(AnimatorController controller, string name)
        {
            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].name == name) return i;
            return -1;
        }
    }
}
