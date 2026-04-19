// Reacts to HealthComponent events by enabling/disabling modules at configurable thresholds.
// Handles death cleanup: ragdoll trigger, despawn timer, and noise emission.
// Drag onto any entity with a HealthComponent.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using FMODUnity;

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

    [Header("On Damage")]
    [SerializeField] private bool emitNoiseOnDamage = true;
    [SerializeField] private float damageNoiseRadius = 15f;
    [SerializeField] private EventReference hurtSound;

    [Header("On Death")]
    [SerializeField] private UnityEvent onDeath;
    [SerializeField] private bool emitNoiseOnDeath = true;
    [SerializeField] private float deathNoiseRadius = 20f;
    [SerializeField] private EventReference deathSound;
    [Tooltip("Destroy or disable the GameObject after this delay. 0 = never.")]
    [SerializeField] private float despawnDelay = 8f;
    [SerializeField] private bool disableAgentOnDeath = true;

    private HealthComponent health;
    private NoiseEmitter noiseEmitter;
    private AgentController agentController;

    private void Awake()
    {
        health = GetComponent<HealthComponent>();
        noiseEmitter = GetComponent<NoiseEmitter>();
        agentController = GetComponent<AgentController>();

        if (!health)
            Debug.LogWarning($"{name}: HealthReactionModule needs a HealthComponent.", this);
    }

    private void OnEnable()
    {
        if (!health) return;
        health.OnDamage += HandleDamage;
        health.OnDeath += HandleDeath;

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
    }

    private void HandleDamage(int amount)
    {
        if (emitNoiseOnDamage && noiseEmitter)
            noiseEmitter.Emit(NoiseType.Hurt, damageNoiseRadius);

        if (!hurtSound.IsNull)
            RuntimeManager.PlayOneShot(hurtSound, transform.position);

        CheckThresholds();
    }

    private void HandleDeath()
    {
        if (emitNoiseOnDeath && noiseEmitter)
            noiseEmitter.Emit(NoiseType.Death, deathNoiseRadius);

        if (!deathSound.IsNull)
            RuntimeManager.PlayOneShot(deathSound, transform.position);

        onDeath?.Invoke();

        if (disableAgentOnDeath && agentController)
            agentController.enabled = false;

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
