// What the expedition director saves: one SettlementState per settlement that runs bands, and one
// ExpeditionRecord per band from the evening it is announced until it is home or lost. Plain public fields,
// written by the save serializer as they are (it carries the Vector3 converter, so a position is a plain
// Vector3 field, as in NpcGroup.Record). Save format: fields are appended at the end, never renamed.
//
// Records are classes, not structs: the director keeps them in lists and changes them in place (a stage
// ends, a member is hurt), and a struct would be silently changed on a copy.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents.Expeditions
{
    /// <summary>One settlement's rotation, kept by the director so it runs while the settlement's chunk is unloaded.</summary>
    [Serializable]
    public sealed class SettlementState
    {
        /// <summary>The settlement's identity (its SaveableEntity id), never its scene or position.</summary>
        public string settlementId;

        /// <summary>Its residents as last seen: the baked catalog's snapshot, refreshed whenever the chunk loads.</summary>
        public RosterEntry[] roster = Array.Empty<RosterEntry>();

        /// <summary>Bands raised so far. Each band's seed is drawn from it, so a reload raises the same band again.</summary>
        public int rotation;

        /// <summary>By resident key: the first game day (DayNightCycle.Day) the resident may be picked again after its band came home.</summary>
        public Dictionary<string, int> restUntilDay = new Dictionary<string, int>();

        /// <summary>The goal of the last band raised; the draw gives it less weight. Empty before the first band.</summary>
        public string lastGoalId = string.Empty;
    }

    /// <summary>One stage of a band's trip, rolled from its goal's <see cref="StageSpec"/> when the band is raised.</summary>
    [Serializable]
    public struct StageRecord
    {
        public const float NoLimit = -1f;

        public StageKind kind;

        /// <summary>GAME minutes the stage may last; <see cref="NoLimit"/> when it ends only on its own (arrival, dawn, the last waypoint).</summary>
        public float minutes;

        /// <summary>Waypoints a Search visits; 0 for the other kinds.</summary>
        public int waypoints;
    }

    /// <summary>One resident on a band.</summary>
    [Serializable]
    public sealed class MemberRecord
    {
        /// <summary>The resident's key (<see cref="ResidentKey"/>).</summary>
        public string residentKey;

        /// <summary>Its archetype's index in its culture's <c>archetypes</c> list, so a stand-in looks and acts like it.</summary>
        public int archetypeIndex = -1;

        /// <summary>The row of its goal's <c>kits</c> it carries; -1 when the goal lists none for its slot.</summary>
        public int kitIndex = -1;

        /// <summary>Health as a share of the maximum, read back whenever the band folds.</summary>
        public float health01 = 1f;

        /// <summary>Died on the road. The dead are never spawned again and never come home.</summary>
        public bool dead;

        /// <summary>Leads the band: hails, answers for it and sounds the horn.</summary>
        public bool isLeader;

        /// <summary>
        /// The role it plays on the road: its slot's role, set for every member (None for a slot any adult fills with no
        /// role named). A stand-in for a role it does not hold (<see cref="RoleSlot.fillFromAnyAdult"/>) keeps its own
        /// archetype, so behaviour on the road keys on this, never on the archetype's roles. None in a record saved
        /// before the field existed.
        /// </summary>
        public ExpeditionRole role;
    }

    /// <summary>One band, from the evening it is announced until it is home or lost.</summary>
    [Serializable]
    public sealed class ExpeditionRecord
    {
        /// <summary>Not started: the band has not been handed off yet.</summary>
        public const int NotStarted = -1;

        public string id;
        public string settlementId;

        /// <summary>The <see cref="ExpeditionGoal.id"/> the band runs.</summary>
        public string goalId;

        /// <summary>Every roll the band makes is drawn from it (System.Random), so a reload cannot farm outcomes.</summary>
        public int seed;

        public ExpeditionPhase phase;

        /// <summary>The trip, rolled when the band was raised. The last stage is ReturnHome.</summary>
        public StageRecord[] stages = Array.Empty<StageRecord>();

        /// <summary>The stage the band is on; <see cref="NotStarted"/> before the trip begins, <c>stages.Length</c> once it has ended.</summary>
        public int stageIndex = NotStarted;

        /// <summary>GAME minutes left on the current stage's timer; <see cref="StageRecord.NoLimit"/> when it has none.</summary>
        public float stageMinutesLeft = StageRecord.NoLimit;

        /// <summary>Waypoints of the current Search already reached.</summary>
        public int waypointsDone;

        public MemberRecord[] members = Array.Empty<MemberRecord>();

        /// <summary>The game day (DayNightCycle.Day) the band leaves.</summary>
        public int departDay;

        /// <summary>The <c>NpcGroup</c> that moves the band once it is handed off; empty before.</summary>
        public string groupId = string.Empty;

        /// <summary>The road point the band is handed off at on the way out, and walks back in from.</summary>
        public Vector3 handoffPoint;

        /// <summary>
        /// Where the current stage is headed: a Travel's destination, or the centre a Search circles. Chosen when the
        /// stage begins and saved, so a reload walks on to the same place instead of rolling a new one.
        /// </summary>
        public Vector3 target;
    }
}
