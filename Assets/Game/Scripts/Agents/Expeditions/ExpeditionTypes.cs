// The shared vocabulary of settlement expeditions. Phases and stage kinds are saved as ints in the band
// record, so both enums are APPEND ONLY: a member inserted or reordered loads every old save as the
// wrong phase or stage. Roles are flags on archetypes and roster rows, so they are append only too.
using System;

namespace SpaceGame.Agents.Expeditions
{
    /// <summary>
    /// What a resident can be on a band. An archetype may have several (a hunter is a Warrior and a Hunter).
    /// "Any adult" is not a role: it is a slot rule (<see cref="RoleSlot.any"/>).
    /// </summary>
    [Flags]
    public enum ExpeditionRole
    {
        None = 0,
        /// <summary>Front line, first on watch; the leader is one of them. Warrior slots take only Warriors.</summary>
        Warrior = 1,
        /// <summary>Tracks, catches and kills prey.</summary>
        Hunter = 2,
        /// <summary>Finds waypoints and sites.</summary>
        Scout = 4,
        /// <summary>Carries the band's loads and leads captured animals.</summary>
        Bearer = 8,
        /// <summary>Repairs and raises structures.</summary>
        Builder = 16,
        /// <summary>Tends the wounded.</summary>
        Healer = 32,
    }

    /// <summary>Where a band is in its life. Saved as an int: append only.</summary>
    public enum ExpeditionPhase
    {
        /// <summary>Chosen the evening before; still at home.</summary>
        Announced,
        /// <summary>The departure ceremony is running.</summary>
        Ceremony,
        /// <summary>Mustered and walking out; not yet handed off.</summary>
        Departing,
        /// <summary>On the road, as stand-ins or a folded group.</summary>
        Out,
        /// <summary>Back at the hand-off point, walking in.</summary>
        Returning,
        /// <summary>Home; the record is finished.</summary>
        Home,
        /// <summary>Never coming back.</summary>
        Lost,
    }

    /// <summary>One step of a band's trip. Saved as an int: append only.</summary>
    public enum StageKind
    {
        /// <summary>Walk to a target; ends on arrival.</summary>
        Travel,
        /// <summary>Visit a rolled number of waypoints around the target; ends when they are done or time runs out.</summary>
        Search,
        /// <summary>Stop for the night and stand guard in place; ends at dawn.</summary>
        Halt,
        /// <summary>Walk back to the hand-off point; ends on arrival.</summary>
        ReturnHome,
    }

    /// <summary>The one key type for a resident, wherever it came from. Saved: never change a prefix.</summary>
    public static class ResidentKey
    {
        private const string AuthoredPrefix = "r:";

        /// <summary>The key of a resident placed by Generate, from its roster index.</summary>
        public static string ForAuthored(int index) => AuthoredPrefix + index;
    }
}
