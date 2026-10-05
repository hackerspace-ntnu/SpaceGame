// Reacts to HealthComponent events by enabling/disabling modules at configurable thresholds.
// Handles death cleanup: the body's lifetime (Remains) and noise emission. The body going limp is AgentRagdoll's,
// which subscribes to the same HealthComponent directly — a corpse has to be limp on every machine
// looking at it, and this module's consequences are deliberately run only where the death happened.
// Drag onto any entity with a HealthComponent.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using FMODUnity;
using SpaceGame.Audio;
using SpaceGame.Core;
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
        [Tooltip("Seconds the body lies where it fell before the world takes it away -- and then only " +
                 "once no player is close enough to watch it go (Remains). 0 = it stays for good, which " +
                 "is a Strider monowheel's wreck: AbandonedVehicle decides when that goes.")]
        [SerializeField] private float corpseLifetime = 180f;
        [SerializeField] private bool disableAgentOnDeath = true;

        /// <summary>Whether this body is ever taken away on its own.</summary>
        public bool Despawns => corpseLifetime > 0f;

        [Header("Diagnostics")]
        [Tooltip("Log every hit this entity takes, with who dealt it and how much. For 'it keeps taking damage and I cannot see what from'. HealthComponent already records LastDamageSource; nothing was reading it back out, so the only way to answer the question was to guess. Off by default: a busy fight would fill the console.")]
        [SerializeField] private bool logDamage = false;

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

        // A body brought back to life is not remains any more: its countdown stops, or the world would
        // take a living creature away. Also restores the agent this module disabled on death.
        private void HandleRevive()
        {
            if (TryGetComponent(out Remains remains)) remains.Stop();

            if (disableAgentOnDeath && agentController)
                agentController.enabled = true;
        }

        private void HandleDamage(int amount)
        {
            if (logDamage)
            {
                Transform source = health.LastDamageSource;
                // The full path, not just the name: "Cactus" and "Cactus" are two different props,
                // and the parent chain is what says which system a hit came out of.
                string who = source != null
                    ? $"{Path(source)} ({source.GetInstanceID()})"
                    : "<no source recorded>";
                Debug.Log($"[Damage] {name} took {amount} from {who}, now " +
                          $"{health.GetHealth}/{health.GetMaxHealth}", this);
            }

            if (!string.IsNullOrEmpty(hurtAnimTrigger) && animator)
                animator.SetTrigger(hurtAnimTrigger);

            if (emitNoiseOnDamage && noiseEmitter)
                noiseEmitter.Emit(NoiseType.Hurt, damageNoiseRadius, health.LastDamageSource);

            Sfx.Play(hurtId, transform.position, hurtSound, GetInstanceID());

            CheckThresholds();
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }

        /// <summary>
        /// Tell the tribe's ledger who killed one of theirs.
        ///
        /// <para>
        /// Below the <c>IsRestoring</c> guard in <see cref="HandleDeath"/>, and that placement is
        /// the whole point: <c>HealthComponent</c> re-raises <c>OnDeath</c> for every corpse the
        /// world loads, so reporting above it would charge the player for the same murder on every
        /// reload until the tribe was permanently at war with them.
        /// </para>
        /// <para>
        /// A mount or vehicle killed out from under a tribe member is design §3.4's `MountKill` and
        /// is NOT reported here: the animal is Fauna, so its own faction keeps no ledger, and
        /// knowing whose mount it was needs the roster ownership Phase 4 adds. Recorded rather than
        /// faked — attributing it to whatever faction happened to be nearby would be worse than not
        /// attributing it at all.
        /// </para>
        /// </summary>
        private void ReportKillToLedger()
        {
            FactionGoodwillLedger ledger = FactionGoodwillLedger.Instance;
            if (ledger == null || health == null) return;

            if (!TryGetComponent(out EntityFaction mine) || !ledger.Tracks(mine.Faction)) return;

            Transform source = health.LastDamageSource;
            if (source == null) return;

            // Attributed to the entity, not the collider or the projectile that carried the
            // reference — the same climb ProvocationModule makes, and for the same reason.
            EntityFaction killer = source.GetComponentInParent<EntityFaction>();
            if (killer == null || killer.transform == transform) return;

            ledger.Report(mine, killer, GoodwillEvent.Kill);
        }

        private void HandleDeath()
        {
            // Decided somewhere else, not a kill on this machine. The ledger, the noise event and
            // the UnityEvent are consequences that happen once, where the death was decided, and
            // never again on a load. What must still happen here is the resulting STATE, or the
            // world comes back with a corpse standing up and fighting.
            if (health && health.IsRestoring)
            {
                // The server's death arriving on a client, live: a watcher sees and hears it like the
                // host does. The body's countdown stays the server's (ApplyDeadState).
                if (health.IsReplicating)
                    PresentDeath();

                ApplyDeadState();
                return;
            }

            ReportKillToLedger();
            PresentDeath();

            if (emitNoiseOnDeath && noiseEmitter)
                noiseEmitter.Emit(NoiseType.Death, deathNoiseRadius);

            onDeath?.Invoke();

            ApplyDeadState();
        }

        /// <summary>
        /// What a watcher sees and hears of a death: the death animation and the death sound. Run
        /// on the machine that decided the death and on every client the death replicates to.
        /// </summary>
        private void PresentDeath()
        {
            if (!string.IsNullOrEmpty(dieAnimTrigger) && animator)
                animator.SetTrigger(dieAnimTrigger);

            Sfx.Play(deathId, transform.position, deathSound, GetInstanceID());
        }

        /// <summary>
        /// The lasting part of dying: the agent stops thinking and the body lies there, counting down
        /// to being taken away.
        ///
        /// Split out so a restored death can reach it without the one-off effects. A restored death
        /// does not restart a countdown already running: <c>RemainsSaveable</c> may have put back the
        /// time the body had left before this death was replayed, and a body from a save older than
        /// that saver gets the full lifetime. The countdown is the server's -- a client's copy goes
        /// when the server's network despawn reaches it.
        /// </summary>
        private void ApplyDeadState()
        {
            if (disableAgentOnDeath && agentController)
                agentController.enabled = false;

            if (corpseLifetime > 0f && Network.Decides)
                Remains.On(gameObject).BeginUnlessCounting(corpseLifetime);
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
    }
}
