using UnityEngine;

/// <summary>
/// Drop on a GameObject with a trigger Collider that also has any <see cref="ITriggerable"/>.
/// Fires when a player or AI agent enters the volume. Identifies initiators by:
///   • Player — GameObject tagged "Player".
///   • AI agent — has an AgentController in self or parents.
/// Both checks are togglable. After firing, a cooldown re-arms so the same agent stepping
/// back through the volume doesn't immediately re-trigger.
/// </summary>
[RequireComponent(typeof(Collider))]
[AddComponentMenu("Triggers/Volume Trigger")]
public class VolumeTrigger : MonoBehaviour
{
    [Tooltip("Optional. If unset, the first ITriggerable on this GameObject is used.")]
    [SerializeField] private MonoBehaviour triggerableOverride;

    [SerializeField] private bool triggerForPlayers = true;
    [SerializeField] private bool triggerForAgents = true;
    [Tooltip("Seconds before this volume can fire again after a successful trigger.")]
    [SerializeField] private float rearmCooldown = 1f;

    private ITriggerable cached;
    private float armedAt;

    private void Awake()
    {
        cached = ResolveTriggerable();
        var col = GetComponent<Collider>();
        if (!col.isTrigger)
            Debug.LogWarning($"[VolumeTrigger] Collider on '{name}' should be set to isTrigger.", this);
    }

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (Time.time < armedAt) return;
        var t = cached ?? ResolveTriggerable();
        if (t == null) return;

        GameObject candidate = ResolveInitiatorRoot(other);
        if (candidate == null) return;
        if (!IsEligible(candidate)) return;
        if (!t.CanTrigger(candidate)) return;

        if (t.Trigger(candidate) != null)
            armedAt = Time.time + rearmCooldown;
    }

    private ITriggerable ResolveTriggerable()
    {
        if (triggerableOverride is ITriggerable explicitT) return explicitT;
        return GetComponent<ITriggerable>();
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
