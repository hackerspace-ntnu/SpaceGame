using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Destination that returns the initiator to the exterior position recorded by
/// InteriorManager when they entered the current interior. Pair this with a
/// VolumeTransitionTrigger inside an interior scene to make a "walk-out" exit.
/// </summary>
[CreateAssetMenu(fileName = "Destination_ExitInterior", menuName = "Scene Management/Destinations/Exit Interior")]
public class ExitInteriorDestination : SceneDestination
{
    [Tooltip("Hard cap on how long Apply() will wait for the initiator to leave the interior scene.")]
    [SerializeField] private float timeoutSeconds = 15f;

    public override bool IsValid()
    {
        return InteriorManager.Instance != null;
    }

    public override IEnumerator Apply(GameObject initiator)
    {
        if (!IsValid() || initiator == null) yield break;

        Scene before = initiator.scene;
        InteriorManager.Instance.ExitInterior(initiator);

        float deadline = Time.unscaledTime + Mathf.Max(1f, timeoutSeconds);
        while (initiator != null && initiator.scene == before)
        {
            if (Time.unscaledTime >= deadline)
            {
                Debug.LogError(
                    $"[ExitInteriorDestination] Timed out after {timeoutSeconds:0.0}s waiting for " +
                    $"initiator '{initiator.name}' to leave '{before.name}'. " +
                    "Common cause: no return-info on file (player wasn't entered via InteriorManager). " +
                    "Letting the transition complete to unblock the orchestrator.");
                yield break;
            }
            yield return null;
        }
    }
}
