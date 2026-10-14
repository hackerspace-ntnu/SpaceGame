using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay.Arrival;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Runs the crew's quests: the story quest (<see cref="chain"/>), begun on landing, and every quest from the
    /// catalog (<see cref="quests"/>) they have taken on since. Each <see cref="Quest"/> is one shared place in one
    /// <see cref="ObjectiveChain"/>, and every started quest advances every frame, whichever one the visor follows.
    ///
    /// <para>
    /// <b>The server decides, every machine presents.</b> Only where <see cref="Network.Decides"/>
    /// does this begin quests and steps and judge them met; everywhere else the quests arrive through
    /// <see cref="ObjectiveNetwork"/> into <see cref="Adopt"/>. The briefing, the visor and the
    /// beacon all read <see cref="Quests"/> on their own machine and add nothing to the wire. A client
    /// sends two things: "my player has done their part" for a step every player must do themselves
    /// (<see cref="LocalPlayerFinished"/>), and "give us this quest" from a <see cref="QuestGiver"/> that
    /// fired on it (<see cref="GiveRequested"/>).
    /// </para>
    /// <para>
    /// <b>Story worlds only.</b> A versus match has one hull per team and no story, so the quests
    /// stand still there, and nothing is saved for them.
    /// </para>
    /// <para>
    /// Lives in <c>persistentScene</c>. No quest moves until the crew have landed — the
    /// bodies are spawned at the hull and seated a frame later, and nothing may be judged while
    /// the ship is still in the air.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]   // awake before the presenters that bind to it in their OnEnable
    public class ObjectiveDirector : MonoBehaviour
    {
        public static ObjectiveDirector Instance { get; private set; }

        [Tooltip("The story quest. Begins when the crew land.")]
        [SerializeField] private ObjectiveChain chain;

        [Tooltip("Every other quest the crew can take on. A quest must be listed here to begin, save and " +
                 "replicate; it begins when its trigger fires or a QuestGiver gives it.")]
        [SerializeField] private ObjectiveChain[] quests = Array.Empty<ObjectiveChain>();

        [Tooltip("How often the server asks the triggers of quests not yet begun, seconds.")]
        [SerializeField, Min(0.05f)] private float triggerCheckSeconds = 0.5f;

        /// <summary>Every quest begun, in the order the crew took them on. The story quest, when there is one, is first.</summary>
        private readonly List<Quest> started = new();

        private float nextTriggerCheck;

        /// <summary>The quests are on this machine's <see cref="QuestLog"/>: true in a story world.</summary>
        private bool listed;

        /// <summary>
        /// A quest's progress changed on this machine. Live (true) when it happened in play — a step reached, a
        /// step begun — and false when it was restored from a save or read off the wire on joining, which a
        /// briefing must not replay.
        /// </summary>
        public event Action<Quest, bool> Changed;

        /// <summary>A quest was begun on this machine. Live (true) when the crew took it on just now, in play.</summary>
        public event Action<Quest, bool> Started;

        /// <summary>
        /// This machine's own player has done their part of the quest's step at this index. Raised once
        /// per step; <see cref="ObjectiveNetwork"/> carries it to the server.
        /// </summary>
        public event Action<Quest, int> LocalPlayerFinished;

        /// <summary>A client was asked to give the crew this quest; <see cref="ObjectiveNetwork"/> asks the server.</summary>
        public event Action<ObjectiveChain> GiveRequested;

        /// <summary>Lookups for whatever asks about the world outside any quest: the quest triggers, a <see cref="QuestGiver"/>.</summary>
        public ObjectiveWorld Lookups { get; } = new();

        /// <summary>Every quest begun, story first, then in the order taken on.</summary>
        public IReadOnlyList<Quest> Quests => started;

        /// <summary>A story world with a story quest to run.</summary>
        public bool IsRunning => chain != null && !VersusSession.IsActive;

        /// <summary>The story quest, or null without one.</summary>
        public Quest Story => started.Count > 0 && started[0].Chain == chain ? started[0] : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[Objectives] A second ObjectiveDirector was destroyed; the crew have " +
                               "one set of quests.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            ValidateCatalog();

            if (chain == null)
                Debug.LogError("[Objectives] No story quest assigned, so the crew have no objectives.", this);
            else
                started.Add(new Quest(chain, 0));
        }

        private void OnEnable()
        {
            // A duplicate being destroyed still gets OnEnable this frame; only the survivor records.
            if (Instance == this) UseChannel.UsedOnServer += OnItemUsed;
        }

        private void OnDisable() => UseChannel.UsedOnServer -= OnItemUsed;

        private void OnDestroy()
        {
            if (Instance != this) return;

            Instance = null;
            List(false);
        }

        private void OnItemUsed(InventoryItem item, GameObject holder)
        {
            foreach (Quest quest in started) quest.World.RecordUse(item);
        }

        private void Update()
        {
            List(IsRunning);
            if (!IsRunning) return;

            for (int i = 0; i < started.Count; i++)
                if (started[i].TrackLocalPlayer())
                    LocalPlayerFinished?.Invoke(started[i], started[i].Progress.Step);

            if (!Network.Decides || !CrewHasLanded) return;

            CheckTriggers();

            // By index: a quest begun by another's change is appended, and advances from next frame.
            for (int i = 0; i < started.Count; i++)
                if (started[i].Advance())
                    Changed?.Invoke(started[i], true);
        }

        private static bool CrewHasLanded => ArrivalDirector.CrewHasLanded;

        /// <summary>Puts every quest on this machine's quest log, or takes them all off it.</summary>
        private void List(bool running)
        {
            if (listed == running) return;

            listed = running;
            foreach (Quest quest in started)
            {
                if (running) QuestLog.Open(quest, acquired: false);
                else QuestLog.Close(quest);
            }
        }

        // ── Lookup ──────────────────────────────────────────────────────────────

        /// <summary>The begun quest with this id, or null.</summary>
        public Quest Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            foreach (Quest quest in started)
                if (quest.Id == id) return quest;

            return null;
        }

        /// <summary>The begun quest of this chain, or null.</summary>
        public Quest Find(ObjectiveChain quest) => quest != null ? Find(quest.Id) : null;

        /// <summary>The story quest or the catalog quest with this id, begun or not; null for none.</summary>
        public ObjectiveChain FindChain(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (chain != null && chain.Id == id) return chain;

            foreach (ObjectiveChain quest in quests)
                if (quest != null && quest.Id == id) return quest;

            return null;
        }

        // ── Beginning a quest ───────────────────────────────────────────────────

        /// <summary>
        /// ANY machine: give the crew <paramref name="quest"/>. The server begins it; a client asks the server.
        /// Giving a quest the crew already have does nothing.
        /// </summary>
        public void Give(ObjectiveChain quest)
        {
            if (quest == null || !IsRunning || Find(quest) != null) return;

            if (Network.Decides) StartQuest(quest);
            else GiveRequested?.Invoke(quest);
        }

        /// <summary>
        /// SERVER: begins the catalog quest <paramref name="quest"/> and makes it every player's active quest.
        /// False when it has already begun, is not in the catalog, or this is not a story world.
        /// </summary>
        public bool StartQuest(ObjectiveChain quest)
        {
            if (quest == null || !IsRunning || Find(quest) != null) return false;

            if (quest == chain || FindChain(quest.Id) != quest)
            {
                Debug.LogError($"[Objectives] '{quest.name}' is not in the ObjectiveDirector's quest catalog, so it " +
                               "cannot begin: add it to the director's Quests list.", quest);
                return false;
            }

            Add(quest, NextOrder, live: true);
            return true;
        }

        /// <summary>SERVER: a client asked for the quest with this id.</summary>
        public bool StartQuest(string id) => StartQuest(FindChain(id));

        /// <summary>SERVER: begins every catalog quest whose trigger has fired. Throttled: triggers ask about the whole crew.</summary>
        private void CheckTriggers()
        {
            if (Time.time < nextTriggerCheck) return;
            nextTriggerCheck = Time.time + triggerCheckSeconds;

            foreach (ObjectiveChain quest in quests)
                if (quest != null && quest.Trigger != null && Find(quest) == null && quest.Trigger.ShouldStart(this))
                    StartQuest(quest);
        }

        private int NextOrder
        {
            get
            {
                int order = 0;
                foreach (Quest quest in started) order = Mathf.Max(order, quest.Order + 1);
                return order;
            }
        }

        private Quest Add(ObjectiveChain quest, int order, bool live)
        {
            var begun = new Quest(quest, order);
            started.Add(begun);

            if (listed) QuestLog.Open(begun, acquired: live);
            Started?.Invoke(begun, live);
            return begun;
        }

        private void Drop(Quest quest)
        {
            started.Remove(quest);
            QuestLog.Close(quest);
        }

        // ── Wire ────────────────────────────────────────────────────────────────

        /// <summary>SERVER: the player on <paramref name="clientId"/> has done their part of step <paramref name="step"/> of quest <paramref name="questId"/>.</summary>
        public void RecordFinished(ulong clientId, string questId, int step) =>
            Find(questId)?.RecordFinished(clientId, step);

        /// <summary>
        /// Everywhere but the server: every quest as the server has it. Quests this machine does not have yet are
        /// begun (live when the crew took them on just now), and catalog quests the server no longer has are dropped.
        /// </summary>
        public void Adopt(IReadOnlyList<QuestState> replicated, bool live)
        {
            for (int i = started.Count - 1; i >= 0; i--)
                if (started[i] != Story && !Contains(replicated, started[i].Id))
                    Drop(started[i]);

            foreach (QuestState state in replicated)
            {
                string id = state.Quest.Value;
                Quest quest = Find(id);
                if (quest == null)
                {
                    ObjectiveChain known = FindChain(id);
                    if (known == null)
                    {
                        Debug.LogError($"[Objectives] The server runs quest '{id}', which this build's catalog does " +
                                       "not have. The two builds differ.", this);
                        continue;
                    }

                    quest = Add(known, state.Order, live);
                }

                if (quest.Adopt(state.Progress, live)) Changed?.Invoke(quest, live);
            }
        }

        private static bool Contains(IReadOnlyList<QuestState> replicated, string id)
        {
            foreach (QuestState state in replicated)
                if (state.Quest.Value == id) return true;

            return false;
        }

        // ── Save ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Restore-only. Called by <c>ObjectiveSaveable</c>; do not call from gameplay. Puts every quest back
        /// where the save says, without replaying anything that happened to get there. Quests begun in this
        /// session that the save does not have are dropped; a quest the catalog no longer has is skipped, loudly.
        /// </summary>
        public void Restore(IReadOnlyList<SavedQuest> saved)
        {
            for (int i = started.Count - 1; i >= 0; i--)
                if (started[i] != Story) Drop(started[i]);

            var ordered = new List<SavedQuest>(saved);
            ordered.Sort((a, b) => a.Order.CompareTo(b.Order));

            var restoredIds = new HashSet<string>();
            foreach (SavedQuest record in ordered)
            {
                ObjectiveChain quest = FindChain(record.Id);
                if (quest == null)
                {
                    Debug.LogWarning($"[Objectives] The save names quest '{record.Id}', which the catalog no longer " +
                                     "has. Dropping it.", this);
                    continue;
                }

                if (!restoredIds.Add(quest.Id)) continue;

                Quest restored = quest == chain ? Story : Add(quest, record.Order, live: false);
                restored.Restore(Resolve(quest, record.Step, record.Complete, record.Begun, record.Remarks));
                Changed?.Invoke(restored, false);
            }

            Quest story = Story;
            if (story != null && !restoredIds.Contains(story.Id))
            {
                story.Restore(default);
                Changed?.Invoke(story, false);
            }
        }

        /// <summary>
        /// A saved record as progress through <paramref name="chain"/>. By id, never by position, so
        /// steps can be inserted and reordered under existing saves. An id the chain no longer has
        /// restarts the chain — noisily, because it means a step was renamed or deleted.
        /// </summary>
        public static ObjectiveProgress Resolve(ObjectiveChain chain, string stepId, bool complete, bool begun,
                                                int remarks = 0)
        {
            if (complete) return new ObjectiveProgress { Step = chain.Steps.Count };

            int step = chain.IndexOf(stepId);
            if (step < 0)
            {
                Debug.LogWarning($"[Objectives] The save names step '{stepId}', which '{chain.name}' " +
                                 "no longer has. Starting the chain again.");
                return default;
            }

            return new ObjectiveProgress { Step = step, Begun = begun, Remarks = remarks };
        }

        // ── Authoring ───────────────────────────────────────────────────────────

        /// <summary>Every quest needs an id that fits the wire and no other quest shares; said once, at load.</summary>
        private void ValidateCatalog()
        {
            var seen = new HashSet<string>();
            if (chain != null) Validate(chain, seen);

            foreach (ObjectiveChain quest in quests)
            {
                if (quest == null) Debug.LogError("[Objectives] The quest catalog has an empty slot.", this);
                else if (quest == chain) Debug.LogError($"[Objectives] '{quest.name}' is the story quest; it must not be in the catalog too.", this);
                else Validate(quest, seen);
            }
        }

        private void Validate(ObjectiveChain quest, HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(quest.Id))
                Debug.LogError($"[Objectives] Quest '{quest.name}' has no id, so it cannot be saved or replicated.", quest);
            else if (Encoding.UTF8.GetByteCount(quest.Id) > ObjectiveChain.MaxIdBytes)
                Debug.LogError($"[Objectives] Quest id '{quest.Id}' is longer than {ObjectiveChain.MaxIdBytes} bytes, " +
                               "so it does not fit on the wire.", quest);
            else if (!seen.Add(quest.Id))
                Debug.LogError($"[Objectives] Two quests share the id '{quest.Id}'.", quest);
        }
    }
}
