// A Shellback of the workshop's story: the one who got out (WorkshopGatekeeper) or one of the family shut inside
// (WorkshopFamilyMember). Not a settlement resident: the mission spawns them, saves none of them, and rebuilds them
// where its state has them.
//
// The server places them (Stand, PutAt); what they SAY is decided on each machine from the mission's replicated state,
// the way DialogInteraction's lines are. A talk press is answered on the presser's machine; lines a Shellback speaks
// on its own are queued on every machine and shown in the dialog popup to the players close enough to hear them.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;
using SpaceGame.Presentation.Speech;

namespace SpaceGame.World
{
    public abstract class WorkshopShellback : MonoBehaviour, IDialogResponder
    {
        [Header("Standing")]
        [Tooltip("How close to a spot counts as there, in metres.")]
        [SerializeField, Min(0.2f)] private float arriveRadius = 1.2f;

        [Tooltip("How close a player must come for a standing Shellback to turn to them, in metres.")]
        [SerializeField, Min(1f)] private float greetRadius = 14f;

        [Tooltip("Seconds a Shellback keeps facing a player it has turned to, renewed while they stay.")]
        [SerializeField, Min(0.5f)] private float focusSeconds = 4f;

        [Header("Speaking")]
        [Tooltip("How close a player must be to hear what a Shellback says on its own, in metres.")]
        [SerializeField, Min(1f)] private float hearingRadius = 30f;

        [Tooltip("Shortest time a line it says on its own stays up, seconds.")]
        [SerializeField, Min(0.5f)] private float secondsPerLine = 4f;

        private const string StoryReason = "workshop story";

        private readonly Queue<string> pending = new();

        private AgentGoal goal;
        private InteractionFocusModule focus;
        private HealthComponent health;
        private WorkshopMission mission;
        private int lineTurn;
        private WorkshopMissionPhase turnPhase;

        public bool IsAlive => health == null || health.Alive;

        /// <summary>Standing at the spot it was last sent to.</summary>
        public bool HasArrived => goal != null && goal.HasArrived;

        protected AgentGoal Goal => goal;
        protected float ArriveRadius => arriveRadius;
        protected float GreetRadius => greetRadius;

        /// <summary>The mission it belongs to on this machine, found once it is loaded.</summary>
        protected WorkshopMission Mission
        {
            get
            {
                if (mission != null) return mission;
                mission = WorkshopMission.Nearest(transform.position);
                if (mission != null) mission.Changed += OnMissionChanged;
                return mission;
            }
        }

        protected virtual void Awake()
        {
            goal = GetComponent<AgentGoal>();
            focus = GetComponentInChildren<InteractionFocusModule>(true);
            health = GetComponent<HealthComponent>();

            // The story's hold and the story's lines: idle glances and small talk do not belong to people whose home
            // was just robbed.
            ScriptedStand.QuietStockModules(gameObject);
        }

        protected virtual void OnDisable()
        {
            if (mission != null) mission.Changed -= OnMissionChanged;
            mission = null;
            pending.Clear();
        }

        // ── Server: where it is ──────────────────────────────────────────────

        /// <summary>
        /// Server, every think: to <paramref name="spot"/>, facing the way it faces, and turned to <paramref name="greet"/>
        /// once there and near. <paramref name="run"/> runs there rather than walking.
        /// </summary>
        public void Stand(Pose spot, Transform greet, bool run = false)
        {
            if (!IsAlive || goal == null) return;
            ScriptedStand.Stand(goal, spot, arriveRadius, StoryReason, run);
            if (HasArrived) FaceIfNear(greet);
        }

        /// <summary>Server: straight to <paramref name="spot"/>, where no walk leads or the story moved on in a reload.</summary>
        public void PutAt(Pose spot) => ScriptedStand.PutAt(gameObject, goal, spot, arriveRadius, StoryReason);

        /// <summary>Server: turn to <paramref name="player"/> if they are within <see cref="GreetRadius"/>. True when it did.</summary>
        protected bool FaceIfNear(Transform player)
        {
            if (player == null || focus == null || !Within(player, greetRadius)) return false;
            focus.FocusOn(player, focusSeconds);
            return true;
        }

        // ── Every machine: what it says ──────────────────────────────────────

        public bool CanRespond(Transform player) => IsAlive && Mission != null;

        public void Respond(Transform player)
        {
            WorkshopMission at = Mission;
            if (at == null) return;

            // Talking to one that is telling its story moves the story on, rather than starting another line.
            string line = pending.Count > 0 ? pending.Dequeue() : NextLine(at);
            Speaker.Of(transform).Say(line, SpeechChannel.Dialog, showPopup: true);
            OnTalkedTo(at);
        }

        /// <summary>What it says to a player who talks to it now. <see cref="Turn"/> rotates through a set.</summary>
        protected abstract string NextLine(WorkshopMission at);

        /// <summary>After a talk press was answered, on the presser's machine.</summary>
        protected virtual void OnTalkedTo(WorkshopMission at) { }

        /// <summary>The mission's state changed on this machine. True when it just happened in play.</summary>
        protected virtual void OnMissionChanged(bool live) { }

        /// <summary>The next of <paramref name="lines"/> in turn; the turn starts again when the story moves on.</summary>
        protected string Turn(WorkshopMission at, string[] lines)
        {
            if (at.Phase != turnPhase)
            {
                turnPhase = at.Phase;
                lineTurn = 0;
            }
            return lines.Length == 0 ? string.Empty : lines[lineTurn++ % lines.Length];
        }

        /// <summary>Say <paramref name="lines"/> one after another, unasked, to whoever is close enough to hear.</summary>
        protected void Speak(IEnumerable<string> lines)
        {
            pending.Clear();
            foreach (string line in lines)
                if (!string.IsNullOrWhiteSpace(line)) pending.Enqueue(line);
        }

        protected virtual void Update()
        {
            if (pending.Count == 0 || !IsAlive) return;

            Speaker speaker = Speaker.Of(transform);
            if (speaker.IsShowing) return;

            Transform local = GameplayMenuScope.LocalPlayerTransform;
            speaker.Say(pending.Dequeue(), SpeechChannel.Dialog, local != null && Within(local, hearingRadius), secondsPerLine);
        }

        protected bool Within(Transform player, float radius) =>
            (player.position - transform.position).sqrMagnitude <= radius * radius;
    }
}
