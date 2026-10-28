// The witch's main attack: she plants her feet, throws her arms out and spins, and every turn she
// flings small balls of poison the way she is facing. The balls are slow, so a spin draws a spiral
// across the clearing that the player has to read and walk through -- and that the keepers, who
// cannot dodge, have to be carried out of.
//
// Why a spiral (SpriteHamletQuest.md, "Witch"): the thorn eruption it replaced as her main attack was
// a ring on the ground that tracked the player; nothing about it involved the keepers, and a ring
// around thorns rising from the earth read as nonsense. A spiral is visible from anywhere, punishes
// standing still without being unavoidable (the gaps are the counterplay, GDC-L1-DESIGN-0002), and
// sweeps the whole clearing -- so it threatens the keepers wherever they stand, which is what gives
// the player a reason to pick them up.
//
// Decide on one machine, present on all -- ConjurerCastModule's split. The server decides when and
// which way to spin and broadcasts NetMsg.WitchSpiral once; every machine then spins her and throws
// the same balls on its own clock, and only the server's balls bill anything. Freezing her mid-spin
// ends it everywhere, because stasis already replicates: the spin is a reward for a well-timed bolt.
//
// RangedAttack priority, above her thorn cast, and the two never overlap: this waits while the cast
// is busy, and claims the frame for the whole spin so the cast does not start under it.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay.SpriteHamlet;
using SpaceGame.Gameplay.Status;
using SpaceGame.Presentation;

namespace SpaceGame.Agents
{
    public class PoisonSpiralModule : BehaviourModuleBase
    {
        [Header("Engagement")]
        [Tooltip("A target further than this is not worth a spin, metres.")]
        [SerializeField, Min(1f)] private float range = 20f;

        [Tooltip("Seconds from the START of one spin to the earliest start of the next.")]
        [SerializeField, Min(0.1f)] private float cooldownSeconds = 9f;

        [Tooltip("Seconds after she first has a target before the first spin, so the fight opens " +
                 "with her vanishing rather than with a wall of poison.")]
        [SerializeField, Min(0f)] private float openingDelay = 4f;

        [Tooltip("Her other attack. A spin waits until it is not busy.")]
        [SerializeField] private ConjurerCastModule otherAttack;

        [Header("Spin")]
        [Tooltip("Seconds she stands with her arms out before the first ball — the warning.")]
        [SerializeField, Min(0f)] private float windUpSeconds = 0.9f;

        [Tooltip("Seconds she spins and throws.")]
        [SerializeField, Min(0.1f)] private float spinSeconds = 4f;

        [Tooltip("Whole turns per second while she spins.")]
        [SerializeField, Min(0.05f)] private float turnsPerSecond = 0.45f;

        [Tooltip("Balls thrown per second, per arm.")]
        [SerializeField, Min(0.1f)] private float throwsPerSecond = 5f;

        [Tooltip("How many streams leave her at once, evenly around her. Two makes a double spiral.")]
        [SerializeField, Range(1, 6)] private int arms = 2;

        [Tooltip("Seconds she staggers dizzy after the spin before she moves again.")]
        [SerializeField, Min(0f)] private float recoverSeconds = 0.8f;

        [Header("Balls")]
        [SerializeField] private PoisonBall ballPrefab;

        [Tooltip("How far out from her a ball is born, metres, so it does not start inside her.")]
        [SerializeField, Min(0f)] private float throwReach = 0.7f;

        [Tooltip("Height above her feet a ball is born; it then settles to its own float height.")]
        [SerializeField, Min(0f)] private float throwHeight = 0.9f;

        [Header("Presentation")]
        [Tooltip("The model she spins. Turned on every machine; her root never turns, so the motor " +
                 "and the NetworkTransform are untouched.")]
        [SerializeField] private Transform spinPivot;

        [Tooltip("Played as the arms go out. Optional.")]
        [SerializeField] private CharacterAction windUpAction;

        [Tooltip("Held for the spin. Optional.")]
        [SerializeField] private CharacterAction spinAction;

        [Tooltip("The swirl of poison around her while she spins. Optional.")]
        [SerializeField] private ParticleSystem swirl;

