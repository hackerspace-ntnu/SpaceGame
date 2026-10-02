// Who in a settlement knows what a player did — harm or kindness. Residents only know what they SAW or were TOLD:
//   witness   — the resident it was done to, and every living resident in notice range with a line of sight to
//               it, records the deed first-hand, re-read through its own bond (your sister hit → HarmedKin).
//   word of mouth — anyone who knows tells every resident within earshot (Rumours → Pass), and
//               retells what it only heard too, so the news crosses the settlement neighbour to neighbour.
//   bedtime   — family members tell each other their first-hand facts.
//   hearth    — bonded friends who both sat at the hearth today do the same.
//   dawn      — everyone learns that somebody died.
// Bedtime and hearth retell first-hand facts only, one hop; word of mouth has no hop limit. However it is learned,
// a deed is weighed once per resident: its favor moves by the deed's value times its closeness to whoever the deed
// was done to (Favor), and it holds the deed by its own temper (ForgiveDays) from the day it happened.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    public static class Gossip
    {
        /// <summary>Height of a resident's eyes and of the body they look at, for the sight line.</summary>
        private const float EyeHeight = 1.5f;

        /// <summary>A threat is held half as long as a hit, harm to kin twice as long.</summary>
        private const float ThreatWeight = 0.5f;
        private const float KinHarmWeight = 2f;

        /// <summary>A player (<paramref name="doer"/>) just did <paramref name="act"/> to <paramref name="subject"/>: everyone who saw it knows.</summary>
        public static void Witness(Resident subject, Transform doer, ActKind act, float noticeRadius)
        {
            SettlementSociety society = subject != null ? subject.Society : null;
            if (society == null || doer == null) return;

            string profile = ResidentMemory.ProfileOf(doer);
            if (profile == null) return;

            int day = society.Day;
            float radiusSqr = noticeRadius * noticeRadius;
            foreach (Resident r in society.Residents)
            {
                if (r == null || r.IsDead || r.Memory == null) continue;
                bool saw = r == subject ||
                           ((r.transform.position - subject.transform.position).sqrMagnitude <= radiusSqr &&
                            CanSee(r.transform, subject.transform, doer));
                if (!saw) continue;

                Learn(r, subject, profile, act, subject.index, day, heard: false);
                if (Favor.IsHarm(act)) r.Memory.SawHit(profile, Time.time);
            }
        }

        public static void SpreadAtBedtime(SettlementSociety s, int day)
        {
            foreach (Resident r in s.Residents)
            {
                if (!IsListening(r)) continue;
                foreach (var bond in r.bonds)
                {
                    if (bond.kind != BondKind.Family) continue;
                    Resident kin = s.ResidentAt(bond.other);
                    Tell(s, r, kin, day);
                    Tell(s, kin, r, day);
                }
            }
        }

        public static void SpreadAtHearth(SettlementSociety s, int day)
        {
            foreach (Resident r in s.Residents)
            {
                if (!IsListening(r) || !SatAtHearth(s, r, day)) continue;
                foreach (var bond in r.bonds)
                {
                    Resident friend = s.ResidentAt(bond.other);
                    if (bond.kind == BondKind.Friend && IsListening(friend) && SatAtHearth(s, friend, day))
                    {
                        Tell(s, r, friend, day);
                        Tell(s, friend, r, day);
                    }
                }
            }
        }

        public static void DawnDeaths(SettlementSociety s, int day)
        {
            foreach (Resident dead in s.Residents)
            {
                if (dead == null || !dead.IsDead) continue;
                foreach (Resident r in s.Residents)
                    if (IsListening(r)) r.Memory.LearnDeath(dead.index, day);
            }
        }

        /// <summary>
        /// Word of mouth: every unforgiven fact the teller holds, seen or heard, that the listener does not yet.
        /// Each one newly passed on is added to <paramref name="told"/> as the teller holds it.
        /// </summary>
        public static void Pass(SettlementSociety s, Resident teller, Resident listener, int day,
                                List<ResidentMemory.Deed> told) =>
            Tell(s, teller, listener, day, firstHandOnly: false, told);

        /// <summary>The line a fact is told with: what the teller saw, as the observation the line table keys on.</summary>
        public static Observation ObservationOf(ActKind act) => act switch
        {
            ActKind.Hit => Observation.Hitting,
            ActKind.Threatened => Observation.Menacing,
            ActKind.Defended => Observation.Defending,
            _ => Observation.KinHarmed,
        };

        /// <summary>The teller's unexpired facts the listener lacks, retold as heard and re-read through the listener's own bonds.</summary>
        private static void Tell(SettlementSociety s, Resident teller, Resident listener, int day,
                                 bool firstHandOnly = true, List<ResidentMemory.Deed> told = null)
        {
            if (!IsListening(teller) || !IsListening(listener)) return;
            foreach (ResidentMemory.Deed d in teller.Memory.Deeds)
            {
                if ((firstHandOnly && d.heard) || d.expires <= day || d.victim == listener.index) continue;
                if (Learn(listener, s.ResidentAt(d.victim), d.profile, BaseAct(d.act), d.victim, d.day, heard: true))
                    told?.Add(d);
            }
        }

        // One resident comes to know one deed: remembered as its own bond reads it, and — the first time only —
        // weighed in its favor. False when it already knew.
        private static bool Learn(Resident r, Resident subject, string profile, ActKind act, int subjectIndex, int day, bool heard)
        {
            ActKind heldAs = subject != null ? AsSeenBy(r, subject, act) : act;
            bool known = r.Memory.Holds(profile, heldAs, subjectIndex, day);
            r.Memory.AddDeed(profile, heldAs, subjectIndex, day, DaysHeldFor(heldAs, r.ForgiveDays), heard);
            if (known) return false;

            ResidentTuning tuning = ResidentTuning.Instance;
            r.Memory.ChangeFavor(profile, Favor.ValueOf(act, tuning), Favor.ShareOf(r, subject, tuning), tuning.favorLimit);
            return true;
        }

        private static ActKind AsSeenBy(Resident observer, Resident victim, ActKind act)
        {
            if (observer == victim || !AreFamily(observer, victim)) return act;
            if (act == ActKind.Killed) return ActKind.KilledKin;
            return act == ActKind.Hit ? ActKind.HarmedKin : act;
        }

        private static ActKind BaseAct(ActKind act) => act switch
        {
            ActKind.HarmedKin => ActKind.Hit,
            ActKind.KilledKin => ActKind.Killed,
            _ => act,
        };

        // How long a deed is held — a grudge until it is forgiven, a kindness as long as a hit.
        private static float DaysHeldFor(ActKind act, float forgiveDays) => act switch
        {
            ActKind.Killed or ActKind.KilledKin => ResidentMemory.Never,
            ActKind.Threatened => forgiveDays * ThreatWeight,
            ActKind.HarmedKin => forgiveDays * KinHarmWeight,
            _ => forgiveDays,
        };

        private static bool AreFamily(Resident a, Resident b) => HasBond(a, b.index) || HasBond(b, a.index);

        private static bool HasBond(Resident a, int other)
        {
            if (a.bonds == null) return false;
            foreach (var bond in a.bonds)
                if (bond.other == other && bond.kind == BondKind.Family) return true;
            return false;
        }

        private static bool SatAtHearth(SettlementSociety s, Resident r, int day)
        {
            DayPlan plan = s.PlanFor(r.index, day);
            if (plan?.segments == null) return false;
            foreach (PlanSegment seg in plan.segments)
                if (seg.activity == Activity.Hearth) return true;
            return false;
        }

        private static bool IsListening(Resident r) => r != null && !r.IsDead && r.Memory != null && r.bonds != null;

        // Eye to chest; the witness's own body, the victim's and the attacker's never count as cover.
        private static bool CanSee(Transform witness, Transform victim, Transform attacker)
        {
            Vector3 from = witness.position + Vector3.up * EyeHeight;
            Vector3 to = victim.position + Vector3.up * EyeHeight;
            return !Physics.Linecast(from, to, out RaycastHit hit, Physics.DefaultRaycastLayers,
                                     QueryTriggerInteraction.Ignore)
                   || hit.transform.IsChildOf(witness)
                   || hit.transform.IsChildOf(victim)
                   || hit.transform.IsChildOf(attacker);
        }
    }
}
