// The wrangler who works a ranch, on his NPC prefab. Not a settlement resident: the ranch spawns him, saves nothing about
// him, and puts him back where its quest has him whenever its chunk loads (Ranch).
//
// The server places him (Stand, Wait, PutAt); what he says is worked out on each machine. Talked to before anybody has
// the ranch's quest, he offers it and gives it to the crew (ObjectiveDirector.Give is safe on any machine); during the
// quest he answers with the current step's hints; after it, with his thanks. The quest's own briefings are his too —
// a QuestVoice on the same prefab has ObjectiveBriefing say them through him.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Presentation;
using SpaceGame.Presentation.Speech;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AgentGoal))]
    public sealed class RanchWrangler : MonoBehaviour, IDialogResponder
    {
        private const string StoryReason = "working the ranch";

        [Tooltip("The ranch he works.")]
        [SerializeField] private RanchSite site;

        [Header("Standing")]
        [Tooltip("How close to a spot counts as there, metres.")]
        [SerializeField, Min(0.2f)] private float arriveRadius = 1.5f;

        [Tooltip("How close a player must come for him to turn to them, metres.")]
        [SerializeField, Min(1f)] private float greetRadius = 14f;

        [Tooltip("Seconds he keeps facing a player he has turned to, renewed while they stay.")]
        [SerializeField, Min(0.5f)] private float focusSeconds = 4f;

        [Header("Talk")]
        [Tooltip("Said once, unasked, the first time this machine's player comes within the call radius before the " +
                 "crew have the quest — so a player walking past learns there is somebody to talk to.")]
        [SerializeField, TextArea(1, 3)] private string callOut = "Hey, spacer! Ever sat an esko? Come talk to me.";

        [SerializeField, Min(1f)] private float callRadius = 14f;

        [Tooltip("Said when talked to before the crew have the quest; then the quest is given and its first briefing follows.")]
        [SerializeField, TextArea(1, 3)] private string offer =
            "Name's Tovak. I break eskos for the town, and I'm short a pair of hands. Come on, I'll show you.";

        [Tooltip("Said in turn when talked to after the quest is done.")]
        [SerializeField, TextArea(1, 3)] private string[] afterwards =
        {
            "Not bad for somebody who fell out of the sky.",
            "The esko's yours to ride whenever you like. Bring her back fed.",
        };

        private static readonly List<RanchWrangler> Live = new();

        private AgentGoal goal;
        private InteractionFocusModule focus;
        private HealthComponent health;
        private bool calledOut;
        private int lineTurn;
        private ObjectiveStep turnStep;

        public bool IsAlive => health == null || health.Alive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();

        /// <summary>EVERY machine: the living wrangler of <paramref name="ranchSite"/> spawned here, or null.</summary>
        public static RanchWrangler Of(RanchSite ranchSite)
        {
            foreach (RanchWrangler wrangler in Live)
                if (wrangler.site == ranchSite && wrangler.IsAlive) return wrangler;

            return null;
        }

        private void Awake()
        {
            goal = GetComponent<AgentGoal>();
            focus = GetComponentInChildren<InteractionFocusModule>(true);
            health = GetComponent<HealthComponent>();
            ScriptedStand.QuietStockModules(gameObject);
        }

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

        // ── SERVER: where he is ──────────────────────────────────────────────

        /// <summary>SERVER: to <paramref name="spot"/>, turned to <paramref name="greet"/> once there and near.</summary>
        public void Stand(Pose spot, Transform greet, bool run)
        {
            ScriptedStand.Stand(goal, spot, arriveRadius, StoryReason, run);
            if (goal.HasArrived) FaceIfNear(greet);
        }

        /// <summary>SERVER: stop where he is and look round for <paramref name="straggler"/>.</summary>
        public void Wait(Transform straggler)
        {
            goal.Set(transform.position, arriveRadius, StoryReason, hold: true,
                     facePoint: straggler != null ? straggler.position : null);
            FaceIfNear(straggler);
        }

        /// <summary>SERVER: straight to <paramref name="spot"/>, as when the ranch's chunk loads mid-quest.</summary>
        public void PutAt(Pose spot) => ScriptedStand.PutAt(gameObject, goal, spot, arriveRadius, StoryReason);

        /// <summary>Standing at <paramref name="point"/>, over the ground.</summary>
        public bool IsAt(Vector3 point)
        {
            Vector3 at = transform.position;
            float dx = point.x - at.x, dz = point.z - at.z;
            float reach = AgentGoal.ArrivalRadius(arriveRadius, hold: true);
            return dx * dx + dz * dz <= reach * reach;
        }

        private void FaceIfNear(Transform player)
        {
            if (player == null || focus == null || !Within(player, greetRadius)) return;
            focus.FocusOn(player, focusSeconds);
        }

        // ── EVERY machine: what he says ──────────────────────────────────────

        public bool CanRespond(Transform player) => IsAlive && Ranch.Of(site) != null && ObjectiveDirector.Instance != null;

        public void Respond(Transform player)
        {
            Ranch ranch = Ranch.Of(site);
            ObjectiveDirector director = ObjectiveDirector.Instance;
            if (ranch == null || director == null) return;

            Quest taken = director.Find(ranch.QuestChain);
            if (taken == null)
            {
                Say(offer);
                director.Give(ranch.QuestChain);
            }
            else if (taken.IsComplete)
            {
                Say(Turn(null, afterwards));
            }
            else
            {
                IReadOnlyList<string> hints = ranch.HintsFor(taken.Current);
                Say(hints.Count > 0 ? Turn(taken.Current, hints) : taken.Current.Title);
            }
        }

        private void Update()
        {
            if (calledOut || !IsAlive || string.IsNullOrWhiteSpace(callOut)) return;

            Transform local = GameplayMenuScope.LocalPlayerTransform;
            if (local == null || !Within(local, callRadius)) return;

            calledOut = true;
            Ranch ranch = Ranch.Of(site);
            ObjectiveDirector director = ObjectiveDirector.Instance;
            if (ranch != null && director != null && director.Find(ranch.QuestChain) == null) Say(callOut);
        }

        private void Say(string line)
        {
            if (!string.IsNullOrWhiteSpace(line)) Speaker.Of(transform).Say(line, SpeechChannel.Dialog, showPopup: true);
        }

        /// <summary>The next of <paramref name="lines"/> in turn; the turn starts again with every new step.</summary>
        private string Turn(ObjectiveStep step, IReadOnlyList<string> lines)
        {
            if (step != turnStep)
            {
                turnStep = step;
                lineTurn = 0;
            }
            return lines.Count == 0 ? string.Empty : lines[lineTurn++ % lines.Count];
        }

        private bool Within(Transform player, float radius) =>
            (player.position - transform.position).sqrMagnitude <= radius * radius;
    }
}
