using System.Collections.Generic;
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

        private const string WornLeftMaskPath = "Assets/Game/Art/Animations/Player/WornLeftArm.mask";
        private const string WornLeftLayerName = "Worn Left";
        private const string WornLeftMaskName = "WornLeftArm";

        /// <summary>
        /// The pose parameter of the <see cref="WornLeftLayerName"/> layer. Written by
        /// <c>PlayerAimRig.LeftArmStyle</c>, which is non-zero only while the layer above is
        /// already posing for the RIGHT arm's device — the one case a single pose cannot cover.
        /// </summary>
        private const string WornLeftStyleParameter = "WornLeftStyle";

        /// <summary>
        /// The pose parameter. Written by <c>PlayerAimRig</c> from what is in the hand — or, with
        /// empty hands, from a lit Flashlight Gauntlet, which borrows this same pose rather than
        /// carrying one of its own. One parameter, so the two can never both be on.
        /// </summary>
        private const string HoldStyleParameter = "HoldStyle";

        /// <summary>
        /// Bool selecting the mirrored twin of whichever hold state <see cref="HoldStyleParameter"/>
        /// names. Written by <c>PlayerAimRig</c> when the LEFT arm is what asked for the pose — a
        /// lit Flashlight Gauntlet on that wrist. The clips are all right-handed, so without it a
        /// left-arm lamp raises the right arm and lights nothing.
        /// </summary>
        private const string HoldMirrorParameter = "HoldMirror";

        private const string RelaxedClip = "Assets/ThirdParty/Kevin Iglesias/Human Animations/Animations/Male/Combat/Gun/HumanM@Gun_Aim02.fbx";
        private const string OneHandedClip = "Assets/ThirdParty/Kevin Iglesias/Human Animations/Animations/Male/Combat/Gun/HumanM@Gun_Aim01.fbx";
        private const string TwoHandedClip = "Assets/ThirdParty/Kevin Iglesias/Human Animations/Animations/Male/Combat/AssaultRifle/AssultRifleIdle.fbx";

        /// <summary>
        /// The three poses, and the value of <see cref="HoldStyleParameter"/> each answers to.
        /// One table because the mirrored twins below are built from the same rows — a fourth
        /// style added here gets its mirror for nothing.
        /// </summary>
        private static readonly (string Name, string Clip, int Style)[] HoldStyles =
        {
            ("Hold Relaxed", RelaxedClip, 1),
            ("Hold OneHanded", OneHandedClip, 2),
            ("Hold TwoHanded", TwoHandedClip, 3),
        };

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
            EnsureBoolParameter(controller, HoldMirrorParameter);
            EnsureIntParameter(controller, WornLeftStyleParameter);
            EnsureIntParameter(controller, ArmRaiseParameter);
            EnsureFloatParameter(controller, AimPitchParameter);

            int index = FindLayer(controller, LayerName);
            if (index >= 0)
            {
                AnimatorControllerLayer[] layers = controller.layers;
                layers[index].avatarMask = mask;
                controller.layers = layers;

                EnsureRaiseStates(controller, layers[index].stateMachine);
                EnsureMirroredHoldStates(layers[index].stateMachine);

                Debug.Log($"PlayerUpperBodySetup: layer '{LayerName}' already exists at index " +
                          $"{index}. Mask refreshed; hold states left alone; raise and mirrored " +
                          "states ensured.");
            }
            else
            {
                BuildLayer(controller, mask);
                EnsureRaiseStates(controller, controller.layers[controller.layers.Length - 1].stateMachine);
                EnsureMirroredHoldStates(controller.layers[controller.layers.Length - 1].stateMachine);

                Debug.Log($"PlayerUpperBodySetup: built '{LayerName}' at index {controller.layers.Length - 1}.");
            }

            EnsureWornLeftLayer(controller);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
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

            return SaveMask(mask, MaskPath, MaskName);
        }

        /// <summary>
        /// The LEFT arm and nothing else — the mask of the second layer.
        ///
        /// <para>
        /// No Body here, unlike the mask above. This layer only ever runs while the layer beneath
        /// it is posing the chest for the other arm's device, and a second opinion about the chest
        /// would fight it: the torso would take the left arm's pose and the right arm would stand
        /// in a posture its own clip never asked for.
        /// </para>
        /// </summary>
        private static AvatarMask BuildWornLeftMask()
        {
            var mask = new AvatarMask();

            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, false);

            return SaveMask(mask, WornLeftMaskPath, WornLeftMaskName);
        }

        /// <summary>
        /// Write a freshly built mask over the asset at <paramref name="path"/>, or create it.
        ///
        /// <para>
        /// The name is set before anything is written. <c>CopySerialized</c> copies the name too,
        /// and an in-memory AvatarMask has none — refreshing the asset therefore blanked it, and
        /// Unity warned that the main object name did not match the filename.
        /// </para>
        /// </summary>
        private static AvatarMask SaveMask(AvatarMask mask, string path, string name)
        {
            mask.name = name;

            var existing = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mask, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            AssetDatabase.CreateAsset(mask, path);
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

            foreach ((string name, string clip, int style) in HoldStyles)
                AddStyleState(stateMachine, name, clip, style);

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

        private static AnimatorStateTransition AnyStateTo(AnimatorStateMachine sm, AnimatorState state, int style)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(state);
            t.AddCondition(AnimatorConditionMode.Equals, style, HoldStyleParameter);
            t.duration = 0.15f;
            t.hasExitTime = false;
            t.hasFixedDuration = true;

            // Without this the state re-enters itself every frame the condition holds, which
            // restarts the clip continuously and looks like the pose is frozen on frame one.
            t.canTransitionToSelf = false;

            return t;
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
        // no Left set to drift from the Right one. Clips: the gauntlet-point set in the astronaut's
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

        // ── The mirrored hold poses ───────────────────────────────────────────
        //
        // Every hold clip is right-handed. Gun_Aim01, the one-handed pose almost everything uses,
        // puts the right hand 0.19 up and 0.19 forward of the body centre and leaves the left one
        // down at the hip. That is right for a held item, whichever hand grips it — the pose is how
        // the body stands around the thing, and mirroring it would swap the shoulder every
        // two-handed item is braced against. It is wrong for the other thing that asks for this
        // pose: a lit Flashlight Gauntlet, which can be worn on either forearm and whose beam
        // leaves along the arm it is on. Unmirrored, a left-arm lamp raises the EMPTY arm.
        //
        // So each hold state gets a twin that plays the same clip mirrored, and HoldMirror picks
        // between them. A bool rather than more values of HoldStyle: mirroring is a fact about the
        // ARM asking, not about the pose, and folding it into the style enum would double it.

        private static string MirroredName(string holdState) => holdState + " Mirrored";

        /// <summary>Idempotent, like the raise states: builds the twins once and never again.</summary>
        private static void EnsureMirroredHoldStates(AnimatorStateMachine sm)
        {
            if (FindState(sm, MirroredName(HoldStyles[0].Name)) != null) return;

            // The existing hold states are the UNmirrored half now, and have to say so, or both
            // twins are live on every frame a left-arm lamp is on and the arms flicker between
            // them. Empty (style 0) is left alone: it is where the layer goes when nothing is
            // posing, and it must be reachable whatever the mirror says.
            foreach (AnimatorStateTransition t in sm.anyStateTransitions)
            {
                if (StyleOf(t) <= 0 || Mentions(t, HoldMirrorParameter)) continue;
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, HoldMirrorParameter);
            }

            foreach ((string name, string clip, int style) in HoldStyles)
                AddMirroredStyleState(sm, name, clip, style);
        }

        private static void AddMirroredStyleState(AnimatorStateMachine sm, string holdState,
                                                  string clipPath, int style)
        {
            AnimatorState state = sm.AddState(MirroredName(holdState));
            state.motion = LoadClip(clipPath);
            state.mirror = true;
            state.writeDefaultValues = true;

            AnimatorStateTransition t = AnyStateTo(sm, state, style);
            t.AddCondition(AnimatorConditionMode.If, 0f, HoldMirrorParameter);

            // Spelled out rather than inherited from EnsureRaiseStates, which only ever runs its
            // patch pass once and will not see these: a raise still outranks a hold, mirrored or
            // not, so both arms do not try to point at once.
            t.AddCondition(AnimatorConditionMode.Equals, 0, ArmRaiseParameter);
        }

        // ── The left arm's own layer ──────────────────────────────────────────
        //
        // The layer above answers ONE ask: a held item, or the pose a working forearm device wants.
        // A player with a lit torch on one wrist and a powered scanner on the other makes two, and
        // the right arm's used to win outright — the left arm hung at the side with its lamp
        // lighting the ground, which is a device reporting its own state wrongly.
        //
        // Masked to the left arm alone, so it can carry that arm while the layer beneath carries
        // the chest and the right arm. Same clips, mirrored, entered on a parameter of its own;
        // PlayerAimRig.LeftArmStyle is non-zero only in that two-device case, so the two layers
        // never describe the same limb.

        private static string WornLeftName(string holdState) => "Worn Left " + holdState;

        /// <summary>Idempotent: refreshes the mask, and builds the layer the first time only.</summary>
        private static void EnsureWornLeftLayer(AnimatorController controller)
        {
            AvatarMask mask = BuildWornLeftMask();

            int index = FindLayer(controller, WornLeftLayerName);
            if (index >= 0)
            {
                AnimatorControllerLayer[] existing = controller.layers;
                existing[index].avatarMask = mask;
                controller.layers = existing;

                Debug.Log($"PlayerUpperBodySetup: layer '{WornLeftLayerName}' already exists at " +
                          $"index {index}. Mask refreshed; states left alone.");
                return;
            }

            var stateMachine = new AnimatorStateMachine
            {
                name = WornLeftLayerName,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(stateMachine, controller);

            AnimatorState empty = stateMachine.AddState("Empty");
            stateMachine.defaultState = empty;
            WornLeftAnyStateTo(stateMachine, empty, 0);

            foreach ((string name, string clip, int style) in HoldStyles)
            {
                AnimatorState state = stateMachine.AddState(WornLeftName(name));
                state.motion = LoadClip(clip);

                // Every hold clip is right-handed — see the mirrored twins above. This layer is
                // the left arm's, so every state on it is mirrored; there is no unmirrored half
                // and therefore no bool to pick between them.
                state.mirror = true;
                state.writeDefaultValues = true;

                WornLeftAnyStateTo(stateMachine, state, style);
            }

            var layer = new AnimatorControllerLayer
            {
                name = WornLeftLayerName,
                defaultWeight = 0f,          // PlayerAimRig owns the weight from the first frame.
                avatarMask = mask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                iKPass = true,
                stateMachine = stateMachine
            };

            controller.AddLayer(layer);
            MoveLayerAfter(controller, WornLeftLayerName, LayerName);

            Debug.Log($"PlayerUpperBodySetup: built '{WornLeftLayerName}' after '{LayerName}'.");
        }

        private static void WornLeftAnyStateTo(AnimatorStateMachine sm, AnimatorState state, int style)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(state);
            t.AddCondition(AnimatorConditionMode.Equals, style, WornLeftStyleParameter);
            t.duration = 0.15f;
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.canTransitionToSelf = false;
        }

        /// <summary>
        /// Put <paramref name="layerName"/> immediately after <paramref name="afterName"/>.
        ///
        /// <para>
        /// <c>AddLayer</c> appends, and a layer appended to the end would sit above the Glide
        /// layer — a gliding player's arms are the wingsuit's, and a torch on one wrist must not
        /// pull one of them out of the glide. Directly above the layer whose limb it is overriding
        /// and nowhere else.
        /// </para>
        /// </summary>
        private static void MoveLayerAfter(AnimatorController controller, string layerName, string afterName)
        {
            var layers = new List<AnimatorControllerLayer>(controller.layers);

            int from = layers.FindIndex(l => l.name == layerName);
            int after = layers.FindIndex(l => l.name == afterName);
            if (from < 0 || after < 0) return;

            AnimatorControllerLayer moving = layers[from];
            layers.RemoveAt(from);

            after = layers.FindIndex(l => l.name == afterName);
            layers.Insert(after + 1, moving);

            controller.layers = layers.ToArray();
        }

        /// <summary>
        /// The <see cref="HoldStyleParameter"/> value this Any State transition fires on, or -1
        /// where it does not name one at all (a raise transition).
        /// </summary>
        private static int StyleOf(AnimatorStateTransition t)
        {
            foreach (AnimatorCondition c in t.conditions)
                if (c.parameter == HoldStyleParameter) return Mathf.RoundToInt(c.threshold);
            return -1;
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

        private static void EnsureBoolParameter(AnimatorController controller, string name)
        {
            AnimatorControllerParameter[] ps = controller.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].name == name) return;
            controller.AddParameter(name, AnimatorControllerParameterType.Bool);
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
