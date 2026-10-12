using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Where an NPC holder is aiming, for items that would otherwise ask a player's AimProvider. Read by
    /// UsableItem.HolderAimRay.
    /// </summary>
    public interface INpcAim
    {
        bool HasAimPoint { get; }
        Vector3 AimPoint { get; }
    }
}
