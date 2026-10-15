// What a deed does to a resident's favor toward the player who did it. A deed is worth its full value
// (ResidentTuning.favorFor*) to the resident it was done to, and a share of it to everyone else who sees or
// hears of it — the larger the closer they are to that resident: family, then friends, then coworkers, then
// anyone else in the settlement. So a good deed warms the whole settlement a little and the one you helped
// most, and harm cools it the same way. Each resident counts a deed once (Gossip decides when it is new).
//
// Pure: read by Gossip when a deed is learned, tested on its own.
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    public static class Favor
    {
        /// <summary>Is this deed harm (and so a grudge), rather than a kindness?</summary>
        public static bool IsHarm(ActKind act) => act != ActKind.Defended;

        /// <summary>What <paramref name="act"/> is worth to the resident it was done to; kin readings count as the act itself.</summary>
        public static float ValueOf(ActKind act, ResidentTuning tuning) => act switch
        {
            ActKind.Hit or ActKind.HarmedKin => tuning.favorForHit,
            ActKind.Killed or ActKind.KilledKin => tuning.favorForKilling,
            ActKind.Threatened => tuning.favorForThreat,
            ActKind.Defended => tuning.favorForDefending,
            _ => 0f,
        };

        /// <summary>
        /// How much of a deed done to <paramref name="subject"/> reaches <paramref name="listener"/>: all of it for
        /// the subject itself, else the share of the closest bond either of them holds to the other.
        /// </summary>
        public static float ShareOf(Resident listener, Resident subject, ResidentTuning tuning)
        {
            if (listener == null || subject == null) return tuning.neighbourShare;
            if (listener == subject) return 1f;

            float share = tuning.neighbourShare;
            Closest(listener, subject.index, tuning, ref share);
            Closest(subject, listener.index, tuning, ref share);
            return share;
        }

        /// <summary>The change a deed worth <paramref name="value"/> makes to <paramref name="current"/>, kept inside the limit.</summary>
        public static float Apply(float current, float value, float share, float limit) =>
            Mathf.Clamp(current + value * share, -limit, limit);

        private static void Closest(Resident from, int other, ResidentTuning tuning, ref float share)
        {
            if (from.bonds == null) return;
            foreach (ResidentBond bond in from.bonds)
                if (bond.other == other) share = Mathf.Max(share, ShareOf(bond.kind, tuning));
        }

        private static float ShareOf(BondKind kind, ResidentTuning tuning) => kind switch
        {
            BondKind.Family => tuning.familyShare,
            BondKind.Friend => tuning.friendShare,
            _ => tuning.coworkerShare,
        };
    }
}
