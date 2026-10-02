// Plan §10.3: everything a settlement's residents need that would otherwise fail silently at runtime —
// archetypes that read alike, a line table that does not parse or leaves a topic × stance unanswered,
// residents without an archetype, components added on an instance, and residents of another faction.
// Whether every door and spot can be walked to is the generator's own check (SettlementPlaces.Problems).
// The inspector's Generate runs this after every pass.
using System;
using System.Collections.Generic;
using System.Linq;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceGame.Agents.Residents.EditorTools
{
    public static class ResidentValidator
    {
        public enum Severity { Info, Warning, Error }

        public readonly struct Finding
        {
            public readonly Severity severity;
            public readonly string message;
            public readonly Object context;
            public Finding(Severity severity, string message, Object context) => (this.severity, this.message, this.context) = (severity, message, context);
        }

        // Two archetypes must differ in at least this many of: post, trips, temperament quadrant, held item.
        private const int MinArchetypeDifferences = 2;

        /// <summary>Logs every finding with its context object; returns the number of errors.</summary>
        public static int Report(Settlement settlement)
        {
            List<Finding> findings = Validate(settlement);
            foreach (Finding f in findings)
            {
                string line = $"[ResidentValidator] {settlement.name}: {f.message}";
                if (f.severity == Severity.Error) Debug.LogError(line, f.context);
                else if (f.severity == Severity.Warning) Debug.LogWarning(line, f.context);
                else Debug.Log(line, f.context);
            }
            return findings.Count(f => f.severity == Severity.Error);
        }

        public static List<Finding> Validate(Settlement settlement)
        {
            var findings = new List<Finding>();
            SettlementCulture culture = settlement.Culture;
            if (culture == null) return findings;

            CheckArchetypes(findings, culture);
            CheckErrands(findings, culture);
            CheckLines(findings, culture);
            CheckResidents(findings, settlement.Society);
            return findings;
        }

        private static void CheckArchetypes(List<Finding> findings, SettlementCulture culture)
        {
            var all = culture.archetypes.Where(a => a != null).Distinct().ToList();
            if (all.Count == 0) findings.Add(new Finding(Severity.Error, $"{culture.name} lists no archetypes.", culture));
            foreach (ResidentArchetype a in all.Where(a => string.IsNullOrEmpty(a.roleName)))
                findings.Add(new Finding(Severity.Error, $"archetype {a.name} has no roleName.", a));
            // Alike archetypes are a design note, not a fault: one line, so it never buries a real warning.
            var alike = new List<string>();
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                {
                    ResidentArchetype a = all[i], b = all[j];
                    int differences = (a.post != b.post ? 1 : 0) + (a.trips != b.trips ? 1 : 0) + (a.heldItem != b.heldItem ? 1 : 0)
                                      + (Quadrant(a) != Quadrant(b) ? 1 : 0);
                    if (differences < MinArchetypeDifferences) alike.Add($"{a.name}/{b.name}");
                }
            if (alike.Count > 0)
                findings.Add(new Finding(Severity.Info, $"{alike.Count} archetype pairs differ in fewer than {MinArchetypeDifferences} of " +
                                                        $"post/trips/temperament/held item and may read alike: {string.Join(", ", alike)}.", culture));
        }

        // A chore must be whole and carry something the tuning can replicate; patrols need a guard to hand out.
        private static void CheckErrands(List<Finding> findings, SettlementCulture culture)
        {
            ResidentTuning tuning = ResidentTuning.Instance;
            foreach (ResidentArchetype a in culture.archetypes.Where(a => a != null && a.chore != null))
            {
                if (!a.chore.IsComplete)
                    findings.Add(new Finding(Severity.Error, $"chore {a.chore.name} (kept by {a.name}) lacks a source or a target spot use.", a.chore));
                else if (a.chore.carried != null && tuning.PropIndexOf(a.chore.carried) == 0)
                    findings.Add(new Finding(Severity.Error, $"chore {a.chore.name} carries {a.chore.carried.name}, which ResidentTuning.carryItems does not list — nobody would see it.", a.chore));
                if (a.post == null && a.Lifestyle != Lifestyle.Roamer)
                    findings.Add(new Finding(Severity.Warning, $"{a.name} keeps a chore but is not a roamer or a post holder; the chore will never be planned.", a));
            }
            if (culture.patrolShare > 0f && !culture.archetypes.Any(a => a != null && a.duty == ResidentDuty.Patrol))
                findings.Add(new Finding(Severity.Warning, $"{culture.name} wants a patrol share of {culture.patrolShare:0.##} but has no archetype whose duty is Patrol.", culture));
        }

        private static (bool bold, bool prickly) Quadrant(ResidentArchetype a) => (a.nerve >= Attitude.Midpoint, a.temper >= Attitude.Midpoint);

        private static void CheckLines(List<Finding> findings, SettlementCulture culture)
        {
            TextAsset lines = culture.lines;
            if (lines == null)
            {
                findings.Add(new Finding(Severity.Error, $"{culture.name} has no line table.", culture));
                return;
            }
            LineTable table = LineTable.Parse(lines.text);
            foreach (string error in table.Errors) findings.Add(new Finding(Severity.Error, $"lines: {error}", lines));
            foreach (var clash in table.Rows.GroupBy(r => r.id).Where(g => g.Count() > 1))
                findings.Add(new Finding(Severity.Error, $"lines: keys {string.Join(", ", clash.Select(r => r.key))} share id {clash.Key}.", lines));
            foreach (LineRow row in table.Rows)
                if (SpeechTokens.CountTokens(row.text, out string unknown) > LineTable.MaxTokensPerLine || !string.IsNullOrEmpty(unknown))
                    findings.Add(new Finding(Severity.Warning, $"line {row.key}: unknown token '{unknown}' or more than {LineTable.MaxTokensPerLine} tokens.", lines));
            var generic = table.Rows.Where(r => string.IsNullOrEmpty(r.speaker)).ToList();
            var gaps = from Topic topic in Enum.GetValues(typeof(Topic))
                       from Stance stance in Enum.GetValues(typeof(Stance))
                       where !generic.Any(r => (!r.topic.HasValue || r.topic.Value == topic) && r.MatchesStance(stance))
                       select $"{topic} × {stance}";
            string missing = string.Join(", ", gaps);
            if (missing.Length > 0) findings.Add(new Finding(Severity.Warning, $"lines: no generic row for {missing}.", lines));
        }

        private static void CheckResidents(List<Finding> findings, SettlementSociety society)
        {
            IReadOnlyList<Resident> residents = society.Residents;
            var factions = residents.Where(r => r != null && r.TryGetComponent(out EntityFaction _))
                .Select(r => r.GetComponent<EntityFaction>().Faction).Where(f => f != null).ToList();
            FactionDefinition owner = factions.GroupBy(f => f).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            for (int i = 0; i < residents.Count; i++)
            {
                Resident r = residents[i];
                if (r.index != i) findings.Add(new Finding(Severity.Error, $"{r.name}: roster index {r.index} where {i} was expected — regenerate.", r));
                if (r.archetype == null) findings.Add(new Finding(Severity.Warning, $"{r.name} has no archetype.", r));
                if (r.TryGetComponent(out EntityFaction faction) && faction.Faction != owner)
                    findings.Add(new Finding(Severity.Error, $"{r.name} is {(faction.Faction ? faction.Faction.name : "factionless")}, not {owner?.name}.", r));
                if (PrefabUtility.IsPartOfPrefabInstance(r))
                    foreach (var added in PrefabUtility.GetAddedComponents(r.gameObject))
                        findings.Add(new Finding(Severity.Warning, $"{r.name}: {added.instanceComponent.GetType().Name} is added on the instance — it belongs on the prefab.", r));
            }
        }
    }
}
