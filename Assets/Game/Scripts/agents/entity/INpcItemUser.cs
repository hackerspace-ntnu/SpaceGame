using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>
    /// What an item-use module drives: the NPC's hand (EntityEquipmentController) or one worn gauntlet
    /// (WornGauntletUser). The module decides WHEN; this decides how.
    /// </summary>
    public interface INpcItemUser
    {
        Vector3 FireOrigin { get; }
        void AimAt(Vector3 worldPoint);
        void ClearAim();
        bool TryUseAt(Vector3 worldAimPoint);
        bool TryUseForward();
        bool TryUseOnSelf();
    }
}
