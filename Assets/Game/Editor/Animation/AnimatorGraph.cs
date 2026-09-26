using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The handful of AnimatorController operations the humanoid builder is made of: a layer with
    /// its own state machine, a state, a blend tree, a transition.
    ///
    /// <para>
    /// Every sub-object is added to the controller asset explicitly. A state machine or blend tree
    /// that is only referenced, never added, saves as a dangling reference: the controller reads
    /// back correctly in the session that built it and plays nothing after the next reload.
    /// </para>
    /// </summary>
    internal static class AnimatorGraph
    {
        public static AnimatorStateMachine AddLayer(AnimatorController controller, string name, AvatarMask mask,
                                                    float defaultWeight, bool ikPass,
                                                    AnimatorLayerBlendingMode blending = AnimatorLayerBlendingMode.Override)
        {
            var stateMachine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(stateMachine, controller);

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = name,
                stateMachine = stateMachine,
                avatarMask = mask,
                defaultWeight = defaultWeight,
                blendingMode = blending,
                iKPass = ikPass
            });
            return stateMachine;
        }

        /// <summary>A state playing <paramref name="motion"/>, Write Defaults on (every state is).</summary>
        public static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion motion)
        {
            AnimatorState state = sm.AddState(name);
            state.motion = motion;
            state.writeDefaultValues = true;
            return state;
        }

        public static BlendTree Tree1D(AnimatorController controller, string name, string parameter,
                                       params (Motion motion, float threshold)[] children)
        {
            BlendTree tree = NewTree(controller, name, BlendTreeType.Simple1D, parameter);
            tree.useAutomaticThresholds = false;
            foreach ((Motion motion, float threshold) in children)
                tree.AddChild(motion, threshold);
            return tree;
        }

        public static BlendTree Tree2D(AnimatorController controller, string name, string parameterX,
                                       string parameterY, params (Motion motion, Vector2 position)[] children)
        {
            BlendTree tree = NewTree(controller, name, BlendTreeType.FreeformDirectional2D, parameterX);
            tree.blendParameterY = parameterY;
            foreach ((Motion motion, Vector2 position) in children)
                tree.AddChild(motion, position);
            return tree;
        }

        /// <summary>A transition on conditions alone, no exit time.</summary>
        public static AnimatorStateTransition When(AnimatorState from, AnimatorState to, float duration,
                                                   params (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            Configure(t, duration, conditions);
            return t;
        }

        /// <summary>An Any State transition that cannot re-enter its own state every frame.</summary>
        public static AnimatorStateTransition AnyWhen(AnimatorStateMachine sm, AnimatorState to, float duration,
                                                      params (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(to);
            Configure(t, duration, conditions);

            // Without this the state re-enters itself on every frame the condition holds, which
            // restarts the clip continuously and reads as a pose frozen on frame one.
            t.canTransitionToSelf = false;
            return t;
        }

        /// <summary>A transition taken at <paramref name="exitTime"/> of the source clip.</summary>
        public static AnimatorStateTransition AtEnd(AnimatorState from, AnimatorState to, float exitTime, float duration,
                                                    params (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            Configure(t, duration, conditions);
            t.hasExitTime = true;
            t.exitTime = exitTime;
            return t;
        }

        public static (AnimatorConditionMode, float, string) If(string parameter) =>
            (AnimatorConditionMode.If, 0f, parameter);

        public static (AnimatorConditionMode, float, string) IfNot(string parameter) =>
            (AnimatorConditionMode.IfNot, 0f, parameter);

        public static (AnimatorConditionMode, float, string) Is(string parameter, int value) =>
            (AnimatorConditionMode.Equals, value, parameter);

        public static (AnimatorConditionMode, float, string) Greater(string parameter, float value) =>
            (AnimatorConditionMode.Greater, value, parameter);

        private static BlendTree NewTree(AnimatorController controller, string name, BlendTreeType type, string parameter)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = type,
                blendParameter = parameter,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            return tree;
        }

        private static void Configure(AnimatorStateTransition t, float duration,
                                      (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
        {
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = duration;
            foreach ((AnimatorConditionMode mode, float threshold, string parameter) in conditions)
                t.AddCondition(mode, threshold, parameter);
        }
    }
}
