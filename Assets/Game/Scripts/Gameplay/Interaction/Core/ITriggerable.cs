using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Anything that can be "fired" with an initiator GameObject — scene transitions, cutscenes,
    /// portals, anything in that family. Triggers (interactable, volume, scripted) don't need to
    /// know which one they're firing, they just call <see cref="Trigger"/>.
    ///
    /// The seam exists so the trigger components (click, walk-in-volume, scripted) stay
    /// content-agnostic and any new "do something to the player" action plugs in by implementing
    /// this interface — no new trigger class per action type.
    ///
    /// NETCODE: an implementation owns its own replication, exactly as <see cref="IInteractable"/>
    /// does. The triggers make no promise about which machine calls you, only about which machine's
    /// OBSERVATION was allowed to count, and the two are not the same question:
    ///
    ///   • <see cref="VolumeTrigger"/> fires on the SERVER only. Every player's body exists on every
    ///     machine, so a volume overlaps locally whenever anybody walks into it, and letting each
    ///     machine act on that means every client running the action for a player who is not theirs.
    ///     So the initiator you are handed may be a body this machine does not own — and the server
    ///     is where a decision about shared state belongs anyway.
    ///
    ///   • <see cref="InteractableTrigger"/> fires on the machine of the player who pressed the key,
    ///     which is always the OWNER of that body, because Interactor only runs on a locally driven
    ///     player. That machine may well be a client.
    ///
    /// Either way, getting the consequence onto the other machines is yours. The reference for what
    /// that looks like is PlayerInteriorTransit: an owner-gated RPC to the server, the state change
    /// applied there, and only the one machine that has to redraw told about it.
    /// </summary>
    public interface ITriggerable
    {
        /// <summary>True if the action can run right now for this initiator.</summary>
        bool CanTrigger(GameObject initiator);

        /// <summary>
        /// Fire the action for the given initiator. Returns the coroutine driving it, or null
        /// if the action declined to start (busy, invalid initiator, missing config, …).
        /// </summary>
        Coroutine Trigger(GameObject initiator);
    }
}
