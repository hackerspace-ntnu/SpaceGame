// Holds a creature standing still and asleep until a player walks close enough to be worth
// waking up for, then hands off to the normal behaviour ladder.
//
// What "asleep" and "waking" LOOK like is not this module's business any more. The animator
// owns both: its entry state is a Sleep clip that holds the standing pose with the eye shut,
// and a Wake trigger sends it to an Awakening clip that opens the eye and falls through to
// Idle. This module decides WHEN, counts the clip out, and switches itself off. It used to
// drive the eyelid's blend shapes directly, frame by frame, from Update -- which fought the
// Animator for the same two properties and only worked because no clip animated them.
//
// The body never moves through any of it. Sleep is Idle's first frame held flat, so a
// creature that wakes up and walks away does it without a single pose change; there is no
// buried squat and no rising clip. (There was, once. See anim.py's SLEEP / WAKE note.)
//
// It is ONE module at Scripted priority for the same reason it always was: while it is
// running it starves chase, cast and wander, and that is the documented use of
// MoveIntent.Idle() -- standing still IS the behaviour -- so nothing else on the prefab has
// to know this module exists.
//
// TWO THINGS THAT FOLLOW FROM THE ANIMATOR OWNING THE SEQUENCE, both easy to trip over:
//
//   * The controller's ENTRY state is Sleep, so a creature with no DormantModule on it stands
//     asleep forever -- nothing else fires the Wake trigger. Removing the component no longer
//     gets you an awake conjurer; leave it on and widen wakeRadius, or call WakeNow().
//   * Nothing ever goes back. Awake is a latch, set once, and no transition targets Sleep or
//     Awakening. A creature that has woken cannot be re-asleep short of respawning it.
//
// It does not wake on damage. Shooting a sleeping conjurer leaves it asleep. That is scope,
// not an oversight: add a HealthComponent hook here if it should flinch awake.
using UnityEngine;

namespace SpaceGame.Agents
{
    public class DormantModule : BehaviourModuleBase
    {
        private enum Phase
        {
            Asleep,     // standing still, lid shut, watching for someone to come close
            Waking,     // the Awakening clip is playing; still claiming the frame
            Done,       // never observed: the module disables itself on the way in
        }

        [Header("Waking")]
        [Tooltip("How close a hostile has to get. A plain radius on purpose: the creature's eye " +
                 "is shut, so a PerceptionModule cone and a line-of-sight test would both be " +
                 "answering a question it cannot ask.")]
        [SerializeField] private float wakeRadius = 25f;
        [Tooltip("Seconds between registry sweeps. Every sleeping creature in the world pays this.")]
        [SerializeField] private float rescanInterval = 0.35f;
        [Tooltip("Length of the Awakening clip, in seconds. The creature keeps standing still " +
                 "for this long after the trigger, so it must match the clip or the module " +
                 "hands off mid-blink. The builder writes it from the clip it generated.")]
        [SerializeField] private float awakenSeconds = 1.4f;

        [Header("Animator")]
        [Tooltip("Left empty, the first Animator found in the children is used -- which is the " +
                 "right one on this prefab, where the Animator sits on the model child rather " +
                 "than on the root.")]
        [SerializeField] private Animator animator;
        [Tooltip("Trigger that sends the animator from Sleep to Awakening.")]
        [SerializeField] private string wakeTrigger = "Wake";
        [Tooltip("Bool latched true once the creature is awake. Gates the attack, so a sleeping " +
                 "creature cannot be startled straight into a cast by the Any State edge.")]
        [SerializeField] private string awakeBool = "Awake";

        private Phase phase = Phase.Asleep;
        private float timer;
        private float rescanTimer;

        private EntityFaction selfFaction;
        private Transform threat;

        private void Reset() => SetPriorityDefault(ModulePriority.Scripted);

