// What the machine IS: finding the legs, measuring them, and deriving the gait from what was
// found.
//
// Everything in here runs once, at Initialise, and everything downstream is a function of it.
// That is deliberate: rescaling the model or re-exporting it with different proportions re-tunes
// the whole machine with no numbers to edit, because the stride, the cadence, the step height and
// the top speed are all read off the rig rather than authored.
//
// The one exception is grounding the feet, which also happens on a landing.
using System.Collections.Generic;
using SpaceGame.Walker;
using UnityEngine;

public partial class OstrichLocomotion
{
    /// Everything the gait and the IK need to know about one leg. A class rather than a struct
    /// because it is mutated in place inside foreach loops all over the gait.
    private class LegState
    {
        public WalkerRig.Leg Rig;
        public Vector3 HomeLocal;       // rest foothold, in body space
        public Vector3 Foot;            // world contact point; fixed while planted
        public Vector3 GroundNormal = Vector3.up;
        public Vector3 SwingFrom, SwingTo;
        public float PhaseOffset;
        public bool Swinging;
        public float SwingT;
        public float SwingLift;
        /// Seconds this swing was given, fixed at lift-off. Recomputing it every frame from the
        /// current pace lets a step that began at speed stretch when the rider slows, so the foot
        /// is still in the air when its slice reopens, misses that slot and limps for a stride.
        public float SwingSpan;
        public bool WasInSlice;
        public bool Unreachable;
        /// Seconds this foot has been on the ground. Gates the step-early rule.
        public float StanceTime;
        /// Share of the body this foot is currently carrying, 0..1. Ramps in from touchdown and
        /// out again as lift-off comes up, so the two feet trade the weight rather than swapping it.
        public float Load;
    }

    /// Fraction of the available horizontal reach the stride is sized against.
    ///
    /// The trailing leg is at its most extended in the instant before it lifts, so this is what
    /// decides how close to dead straight the leg gets at the end of every stance. At 0.85 it
    /// reaches 94% of the linkage's length and the smallest bump tips it past what the solver can
    /// honour; at 0.72 it tops out around 90% and keeps a visible bend in the hock.
    private const float StrideHipFraction = 0.72f;

    private readonly List<LegState> legs = new List<LegState>();
    private WalkerGround ground;

    private float strideLength;
    private float legReach;             // hip to sole at rest: the radius the hip pitch sweeps
    private float restHipHeight;        // hip above sole in the model's rest pose
    private float workingHipHeight;     // hip above sole while walking, after standing down
    private float stepHeight;
    private float maxFootRadius;
    private float footprintRadius;

    /// Discovers and measures the rig. Separated from Awake so the whole locomotion can be driven
    /// deterministically from a test, where Unity's callbacks never fire.
    public void Initialise()
    {
        legs.Clear();
        if (body == null) body = transform;
        ground = new WalkerGround(transform, groundMask, rayStartAbove, rayLength);
        if (armatureRoot == null) armatureRoot = WalkerRig.FindArmature(transform);

        foreach (WalkerRig.Leg rig in WalkerRig.Build(armatureRoot, body))
        {
            Vector3 contact = rig.Ankle.TransformPoint(rig.Geometry.ContactLocalAnkle);
            legs.Add(new LegState
            {
                Rig = rig,
                Foot = contact,
                HomeLocal = body.InverseTransformPoint(contact),
            });
        }

        ready = legs.Count > 0;
        if (!ready)
        {
            Debug.LogWarning("[OstrichLocomotion] No leg chains found; disabled. The rig needs " +
                             "Coxa_/Hip_/Knee_/Ankle_ bones with a *Pin* mesh at each joint.", this);
            return;
        }

        // Left before right, so the two legs get opposite halves of the cycle in a stable order.
        legs.Sort((a, b) => a.HomeLocal.x.CompareTo(b.HomeLocal.x));
        for (int i = 0; i < legs.Count; i++)
            legs[i].PhaseOffset = WalkerGait.MetachronalOffset(i, legs.Count);

        MeasureGait();

        if (autoCalibrateRideHeight)
        {
            float avgFootY = 0f;
            foreach (LegState leg in legs) avgFootY += leg.Foot.y;
            avgFootY /= legs.Count;

            // Where the body sits in the rest pose, then dropped by however much standing the hips
            // down to workingHipHeight requires. Without the second term the bird walks at the
            // height it was modelled at, on straight legs, with no reach left to stride with.
            rideHeight = (body.position.y - avgFootY) - (restHipHeight - workingHipHeight);
        }

        ResetBodyState();
    }

