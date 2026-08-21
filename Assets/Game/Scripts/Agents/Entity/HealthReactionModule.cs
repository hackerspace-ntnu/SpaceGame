// Reacts to HealthComponent events by enabling/disabling modules at configurable thresholds.
// Handles death cleanup: ragdoll trigger, despawn timer, and noise emission.
// Drag onto any entity with a HealthComponent.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using FMODUnity;
using SpaceGame.Audio;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [Serializable]
    public struct HealthThresholdReaction
    {
        [Tooltip("Trigger when HP drops to or below this percentage (0-1)."), Range(0f, 1f)]
        public float healthPercentage;
        [Tooltip("Modules to enable when threshold is crossed.")]
        public List<MonoBehaviour> enableModules;
        [Tooltip("Modules to disable when threshold is crossed.")]
        public List<MonoBehaviour> disableModules;
        public UnityEvent onThresholdReached;

        [HideInInspector] public bool triggered;
    }

    public class HealthReactionModule : MonoBehaviour
    {
        [Header("Threshold Reactions")]
        [SerializeField] private List<HealthThresholdReaction> thresholdReactions;

        [Header("Animation")]
        [Tooltip("Animator trigger to fire on damage. Leave empty to disable.")]
        [SerializeField] private string hurtAnimTrigger = "Hurt";
        [Tooltip("Animator trigger to fire on death. Leave empty to disable.")]
        [SerializeField] private string dieAnimTrigger = "Death";

        [Header("On Damage")]
        [SerializeField] private bool emitNoiseOnDamage = true;
        [SerializeField] private float damageNoiseRadius = 15f;
        [SerializeField] private SfxId hurtId = SfxId.EntityHurt;
        [SerializeField] private EventReference hurtSound;

        [Header("On Death")]
        [SerializeField] private UnityEvent onDeath;
        [SerializeField] private bool emitNoiseOnDeath = true;
        [SerializeField] private float deathNoiseRadius = 20f;
        [SerializeField] private SfxId deathId = SfxId.EntityDeath;
        [SerializeField] private EventReference deathSound;
        [Tooltip("Destroy or disable the GameObject after this delay. 0 = never.")]
        [SerializeField] private float despawnDelay = 8f;
        [SerializeField] private bool disableAgentOnDeath = true;

        private HealthComponent health;
        private NoiseEmitter noiseEmitter;
        private AgentController agentController;
        private Animator animator;

        private void Awake()
        {
            health = GetComponent<HealthComponent>();
            noiseEmitter = GetComponent<NoiseEmitter>();
            agentController = GetComponent<AgentController>();
            animator = GetComponentInChildren<Animator>();

            if (!health)
                Debug.LogWarning($"{name}: HealthReactionModule needs a HealthComponent.", this);
        }

        private void OnEnable()
        {
            if (!health) return;
            health.OnDamage += HandleDamage;
            health.OnDeath += HandleDeath;
            health.OnRevive += HandleRevive;

            // Reset threshold triggers in case entity was revived.
            if (thresholdReactions != null)
                for (int i = 0; i < thresholdReactions.Count; i++)
                {
                    var r = thresholdReactions[i];
                    r.triggered = false;
                    thresholdReactions[i] = r;
                }
        }

        private void OnDisable()
        {
            if (!health) return;
            health.OnDamage -= HandleDamage;
            health.OnDeath -= HandleDeath;
            health.OnRevive -= HandleRevive;
        }

        // A revive that lands inside the death despawn window (respawn delay is
        // shorter than despawnDelay in the deathmatch minigame) must cancel the
        // pending Despawn, or the entity is disabled again mid-fight seconds after
        // coming back. Also restores the agent this module disabled on death.
        private void HandleRevive()
        {
            CancelInvoke(nameof(Despawn));

            if (disableAgentOnDeath && agentController)
                agentController.enabled = true;
        }

        private void HandleDamage(int amount)
        {
            if (!string.IsNullOrEmpty(hurtAnimTrigger) && animator)
                animator.SetTrigger(hurtAnimTrigger);

            if (emitNoiseOnDamage && noiseEmitter)
                noiseEmitter.Emit(NoiseType.Hurt, damageNoiseRadius, health.LastDamageSource);

            Sfx.Play(hurtId, transform.position, hurtSound, GetInstanceID());

            CheckThresholds();
        }

        private void HandleDeath()
        {
            // A save being loaded, not a kill. Everything below is a consequence of dying — a sound,
            // a noise event, a UnityEvent, a despawn countdown — and none of them may happen again on
            // the load after the one that killed this entity. What must still happen is the resulting
            // STATE, or the world comes back with a corpse standing up and fighting.
            if (health && health.IsRestoring)
            {
                ApplyDeadState(immediate: true);
                return;
            }

            if (!string.IsNullOrEmpty(dieAnimTrigger) && animator)
                animator.SetTrigger(dieAnimTrigger);

            if (emitNoiseOnDeath && noiseEmitter)
                noiseEmitter.Emit(NoiseType.Death, deathNoiseRadius);

            Sfx.Play(deathId, transform.position, deathSound, GetInstanceID());

            onDeath?.Invoke();

            ApplyDeadState(immediate: false);
        }

        /// <summary>
        /// The lasting part of dying: the agent stops thinking and the body eventually goes away.
        ///
        /// Split out so a restored death can reach it without the one-off effects. <paramref
        /// name="immediate"/> skips the despawn delay, because that delay exists to let a player watch
        /// something die — a corpse arriving from a save has already been dead for however long the
        /// player was away, and waiting out the timer would leave it briefly standing.
        /// </summary>
        private void ApplyDeadState(bool immediate)
        {
            if (disableAgentOnDeath && agentController)
                agentController.enabled = false;

            if (immediate)
            {
                if (despawnDelay > 0f) Despawn();
                return;
            }

            if (despawnDelay > 0f)
                Invoke(nameof(Despawn), despawnDelay);
        }

        private void CheckThresholds()
        {
            if (thresholdReactions == null || !health)
                return;

            float pct = (float)health.GetHealth / health.GetMaxHealth;

            for (int i = 0; i < thresholdReactions.Count; i++)
            {
                HealthThresholdReaction reaction = thresholdReactions[i];
                if (reaction.triggered || pct > reaction.healthPercentage)
                    continue;

                reaction.triggered = true;
                thresholdReactions[i] = reaction;

                foreach (MonoBehaviour mb in reaction.enableModules)
                    if (mb) mb.enabled = true;

                foreach (MonoBehaviour mb in reaction.disableModules)
                    if (mb) mb.enabled = false;

                reaction.onThresholdReached?.Invoke();
            }
        }

        private void Despawn() => gameObject.SetActive(false);
    }
}
