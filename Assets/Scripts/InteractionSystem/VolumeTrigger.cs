using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drop on a GameObject with a trigger Collider that also has any <see cref="ITriggerable"/>.
/// Fires when a player or AI agent enters the volume. Identifies initiators by:
///   • Player — GameObject tagged "Player".
///   • AI agent — has an AgentController in self or parents.
/// Both checks are togglable. After firing, a cooldown re-arms so the same agent stepping
/// back through the volume doesn't immediately re-trigger.
///
/// Eligibility does NOT require the initiator to share the trigger's scene — world streaming
/// migrates players/agents between exterior chunk sub-scenes constantly. The one exclusion is
/// a player who is currently inside an interior (asked via <see cref="InteriorManager"/>),
/// because interiors load additively and overlap the exterior in world space.
///
/// Re-entry cooldown: after a player triggers this volume and goes through an interior, they
/// cannot re-trigger THIS SAME volume for <see cref="reentryCooldown"/> seconds after they
/// return to the exterior. The window is measured from the exit (not the original fire),
/// because real time keeps passing while the player is inside the interior — a cooldown
/// started at entry would have expired by the time they came back out.
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
    [Tooltip("After a player returns from an interior, how long (seconds) THIS volume refuses to " +
             "re-fire for them. Measured from the moment they step back into the exterior, so it " +
             "is a real post-exit cooldown regardless of how long they spent inside.")]
    [SerializeField] private float reentryCooldown = 4f;

    private ITriggerable cached;
    private float armedAt;
    private float lastStayLog;

    // Per-(player, this-volume) re-entry state. Static so it survives the volume being
    // unloaded/reloaded by world streaming while the player is off in an interior.
    private class ReentryState
    {
        // True once this player triggers this volume, until they are next confirmed back
        // in the exterior. Names a "transition is in flight through this volume" — NOT a
        // literal inside-interior reading (the interior load is async; the player is not
        // inside synchronously on the frame Trigger() is called).
        public bool TransitionPending;
        public float CooldownUntil;      // unscaled-time before which this volume must not re-fire
    }
    private static readonly Dictionary<(int playerId, int volumeId), ReentryState> s_reentry = new();

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

    private void OnDestroy()
    {
        // Drop this volume's re-entry entries so the static map doesn't accumulate
        // stale (player, volume) pairs as volumes stream in and out.
        int volumeId = GetInstanceID();
        var stale = new List<(int, int)>();
        foreach (var key in s_reentry.Keys)
            if (key.volumeId == volumeId) stale.Add(key);
        foreach (var key in stale) s_reentry.Remove(key);
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

        // Advance the inside→outside tracking every time we see the candidate, so the
        // post-exit cooldown can start the instant they return even if we never get a
        // clean OnTriggerEnter (teleport-back lands them mid-volume → only Stay fires).
        UpdateReentryTracking(candidate);

        if (!IsEligible(candidate))
        {
            if (source == "Enter")
            {
                bool insideInterior = InteriorManager.Instance != null && InteriorManager.Instance.IsInsideInterior(candidate);
                Debug.Log($"[VolumeTrigger] '{name}' {source} rejected: '{candidate.name}' not eligible " +
                          $"(tag={candidate.tag}, hasAgent={candidate.GetComponentInParent<AgentController>() != null}, " +
                          $"insideInterior={insideInterior}, triggerForPlayers={triggerForPlayers}, triggerForAgents={triggerForAgents})", this);
            }
            return;
        }

        // Per-(player, this-volume) re-entry cooldown — refuse to re-fire the same
        // entrance for a while after they came back out through it.
        var state = GetReentryState(candidate);
        if (Time.unscaledTime < state.CooldownUntil)
        {
            if (source == "Enter")
                Debug.Log($"[VolumeTrigger] '{name}' {source} rejected: re-entry cooldown " +
                          $"({state.CooldownUntil - Time.unscaledTime:0.00}s remaining for '{candidate.name}')", this);
            return;
        }

        if (!t.CanTrigger(candidate))
        {
            // SceneTransition prints its own diagnostic when it denies — no log here.
            return;
        }

        if (t.Trigger(candidate) != null)
        {
            armedAt = Time.time + rearmCooldown;
            // A transition is now in flight through this volume. When the player is next
            // confirmed back outside (not inside an interior), the post-exit cooldown arms.
            state.TransitionPending = true;
        }
    }

    private ReentryState GetReentryState(GameObject player)
    {
        var key = (player.GetInstanceID(), GetInstanceID());
        if (!s_reentry.TryGetValue(key, out var state))
        {
            state = new ReentryState();
            s_reentry[key] = state;
        }
        return state;
    }

    // Arm the post-exit re-entry cooldown the first time we see this player back in the
    // exterior after they triggered a transition through this volume.
    //
    // Why "back outside" and not "exited interior": some destinations are same-scene
    // teleports with no interior at all. The condition we want is simply "the transition
    // this volume kicked off has resolved and the player is here again, eligible". Using
    // IsInsideInterior == false covers both: interior round-trips (false once they return)
    // and non-interior transitions (false the whole time, so the cooldown arms on the
    // next frame the player overlaps this volume — still a real post-fire guard).
    private void UpdateReentryTracking(GameObject player)
    {
        var state = GetReentryState(player);
        if (!state.TransitionPending) return;

        bool insideInterior = InteriorManager.Instance != null && InteriorManager.Instance.IsInsideInterior(player);
        if (insideInterior) return;   // still away — wait for the round-trip to finish

        // Player is back (or never left, for same-scene destinations). Arm the cooldown,
        // measured from now, and clear the pending flag so it only arms once per trip.
        state.CooldownUntil = Time.unscaledTime + Mathf.Max(0f, reentryCooldown);
        state.TransitionPending = false;
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
        // We must reject a player who has walked off into an interior: interior scenes load
        // additively beside the exterior, so an entrance volume in the persistent scene
        // physically overlaps the world position of a player who is no longer "here", and
        // OnTriggerStay would keep re-firing the entrance against them.
        //
        // The check used to be `go.scene != gameObject.scene`. That is WRONG: world streaming
        // migrates the player between exterior chunk sub-scenes, so the player's scene almost
        // never equals the trigger's persistent scene during normal play — the raw scene check
        // silently rejected every legitimate trigger. Ask InteriorManager directly instead.
        if (InteriorManager.Instance != null && InteriorManager.Instance.IsInsideInterior(go))
            return false;

        if (triggerForPlayers && go.CompareTag("Player")) return true;
        if (triggerForAgents && go.GetComponentInParent<AgentController>() != null) return true;
        return false;
    }
}
