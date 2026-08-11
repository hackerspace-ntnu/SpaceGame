// Decides what the crawler's digging head, claw and collector should be doing, and hands those
// decisions to CrawlerToolRig as 0..1 targets.
//
// A **side-effect module**: `ClaimsMovement` is false, so it never returns a MoveIntent and never
// competes with WanderModule or a rider for the frame. AgentController ticks it every frame
// regardless of who won movement, which is exactly right — the machine should keep working its
// tools while it walks, and keep walking while it works them.
//
// The behaviour is a two-state read of the machine's own speed, not a plan:
//
//   travelling  — everything stowed. Boom up, drum stopped, claw furled over the hull, bucket
//                 carried high. A 21 m machine walking with its digging head down would plough.
//   working     — it has stopped, or slowed below `workSpeed`. Boom down, drum turning, claw
//                 sweeping out and back, and the collector running a lower / tip / raise cycle so
//                 the bucket visibly empties.
//
// Speed is read from `AgentContext.Velocity`, which AgentController fills from the motor's achieved
// velocity — not from the commanded twist. That matters on this machine: DesertCrawlerLocomotion
// clamps the throttle to what the planted legs can still reach, so a crawler that has been *asked*
// to move but is stepping in place reads as stopped, and starts working, which is the behaviour you
// want.
//
// There is deliberately no digging *simulation* here — nothing is excavated, nothing accumulates in
// the bucket. This drives the armature so the machine reads as a working digger; wiring it to real
// resource collection is a separate job with its own state to own.
using UnityEngine;

[RequireComponent(typeof(CrawlerToolRig))]
public class CrawlerToolModule : BehaviourModuleBase
{
    [Header("When to work")]
    [Tooltip("Below this speed the machine is considered stopped and starts working its tools.")]
    [SerializeField] private float workSpeed = 0.6f;
    [Tooltip("Seconds below workSpeed before the tools deploy. Stops them flapping every time the " +
             "gait pauses between steps.")]
    [SerializeField] private float settleTime = 1.2f;

    [Header("Claw sweep")]
    [Tooltip("Seconds for one out-and-back sweep of the claw while working.")]
    [SerializeField] private float clawPeriod = 7f;

    [Header("Collector cycle")]
    [Tooltip("Seconds for one lower / tip / raise cycle of the bucket while working.")]
    [SerializeField] private float collectorPeriod = 11f;

    private CrawlerToolRig rig;
    private float slowFor;
    private float clawClock;
    private float collectorClock;

    // Pure side effect: this module drives bones, never movement.
    public override bool ClaimsMovement => false;

    public override string ModuleDescription =>
        "Works the crawler's digging drum, claw and collector bucket while it is stopped, and " +
        "stows them while it walks. Drives bones only — never claims movement.";

    private void Reset()
    {
        SetPriorityDefault(ModulePriority.Ambient);
    }

    private void Awake()
    {
        rig = GetComponent<CrawlerToolRig>();
    }

    public override MoveIntent? Tick(in AgentContext context, float deltaTime)
    {
        if (rig == null || !rig.IsReady) return null;

        float speed = new Vector3(context.Velocity.x, 0f, context.Velocity.z).magnitude;
        slowFor = speed <= workSpeed ? slowFor + deltaTime : 0f;
        bool working = slowFor >= settleTime;

        if (!working)
        {
            Stow();
            return null;
        }

        Work(deltaTime);
        return null;   // always: a side-effect module must never claim the frame
    }

    private void Stow()
    {
        // 1 raises the boom: the model is authored with the drum already at cutting height, so
        // travelling is the pose that needs the angle, not working.
        rig.DiggerPitch = 1f;
        rig.DrumSpin = 0f;
        rig.ClawReach = 0f;
        rig.ClawGrip = 0f;
        rig.CollectorLift = 0f;
        rig.CollectorTip = 0f;
        clawClock = 0f;
        collectorClock = 0f;
    }

    private void Work(float deltaTime)
    {
        rig.DiggerPitch = 0f;   // down at cutting height — the authored rest pose
        rig.DrumSpin = 1f;

        // Claw: out, grip, back, release. One smooth pass rather than four scripted poses, so it
        // never looks like it is snapping between keyframes.
        clawClock = Repeat(clawClock + deltaTime, clawPeriod);
        float sweep = clawClock / clawPeriod;
        rig.ClawReach = Mathf.SmoothStep(0f, 1f, Triangle(sweep));
        // Grip closes around the far end of the reach, where the claw would actually have hold of
        // something, and opens again on the way back.
        rig.ClawGrip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.5f, sweep))
                       * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.8f, sweep)));

        // Collector: lower, hold, tip to dump, right itself, raise.
        collectorClock = Repeat(collectorClock + deltaTime, collectorPeriod);
        float c = collectorClock / collectorPeriod;
        rig.CollectorLift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.25f, c))
                            * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 0.95f, c)));
        rig.CollectorTip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.58f, c))
                           * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.66f, 0.78f, c)));
    }

    /// 0 → 1 → 0 across t in 0..1.
    private static float Triangle(float t) => 1f - Mathf.Abs(t * 2f - 1f);

    private static float Repeat(float value, float period)
        => period <= 0f ? 0f : value - period * Mathf.Floor(value / period);
}
