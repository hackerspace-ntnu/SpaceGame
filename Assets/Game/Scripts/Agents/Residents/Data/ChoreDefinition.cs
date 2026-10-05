// A chore is one repeating round of errands, described entirely by data: fetch something at a source
// spot, carry it to one or a few target spots, come back for more. Watering plants (well → each plant),
// hauling ore (pile → smelter), carrying goods between shops (stack → another stack) are all this one shape.
// The spots are SpotUse assets on building and decoration prefabs; a chore never names a position, so
// it works in every settlement that has the places and quietly does nothing in one that does not.
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.World;

namespace SpaceGame.Agents.Residents
{
    [CreateAssetMenu(menuName = "SpaceGame/Residents/Chore", fileName = "Chore")]
    public sealed class ChoreDefinition : ScriptableObject
    {
        [Tooltip("What residents call the errand in speech: watering, hauling.")]
        public string displayName;

        [Tooltip("Where each round starts and the carried thing is picked up: a well, an ore pile, a stack of goods.")]
        public SpotUse source;

        [Tooltip("Where it is taken. May be the same use as the source (shop to shop): a round then goes to a different spot.")]
        public SpotUse target;

        [Tooltip("Targets visited per round before going back to the source (inclusive range, seeded per round).")]
        public Vector2Int targetsPerRound = new Vector2Int(2, 4);

        [Tooltip("Held in the hand between pickup and the last delivery: a hand tool such as a bucket or a crate, drawn the way " +
                 "every held item is. Must be listed in the tuning's carryItems. Empty = nothing.")]
        public InventoryItem carried;

        [Tooltip("Carried for the whole chore, the empty bucket going back to the well included. Off: only between pickup and the last delivery.")]
        public bool carryThroughout;

        [Tooltip("Moves real props: a round fetches a prop resting at a source spot's PropRest and sets it down at a free rest " +
                 "of a target spot, carrying the prop's own item. Falls back to an ordinary round when there is none to move.")]
        public bool carriesProps;

        [Tooltip("REAL seconds spent at the source, filling or loading.")]
        public Vector2 sourceSeconds = new Vector2(4f, 7f);

        [Tooltip("REAL seconds spent at each target, pouring or setting down.")]
        public Vector2 targetSeconds = new Vector2(3f, 6f);

        public bool IsComplete => source != null && target != null;
    }
}
