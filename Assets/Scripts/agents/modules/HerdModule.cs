// Distributes the highest-priority move intent across all herd members each frame.
//
//   MOVING — all members follow the broadcast destination, each offset to a unique
//   evenly-spaced slot on a circle around the target.
//
//   SETTLING — once all members have arrived, the herd fans out to evenly-spaced
//   positions around the group center. The broadcast clears only after every member
//   has reached their settle slot, letting lower-priority modules pick the next move.
//
//   REACTIVE — chase/flee intents (priority >= Reactive) bypass spread and settling
//   and are broadcast as-is so the whole herd reacts immediately.
//
// Combat and other reactive modules above Social priority are unaffected on the
// member that owns them — they win locally and broadcast to the rest.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class HerdModule : BehaviourModuleBase
{
    [Header("Herd")]
    [Tooltip("Members with the same ID form one herd.")]
    [SerializeField] private string herdId = "default";

    [Header("Spread")]
    [Tooltip("Radius of the circle members spread out on when settling.")]
    [SerializeField] private float settleRadius = 4f;
    [Tooltip("How close a member must be to its settle slot to count as arrived.")]
    [SerializeField] private float settleStopDistance = 0.6f;

    [Header("Combat Spread")]
    [Tooltip("Radius of the circle members spread to when a reactive intent (chase/flee) is broadcast. Prevents piling on the target.")]
    [SerializeField] private float combatSpread = 3.5f;

    [Header("Separation")]
    [Tooltip("Agents closer than this get nudged apart.")]
    [SerializeField] private float separationRadius = 2f;
    [Tooltip("How strongly to push away from nearby members.")]
    [SerializeField] private float separationStrength = 0.8f;

    public override bool ClaimsMovement => true;

    // ── Shared state per herd ─────────────────────────────────────────────────
    private struct BroadcastSlot
    {
        public int Priority;
        public MoveIntent Intent;
        public bool HasValue;
    }

    private enum HerdPhase { Idle, Moving, Settling }

    private struct HerdState
    {
        public HerdPhase Phase;
        public Vector3 Destination;
        public Vector3 SettleCenter;
    }

    private static readonly Dictionary<string, List<HerdModule>> s_herds = new();
    private static readonly Dictionary<string, BroadcastSlot> s_broadcast = new();
    private static readonly Dictionary<string, int> s_broadcastFrame = new();
    private static readonly Dictionary<string, HerdState> s_state = new();

    private IMovementMotor motor;

    // This member's assigned slot index on the settle circle — assigned once per settle phase.
    private int mySlotIndex = -1;
    private Vector3 mySlotPosition;
    private bool slotAssigned;

    // ── Registry ──────────────────────────────────────────────────────────────
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

    // Called by AgentController after it resolves a winning intent for this member.
    public void Publish(int priority, MoveIntent intent)
    {
        int frame = Time.frameCount;
        if (!s_broadcastFrame.TryGetValue(herdId, out int lastFrame) || lastFrame != frame)
        {
            s_broadcast[herdId] = default;
            s_broadcastFrame[herdId] = frame;
        }

        if (!s_broadcast.TryGetValue(herdId, out BroadcastSlot slot) ||
            !slot.HasValue || priority > slot.Priority)
        {
            s_broadcast[herdId] = new BroadcastSlot { Priority = priority, Intent = intent, HasValue = true };
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Reset() => SetPriorityDefault(ModulePriority.Social);

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
        "Distributes the highest-priority move intent across all herd members each frame.\n\n" +
        "• MOVING — all members move toward the broadcast destination, each to a unique evenly-spaced slot.\n" +
        "• SETTLING — once arrived, members fan out to evenly-spaced positions around the group center.\n" +
        "  The broadcast clears only after all members have settled, then lower-priority modules pick the next move.\n" +
        "• REACTIVE — chase/flee bypass spread entirely; the whole herd reacts immediately.\n\n" +
        "• herdId — string key grouping members into one herd\n" +
        "• settleRadius — radius of the circle members spread to when settling\n" +
        "• settleStopDistance — how close counts as arrived at settle slot\n" +
        "• separationRadius / separationStrength — nudge apart members that are too close";

    // ── Tick ──────────────────────────────────────────────────────────────────
    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        if (motor == null)
            return null;

        if (!s_herds.TryGetValue(herdId, out var members) || members.Count < 2)
            return null;

        // Separation nudge (always).
        foreach (var other in members)
        {
            if (other == this || !other.isActiveAndEnabled)
                continue;

            float dist = Vector3.Distance(context.Position, other.transform.position);
            if (dist < separationRadius && dist > 0.001f)
            {
                Vector3 away = context.Position - other.transform.position;
                away.y = 0f;
                motor.NudgeDestination(away.normalized * (separationStrength * (1f - dist / separationRadius)));
            }
        }

        // Read last frame's broadcast.
        if (!s_broadcast.TryGetValue(herdId, out BroadcastSlot slot) || !slot.HasValue)
            return null;

        MoveIntent broadcast = slot.Intent;

        // Reactive intents (chase, flee, stop-and-face): spread members on a circle
        // around the target so they surround it rather than piling on the same point.
        if (slot.Priority >= ModulePriority.Reactive)
        {
            s_state[herdId] = default;

            Vector3 combatCenter = broadcast.Type == AgentIntentType.StopAndFacePosition
                ? broadcast.FacePosition
                : broadcast.TargetPosition;

            // Assign each agent the unoccupied slot closest to their current position,
            // so no-one has to walk through others to reach the wrong side of the circle.
            if (!slotAssigned)
            {
                int count = members.Count;
                bool[] taken = new bool[count];

                // First pass: let already-assigned members keep their slots.
                foreach (var other in members)
                {
                    if (other == this || !other.isActiveAndEnabled) continue;
                    if (other.slotAssigned)
                        taken[other.mySlotIndex % count] = true;
                }

                // Pick nearest free slot for this member.
                float bestDist = float.MaxValue;
                int bestSlot = 0;
                for (int i = 0; i < count; i++)
                {
                    if (taken[i]) continue;
                    float a = i * (360f / count) * Mathf.Deg2Rad;
                    Vector3 slotPos = combatCenter + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * combatSpread;
                    float d = Vector3.Distance(context.Position, slotPos);
                    if (d < bestDist) { bestDist = d; bestSlot = i; }
                }

                mySlotIndex = bestSlot;
                slotAssigned = true;
            }

            float angle = mySlotIndex * (360f / members.Count) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * combatSpread;
            Vector3 candidate = combatCenter + offset;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit navHit, combatSpread, NavMesh.AllAreas))
                broadcast.TargetPosition = navHit.position;
            else
                broadcast.TargetPosition = candidate;

            broadcast.Type = AgentIntentType.MoveToPosition;
            return broadcast;
        }

        // Non-positional intents with no spread logic: broadcast as-is.
        if (broadcast.Type != AgentIntentType.MoveToPosition)
        {
            s_state[herdId] = default;
            slotAssigned = false;
            return broadcast;
        }

        // Get or update herd state.
        if (!s_state.TryGetValue(herdId, out HerdState state))
            state = default;

        // New destination broadcast — switch to Moving and assign slots.
        if (state.Phase == HerdPhase.Idle ||
            Vector3.Distance(state.Destination, broadcast.TargetPosition) > 0.5f)
        {
            state.Phase = HerdPhase.Moving;
            state.Destination = broadcast.TargetPosition;
            s_state[herdId] = state;
            slotAssigned = false;
        }

        if (state.Phase == HerdPhase.Moving)
        {
            // Assign a unique slot on a circle around the destination.
            if (!slotAssigned)
            {
                mySlotIndex = members.IndexOf(this);
                float angle = mySlotIndex * (360f / members.Count) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * settleRadius;
                Vector3 candidate = state.Destination + offset;
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, settleRadius, NavMesh.AllAreas))
                    mySlotPosition = hit.position;
                else
                    mySlotPosition = state.Destination;
                slotAssigned = true;
            }

            // Check if all members have arrived.
            bool allArrived = true;
            foreach (var other in members)
            {
                if (!other.isActiveAndEnabled) continue;
                if (Vector3.Distance(other.transform.position, other.mySlotPosition) > settleStopDistance + 0.5f)
                {
                    allArrived = false;
                    break;
                }
            }

            if (allArrived)
            {
                // Switch to settling: fan out from the group center.
                Vector3 center = Vector3.zero;
                int count = 0;
                foreach (var other in members)
                {
                    if (!other.isActiveAndEnabled) continue;
                    center += other.transform.position;
                    count++;
                }
                center /= count;

                state.Phase = HerdPhase.Settling;
                state.SettleCenter = center;
                s_state[herdId] = state;

                // Assign evenly-spaced settle slots around the group center.
                foreach (var other in members)
                {
                    if (!other.isActiveAndEnabled) continue;
                    int idx = members.IndexOf(other);
                    float angle = idx * (360f / members.Count) * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * settleRadius;
                    Vector3 candidate = center + offset;
                    if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, settleRadius, NavMesh.AllAreas))
                        other.mySlotPosition = hit.position;
                    else
                        other.mySlotPosition = center;
                }
            }

            return MoveIntent.MoveTo(mySlotPosition, settleStopDistance, 1f);
        }

        if (state.Phase == HerdPhase.Settling)
        {
            // Check if all members have reached their settle slot.
            bool allSettled = true;
            foreach (var other in members)
            {
                if (!other.isActiveAndEnabled) continue;
                if (Vector3.Distance(other.transform.position, other.mySlotPosition) > settleStopDistance + 0.2f)
                {
                    allSettled = false;
                    break;
                }
            }

            if (allSettled)
            {
                // Done — clear state so lower-priority modules pick the next move.
                s_state[herdId] = default;
                s_broadcast[herdId] = default;
                slotAssigned = false;
                return null;
            }

            return MoveIntent.MoveTo(mySlotPosition, settleStopDistance, 1f);
        }

        return null;
    }

    // ── Validation ────────────────────────────────────────────────────────────
    protected override void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(herdId))
            herdId = "default";
        combatSpread       = Mathf.Max(0.5f, combatSpread);
        settleRadius       = Mathf.Max(0.5f, settleRadius);
        settleStopDistance = Mathf.Max(0.1f, settleStopDistance);
        separationRadius   = Mathf.Max(0.1f, separationRadius);
        separationStrength = Mathf.Max(0f, separationStrength);
    }
}
