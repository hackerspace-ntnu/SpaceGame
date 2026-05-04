using UnityEngine;

/// <summary>
/// Drop on the same GameObject as a SceneTransition to fire it when a moving agent
/// enters a trigger collider. Identifies eligible initiators by:
///   • Player — GameObject tagged "Player" (matches existing convention).
///   • AI agent — has an AgentController in self or parents (universal "moving agent" marker).
///
/// Both checks are togglable per volume; default is to accept either. After firing,
/// the trigger goes on a short cooldown so the same agent stepping back through it
/// (e.g. after exit) doesn't immediately re-enter.
/// </summary>
[RequireComponent(typeof(SceneTransition))]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("Scene Management/Triggers/Volume Transition Trigger")]
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
        if (transition == null || transition.IsBusy) return;

        GameObject candidate = ResolveInitiatorRoot(other);
        if (candidate == null) return;
        if (!IsEligible(candidate)) return;

        if (transition.Trigger(candidate) != null)
            armedAt = Time.time + rearmCooldown;
    }

    private static GameObject ResolveInitiatorRoot(Collider other)
    {
        // Compound colliders on agents/players hang under a rigidbody root — climb to it
        // so we identify the entity, not the limb.
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
