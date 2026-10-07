using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.World
{
    /// <summary>
    /// Marks a hand-placed fixture as part of the mutable world, so <c>SaveablePolicy</c> gives it a
    /// <c>SaveableEntity</c> and its savers (the gear wall's contents, in the colony interiors) are
    /// collected and restored.
    ///
    /// <para>
    /// Exists because <c>WallInventory</c> deliberately does not implement
    /// <see cref="IPersistentEntity"/> itself: the lander nests a wall under its own root entity, and a
    /// wall that asked for an entity of its own would be recorded twice there. A fixture that stands
    /// alone in a scene carries this instead.
    /// </para>
    /// </summary>
    public sealed class PersistentFixture : MonoBehaviour, IPersistentEntity
    {
    }
}
