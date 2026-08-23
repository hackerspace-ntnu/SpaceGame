// The opt-out from the authority gate, for the modules that are not simulation at all.
//
// AgentController stops ticking modules on a machine that does not drive the entity, because
// deciding is exactly the thing that must happen in one place: two machines each picking a target,
// each pathing to it and each billing the same bite is the bug that gate exists to close.
//
// A handful of modules are not deciding anything. ChatterModule shows a popup to whoever is
// sitting at THIS machine, when THEIR player walks close enough to hear it. That is a local
// effect, it writes nothing another peer can observe, and it is wrong on exactly one machine if it
// only runs on the server — the one belonging to the player standing next to the NPC.
//
// So: mark those, and they keep ticking everywhere. The bar for the marker is absolute — a
// presentation module may not damage, spawn, consume, move the body, or write anything a peer can
// see. If in doubt it is not one, because the failure mode of a wrong answer here is silent
// multiplication and the failure mode of the cautious one is an effect that only the host sees.
namespace SpaceGame.Agents
{
    /// <summary>
    /// Implement alongside <see cref="IBehaviourModule"/> on a module whose whole job is local
    /// output — sound, popups, particles — so it keeps ticking on machines that only watch this
    /// agent.
    ///
    /// <para>
    /// A presentation module runs with a reduced <see cref="AgentContext"/> on those machines:
    /// there is no motor to read and no neighbour scan, so Velocity, IsImmobile,
    /// HasReachedDestination and the NearbyAgent arrays are all empty. Targeting and Goal are
    /// whatever the local copy happens to hold, which on a watching machine means "nothing" rather
    /// than "wrong". Anything that cannot cope with that is simulation and does not belong here.
    /// </para>
    /// <para>
    /// Its Tick must return null. The presentation pass discards MoveIntents by design — a
    /// watching machine has no business steering the body.
    /// </para>
    /// </summary>
    public interface IPresentationModule
    {
    }
}
