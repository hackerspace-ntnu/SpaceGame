// Something the sand can strip. Put it on the player, on NPCs, on anything with health.
//
// The damage is applied on the server only. Clients render the storm and feel none of it, so the
// health they see is the health the server replicated — the same arrangement every other damage
// source in the game already has, and the reason a client cannot sand itself to death.
using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.World.Weather
{
    [DisallowMultipleComponent]
    public class SandstormVictim : MonoBehaviour
    {
        [Tooltip("Seconds between damage ticks. Storm damage is a gameplay event, not a continuous " +
                 "quantity: ticking every frame would flood the network with tiny damage messages " +
                 "and give the player nothing extra to feel.")]
        [SerializeField, Min(0.1f)] private float tickInterval = 0.5f;

        [Tooltip("Where exposure is measured. Leave empty to use this object's own position — set " +
                 "it to the head for anything tall enough to poke out of a low storm.")]
        [SerializeField] private Transform samplePoint;

        // Shared: filled and fully consumed inside one synchronous call, so no other victim can
        // observe it mid-use. Same reasoning as AgentTargeting's candidate buffer.
        private static readonly List<ISandProtection> ProtectionBuffer = new List<ISandProtection>(8);

        private IDamageable damageable;
        private float sinceLastTick;

        // Storm damage is fractional per tick and health is an integer. Carrying the remainder is
        // what stops a 0.4-per-tick storm from rounding to zero and being harmless forever.
        private float pendingDamage;

        /// <summary>Storm density at this victim after shelter, 0 to 1. For HUD and debugging.</summary>
        public float Exposure { get; private set; }

        /// <summary>Best protection currently worn, 0 to 1. Refreshed on the damage tick.</summary>
        public float Protection { get; private set; }

        /// <summary>Health per second currently being lost. Zero when safe.</summary>
        public float DamageRate { get; private set; }

        private void Awake()
        {
            damageable = GetComponent<IDamageable>();
            if (damageable == null)
                damageable = GetComponentInParent<IDamageable>();

            if (damageable == null)
                Debug.LogWarning($"[Sandstorm] {name} has a SandstormVictim but nothing damageable " +
                                 "to apply it to.", this);

            if (samplePoint == null)
                samplePoint = transform;
        }

        private void Update()
        {
            // Only the server decides who is hurt, but everyone keeps Exposure current: the HUD and
            // the audio on a client read it, and they would otherwise show clear air in a storm.
            sinceLastTick += Time.deltaTime;
            if (sinceLastTick < tickInterval)
                return;

            // The accumulated time, not the nominal interval: a frame spike must not quietly cost
            // the player less health than the seconds they actually spent in the sand.
            float elapsed = sinceLastTick;
            sinceLastTick = 0f;

            Refresh(elapsed);
        }

        private void Refresh(float elapsed)
        {
            if (!Sandstorms.TrySample(samplePoint.position, out StormSample sample))
            {
                Exposure = 0f;
                Protection = 0f;
                DamageRate = 0f;
                pendingDamage = 0f;
                return;
            }

            Exposure = sample.Exposure;
            Protection = BestProtection();
            DamageRate = sample.Profile.damagePerSecond * Exposure * (1f - Protection);

            if (DamageRate <= 0f || !HasAuthority)
            {
                pendingDamage = 0f;
                return;
            }

            if (damageable == null || !damageable.Alive)
                return;

            pendingDamage += DamageRate * elapsed;

            int whole = Mathf.FloorToInt(pendingDamage);
            if (whole <= 0)
                return;

            pendingDamage -= whole;
            damageable.Damage(whole);
        }

        // The best single source, not the sum. Wearing a 0.9 helmet and a 0.4 cloak gives 0.9:
        // stacking would let five mediocre items add up to immunity, which is both untunable and
        // the kind of rule players discover by accident and then never stop exploiting.
        private float BestProtection()
        {
            GetComponentsInChildren(ProtectionBuffer);

            float best = 0f;
            for (int i = 0; i < ProtectionBuffer.Count; i++)
                best = Mathf.Max(best, ProtectionBuffer[i].SandProtection);

            return Mathf.Clamp01(best);
        }

        private static bool HasAuthority => !Network.IsNetworked || Network.Server;
    }
}
