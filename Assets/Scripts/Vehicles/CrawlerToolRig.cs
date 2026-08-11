// Poses every non-leg bone on the desert crawler: the digging head, the scavenging claw and the
// collector bucket.
//
// The legs are not this component's business. `DesertCrawlerLocomotion` owns the
// Coxa/Hip/Knee/Ankle/Foot chains and writes them every frame; this writes `Dig_*`, `Claw_*` and
// `Collector_*` and nothing else, so the two never contend for a transform.
//
// Everything is expressed as a **0..1 target**, not an angle. Callers say "digger down" or "bucket
// tipped" and the ranges below decide what that means in degrees, so the machine can be
// re-proportioned or the rest pose re-authored without every caller needing to be revisited. Each
// target is eased toward rather than snapped, because these are heavy hydraulic things and a step
// change reads as a glitch.
//
// **Rest pose is captured at Awake and every rotation is relative to it.** The bones arrive from
// Blender already posed — the boom at its carry angle, the claw arced over the hull, the bucket
// raised — and that authored pose is the zero point. Writing absolute rotations would throw all of
// that away the first frame.
//
// Axis choice: the bones were authored in Blender's x = 0 plane with zero roll, which puts each
// one's local X across the machine, and the FBX axis conversion carries local axes through
// unchanged. So local X is the hinge for every joint here. It is exposed as a field anyway —
// if a bone is ever re-authored with a different roll, this is the one number that needs changing,
// and finding that out from the Inspector beats recompiling to test a guess.
//
// Runs at 110: after DesertCrawlerLocomotion (100), so the hull pose for the frame is settled before
// anything is hung off it. Nothing here depends on that ordering today; it is there so that adding
// a tool that reads hull velocity does not silently read last frame's.
using UnityEngine;

[DefaultExecutionOrder(110)]
public class CrawlerToolRig : MonoBehaviour
{
    [Header("Rig")]
    [Tooltip("Armature root. Left empty, the first child named CRAWLER_Rig (or any Transform with " +
             "a Root bone under it) is used.")]
    [SerializeField] private Transform armatureRoot;

    [Tooltip("Local axis every tool joint hinges about. Local X for bones authored in Blender's " +
             "x = 0 plane at zero roll, which is all of them.")]
    [SerializeField] private Vector3 hingeAxis = Vector3.right;

    [Header("Digging head")]
    // Negative, and measured rather than assumed. The model's rest pose already has the drum
    // 0.21 m off the ground — it is authored *deployed* — so the useful travel is upward, and a
    // positive angle about this bone's local X swings the drum backwards and under the hull
    // instead. Probed on the built prefab: +30 deg moves the boom tip (0.00, -0.56, -4.12).
    [Tooltip("Degrees the boom swings from cutting height to fully raised for travel.")]
    [SerializeField] private float diggerRange = -34f;
    [Tooltip("Revolutions per second of the cutter drum at full speed.")]
    [SerializeField] private float drumMaxRevs = 1.1f;
    [SerializeField] private float diggerSmooth = 1.6f;

    [Header("Claw")]
    [Tooltip("Degrees each of the six claw segments bends when the arm reaches out. The arm is a " +
             "chain, so the tip travels the sum of these.")]
    [SerializeField] private float clawSegmentRange = 14f;
    [Tooltip("Degrees the grabber closes.")]
    [SerializeField] private float clawGripRange = 26f;
    [SerializeField] private float clawSmooth = 1.1f;

    [Header("Collector")]
    [Tooltip("Degrees the lift arm swings from carry to fully lowered.")]
    [SerializeField] private float collectorLiftRange = 30f;
    // Also negative: probed at +30 deg the bucket's nose lifts (0.00, +0.66, -1.83), which is the
    // scooping direction, not the dumping one.
    [Tooltip("Degrees the bucket rolls forward to dump.")]
    [SerializeField] private float collectorTipRange = -46f;
    [SerializeField] private float collectorSmooth = 1.4f;

    // ---- targets, 0..1 --------------------------------------------------
    /// 0 = boom at its authored cutting height, 1 = raised clear of the ground for travel.
    /// The rest pose is the working pose on this machine, so 0 is "digging", not "stowed".
    public float DiggerPitch { get; set; }
    /// 0 = drum stopped, 1 = full speed.
    public float DrumSpin { get; set; }
    /// 0 = claw furled over the hull, 1 = reached out.
    public float ClawReach { get; set; }
    /// 0 = grabber open, 1 = closed.
    public float ClawGrip { get; set; }
    /// 0 = bucket carried high, 1 = lowered.
    public float CollectorLift { get; set; }
    /// 0 = bucket level, 1 = tipped forward to dump.
    public float CollectorTip { get; set; }

