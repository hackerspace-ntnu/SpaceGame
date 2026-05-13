using UnityEngine;

/// <summary>
/// Compatibility shim: legacy volume trigger specific to SceneTransition. Prefer the
/// generic <see cref="VolumeTrigger"/> on the same GameObject — it works with any
/// <see cref="ITriggerable"/>. Kept so existing prefabs/scenes that serialize this
/// component keep working.
/// </summary>
[System.Obsolete("Use VolumeTrigger (which auto-discovers any ITriggerable on the same GameObject).")]
[RequireComponent(typeof(SceneTransition))]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("Scene Management/Triggers/Volume Transition Trigger (legacy)")]
public class VolumeTransitionTrigger : MonoBehaviour
{
    [SerializeField] private bool triggerForPlayers = true;
    [SerializeField] private bool triggerForAgents = true;
    [Tooltip("Seconds before this volume can fire again after a successful trigger.")]
    [SerializeField] private float rearmCooldown = 1f;

    private SceneTransition transition;
    private float armedAt;

    private void Awake()
    {
        transition = GetComponent<SceneTransition>();
        var col = GetComponent<Collider>();
        if (!col.isTrigger)
            Debug.LogWarning($"[VolumeTransitionTrigger] Collider on '{name}' should be set to isTrigger.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (Time.time < armedAt) return;
        if (transition == null) return;

        GameObject candidate = ResolveInitiatorRoot(other);
        if (candidate == null) return;
        if (!IsEligible(candidate)) return;
        if (!transition.CanTrigger(candidate)) return;

        if (transition.Trigger(candidate) != null)
            armedAt = Time.time + rearmCooldown;
    }

    private static GameObject ResolveInitiatorRoot(Collider other)
    {
        if (other.attachedRigidbody != null) return other.attachedRigidbody.gameObject;
        return other.gameObject;
    }

    private bool IsEligible(GameObject go)
    {
        if (triggerForPlayers && go.CompareTag("Player")) return true;
        if (triggerForAgents && go.GetComponentInParent<AgentController>() != null) return true;
        return false;
    }
}
