using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    /// <summary>One worn gauntlet, as something an item-use module can fire (NpcGauntletUseModule).</summary>
    public sealed class WornGauntletUser : INpcItemUser
    {
        private readonly EntityBodyEquipment body;
        private readonly BodySlot slot;

        public WornGauntletUser(EntityBodyEquipment body, BodySlot slot)
        {
            this.body = body;
            this.slot = slot;
        }

        /// <summary>Worn, usable, and opted in on its asset (InventoryItem.npcUsable).</summary>
        public bool IsReady => body != null && body.IsNpcUsable(slot);

        public Vector3 FireOrigin => body.FireOrigin(slot);
        public void AimAt(Vector3 worldPoint) => body.AimAt(worldPoint);
        public void ClearAim() => body.ClearAim();
        public bool TryUseAt(Vector3 worldAimPoint) => body.TryUseWornAt(slot, worldAimPoint);
        public bool TryUseForward() => TryUseAt(FireOrigin + body.transform.forward * NpcItemFire.ForwardReach);
        public bool TryUseOnSelf() => TryUseAt(body.transform.position + Vector3.down * NpcItemFire.SelfAimDrop);
    }
}
