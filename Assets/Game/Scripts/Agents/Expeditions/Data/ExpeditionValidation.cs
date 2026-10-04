// Everything that makes an expedition profile broken, in one list. The profile's OnValidate prints it as
// warnings while a designer works; the settlement validator and the asset tests treat it as errors.
// The slot rules are pure (plain structs in, strings out), so they are testable without the Editor.
using System;
using System.Collections.Generic;
using System.Linq;
using SpaceGame.World;

namespace SpaceGame.Agents.Expeditions
{
    public static class ExpeditionValidation
    {
        /// <summary>Warriors every band takes at least: the front line and its leader.</summary>
        public const int MinWarriors = 2;
        /// <summary>The smallest band a goal may ask for (the sum of its slots' minimums).</summary>
        public const int MinBandSize = 3;
        /// <summary>The largest band a goal may ask for (the sum of its slots' maximums).</summary>
        public const int MaxBandSize = 10;

        public static List<string> Problems(ExpeditionProfile profile) => Problems(profile, ExpeditionCatalog.Instance);

        public static List<string> Problems(ExpeditionProfile profile, ExpeditionCatalog catalog)
        {
            var problems = new List<string>();
            if (profile == null)
            {
                problems.Add("there is no profile.");
                return problems;
            }

            if (profile.goals.Length == 0)
                problems.Add("goals is empty; this settlement can raise no band.");
            for (int i = 0; i < profile.goals.Length; i++)
            {
                ExpeditionGoal goal = profile.goals[i];
                if (goal == null) problems.Add($"goals[{i}] is empty.");
                else CheckGoal(goal, problems);
            }

            if (profile.musterUse == null)
                problems.Add("musterUse is not set; a band has nowhere to muster.");
            else if (profile.musterUse.role != SpotRole.Assembly)
                problems.Add($"musterUse {profile.musterUse.name} has role {profile.musterUse.role}, not Assembly; the day planner would send residents there.");
            if (catalog == null || catalog.IndexOf(profile) < 0)
                problems.Add("it is not in the ExpeditionCatalog, so a stand-in cannot replicate it.");
            if (profile.culture == null)
                problems.Add("culture is not set, so a stand-in cannot look up its archetype.");
            else
            {
                if (profile.culture.expeditions != profile)
                    problems.Add($"its culture {profile.culture.name} names another profile as its expeditions.");
                problems.AddRange(QuotaProblems(profile.roleQuotas,
                    role => profile.culture.archetypes.Any(a => a != null && (a.expeditionRoles & role) != 0)));
            }
            return problems;
        }

        /// <summary>The rules over a goal's slot table alone. <paramref name="hasKit"/> answers whether the goal lists a kit for a role.</summary>
        public static List<string> SlotProblems(string goal, IReadOnlyList<RoleSlot> slots, Func<ExpeditionRole, bool> hasKit)
        {
            var problems = new List<string>();
            for (int i = 0; i < slots.Count; i++)
            {
                RoleSlot slot = slots[i];
                if (!IsOneRole(slot.role) && !(slot.any && slot.role == ExpeditionRole.None))
                    problems.Add($"goal '{goal}' slots[{i}] names '{slot.role}'; a slot names exactly one role (or None when any adult fills it).");
                if (slot.fillFromAnyAdult && slot.role == ExpeditionRole.Warrior)
                    problems.Add($"goal '{goal}' slots[{i}] lets any adult fill a Warrior slot; Warrior slots take only Warriors.");
                if (slot.min > slot.max)
                    problems.Add($"goal '{goal}' slots[{i}] asks for at least {slot.min} but at most {slot.max}.");
            }

            int warriors = slots.Where(s => !s.any && s.role == ExpeditionRole.Warrior).Sum(s => s.min);
            if (warriors < MinWarriors)
                problems.Add($"goal '{goal}' takes at least {warriors} Warrior(s); every band needs {MinWarriors} in Warrior slots.");

            int smallest = slots.Sum(s => s.min), largest = slots.Sum(s => s.max);
            if (smallest < MinBandSize)
                problems.Add($"goal '{goal}' can leave with {smallest} members; a band is at least {MinBandSize}.");
            if (largest > MaxBandSize)
                problems.Add($"goal '{goal}' can leave with {largest} members; a band is at most {MaxBandSize}.");

            foreach (ExpeditionRole role in slots.Select(s => s.role).Distinct())
                if (!hasKit(role))
                    problems.Add($"goal '{goal}' has a {role} slot but no kit for {role}.");
            return problems;
        }

        /// <summary>
        /// The rules over a profile's role quotas alone. <paramref name="cultureHasRole"/> answers whether any archetype of
        /// the profile's culture has a role: a quota of a role nobody can be given can never be met.
        /// </summary>
        public static List<string> QuotaProblems(IReadOnlyList<RoleQuota> quotas, Func<ExpeditionRole, bool> cultureHasRole)
        {
            var problems = new List<string>();
            for (int i = 0; i < quotas.Count; i++)
            {
                RoleQuota quota = quotas[i];
                if (!IsOneRole(quota.role))
                    problems.Add($"roleQuotas[{i}] names '{quota.role}'; a quota names exactly one role.");
                else if (quota.role == ExpeditionRole.Warrior)
                    problems.Add($"roleQuotas[{i}] names Warrior; the warrior quota (warriorQuotaSmall, warriorQuotaLarge) keeps those.");
                else if (!cultureHasRole(quota.role))
                    problems.Add($"roleQuotas[{i}] keeps {quota.role}, but no archetype of the culture has that role.");
                if (quotas.Take(i).Any(earlier => earlier.role == quota.role))
                    problems.Add($"roleQuotas[{i}] repeats the quota of {quota.role}.");
            }
            return problems;
        }

        private static bool IsOneRole(ExpeditionRole role) => role != ExpeditionRole.None && (role & (role - 1)) == 0;

        private static void CheckGoal(ExpeditionGoal goal, List<string> problems)
        {
            problems.AddRange(SlotProblems(goal.name, goal.slots, role => goal.KitIndexOf(role) >= 0));

            if (goal.stages.Length == 0 || goal.stages[goal.stages.Length - 1].kind != StageKind.ReturnHome)
                problems.Add($"goal '{goal.name}' does not end with ReturnHome; its band would never come back.");

            foreach (RoleKit row in goal.kits)
            {
                if (row.kit == null) problems.Add($"goal '{goal.name}' lists no kit under {row.role}.");
                else if (row.kit.weapon == null) problems.Add($"kit '{row.kit.name}' ({goal.name}, {row.role}) has no weapon.");
            }
        }
    }
}
