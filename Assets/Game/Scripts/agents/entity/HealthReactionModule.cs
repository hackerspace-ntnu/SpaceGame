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

            // A restore has already said which thresholds had fired, so this enable must not
            // contradict it. Consumed rather than left standing, so the next genuine enable — a
            // revive, a despawned body switched back on — resets the latches as it always did.
            if (thresholdsRestored)
            {
                thresholdsRestored = false;
                return;
            }

            // Reset threshold triggers in case entity was revived.
            if (thresholdReactions != null)
                for (int i = 0; i < thresholdReactions.Count; i++)
                {
                    var r = thresholdReactions[i];
                    r.triggered = false;
                    thresholdReactions[i] = r;
                }
        }

        // ── Save/restore ──────────────────────────────────────────────────────────
        //
        // <c>HealthThresholdReaction.triggered</c> is [HideInInspector] on a serialized struct and is
        // explicitly cleared above, so it is pure runtime state that nothing captured. Two things
        // went wrong because of that, and only one of them was a loss.
        //
        // THE ACTIVE MISBEHAVIOUR. A creature restored at 20% health comes back with every latch
        // clear, so the first hit it takes afterwards re-crosses thresholds it crossed long ago and
        // <c>onThresholdReached</c> fires AGAIN — the enrage event replays, the scream replays, on
        // every single load. Persisting the latches is what stops it.
        //
        // THE LOSS. The reactions' enable/disable lists are durable state written into module
        // `enabled` flags, and nothing put them back. So an agent that a threshold had switched OFF
        // — including its AgentController, which is exactly how ApplyDeadState parks a corpse — came
        // back switched on and thinking again. Restoring re-applies those lists, silently.
        private bool thresholdsRestored;

        /// <summary>Which thresholds had already fired, positionally. Read by the save system.</summary>
        public bool[] TriggeredThresholds()
        {
            if (thresholdReactions == null) return System.Array.Empty<bool>();

            var flags = new bool[thresholdReactions.Count];
            for (int i = 0; i < thresholdReactions.Count; i++)
                flags[i] = thresholdReactions[i].triggered;

            return flags;
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Positional, and short or long arrays are tolerated: a reaction added to the prefab since
        /// the save reads as "not yet fired", which is the right answer for a threshold that did not
        /// exist to be crossed.
        /// </summary>
        public void RestoreThresholds(bool[] flags)
        {
            thresholdsRestored = true;
            if (thresholdReactions == null) return;

            for (int i = 0; i < thresholdReactions.Count; i++)
            {
                HealthThresholdReaction reaction = thresholdReactions[i];
                bool fired = flags != null && i < flags.Length && flags[i];

                reaction.triggered = fired;
                thresholdReactions[i] = reaction;

                // Silently: the modules this reaction switched are STATE and must come back, but the
                // UnityEvent is an ANNOUNCEMENT of a moment that has already happened.
                if (fired) ApplyReaction(reaction, announce: false);
            }

            // The models on the hand bones are a projection of which combat modules are enabled, and
            // the lines above have just changed that. Their own Awake ran with the prefab's answer.
            WeaponSelector.RefreshAll(gameObject);
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

                // A save being applied, not a wound. The same rule HandleDeath follows and for the
                // same reason: the resulting STATE must happen, the announcement must not. Belt and
                // braces today — RestoreHealth raises OnRestored rather than OnDamage, so this path
                // is not currently reached during a restore — and it is the guard that keeps it
                // correct if anything ever routes a restore through Damage.
                ApplyReaction(reaction, announce: health == null || !health.IsRestoring);
            }
        }

        /// <summary>
        /// One threshold's consequences. <paramref name="announce"/> separates the lasting half — the
        /// modules this reaction switches on and off — from the one-off half, which is a UnityEvent
        /// that may play a scream, spawn a effect or start a cutscene and must fire exactly once in
        /// the life of the creature, not once per load.
        /// </summary>
        private void ApplyReaction(in HealthThresholdReaction reaction, bool announce)
        {
            if (reaction.enableModules != null)
                foreach (MonoBehaviour mb in reaction.enableModules)
                    if (mb) mb.enabled = true;

            if (reaction.disableModules != null)
                foreach (MonoBehaviour mb in reaction.disableModules)
                    if (mb) mb.enabled = false;

            if (announce) reaction.onThresholdReached?.Invoke();
        }

        private void Despawn() => gameObject.SetActive(false);
    }
}
