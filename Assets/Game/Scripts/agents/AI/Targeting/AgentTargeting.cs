// The single place an agent decides who it is fighting.
//
// Before this existed, ChaseModule, CloseCombatModule, AgentRangedCombatModule, HuntModule and
// KeepDistanceModule each ran their own EntityTargetRegistry query on their own schedule with
// their own staleness rules. Three consequences, all visible in play:
//   * One agent could chase A, shoot B and back away from C in the same frame.
//   * Most of them held their first-resolved target until it died, so an agent would walk past
//     an enemy standing next to it to reach whoever happened to be nearest at spawn.
//   * The registry was scanned once per module per agent per frame.
//
// This component resolves once per interval, scores candidates instead of taking the raw
// nearest, and publishes the answer through AgentContext.Targeting. Behaviour modules read it.
//
// Runs ahead of AgentController (execution order 0) so the decision is already current when
// modules tick. Optional: agents without it fall back to their own per-module resolution.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.World.Weather;

namespace SpaceGame.Agents
{
    [DefaultExecutionOrder(-50)]
    public class AgentTargeting : MonoBehaviour
    {
        [Tooltip("Tuning asset. When assigned it overrides every inline value below — that is how " +
                 "the same prefab hunts cautiously in the open world and aggressively in the arena.")]
        [SerializeField] private TargetingProfile profile;

        [Header("Inline tuning (ignored when a profile is assigned)")]
        [SerializeField] private FactionRelationship relationship = FactionRelationship.Hostile;
        [Tooltip("Candidates beyond this are never scored. Automatically widened at Awake to cover " +
                 "the agent's own longest weapon range.")]
        [SerializeField] private float acquisitionRange = 35f;
        [Tooltip("An acquired target is dropped past this distance. Kept above acquisitionRange so " +
                 "targets don't flicker at the boundary.")]
        [SerializeField] private float loseRange = 45f;
        [Tooltip("Require field-of-view + line-of-sight to acquire. Turn off for arena modes where " +
                 "everyone is expected to know where everyone else is.")]
        [SerializeField] private bool requireLineOfSightToAcquire = true;
        [Tooltip("Acquire anything inside this radius regardless of facing, so an agent reacts to " +
                 "something that walked up behind it.")]
        [SerializeField] private float proximityAcquireRange = 4f;

        [Tooltip("Draw the current target link in the Scene view while this object is selected.")]
        [SerializeField] private bool drawGizmos = true;

        // ── Published state ───────────────────────────────────────────────────────
        public Transform Target { get; private set; }
        public bool HasTarget => Target != null;

        // Distance from this agent to the target, refreshed every frame. Modules should read this
        // rather than recomputing it — five modules each calling Vector3.Distance on the same pair
        // was a measurable share of the per-frame cost with a full arena.
        public float DistanceToTarget { get; private set; }

        public bool CanSeeTarget { get; private set; }
        public Vector3 LastKnownPosition { get; private set; }
        public bool HasLastKnownPosition { get; private set; }
        public float TimeSinceSeen { get; private set; }

        // Whoever most recently damaged this agent. Scored as a strong bias so an agent turns on
        // its attacker instead of continuing toward a marginally closer target.
        public Transform LastAttacker { get; private set; }

        public FactionRelationship Relationship => settings.relationship;

        // ── Internals ─────────────────────────────────────────────────────────────
        private TargetingProfile settings;
        private TargetingProfile runtimeDefaults;
        private EntityFaction selfFaction;
        private PerceptionModule perception;
        private HealthComponent health;
        private float reevaluateTimer;

        // Acquisition ranges actually used, after widening to cover the agent's own weapons.
        // Held here rather than written back to the profile: a profile is a shared asset and
        // several prefabs point at the same one.
        private float effectiveAcquisitionRange;
        private float effectiveLoseRange;

        // How far this agent can see through the weather, 0 to 1. Refreshed on an interval rather
        // than per frame: a sandstorm's density where one agent is standing changes over seconds,
        // and sampling it every frame for every agent is exactly the per-agent-per-frame cost this
        // component was written to remove.
        private float stormSightFactor = 1f;
        private float stormSampleTimer;
        private const float StormSampleInterval = 1f;

