using UnityEngine;

/// <summary>
/// Compatibility shim. "Exit interior with a fade" door, pre-dating the unified
/// SceneTransition stack. For new content, prefer:
///
///   SceneTransition + InteractableTrigger + FadeToBlackEffect + ExitInteriorDestination
///
/// The fade sequence still runs on LetterboxOverlay (DontDestroyOnLoad) so this
/// component being destroyed mid-coroutine doesn't strand a black screen.
/// </summary>
[System.Obsolete("Use SceneTransition + ExitInteriorDestination + FadeToBlackEffect instead.")]
public class InteriorExit : MonoBehaviour, IInteractable
{
    public bool CanInteract() => InteriorManager.Instance != null;

    public void Interact(Interactor interactor)
    {
        if (!CanInteract() || interactor == null) return;

        // Capture the player reference before we hand off — `interactor` may be the player.
        GameObject player = interactor.gameObject;
        LetterboxOverlay.Instance.FadeOutInAround(
            duringBlack: () => InteriorManager.Instance.ExitInterior(player),
            fadeOutDur: 0.25f,
            holdDur: 0.4f,
            fadeInDur: 0.35f);
    }
}
