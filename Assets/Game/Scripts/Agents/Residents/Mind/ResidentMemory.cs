// What one resident remembers about players: how familiar each one is, how much it favors them, and the named
// deeds it knows of — harm (a grudge) and kindness alike. Plain C#, keyed by the player's save PROFILE id rather
// than by object, so it survives the player leaving, rejoining as a new NetworkObject and the world reloading.
// A player with no bound profile is unknown and nothing is written for them.
//
// Familiarity grows by talking; favor is signed and moved by deeds (Favor), once per deed. Regard — the two
// added — is what makes a resident warm or cold. A deed carries whether it was seen or HEARD: harm to somebody
// who is not kin that was only heard of moves favor but is not a personal grudge. Witnessed hits are also kept
// as Time.time stamps for "is hitting right now"; those are deliberately not saved — a reload is not a reason
// to still be mid-fight.
using System;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core.Persistence;

namespace SpaceGame.Agents.Residents
{
    public sealed class ResidentMemory
    {
        /// <summary>Expiry of a lethal act: never forgiven.</summary>
        public const float Never = float.MaxValue;

        [Serializable]
        public struct Deed
        {
            public string profile;
            public ActKind act;
            public int victim;
            public int day;
            public float expires;
            public bool heard;
        }

        /// <summary>A death this resident has heard of, and the dawn it learned of it.</summary>
        [Serializable]
        public struct KnownDeath
        {
            public int victim;
            public int day;
        }

        [Serializable]
        public class Acquaintance
        {
            public string profile;
            public float familiarity;
            public int talkDay;
            public int talksToday;
        }

        [Serializable]
        public struct Standing
        {
            public string profile;
            public float favor;
        }

        /// <summary>The saved form: only plain fields and lists, so Newtonsoft round-trips it.</summary>
        [Serializable]
        public class MemoryState
        {
            public List<Acquaintance> acquaintances = new();
            public List<Standing> favor = new();
            public List<Deed> deeds = new();
            public List<KnownDeath> knownDeaths = new();
        }

        private readonly Dictionary<string, Acquaintance> acquaintances = new();
        private readonly Dictionary<string, float> favor = new();
        private readonly List<Deed> deeds = new();
        private readonly List<KnownDeath> knownDeaths = new();
        private readonly List<(string profile, float time)> hitsSeen = new();

        public IReadOnlyList<Deed> Deeds => deeds;
        public IReadOnlyList<KnownDeath> KnownDeaths => knownDeaths;
        public IReadOnlyDictionary<string, float> FavorByProfile => favor;

        public float Familiarity(string profile) =>
            profile != null && acquaintances.TryGetValue(profile, out Acquaintance a) ? a.familiarity : 0f;

        public float FavorOf(string profile) => profile != null && favor.TryGetValue(profile, out float f) ? f : 0f;

        /// <summary>What decides warm or cold: how well this resident knows the player plus how it favors them.</summary>
        public float Regard(string profile) => Familiarity(profile) + FavorOf(profile);

        /// <summary>A deed worth <paramref name="value"/> to its subject, of which this resident takes <paramref name="share"/>.</summary>
        public void ChangeFavor(string profile, float value, float share, float limit)
        {
            if (profile != null) favor[profile] = Favor.Apply(FavorOf(profile), value, share, limit);
        }

        public bool Met(string profile) => profile != null && acquaintances.ContainsKey(profile);

        /// <summary>One conversation: familiarity grows by <paramref name="gain"/> up to <paramref name="dailyCap"/> talks a day.</summary>
        public void Talked(string profile, int day, float gain, int dailyCap)
        {
            if (profile == null) return;
            if (!acquaintances.TryGetValue(profile, out Acquaintance a))
                acquaintances[profile] = a = new Acquaintance { profile = profile, talkDay = day };

            if (a.talkDay != day) { a.talkDay = day; a.talksToday = 0; }
            if (a.talksToday >= dailyCap) return;
            a.talksToday++;
            a.familiarity += gain;
        }

