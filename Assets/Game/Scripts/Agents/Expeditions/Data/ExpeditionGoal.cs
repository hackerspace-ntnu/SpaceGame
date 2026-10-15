// One kind of band a settlement sends out: who goes (role slots), what it does on the road (stages, rolled
// per band from the ranges here) and what each role carries (kits). The rotation draws among a profile's
// goals by weight. A new kind of trip is a new asset; a new kind of stage is new code.
using System;
using UnityEngine;

namespace SpaceGame.Agents.Expeditions
{
    /// <summary>How many members of one role a band takes.</summary>
    [Serializable]
    public struct RoleSlot
    {
        [Tooltip("The role that fills the slot, and the kit its members draw. Name exactly one role.")]
        public ExpeditionRole role;

        [Tooltip("Members the band must have here, or it is not raised.")]
        [Min(0)] public int min;

        [Tooltip("Members the band takes here when there are residents to spare.")]
        [Min(0)] public int max;

        [Tooltip("Any adult may fill the slot, whatever its roles. Its members still draw the kit of Role; leave Role None " +
                 "for the kit listed under None.")]
        public bool any;

        [Tooltip("Prefer residents with Role; when too few of them are home and rested, any adult fills the rest of Min. " +
                 "A stand-in keeps its own archetype and draws Role's kit and plays Role on the road. Never on a Warrior slot.")]
        public bool fillFromAnyAdult;
    }

    /// <summary>One stage of the trip, before the per-band rolls.</summary>
    [Serializable]
    public struct StageSpec
    {
        public StageKind kind;

        [Tooltip("GAME minutes the stage may last, rolled per band between x and y. 0 = no limit: it ends on its own " +
                 "(arrival, dawn, the last waypoint).")]
        public Vector2 minutesRange;

        [Tooltip("How many waypoints a Search visits, rolled per band between x and y. Unused by the other kinds.")]
        public Vector2Int countRange;
    }

    /// <summary>The kit one role draws on this goal.</summary>
    [Serializable]
    public struct RoleKit
    {
        public ExpeditionRole role;
        public ExpeditionKit kit;
    }

    [CreateAssetMenu(menuName = "SpaceGame/Expeditions/Goal", fileName = "Goal")]
    public sealed class ExpeditionGoal : ScriptableObject
    {
        [Tooltip("Saved in band records and the settlement's last-goal memory: never rename it once shipped.")]
        public string id;

        [Tooltip("What residents call the trip in speech: scouting, the hunt.")]
        public string displayName;

        [Tooltip("Relative chance in the rotation's draw among the goals that are possible right now.")]
        [Min(0f)] public float weight = 1f;

        [Tooltip("Who goes. Warrior slots take only Warriors and must hold at least two; the band is 3 to 10 strong.")]
        public RoleSlot[] slots = Array.Empty<RoleSlot>();

        [Tooltip("The trip, in order. The last stage must be ReturnHome.")]
        public StageSpec[] stages = Array.Empty<StageSpec>();

        [Tooltip("The kit each slot's role draws. Every role a slot names needs a row.")]
        public RoleKit[] kits = Array.Empty<RoleKit>();

        /// <summary>The row of <see cref="kits"/> listed under exactly <paramref name="role"/>; -1 when there is none.</summary>
        public int KitIndexOf(ExpeditionRole role) => Array.FindIndex(kits, k => k.role == role);
    }
}
