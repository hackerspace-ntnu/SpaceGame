// Every war currently on: which tribe, which player, how escalated, which party is out, how long until
// the next. Pure C# so the lifecycle is tested without a scene; WarPartyDirector is the glue that acts
// on it. A war is keyed per player (rosters spec §5.1): two players at war with Sand are two wars.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    public sealed class War
    {
        public War(FactionDefinition tribe, string profileId)
        {
            Tribe = tribe;
            ProfileId = profileId;
        }

        public FactionDefinition Tribe { get; }
        public string ProfileId { get; }
        public int Tier { get; internal set; }
        public string PartyGroupId { get; internal set; } = string.Empty;
        public float Cooldown { get; internal set; }

        public bool HasParty => !string.IsNullOrEmpty(PartyGroupId);

        internal int PartiesRaised;
    }

    public sealed class WarBook
    {
        private readonly List<War> wars = new();

        // The tier a war stands at, kept between parties and across saves (FactionGoodwillSaveable's
        // warTier). Cleared when the war ends: peace resets escalation.
        private readonly Dictionary<(string, string), int> tiers = new();

        public IReadOnlyList<War> Wars => wars;

        /// <summary>A copy to iterate while resolving, which may close wars.</summary>
        public List<War> Snapshot() => new(wars);

        public War Find(FactionDefinition tribe, string profileId)
        {
            foreach (War war in wars)
                if (war.Tribe == tribe && war.ProfileId == profileId) return war;

            return null;
        }

        public War FindByGroup(string groupId)
        {
            if (string.IsNullOrEmpty(groupId)) return null;

            foreach (War war in wars)
                if (war.PartyGroupId == groupId) return war;

            return null;
        }

        public War Open(FactionDefinition tribe, string profileId)
        {
            War war = Find(tribe, profileId);
            if (war != null) return war;

            war = new War(tribe, profileId) { Tier = TierFor(tribe, profileId) };
            wars.Add(war);
            return war;
        }

        public void Close(War war)
        {
            if (war == null) return;

            wars.Remove(war);
            tiers.Remove(Key(war.Tribe, war.ProfileId));
        }

        /// <summary>Cooldowns run only between parties.</summary>
        public void Tick(float deltaTime)
        {
            foreach (War war in wars)
                if (!war.HasParty) war.Cooldown = Mathf.Max(0f, war.Cooldown - deltaTime);
        }

        public bool ReadyToRaise(War war) => war != null && !war.HasParty && war.Cooldown <= 0f;

        /// <summary>
        /// Name the next party. <paramref name="taken"/> is asked because the counter restarts after a
        /// load while a restored party may still hold "…:1".
        /// </summary>
        public string AssignParty(War war, Func<string, bool> taken)
        {
            string id;
            do
            {
                war.PartiesRaised++;
                id = $"warparty:{war.Tribe.ID}:{war.ProfileId}:{war.PartiesRaised}";
            }
            while (taken(id));

            war.PartyGroupId = id;
            return id;
        }

        public void Resolve(War war, Reckoning outcome, int maxTier, float cooldown)
        {
            war.Tier = WarPartyRules.NextTier(outcome, war.Tier, maxTier);
            tiers[Key(war.Tribe, war.ProfileId)] = war.Tier;
            ClearParty(war, cooldown);
        }

        public void ClearParty(War war, float cooldown)
        {
            war.PartyGroupId = string.Empty;
            war.Cooldown = Mathf.Max(0f, cooldown);
        }

        /// <summary>A party restored from a save: its war resumes with it rather than raising a second.</summary>
        public War Adopt(FactionDefinition tribe, string profileId, string groupId, int tier)
        {
            War war = Open(tribe, profileId);
            war.PartyGroupId = groupId;
            war.Tier = Mathf.Max(0, tier);
            tiers[Key(tribe, profileId)] = war.Tier;
            return war;
        }

        public int TierFor(FactionDefinition tribe, string profileId)
        {
            War war = Find(tribe, profileId);
            if (war != null) return war.Tier;

            return tiers.TryGetValue(Key(tribe, profileId), out int tier) ? tier : 0;
        }

        public void RestoreTier(FactionDefinition tribe, string profileId, int tier)
        {
            int restored = Mathf.Max(0, tier);
            if (restored == 0)
                tiers.Remove(Key(tribe, profileId));
            else
                tiers[Key(tribe, profileId)] = restored;

            War war = Find(tribe, profileId);
            if (war != null && !war.HasParty) war.Tier = restored;
        }

        private static (string, string) Key(FactionDefinition tribe, string profileId) =>
            (tribe != null ? tribe.ID : string.Empty, profileId ?? string.Empty);
    }
}
