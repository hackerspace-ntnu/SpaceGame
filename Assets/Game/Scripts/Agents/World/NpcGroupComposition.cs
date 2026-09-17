// Which prefabs a group spawns, in spawn order. Kept out of NpcWorldSim so the rule — explicit prefab,
// else the roster's draw for the role, and a war party's people from its tier — is tested without
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

        public PlannedMember(GameObject prefab, bool leads)
        {
            Prefab = prefab;
            Leads = leads;
        }
    }

    public static class NpcGroupComposition
    {
        /// <summary>
        /// Whether a planned member boards the group's vessel. Every drawn member does today; one that
        /// travels another way is left out here, and so is neither counted for the vessel nor seated.
        /// </summary>
        public static bool Rides(PlannedMember member) => member.Prefab != null;

        public static List<PlannedMember> Resolve(NpcGroup group, NpcGroupTemplate template)
        {
            var plan = new List<PlannedMember>();
            FactionRoster roster = template.tribe != null ? template.tribe.roster : null;

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

                foreach (RoleCount wanted in tier.roles)
                {
                    if (wanted == null) continue;

                    for (int i = 0; i < Mathf.Max(1, wanted.count); i++)
                        plan.Add(new PlannedMember(roster.Draw(wanted.role, group.RosterSeed, plan.Count), plan.Count == 0));
                }

                return plan;
            }

            if (template.members == null) return plan;

            foreach (NpcGroupMemberSpec spec in template.members)
            {
                if (spec == null) continue;

                for (int i = 0; i < Mathf.Max(1, spec.count); i++)
                {
                    GameObject prefab = spec.prefab != null
                        ? spec.prefab
                        : roster != null ? roster.Draw(spec.role, group.RosterSeed, plan.Count) : null;

                    plan.Add(new PlannedMember(prefab, spec.isLeader));
                }
            }

            return plan;
        }
    }
}
