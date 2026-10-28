// A carrier whose seat is in the air — the NPC craft. A rider killed in it falls, so EntityLootTable holds
// that body's loot until the body is down (LootAwaitingGround) rather than dropping it at the kill point,
// and simulation distance never puts it to sleep (SimulationDistance.md).
using UnityEngine;

namespace SpaceGame.Agents
{
    public interface IAirborneCarrier { }

    public static class AirborneSeat
    {
        /// <summary>Is <paramref name="body"/> seated under an <see cref="IAirborneCarrier"/>?</summary>
        public static bool IsSeatedAloft(Transform body) =>
            body.parent != null && body.parent.GetComponentInParent<IAirborneCarrier>() != null;
    }
}
