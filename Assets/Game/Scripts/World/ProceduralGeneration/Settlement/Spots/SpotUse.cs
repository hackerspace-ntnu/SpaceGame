// What a SettlementSpot is FOR: the forge's anvil, a seat at the hearth, a patch of shade, the stall
// counter. Spots on building and decoration prefabs name one of these, and everything a resident does
// there follows from it: whether it is a job (and which archetypes work it), when it must stay manned,
// and the loop the body holds while it is there. A new kind of place is a new asset, never new code.
using UnityEngine;
using UnityEngine.Serialization;
using SpaceGame.Presentation;

namespace SpaceGame.World
{
    /// <summary>How the day planner uses a spot.</summary>
    public enum SpotRole : byte
    {
        /// <summary>A job: the archetypes whose post this is work here in their work hours.</summary>
        Work,
        /// <summary>Where the evening gathers — hearth seats. Also a free-time spot.</summary>
        Gathering,
        /// <summary>Free time: a seat, shade, a view, a customer's place at a stall.</summary>
        Leisure,
        /// <summary>A stop on an errand — a well, a plant, a stack of goods. Shared, never booked, not a place to idle.</summary>
        Errand,
    }

    [CreateAssetMenu(menuName = "SpaceGame/Settlement/Spot Use", fileName = "SpotUse")]
    public sealed class SpotUse : ScriptableObject
    {
        [Tooltip("What residents call the place in speech: the forge, the stall.")]
        public string displayName;

        [Tooltip("Work: a job the archetypes posted here hold. Gathering: the evening hearth. Leisure: anywhere to spend free time. Errand: a stop on a chore, used by any number of residents.")]
        public SpotRole role = SpotRole.Work;

        [Tooltip("Looping cue held while a resident is at the spot: work, sit, listen. Empty = stand.")]
        [FormerlySerializedAs("workCue")]
        public CharacterCue holdCue;

        [Tooltip("Work only. Never left empty in daytime: breaks are staggered so one worker always stays.")]
        public bool alwaysManned;

        [Tooltip("Work only. One worker is planned onto the night shift.")]
        public bool nightManned;

        [Tooltip("Work only. A post no NavMesh walks to — a tower deck up a ladder. Its worker is put there and taken down again " +
                 "while nobody is looking, and takes no breaks in between.")]
        public bool elevated;
    }
}
