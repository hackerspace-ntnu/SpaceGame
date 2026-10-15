using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Gives the crew another quest from partway through this one. The new quest is a catalog quest like any other:
    /// it keeps its own progress, is saved by its own id, and becomes every player's active quest when it begins.
    ///
    /// <para>
    /// <see cref="Completion.HandOff"/>: this step is met the moment the new quest begins, and this quest moves straight
    /// on — the two then run side by side. Its own briefing is never heard (the step is over in the frame it begins),
    /// so the words belong on the new quest's first step.
    /// </para>
    /// <para>
    /// <see cref="Completion.WaitForQuest"/>: this step is met only once the new quest is finished — a sub-quest. While
    /// it waits, it points where the sub-quest's current step points, so following this quest still leads somewhere.
    /// </para>
    /// <para>
    /// Never blocks this quest on something that cannot happen: a quest the director refuses (not in the catalog — it
    /// says so) or one a save no longer has counts as done. A quest the crew already finished counts as done too.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Start Quest Step")]
    public class StartQuestStep : ObjectiveStep
    {
        public enum Completion
        {
            /// <summary>Met as soon as the new quest begins; this quest carries on beside it.</summary>
            HandOff,

            /// <summary>Met once the new quest is finished; this quest waits for it.</summary>
            WaitForQuest,
        }

        [Tooltip("The quest to give. Must be in the ObjectiveDirector's quest catalog, and never the quest this step " +
                 "is in.")]
        [SerializeField] private ObjectiveChain quest;

        [Tooltip("HandOff: move on as soon as the new quest begins. WaitForQuest: wait until it is finished.")]
        [SerializeField] private Completion completion = Completion.HandOff;

        [Tooltip("Under the title while waiting for the new quest; {0} is its name.")]
        [SerializeField] private string waitingStatus = "Finish <b>{0}</b>";

        /// <summary>The quest this step gives.</summary>
        public ObjectiveChain Gives => quest;

        public override bool TryBegin(ObjectiveWorld world)
        {
            if (quest == null)
            {
                Debug.LogError($"[Objectives] '{name}' names no quest to start. The step is skipped.", this);
                return true;
            }

            // Already begun (a crewmate's trigger got there first) is as good as begun now.
            world.StartQuest(quest);
            return true;
        }

        public override bool IsMet(ObjectiveWorld world)
        {
            if (completion == Completion.HandOff) return true;

            Quest given = world.FindQuest(quest);
            return given == null || given.IsComplete;
        }

        public override string Status(ObjectiveWorld world) =>
            TryGetWaiting(world, out _) ? string.Format(waitingStatus, quest.Title) : string.Empty;

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            position = default;
            return TryGetWaiting(world, out Quest given) && given.Current.TryGetWaypoint(given.World, out position);
        }

        public override bool TryGetBeacon(ObjectiveWorld world, out Vector3 position)
        {
            position = default;
            return TryGetWaiting(world, out Quest given) && given.Current.TryGetBeacon(given.World, out position);
        }

        /// <summary>EVERY machine: waiting on a sub-quest that is still on a step.</summary>
        private bool TryGetWaiting(ObjectiveWorld world, out Quest given)
        {
            given = completion == Completion.WaitForQuest ? world.FindQuest(quest) : null;
            return given != null && given.Current != null;
        }
    }
}
