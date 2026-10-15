// A resident's mouth. The server decides what is said — an answer when a player talks to it, a warning
// naming the cause when its temper climbs a band, a remark or greeting when it notices something — and
// broadcasts only a line id. Every machine, host included, looks the same row up, resolves the same
// tokens and presents it: speech is public and costs a dozen bytes. The ONLY sender of ResidentSaid.
//
// Persistence: none of its own. What a resident remembers about a player lives in ResidentMemory
// (ResidentSaveable); the recent-line ring and the greeting set are minutes long and start empty.
using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Presentation.Speech;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    [RequireComponent(typeof(Resident))]
    public sealed class ResidentVoice : MonoBehaviour, IDialogResponder
    {
        [Tooltip("How close a player must stand for the server to accept that they addressed this resident (m).")]
        [SerializeField, Min(0f)] private float addressRange = 6f;
        [Tooltip("Seconds the resident keeps facing its listener after its answer finishes.")]
        [SerializeField, Min(0f)] private float focusAfterLine = 2f;
        [Tooltip("Lines remembered: not repeated while anything else fits, and listed in the inspector.")]
        [SerializeField, Min(1)] private int recentLines = 10;
        [Tooltip("Topics a returning player is answered with, in turn. A first meeting is always a Greeting.")]
        [SerializeField] private Topic[] conversation = { Topic.Work, Topic.Gossip, Topic.Need, Topic.Ambition };

        private Resident resident;
        private ResidentAwareness awareness;
        private ProvocationModule provocation;
        private AgentTargeting targeting;
        private readonly List<uint> recentIds = new();
        private readonly List<string> lastLines = new();
        private readonly HashSet<ulong> greeted = new();
        private int linesSaid;
        private int conversationTurn;
        private float nextRemarkAt;

        public LineRow LastLine { get; private set; }
        public int LastScore { get; private set; }
        public IReadOnlyList<string> LastLines => lastLines;
        public float LastLineEndsAt { get; private set; }
        public Register? LastRegister => LastLine?.register;

        /// <summary>Lines said since this resident woke; compare across a Say to learn whether a line fitted.</summary>
        public int LinesSaid => linesSaid;

        private void Awake()
        {
            resident = GetComponent<Resident>();
            awareness = GetComponent<ResidentAwareness>();
            provocation = GetComponent<ProvocationModule>();
            targeting = GetComponent<AgentTargeting>();
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.ResidentAddressed, OnAddressed);
            this.NetOn(NetMsg.ResidentSaid, OnSaid);
            if (provocation != null) provocation.BandChanged += OnBandChanged;
            if (awareness != null) awareness.Observed += OnObserved;
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.ResidentAddressed, OnAddressed);
            this.NetOff(NetMsg.ResidentSaid, OnSaid);
            if (provocation != null) provocation.BandChanged -= OnBandChanged;
            if (awareness != null) awareness.Observed -= OnObserved;
        }

        public bool CanRespond(Transform player) =>
            resident.Society != null && !resident.IsDead && (resident.Presence == null || !resident.Presence.Hidden);

        public void Respond(Transform player)
        {
            GameObject body = BodyOf(player);
            if (body != null) this.NetToServer(NetMsg.ResidentAddressed, new NetArg().With(body));
        }

        private void OnAddressed(in NetArg arg, ulong sender)
        {
            SettlementSociety society = resident.Society;
            GameObject body = arg.Resolve();
            if (!Network.Decides || society == null || body == null || !Network.MayActFor(body, sender)) return;
            Transform player = body.transform;
            if ((player.position - transform.position).sqrMagnitude > addressRange * addressRange) return;

            // Re-checked here rather than trusted: the asking machine's prompt can be a frame stale.
            Stance stance = StanceToward(player);
            if ((provocation != null && provocation.Band >= AggressionBand.Drawn) || (targeting != null && targeting.IsFightingWith(player)))
            {
                Speak(Topic.Refusal, player, stance, Observation.None, null, null, addressed: true);
                return;
            }

            string profile = ResidentMemory.ProfileOf(player);
            int day = society.Day;
            Observation seen = PlayerRead.Of(player, transform).Observe(profile != null && resident.Memory.KinHarmedBy(profile, day));
            if (!Answer(player, stance, profile != null && resident.Memory.Met(profile), seen)) return;

            if (resident.Focus != null) resident.Focus.FocusOn(player, LastLineEndsAt - Time.time + focusAfterLine);
            if (profile != null) resident.Memory.Talked(profile, day, resident.FamiliarityGain, ResidentTuning.Instance.dailyTalkCap);
        }

        private bool Answer(Transform player, Stance stance, bool met, Observation seen)
        {
            for (int i = 0; met && i < conversation.Length; i++)
            {
                if (!Speak(conversation[(conversationTurn + i) % conversation.Length], player, stance, seen, null, null, addressed: true))
                    continue;
                conversationTurn += i + 1;
                return true;
            }
            return Speak(Topic.Greeting, player, stance, seen, null, null, addressed: true);
        }

        private void OnBandChanged(AggressionBand from, AggressionBand to)
        {
            if (!Network.Decides || to <= from) return;
            Say(to == AggressionBand.Grudge ? Topic.Alarm : Topic.Warning, provocation.LastCauseFrom, CauseOf(provocation.LastCause));
        }

        private static Observation CauseOf(AggressionInput cause) => cause switch
        {
            AggressionInput.Hit => Observation.Hitting,
            AggressionInput.AllyHurt => Observation.KinHarmed,
            AggressionInput.Menace => Observation.Menacing,
            AggressionInput.Gunshot => Observation.ArmedHeld,
            AggressionInput.Jostle => Observation.Jostling,
            _ => Observation.Approaching,
        };

        private void OnObserved(Transform player, Observation seen, Stance stance)
        {
            SettlementSociety society = resident.Society;
            if (!Network.Decides || society == null || player == null || seen == Observation.None) return;

            // Consequence lane: never throttled, but never over the resident's own unfinished line.
            if (seen is Observation.KinHarmed or Observation.Hitting or Observation.Menacing)
            {
                if (Time.time >= LastLineEndsAt) Speak(Topic.Warning, player, stance, seen, null, null, addressed: false);
                return;
            }

            // Ambient lane: one greeting per player per visit, then remarks under both throttles.
            ResidentTuning tuning = ResidentTuning.Instance;
            ulong playerId = NetArg.IdOf(player.gameObject);
            bool greeting = seen == Observation.Approaching && !greeted.Contains(playerId);
            if (Time.time < nextRemarkAt || !society.TryRemark(playerId, Time.time, tuning.remarkGapPerPlayer)) return;
            if (!Speak(greeting ? Topic.Greeting : Topic.Remark, player, stance, seen, null, null, addressed: false)) return;

            nextRemarkAt = Time.time + tuning.remarkGapPerResident;
            if (greeting) greeted.Add(playerId);
        }

        /// <summary>Server: say the best line for <paramref name="topic"/>, publicly. <paramref name="addressee"/> is who the
        /// line is about (the stance is taken toward them), not who gets a popup — only answers to being talked to do.</summary>
        public void Say(Topic topic, Transform addressee, Observation obs, Register? repliesTo = null, Resident subject = null) =>
            Speak(topic, addressee, StanceToward(addressee), obs, repliesTo, subject, addressed: false);

        private bool Speak(Topic topic, Transform toward, Stance stance, Observation seen, Register? repliesTo, Resident subject, bool addressed)
        {
            LineTable table = resident.Society != null ? resident.Society.Lines : null;
            if (!Network.Decides || table == null) return false;

            var query = new LineQuery
            {
                person = resident.displayName,
                archetype = resident.archetype != null ? resident.archetype.name : null,
                topic = topic,
                stance = stance,
                observation = seen,
                activity = resident.Presence != null ? resident.Presence.Activity : Activity.None,
                repliesTo = repliesTo,
                seed = resident.seed + linesSaid,
            };
            LineRow row = LineMatcher.Pick(table, in query, recentIds, out int score);
            if (row == null) return false;

            linesSaid++;
            string text = SpeechTokens.Resolve(row.text, resident, subject);
            LastLine = row;
            LastScore = score;
            LastLineEndsAt = Time.time + Typewriter.DurationFor(text);
            Push(recentIds, row.id);
            Push(lastLines, text);

            var arg = new NetArg(a: unchecked((int)row.id), b: subject != null ? subject.index : -1);
            this.NetToAll(NetMsg.ResidentSaid, addressed ? arg.With(BodyOf(toward)) : arg);
            return true;
        }

        private void OnSaid(in NetArg arg, ulong sender)
        {
            // Only the server speaks for a resident; the same id sent to the server by a client is ignored.
            SettlementSociety society = resident.Society;
            if (sender != NetworkManager.ServerClientId || society == null || society.Lines == null) return;
            if (!society.Lines.TryGet(unchecked((uint)arg.A), out LineRow row)) return;

            IReadOnlyList<Resident> roster = society.Residents;
            Resident subject = arg.B >= 0 && arg.B < roster.Count ? roster[arg.B] : null;
            GameObject addressee = arg.Resolve();
            bool toLocalPlayer = addressee != null && Network.Owns(addressee.transform);
            Speaker.Of(transform).Say(SpeechTokens.Resolve(row.text, resident, subject), ChannelOf(row, addressee != null), toLocalPlayer);
        }

        /// <summary>Toward a player, what awareness reads now; toward no one (resident talk), the temperament.</summary>
        private Stance StanceToward(Transform toward) =>
            toward != null && awareness != null ? awareness.StanceToward(toward) : resident.OpeningStance;

        private static SpeechChannel ChannelOf(LineRow row, bool addressed)
        {
            if (row.topic is Topic.Warning or Topic.Alarm or Topic.Refusal) return SpeechChannel.Warning;
            if (addressed) return SpeechChannel.Reply;
            return row.topic is Topic.Remark or Topic.Greeting or Topic.Farewell ? SpeechChannel.Ambient : SpeechChannel.Talk;
        }

        private void Push<T>(List<T> ring, T item)
        {
            ring.Add(item);
            if (ring.Count > recentLines) ring.RemoveAt(0);
        }

        /// <summary>The player's networked body: what a message can name and every machine can resolve.</summary>
        private static GameObject BodyOf(Transform player)
        {
            if (player == null) return null;
            NetworkObject netObj = player.GetComponentInParent<NetworkObject>();
            return netObj != null ? netObj.gameObject : player.gameObject;
        }
    }
}