        [Tooltip("Seconds the model takes to turn back to her body's heading after the spin.")]
        [SerializeField, Min(0.01f)] private float unwindSeconds = 0.3f;

        private static readonly List<PoisonSpiralModule> Presenting = new();

        private CharacterActions actions;
        private StatusReceiver receiver;

        // ---- the deciding half, on the simulating machine.
        private bool attacking;
        private float attackElapsed;
        private float cooldownRemaining;
        private float targetSeenFor;

        // ---- the presenting half, on every machine.
        private bool presenting;
        private float presentElapsed;
        private float startYaw;
        private int turn;
        private Vector3 origin;
        private int thrown;
        private float unwind;
        private Quaternion unwindFrom;

        private float AttackSeconds => windUpSeconds + spinSeconds + recoverSeconds;

        private int TotalThrows => Mathf.FloorToInt(spinSeconds * throwsPerSecond) + 1;

        /// <summary>Every machine: the spin is being drawn here, from the arms going out to the dizzy end.</summary>
        public bool IsPresenting => presenting;

        /// <summary>
        /// Is a witch within <paramref name="radius"/> of <paramref name="position"/> spinning right
        /// now? Every machine — the keepers duck when one is.
        /// </summary>
        public static bool SpinningNear(Vector3 position, float radius)
        {
            float reach = radius * radius;
            foreach (PoisonSpiralModule spinner in Presenting)
                if (spinner != null && (spinner.origin - position).sqrMagnitude <= reach)
                    return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Presenting.Clear();

        private void Reset() => SetPriorityDefault(ModulePriority.RangedAttack + 1);

        private void Awake()
        {
            actions = GetComponentInChildren<CharacterActions>(true);
            if (spinPivot == null)
            {
                Animator animator = GetComponentInChildren<Animator>(true);
                spinPivot = animator != null ? animator.transform : null;
            }
        }

        private void OnEnable()
        {
            receiver = StatusReceiver.Ensure(gameObject);
            this.NetOn(NetMsg.WitchSpiral, OnSpiralElsewhere);
            attacking = false;
            cooldownRemaining = 0f;
            targetSeenFor = 0f;
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.WitchSpiral, OnSpiralElsewhere);
            EndPresentation(interrupted: true);
            if (spinPivot != null) spinPivot.localRotation = Quaternion.identity;
        }

        public override string ModuleDescription =>
            "Stops, throws her arms out for windUpSeconds, then spins for spinSeconds flinging " +
            "PoisonBalls the way she faces — a spiral to walk through. Waits while otherAttack is " +
            "busy; a stasis ends the spin on every machine.\n\n" +
            "• range / cooldownSeconds / openingDelay\n" +
            "• turnsPerSecond / throwsPerSecond / arms\n" +
            "• spinPivot — the model turned for the spin";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            if (!Network.Simulates(this)) return null;

            if (cooldownRemaining > 0f) cooldownRemaining -= deltaTime;

            if (attacking)
            {
                attackElapsed += deltaTime;
                if (attackElapsed < AttackSeconds) return MoveIntent.Idle();

                attacking = false;
                return null;
            }

            AgentTargeting targeting = context.Targeting;
            if (targeting == null || !targeting.HasTarget)
            {
                targetSeenFor = 0f;
                return null;
            }

            targetSeenFor += deltaTime;
            if (targetSeenFor < openingDelay || cooldownRemaining > 0f) return null;
            if (otherAttack != null && otherAttack.IsBusy) return null;
            if (targeting.DistanceToTarget > range) return null;

            Begin(context.Position, targeting.Target.position);
            return MoveIntent.Idle();
        }

        private void Begin(Vector3 at, Vector3 toward)
        {
            attacking = true;
            attackElapsed = 0f;
            cooldownRemaining = cooldownSeconds;

            Vector3 facing = toward - at;
            float yaw = facing.sqrMagnitude > 0.01f ? Quaternion.LookRotation(facing).eulerAngles.y : transform.eulerAngles.y;
            int direction = Random.value < 0.5f ? -1 : 1;

            // Drawn by the broadcast alone, on every machine including this one — NetToAll always
            // dispatches locally too.
            this.NetToAll(NetMsg.WitchSpiral, new NetArg { P = at, A = Mathf.RoundToInt(yaw * 100f), B = direction });
        }

