// Residents talking to each other, so a settlement with a player in it sounds lived in. Two residents
// holding at one place, or at two spots of one circle (the seats round a fire, a stall and its customers),
// face each other; one opens, and each reply is chosen only WHEN THE PREVIOUS LINE ENDS, against the
// replier's own facts and the register it is answering (a question gets an answer, a complaint gets
// sympathy or a shrug). No fitting reply, or maxTurns lines, ends it, and the focus lapses back into the
// hold. Nobody within earshot → nobody talks: lines are for players.
//
// Two residents on the move — a patrol pair, friends out for a walk, strangers who meet in the street — talk
// WITHOUT stopping: no focus is taken, so neither is turned to face the other and both keep walking. Such a
// talk ends when they drift apart, and the pair rests a shorter while before the next one.
//
// Server only, ticked by SettlementSociety. Persistence: none — a conversation is seconds long and simply
// does not resume.
using System.Collections.Generic;
using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    public sealed class Conversations
    {
        /// <summary>Openers in priority order, after a fresh act about a player.</summary>
        private static readonly Topic[] Openers = { Topic.Work, Topic.Gossip, Topic.Ambition };

        private sealed class Conversation
        {
            public Resident speaker, listener;
            public int turns;
            public bool walking;
        }

        private readonly SettlementSociety society;
        private readonly List<Conversation> talks = new();
        private readonly Dictionary<Resident, float> restingUntil = new();
        private float nextPairAt;

        public Conversations(SettlementSociety society) => this.society = society;

        /// <summary>Talks opened since the society was made, standing and walking — what the residents baseline counts.</summary>
        public int Opened { get; private set; }

        public void Tick()
        {
            for (int i = talks.Count - 1; i >= 0; i--)
                if (!Advance(talks[i])) End(i);

            if (Time.time < nextPairAt) return;
            nextPairAt = Time.time + ResidentTuning.Instance.pairInterval;
            PairUp();
            PairWalkers();
        }

        // Residents on an amble or patrol, close enough to talk, with a player in earshot of the first.
        private void PairWalkers()
        {
            ResidentTuning tuning = ResidentTuning.Instance;
            IReadOnlyList<Resident> roster = society.Residents;
            for (int a = 0; a < roster.Count; a++)
            {
                if (!FreeOnTheMove(roster[a])) continue;
                for (int b = a + 1; b < roster.Count; b++)
                {
                    if (!FreeOnTheMove(roster[b]) || FlatDistance(roster[a], roster[b]) > tuning.walkTalkRange) continue;
                    if (!ObserverCheck.AnyPlayerSees(roster[a].transform.position, tuning.earshot)) break;
                    Open(roster[a], roster[b], walking: true);
                    break;
                }
            }
        }

        private bool FreeOnTheMove(Resident r) =>
            Rested(r) && r.TryGetComponent(out ResidentRoutine routine) && routine.OnTheMove && r.GetComponent<ResidentVoice>() != null;

        private static float FlatDistance(Resident a, Resident b) =>
            Vector3.ProjectOnPlane(a.transform.position - b.transform.position, Vector3.up).magnitude;

        private void PairUp()
        {
            float earshot = ResidentTuning.Instance.earshot;
            IReadOnlyList<Resident> roster = society.Residents;
            for (int a = 0; a < roster.Count; a++)
            {
                if (!Free(roster[a], out int place, out int circle)) continue;
                for (int b = a + 1; b < roster.Count; b++)
                {
                    if (!Free(roster[b], out int other, out int otherCircle)) continue;
                    if (other != place && (circle == 0 || otherCircle != circle)) continue;
                    if (!ObserverCheck.AnyPlayerSees(roster[a].transform.position, earshot)) break;
                    Open(roster[a], roster[b]);
                    break;
                }
            }
        }

        /// <summary>Holding at its plan place, idle, calm, rested and not already talking.</summary>
        private bool Free(Resident r, out int place, out int circle)
        {
            place = -1;
            circle = 0;
            if (!Rested(r)) return false;

            ResidentRoutine routine = r.GetComponent<ResidentRoutine>();
            if (routine == null || !routine.AtPlace || !routine.Current.HasValue) return false;
            place = routine.HeldPlace;
            if (place == ResidentPresence.NoPlace) return false;   // standing at no place: two such are not "at one place"
            SettlementPlace at = society.Place(place);
            circle = at != null ? at.Group : 0;
            return r.GetComponent<ResidentVoice>() != null;
        }

        /// <summary>Alive, onstage, calm, not already talking, not resting from the last talk.</summary>
        private bool Rested(Resident r)
        {
            if (r == null || r.IsDead || r.IsOffstage || InTalk(r)) return false;
            if (restingUntil.TryGetValue(r, out float until) && Time.time < until) return false;
            if (r.Provocation != null && r.Provocation.Band != AggressionBand.Calm) return false;
            return r.Focus == null || !r.Focus.IsFocused;
        }

        private void Open(Resident a, Resident b, bool walking = false)
        {
            ResidentVoice voice = a.GetComponent<ResidentVoice>();
            if (!OpenFreshAct(a, voice) && !System.Array.Exists(Openers, topic => TrySay(voice, topic, Observation.None))) return;

            var talk = new Conversation { speaker = a, listener = b, turns = 1, walking = walking };
            talks.Add(talk);
            Opened++;
            Face(talk, voice);
        }

        /// <summary>Today's deed is the news: "the outsider hit {name}", naming whoever it was done to.</summary>
        private bool OpenFreshAct(Resident a, ResidentVoice voice)
        {
            int day = society.Day;
            foreach (ResidentMemory.Deed deed in a.Memory.Deeds)
            {
                if (deed.day != day) continue;
                Resident victim = society.ResidentAt(deed.victim);
                return TrySay(voice, Topic.Gossip, Gossip.ObservationOf(deed.act), subject: victim);
            }
            return false;
        }

        /// <summary>Waits out the current line, then the other one answers it. False ends the talk.</summary>
        private bool Advance(Conversation talk)
        {
            if (talk.speaker == null || talk.listener == null || talk.speaker.IsDead || talk.listener.IsDead) return false;
            if (talk.speaker.Provocation != null && talk.speaker.Provocation.Band != AggressionBand.Calm) return false;
            if (talk.listener.Provocation != null && talk.listener.Provocation.Band != AggressionBand.Calm) return false;
            if (talk.walking && FlatDistance(talk.speaker, talk.listener) > ResidentTuning.Instance.walkTalkRange * 2f) return false;

            ResidentVoice said = talk.speaker.GetComponent<ResidentVoice>();
            if (Time.time < said.LastLineEndsAt) return true;
            if (talk.turns >= ResidentTuning.Instance.maxTurns) return false;

            ResidentVoice reply = talk.listener.GetComponent<ResidentVoice>();
            if (!TrySay(reply, Topic.Reply, Observation.None, said.LastRegister)) return false;

            (talk.speaker, talk.listener) = (talk.listener, talk.speaker);
            talk.turns++;
            Face(talk, reply);
            return true;
        }

        private static void Face(Conversation talk, ResidentVoice current)
        {
            if (talk.walking) return;

            float seconds = current.LastLineEndsAt - Time.time + ResidentTuning.Instance.focusAfterLine;
            if (talk.speaker.Focus != null) talk.speaker.Focus.FocusOn(talk.listener.transform, seconds);
            if (talk.listener.Focus != null) talk.listener.Focus.FocusOn(talk.speaker.transform, seconds);
        }

        private void End(int index)
        {
            Conversation talk = talks[index];
            talks.RemoveAt(index);
            ResidentTuning tuning = ResidentTuning.Instance;
            float until = Time.time + (talk.walking ? tuning.walkTalkRestSeconds : tuning.conversationRestSeconds);
            if (talk.speaker != null) restingUntil[talk.speaker] = until;
            if (talk.listener != null) restingUntil[talk.listener] = until;
        }

        private bool InTalk(Resident r) => talks.Exists(t => t.speaker == r || t.listener == r);

        /// <summary>Says the best line for <paramref name="topic"/>; false when no row fits.</summary>
        private static bool TrySay(ResidentVoice voice, Topic topic, Observation seen, Register? repliesTo = null,
                                   Resident subject = null)
        {
            int before = voice.LinesSaid;
            voice.Say(topic, null, seen, repliesTo, subject);
            return voice.LinesSaid != before;
        }
    }
}