    /// Stride, cadence and the speeds they imply, all derived from the measured rig so rescaling
    /// the model re-tunes the machine with no numbers to edit.
    private void MeasureGait()
    {
        legReach = 0f;
        maxFootRadius = 0f;
        restHipHeight = 0f;
        float maxLinkage = 0f;

        foreach (LegState leg in legs)
        {
            // The radius the hip pitch sweeps the foot through: hip to sole, at rest. Deliberately
            // not Geometry.RestFootRadius, which is the horizontal offset from the yaw axis and is
            // near zero on a bird -- using it here is what would give a three-centimetre stride.
            legReach += Vector3.Distance(leg.Rig.Hip.position, leg.Foot);
            restHipHeight += leg.Rig.Hip.position.y - leg.Foot.y;
            maxLinkage = Mathf.Max(maxLinkage, leg.Rig.Geometry.MaxReach);

            Vector2 flat = new Vector2(leg.HomeLocal.x, leg.HomeLocal.z);
            maxFootRadius = Mathf.Max(maxFootRadius, flat.magnitude);
        }
        legReach /= legs.Count;
        restHipHeight /= legs.Count;

        // The stride is bounded by geometry, not by how far the hip joint is allowed to rotate.
        //
        // A leg carrying the body at height h out of a total linkage length L can only put its
        // foot on the ground within sqrt(L^2 - h^2) of the hip: everything else is spent reaching
        // DOWN. These legs are modelled nearly straight -- h is 91% of L -- so at the modelled
        // height that budget is almost nothing, and a stride derived from the hip's angular travel
        // instead would ask for steps the leg physically cannot place. It would get them, too:
        // the solver would clamp, the foot would hang off the end of a straight leg, and the bird
        // would skate. Standing it down by hipHeightFraction is what buys the stride back.
        workingHipHeight = restHipHeight * hipHeightFraction;
        float horizontalBudget = Mathf.Sqrt(
            Mathf.Max(maxLinkage * 0.15f, maxLinkage * maxLinkage - workingHipHeight * workingHipHeight));

        strideLength = 2f * horizontalBudget * StrideHipFraction;
        stepHeight = legReach * stepClearance;

        footprintRadius = 0f;
        foreach (LegState leg in legs)
        {
            Transform sole = leg.Rig.Foot != null ? leg.Rig.Foot : leg.Rig.Ankle;
            foreach (MeshRenderer r in sole.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name.StartsWith("COL_")) continue;
                Vector3 size = r.bounds.size;
                footprintRadius = Mathf.Max(footprintRadius, Mathf.Max(size.x, size.z) * 0.35f);
            }
        }
        if (footprintRadius < 1e-4f) footprintRadius = legReach * 0.08f;

        // Top speed is quoted at the run duty, because that is the fastest the gait ever gets.
        MaxSpeed = WalkerGait.MaxSpeed(strideLength, stepDuration, runSwingDuty);
    }

    /// Put every foot on the ground under its rest position and cancel any swing in flight.
    ///
    /// Also what a landing calls. A bird that has just fallen is still holding the footholds it was
    /// stranded with, metres above where it now stands, and the gait would carry on from those.
    private void GroundFeet()
    {
        foreach (LegState leg in legs)
        {
            Vector3 home = body.TransformPoint(leg.HomeLocal);
            if (SampleGround(home, out Vector3 point, out Vector3 normal))
            {
                leg.Foot = point;
                leg.GroundNormal = normal;
            }
            else
            {
                leg.Foot = home;
                leg.GroundNormal = Vector3.up;
            }
            leg.Swinging = false;
            leg.SwingT = 0f;
            leg.WasInSlice = false;
            leg.Unreachable = false;
            // Fresh feet carry nothing yet. They take the weight on over the next fraction of a
            // second, which is also what lands the bird toe-first and rolls it flat.
            leg.StanceTime = 0f;
            leg.Load = 0f;
        }
    }

    /// The surface a whole sole will meet, at this rig's footprint size.
    private bool SampleGround(Vector3 at, out Vector3 point, out Vector3 normal)
        => ground.Sample(at, footprintRadius, out point, out normal);
}