        // Both are floored at proximityAcquireRange. Without that floor a thick enough storm would
        // shrink the working range inside the point-blank bypass, and an agent would fail to react
        // to something it is standing next to — which reads as broken AI, not as bad weather.
        /// <summary>Acquisition range after weather. What the agent can actually spot at.</summary>
        public float SightRange =>
            Mathf.Max(effectiveAcquisitionRange * stormSightFactor, settings.proximityAcquireRange);

        /// <summary>Retention range after weather, so a target lost in the sand is dropped too.</summary>
        public float LoseRange =>
            Mathf.Max(effectiveLoseRange * stormSightFactor, settings.proximityAcquireRange);

        // Shared across agents rather than one list each: Reevaluate fills and fully consumes it
        // inside a single synchronous call, so no other agent can observe it mid-use.
        private static readonly List<EntityFaction> candidateBuffer = new List<EntityFaction>(64);

        private void Awake()
        {
            selfFaction = GetComponent<EntityFaction>();
            perception = GetComponent<PerceptionModule>();
            health = GetComponent<HealthComponent>();

            // Without an asset, synthesise one from the inline fields. Everything downstream then
            // reads a single settings object and never has to know which source it came from.
            if (profile == null)
            {
                runtimeDefaults = ScriptableObject.CreateInstance<TargetingProfile>();
                runtimeDefaults.hideFlags = HideFlags.HideAndDontSave;
                runtimeDefaults.relationship = relationship;
                runtimeDefaults.acquisitionRange = acquisitionRange;
                runtimeDefaults.loseRange = Mathf.Max(loseRange, acquisitionRange);
                runtimeDefaults.requireLineOfSightToAcquire = requireLineOfSightToAcquire;
                runtimeDefaults.proximityAcquireRange = proximityAcquireRange;
                settings = runtimeDefaults;
            }
            else
            {
                settings = profile;
            }

            if (selfFaction == null)
            {
                Debug.LogWarning($"{name}: AgentTargeting needs an EntityFaction to know who is hostile. " +
                                 "Without one this agent can never acquire a target.", this);
            }

            RecomputeEffectiveRanges();
        }

        // Attach or fetch the component. Used by AgentController and by the alert/noise modules so
        // none of them depend on Awake ordering, and so a prefab that predates this component still
        // gets one shared target decision instead of falling back to per-module resolution.
        public static AgentTargeting GetOrAdd(GameObject host)
        {
            AgentTargeting existing = host.GetComponent<AgentTargeting>();
            return existing != null ? existing : host.AddComponent<AgentTargeting>();
        }

        // An agent that can shoot 22 m but only acquires at 15 m never starts the fight it is
        // equipped for. Widen the working ranges to cover the longest weapon on the agent rather
        // than making every profile author remember to.
        private void RecomputeEffectiveRanges()
        {
            float weaponRange = 0f;
            foreach (AgentRangedCombatModule ranged in GetComponents<AgentRangedCombatModule>())
                weaponRange = Mathf.Max(weaponRange, ranged.MaxRange);
            foreach (CloseCombatModule melee in GetComponents<CloseCombatModule>())
                weaponRange = Mathf.Max(weaponRange, melee.AttackRange);

            // Artifacts the agent is actually carrying count too. An NPC holding a looted rifle
            // that reaches 40 m but acquiring at 35 would stand and watch a fight it is equipped to
            // join — the same failure this method already exists to prevent for profile weapons.
            foreach (NpcItemUseModule item in GetComponents<NpcItemUseModule>())
                weaponRange = Mathf.Max(weaponRange, item.MaxRange);

            effectiveAcquisitionRange = Mathf.Max(settings.acquisitionRange, weaponRange + WeaponRangeMargin);
            effectiveLoseRange = Mathf.Max(settings.loseRange, effectiveAcquisitionRange + WeaponRangeMargin);
        }

        // Headroom past the longest weapon range, so a target that steps just outside the fire band
        // stays acquired instead of being dropped and immediately re-acquired.
        private const float WeaponRangeMargin = 5f;

        private void OnEnable()
        {
            ClearTarget();

            // Memory too, not just the target: a respawned entity that kept its last known position
            // would walk straight back to where it died to investigate its own corpse.
            HasLastKnownPosition = false;
            TimeSinceSeen = 0f;
            LastAttacker = null;
            reevaluateTimer = 0f;
            if (health != null)
                health.OnDamage += HandleDamaged;
        }

