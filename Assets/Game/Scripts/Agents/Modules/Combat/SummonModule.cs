// Calls minions up out of the ground around itself while it has someone to fight -- the witch's
// sporelings, sent at the Oathkeepers while her ward stands.
//
// A side-effect module: it never claims movement, so whatever else the creature is doing (casting,
// blinking away) carries on while it summons. Ticks only where the AgentController ticks, which is
// the server, so World.Spawn is called from the one place allowed to.
//
// The summoned are ordinary creatures: they choose their own targets from their own TargetingProfile
// (which is how the sporelings come to prefer the keepers), replicate and save themselves. When the
// summoner dies they go with it. A reload forgets which were summoned -- the cap counts this
// session's summons only -- and any left over simply fight on.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    public class SummonModule : BehaviourModuleBase
    {
        [Header("What")]
        [Tooltip("A registered network prefab.")]
        [SerializeField] private GameObject minionPrefab;

        [Header("How many")]
        [Tooltip("Seconds between waves.")]
        [SerializeField] private float waveInterval = 10f;
        [SerializeField] private int perWave = 2;
        [Tooltip("No new wave while this many of its summons are still standing.")]
        [SerializeField] private int maxAlive = 4;

        [Header("Where")]
        [Tooltip("Minions rise this far from the summoner at most, metres.")]
        [SerializeField] private float radius = 6f;
        [Tooltip("And at least this far, so they do not appear inside it.")]
        [SerializeField] private float minRadius = 2f;
        [SerializeField] private float navMeshSampleDistance = 3f;
        [SerializeField] private int placementTries = 6;

        private readonly List<GameObject> summoned = new();
        private HealthComponent health;
        private float waveTimer;

        public override bool ClaimsMovement => false;

        private void Reset() => SetPriorityDefault(ModulePriority.RangedAttack);

        private void Awake() => health = GetComponent<HealthComponent>();

        private void OnEnable()
        {
            // The first wave comes a full interval after the summoning starts, not on the same frame.
            waveTimer = waveInterval;
            if (health != null) health.OnDeath += DismissAll;
        }

        private void OnDisable()
        {
            if (health != null) health.OnDeath -= DismissAll;
        }

        public override string ModuleDescription =>
            "Side effect: while it has a target, raises perWave minions around itself every " +
            "waveInterval, up to maxAlive. Its summons go when it dies.\n\n" +
            "• minionPrefab — a registered network prefab\n" +
            "• radius / minRadius — the ring they rise in";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (minionPrefab == null || context.Targeting == null || !context.Targeting.HasTarget)
                return null;

            waveTimer -= deltaTime;
            if (waveTimer > 0f)
                return null;
            waveTimer = waveInterval;

            summoned.RemoveAll(minion => minion == null || !IsStanding(minion));
            for (int i = 0; i < perWave && summoned.Count < maxAlive; i++)
                if (TryFindSpot(context.Position, out Vector3 spot))
                    Raise(spot, context.Position);

            return null;
        }

        private void Raise(Vector3 spot, Vector3 summoner)
        {
            Vector3 away = spot - summoner;
            away.y = 0f;
            Quaternion facing = away.sqrMagnitude > 0.01f ? Quaternion.LookRotation(away) : Quaternion.identity;

            GameObject minion = GameServices.World.Spawn(minionPrefab, spot, facing);
            if (minion != null) summoned.Add(minion);
        }

        private bool TryFindSpot(Vector3 around, out Vector3 spot)
        {
            for (int i = 0; i < placementTries; i++)
            {
                Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(minRadius, radius);
                Vector3 candidate = around + new Vector3(ring.x, 0f, ring.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleDistance, NavMesh.AllAreas))
                {
                    spot = hit.position;
                    return true;
                }
            }

            spot = default;
            return false;
        }

        private static bool IsStanding(GameObject minion) =>
            !minion.TryGetComponent(out HealthComponent minionHealth) || minionHealth.Alive;

        private void DismissAll()
        {
            if (!Network.Decides || (health != null && health.IsRestoring)) return;

            foreach (GameObject minion in summoned)
                if (minion != null) GameServices.World.Despawn(minion);
            summoned.Clear();
        }

        protected override void OnValidate()
        {
            waveInterval = Mathf.Max(0.5f, waveInterval);
            perWave = Mathf.Max(1, perWave);
            maxAlive = Mathf.Max(perWave, maxAlive);
            minRadius = Mathf.Max(0f, minRadius);
            radius = Mathf.Max(minRadius + 0.5f, radius);
            placementTries = Mathf.Max(1, placementTries);
        }
    }
}
