using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// Bring an item back to a character and put it down at their feet — "return the acorn basket".
    /// Met while a loose copy lies within <see cref="handOverRadius"/> of the character; the
    /// <see cref="RewardStep"/> after it takes the item away.
    ///
    /// <para>
    /// Put down rather than handed over, because the world already has a replicated, saved way to put
    /// an item somewhere — dropping it — and no way to give one to a character. Before it is found the
    /// waypoint points at the item if it lies loose within <see cref="searchRadius"/> of the character;
    /// once somebody carries it, at the character.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Deliver Item Step")]
    public class DeliverItemStep : ObjectiveStep
    {
        [SerializeField] private InventoryItem item;

        [Tooltip("The QuestActor id of who it goes to.")]
        [SerializeField] private string actorId;

        [Tooltip("Where the waypoint points while the character's chunk is not loaded. Optional.")]
        [SerializeField] private string fallbackSiteId;

        [Tooltip("How close to the character it must be put down, metres.")]
        [SerializeField, Min(0.5f)] private float handOverRadius = 3f;

        [Tooltip("How far from the character a loose copy is looked for, to point the waypoint at it, metres.")]
        [SerializeField, Min(1f)] private float searchRadius = 150f;

        public InventoryItem Item => item;

        public override bool IsMet(ObjectiveWorld world) =>
            QuestActor.TryFind(actorId, out QuestActor actor)
            && world.TryFindLoose(item, actor.transform.position, handOverRadius, out _);

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            if (!QuestPlaces.TryLocate(actorId, fallbackSiteId, out Vector3 owner))
            {
                position = default;
                return false;
            }

            if (world.TryFindLoose(item, owner, searchRadius, out position)) return true;

            position = owner;
            return true;
        }

        /// <summary>A light over the item while it lies loose, so a basket in the undergrowth can be found.</summary>
        public override bool TryGetBeacon(ObjectiveWorld world, out Vector3 position)
        {
            position = default;
            return QuestPlaces.TryLocate(actorId, fallbackSiteId, out Vector3 owner)
                   && world.TryFindLoose(item, owner, searchRadius, out position);
        }
    }
}
