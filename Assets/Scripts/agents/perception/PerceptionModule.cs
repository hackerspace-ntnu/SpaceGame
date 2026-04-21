// Line-of-sight and field-of-view gate for entity targeting.
// Other modules call CanSee(target) before acting. Fully optional — remove it and modules
// revert to radius-only detection. Emits noise when target is spotted (for alert system).
// Also supports a "last seen" position used by SearchModule.
using UnityEngine;
using FMODUnity;

public class PerceptionModule : MonoBehaviour
{
    [Header("Field of View")]
    [SerializeField] private float fieldOfViewAngle = 110f;
    [Tooltip("Extra FOV added while the agent is moving. Simulates wider peripheral awareness when walking.")]
    [SerializeField] private float movingFovBonus = 40f;
    [SerializeField] private Transform eyeTransform;

    [Header("Line of Sight")]
    [SerializeField] private LayerMask occlusionLayers;
    [Tooltip("Tag of objects that block line of sight (e.g. walls, terrain).")]
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

    private NoiseEmitter noiseEmitter;
    private Vector3 prevForward;
    private Vector3 prevPosition;
    private bool isMoving;

    private void Awake()
    {
        noiseEmitter = GetComponent<NoiseEmitter>();
        prevPosition = transform.position;
        prevForward = transform.forward;
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
        prevForward = GetForward();
        prevPosition = transform.position;
    }

    // Call this from ChaseModule / other targeting modules instead of raw distance checks.
    public bool CanSee(Transform target)
    {
        if (!target)
            return false;

        Vector3 origin = eyeTransform ? eyeTransform.position : transform.position + Vector3.up * eyeHeight;
        Vector3 toTarget = target.position - origin;

        // FOV check — widen by the angular sweep this frame (covers mid-turn arcs) and by movingFovBonus while walking.
        Vector3 currentForward = GetForward();
        float sweep = Vector3.Angle(prevForward, currentForward);
        float effectiveFov = fieldOfViewAngle + sweep + (isMoving ? movingFovBonus : 0f);
        if (Vector3.Angle(currentForward, toTarget) > effectiveFov * 0.5f)
            return false;

        // Line-of-sight raycast
        if (Physics.Raycast(origin, toTarget.normalized, out RaycastHit hit, toTarget.magnitude, occlusionLayers))
        {
            // Something occluded — check if it's the target itself
            if (hit.transform != target && !hit.transform.IsChildOf(target))
                return false;
        }

        // Visible — update memory
        LastKnownPosition = target.position;
        HasLastKnownPosition = true;
        TimeSinceLastSeen = 0f;

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

    private Vector3 GetForward() => eyeTransform ? eyeTransform.forward : transform.forward;

    private void OnValidate()
    {
        fieldOfViewAngle = Mathf.Clamp(fieldOfViewAngle, 1f, 360f);
        eyeHeight = Mathf.Max(0f, eyeHeight);
        memoryDuration = Mathf.Max(0f, memoryDuration);
        spotNoiseRadius = Mathf.Max(0f, spotNoiseRadius);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = eyeTransform ? eyeTransform.position : transform.position + Vector3.up * eyeHeight;
        Vector3 forward = GetForward();
        float half = fieldOfViewAngle * 0.5f;

        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(origin, Quaternion.Euler(0, half, 0) * forward * 5f);
        Gizmos.DrawRay(origin, Quaternion.Euler(0, -half, 0) * forward * 5f);
    }
}
