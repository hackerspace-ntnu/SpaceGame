using UnityEngine;

/// <summary>
/// Place on a door inside an interior scene. On interact, fades to black, returns the
/// player to where they came from in the exterior, fades back.
///
/// The fade sequence is run on LetterboxOverlay (DontDestroyOnLoad) rather than on this
/// component — because ExitInterior unloads the interior scene, which destroys this
/// GameObject mid-coroutine and would leave the screen stuck black if the fade-back
/// step ran here.
/// </summary>
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
