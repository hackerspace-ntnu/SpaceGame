// The shared vocabulary of the resident system. Every planner, matcher, line row and inspector speaks
// in these words, so they live in one file and are byte-sized: Activity and the prop index travel in
// ResidentPresence's NetworkVariables, the rest are parsed out of the line TSV by name.
using System;

namespace SpaceGame.Agents.Residents
{
    /// <summary>What a resident is doing. Plan segments use Sleep..Trip; presence adds the rest.</summary>
    public enum Activity : byte
    {
        None,
        Sleep,
        Work,
        Break,
        Stroll,
        Hearth,
        Trip,
        Walking,
        Talking,
        Sitting,
        Stalking,
        Sheltering,
        /// <summary>Carrying out an errand: the rounds of a <see cref="ChoreDefinition"/>.</summary>
        Chore,
        /// <summary>Walking the settlement's perimeter on guard.</summary>
        Patrol,
        /// <summary>Wandering from place to place with no booked seat to go to.</summary>
        Amble,
        /// <summary>On a ladder between a street and a deck: shows the climb pose, holds no place.</summary>
        Climbing,
    }

    /// <summary>The kind of a settlement place a plan segment points at.</summary>
    public enum PlaceKind : byte
    {
        /// <summary>A dwelling's door: home, where a resident goes indoors to sleep.</summary>
        Door,
        /// <summary>A Work spot: a job post.</summary>
        Post,
        /// <summary>A Gathering spot: a seat at the evening hearth.</summary>
        Hearth,
        /// <summary>A Leisure spot: anywhere to spend free time.</summary>
        Stroll,
        /// <summary>A point outside the settlement an outrider walks to.</summary>
        Trip,
        /// <summary>Home for a resident with no dwelling: it sleeps here in the open.</summary>
        Camp,
        /// <summary>An Errand spot — a well, a plant, a pile — that any number of residents may use at once.</summary>
        Errand,
        /// <summary>A point on the settlement's perimeter ring, gathered where plans are built (like trip points).</summary>
        Patrol,
    }

    /// <summary>A standing assignment that replaces a lifestyle's ordinary work hours.</summary>
    public enum ResidentDuty : byte
    {
        None,
        /// <summary>Walks the perimeter, in a pair, through the working day.</summary>
        Patrol,
    }

    /// <summary>What an outrider leaves the settlement for. Flags on the archetype, rows in the tuning table.</summary>
    [Flags]
    public enum TripKind
    {
        None = 0,
        Hunt = 1,
        Scout = 2,
        Forage = 4,
        Salvage = 8,
        Water = 16,
    }

    /// <summary>Derived from <see cref="ResidentArchetype"/>: post → Stationed, trips → Outrider, else Roamer.</summary>
    public enum Lifestyle : byte
    {
        Stationed,
        Roamer,
        Outrider,
    }

    /// <summary>How a resident regards one player right now. Pure output of <see cref="Attitude"/>.</summary>
    public enum Stance : byte
    {
        Unsure,
        Curious,
        Asking,
        Protective,
        Afraid,
        Warm,
        Cold,
        Hostile,
    }

    /// <summary>The four buckets a line row's <c>stance</c> column may name instead of one stance.</summary>
    public enum StanceFamily : byte
    {
        Friendly,
        Neutral,
        Wary,
        Hostile,
    }

    /// <summary>
    /// The single most important thing a resident notices about a player, by priority. <see cref="Jostling"/>
    /// is never noticed, only named: it is the cause of a band a shove raised (ResidentVoice.CauseOf).
    /// </summary>
    public enum Observation : byte
    {
        None,
        KinHarmed,
        Hitting,
        Menacing,
        ArmedHeld,
        Gauntlet,
        Curio,
        Sprinting,
        Approaching,
        Jostling,
        /// <summary>Not read off a player: names a defence in lines about it (thanks, gossip, rumour).</summary>
        Defending,
    }

    /// <summary>What a line is about. The <c>topic</c> column of the line TSV.</summary>
    public enum Topic : byte
    {
        Greeting,
        Farewell,
        Remark,
        Warning,
        Work,
        Gossip,
        Ambition,
        Need,
        Reply,
        Refusal,
        Alarm,
        Grief,
        /// <summary>Passing on news of an attack that just happened (Rumours); the subject is the victim.</summary>
        Rumour,
    }

    /// <summary>The shape of a line, which is what a reply in a chained conversation answers.</summary>
    public enum Register : byte
    {
        Statement,
        Question,
        Exclamation,
        Complaint,
    }

    /// <summary>Why two residents are linked. Gossip and kin reactions follow bonds.</summary>
    public enum BondKind : byte
    {
        Family,
        Coworker,
        Friend,
    }

    /// <summary>
    /// A named deed a player did to (or for) a resident: what residents remember, retell and weigh in favor
    /// (<see cref="Favor"/>). Every kind but <see cref="Defended"/> is harm, and a grudge.
    /// </summary>
    public enum ActKind : byte
    {
        Hit,
        Threatened,
        HarmedKin,
        KilledKin,
        Killed,
        /// <summary>Hurt whoever was attacking the resident.</summary>
        Defended,
    }

    /// <summary>The one live deviation from the day plan a resident can carry.</summary>
    public enum OverrideKind : byte
    {
        None,
        Shelter,
        Approach,
        Scripted,
    }
}
