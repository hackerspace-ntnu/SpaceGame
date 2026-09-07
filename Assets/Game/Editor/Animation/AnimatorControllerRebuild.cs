// Rebuilding an animator controller a prefab already references, without losing it.
//
// Two ways of "starting over" on a controller asset have each produced a controller with no
// states and a clean console:
//
//   * Delete the asset and create it again: the GUID changes, and anything not rewritten in the
//     same run -- another prefab, an override controller -- keeps a reference to nothing.
//   * Keep the asset but DestroyImmediate every sub-asset by hand and reset `layers`: the
//     controller saves correctly at the time and reads back correctly at the time, and then, later
//     in the same editor session, the in-memory object turns up EMPTY and DIRTY -- three
//     objects where the file has thirteen -- and the next AssetDatabase save writes that empty
//     object over the good file (RobotHorse.controller, 2026-09-07 20:10; the Clanker's
//     "animation didn't work" that afternoon was the same corpse).
//
// What has never done either is Appa's way: empty the controller through the AnimatorController
// API -- RemoveState, RemoveAnyStateTransition, RemoveParameter -- which destroys what it created
// with the bookkeeping it expects, keep the layer and its state machine (same fileID every
// build), and add the new states into it. Only the blend trees, which the builders add to the
// asset by hand, are taken out by hand.
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class AnimatorControllerRebuild
    {
        /// <summary>
        /// The controller at <paramref name="path"/>, emptied and ready to be rebuilt into, or a
        /// new one there. Exactly one layer, no states, no parameters, no blend trees.
        /// </summary>
        public static AnimatorController LoadOrCreateEmpty(string path)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
                return AnimatorController.CreateAnimatorControllerAtPath(path);

            for (int i = controller.layers.Length - 1; i >= 1; i--)
                controller.RemoveLayer(i);
            if (controller.layers.Length == 0)
                controller.AddLayer("Base Layer");

            // Transitions reference states, so they go first; the states go last.
            AnimatorStateMachine sm = controller.layers[0].stateMachine;
            foreach (AnimatorStateTransition t in sm.anyStateTransitions.ToArray())
                sm.RemoveAnyStateTransition(t);
            foreach (AnimatorTransition t in sm.entryTransitions.ToArray())
                sm.RemoveEntryTransition(t);
            foreach (ChildAnimatorStateMachine child in sm.stateMachines.ToArray())
                sm.RemoveStateMachine(child.stateMachine);
            foreach (ChildAnimatorState child in sm.states.ToArray())
                sm.RemoveState(child.state);

            while (controller.parameters.Length > 0)
                controller.RemoveParameter(0);

            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (sub is not BlendTree tree) continue;
                AssetDatabase.RemoveObjectFromAsset(tree);
                Object.DestroyImmediate(tree, true);
            }

            return controller;
        }

        /// <summary>
        /// Save, reimport from disk, and refuse a controller whose states did not survive. What
        /// comes back is the object every later load will return, so callers wire prefabs to it.
        /// </summary>
        public static AnimatorController SaveAndVerify(AnimatorController controller, string path, int minimumStates)
        {
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var saved = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            AnimatorStateMachine root = saved != null && saved.layers.Length > 0 ? saved.layers[0].stateMachine : null;
            int onDisk = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimatorState>().Count();
            if (root == null || root.states.Length < minimumStates || onDisk < minimumStates
                || root.defaultState == null || root.defaultState.motion == null)
            {
                throw new System.InvalidOperationException(
                    $"{path} did not keep its states after saving ({root?.states.Length ?? 0} in memory, " +
                    $"{onDisk} on disk, default {root?.defaultState?.name ?? "none"}). An animator with a " +
                    "controller that has no states plays nothing and says nothing.");
            }
            return saved;
        }
    }
}
