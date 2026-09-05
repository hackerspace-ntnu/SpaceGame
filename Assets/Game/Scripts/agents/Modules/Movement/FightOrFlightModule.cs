// A peaceful animal's answer to being threatened: run first, fight if running is not working.
//
// ## What this owns, and what it deliberately does not
//
// The agent stack gives exactly one owner to each of three decisions — who to fight
// (AgentTargeting), where to go (AgentGoal), how to move (IMovementMotor) — and a module that
// duplicates one of them is the bug that architecture exists to prevent. This owns none of them.
// It owns a fourth thing the stack had no home for: **which of two existing behaviours is allowed
// to answer right now**.
//
// It sits one step above FleeModule at Override + 1, which makes it the first movement module
// ticked, so its bookkeeping runs every frame. It answers `null` — pass, I have no opinion — for
// all but one moment: while the roar is playing it returns Idle, because standing still is
// genuinely the behaviour there and the telegraph is worthless if the animal charges through it.
// The rest of the time all it does is switch FleeModule on and off:
//
//     FLEEING   FleeModule enabled at Override (30). It outranks everything below, so the
//               animal runs and the melee module never gets a frame.
//     ENRAGED   FleeModule disabled. ChaseModule (20) and CloseCombatModule (23) are suddenly
//               the highest live modules, so the same creature closes and attacks with no
//               second target system and no state threaded through the stack.
//
// That is the whole mechanism. Both behaviours are stock modules doing their normal job.
//
// ## What flips it
//
// Two rules, because a wounded animal and a cornered animal are different animals and both turn:
//
//   * **Wounded** — more than `enrageDamage` taken since the threat appeared. Chip damage from a
//     stray shot does not qualify; a magazine does.
//   * **Cornered** — the threat is inside `corneredDistance`. Running is pointless at knife range,
//     and an animal that keeps trying to run while being shot point-blank reads as broken.
//
// Both are exposed rather than hard-coded, because the interesting behaviour here is the one that
// emerges from where those two numbers sit relative to the player's damage output and closing
// speed, and that is a thing to tune by playing rather than to derive (GDC-L1-SYS-0002).
//
// Rage is not permanent: `rageDuration` seconds after the last hit it drops back to fleeing, and
// ProvocationModule's own leash then forgets the target entirely. So you can end a fight by
// leaving, which is the same contract every other peaceful creature here offers.
//
// ## The roar
//
// Entering ENRAGED fires the roar trigger and the aggro sound. This is a telegraph, not a
// flourish: it is the only warning the player gets that something friendly has stopped being
// friendly, and it is timed to land *before* the first charge so the warning is actionable
// (GDC-L1-ANIM-0003). Do not move it to the moment of impact.
//
// ## Being spooked
//
// A gunshot does not hurt anyone, so nothing in the damage path would ever notice it. The prefab
// wires NoiseReceiverModule to aggro on Gunshot, which hands the shooter to AgentTargeting; this
// module then feeds that through ProvocationModule.Provoke so the grudge machinery holds it for
// as long as it holds a real attacker's. The animal bolts, and because a gunshot deals no damage
// it stays in FLEEING — being shot AT is not the same as being shot.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    public class FightOrFlightModule : BehaviourModuleBase
    {
        public enum Mood
        {
            Calm,
            Fleeing,
            Enraged,
        }

        [Header("Escalation")]
        [Tooltip("Damage taken since this threat appeared before the animal stops running and " +
                 "fights. A stray shot should not qualify; a sustained burst should.")]
        [SerializeField] private int enrageDamage = 45;

        [Tooltip("If the threat gets this close, running is pointless and the animal turns. Keep " +
                 "it comfortably above CloseCombatModule.attackRange or it will turn to fight " +
                 "from outside the range it can actually reach, and stand there.")]
        [SerializeField] private float corneredDistance = 7f;

        [Tooltip("Seconds after the last damage before rage fades back to fleeing. The grudge " +
                 "itself outlives this — ProvocationModule.calmDownDelay owns that.")]
        [SerializeField] private float rageDuration = 12f;

        [Header("Roar")]
        [Tooltip("Animator trigger fired when the animal turns to fight. Fires once per " +
                 "escalation, before the first charge, as the player's warning.")]
        [SerializeField] private string roarTrigger = "Roar";

        [Tooltip("Seconds the roar holds before the charge is allowed to start. Should match the " +
                 "roar clip, or the animal lunges through its own telegraph.")]
        [SerializeField] private float roarDuration = 1.4f;

        [SerializeField] private SfxId roarSound = SfxId.EntityAggro;

        [Tooltip("A sound FILE under StreamingAssets/Audio that replaces the FMOD event above for this creature. Named, it wins; empty, the catalog id plays as before. This exists because new FMOD events cannot be authored in this project - the Studio project that built the banks is not in the repo - so the 19 shipped events are all there is. It is a file rather than an AudioClip because Unity audio is DISABLED project-wide (AudioManager m_DisableAudio) and there is no Unity AudioListener, so an AudioSource here is silent; SfxFile plays it through FMOD instead.")]
        [SerializeField] private string roarFile;

        [Tooltip("Animator bool held true for the length of the roar. The controller uses it to " +
                 "stop Hurt and the attack from cutting the roar short: they fire from AnyState, " +
                 "Unity consumes only the trigger it takes, and a Hurt set by the same shot that " +
                 "enraged the animal interrupts the roar one frame in - which looks exactly like " +
                 "the roar never playing at all. Empty to skip the gate.")]
        [SerializeField] private string roaringFlag = "IsRoaring";

        [Tooltip("How far the roar carries, in metres. A telegraph the player cannot hear is not a telegraph.")]
        [SerializeField] private float roarClipRange = 60f;

        [Tooltip("Loudness of the file, 0..1.")]
        [SerializeField, Range(0f, 1f)] private float roarClipVolume = 1f;

        [Header("Wiring")]
        [Tooltip("Enabled only while fleeing. Found on this object when left empty.")]
        [SerializeField] private FleeModule fleeModule;

        [Tooltip("Enabled only while enraged — the chase and attack modules. Left empty, this " +
                 "finds the ChaseModule and CloseCombatModule on this object.\n\n" +
                 "These have to be switched off while fleeing, not merely out-ranked. FleeModule " +
                 "returns null on any frame it cannot find a NavMesh point to run to, and null " +
                 "means *pass* — so the frame falls through to chase and the animal walks calmly " +
                 "at the thing it is supposed to be running from.")]
        [SerializeField] private Behaviour[] combatModules;

        [SerializeField] private ProvocationModule provocation;
        [SerializeField] private AgentAnimatorDriver animatorDriver;

        private HealthComponent health;

        private Mood mood = Mood.Calm;
        private Transform threat;
        private int damageSinceThreat;
        private float rageTimer;
        private float roarTimer;

        public Mood CurrentMood => mood;
        public bool IsEnraged => mood == Mood.Enraged;

        /// <summary>True while the roar is still playing and the animal is holding still for it.</summary>
        public bool IsRoaring => roarTimer > 0f;

        // Above FleeModule (Override), so this is the first movement module ticked and its
        // bookkeeping runs every frame regardless of who ends up winning the frame.
        private void Reset() => SetPriorityDefault(ModulePriority.Override + 1);

        private void Awake()
        {
            if (fleeModule == null) fleeModule = GetComponent<FleeModule>();
            if (provocation == null) provocation = GetComponent<ProvocationModule>();
            if (animatorDriver == null) animatorDriver = GetComponentInChildren<AgentAnimatorDriver>();
            health = GetComponent<HealthComponent>();

            if (combatModules == null || combatModules.Length == 0)
            {
                var found = new List<Behaviour>(2);
                var chase = GetComponent<ChaseModule>();
                var melee = GetComponent<CloseCombatModule>();
                if (chase != null) found.Add(chase);
                if (melee != null) found.Add(melee);
                combatModules = found.ToArray();
            }
        }

        private void OnEnable()
        {
            // Streaming, respawn and save restores all re-enable an agent, and an animal that
            // comes back mid-rage with no threat left would stand there with flee switched off.
            mood = Mood.Calm;
            threat = null;
            damageSinceThreat = 0;
            rageTimer = 0f;
            roarTimer = 0f;

            // Calm is flee-shaped, not fight-shaped: a peaceful animal that comes back from a
            // stream or a save with its combat modules live would charge the first thing that
            // looked at it.
            if (fleeModule != null)
                fleeModule.enabled = true;
            for (int i = 0; combatModules != null && i < combatModules.Length; i++)
                if (combatModules[i] != null)
                    combatModules[i].enabled = false;

            if (health != null)
                health.OnDamage += HandleDamage;
        }

        private void OnDisable()
        {
            if (health != null)
                health.OnDamage -= HandleDamage;
        }

        private void HandleDamage(int amount)
        {
            // A save being replayed, not a hit. HealthComponent re-raises OnDamage while restoring,
            // and a creature that was wounded once should not come back enraged at nobody.
            if (amount <= 0 || (health != null && health.IsRestoring))
                return;

            damageSinceThreat += amount;
            rageTimer = rageDuration;
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (roarTimer > 0f)
                roarTimer -= deltaTime;

            Transform target = context.Targeting != null ? context.Targeting.Target : null;

            if (target == null)
            {
                Relax();
                return null;
            }

            // A new threat starts a fresh account. Without this, damage from a fight ten minutes
            // ago would enrage the animal the instant anything else looked at it.
            if (target != threat)
            {
                threat = target;
                damageSinceThreat = 0;
                rageTimer = 0f;
                if (mood == Mood.Enraged)
                    SetMood(Mood.Fleeing);
            }

            // Hold the target the way a real grudge is held. A gunshot arrives through
            // NoiseReceiverModule as a bare ForceTarget, and AgentTargeting's staleness pass drops
            // it again within seconds — a Fauna creature is Neutral toward everything, so nothing
            // would ever re-acquire it and the animal would take two steps and forget.
            if (provocation != null && provocation.Aggressor != target)
                provocation.Provoke(target);

            if (mood == Mood.Calm)
                SetMood(Mood.Fleeing);

            if (mood == Mood.Fleeing && ShouldTurnAndFight(context, target))
                SetMood(Mood.Enraged);
            else if (mood == Mood.Enraged)
            {
                rageTimer -= deltaTime;
                if (rageTimer <= 0f)
                    SetMood(Mood.Fleeing);
            }

            // Planted for the length of the roar, and only then. This is the one case where
            // claiming the frame is correct rather than greedy: everything below is chase and
            // attack, and letting them run here would start the charge underneath the warning
            // that exists to precede it. Last, so the bookkeeping above still runs while roaring.
            if (roarTimer > 0f)
                return MoveIntent.StopAndFace(target.position);

            return null;
        }

        private bool ShouldTurnAndFight(in AgentContext context, Transform target)
        {
            if (damageSinceThreat >= enrageDamage)
                return true;

            return Vector3.Distance(context.Position, target.position) <= corneredDistance;
        }

        private void Relax()
        {
            threat = null;
            damageSinceThreat = 0;
            rageTimer = 0f;

            if (mood != Mood.Calm)
                SetMood(Mood.Calm);
        }

        private void SetMood(Mood next)
        {
            if (mood == next)
                return;

            bool escalating = next == Mood.Enraged;
            mood = next;

            // The lever this module pulls, and it has to cut BOTH ways. Priority alone does not
            // make these mutually exclusive: FleeModule out-ranks chase, but it returns null on
            // any frame it cannot find a NavMesh point to run to, and null means pass — so the
            // frame falls through and the animal walks at the threat it is fleeing. Measured on
            // Appa: he covered 11.5 m toward the shooter while nominally in FLEEING, closed to
            // 3.8 m, and then legitimately tripped the cornered rule. Switch them, do not stack
            // them.
            if (fleeModule != null)
            {
                fleeModule.enabled = !escalating;

                // Deciding he is frightened is THIS module's job; FleeModule's own triggerRadius
                // answers a different question — "did something frightening get close" — and is
                // the wrong gate for an alarm that arrived from further away. A gunshot carries
                // 40 m and FleeModule trips at 22, so in that band he acquired the shooter, went
                // to Fleeing, and then walked his errand as if nothing had happened. Measured:
                // shot from 30 m, he ended up 7 m CLOSER to the gun at 0.62 m/s.
                if (next == Mood.Fleeing)
                    fleeModule.Alarm();
            }

            for (int i = 0; i < combatModules.Length; i++)
                if (combatModules[i] != null)
                    combatModules[i].enabled = escalating;

            if (!escalating)
                return;

            rageTimer = Mathf.Max(rageTimer, rageDuration);
            roarTimer = roarDuration;

            if (animatorDriver != null && !string.IsNullOrEmpty(roarTrigger))
                animatorDriver.TriggerByName(roarTrigger);

            PlayRoar();
        }

        /// <summary>
        /// The roar, from a clip when one is pinned and from the FMOD catalog otherwise.
        ///
        /// <para>
        /// Every machine that runs this reaches here, which is what you want for a telegraph:
        /// the point of a roar is that the player hears the charge coming, and a sound only the
        /// server plays is heard by nobody on a client.
        /// </para>
        /// </summary>
        private void SetRoaringFlag(bool value)
        {
            if (animatorDriver != null && !string.IsNullOrEmpty(roaringFlag))
                animatorDriver.SetBoolByName(roaringFlag, value);
        }

        private void PlayRoar()
        {
            // The file first, the catalog second. SfxFile returns false when the file is
            // missing or FMOD refuses it, so a typo falls back to the event rather than to silence.
            if (!string.IsNullOrEmpty(roarFile) &&
                SfxFile.Play(roarFile, transform.position, roarClipVolume,
                             Mathf.Min(6f, roarClipRange * 0.1f), roarClipRange))
                return;

            Sfx.Play(roarSound, transform.position, GetInstanceID());
        }

        public override string ModuleDescription =>
            "Peaceful animal temperament: runs from a threat, turns and fights when running stops " +
            "working. It switches FleeModule off so the melee modules below it can win the frame, " +
            "and holds the animal still for the length of the roar.\n\n" +
            "• enrageDamage — damage from one threat before it turns and fights\n" +
            "• corneredDistance — threat this close makes running pointless; it turns\n" +
            "• rageDuration — seconds after the last hit before it goes back to running\n" +
            "• roarTrigger / roarDuration — the telegraph, fired before the first charge\n\n" +
            "Needs FleeModule (fleeFromCurrentTarget on), ProvocationModule, and a chase + melee " +
            "module below Override priority.";

        protected override void OnValidate()
        {
            enrageDamage = Mathf.Max(1, enrageDamage);
            corneredDistance = Mathf.Max(0.5f, corneredDistance);
            rageDuration = Mathf.Max(0.5f, rageDuration);
            roarDuration = Mathf.Max(0f, roarDuration);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = mood == Mood.Enraged ? new Color(1f, 0.3f, 0.1f)
                                                : new Color(0.4f, 0.7f, 1f);
            Gizmos.DrawWireSphere(transform.position, corneredDistance);
        }
    }
}
