// A carrier whose seat is in the air — the NPC craft. A rider killed in it falls, so EntityLootTable holds
// that body's loot until the body is down (LootAwaitingGround) rather than dropping it at the kill point.
namespace SpaceGame.Agents
{
    public interface IAirborneCarrier { }
}
