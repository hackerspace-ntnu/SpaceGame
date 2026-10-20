// A place one of a hunt's people keeps to, under its BountyHuntSite: a guard post, a stop on a patrol, a patch to
// wander. The cast entry's spotTag picks the spots; the server sends each person to theirs.
//
// The pose held there comes from `use`, and is shown on EVERY machine with nothing sent: each machine looks for a body
// standing idle on the spot and holds the use's loop on it, the way every machine already animates every NPC from what
// it sees (AnimatorAuthority). The loop's pick is seeded from the body and the spot, so machines agree on it. Nothing
// saved: a reload stands the person back on the spot and the loop starts again (Bounties.md, "Spots").
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Agents.Residents;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.World.Bounties
{
    /// <summary>How a person keeps to their spot.</summary>
    public enum SpotMode
    {
        /// <summary>Stands on it, facing its forward.</summary>
        Post,

        /// <summary>Walks every spot of the same tag in hierarchy order, round and round.</summary>
        Patrol,

        /// <summary>Wanders within <see cref="BountyHuntSpot.WanderRadius"/> of it.</summary>
        Wander,
    }

    [DisallowMultipleComponent]
    public sealed class BountyHuntSpot : MonoBehaviour
    {
        private const float GizmoFacingLength = 1f;

        // Every body a spot holds a loop on, so two spots near each other never pose the same one.
        private static readonly HashSet<AgentController> Held = new();

        [Tooltip("Cast entries with this spotTag stand at the spots tagged with it.")]
        [SerializeField] private string spotTag = string.Empty;

        [Tooltip("What the person does here: the loop their body holds while they stand idle on it. Empty = stand.")]
        [SerializeField] private SpotUse use;

        [SerializeField] private SpotMode mode = SpotMode.Post;

        [Tooltip("Wander only: how far from the spot they roam, in metres.")]
        [SerializeField, Min(0.5f)] private float wanderRadius = 8f;

        [Tooltip("A body standing idle this close to the spot, across the ground, holds its use's loop, in metres.")]
        [SerializeField, Min(0.1f)] private float holdRadius = 1.5f;

        [Tooltip("Seconds between looks for who stands on the spot, and whether they still do. The loop itself is " +
                 "re-asserted every frame.")]
        [SerializeField, Min(0.05f)] private float checkSeconds = 0.5f;

        private AgentController holder;
        private BodyLanguage holderBody;
        private AgentTargeting holderTargeting;
        private HealthComponent holderHealth;
        private CharacterCue heldCue;
        private float nextCheck;

        public string SpotTag => spotTag;
        public SpotUse Use => use;
        public SpotMode Mode => mode;
        public float WanderRadius => wanderRadius;

        private CharacterCue Cue => use != null ? use.holdCue : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Held.Clear();

        private void Update()
        {
            if (Cue == null) return;

            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + checkSeconds;
                // Reference checks: a holder destroyed since the last look must still be let go of, not merely forgotten.
                if (!ReferenceEquals(holder, null) && !IsIdleHere(holder, holderHealth, holderTargeting)) LetGo();
                if (ReferenceEquals(holder, null)) TakeNearestIdle();
            }

            // A held loop is kept by asking for it every frame (BodyLanguage.Hold). The spot's place among its siblings
            // fixes the pick, so the same loop plays on every machine and again after the body looks up.
            if (holderBody != null) holderBody.Hold(heldCue, transform.GetSiblingIndex());
        }

        private void OnDisable() => LetGo();

        /// <summary>The nearest NPC body standing idle on the spot that no other spot holds.</summary>
        private void TakeNearestIdle()
        {
            AgentController nearest = null;
            float best = holdRadius * holdRadius;
            foreach (EntityFaction entity in EntityTargetRegistry.All)
            {
                if (entity == null) continue;
                float distance = FlatDistanceSquared(entity.transform.position);
                if (distance > best) continue;

                AgentController agent = entity.GetComponentInParent<AgentController>();
                // A resident's body is ResidentPresence's to pose; a player has no AgentController at all.
                if (agent == null || Held.Contains(agent) || agent.TryGetComponent(out Resident _)) continue;
                if (BodyLanguage.Of(agent) == null) continue;
                if (!IsIdleHere(agent, agent.GetComponent<HealthComponent>(), agent.GetComponent<AgentTargeting>())) continue;

                best = distance;
                nearest = agent;
            }

            if (nearest == null) return;

            holder = nearest;
            holderBody = BodyLanguage.Of(nearest);
            holderTargeting = nearest.GetComponent<AgentTargeting>();
            holderHealth = nearest.GetComponent<HealthComponent>();
            heldCue = Cue;
            Held.Add(nearest);
        }

        /// <summary>
        /// Alive, in play, on the spot, standing still, and not in a fight. Who an agent fights is known only where it is
        /// simulated (the server); a watcher sees the fight as the body moving, which lets the loop go just the same.
        /// </summary>
        private bool IsIdleHere(AgentController agent, HealthComponent health, AgentTargeting targeting) =>
            agent != null && agent.isActiveAndEnabled && !agent.IsParked &&
            (health == null || health.Alive) &&
            (targeting == null || !targeting.HasTarget) &&
            FlatDistanceSquared(agent.transform.position) <= holdRadius * holdRadius &&
            BodyLanguage.Of(agent) is BodyLanguage body && body.Posture == BodyPosture.Standing;

        private void LetGo()
        {
            if (holderBody != null) holderBody.Release(heldCue);
            if (!ReferenceEquals(holder, null)) Held.Remove(holder);

            holder = null;
            holderBody = null;
            holderTargeting = null;
            holderHealth = null;
            heldCue = null;
        }

        private float FlatDistanceSquared(Vector3 point)
        {
            Vector3 offset = point - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, mode == SpotMode.Wander ? wanderRadius : holdRadius);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * GizmoFacingLength);
        }
    }
}
