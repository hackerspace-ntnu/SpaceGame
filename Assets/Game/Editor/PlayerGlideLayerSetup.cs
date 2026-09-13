using SpaceGame.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the player's full-body Glide layer: the pose worn while a wingsuit's wings are out.
    ///
    /// <para>
    /// A layer of its own rather than a state on the Base Layer, because a glide is not a way of
    /// moving along the ground — the locomotion tree underneath it has nothing to say about a body
    /// in the air, and a state wired into it would need transitions from and back to every one of
    /// its neighbours. A layer at weight 1 simply takes the whole body while the bool is true and
    /// gives it straight back.
    /// </para>
    /// <para>
    /// It is added ABOVE Upper Body and is unmasked, which is deliberate: while the wings are out
    /// the arms are the wing, and a hold pose or a gauntlet raise reaching through would fold one
    /// of them in mid-flight.
    /// </para>
    /// <para>
    /// Idempotent, and a script rather than hand-edited YAML for the reason
    /// <see cref="PlayerUpperBodySetup"/> gives: a layer is a state machine, states and transitions,
    /// each with its own fileID, and inventing those by hand is how a controller ends up subtly
    /// corrupt in a way that only shows at runtime.
    /// </para>
    /// </summary>
    internal static class PlayerGlideLayerSetup
    {
        private const string ControllerPath =
            "Assets/Game/Art/Animations/Player/AstronautArmature.controller";

        private const string GlideClip = "Assets/Game/Art/Animations/Player/Glide.fbx";

        private const string LayerName = "Glide";
        private const string StateName = "Gliding";
        private const string EmptyState = "Not Gliding";

        /// <summary>Blend in and out of the pose. Long enough to read as the wings catching.</summary>
        private const float TransitionSeconds = 0.25f;

        [MenuItem("Tools/SpaceGame/Player/Build Glide Layer")]
        public static void Build()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"PlayerGlideLayerSetup: no AnimatorController at {ControllerPath}.");
                return;
            }

            EnsureBoolParameter(controller, WingsuitFlight.GlidingParameter);

            if (FindLayer(controller, LayerName) >= 0)
            {
                Debug.Log($"PlayerGlideLayerSetup: layer '{LayerName}' already exists — left alone. " +
                          "Delete it in the Animator window first if you want it rebuilt.");
                return;
            }

            BuildLayer(controller);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log($"PlayerGlideLayerSetup: built '{LayerName}' at index " +
                      $"{controller.layers.Length - 1}.");
        }

        private static void BuildLayer(AnimatorController controller)
        {
            var stateMachine = new AnimatorStateMachine
            {
                name = LayerName,
                hideFlags = HideFlags.HideInHierarchy
            };

            AssetDatabase.AddObjectToAsset(stateMachine, controller);

            // An empty default state rather than an empty layer: the layer runs at weight 1 the
            // whole time, so what it plays while the wings are folded has to be nothing at all.
            // Weight is not animated, because a state with no motion contributes nothing anyway
            // and a driven weight is one more thing to get wrong on a remote player.
            AnimatorState idle = stateMachine.AddState(EmptyState);
            idle.writeDefaultValues = true;
            stateMachine.defaultState = idle;

            AnimatorState gliding = stateMachine.AddState(StateName);
            gliding.motion = LoadClip(GlideClip);
            gliding.writeDefaultValues = true;

            Transition(stateMachine.AddAnyStateTransition(gliding),
                       AnimatorConditionMode.If, WingsuitFlight.GlidingParameter);

            Transition(stateMachine.AddAnyStateTransition(idle),
                       AnimatorConditionMode.IfNot, WingsuitFlight.GlidingParameter);

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = LayerName,

                // Full weight and no mask. The pose owns the whole body while it is playing, and
                // the empty state is what makes that harmless the rest of the time.
                defaultWeight = 1f,
                avatarMask = null,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = stateMachine
            });
        }

        private static void Transition(AnimatorStateTransition t, AnimatorConditionMode mode,
                                       string parameter)
        {
            t.AddCondition(mode, 0f, parameter);
            t.duration = TransitionSeconds;
            t.hasExitTime = false;
            t.hasFixedDuration = true;

            // Without this the state re-enters itself every frame its condition holds, restarting
            // the clip continuously — which reads as a pose frozen on frame one.
            t.canTransitionToSelf = false;
        }

        /// <summary>
        /// The first real AnimationClip inside an imported FBX. Same trap as
        /// <see cref="PlayerUpperBodySetup"/>: asking for the asset directly can hand back the
        /// <c>__preview__</c> clip, which animates nothing.
        /// </summary>
        private static AnimationClip LoadClip(string path)
        {
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
            }

            Debug.LogError($"PlayerGlideLayerSetup: no AnimationClip inside '{path}'. The state " +
                           "will be created empty and a gliding player will keep whatever pose " +
                           "the layers underneath give them.");
            return null;
        }

        private static void EnsureBoolParameter(AnimatorController controller, string name)
        {
            AnimatorControllerParameter[] ps = controller.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].name == name) return;

            controller.AddParameter(name, AnimatorControllerParameterType.Bool);
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
