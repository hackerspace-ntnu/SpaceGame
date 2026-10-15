// News of what a player did — harm or kindness — passed on by word of mouth the moment it happens. Whoever
// knows (the resident it was done to and the witnesses first, Gossip.Witness) tells every resident within
// earshot; each of those passes it on to everyone within ITS earshot rumourTellSeconds later, so the news
// walks across the settlement from neighbour to neighbour, and reaches anyone who later comes within earshot
// of somebody who knows. What was only heard is retold too: there is no hop limit, only distance. Each
// resident weighs the news in its favor once, by its closeness to whoever it was done to (Favor). News never
// starts a fight: a fight spreads by fighters calling for help (AlertBroadcaster.callForHelpEvery on the
// character prefab), in or out of any settlement.
//
// Server only, ticked by SettlementSociety. Persistence: the news itself is ResidentMemory's (saved); when
// each resident learned it is runtime only.
using System.Collections.Generic;
using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    public sealed class Rumours
    {
        private readonly SettlementSociety society;
        // When each resident last learned something new; it passes news on rumourTellSeconds after that. A
        // resident missing here (a witness, anyone after a reload) knows from before, and tells at once.
        private readonly Dictionary<Resident, float> learnedAt = new();
        private readonly List<ResidentMemory.Deed> told = new();
        private float nextSweepAt;

        public Rumours(SettlementSociety society) => this.society = society;

        public void Tick()
        {
            if (Time.time < nextSweepAt) return;
            nextSweepAt = Time.time + ResidentTuning.Instance.rumourSweepSeconds;
            PassNews();
        }

        private void PassNews()
        {
            ResidentTuning tuning = ResidentTuning.Instance;
            float now = Time.time;
            float earshotSqr = tuning.earshot * tuning.earshot;
            int day = society.Day;
            IReadOnlyList<Resident> roster = society.Residents;
            foreach (Resident teller in roster)
            {
                if (!CanTalk(teller) || teller.Memory.Deeds.Count == 0) continue;
                if (learnedAt.TryGetValue(teller, out float learned) && now - learned < tuning.rumourTellSeconds) continue;

                told.Clear();
                foreach (Resident listener in roster)
                {
                    if (listener == teller || !CanTalk(listener) || !WithinEarshot(teller, listener, earshotSqr)) continue;

                    int before = told.Count;
                    Gossip.Pass(society, teller, listener, day, told);
                    if (told.Count > before) learnedAt[listener] = now;
                }

                if (told.Count > 0) SayIt(teller, told[0], tuning.earshot);
            }
        }

        // Out loud only where a player can hear it, never over the teller's own line or mid-fight, and never by
        // the resident it was done to — it answers for itself (a fight, or its thanks).
        private void SayIt(Resident teller, ResidentMemory.Deed news, float earshot)
        {
            if (news.victim == teller.index) return;
            ResidentVoice voice = teller.GetComponent<ResidentVoice>();
            if (voice == null || Time.time < voice.LastLineEndsAt) return;
            if (teller.Provocation != null && teller.Provocation.IsProvoked) return;
            if (!ObserverCheck.AnyPlayerSees(teller.transform.position, earshot)) return;

            Resident victim = society.ResidentAt(news.victim);
            voice.Say(Topic.Rumour, null, Gossip.ObservationOf(news.act), subject: victim);
        }

        private static bool WithinEarshot(Resident a, Resident b, float earshotSqr) =>
            (a.transform.position - b.transform.position).sqrMagnitude <= earshotSqr;

        // Indoors (offstage) a resident is out of everyone's earshot.
        private static bool CanTalk(Resident r) => r != null && !r.IsDead && !r.IsOffstage && r.Memory != null;
    }
}
