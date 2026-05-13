using System.Collections;
using UnityEngine;

/// <summary>
/// Compatibility shim. Pre-unified "go somewhere" interactable that bundles cutscene +
/// fade + teleport in one component. For new content, use the unified stack:
///
///   • <see cref="SceneTransition"/> as the orchestrator
///   • <see cref="InteractableTrigger"/> or <see cref="VolumeTrigger"/> to fire it
///   • <see cref="WalkThroughCutsceneEffect"/> + <see cref="FadeToBlackEffect"/> as effects
///   • <see cref="InteriorSceneDestination"/> or <see cref="SameSceneAnchorDestination"/>
///
/// Kept so existing prefabs that serialize InteriorPortal keep working until they're
/// migrated. The behaviour is unchanged.
/// </summary>
[System.Obsolete("Use SceneTransition + a trigger + effects + a destination instead. " +
                 "SameSceneAnchorDestination covers the SameSceneAnchor mode.")]
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

    [SerializeField] private InteriorScene targetInterior;
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
            GameObject player = interactor.gameObject;

            if (transitionCutscene != null)
                yield return CutsceneRunner.PlayAndAwait(transitionCutscene, player);

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

            fired = true;
        }
        finally
        {
            busy = false;
        }
    }
}
