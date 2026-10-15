using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the crew's place in every objective chain: which step, whether its setup has run, and
    /// for a side chain whether anybody has accepted it.
    ///
    /// <para>
    /// A global saver, like the game timer beside it: the chains belong to the world rather than to
    /// any object in it, and must be written even when no chunk is loaded.
    /// </para>
    /// <para>
    /// <b>Ids, never indices.</b> A chain is written as its <see cref="ObjectiveChain.Id"/> and a step
    /// as its <see cref="ObjectiveStep.Id"/>, so chains and steps can be added and reordered without
    /// an old save landing on the wrong one. What a step spawned — the module in the sand, the artifact
    /// by the wreck — is saved as the runtime entity it is, not here.
    /// </para>
    /// <para>
    /// <b>Saves from before side chains</b> hold one chain's fields at the top level
    /// (<c>step</c>, <c>complete</c>, <c>begun</c>) and no <c>chains</c>. They are read as the main
    /// chain's record.
    /// </para>
    /// </summary>
    public class ObjectiveSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "objectives";   // written into save files — NEVER rename

        [Tooltip("Optional. Left empty, the director on this GameObject is used, then the live one.")]
        [SerializeField] private ObjectiveDirector director;

        public string SaveKey => Key;

        public struct ChainState
        {
            public string chain;

            /// <summary>The current step's id. Null once the chain is finished.</summary>
            public string step;

            public bool complete;

            /// <summary>The current step's one-time setup has already run.</summary>
            public bool begun;

            /// <summary>Somebody accepted this side chain. Ignored for the main chain.</summary>
            public bool started;
        }

        /// <summary>What is written.</summary>
        public struct Record
        {
            public List<ChainState> chains;
        }

        /// <summary>What is read: a <see cref="Record"/>, or a save from before side chains.</summary>
        public struct State
        {
            public List<ChainState> chains;

            // A save from before side chains: the main chain, at the top level.
            public string step;
            public bool complete;
            public bool begun;
        }

        public object CaptureState()
        {
            ObjectiveDirector live = Resolve();
            if (live == null || !live.IsRunning) return null;

            var chains = new List<ChainState>(live.Runs.Count);
            foreach (ObjectiveRun run in live.Runs)
            {
                // A side chain nobody accepted is the default; leaving it out keeps the record short.
                if (run.Chain == null || !run.IsStarted) continue;

                chains.Add(new ChainState
                {
                    chain = run.Chain.Id,
                    step = run.CurrentStepId,
                    complete = run.IsComplete,
                    begun = run.Progress.Begun,
                    started = run.Progress.Started,
                });
            }

            return new Record { chains = chains };
        }

        public void RestoreState(JObject state)
        {
            ObjectiveDirector live = Resolve();
            if (live == null || state == null) return;

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            live.Restore(ToProgress(live, restored));
        }

        /// <summary>Each saved record as progress through its chain, keyed by chain id.</summary>
        public static Dictionary<string, ObjectiveProgress> ToProgress(ObjectiveDirector live, State restored)
        {
            var records = new Dictionary<string, ObjectiveProgress>();

            if (restored.chains == null)
            {
                ObjectiveRun main = live.Runs.Count > 0 ? live.Runs[0] : null;
                if (main != null)
                    records[main.Chain.Id] = ObjectiveDirector.Resolve(main.Chain, restored.step,
                                                                       restored.complete, restored.begun,
                                                                       started: false);
                return records;
            }

            foreach (ChainState saved in restored.chains)
            {
                if (string.IsNullOrEmpty(saved.chain)) continue;

                ObjectiveRun run = live.Find(saved.chain);

                // A chain this world no longer has keeps its name in the map, so the director can say so.
                records[saved.chain] = run == null
                    ? default
                    : ObjectiveDirector.Resolve(run.Chain, saved.step, saved.complete, saved.begun, saved.started);
            }

            return records;
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