        private void OnSpiralElsewhere(in NetArg arg, ulong sender) => BeginPresentation(arg.P, arg.A / 100f, arg.B >= 0 ? 1 : -1);

        /// <summary>The spin, on every machine. Idempotent against the host's own echo.</summary>
        private void BeginPresentation(Vector3 at, float yaw, int direction)
        {
            if (presenting) return;

            presenting = true;
            presentElapsed = 0f;
            origin = at;
            startYaw = yaw;
            turn = direction;
            thrown = 0;
            unwind = 0f;
            Presenting.Add(this);

            if (actions != null && windUpAction != null) actions.Play(windUpAction);
        }

        /// <summary>Every machine: stop a spin where it is — the fight is resetting or ending.</summary>
        public void Interrupt() => EndPresentation(interrupted: true);

        private void EndPresentation(bool interrupted)
        {
            if (!presenting) return;

            presenting = false;
            Presenting.Remove(this);
            if (swirl != null) swirl.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (actions != null && spinAction != null) actions.Stop(spinAction);

            if (spinPivot != null)
            {
                unwind = unwindSeconds;
                unwindFrom = spinPivot.localRotation;
            }

            // A spin ended early (a stasis) frees the deciding half too, so the brain is not left
            // waiting out a spin nobody is drawing when the freeze lets go.
            if (interrupted) attacking = false;
        }

        private void Update()
        {
            if (!presenting)
            {
                Unwind();
                return;
            }

            if (receiver != null && receiver.Has(StatusKind.Stasis))
            {
                EndPresentation(interrupted: true);
                return;
            }

            presentElapsed += Time.deltaTime;
            float spinT = presentElapsed - windUpSeconds;

            if (spinT >= 0f && spinT <= spinSeconds)
            {
                if (thrown == 0)
                {
                    if (swirl != null) swirl.Play(true);
                    if (actions != null && spinAction != null) actions.Play(spinAction);
                }

                if (spinPivot != null) spinPivot.rotation = Quaternion.Euler(0f, YawAt(spinT), 0f);
                ThrowDue(spinT);
            }
            else if (spinT > spinSeconds)
            {
                // The last stream, then she unwinds while the deciding half waits out her dizziness.
                ThrowDue(spinSeconds);
                EndPresentation(interrupted: false);
            }
        }

        private float YawAt(float spinT) => startYaw + turn * 360f * turnsPerSecond * spinT;

        /// <summary>
        /// Every ball whose moment has come, each from its OWN moment's heading rather than the
        /// frame's, so a slow frame does not bunch a stream up and every machine throws the same
        /// spiral.
        /// </summary>
        private void ThrowDue(float spinT)
        {
            if (ballPrefab == null) return;

            bool cosmetic = !Network.Simulates(this);
            int due = Mathf.Min(TotalThrows, Mathf.FloorToInt(spinT * throwsPerSecond) + 1);

            for (; thrown < due; thrown++)
            {
                float yaw = YawAt(thrown / throwsPerSecond);
                for (int arm = 0; arm < arms; arm++)
                {
                    Quaternion heading = Quaternion.Euler(0f, yaw + arm * 360f / arms, 0f);
                    Vector3 from = origin + heading * Vector3.forward * throwReach + Vector3.up * throwHeight;
                    PoisonBall ball = Instantiate(ballPrefab, from, heading);
                    ball.Launch(transform, cosmetic);
                }
            }
        }

        private void Unwind()
        {
            if (unwind <= 0f || spinPivot == null) return;

            unwind = Mathf.Max(0f, unwind - Time.deltaTime);
            spinPivot.localRotation = Quaternion.Slerp(Quaternion.identity, unwindFrom, unwind / unwindSeconds);
        }

        protected override void OnValidate()
        {
            SetMinPriority(ModulePriority.RangedAttack + 1);
        }
    }
}