    public bool IsReady => ready;

    private struct Joint
    {
        public Transform Bone;
        public Quaternion Rest;
    }

    private Joint digBoom, digDrum, collectorLift, collectorBucket, clawGrab;
    private Joint[] clawSegments = System.Array.Empty<Joint>();

    private float pitch, reach, grip, lift, tip;
    private float drumAngle;
    private bool ready;

    private void Awake()
    {
        Initialise();
    }

    /// Find the tool bones and record the pose they arrived in. Safe to call again — the editor
    /// build calls it so the prefab can be inspected without entering play mode.
    public void Initialise()
    {
        Transform rig = armatureRoot != null ? armatureRoot : FindArmature();
        if (rig == null)
        {
            Debug.LogWarning($"[CrawlerToolRig] {name}: no armature found; tools stay put.");
            return;
        }

        digBoom = Capture(rig, "Dig_Boom");
        digDrum = Capture(rig, "Dig_Drum");
        collectorLift = Capture(rig, "Collector_Lift");
        collectorBucket = Capture(rig, "Collector_Bucket");
        clawGrab = Capture(rig, "Claw_Grab");

        var segments = new System.Collections.Generic.List<Joint>();
        for (int i = 1; i <= 32; i++)
        {
            Joint j = Capture(rig, $"Claw_{i:00}");
            if (j.Bone == null) break;
            segments.Add(j);
        }
        clawSegments = segments.ToArray();

        ready = digBoom.Bone != null || collectorLift.Bone != null || clawSegments.Length > 0;
        if (!ready)
        {
            Debug.LogWarning($"[CrawlerToolRig] {name}: found none of Dig_Boom / Collector_Lift / " +
                             "Claw_01 under the armature. Was the FBX exported before the tools " +
                             "were rigged?");
        }
    }

    private Transform FindArmature()
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "CRAWLER_Rig") return t;
        }
        // Fall back to whatever holds the Root bone, so a renamed armature still works.
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Root" && t.parent != null) return t.parent;
        }
        return null;
    }

    private static Joint Capture(Transform rig, string boneName)
    {
        foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != boneName) continue;
            return new Joint { Bone = t, Rest = t.localRotation };
        }
        return default;
    }

    private void LateUpdate()
    {
        if (!ready) return;
        float dt = Time.deltaTime;

        pitch = Mathf.MoveTowards(pitch, Mathf.Clamp01(DiggerPitch), diggerSmooth * dt);
        reach = Mathf.MoveTowards(reach, Mathf.Clamp01(ClawReach), clawSmooth * dt);
        grip = Mathf.MoveTowards(grip, Mathf.Clamp01(ClawGrip), clawSmooth * dt);
        lift = Mathf.MoveTowards(lift, Mathf.Clamp01(CollectorLift), collectorSmooth * dt);
        tip = Mathf.MoveTowards(tip, Mathf.Clamp01(CollectorTip), collectorSmooth * dt);

        Apply(digBoom, pitch * diggerRange);
        Apply(collectorLift, lift * collectorLiftRange);
        Apply(collectorBucket, tip * collectorTipRange);
        Apply(clawGrab, grip * clawGripRange);

        // The claw is a chain: every segment bends by the same amount, so the tip travels the sum
        // and the arm unfurls rather than kinking at one joint.
        for (int i = 0; i < clawSegments.Length; i++)
        {
            Apply(clawSegments[i], reach * clawSegmentRange);
        }

        // The drum free-runs rather than easing to a pose — it is a wheel, not a hinge.
        if (digDrum.Bone != null)
        {
            drumAngle += Mathf.Clamp01(DrumSpin) * drumMaxRevs * 360f * dt;
            if (drumAngle > 360f) drumAngle -= 360f;
            Apply(digDrum, drumAngle);
        }
    }

    private void Apply(Joint j, float degrees)
    {
        if (j.Bone == null) return;
        j.Bone.localRotation = j.Rest * Quaternion.AngleAxis(degrees, hingeAxis);
    }

    /// Snap every tool to its current target without easing. For editor previews and for spawning,
    /// where a machine easing out of its rest pose over the first second looks like a fault.
    public void SnapToTargets()
    {
        if (!ready) return;
        pitch = Mathf.Clamp01(DiggerPitch);
        reach = Mathf.Clamp01(ClawReach);
        grip = Mathf.Clamp01(ClawGrip);
        lift = Mathf.Clamp01(CollectorLift);
        tip = Mathf.Clamp01(CollectorTip);
    }
}
