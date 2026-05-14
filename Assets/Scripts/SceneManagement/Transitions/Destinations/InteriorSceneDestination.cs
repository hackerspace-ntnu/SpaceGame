using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Destination that loads an InteriorScene additively (via InteriorManager) and
/// places the initiator at the scene's spawn anchor. Two initiators going to the
/// same InteriorScene share the loaded scene — refcounting lives in InteriorManager.
/// </summary>
[CreateAssetMenu(fileName = "Destination_Interior_", menuName = "Scene Management/Destinations/Interior Scene")]
public class InteriorSceneDestination : SceneDestination
{
    [SerializeField] private InteriorScene interior;

    [Tooltip("Hard cap on how long Apply() will wait for the initiator's scene to change. " +
             "If exceeded, the transition completes and a warning is logged — better than a stuck door.")]
    [SerializeField] private float timeoutSeconds = 15f;

    public InteriorScene Interior => interior;

    public override bool IsValid()
    {
        if (interior == null || string.IsNullOrEmpty(interior.SceneName)) return false;
        if (InteriorManager.Instance == null) return false;
        return true;
    }

    public override IEnumerator Apply(GameObject initiator)
    {
        if (!IsValid() || initiator == null) yield break;

        Scene before = initiator.scene;
        InteriorManager.Instance.EnterInterior(initiator, interior);

        // InteriorManager finishes the move by calling MoveGameObjectToScene +
        // TeleportPlayer once the additive load completes. Wait for that to flip.
        // Cap with a timeout so a missing scene / failed RPC / netcode replication
        // delay doesn't deadlock the transition (which would leave the door stuck busy
        // and any fade frozen on screen).
        float deadline = Time.unscaledTime + Mathf.Max(1f, timeoutSeconds);
        while (initiator != null && initiator.scene == before)
        {
            if (Time.unscaledTime >= deadline)
            {
                Debug.LogError(
                    $"[InteriorSceneDestination] Timed out after {timeoutSeconds:0.0}s waiting for " +
                    $"initiator '{initiator.name}' to land in '{interior.SceneName}'. " +
                    "Common causes: scene not in Build Settings, network RPC dropped, or " +
                    "MoveGameObjectToScene didn't replicate. Letting the transition complete " +
                    "to unblock the orchestrator.", interior);
                yield break;
            }
            yield return null;
        }
    }
}
