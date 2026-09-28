using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// A controller as plain text: parameters, layers, states, motions, transitions — everything
    /// that decides how it plays, and nothing that does not (fileIDs, editor positions).
    ///
    /// <para>
    /// Two controllers that describe the same play the same. The builder compares descriptions to
    /// leave an up-to-date controller untouched — every rebuild otherwise mints fresh fileIDs for
    /// every state and rewrites the whole file for git — and the Audit prints the difference
    /// between the live controller and what a rebuild would write, which is how a hand edit that a
    /// rebuild is about to erase gets noticed first.
    /// </para>
    /// </summary>
    internal static class AnimatorDescription
    {
        public static string Describe(AnimatorController controller)
        {
            var text = new StringBuilder();
            foreach (AnimatorControllerParameter p in controller.parameters)
                text.AppendLine($"param {p.name} {p.type} {F(p.defaultFloat)} {p.defaultInt} {p.defaultBool}");

            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                AnimatorControllerLayer l = layers[i];
                text.AppendLine($"layer {i} '{l.name}' mask={Ref(l.avatarMask)} weight={F(l.defaultWeight)} " +
                                $"ik={l.iKPass} blend={l.blendingMode} sync={l.syncedLayerIndex}");
                DescribeMachine(text, l.stateMachine, "  ");
            }
            return text.ToString();
        }

        private static void DescribeMachine(StringBuilder text, AnimatorStateMachine sm, string indent)
        {
            text.AppendLine($"{indent}default '{(sm.defaultState != null ? sm.defaultState.name : "none")}'");

            foreach (ChildAnimatorState child in sm.states)
            {
                AnimatorState s = child.state;
                text.AppendLine($"{indent}state '{s.name}' speed={F(s.speed)} " +
                                $"speedParam={Param(s.speedParameterActive, s.speedParameter)} " +
                                $"mirror={s.mirror} mirrorParam={Param(s.mirrorParameterActive, s.mirrorParameter)} " +
                                $"cycle={F(s.cycleOffset)} cycleParam={Param(s.cycleOffsetParameterActive, s.cycleOffsetParameter)} " +
                                $"wd={s.writeDefaultValues} behaviours={s.behaviours.Length}");
                text.AppendLine($"{indent}  motion {Motion(s.motion)}");
                foreach (AnimatorStateTransition t in s.transitions)
                    text.AppendLine($"{indent}  {Transition(t)}");
            }

            foreach (AnimatorStateTransition t in sm.anyStateTransitions)
                text.AppendLine($"{indent}any {Transition(t)}");

            foreach (ChildAnimatorStateMachine child in sm.stateMachines)
            {
                text.AppendLine($"{indent}machine '{child.stateMachine.name}'");
                DescribeMachine(text, child.stateMachine, indent + "  ");
            }
        }

        private static string Motion(Motion motion)
        {
            if (motion == null) return "none";
            if (!(motion is BlendTree tree)) return $"clip '{motion.name}' {Ref(motion)}";

            var text = new StringBuilder($"tree {tree.blendType} {tree.blendParameter} {tree.blendParameterY} [");
            foreach (ChildMotion c in tree.children)
            {
                text.Append($" ({F(c.threshold)} {F(c.position.x)},{F(c.position.y)} ts={F(c.timeScale)} " +
                            $"mirror={c.mirror} {Motion(c.motion)})");
            }
            return text.Append(" ]").ToString();
        }

        private static string Transition(AnimatorStateTransition t)
        {
            string to = t.destinationState != null ? t.destinationState.name
                : t.destinationStateMachine != null ? "machine " + t.destinationStateMachine.name
                : t.isExit ? "exit" : "none";

            var text = new StringBuilder($"-> '{to}' dur={F(t.duration)} fixed={t.hasFixedDuration} " +
                                         $"exit={(t.hasExitTime ? F(t.exitTime) : "no")} offset={F(t.offset)} " +
                                         $"interrupt={t.interruptionSource} self={t.canTransitionToSelf} [");
            foreach (AnimatorCondition c in t.conditions)
                text.Append($" {c.parameter} {c.mode} {F(c.threshold)}");
            return text.Append(" ]").ToString();
        }

        private static string Param(bool active, string name) => active ? name : "-";

        private static string Ref(Object asset)
        {
            if (asset == null) return "none";
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id)
                ? $"{guid}:{id}"
                : "unsaved";
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
