// Walks a keeper to its stone in a running ChantCircle and keeps it there, facing the centre.
//
// The circle decides WHEN (from the quest) and WHERE (its stones); this only answers "go there and
// stay". Override priority: a keeper whose circle is running drops everything for it -- following,
// errands, wandering -- and only a stun (Scripted) moves it off. Outside a running circle it passes.
using UnityEngine;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Gameplay.SpriteHamlet;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(QuestActor))]
    public class ChantStoneModule : BehaviourModuleBase
    {
        [Tooltip("How close to its stone the keeper stops walking, metres. Inside the circle's own tolerance.")]
        [SerializeField] private float arriveDistance = 0.5f;

        [Tooltip("Further than this from its stone, it runs, metres.")]
        [SerializeField] private float runBeyond = 6f;

        private QuestActor actor;

        private void Reset() => SetPriorityDefault(ModulePriority.Override);

        private void Awake() => actor = GetComponent<QuestActor>();

        public override string ModuleDescription =>
            "Takes the keeper to its stone in a running ChantCircle and holds it there facing the " +
            "centre. Passes when no circle naming this keeper is running.\n\n" +
            "• arriveDistance — how close it stops\n" +
            "• runBeyond — when it runs";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (!ChantCircle.TryFindRunningFor(actor.ActorId, out ChantCircle circle)) return null;
            if (!circle.TryGetStone(actor.ActorId, out Vector3 stone)) return null;

            Vector3 toStone = stone - context.Position;
            toStone.y = 0f;
            float distance = toStone.magnitude;

            if (distance <= arriveDistance)
                return MoveIntent.StopAndFace(circle.Centre);

            return MoveIntent.MoveTo(stone, arriveDistance, 1f, isRunning: distance > runBeyond);
        }

        protected override void OnValidate()
        {
            SetMinPriority(ModulePriority.Override);
            arriveDistance = Mathf.Max(0.1f, arriveDistance);
            runBeyond = Mathf.Max(arriveDistance, runBeyond);
        }
    }
}
