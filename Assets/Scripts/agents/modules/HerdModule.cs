// Keeps a herd moving together without overriding individual behaviours.
//
//   SHARED DESTINATION — the herd maintains one shared target. The first member to start
//   moving sets it; all others (idle or moving in a different direction) adopt a
//   spread point around it so the group converges on one area instead of scattering.
//
//   SEPARATION — nudges members apart when they get too close.
//
//   COHESION — biases each member's destination slightly toward the herd center.
//
// Combat, flee, and any reactive module are fully unaffected.
using System.Collections.Generic;
using UnityEngine;

public class HerdModule : BehaviourModuleBase
{
    [Header("Herd")]
    [Tooltip("Members with the same ID form one herd.")]
    [SerializeField] private string herdId = "default";

    [Header("Separation")]
    [Tooltip("Agents closer than this get pushed away.")]
    [SerializeField] private float separationRadius = 2f;
    [Tooltip("How strongly to push away from nearby members.")]
    [SerializeField] private float separationStrength = 0.8f;

    [Header("Cohesion")]
    [Tooltip("Radius within which other members count toward the herd center.")]
    [SerializeField] private float cohesionRadius = 14f;
    [Tooltip("How strongly to bias the current destination toward the herd center. Keep low (0.1–0.25).")]
    [Range(0f, 1f)]
    [SerializeField] private float cohesionStrength = 0.15f;

    [Header("Destination Sharing")]
    [Tooltip("Members within this range are considered part of the active group.")]
    [SerializeField] private float shareRadius = 20f;
    [Tooltip("Each member gets a random offset within this radius around the shared destination.")]
    [SerializeField] private float destinationSpread = 2.5f;

    // Never claims movement — works entirely as a side-effect.
    public override bool ClaimsMovement => false;

    // ── Static registry ───────────────────────────────────────────────────────
    private static readonly Dictionary<string, List<HerdModule>> s_herds = new();
    // One shared destination per herd ID. Null = no active target.
    private static readonly Dictionary<string, Vector3?> s_herdDestinations = new();

    private IMovementMotor motor;
    public IMovementMotor Motor => motor;

    private static void Register(HerdModule m)
    {
        if (!s_herds.TryGetValue(m.herdId, out var list))
        {
            list = new List<HerdModule>();
            s_herds[m.herdId] = list;
        }
        if (!list.Contains(m))
            list.Add(m);
    }

    private static void Unregister(HerdModule m)
    {
        if (s_herds.TryGetValue(m.herdId, out var list))
            list.Remove(m);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Reset() => SetPriorityDefault(ModulePriority.Fallback);

    private void Start()
    {
        var controller = GetComponent<AgentController>();
        if (controller != null)
            motor = controller.Motor;
        else
            Debug.LogWarning($"{name}: HerdModule requires an AgentController on the same GameObject.", this);
    }

    private void OnEnable()  => Register(this);
    private void OnDisable() => Unregister(this);

    public override string ModuleDescription =>
        "Keeps herd members moving together by sharing one destination across the group.\n\n" +
        "• The first member to move sets the shared destination; all others follow to a spread point around it.\n" +
        "• When the whole herd reaches the target the shared destination is cleared so the next move is fresh.\n\n" +
        "• herdId — string key grouping members (same ID = same herd)\n" +
        "• separationRadius — members closer than this are pushed apart\n" +
        "• separationStrength — push magnitude (applies even when idle)\n" +
        "• cohesionRadius — range within which members pull toward the center\n" +
        "• cohesionStrength — how strongly to bias toward center (0.1–0.25 recommended)\n" +
        "• shareRadius — range within which members participate in the shared destination\n" +
        "• destinationSpread — radius of random offset per member around the shared destination";

    // ── Tick ──────────────────────────────────────────────────────────────────
    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        if (motor == null)
            return null;

        if (!s_herds.TryGetValue(herdId, out var members) || members.Count < 2)
            return null;

        Vector3 myDestination = motor.CurrentDestination ?? Vector3.zero;
        bool iAmMoving = motor.CurrentDestination.HasValue;

        // If I just picked a new destination, register it as the herd's shared target.
        if (iAmMoving)
        {
            if (!s_herdDestinations.TryGetValue(herdId, out Vector3? current) || current == null)
                s_herdDestinations[herdId] = myDestination;
        }

        Vector3? sharedDestination = s_herdDestinations.TryGetValue(herdId, out var sd) ? sd : null;

        Vector3 centerSum = Vector3.zero;
        int cohesionCount = 0;
        Vector3 separationForce = Vector3.zero;
        int membersStillMoving = 0;

        foreach (var other in members)
        {
            if (other == this || !other.isActiveAndEnabled)
                continue;

            float dist = Vector3.Distance(context.Position, other.transform.position);

            // Separation: push away from members that are too close.
            if (dist < separationRadius && dist > 0.001f)
            {
                Vector3 away = context.Position - other.transform.position;
                away.y = 0f;
                separationForce += away.normalized * (1f - dist / separationRadius);
            }

            // Cohesion: accumulate herd center from nearby members.
            if (dist <= cohesionRadius)
            {
                centerSum += other.transform.position;
                cohesionCount++;
            }

            if (dist <= shareRadius)
            {
                // Sync: redirect members that are moving somewhere different to the shared destination.
                if (sharedDestination.HasValue && other.Motor != null)
                {
                    Vector3? otherDest = other.Motor.CurrentDestination;
                    bool otherIsMovingElsewhere = otherDest.HasValue &&
                        Vector3.Distance(otherDest.Value, sharedDestination.Value) > destinationSpread * 2f;
                    bool otherIsIdle = !otherDest.HasValue;

                    if (otherIsIdle || otherIsMovingElsewhere)
                    {
                        Vector2 offset2D = Random.insideUnitCircle * destinationSpread;
                        Vector3 spread = sharedDestination.Value + new Vector3(offset2D.x, 0f, offset2D.y);
                        other.Motor.SuggestDestination(spread);
                    }
                }

                if (other.Motor?.CurrentDestination.HasValue == true)
                    membersStillMoving++;
            }
        }

        // Clear shared destination once the whole group has settled.
        if (sharedDestination.HasValue && membersStillMoving == 0 && !iAmMoving)
            s_herdDestinations[herdId] = null;

        // Apply separation nudge (always, even when idle).
        if (separationForce.sqrMagnitude > 0.001f)
            motor.NudgeDestination(separationForce.normalized * separationStrength);

        // Cohesion nudge on my own destination.
        if (iAmMoving && cohesionCount > 0)
        {
            Vector3 herdCenter = centerSum / cohesionCount;
            Vector3 toCenter = herdCenter - context.Position;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 0.01f)
                motor.NudgeDestination(toCenter.normalized * (toCenter.magnitude * cohesionStrength));
        }

        return null;
    }

    // ── Validation ────────────────────────────────────────────────────────────
    protected override void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(herdId))
            herdId = "default";
        separationRadius = Mathf.Max(0.1f, separationRadius);
        separationStrength = Mathf.Max(0f, separationStrength);
        cohesionRadius = Mathf.Max(separationRadius, cohesionRadius);
        cohesionStrength = Mathf.Clamp01(cohesionStrength);
        shareRadius = Mathf.Max(cohesionRadius, shareRadius);
        destinationSpread = Mathf.Max(0f, destinationSpread);
    }
}
