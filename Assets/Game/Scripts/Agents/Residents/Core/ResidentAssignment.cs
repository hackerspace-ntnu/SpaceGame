// Who each character a Settlement just placed is: its place in the roster, its archetype, name and seed,
// the dwelling it sleeps in, and its family, coworkers and friends. Run by Settlement.Generate, last, on
// characters it has just spawned — so it starts from nothing every time and the same seed, config and
// position always give the same people (and their saved memories find them again after a regenerate).
//
// Beds go in order: special characters first, then everyone else; whoever shares a dwelling is family.
// A job archetype is handed out only where a spot of its post exists, an errand-only one where both ends of
// its chore exist, and lifestyles fill towards the culture's shares. A character whose prefab has a profile in the
// culture is drawn from the archetypes that profile suits, when any of them is usable here. Guards (a standing duty, no post) are
// handed out first, up to the culture's patrol share, so a settlement has pairs rather than one stray.
// Values only — the component stack is the character prefab's own.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents
{
    public static class ResidentAssignment
    {
        private const int FriendsEach = 2;
        // Below this many residents a settlement has no patrol: a pair would be a quarter of its people.
        private const int SmallestPatrolledSettlement = 8;

        /// <summary>A character Generate placed: its body, and the archetype a special character was given (else null).</summary>
        public readonly struct Newcomer
        {
            public readonly GameObject body;
            public readonly ResidentArchetype archetype;
            public readonly bool special;
            /// <summary>The character prefab it was spawned from: the key of its culture profile.</summary>
            public readonly GameObject prefab;

            public Newcomer(GameObject body, ResidentArchetype archetype, bool special, GameObject prefab = null) =>
                (this.body, this.archetype, this.special, this.prefab) = (body, archetype, special, prefab);
        }

        public static void Assign(Settlement settlement, SettlementCulture culture, IReadOnlyList<Newcomer> newcomers,
                                  IReadOnlyList<Dwelling> dwellings)
        {
            int seed = settlement.Seed;
            List<ResidentArchetype> archetypes = UsableArchetypes(settlement, culture, seed);
            if (archetypes.Count == 0)
                Debug.LogWarning($"[Settlement] {settlement.name}: {culture.name} has no archetype this settlement can use " +
                                 "(a job archetype needs a spot of its post); residents get none.", culture);

            var beds = new List<Dwelling>();
            foreach (Dwelling dwelling in dwellings)
                for (int b = 0; b < dwelling.Beds; b++) beds.Add(dwelling);

            var names = new Queue<string>(culture.names.Where(n => !string.IsNullOrEmpty(n)).OrderBy(n => Hash(seed, n)));
            var roster = new List<Resident>(newcomers.Count);
            foreach (Newcomer newcomer in newcomers)
            {
                if (!newcomer.body.TryGetComponent(out Resident resident))
                {
                    Debug.LogError($"[Settlement] {settlement.name}: {newcomer.body.name} has no Resident — its prefab lacks the " +
                                   "resident stack. Generate from the Settlement inspector, which adds it.", newcomer.body);
                    continue;
                }

                resident.index = roster.Count;
                resident.settlement = settlement;
                resident.archetype = newcomer.archetype != null ? newcomer.archetype
                    : PickArchetype(roster, Suited(archetypes, culture.SuitsOf(newcomer.prefab)), culture, newcomers.Count);
                // A special character keeps the name its prefab gives it; everyone else is named by the culture.
                if (!newcomer.special || string.IsNullOrEmpty(resident.displayName))
                    resident.displayName = names.Count > 0 ? names.Dequeue() : string.Empty;
                resident.seed = (int)Hash(seed, resident.displayName + resident.index);
                resident.home = resident.index < beds.Count ? beds[resident.index] : null;
                resident.campPosition = newcomer.body.transform.position;
                roster.Add(resident);
            }

            foreach (Resident resident in roster)
            {
                resident.bonds = Bonds(resident, roster, seed);
#if UNITY_EDITOR
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(resident);
#endif
            }
        }

        // Job archetypes only where a spot of their post was generated, in a seeded order per settlement.
        private static List<ResidentArchetype> UsableArchetypes(Settlement settlement, SettlementCulture culture, int seed)
        {
            var jobs = new HashSet<SpotUse>();
            var errands = new HashSet<SpotUse>();
            Transform generated = settlement.GeneratedRoot;
            if (generated != null)
                foreach (SettlementSpot spot in generated.GetComponentsInChildren<SettlementSpot>())
                {
                    if (spot.Use == null) continue;
                    if (spot.Use.role == SpotRole.Work) jobs.Add(spot.Use);
                    else if (spot.Use.role == SpotRole.Errand) errands.Add(spot.Use);
                }

            return culture.archetypes
                .Where(a => a != null && IsUsable(a, jobs, errands))
                .Distinct()
                .OrderBy(a => Hash(seed, a.name))
                .ToList();
        }

        // A job needs a spot of its post; a standing duty needs nothing; an errand-only archetype needs both ends of its chore.
        private static bool IsUsable(ResidentArchetype a, HashSet<SpotUse> jobs, HashSet<SpotUse> errands)
        {
            if (a.duty != ResidentDuty.None) return true;
            if (a.Lifestyle == Lifestyle.Stationed) return jobs.Contains(a.post);
            return a.chore == null || (a.chore.IsComplete && errands.Contains(a.chore.source) && errands.Contains(a.chore.target));
        }

        // The usable archetypes a character's profile suits, in the settlement's order; all of them without a usable match.
        private static List<ResidentArchetype> Suited(List<ResidentArchetype> usable, IReadOnlyCollection<ResidentArchetype> suits)
        {
            if (suits == null || suits.Count == 0) return usable;
            List<ResidentArchetype> suited = usable.Where(a => suits.Contains(a)).ToList();
            return suited.Count > 0 ? suited : usable;
        }

        /// <summary>Guards for a settlement of <paramref name="population"/>: the patrol share rounded down to whole pairs.</summary>
        public static int PatrolWanted(SettlementCulture culture, int population)
        {
            if (culture.patrolShare <= 0f || population < SmallestPatrolledSettlement) return 0;

            int wanted = Mathf.RoundToInt(population * culture.patrolShare);
            return Mathf.Max(2, wanted - wanted % 2);
        }

        /// <summary>
        /// A guard while the patrol is short of its share; else the usable archetype of the lifestyle furthest below
        /// its share, least used first.
        /// </summary>
        private static ResidentArchetype PickArchetype(List<Resident> roster, List<ResidentArchetype> archetypes, SettlementCulture culture, int population)
        {
            if (archetypes.Count == 0) return null;

            var assigned = roster.Where(r => r.archetype != null).Select(r => r.archetype).ToList();
            List<ResidentArchetype> guards = archetypes.Where(a => a.duty == ResidentDuty.Patrol).ToList();
            if (guards.Count > 0 && assigned.Count(a => a.duty == ResidentDuty.Patrol) < PatrolWanted(culture, population))
                return guards.OrderBy(a => assigned.Count(x => x == a)).First();

            List<ResidentArchetype> ordinary = archetypes.Where(a => a.duty == ResidentDuty.None).ToList();
            if (ordinary.Count == 0) return null;

            int people = roster.Count + 1;
            return new[] { Lifestyle.Stationed, Lifestyle.Roamer, Lifestyle.Outrider }
                .Where(l => ordinary.Any(a => a.Lifestyle == l))
                .OrderByDescending(l => culture.ShareOf(l) * people - assigned.Count(a => a.Lifestyle == l && a.duty == ResidentDuty.None))
                .SelectMany(l => ordinary.Where(a => a.Lifestyle == l).OrderBy(a => assigned.Count(x => x == a)))
                .First();
        }

        /// <summary>Family = same dwelling, coworkers = same post, then friends by seed among the rest.</summary>
        private static ResidentBond[] Bonds(Resident r, List<Resident> roster, int seed)
        {
            var bonds = new List<ResidentBond>();
            SpotUse post = r.archetype != null ? r.archetype.post : null;
            ResidentDuty duty = r.archetype != null ? r.archetype.duty : ResidentDuty.None;
            foreach (Resident o in roster.Where(o => o != r))
            {
                if (r.home != null && o.home == r.home) bonds.Add(new ResidentBond { other = o.index, kind = BondKind.Family });
                else if (post != null && o.archetype != null && o.archetype.post == post) bonds.Add(new ResidentBond { other = o.index, kind = BondKind.Coworker });
                else if (duty != ResidentDuty.None && o.archetype != null && o.archetype.duty == duty) bonds.Add(new ResidentBond { other = o.index, kind = BondKind.Coworker });
            }
            var friends = roster.Where(o => o != r && bonds.All(b => b.other != o.index))
                .OrderBy(o => Hash(seed, $"{Mathf.Min(r.index, o.index)}:{Mathf.Max(r.index, o.index)}")).Take(FriendsEach);
            bonds.AddRange(friends.Select(o => new ResidentBond { other = o.index, kind = BondKind.Friend }));
            return bonds.ToArray();
        }

        private static uint Hash(int seed, string key) => LineTable.IdOf($"{seed}:{key}");
    }
}
