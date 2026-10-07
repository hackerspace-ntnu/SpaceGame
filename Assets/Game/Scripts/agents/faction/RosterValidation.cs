// Everything that makes a roster broken, in one list. OnValidate prints it as warnings while a
// designer works; it is an error for the shipped roster (spec §3, CONTENT-0004).
using System.Collections.Generic;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    public static class RosterValidation
    {
        public static List<string> Problems(FactionRoster roster)
        {
            var problems = new List<string>();
            if (roster == null)
            {
                problems.Add("there is no roster.");
                return problems;
            }

            if (roster.faction == null)
                problems.Add("faction is not set.");
            else if (roster.faction.roster != roster)
                problems.Add($"{roster.faction.factionName}.roster does not point back at this roster.");

            for (int i = 0; i < roster.members.Length; i++)
                CheckMember(roster, i, problems);

            for (int i = 0; i < roster.handItems.Length; i++)
            {
                InventoryItem item = roster.handItems[i];
                if (item == null)
                    problems.Add($"handItems[{i}] is empty.");
                else if (item.equipKind != EquipKind.Hand)
                    problems.Add($"handItems[{i}] '{item.name}' is a {item.equipKind}, not a hand item.");
            }

            if (roster.warPartyTiers.Length == 0)
                problems.Add("warPartyTiers is empty; this tribe cannot raise a war party.");

            for (int t = 0; t < roster.warPartyTiers.Length; t++)
            {
                WarPartyTier tier = roster.warPartyTiers[t];
                if (tier == null || tier.roles == null) continue;

                foreach (RoleCount wanted in tier.roles)
                {
                    if (wanted != null && !HasRole(roster, wanted.role))
                        problems.Add($"warPartyTiers[{t}] asks for {wanted.role}, which has no members.");
                }
            }

            return problems;
        }

        private static void CheckMember(FactionRoster roster, int i, List<string> problems)
        {
            RosterMember member = roster.members[i];
            if (member == null || member.prefab == null)
            {
                problems.Add($"members[{i}] has no prefab.");
                return;
            }

            string label = $"members[{i}] '{member.prefab.name}'";

            if (member.prefab.GetComponent<AgentController>() == null)
                problems.Add($"{label} has no AgentController.");

            EntityFaction entityFaction = member.prefab.GetComponent<EntityFaction>();
            if (entityFaction == null)
                problems.Add($"{label} has no EntityFaction.");

            if (member.role == RosterRole.Rider)
            {
                if (member.prefab.GetComponent<NpcPassenger>() == null)
                    problems.Add($"{label} is a Rider but carries no NpcPassenger to seat one.");
                return;
            }

            // Faction is not replicated: a client only ever sees the prefab's own. So it must already
            // be the tribe, or the two machines disagree about who this person is.
            if (entityFaction != null && roster.faction != null && entityFaction.Faction != roster.faction)
                problems.Add($"{label} serializes faction '{entityFaction.Faction?.factionName}', not " +
                             $"'{roster.faction.factionName}'.");
        }

        private static bool HasRole(FactionRoster roster, RosterRole role)
        {
            foreach (RosterMember member in roster.members)
                if (member != null && member.prefab != null && member.role == role && member.weight > 0f)
                    return true;

            return false;
        }
    }
}
