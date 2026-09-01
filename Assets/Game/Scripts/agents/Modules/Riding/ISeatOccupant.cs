// "Somebody is already sitting here, and they are not a player."
//
// MountModule tracks exactly one kind of rider — a PlayerMovement it seated itself — and knows
// nothing about NpcPassenger, on purpose (see that class for why the two share no code). The cost
// of that separation is that the seat reads as free when a caravan's nomad is in it: the player
// mounts, and the nomad rides on inside them.
//
// This is the one thing the seat has to be able to ask without knowing who is in it. It is not a
// rider abstraction and should not grow into one — a mount needs to evict, nothing more.
namespace SpaceGame.Agents
{
    public interface ISeatOccupant
    {
        /// <summary>Whether this occupant currently has somebody in the saddle.</summary>
        bool HasRider { get; }

        /// <summary>
        /// Get them out, because a player is taking the seat. Must be safe to call with no rider,
        /// and on a machine that is only watching — where the eviction belongs to the authority and
        /// arrives by replication like every other part of the arrangement.
        /// </summary>
        void VacateSeat();
    }
}