        public override string ModuleDescription =>
            "Holds the creature still and asleep until a hostile comes within wakeRadius, then " +
            "triggers the animator's Awakening clip, waits it out, and switches itself off so " +
            "the normal behaviour ladder takes over. The body never moves or changes pose while " +
            "asleep or waking — only the eyelid does, and the clip owns that.\n\n" +
            "• wakeRadius — how close a hostile must get\n" +
            "• awakenSeconds — must match the Awakening clip's length";

        private void Start()
        {
            selfFaction = GetComponent<EntityFaction>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);

            // Loud, because the failure is otherwise a creature that stands asleep forever with
            // nothing at all in the console to say why -- the animator's entry state is Sleep and
            // this is the only thing that ever leaves it.
            if (animator == null || animator.runtimeAnimatorController == null)
                Debug.LogWarning($"[DormantModule] {name} has no Animator with a controller. It " +
                                 "will still hand off on schedule, but it will never open its " +
                                 "eye and the animator will sit in Sleep for good.", this);
        }

        /// <summary>Wakes the creature now, whatever is or is not nearby.</summary>
        /// <remarks>
        /// The escape hatch for a conjurer that is meant to be standing there awake: this
        /// creature's controller starts in Sleep, so removing the component leaves one asleep
        /// rather than alert.
        /// </remarks>
        public void WakeNow()
        {
            if (phase == Phase.Asleep)
                BeginWaking();
        }

        // ── tick ────────────────────────────────────────────────────────────────────────────

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            switch (phase)
            {
                case Phase.Asleep:
                    TickAsleep(deltaTime);
                    break;

                case Phase.Waking:
                    timer += deltaTime;
                    if (timer >= awakenSeconds)
                        Finish();
                    break;
            }

            // Claims the frame for the whole sequence. This is the one legitimate use of Idle:
            // standing still is the behaviour, not a gap between actions.
            return MoveIntent.Idle();
        }

        private void TickAsleep(float deltaTime)
        {
            // Resolved by RELATIONSHIP, not by "the player" — the same call WatchModule makes. It
            // costs nothing here and it is what makes this work for a second player, for a
            // hostile NPC, and on a dedicated server where there is no local player at all.
            //
            // Refresh owns rescanTimer, including the interval gate, so there is deliberately no
            // second timer around this call: two of them decrementing the same field is how a
            // "once a third of a second" sweep quietly becomes a per-frame one.
            threat = TargetResolution.Refresh(threat, ref rescanTimer, rescanInterval, deltaTime,
                                              selfFaction, FactionRelationship.Hostile,
                                              transform.position);
            if (!threat)
                return;

            Vector3 delta = threat.position - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > wakeRadius * wakeRadius)
                return;

            BeginWaking();
        }

        private void BeginWaking()
        {
            phase = Phase.Waking;
            timer = 0f;
            if (animator != null && animator.runtimeAnimatorController != null &&
                !string.IsNullOrEmpty(wakeTrigger))
                animator.SetTrigger(wakeTrigger);
        }

        private void Finish()
        {
            phase = Phase.Done;

            // Latched before the hand-off, not after: the attack hangs off an Any State edge
            // gated on this bool, and a cast that arrives on the same frame the module gives up
            // the ladder would otherwise be swallowed.
            if (animator != null && animator.runtimeAnimatorController != null &&
                !string.IsNullOrEmpty(awakeBool))
                animator.SetBool(awakeBool, true);

            // The hand-off. AgentController re-reads IsActive every frame, so the tick after this
            // one is won by whatever is next on the ladder — ChaseModule/the cast if the creature
            // has a target by now, WanderModule if it does not.
            enabled = false;
        }

        // ── editor ──────────────────────────────────────────────────────────────────────────

        protected override void OnValidate()
        {
            SetMinPriority(ModulePriority.Scripted);
            wakeRadius = Mathf.Max(0.5f, wakeRadius);
            rescanInterval = Mathf.Max(0.05f, rescanInterval);
            awakenSeconds = Mathf.Max(0.01f, awakenSeconds);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, wakeRadius);
        }
    }
}
