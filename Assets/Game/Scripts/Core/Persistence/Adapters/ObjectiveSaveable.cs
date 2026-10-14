using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the crew's quests: for each, which step, and whether its setup has run.
    ///
    /// <para>
    /// A global saver, like the game timer beside it: the quests belong to the world rather than to
    /// any object in it, and must be written even when no chunk is loaded.
    /// </para>
    /// <para>
    /// <b>Ids, never indices.</b> Each quest is written as its <see cref="ObjectiveChain.Id"/> and its step as its
    /// <see cref="ObjectiveStep.Id"/>, so quests and steps can be added without an old save landing on the wrong one.
    /// The story quest keeps the record's top-level fields, which is all a save from before side quests has; every
    /// other quest is one entry of <see cref="State.quests"/>. What a step spawned — the module in the sand, the
    /// artifact by the wreck — is saved as the runtime entity it is, not here.
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
            /// <summary>The story quest's current step id. Null once it is finished.</summary>
            public string step;

            public bool complete;

            /// <summary>The story quest's current step's one-time setup has already run.</summary>
            public bool begun;

            /// <summary>Which of the story quest's current step's remarks have been said, by bit. Absent (0) in older saves.</summary>
            public int remarks;

            /// <summary>Every other quest the crew have taken on. Absent in older saves.</summary>
            public List<QuestRecord> quests;
        }

        public struct QuestRecord
        {
            public string id;

            /// <summary>When it was taken on, relative to the others: the newest is the active quest after a load.</summary>
            public int order;

            public string step;
            public bool complete;
            public bool begun;
            public int remarks;
        }

        public object CaptureState()
        {
            ObjectiveDirector live = Resolve();
            if (live == null || !live.IsRunning || live.Story == null) return null;

            SavedQuest story = live.Story.ToSaved();
            var state = new State
            {
                step = story.Step,
                complete = story.Complete,
                begun = story.Begun,
                remarks = story.Remarks,
                quests = new List<QuestRecord>(),
            };

            foreach (Quest quest in live.Quests)
            {
                if (quest == live.Story) continue;

                SavedQuest saved = quest.ToSaved();
                state.quests.Add(new QuestRecord
                {
                    id = saved.Id, order = saved.Order, step = saved.Step,
                    complete = saved.Complete, begun = saved.Begun, remarks = saved.Remarks,
                });
            }

            return state;
        }

        public void RestoreState(JObject state)
        {
            ObjectiveDirector live = Resolve();
            if (live == null || live.Story == null || state == null) return;

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            var saved = new List<SavedQuest>
            {
                new() { Id = live.Story.Id, Order = 0, Step = restored.step, Complete = restored.complete,
                        Begun = restored.begun, Remarks = restored.remarks },
            };

            if (restored.quests != null)
                foreach (QuestRecord quest in restored.quests)
                    saved.Add(new SavedQuest
                    {
                        Id = quest.id, Order = quest.order, Step = quest.step,
                        Complete = quest.complete, Begun = quest.begun, Remarks = quest.remarks,
                    });

            live.Restore(saved);
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
