// What a nesting creature LOOKS like, on every machine: lying in the nest while all is calm,
// on its feet the moment a player comes inside the warn radius, and a roar when its aggression
// reaches Drawn — the last warning before NestModule's meter turns into a fight.
//
// The split from NestModule is the authority gate. NestModule decides (walks home, feeds the meter)
// and only runs on the deciding machine; this only draws, so it runs everywhere. Each input it reads
// is one a watcher has too: the nest is static scene dressing, players are in SessionPlayers on every
// machine, the body's own movement is replicated, and the aggression band arrives as
// AgentAction.Band — broadcast from here by the authority, exactly like AggressionTelegraphModule
// does for nomads (that module is humanoid-only, so a creature uses this instead).
//
// Animator only: it moves nothing, damages nothing and writes nothing a peer reads. Nothing to save —
// resting is re-derived every frame and the band comes back with ProvocationSaveable.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    public class NestPresentation : MonoBehaviour
    {
        [Tooltip("How far from the creature a CreatureNest is looked for.")]
        [SerializeField] private float nestSearchRadius = 40f;
        [Tooltip("Below this ground speed (m/s) the creature counts as standing still and may lie down.")]
        [SerializeField] private float stillSpeed = 0.3f;
        [Tooltip("Seconds between player sweeps.")]
        [SerializeField] private float sweepInterval = 0.25f;
        [Tooltip("Bool held true while lying in the nest.")]
        [SerializeField] private string restingBool = "IsResting";
        [Tooltip("Trigger fired when the aggression band reaches Drawn — the last warning.")]
        [SerializeField] private string warningTrigger = "Roar";

        private readonly List<Transform> players = new List<Transform>(4);

        private CreatureNest nest;
        private ProvocationModule provocation;
        private HealthComponent health;
        private AgentAnimatorDriver animatorDriver;
        private AgentAuthority authority;
        private AggressionBand shown;
        private Vector3 lastPosition;
        private float sweepTimer;
        private bool intruderNear;

        private void Awake()
        {
            provocation = GetComponent<ProvocationModule>();
            health = GetComponent<HealthComponent>();
            animatorDriver = GetComponentInChildren<AgentAnimatorDriver>(true);
            authority = new AgentAuthority(this);
        }

        private void OnTransformParentChanged() => authority?.Invalidate();

        private void OnEnable()
        {
            nest = CreatureNest.Nearest(transform.position, nestSearchRadius);
            shown = provocation != null ? provocation.Band : AggressionBand.Calm;
            lastPosition = transform.position;
            sweepTimer = 0f;
            if (provocation != null) provocation.BandChanged += OnBandChanged;
            this.NetOn(NetMsg.AgentActed, OnAgentActed);
        }

        private void OnDisable()
        {
            if (provocation != null) provocation.BandChanged -= OnBandChanged;
            this.NetOff(NetMsg.AgentActed, OnAgentActed);
            SetResting(false);
        }

        private void Update()
        {
            if (nest == null)
            {
                nest = CreatureNest.Nearest(transform.position, nestSearchRadius);
                if (nest == null) return;
            }

            sweepTimer -= Time.deltaTime;
            if (sweepTimer <= 0f)
            {
                sweepTimer = sweepInterval;
                intruderNear = AnyPlayerWithin(nest.WarnRadius);
            }

            Vector3 moved = transform.position - lastPosition;
            lastPosition = transform.position;
            moved.y = 0f;
            bool still = Time.deltaTime > 0f && moved.magnitude / Time.deltaTime < stillSpeed;

            Vector3 offset = transform.position - nest.Position;
            offset.y = 0f;
            bool home = offset.sqrMagnitude <= nest.HomeRadius * nest.HomeRadius;
            bool alive = health == null || health.Alive;

            SetResting(alive && home && still && !intruderNear && shown == AggressionBand.Calm);
        }

        private bool AnyPlayerWithin(float radius)
        {
            SessionPlayers.Collect(players);
            float sqr = radius * radius;
            foreach (Transform player in players)
                if ((player.position - nest.Position).sqrMagnitude <= sqr)
                    return true;
            return false;
        }

        // The authority's meter moved: present it and tell the watchers, who cannot derive it.
        private void OnBandChanged(AggressionBand previous, AggressionBand next)
        {
            Present(next);
            if (Network.Server)
                AgentActionRelay.Broadcast(this, AgentAction.Band, transform.position, transform.forward, (int)next);
        }

        private void OnAgentActed(in NetArg arg, ulong sender)
        {
            if (arg.A != AgentAction.Band || authority == null || authority.SimulatedHere) return;
            Present((AggressionBand)arg.B);
        }

        private void Present(AggressionBand next)
        {
            if (next == AggressionBand.Drawn && shown < AggressionBand.Drawn && animatorDriver)
                animatorDriver.TriggerByName(warningTrigger);
            shown = next;
        }

        private void SetResting(bool resting)
        {
            if (animatorDriver) animatorDriver.SetBoolByName(restingBool, resting);
        }

        private void OnValidate()
        {
            nestSearchRadius = Mathf.Max(1f, nestSearchRadius);
            stillSpeed = Mathf.Max(0.01f, stillSpeed);
            sweepInterval = Mathf.Max(0.05f, sweepInterval);
        }
    }
}
