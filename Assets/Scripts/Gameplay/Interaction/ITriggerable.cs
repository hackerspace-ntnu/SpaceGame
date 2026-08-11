using UnityEngine;

/// <summary>
/// Anything that can be "fired" with an initiator GameObject — scene transitions, cutscenes,
/// portals, anything in that family. Triggers (interactable, volume, scripted) don't need to
/// know which one they're firing, they just call <see cref="Trigger"/>.
///
/// The seam exists so the trigger components (click, walk-in-volume, scripted) stay
/// content-agnostic and any new "do something to the player" action plugs in by implementing
/// this interface — no new trigger class per action type.
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
