// Says the thing the NPC is doing, unprompted, when the player is close enough to hear it.
//
// This is the cheapest personality in the whole system and the one that does the most work. An NPC
// with a task is already crossing the map on its own business; without this, none of that is
// visible to a player who has not stood and watched it for a minute. One line as they pass — "this
// well's been dry since the storm" — is the difference between an NPC that HAS a task and an NPC
// that reads as having one.
//
// Side-effect module: it never returns a MoveIntent and cannot interfere with movement.
using FMODUnity;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Presentation;

namespace SpaceGame.Agents
{
    // IPresentationModule: this keeps ticking on machines that only watch the NPC. It has to. The
    // line is shown in a screen-space popup on THIS machine, to THIS machine's player, chosen by
    // how close THEY are standing — none of which the server can answer on their behalf. Running it
    // only where the NPC is simulated would mean the host hears the camp talking and nobody else
    // ever does. It qualifies because it writes nothing anyone else can observe: a local popup and
    // a local sound, and a static cooldown that is per-machine by nature.
    public class ChatterModule : BehaviourModuleBase, IPresentationModule
    {
        [Header("Audience")]
        [Tooltip("How close a player must be before this NPC says anything. The popup is a " +
                 "screen-space singleton with no position of its own, so this radius is the only " +
                 "thing tying a line to the person who said it — keep it short.")]
        [SerializeField] private float hearingRadius = 12f;

        [Tooltip("Require line of sight to the player. Off by default: a voice from behind a rock " +
                 "you are walking past is fine, and the raycast is not free.")]
        [SerializeField] private bool requireLineOfSight = false;

        [SerializeField] private LayerMask lineOfSightBlockers = ~0;

        [Header("Timing")]
        [Tooltip("Seconds between lines from this NPC (min, max).")]
        [SerializeField] private Vector2 interval = new Vector2(20f, 50f);

        [Tooltip("Seconds after ANY NPC speaks before another may. Shared across every NPC in the " +
                 "game, because they all write to the same popup — without it, walking into a camp " +
                 "of six produces six lines fighting over one text box.")]
        [SerializeField] private float globalCooldown = 6f;

        [SerializeField] private float popupDuration = 2.6f;

        [Header("Lines")]
        [Tooltip("Use the current NpcTaskModule task's chatter array when there is one. This is " +
                 "what makes an NPC talk about what it is actually doing rather than at random.")]
        [SerializeField] private bool useTaskChatter = true;

        [TextArea(1, 3)]
        [Tooltip("Fallback lines, used when there is no task or the task has no chatter of its own.")]
        [SerializeField] private string[] idleChatter;

        [Header("Manners")]
        [Tooltip("Say nothing while this NPC has a target — someone shooting at you does not " +
                 "remark on the weather.")]
        [SerializeField] private bool silentWhileFighting = true;

        [Header("Voice")]
        [SerializeField] private SfxId voiceId = SfxId.NpcMumbleNeutral;
        [SerializeField] private EventReference voiceSound;

        // Shared across every ChatterModule. Static state survives leaving play mode with domain
        // reload off, so it is reset at subsystem registration like every other static here.
        private static float nextGlobalSpeakTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => nextGlobalSpeakTime = 0f;

        private NpcTaskModule taskModule;
        private PlayerController cachedPlayer;
        private float playerRefreshTimer;
        private float speakTimer;

        public override bool ClaimsMovement => false;

        private void Reset() => SetPriorityDefault(ModulePriority.Personality);

        private void Awake() => taskModule = GetComponent<NpcTaskModule>();

        private void OnEnable() => speakTimer = RollInterval();

        public override string ModuleDescription =>
            "Speaks a line about the current task when a player is within hearing range.\n\n" +
            "• Pulls lines from NpcTaskModule's current task, so the NPC talks about what it is doing\n" +
            "• idleChatter — fallback lines when there is no task\n" +
            "• hearingRadius — how close the player must be\n" +
            "• globalCooldown — shared across ALL NPCs, since they share one popup\n" +
            "• silentWhileFighting — no small talk mid-combat";

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            speakTimer -= deltaTime;
            if (speakTimer > 0f) return null;

            // Re-roll regardless of whether the line actually goes out. Otherwise an NPC that spent
            // ten minutes alone fires the instant a player crosses the radius, and every NPC in a
            // camp does it at once.
            speakTimer = RollInterval();

            if (!CanSpeakNow(in context)) return null;

            string line = PickLine();
            if (string.IsNullOrWhiteSpace(line)) return null;

            Speak(line);
            return null;
        }

        private bool CanSpeakNow(in AgentContext context)
        {
            if (Time.time < nextGlobalSpeakTime) return false;

            if (silentWhileFighting && context.Targeting != null && context.Targeting.HasTarget)
                return false;

            // Never talk over a conversation the player is actually having. The popup is one object
            // and a chatter line would replace a dialog line mid-sentence.
            NpcDialogPopupUI popup = NpcDialogPopupUI.Instance;
            if (popup == null || popup.IsVisible || popup.IsQuestionActive) return false;

            Transform listener = ResolveListener();
            if (listener == null) return false;

            Vector3 self = transform.position;
            if ((listener.position - self).sqrMagnitude > hearingRadius * hearingRadius)
                return false;

            if (!requireLineOfSight) return true;

            Vector3 from = self + Vector3.up * 1.5f;
            Vector3 to = listener.position + Vector3.up * 1.2f;
            return !Physics.Linecast(from, to, lineOfSightBlockers, QueryTriggerInteraction.Ignore);
        }

        private Transform ResolveListener()
        {
            playerRefreshTimer -= Time.deltaTime;

            if (cachedPlayer == null || playerRefreshTimer <= 0f)
            {
                cachedPlayer = GameplayMenuScope.FindLocalPlayer();
                playerRefreshTimer = 3f;
            }

            return cachedPlayer != null ? cachedPlayer.transform : null;
        }

        private string PickLine()
        {
            if (useTaskChatter && taskModule != null)
            {
                string fromTask = taskModule.CurrentTask?.RandomChatter();
                if (!string.IsNullOrWhiteSpace(fromTask))
                    return Resolve(fromTask);
            }

            if (idleChatter == null || idleChatter.Length == 0) return null;

            return Resolve(idleChatter[UnityEngine.Random.Range(0, idleChatter.Length)]);
        }

        /// <summary>
        /// Fills in the same tokens DialogInteraction understands, so a line can be moved between a
        /// chatter array and a dialog array without being rewritten.
        /// </summary>
        private string Resolve(string line) => NpcSpeechTokens.Resolve(line, taskModule);

        private void Speak(string line)
        {
            nextGlobalSpeakTime = Time.time + Mathf.Max(0f, globalCooldown);

            NpcDialogPopupUI.Instance.Show(line, popupDuration);

            // At this transform, not through the popup: the popup is screen-space and has no
            // position, so a mumble emitted there would come from nowhere and would not fall off as
            // the player walks away — the same reason DialogInteraction.SpeakLine does it here.
            Sfx.Play(voiceId, transform.position, voiceSound, GetInstanceID());
        }

        private float RollInterval() =>
            UnityEngine.Random.Range(Mathf.Min(interval.x, interval.y), Mathf.Max(interval.x, interval.y));

        protected override void OnValidate()
        {
            hearingRadius = Mathf.Max(1f, hearingRadius);
            interval.x = Mathf.Max(1f, interval.x);
            interval.y = Mathf.Max(interval.x, interval.y);
            globalCooldown = Mathf.Max(0f, globalCooldown);
            popupDuration = Mathf.Max(0.5f, popupDuration);
        }
    }
}
