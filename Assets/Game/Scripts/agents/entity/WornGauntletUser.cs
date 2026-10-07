using System;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Agents
{
    /// <summary>
    /// One worn gauntlet, as something an item-use module can fire (NpcGauntletUseModule). Whether it
    /// may be fired is cached and refreshed on <see cref="EntityBodyEquipment.WornChanged"/>, because a
    /// module asks every tick and the answer only changes when the gear does.
    /// </summary>
    public sealed class WornGauntletUser : INpcItemUser, IDisposable
    {
        private readonly EntityBodyEquipment body;
        private readonly BodySlot slot;
        private bool ready;

        /// <summary>Raised when <see cref="IsReady"/> flips.</summary>
        public event Action ReadyChanged;

        public WornGauntletUser(EntityBodyEquipment body, BodySlot slot)
        {
            this.body = body;
            this.slot = slot;
            ready = body.IsNpcUsable(slot);
            body.WornChanged += OnWornChanged;
        }

        /// <summary>Worn, usable, and opted in on its asset (InventoryItem.npcUsable).</summary>
        public bool IsReady => ready && body != null;

        public Vector3 FireOrigin => body.FireOrigin(slot);
        public void AimAt(Vector3 worldPoint) => body.AimAt(worldPoint);
        public void ClearAim() => body.ClearAim();
        public bool TryUseAt(Vector3 worldAimPoint) => body.TryUseWornAt(slot, worldAimPoint);
        public bool TryUseForward() => TryUseAt(FireOrigin + body.transform.forward * NpcItemFire.ForwardReach);
        public bool TryUseOnSelf() => TryUseAt(body.transform.position + Vector3.down * NpcItemFire.SelfAimDrop);

        public void Dispose()
        {
            if (body != null) body.WornChanged -= OnWornChanged;
        }

        private void OnWornChanged(BodySlot changed)
        {
            if (changed != slot) return;
            bool now = body.IsNpcUsable(slot);
            if (now == ready) return;
            ready = now;
            ReadyChanged?.Invoke();
        }
    }
}