        private void OnDisable()
        {
            if (health != null)
                health.OnDamage -= HandleDamaged;
        }

        private void OnDestroy()
        {
            if (runtimeDefaults != null)
                Destroy(runtimeDefaults);
        }

        // Swap tuning at runtime — MatchManager uses this to give arena bots a more aggressive
        // profile than the same prefab runs with in the open world.
        public void ApplyProfile(TargetingProfile newProfile)
        {
            if (newProfile == null)
                return;

            profile = newProfile;
            settings = newProfile;
            RecomputeEffectiveRanges();
            reevaluateTimer = 0f;
        }

        // Forced acquisition from outside the scoring loop — ally alerts and heard noises.
        // Bypasses line-of-sight on purpose: being told where someone is is the whole point.
        public void ForceTarget(Transform target)
        {
            if (!TargetResolution.IsViable(target))
                return;

            Target = target;
            DistanceToTarget = Vector3.Distance(transform.position, target.position);
            LastKnownPosition = target.position;
            HasLastKnownPosition = true;
            TimeSinceSeen = 0f;

            // Hold the forced target for a full interval before re-scoring, so an alert isn't
            // immediately overruled by whoever happens to be marginally nearer.
            reevaluateTimer = settings.reevaluateInterval;
        }

        /// <summary>
        /// True when this agent is currently fighting <paramref name="other"/>.
        ///
        /// Matched by hierarchy rather than by reference, because the two sides name the same
        /// entity differently. Targeting holds whatever the registry scored — the entity root that
        /// carries the EntityFaction — while a caller usually holds a component somewhere inside
        /// the body: an Interactor on a camera rig, a collider on a limb. Comparing those two
        /// transforms directly answers "no" for a player who is being chased and hit.
        /// </summary>
        public bool IsFightingWith(Transform other)
        {
            if (other == null || Target == null) return false;

            return Target.root == other.root;
        }

        public void ClearTarget()
        {
            Target = null;
            CanSeeTarget = false;
            DistanceToTarget = float.MaxValue;
        }

        // Puts back what this agent remembered, for a save being loaded.
        //
        // Separate from ForceTarget because the two say different things. ForceTarget is an event —
        // "someone just told you where they are" — and it deliberately resets the re-scoring timer and
        // treats the target as seen this instant. A restore is not an event: the agent should come back
        // holding exactly the memory it had, including a target it had LOST sight of seconds ago, so
        // that a search resumes from where it left off instead of restarting.
        //
        // The target is set directly rather than through ForceTarget for that reason, and viability is
        // still checked — a saved target that has since died must not be re-acquired.
        public void RestoreMemory(Transform target, Vector3 lastKnownPosition, bool hasLastKnownPosition,
                                  float timeSinceSeen, Transform lastAttacker)
        {
            if (TargetResolution.IsViable(target))
            {
                Target = target;
                DistanceToTarget = Vector3.Distance(transform.position, target.position);
            }

            LastKnownPosition = lastKnownPosition;
            HasLastKnownPosition = hasLastKnownPosition;
            TimeSinceSeen = timeSinceSeen;
            LastAttacker = lastAttacker;
        }

        private void HandleDamaged(int _)
        {
            if (health == null)
                return;

            Transform source = health.LastDamageSource;
            if (source == null)
                return;

            // Attribute the hit to the entity, not to whichever child collider or projectile
            // carried the reference, so the bias actually matches a scoring candidate.
            EntityFaction attacker = source.GetComponentInParent<EntityFaction>();
            LastAttacker = attacker != null ? attacker.transform : source;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            if (Target != null && !TargetResolution.IsViable(Target))
            {
                ClearTarget();

                // A target that died is not worth investigating. Dropping the memory here is what
                // stops SearchModule from walking over to sniff the corpse of something the agent
                // just killed — losing sight of a live target still leaves the memory intact.
                HasLastKnownPosition = false;
            }

            stormSampleTimer -= deltaTime;
            if (stormSampleTimer <= 0f)
            {
                stormSampleTimer = StormSampleInterval;
                stormSightFactor = Sandstorms.SightFactorAt(transform.position);
            }

            // Strictly on the interval, including while the agent has no target at all. Re-scanning
            // every frame when nothing is acquired is the case that scales worst: it is exactly the
            // situation where every agent in the scene is querying the whole registry at once.
            reevaluateTimer -= deltaTime;
            if (reevaluateTimer <= 0f)
            {
                Reevaluate();
                reevaluateTimer = settings.reevaluateInterval;
            }

            RefreshTargetState(deltaTime);
        }

