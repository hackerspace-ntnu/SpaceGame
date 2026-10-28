// A creature that can be beaten but not killed: health stops at subdueFraction of its maximum, and
// from there it stands where it is and fights no more -- Merob, brought down so the Oathkeepers can
// chant the curse out of him.
//
// The floor is a damage filter, so it holds on the machine that decides damage and nowhere else
// needs to know. Being subdued is READ from health, never remembered: a save taken while he is down
// restores the low health, and he comes back down. Healing him above the floor (Revive) lets him
// stand again.
//
// Scripted priority while subdued: standing still is the behaviour, and the casting and chasing
// below it are starved. The QuestActor beside it is told once per fall, so a quest step can wait for
// "subdued" the way another waits for "dead".
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(HealthComponent))]
    public class SubduableModule : BehaviourModuleBase, IDamageFilter, ISubduable
    {
        [Tooltip("Share of maximum health it cannot be pushed below. Reaching it subdues the creature.")]
        [SerializeField, Range(0.01f, 0.9f)] private float subdueFraction = 0.15f;

        private HealthComponent health;
        private QuestActor actor;
        private bool reported;

        /// <summary>The health it cannot be pushed below.</summary>
        public int Floor => Mathf.Max(1, Mathf.CeilToInt(health.GetMaxHealth * subdueFraction));

        /// <summary>Beaten down to the floor. Read from health, so it survives a reload.</summary>
        public bool IsSubdued => health != null && health.GetHealth <= Floor;

        private void Reset() => SetPriorityDefault(ModulePriority.Scripted);

        private void OnEnable()
        {
            health = GetComponent<HealthComponent>();
            actor = GetComponent<QuestActor>();
            health.AddFilter(this);
            reported = false;
        }

        private void OnDisable()
        {
            if (health != null) health.RemoveFilter(this);
        }

        public override string ModuleDescription =>
            "Cannot be killed: health stops at subdueFraction of its maximum, and there it stands " +
            "still and stops fighting until healed. Tells its QuestActor it was subdued.\n\n" +
            "• subdueFraction — the share of health it cannot drop below";

        public void Filter(HealthComponent victim, ref DamageHit hit)
        {
            int room = victim.GetHealth - Floor;
            if (hit.Amount > room) hit.Amount = Mathf.Max(0, room);
        }

        /// <summary>SERVER: stand it up again at full health — the curse is lifted.</summary>
        public void Revive()
        {
            health.ResetToFull();
            reported = false;
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (!IsSubdued)
            {
                reported = false;
                return null;
            }

            if (!reported)
            {
                reported = true;
                if (actor != null) actor.ReportSubdued();
            }

            return MoveIntent.Idle();
        }

        protected override void OnValidate() => SetMinPriority(ModulePriority.Scripted);
    }
}
