// Line-of-sight and field-of-view gate for entity targeting.
// Other modules call CanSee(target) before acting. Fully optional — remove it and modules
// revert to radius-only detection. Emits noise when target is spotted (for alert system).
// Also supports a "last seen" position used by SearchModule.
//
// Authoritative perception API — other modules should route here instead of re-implementing
// FOV/LoS. Public entry points:
//   CanSee(target)                     — full FOV + LoS from the eye, updates memory
//   HasLineOfSight(target)             — LoS from the eye, no FOV, no memory update
//   HasLineOfSightFrom(origin, target) — LoS from an arbitrary origin (e.g. a weapon muzzle)
using UnityEngine;
using FMODUnity;

public class PerceptionModule : MonoBehaviour
{
    [Header("Field of View")]
    [SerializeField] private float fieldOfViewAngle = 110f;
    [Tooltip("Extra FOV added while the agent is moving. Keep at 0 for realistic perception — raise only if you want widened peripheral awareness while walking.")]
    [SerializeField] private float movingFovBonus = 0f;
    [Tooltip("Origin for LoS raycasts. Typically a head bone so vision starts from eye height. " +
             "NOTE: the FOV direction always comes from the agent's root transform — a skeletal bone's " +
             ".forward is its local +Z in world space, which for rigs imported from Blender points up " +
             "through the head or sideways, not where the character is facing.")]
    [SerializeField] private Transform eyeTransform;

    [Header("Line of Sight")]
    [SerializeField] private LayerMask occlusionLayers;
    [Tooltip("Fallback eye elevation when no eyeTransform is assigned.")]
    [SerializeField] private float eyeHeight = 1.6f;

    [Header("Memory")]
    [Tooltip("How long the entity remembers the last known position after losing sight.")]
    [SerializeField] private float memoryDuration = 5f;

    [Header("Noise on Spot")]
    [SerializeField] private bool emitNoiseOnSpot = true;
    [SerializeField] private float spotNoiseRadius = 12f;

    [Header("Audio")]
    [SerializeField] private bool playSpotSound = true;
    [SerializeField] private EventReference spotSound;

    public Vector3 LastKnownPosition { get; private set; }
    public bool HasLastKnownPosition { get; private set; }
    public float TimeSinceLastSeen { get; private set; }

    public Vector3 EyePosition => eyeTransform ? eyeTransform.position : transform.position + Vector3.up * eyeHeight;
    public float MemoryDuration => memoryDuration;

    private NoiseEmitter noiseEmitter;
    private Vector3 prevPosition;
    private bool isMoving;

    private void Awake()
    {
        noiseEmitter = GetComponent<NoiseEmitter>();
        prevPosition = transform.position;

        if (occlusionLayers == 0)
            Debug.LogWarning($"{name}: PerceptionModule.occlusionLayers is Nothing — agents will see through all geometry. Set the layer mask to walls/terrain.", this);
    }

    private void Update()
    {
        if (HasLastKnownPosition)
            TimeSinceLastSeen += Time.deltaTime;

        if (TimeSinceLastSeen > memoryDuration)
        {
            HasLastKnownPosition = false;
            TimeSinceLastSeen = 0f;
        }

        isMoving = (transform.position - prevPosition).sqrMagnitude > 0.0001f;
        prevPosition = transform.position;
    }

    // Full perception check: FOV + LoS from the eye. Updates last-known memory when visible.
    public bool CanSee(Transform target)
    {
        if (!target)
            return false;

        Vector3 origin = EyePosition;

        // FOV check — horizontalized so vertical offset (tall/short targets) doesn't exclude them,
        // and the direction comes from the root transform, not eyeTransform.forward.
        Vector3 flatForward = FlattenHorizontal(GetForward());
        Vector3 flatToTarget = FlattenHorizontal(target.position - origin);
        if (flatForward.sqrMagnitude < 1e-6f || flatToTarget.sqrMagnitude < 1e-6f)
            return false;

        // Effective FOV: base cone, optionally widened while moving. No per-frame sweep bonus —
        // it produced erratic detection during fast turns.
        float effectiveFov = fieldOfViewAngle + (isMoving ? movingFovBonus : 0f);
        if (Vector3.Angle(flatForward, flatToTarget) > effectiveFov * 0.5f)
            return false;

        if (!HasLineOfSightFrom(origin, target))
            return false;

        // Visible — update memory
        LastKnownPosition = target.position;
        HasLastKnownPosition = true;
        TimeSinceLastSeen = 0f;

        return true;
    }

    // LoS from the eye only — no FOV, no memory update. Use for passive "could we shoot them if we aimed?" checks.
    public bool HasLineOfSight(Transform target) => HasLineOfSightFrom(EyePosition, target);

    // LoS from an arbitrary origin (e.g. a weapon muzzle). Ignores hits on self and the target itself.
    public bool HasLineOfSightFrom(Vector3 origin, Transform target)
    {
        if (!target)
            return false;

        Vector3 toTarget = target.position - origin;
        float distance = toTarget.magnitude;
        if (distance < 1e-4f)
            return true;

        Vector3 dir = toTarget / distance;
        RaycastHit[] hits = Physics.RaycastAll(origin, dir, distance, occlusionLayers);
        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].transform;
            if (t == transform || t.IsChildOf(transform))
                continue;
            if (t == target || t.IsChildOf(target))
                return true;
            return false;
        }
        return true;
    }

    // Call when a target is spotted for the first time to alert nearby allies.
    public void NotifySpotted(Transform target)
    {
        if (emitNoiseOnSpot && noiseEmitter)
            noiseEmitter.Emit(NoiseType.Alert, spotNoiseRadius);

        if (playSpotSound && !spotSound.IsNull)
            RuntimeManager.PlayOneShot(spotSound, transform.position);
    }

    private Vector3 GetForward() => transform.forward;

    private static Vector3 FlattenHorizontal(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private void OnValidate()
    {
        fieldOfViewAngle = Mathf.Clamp(fieldOfViewAngle, 1f, 360f);
        eyeHeight = Mathf.Max(0f, eyeHeight);
        memoryDuration = Mathf.Max(0f, memoryDuration);
        spotNoiseRadius = Mathf.Max(0f, spotNoiseRadius);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = EyePosition;
        Vector3 forward = FlattenHorizontal(GetForward());
        if (forward.sqrMagnitude < 1e-6f)
            return;
        forward.Normalize();
        float half = fieldOfViewAngle * 0.5f;

        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(origin, Quaternion.Euler(0, half, 0) * forward * 5f);
        Gizmos.DrawRay(origin, Quaternion.Euler(0, -half, 0) * forward * 5f);
    }
}
