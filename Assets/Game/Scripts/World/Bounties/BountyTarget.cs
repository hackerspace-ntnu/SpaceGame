// The wanted man (or woman, or thing) while the crew hunt them: added to the spawned target by the hunt's run
// (BountyHuntRun), on the server only, and added again after a load (the run finds the target by its saved identity).
// It never stands in a prefab, so any character a poster prints can be a target. Until it is caught the run keeps it
// to its spot like the rest of the cast; from then on its goal is this component's.
//
//   - It keeps the body: dead, the target is evidence, so the world's usual corpse clean-up (Remains) is held off for
//     as long as the bounty is open. The drag that brings a body home is the rope's, not this.
//   - Beaten — knocked down, netted, held or hurt below `surrenderBelow` of its health — it gives up: it stops fighting
//     for good (a neutral faction, which its own saver keeps) and follows the nearest player within `followRadius`.
//     It waits where it is when nobody is near.
//
// Persistence: none of its own. Dead is health 0, surrendered is the captive faction, both already saved on the body.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.World.Bounties
{
    [DisallowMultipleComponent]
    public sealed class BountyTarget : MonoBehaviour
    {
        private HealthComponent health;
        private AgentRagdoll ragdoll;
        private AgentTargeting targeting;
        private ProvocationModule provocation;
        private EntityFaction faction;
        private AgentGoal goal;
        private BountyBoard.CaptiveTuning tuning;
        private readonly List<Transform> players = new();
        private float nextThink;
        private bool cannotSurrender;

        public bool IsDead => health != null && !health.Alive;

        public bool IsSurrendered => !IsDead && faction != null && tuning.faction != null && faction.Faction == tuning.faction;

        /// <summary>Dead or given up: ready to be brought in.</summary>
        public bool IsCaught => IsDead || IsSurrendered;

        /// <summary>Server: makes <paramref name="body"/> the target, with the board's tuning.</summary>
        public static BountyTarget Attach(GameObject body, BountyBoard.CaptiveTuning tuning)
        {
            BountyTarget target = body.TryGetComponent(out BountyTarget existing) ? existing : body.AddComponent<BountyTarget>();
            target.tuning = tuning;
            return target;
        }

        /// <summary>
        /// Server: handed in, so no longer wanted. The body goes the usual way once nobody watches: led to
        /// <paramref name="ledTo"/> if it lives, then cleared away.
        /// </summary>
        public void Release(Vector3 ledTo, float takenAwaySeconds)
        {
            if (!IsDead && goal != null)
                goal.TrySetSampled(ledTo, tuning.followDistance, "bounty handed in", hold: false, facePoint: null);
            Remains.On(gameObject).Begin(takenAwaySeconds);

            // Disabled first: a component being destroyed still gets this frame's Update, which would stop the
            // countdown just begun.
            enabled = false;
            Destroy(this);
        }

        private void Awake()
        {
            health = GetComponent<HealthComponent>();
            ragdoll = GetComponent<AgentRagdoll>();
            targeting = GetComponent<AgentTargeting>();
            provocation = GetComponent<ProvocationModule>();
            faction = GetComponent<EntityFaction>();
            goal = AgentGoal.GetOrAdd(gameObject);
        }

        private void Update()
        {
            if (!Network.Decides) return;

            // The body is the evidence: whatever started its clean-up countdown, it waits while the bounty is open.
            if (TryGetComponent(out Remains remains) && remains.Counting) remains.Stop();

            if (IsDead || Time.time < nextThink) return;
            nextThink = Time.time + tuning.thinkSeconds;

            if (!IsSurrendered && !cannotSurrender && Beaten()) Surrender();
            if (IsSurrendered) Follow();
        }

        private bool Beaten() =>
            (ragdoll != null && ragdoll.IsHeldOrDown) ||
            (health != null && health.GetHealth <= health.GetMaxHealth * tuning.surrenderBelow);

        private void Surrender()
        {
            if (faction == null || tuning.faction == null)
            {
                Debug.LogError($"[Bounties] {name} cannot surrender: it has no EntityFaction, or the board has no captive faction.", this);
                cannotSurrender = true;
                return;
            }

            faction.SetFaction(tuning.faction);
        }

        private void Follow()
        {
            // A grudge from the fight would otherwise pull it straight back into it.
            if (provocation != null && provocation.IsProvoked) provocation.Forget();
            if (targeting != null && targeting.HasTarget) targeting.ClearTarget();
            if (goal == null || (ragdoll != null && ragdoll.IsHeldOrDown)) return;

            SessionPlayers.Collect(players);
            Transform captor = BountyRules.NearestWithin(players, transform.position, tuning.followRadius);
            if (captor != null) goal.TrySetSampled(captor.position, tuning.followDistance, "bounty captive", hold: false, facePoint: null);
            else if (goal.HasGoal) goal.Clear();
        }
    }
}
