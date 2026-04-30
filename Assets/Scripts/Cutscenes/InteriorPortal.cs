using System.Collections;
using UnityEngine;

// Standalone "go somewhere" interactable.
// Drop on a door / portal / pad. Configure:
//   • Cutscene transitionCutscene — drag whatever cutscene you want (or none).
//   • Destination — either an InteriorScene asset (full additive scene load) or a
//     same-scene anchor id (just teleport within the loaded world).
//   • fadeAroundTransition — black fade around the teleport for a clean cut.
//
// Click it (player Interactor raycast) → cutscene plays (if any) → fade-out →
// teleport → fade-in. The whole post-cutscene chain runs on LetterboxOverlay
// (DontDestroyOnLoad) so it survives interior-scene unloads.
public class InteriorPortal : MonoBehaviour, IInteractable
{
    public enum DestinationKind
    {
        InteriorScene,
        SameSceneAnchor,
    }

    [Header("Transition")]
    [Tooltip("Optional — drag any Cutscene component. If null, transition is just the fade + teleport.")]
    [SerializeField] private Cutscene transitionCutscene;

    [Tooltip("Black-fade around the teleport so the player doesn't see the seam.")]
    [SerializeField] private bool fadeAroundTransition = true;

    [SerializeField] private float fadeOut = 0.25f;
    [SerializeField] private float fadeHold = 0.4f;
    [SerializeField] private float fadeIn = 0.35f;

    [Header("Destination")]
    [SerializeField] private DestinationKind destinationKind = DestinationKind.InteriorScene;

    [Tooltip("Used when destinationKind = InteriorScene. The scene is loaded additively and the player is moved to its spawn anchor.")]
    [SerializeField] private InteriorScene targetInterior;

    [Tooltip("Used when destinationKind = SameSceneAnchor. Looks up the InteriorAnchor with this id in any loaded scene and teleports the player there.")]
    [SerializeField] private string anchorId;

    [Header("Interactable")]
    [SerializeField] private bool playOnce;

    private bool busy;
    private bool fired;

    public bool CanInteract()
    {
        if (busy || (playOnce && fired)) return false;
        switch (destinationKind)
        {
            case DestinationKind.InteriorScene:    return targetInterior != null && InteriorManager.Instance != null;
            case DestinationKind.SameSceneAnchor:  return !string.IsNullOrEmpty(anchorId);
            default: return false;
        }
    }

    public void Interact(Interactor interactor)
    {
        if (!CanInteract() || interactor == null) return;
        StartCoroutine(RunPortal(interactor));
    }

    private IEnumerator RunPortal(Interactor interactor)
    {
        busy = true;
        try
        {
            // 1. Optional cutscene.
            if (transitionCutscene != null)
                yield return CutsceneRunner.PlayAndAwait(transitionCutscene);

            // 2. Fade + teleport. The fade chain runs on LetterboxOverlay (DontDestroyOnLoad)
            //    so it survives the interior scene load that may unload us. We still wait
            //    for it here so `busy` doesn't clear until the whole transition is done —
            //    that prevents a double-click from re-firing mid-fade.
            GameObject player = interactor.gameObject;
            DestinationKind kind = destinationKind;
            InteriorScene interior = targetInterior;
            string aid = anchorId;

            System.Action doTeleport = () =>
            {
                if (kind == DestinationKind.InteriorScene)
                {
                    if (InteriorManager.Instance != null && interior != null)
                        InteriorManager.Instance.EnterInterior(player, interior);
                }
                else
                {
                    var anchor = InteriorAnchor.FindAnywhere(aid);
                    if (anchor != null) anchor.TeleportPlayer(player);
                    else Debug.LogWarning($"[InteriorPortal] No InteriorAnchor with id '{aid}' found in any loaded scene.", this);
                }
            };

            if (fadeAroundTransition)
                yield return LetterboxOverlay.Instance.FadeOutInAround(doTeleport, fadeOut, fadeHold, fadeIn);
            else
                doTeleport();

            // Only mark `fired` after the whole transition succeeded. (If we were destroyed
            // mid-await — interior unload — control never reaches here, but the GameObject
            // is gone anyway so playOnce semantics are moot.)
            fired = true;
        }
        finally
        {
            // If we survived the transition, clear busy. If we were destroyed (interior
            // load took us with it), this never runs but it doesn't matter — the
            // GameObject is gone.
            busy = false;
        }
    }
}