        /// <summary>Remember a deed. The same deed on the same subject that day is one, kept first-hand and longest.</summary>
        public void AddDeed(string profile, ActKind act, int victim, int day, float heldDays, bool heard = false)
        {
            if (profile == null) return;
            float expires = heldDays >= Never ? Never : day + heldDays;
            for (int i = 0; i < deeds.Count; i++)
            {
                Deed d = deeds[i];
                if (d.profile != profile || d.act != act || d.victim != victim || d.day != day) continue;
                d.expires = Mathf.Max(d.expires, expires);
                d.heard &= heard;
                deeds[i] = d;
                return;
            }
            deeds.Add(new Deed { profile = profile, act = act, victim = victim, day = day, expires = expires, heard = heard });
        }

        /// <summary>Is this deed already known, seen or heard — the test before passing news on or weighing it again.</summary>
        public bool Holds(string profile, ActKind act, int victim, int day) =>
            deeds.Exists(d => d.profile == profile && d.act == act && d.victim == victim && d.day == day);

        /// <summary>
        /// An unforgiven grudge that is this resident's own: harm it saw, or harm to its kin however it learned of
        /// it. Harm to somebody else that it only heard of moves its favor and nothing more.
        /// </summary>
        public bool HoldsPersonalGrudge(string profile, int day) =>
            deeds.Exists(d => d.profile == profile && d.expires > day && Favor.IsHarm(d.act) && (!d.heard || IsKinHarm(d.act)));

        public bool KinHarmedBy(string profile, int day) =>
            deeds.Exists(d => d.profile == profile && d.expires > day && IsKinHarm(d.act));

        public void Expire(int day) => deeds.RemoveAll(d => d.expires <= day);

        private static bool IsKinHarm(ActKind act) => act is ActKind.HarmedKin or ActKind.KilledKin;

        public void LearnDeath(int victim, int day)
        {
            if (!KnowsDeath(victim)) knownDeaths.Add(new KnownDeath { victim = victim, day = day });
        }

        public bool KnowsDeath(int victim) => knownDeaths.Exists(d => d.victim == victim);

        /// <summary>A hit this resident just saw (runtime only, see header).</summary>
        public void SawHit(string profile, float time)
        {
            if (profile != null) hitsSeen.Add((profile, time));
        }

        public int HitsSeenSince(string profile, float since)
        {
            hitsSeen.RemoveAll(h => h.time < since);
            int count = 0;
            foreach (var hit in hitsSeen)
                if (hit.profile == profile) count++;
            return count;
        }

        public MemoryState Capture()
        {
            var state = new MemoryState();
            foreach (Acquaintance a in acquaintances.Values)
                state.acquaintances.Add(new Acquaintance
                    { profile = a.profile, familiarity = a.familiarity, talkDay = a.talkDay, talksToday = a.talksToday });
            foreach (var standing in favor)
                state.favor.Add(new Standing { profile = standing.Key, favor = standing.Value });
            state.deeds.AddRange(deeds);
            state.knownDeaths.AddRange(knownDeaths);
            return state;
        }

        /// <summary>Replace everything with <paramref name="s"/>; null clears.</summary>
        public void Restore(MemoryState s)
        {
            acquaintances.Clear();
            favor.Clear();
            deeds.Clear();
            knownDeaths.Clear();
            hitsSeen.Clear();
            if (s == null) return;

            if (s.acquaintances != null)
                foreach (Acquaintance a in s.acquaintances)
                    if (a != null && a.profile != null) acquaintances[a.profile] = a;
            if (s.favor != null)
                foreach (Standing standing in s.favor)
                    if (standing.profile != null) favor[standing.profile] = standing.favor;
            if (s.deeds != null) deeds.AddRange(s.deeds);
            if (s.knownDeaths != null) knownDeaths.AddRange(s.knownDeaths);
        }

        /// <summary>The save profile bound to this player (or the player it is a child of), or null when unknown.</summary>
        public static string ProfileOf(Transform player)
        {
            PlayerSaveService players = SaveManager.Instance?.Players;
            if (players == null) return null;

            for (Transform t = player; t != null; t = t.parent)
                if (players.TryGetProfileFor(t.gameObject, out string profile))
                    return profile;
            return null;
        }
    }
}
