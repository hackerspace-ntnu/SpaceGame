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
    private float lastStayLog;

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
        Debug.Log($"[VolumeTrigger] '{name}' OnTriggerEnter from '{other?.name}'", this);
        TryFire(other, "Enter");
    }

    private void OnTriggerExit(Collider other)
    {
        Debug.Log($"[VolumeTrigger] '{name}' OnTriggerExit from '{other?.name}'", this);
    }

    // Also poll while overlapping — a destination that teleports the player back to the
    // exterior often lands them *inside* this volume (e.g. cave exit teleports to the
    // saved entry position). Unity does not fire OnTriggerEnter for instantaneous
    // teleports, so without this the player can never re-enter. CanTrigger + the cross-
    // transition lockout in SceneTransition still prevent immediate re-fire.
    private void OnTriggerStay(Collider other)
    {
        if (Time.time - lastStayLog > 1f)
        {
            Debug.Log($"[VolumeTrigger] '{name}' OnTriggerStay from '{other?.name}' (armedAt-now={armedAt - Time.time:0.00})", this);
            lastStayLog = Time.time;
        }
        TryFire(other, "Stay");
    }

    private void TryFire(Collider other, string source)
    {
        if (Time.time < armedAt)
        {
            if (source == "Enter")
                Debug.Log($"[VolumeTrigger] '{name}' {source} rejected: armed-cooldown ({armedAt - Time.time:0.00}s remaining)", this);
            return;
        }
        var t = cached ?? ResolveTriggerable();
        if (t == null)
        {
            if (source == "Enter")
                Debug.LogWarning($"[VolumeTrigger] '{name}' has no ITriggerable", this);
            return;
        }

        GameObject candidate = ResolveInitiatorRoot(other);
        if (candidate == null) return;
        if (source == "Enter")
            Debug.Log($"[VolumeTrigger] '{name}' {source}: resolved candidate='{candidate.name}' tag='{candidate.tag}' (other='{other.name}', otherTag='{other.tag}', attachedRb={(other.attachedRigidbody != null ? other.attachedRigidbody.name : "<none>")})", this);
        if (!IsEligible(candidate))
        {
            if (source == "Enter")
                Debug.Log($"[VolumeTrigger] '{name}' {source} rejected: '{candidate.name}' not eligible (tag={candidate.tag}, hasAgent={candidate.GetComponentInParent<AgentController>() != null})", this);
            return;
        }
        if (!t.CanTrigger(candidate))
        {
            // SceneTransition prints its own diagnostic when it denies — no log here.
            return;
        }

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
