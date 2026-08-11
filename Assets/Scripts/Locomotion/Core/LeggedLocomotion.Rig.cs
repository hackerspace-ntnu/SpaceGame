// What the machine IS: finding the limbs, measuring them, and deriving the gait from what was
// found.
//
// Everything here runs once, at Initialise, and everything downstream is a function of it. That is
// deliberate: rescaling the model or re-exporting it with different proportions re-tunes the whole
// machine with no numbers to edit, because the stride, the cadence, the step height and the top
// speed are all read off the rig rather than authored.
//
// Measurement is PER LEG. Both components this replaces averaged reach and hip height across the
// machine into one stride, which is only right when every leg is the same; a quadruped with shorter
// front legs got a stride that fitted neither pair. Cycle DISTANCE stays global -- the body only
// travels one distance per cycle -- and comes from the shortest leg's stride, since that is the one
// that runs out first.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public abstract partial class LeggedLocomotion
    {
        protected readonly List<LegState> legs = new List<LegState>();
        private readonly List<LegMeasurement> measurements = new List<LegMeasurement>();

        private WalkerGround ground;
        private IStrideModel strideModel;
        private IGaitPattern gaitPattern;
        private IBodyMotion bodyMotion;
        private IFootStyle footStyle;

        /// Stride of the SHORTEST leg. The body covers one distance per cycle whatever its legs
        /// are doing, and the shortest leg is the one that runs out of stride first.
        private float cycleStride;
        private float shortestLegReach;
        private float maxFootRadius;

        /// Discovers and measures the rig. Separated from Awake so the whole locomotion can be
        /// driven deterministically from a test, where Unity's callbacks never fire.
        public void Initialise()
        {
            legs.Clear();
            measurements.Clear();

            if (body == null) body = transform;
            ground = new WalkerGround(transform, groundMask, rayStartAbove, rayLength);

            strideModel = CreateStride();
            gaitPattern = CreateGait();
            bodyMotion = CreateBody();
            footStyle = CreateFeet();

            if (armatureRoot == null) armatureRoot = WalkerRig.FindArmature(transform);

            foreach (WalkerRig.Limb rig in WalkerRig.Build(armatureRoot, body))
            {
                Vector3 contact = rig.ContactPoint;
                legs.Add(new LegState
                {
                    Rig = rig,
                    Foot = contact,
                    // Angles are allocated once per leg, here, and reused every frame. Invariant
                    // I3: nothing in the per-frame path allocates.
                    Solved = new WalkerLimbSolver.Result
                    {
                        Pitch = new float[Mathf.Max(1, rig.Geometry.PitchCount)],
                    },
                    Limits = BuildLimits(rig.Geometry),
                });
            }

            ready = legs.Count > 0;
            if (!ready)
            {
                Debug.LogWarning(
                    $"[{GetType().Name}] No limb chains found; disabled. The rig needs a " +
                    "Limb_/Coxa_/Hip_ root with a *Pin* mesh at each joint.", this);
                return;
            }

            supportPoints = new Vector3[legs.Count];

            SortLegs();
            MeasureLegs();

            // BEFORE the speeds are derived, not after. A gait pattern's duty may depend on how
            // many legs it was given -- a ripple wave's does -- and an unbound pattern reporting a
            // duty of zero makes MaxSpeed zero, which makes SetTwist clamp every command to zero,
            // which stops the clock, which reopens no slice. That is invariant I1's failure mode
            // reached from the other end, and it stalls the machine dead.
            gaitPattern.Bind(measurements);

            DeriveSpeeds();

            for (int i = 0; i < legs.Count; i++)
                legs[i].PhaseOffset = gaitPattern.PhaseOffset(i, 0f);

            if (autoCalibrateRideHeight) CalibrateRideHeight();

            ResetBodyState();
        }

        /// A fixed ordering, taken once from the rest footholds, so the gait's leg sequence is
        /// stable. Sorting by live foot position would let the order flip between frames as feet
        /// move, and the sequence would stutter.
        ///
        /// Left before right, then rear to front, which gives a biped the 0 / ½ alternation it
        /// wants with no special case and a hexapod a sequence its gait pattern can re-order.
        private void SortLegs()
        {
            foreach (LegState leg in legs)
                leg.Measure.HomeLocal = body.InverseTransformPoint(leg.Foot);

            legs.Sort((a, b) =>
            {
                int side = a.Measure.HomeLocal.x.CompareTo(b.Measure.HomeLocal.x);
                return side != 0 ? side : a.Measure.HomeLocal.z.CompareTo(b.Measure.HomeLocal.z);
            });
        }

        /// Stride, cadence and the speeds they imply, all derived from the measured rig.
        private void MeasureLegs()
        {
            cycleStride = float.MaxValue;
            shortestLegReach = float.MaxValue;
            maxFootRadius = 0f;

            for (int i = 0; i < legs.Count; i++)
            {
                LegState leg = legs[i];
                WalkerLimbGeometry g = leg.Rig.Geometry;

                LegMeasurement m = leg.Measure;
                m.Index = i;

                // The radius the hip pitch sweeps the foot through: hip to contact, at rest.
                // Deliberately not RestFootRadius, which is the horizontal offset from the yaw axis
                // and is near zero on a bird -- using it here is what gives a three-centimetre
                // stride.
                m.LegReach = Vector3.Distance(leg.Rig.Anchor.position, leg.Foot);
                m.RestHipHeight = leg.Rig.Anchor.position.y - leg.Foot.y;
                m.HipLocal = body.InverseTransformPoint(leg.Rig.Anchor.position);
                m.MaxReach = g.MaxReach;
                m.RestFootRadius = g.RestFootRadius;
                m.FootRadiusFromCentre = new Vector2(m.HomeLocal.x, m.HomeLocal.z).magnitude;
                m.FootprintRadius = MeasureFootprint(leg, m.LegReach);

                // The stride model gets the last word, and only these two words.
                m.WorkingHipHeight = strideModel.WorkingHipHeight(m);
                m.StrideLength = strideModel.StrideLength(m);
                m.StepHeight = m.LegReach * stepClearance;

                leg.Measure = m;
                measurements.Add(m);

                cycleStride = Mathf.Min(cycleStride, m.StrideLength);
                shortestLegReach = Mathf.Min(shortestLegReach, m.LegReach);
                maxFootRadius = Mathf.Max(maxFootRadius, m.FootRadiusFromCentre);
            }
        }

        /// Top speed and pivot rate, once the gait pattern knows the machine's layout.
        ///
        /// Speed is quoted at the RUN duty, because that is the fastest the gait ever gets, and it
        /// is derived rather than authored: a foot must cover its whole stride within the part of
        /// the cycle it spends on the ground, so anything faster would need the feet to slide.
        private void DeriveSpeeds()
        {
            MaxSpeed = WalkerGait.MaxSpeed(cycleStride, stepDuration, gaitPattern.Duty(1f));
            MaxYawRate = DeriveMaxYawRate();
        }

        /// How far the sole spreads around its contact point, measured off the foot's own meshes so
        /// the ground sampling covers the real footprint rather than a guessed radius.
        private static float MeasureFootprint(LegState leg, float legReach)
        {
            float radius = 0f;
            Transform sole = leg.Rig.Tip != null ? leg.Rig.Tip : leg.Rig.TipJoint;
            foreach (MeshRenderer r in sole.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name.StartsWith("COL_")) continue;
                Vector3 size = r.bounds.size;
                radius = Mathf.Max(radius, Mathf.Max(size.x, size.z) * 0.35f);
            }
            return radius > 1e-4f ? radius : legReach * 0.08f;
        }

        /// Where the body sits in the rest pose, then dropped by however much standing the hips
        /// down to their working height requires. Without the second term a machine walks at the
        /// height it was modelled at, on straight legs, with no reach left to stride with.
        private void CalibrateRideHeight()
        {
            float footY = 0f;
            float standDown = 0f;
            foreach (LegState leg in legs)
            {
                footY += leg.Foot.y;
                standDown += leg.Measure.RestHipHeight - leg.Measure.WorkingHipHeight;
            }

            rideHeight = (body.position.y - footY / legs.Count) - standDown / legs.Count;
        }

        /// Put every foot on the ground under its rest position and cancel any swing in flight.
        ///
        /// Also what a landing calls. A machine that has just fallen is still holding the footholds
        /// it was stranded with, metres above where it now stands, and the gait would carry on from
        /// those.
        protected void GroundFeet()
        {
            foreach (LegState leg in legs)
            {
                Vector3 home = body.TransformPoint(leg.Measure.HomeLocal);
                if (SampleGround(leg, home, out Vector3 point, out Vector3 normal))
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
                // second, which is also what lands an articulated sole toe-first and rolls it flat.
                leg.StanceTime = 0f;
                leg.Load = 0f;
            }
        }

        /// The surface a whole sole will meet, at this leg's footprint size.
        private bool SampleGround(LegState leg, Vector3 at, out Vector3 point, out Vector3 normal)
            => ground.Sample(at, leg.Measure.FootprintRadius, out point, out normal);
    }
}
