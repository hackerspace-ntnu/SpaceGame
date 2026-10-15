using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.World;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Which settlement the lander's intercepted signal leads to. Pure, so the rule can be asserted
    /// from a list of towns without a world.
    ///
    /// <para>
    /// <b>The nearest settlement the crew can actually walk to and be welcome at.</b> A town
    /// qualifies when it has a faction, that faction is not <see cref="FactionDefinition.wandering"/>
    /// (the Striders' walking city and the Sky Tribe's flying city are not there when you arrive), and
    /// the relationship table does not call it hostile to the crew (the Clankers). A town closer to the
    /// ship than <c>minimumDistance</c>, measured to its centre, is the one the wreck came down beside
    /// and is skipped: the signal is a reason to go somewhere.
    /// </para>
    /// <para>
    /// <b>Deterministic.</b> Distance is flat (the heightmap's vertical is noise to a walker), and
    /// equal distances fall to the lower id, ordinal, so every machine and every reload of the same
    /// world picks the same town from the same position. It is chosen once and then saved; this only
    /// runs again in a world that has never chosen.
    /// </para>
    /// </summary>
    public static class SignalDestinationRule
    {
        /// <summary>
        /// The nearest qualifying town in <paramref name="towns"/> to <paramref name="from"/>. False when
        /// none qualifies.
        /// </summary>
        public static bool TryChoose(IReadOnlyList<WorldSiteCatalog.TownEntry> towns, Vector3 from, float minimumDistance,
                                     FactionDefinition crew, FactionRelationshipTable relationships,
                                     out WorldSiteCatalog.TownEntry chosen)
        {
            chosen = default;
            bool found = false;
            float best = float.PositiveInfinity;

            for (int i = 0; towns != null && i < towns.Count; i++)
            {
                WorldSiteCatalog.TownEntry town = towns[i];
                if (!Welcomes(town.faction, crew, relationships)) continue;

                float distance = FlatDistance(from, town.position);
                if (distance < minimumDistance) continue;

                bool nearer = distance < best ||
                              (Mathf.Approximately(distance, best) &&
                               string.CompareOrdinal(town.id, chosen.id) < 0);
                if (!nearer) continue;

                chosen = town;
                best = distance;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// A faction whose town the signal may lead the crew to: it exists, stays where it is, and is not
        /// hostile to them. Hostility is the table's (its rows, then each faction's default stance), the
        /// same answer every agent in the game acts on.
        /// </summary>
        public static bool Welcomes(FactionDefinition town, FactionDefinition crew, FactionRelationshipTable relationships)
        {
            if (town == null || town.wandering) return false;
            return relationships == null || !relationships.IsHostile(town, crew);
        }

        /// <summary>Metres between two points over the ground, ignoring height.</summary>
        public static float FlatDistance(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// The compass bearing from one point to another, degrees clockwise from world +Z (north), 0-360 —
        /// the convention the GPS page's heading reads in.
        /// </summary>
        public static float Bearing(Vector3 from, Vector3 to)
        {
            float degrees = Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
            return Mathf.Repeat(degrees, 360f);
        }

        /// <summary>A town as the signal's destination.</summary>
        public static SignalDestination ToDestination(in WorldSiteCatalog.TownEntry town) =>
            SignalDestination.To(town.id, town.faction != null ? town.faction.factionName : string.Empty,
                                 town.position, town.radius);

        /// <summary>A town in one line, for the log line that records the choice.</summary>
        public static string Describe(in WorldSiteCatalog.TownEntry town) =>
            $"{town.name} ({(town.faction != null ? town.faction.factionName : "no faction")}) at " +
            $"({town.position.x:0}, {town.position.z:0}) [{town.id}]";
    }
}
