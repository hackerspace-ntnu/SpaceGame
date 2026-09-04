using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The air in the suit: a per-player number that drains outside
    /// <see cref="BreathableVolume"/>, is topped up by spending a charged bottle, and suffocates
    /// you when it runs out.
    ///
    /// <para>
    /// This is the consumer the oxygen plant never had. Before it, the loop stopped at
    /// <i>find a bottle → power the plant → fill the bottle</i> and a charged bottle was a thing
    /// you owned rather than a thing you spent.
    /// </para>
    /// <para>
    /// <b>The number lives here; the bottle stays the unit it is spent in.</b> A bottle's charge is
    /// its item IDENTITY (a charged and a drained bottle are two assets), which is what lets it
    /// travel on the wire, into the save and onto the pack for free. A partial suit charge cannot
    /// work that way, so it is a float on the player's own record — which replicates and saves
    /// properly — exactly as the oxygen doc's extension note prescribes.
    /// </para>
    /// <para>
    /// <b>Server decides, everyone displays.</b> Modelled on <c>SandstormVictim</c> and
    /// <c>NetworkedHealthComponent</c>: the drain and the suffocation damage are applied only where
    /// this entity is simulated, the value is published through a <see cref="NetworkVariable{T}"/>,
    /// and a client cannot suffocate itself. The warnings are presentation and are raised by each
    /// machine for its own player.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class SuitOxygen : NetworkBehaviour
    {
        /// <summary>
        /// Every live suit, so a volume being switched off or streamed out can take itself back
        /// out of all of them. Trigger exits are not raised when a collider is disabled.
        /// </summary>
        private static readonly List<SuitOxygen> All = new(8);

        [Header("Supply")]
        [Tooltip("A full suit, in the same arbitrary units the gauge shows as a percentage.")]
        [SerializeField, Min(1f)] private float maxOxygen = 100f;

        [Tooltip("Units lost per second outside breathable air. The default empties a full suit in " +
                 "about ten minutes, which is the one number to tune if the open world feels like " +
                 "a stopwatch rather than a journey.")]
        [SerializeField, Min(0f)] private float drainPerSecond = 0.167f;

        [Tooltip("Units one charged bottle puts back. At the default this is a full refill, so a " +
                 "bottle reads as 'a tank' rather than as a sip.")]
        [SerializeField, Min(1f)] private float bottleRestores = 100f;

        [Header("Running out")]
        [Tooltip("Damage per suffocation tick once the supply is empty.")]
        [SerializeField, Min(1)] private int suffocationDamage = 4;

        [Tooltip("Seconds between suffocation ticks. Damage is an event, not a continuous " +
                 "quantity: ticking every frame floods the network and gives the player nothing " +
                 "extra to feel.")]
        [SerializeField, Min(0.1f)] private float suffocationInterval = 2f;

        [Header("Warnings")]
        [Tooltip("Fraction at or below which the visor warns.")]
        [SerializeField, Range(0f, 1f)] private float warnFraction = 0.30f;

        [Tooltip("Fraction at or below which the visor sounds an alarm.")]
        [SerializeField, Range(0f, 1f)] private float alarmFraction = 0.10f;

        /// <summary>
        /// Everyone / Server, matching <c>NetworkedHealthComponent</c>. Owner write permission
        /// would let a client edit its own air, and Owner READ permission publishes to nobody for
        /// a server-owned object.
        /// </summary>
        private readonly NetworkVariable<float> networkOxygen = new(
            100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// The authoritative value where this is simulated, and the last replicated value
        /// everywhere else. Kept as a plain field rather than read out of the NetworkVariable so
        /// that an unspawned or offline suit still works — writing a NetworkVariable that has
        /// never spawned throws.
        /// </summary>
        private float current;

        private readonly HashSet<BreathableVolume> volumes = new();

        private HealthComponent health;
        private float sinceSuffocation;
        private bool warned;
        private bool alarmed;

        /// <summary>Air now.</summary>
        public float Current => current;

        /// <summary>Air in a full suit.</summary>
        public float Max => maxOxygen;

        /// <summary>Air now as 0..1. Zero when <see cref="Max"/> is somehow zero.</summary>
        public float Fraction => maxOxygen > 0f ? Mathf.Clamp01(current / maxOxygen) : 0f;

        /// <summary>Whether the wearer is standing in breathable air and therefore not draining.</summary>
        public bool Breathing => volumes.Count > 0;

        /// <summary>Fraction at or below which the visor warns. Read by the gauge.</summary>
        public float WarnFraction => warnFraction;

        /// <summary>Fraction at or below which the visor alarms. Read by the gauge.</summary>
        public float AlarmFraction => alarmFraction;

        /// <summary>Units one charged bottle restores. Read by the bottle that spends itself.</summary>
        public float BottleRestores => bottleRestores;

        /// <summary>True where this machine decides what happens to this suit.</summary>
        private bool Authoritative => Network.Simulates(this);

        private void Awake()
        {
            health = GetComponentInChildren<HealthComponent>();
            current = maxOxygen;
        }

        private void OnEnable() => All.Add(this);

        private void OnDisable()
        {
            All.Remove(this);
            volumes.Clear();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                networkOxygen.Value = current;
            }
            else
            {
                // Late joiners arrive with the current value already in the variable and no change
                // event coming, so read it once on spawn.
                networkOxygen.OnValueChanged += ApplyOxygen;
                current = networkOxygen.Value;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) networkOxygen.OnValueChanged -= ApplyOxygen;
        }

        private void ApplyOxygen(float _, float next) => current = next;

        /// <summary>Called by a <see cref="BreathableVolume"/> the wearer has entered.</summary>
        public void EnterBreathable(BreathableVolume volume)
        {
            if (volume != null) volumes.Add(volume);
        }

        /// <summary>Called by a <see cref="BreathableVolume"/> the wearer has left.</summary>
        public void ExitBreathable(BreathableVolume volume)
        {
            if (volume != null) volumes.Remove(volume);
        }

        /// <summary>
        /// Takes <paramref name="volume"/> out of every suit that thought it was inside it.
        /// <para>
        /// Unity raises no <c>OnTriggerExit</c> when a collider is disabled or its scene unloads,
        /// so without this a player standing in a chunk that streams out keeps the shelter for
        /// ever and never breathes their own supply again.
        /// </para>
        /// </summary>
        public static void ForgetVolume(BreathableVolume volume)
        {
            if (volume == null) return;

            foreach (SuitOxygen suit in All) suit.volumes.Remove(volume);
        }

        /// <summary>
        /// Puts <paramref name="amount"/> of air back, capped at a full suit. Authoritative
        /// machines only. Returns how much was actually taken, so a caller can refuse to spend a
        /// bottle that would be almost entirely wasted.
        /// </summary>
        public float Refill(float amount)
        {
            if (!Authoritative || amount <= 0f) return 0f;

            float before = current;
            current = Mathf.Min(maxOxygen, current + amount);
            Publish();

            return current - before;
        }

        /// <summary>How much air a full bottle would waste right now, in units.</summary>
        public float SpaceRemaining => Mathf.Max(0f, maxOxygen - current);

        private void Update()
        {
            // Warnings are presentation: every machine raises them for its own player, off the
            // value the server replicated. Doing it server-side would announce one player's
            // failing suit on everybody's visor.
            if (IsLocalPlayersSuit()) UpdateWarnings();

            if (!Authoritative) return;

            float dt = Time.deltaTime;

            if (!Breathing && drainPerSecond > 0f && current > 0f)
            {
                current = Mathf.Max(0f, current - (drainPerSecond * dt));
                Publish();
            }

            if (current > 0f)
            {
                // Reset rather than let it run: stepping into air a moment before a tick should not
                // bank that tick against you for the next time you step out.
                sinceSuffocation = 0f;
                return;
            }

            sinceSuffocation += dt;
            if (sinceSuffocation < suffocationInterval) return;

            sinceSuffocation = 0f;
            if (health != null && health.GetHealth > 0)
                NetDamage.Apply(health.gameObject, suffocationDamage);
        }

        private void Publish()
        {
            // IsSpawned, because writing a NetworkVariable that has never spawned throws — and an
            // offline session has no NetworkManager at all, which is the ordinary case in the
            // editor rather than an exotic one.
            if (IsSpawned && IsServer) networkOxygen.Value = current;
        }

        /// <summary>
        /// Whether this suit belongs to the player driving this machine — the only one whose
        /// warnings belong on this screen.
        /// </summary>
        private bool IsLocalPlayersSuit() => !IsSpawned || IsOwner;

        private void UpdateWarnings()
        {
            float fraction = Fraction;

            bool wantAlarm = fraction <= alarmFraction;
            bool wantWarn = !wantAlarm && fraction <= warnFraction;

            if (wantAlarm != alarmed)
            {
                alarmed = wantAlarm;
                if (wantAlarm)
                {
                    SystemMessages.Post("suit.oxygen", "OXYGEN CRITICAL", MessageSeverity.Alarm);
                }
                else if (!wantWarn)
                {
                    SystemMessages.Clear("suit.oxygen");
                }
            }

            if (wantWarn != warned)
            {
                warned = wantWarn;
                if (wantWarn) SystemMessages.Post("suit.oxygen", "OXYGEN LOW", MessageSeverity.Warning);
                else if (!wantAlarm) SystemMessages.Clear("suit.oxygen");
            }
        }

        /// <summary>
        /// Server-side restore from a save. Not a property setter: this must never be reachable
        /// from ordinary gameplay code, which changes air by draining or by spending a bottle.
        /// </summary>
        public void RestoreOxygen(float value)
        {
            current = Mathf.Clamp(value, 0f, maxOxygen);
            Publish();

            // The thresholds are re-evaluated from scratch next frame, so a world reloaded into an
            // already-critical suit still announces itself.
            warned = false;
            alarmed = false;
        }
    }
}
