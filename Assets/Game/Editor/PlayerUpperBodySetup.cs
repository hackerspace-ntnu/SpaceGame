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

            int index = FindLayer(controller, LayerName);
            if (index >= 0)
            {
                AnimatorControllerLayer[] layers = controller.layers;
                layers[index].avatarMask = mask;
                controller.layers = layers;

                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                Debug.Log($"PlayerUpperBodySetup: layer '{LayerName}' already exists at index " +
                          $"{index}. Mask refreshed; states left alone.");
                return;
            }

            BuildLayer(controller, mask);

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

            // Hand IK goals ON, and this is not a detail.
            //
            // These flags govern whether the layer carries IK goal data, and PlayerAimRig's whole
            // aim is a scripted right-hand goal written in OnAnimatorIK. Switching them off to
            // match the feet was the tidy-looking choice and risks the layer discarding the one
            // thing the layer exists to do — a failure that produces no error and no warning, just
            // an arm that never comes up. The clips carry no IK curves of their own, so leaving
            // these on costs nothing.
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
