// What a creature does about the condition it is under.
//
// It exists so that nothing that APPLIES a condition has to know an agent exists. A flamethrower
// sets a body burning and is finished; this module hears about it and decides that a creature runs.
// Without it the five artifacts that leave conditions behind would each grow their own opinion
// about AI, and the five opinions would disagree.
using System;
using SpaceGame.Gameplay.Status;
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>
    /// Turns a status on this creature into a flee, a stagger, or nothing.
    ///
    /// <para>
    /// <b>Helplessness is derived, every frame, and never written to the agent.</b> While a
    /// suppressing condition is running this module claims the frame with an idle intent, which
    /// starves every module below it — chase, melee, ranged, wander — because
    /// <c>AgentController</c> stops at the first module that returns one. Nothing is disabled and
    /// nothing is restored, so the frame the condition ends the creature simply moves again. The
    /// alternative, switching the brain off, is what a world save then captures: a creature that
    /// reloads frozen forever, with a clean console.
    /// </para>
    /// <para>
    /// Panic is the other half and it is an EVENT, not a per-frame claim: the creature is alarmed
    /// once when the condition starts and <c>FleeModule</c> owns the running from there — including
    /// where to run, how fast, and when it is far enough away to stop. Two modules writing a
    /// destination is a stall with nothing in the console, so this one writes none.
    /// </para>
    /// </summary>
    public class StatusReactionModule : BehaviourModuleBase
    {
        /// <summary>What a condition makes this creature do.</summary>
        public enum StatusResponse
        {
            /// <summary>Nothing. The condition is real but the creature has no answer to it.</summary>
            Ignore,

            /// <summary>Break off whatever it was doing and run — <c>FleeModule</c> decides where.</summary>
            Panic,

            /// <summary>Stand and do nothing at all until it wears off.</summary>
            Helpless,
        }

        [Serializable]
        public struct StatusReaction
        {
            public StatusKind Kind;
            public StatusResponse Response;
        }

        [Header("Reactions")]
        [Tooltip("What each condition makes this creature do. A kind with no row here does nothing " +
                 "— Slick and Inflated are absent on purpose: those change how a body moves, which " +
                 "the movers read for themselves, not what it decides to do.")]
        [SerializeField]
        private StatusReaction[] reactions =
        {
            new StatusReaction { Kind = StatusKind.Burning, Response = StatusResponse.Panic },
            new StatusReaction { Kind = StatusKind.Frozen, Response = StatusResponse.Helpless },
            new StatusReaction { Kind = StatusKind.Foamed, Response = StatusResponse.Helpless },
        };

        private StatusReceiver receiver;
        private FleeModule flee;

        public override string ModuleDescription =>
            "Reacts to the conditions on this creature's StatusReceiver: burning makes it panic, " +
            "frozen and foamed make it stand still.\n\n" +
            "• Panic — alarms FleeModule once when the condition starts; FleeModule owns the run\n" +
            "• Helpless — claims the frame with an idle intent for as long as the condition lasts, " +
            "starving every lower-priority module. Derived every frame, so nothing has to be " +
            "restored and a loaded creature is never left suppressed\n" +
            "• A creature with no FleeModule cannot panic, and simply keeps doing what it was doing";

        private void Reset() => SetPriorityDefault(ModulePriority.Scripted);

        private void Awake() => flee = GetComponent<FleeModule>();

        private void OnEnable()
        {
            // Ensured here rather than authored, and on EVERY machine rather than only the server:
            // a status arrives as a message on this body's relay, and a body with nothing
            // subscribed drops it silently. A creature that reacts to conditions is exactly a
            // creature that has to hear about them.
            receiver = StatusReceiver.Ensure(gameObject);
            if (receiver == null) return;

            receiver.StatusChanged += OnStatusChanged;

            // Read as it is now rather than waiting for the next change. This module is enabled
            // again after a load, a chunk reactivation and an authority handover, and a creature
            // that was already alight when that happened would otherwise stand there calmly.
            foreach (StatusReaction reaction in reactions)
                if (reaction.Response == StatusResponse.Panic && receiver.Has(reaction.Kind))
                    Alarm();
        }

        private void OnDisable()
        {
            if (receiver != null) receiver.StatusChanged -= OnStatusChanged;
        }

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            // Null is "pass", which lets everything below this module run as usual. That is the
            // answer on every ordinary frame, and it is why a module sitting at Scripted priority
            // is not a module that owns the creature.
            return IsHelpless ? MoveIntent.Idle() : (MoveIntent?)null;
        }

        /// <summary>
        /// Asked of the receiver every frame rather than latched, so the answer cannot outlive the
        /// condition that produced it. See the class comment.
        /// </summary>
        private bool IsHelpless
        {
            get
            {
                if (receiver == null) return false;

                foreach (StatusReaction reaction in reactions)
                    if (reaction.Response == StatusResponse.Helpless && receiver.Has(reaction.Kind))
                        return true;

                return false;
            }
        }

        private void OnStatusChanged(StatusKind kind, bool active)
        {
            if (!active) return;
            if (ResponseTo(kind) != StatusResponse.Panic) return;

            Alarm();
        }

        /// <summary>
        /// Frightened now, whatever the distance.
        ///
        /// <para>
        /// <c>FleeModule.Alarm</c> rather than a destination of this module's own: it already knows
        /// how to find somewhere on the NavMesh that is away from the threat, how fast to run there
        /// and when to stop, and a second opinion about any of those would be a second component
        /// writing the same path.
        /// </para>
        /// <para>
        /// It needs a threat to run from, which for a burning creature is whoever lit it — the
        /// condition bills its damage with the source attached, so the creature's own
        /// <c>ProvocationModule</c> has already named them by the time the first tick lands.
        /// </para>
        /// </summary>
        private void Alarm()
        {
            if (flee != null) flee.Alarm();
        }

        private StatusResponse ResponseTo(StatusKind kind)
        {
            foreach (StatusReaction reaction in reactions)
                if (reaction.Kind == kind) return reaction.Response;

            return StatusResponse.Ignore;
        }

        protected override void OnValidate()
        {
            // A creature that cannot act must not be out-voted by a chase module. The floor is
            // Override rather than Scripted so a cutscene can still take a frozen creature over.
            SetMinPriority(ModulePriority.Override);
        }
    }
}