        // Distance, visibility and memory for the target we currently hold. Runs every frame —
        // the interval above only governs how often candidates are re-scored, not how current
        // the held target's state is.
        private void RefreshTargetState(float deltaTime)
        {
            if (!HasTarget)
            {
                if (HasLastKnownPosition)
                {
                    TimeSinceSeen += deltaTime;
                    if (TimeSinceSeen > settings.memoryDuration)
                        HasLastKnownPosition = false;
                }
                return;
            }

            DistanceToTarget = Vector3.Distance(transform.position, Target.position);

            if (DistanceToTarget > LoseRange)
            {
                ClearTarget();
                return;
            }

            CanSeeTarget = perception == null
                           || DistanceToTarget <= settings.proximityAcquireRange
                           || perception.CanSee(Target);

            if (CanSeeTarget)
            {
                LastKnownPosition = Target.position;
                HasLastKnownPosition = true;
                TimeSinceSeen = 0f;
                return;
            }

            TimeSinceSeen += deltaTime;

            // Only give up on an unseen target when sight is what acquired it in the first place.
            // Arena modes turn requireLineOfSightToAcquire off so bots keep converging through walls.
            if (settings.requireLineOfSightToAcquire && TimeSinceSeen > settings.memoryDuration)
                ClearTarget();
        }

        // Scores every candidate in range and keeps the best. Score is an "effective distance":
        // biases make a candidate count as nearer than it is, penalties as further, so everything
        // stays in metres and the numbers on the profile mean something readable.
        private void Reevaluate()
        {
            if (selfFaction == null)
                return;

            // Sight range, not the raw acquisition range: in a sandstorm an agent scores only what
            // it could actually see. proximityAcquireRange below is deliberately left alone — a
            // robot still notices something at arm's length, so the storm hides you without
            // turning you into a ghost.
            EntityTargetRegistry.Query(selfFaction, settings.relationship, transform.position,
                                       SightRange, candidateBuffer);

            Transform best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < candidateBuffer.Count; i++)
            {
                Transform candidate = candidateBuffer[i].transform;
                if (!TargetResolution.IsViable(candidate))
                    continue;

                float distance = Vector3.Distance(transform.position, candidate.position);
                bool visible = IsCandidateVisible(candidate, distance);

                // Sight gates acquisition, not retention: an agent that cannot see anything new
                // keeps whatever it already had rather than dropping to no target at all.
                if (settings.requireLineOfSightToAcquire && !visible && candidate != Target)
                    continue;

                float score = distance;
                if (candidate == Target)
                    score *= 1f - settings.currentTargetBias;
                if (candidate == LastAttacker)
                    score *= 1f - settings.lastAttackerBias;
                if (!visible)
                    score *= 1f + settings.occludedPenalty;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best != null)
                Target = best;
        }

        private bool IsCandidateVisible(Transform candidate, float distance)
        {
            if (perception == null)
                return true;
            if (distance <= settings.proximityAcquireRange)
                return true;

            // IsVisible, not CanSee: scoring a crowd with the memory-writing variant would
            // overwrite LastKnownPosition with whichever candidate happened to be checked last.
            return perception.IsVisible(candidate);
        }

        private void OnValidate()
        {
            acquisitionRange = Mathf.Max(1f, acquisitionRange);
            loseRange = Mathf.Max(acquisitionRange, loseRange);
            proximityAcquireRange = Mathf.Clamp(proximityAcquireRange, 0f, acquisitionRange);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !Application.isPlaying)
                return;

            if (HasTarget)
            {
                Gizmos.color = CanSeeTarget ? Color.red : new Color(1f, 0.5f, 0f);
                Gizmos.DrawLine(transform.position + Vector3.up, Target.position + Vector3.up);
            }
            else if (HasLastKnownPosition)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(LastKnownPosition, 0.6f);
            }
        }
    }
}
