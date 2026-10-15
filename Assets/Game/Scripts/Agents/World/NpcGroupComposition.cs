// Which prefabs a group spawns, in spawn order. Kept out of NpcWorldSim so the rule — the owner's
// planned members when it set them, else explicit prefab, else the roster's deal for the role, a war
// party's people from its tier, and the shuffled members dealt into their column — is tested without
// spawning anything.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    public readonly struct PlannedMember
    {
        /// <summary>Null when nothing could be drawn; the slot is skipped but its index is still spent.</summary>
        public readonly GameObject Prefab;
        public readonly bool Leads;

        /// <summary>Rides one of the group's carriers instead of walking (NpcGroupMemberSpec.crew).</summary>
        public readonly bool Crew;

        /// <summary>Flies on its own wings beside the group's vessel instead of taking a seat (RoleCount.ownWings).</summary>
        public readonly bool OwnWings;

        public PlannedMember(GameObject prefab, bool leads, bool crew = false, bool ownWings = false)
        {
            Prefab = prefab;
            Leads = leads;
            Crew = crew;
            OwnWings = ownWings;
        }
    }

    public static class NpcGroupComposition
    {
        /// <summary>
        /// Whether a planned member boards the group's vessel. Every drawn member does except crew,
        /// who ride one of the group's own carriers, and fliers, who escort it on their own wings; one that
        /// travels another way is left out here, and so is neither counted for the vessel nor seated.
        /// </summary>
        public static bool Rides(PlannedMember member) => member.Prefab != null && !member.Crew && !member.OwnWings;

        public static List<PlannedMember> Resolve(NpcGroup group, NpcGroupTemplate template)
        {
            // The owner's own people: a copy, so the sim never hands back the owner's list.
            if (group.PlannedOverride != null) return new List<PlannedMember>(group.PlannedOverride);

            var plan = new List<PlannedMember>();
            FactionRoster roster = template.tribe != null ? template.tribe.roster : null;
            // How many of each role the roster has dealt this group: its draws come off one deck per role.
            var dealt = new Dictionary<RosterRole, int>();

            GameObject Deal(RosterRole role)
            {
                dealt.TryGetValue(role, out int n);
                dealt[role] = n + 1;
                return roster.Deal(role, group.RosterSeed, n);
            }

            if (group.IsWarParty)
            {
                WarPartyTier tier = roster != null ? roster.TierAt(group.Tier) : null;
                if (tier == null)
                {
                    Debug.LogError($"[NpcWorldSim] War party '{group.Id}' has no tier {group.Tier} to draw " +
                                   $"from: {(template.tribe != null ? template.tribe.factionName : "no tribe")} " +
                                   "has no roster or no warPartyTiers.");
                    return plan;
                }

                // The first member that does not fly on its own wings leads: a flier never does, since it
                // spends the journey keeping station on the vessel rather than steering the party.
                bool led = false;
                foreach (RoleCount wanted in tier.roles)
                {
                    if (wanted == null) continue;

                    for (int i = 0; i < Mathf.Max(1, wanted.count); i++)
                    {
                        bool leads = !led && !wanted.ownWings;
                        led |= leads;
                        plan.Add(new PlannedMember(Deal(wanted.role), leads, ownWings: wanted.ownWings));
                    }
                }

                return plan;
            }

            if (template.members == null) return plan;

            ColumnCard leaderCard = default;
            bool leaderListed = false;
            var deck = new List<ColumnCard>();
            var deckPlaces = new List<int>();

            foreach (NpcGroupMemberSpec spec in template.members)
            {
                if (spec == null) continue;

                if (spec.isLeader && !leaderListed)
                {
                    leaderCard = spec.column;
                    leaderListed = true;
                }

                int count = spec.DrawCount(group.RosterSeed, plan.Count);
                for (int i = 0; i < count; i++)
                {
                    GameObject prefab = spec.prefab != null
                        ? spec.prefab
                        : roster != null ? Deal(spec.role) : null;

                    // Nothing drawn takes no slot, so only a member that spawns is dealt a place.
                    if (spec.column.shuffled && prefab != null)
                    {
                        deck.Add(spec.column);
                        deckPlaces.Add(plan.Count);
                    }

                    plan.Add(new PlannedMember(prefab, spec.isLeader, spec.crew));
                }
            }

            DealColumn(plan, deck, deckPlaces, leaderCard, template.formation, group);
            return plan;
        }

        /// <summary>
        /// Deals the shuffled members (<paramref name="deck"/>, listed at <paramref name="places"/> in
        /// the plan) into those places in the order the group's seed deals (<see cref="ColumnDeal"/>).
        /// A deck no shuffle can deal is left as listed; ColumnDeal has logged why.
        /// </summary>
        private static void DealColumn(List<PlannedMember> plan, List<ColumnCard> deck, List<int> places,
                                       ColumnCard leader, FormationShape shape, NpcGroup group)
        {
            if (deck.Count < 2) return;

            if (!ColumnDeal.TryDeal(leader, deck, FollowerSlots(plan, places), shape, group.RosterSeed, out int[] order))
                return;

            var listed = new PlannedMember[places.Count];
            for (int i = 0; i < places.Count; i++) listed[i] = plan[places[i]];
            for (int i = 0; i < places.Count; i++) plan[places[i]] = listed[order[i]];
        }

        /// <summary>
        /// The formation slot each of <paramref name="places"/> takes, counted as NpcWorldSim.Spawn counts
        /// them: every planned member that spawns and does not lead takes the next.
        /// </summary>
        private static List<int> FollowerSlots(List<PlannedMember> plan, List<int> places)
        {
            var slotAt = new int[plan.Count];
            int followers = 0;
            bool leaderTaken = false;
            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].Prefab == null) continue;

                if (plan[i].Leads && !leaderTaken) leaderTaken = true;
                else slotAt[i] = followers++;
            }

            var slots = new List<int>(places.Count);
            foreach (int place in places) slots.Add(slotAt[place]);
            return slots;
        }
    }
}
