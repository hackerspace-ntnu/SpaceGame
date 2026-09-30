using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the crew's place in the objective chain: which step, and whether its setup has run.
    ///
    /// <para>
    /// A global saver, like the game timer beside it: the chain belongs to the world rather than to
    /// any object in it, and must be written even when no chunk is loaded.
    /// </para>
    /// <para>
    /// <b>Ids, never indices.</b> The step is written as its <see cref="ObjectiveStep.Id"/>, so steps
    /// can be inserted into the chain without an old save landing on the wrong one. What a step
    /// spawned — the module in the sand, the artifact by the wreck — is saved as the runtime entity
    /// it is, not here.
    /// </para>
    /// </summary>
    public class ObjectiveSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "objectives";   // written into save files — NEVER rename

        [Tooltip("Optional. Left empty, the director on this GameObject is used, then the live one.")]
        [SerializeField] private ObjectiveDirector director;

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>The current step's id. Null once the chain is finished.</summary>
            public string step;

            public bool complete;

            /// <summary>The current step's one-time setup has already run.</summary>
            public bool begun;
        }

        public object CaptureState()
        {
            ObjectiveDirector live = Resolve();
            if (live == null || !live.IsRunning) return null;

            return new State
            {
                step = live.CurrentStepId,
                complete = live.IsComplete,
                begun = live.Progress.Begun,
            };
        }

        public void RestoreState(JObject state)
        {
            ObjectiveDirector live = Resolve();
            if (live == null || state == null) return;

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            live.Restore(restored.step, restored.complete, restored.begun);
        }

        /// <summary>
        /// Resolved on demand, for <c>MapSaveable</c>'s reason: <c>SaveManager</c> applies a staged
        /// record the moment a saver registers, which can be before this object's siblings woke.
        /// </summary>
        private ObjectiveDirector Resolve()
        {
            if (director == null) director = GetComponent<ObjectiveDirector>();
            if (director == null) director = ObjectiveDirector.Instance;
            return director;
        }

        private void OnEnable() => SaveManager.RegisterGlobalSaver(this);

        private void OnDisable() => SaveManager.UnregisterGlobalSaver(this);
    }
}
