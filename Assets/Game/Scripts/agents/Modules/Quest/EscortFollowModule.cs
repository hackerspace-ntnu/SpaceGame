// Walks after whichever player is nearest, and waits beside them -- the Oathkeepers being led to
// the witch's hut.
//
// Switched on and off by the quest, not by itself: a QuestStateGate on the character enables this
// only while the escort step runs, so a reloaded world and a late joiner see the keepers following
// with nothing saved here. Off, it does nothing at all.
//
// Social priority: under fleeing and anything scripted (a stunned keeper stands stunned), above
// wandering and errands, which is exactly "drop what you were doing and come along".
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    public class EscortFollowModule : BehaviourModuleBase
    {
        [Header("Following")]
        [Tooltip("Stops this far from the player it follows, metres. Close enough to read as 'with you', " +
                 "far enough not to be underfoot.")]
        [SerializeField] private float followDistance = 3f;

        [Tooltip("Further than this behind, it runs to catch up, metres.")]
        [SerializeField] private float runBeyond = 10f;

        [Tooltip("A player further than this is not followed at all, metres. The keepers wait where " +
                 "they are rather than setting out across the map after somebody they cannot see.")]
        [SerializeField] private float giveUpBeyond = 80f;

        [Tooltip("Seconds between choosing which player to follow. Not per frame -- every follower pays it.")]
        [SerializeField] private float rescanInterval = 0.5f;

        private Transform leader;
        private float rescanTimer;

        private void Reset() => SetPriorityDefault(ModulePriority.Social);

        private void OnEnable()
        {
            leader = null;
            rescanTimer = 0f;
        }

        public override string ModuleDescription =>
            "Follows the nearest player and waits beside them. Meant to be switched on by a quest " +
            "(QuestStateGate) for an escort.\n\n" +
            "• followDistance — how close it stops\n" +
            "• runBeyond — when it runs to catch up\n" +
            "• giveUpBeyond — players further than this are not followed";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            rescanTimer -= deltaTime;
            if (rescanTimer <= 0f || leader == null)
            {
                rescanTimer = rescanInterval;
                PlayerIdentity nearest = PlayerIdentity.Nearest(context.Position, giveUpBeyond, out _);
                leader = nearest != null ? nearest.transform : null;
            }

            if (leader == null)
                return null;

            Vector3 toLeader = leader.position - context.Position;
            toLeader.y = 0f;
            float distance = toLeader.magnitude;

            if (distance <= followDistance)
                return MoveIntent.StopAndFace(leader.position);

            return MoveIntent.MoveTo(leader.position, followDistance, 1f, isRunning: distance > runBeyond);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            followDistance = Mathf.Max(0.5f, followDistance);
            runBeyond = Mathf.Max(followDistance, runBeyond);
            giveUpBeyond = Mathf.Max(runBeyond, giveUpBeyond);
            rescanInterval = Mathf.Max(0.05f, rescanInterval);
        }
    }
}
