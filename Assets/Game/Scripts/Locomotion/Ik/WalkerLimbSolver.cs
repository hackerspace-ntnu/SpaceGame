// Inverse kinematics for one limb: yaw the limb's plane onto the target, then solve inside it.
//
// The limb is a vertical hinge (the coxa) carrying a chain of parallel pitch hinges. That
// decomposes the IK exactly, with no approximation, because of one property of the rig: the first
// pitch joint sits ON the yaw axis, so yawing the coxa does not move it. The planar solve therefore
// always starts from the same point, and the two stages cannot interfere.
//
//   1. Turn the limb's plane about the yaw axis until it contains the target.
//   2. Solve the chain inside that plane, then aim the tip at the ground.
//
// The result is one scalar per hinge. An out-of-plane rotation is not representable, so the pins
// can never twist in their sockets whatever target this is handed -- and unlike the planar rig this
// replaced, there is no residual out-of-plane error to discard: any reachable point can be hit
// exactly, which is what lets a planted foot stay planted while the body moves.
//
// Every joint is bounded. Limits are applied to the quantity a real joint would limit -- the
// child's angle relative to its parent, not its absolute angle in the plane -- and a bound joint
// reports itself so the gait can step that limb early rather than stretch toward a pose it cannot
// reach.
//
// Stage 2 dispatches on how many links are free (WalkerPlanarChain). A three-segment leg leaves two
// free links and takes the analytic path below, which is why generalising to N joints could not
// change how either shipping machine walks.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    // What this solver is handed and what it hands back -- Frame, Limits and Result -- is defined
    // in WalkerLimbSolver.Types.cs.
    public static partial class WalkerLimbSolver
    {
        // ─────────── 2D core ───────────

        /// Two-link planar IK. `d` is the target measured from the first joint, in the plane's
        /// (fwd, up) coordinates. Returns absolute angles for both segments.
        ///
        /// `bendSign` selects which of the two mirror solutions to take, by the sign of the 2D
        /// cross product of the two segments. Passing the rest pose's sign keeps the knee bending
        /// the way the machine was built to bend.
        public static void SolveTwoLink(float l1, float l2, Vector2 d, float bendSign,
                                        out float a1, out float a2, out bool clamped)
        {
            float r = d.magnitude;
            float min = Mathf.Abs(l1 - l2) + 1e-4f;
            float max = l1 + l2 - 1e-4f;

            clamped = r > max || r < min;
            r = Mathf.Clamp(r, min, max);

            // Degenerate target sitting on the joint: hold the straight-out pose rather than
            // producing a NaN heading.
            float baseAngle = d.sqrMagnitude > 1e-12f ? Mathf.Atan2(d.y, d.x) : 0f;

            // Interior angle at the first joint between the target line and the upper segment.
            float cosA = (r * r + l1 * l1 - l2 * l2) / (2f * r * l1);
            float a = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));

            // Both mirror solutions are valid geometry; take the one whose bend matches the rig.
            // Testing the candidate rather than deriving a sign keeps this correct regardless of
            // how the plane basis happened to be oriented.
            a1 = baseAngle + a;
            a2 = SecondAngle(l1, a1, d);
            if (!Mathf.Approximately(bendSign, 0f) && Mathf.Sign(Cross(a1, a2)) != Mathf.Sign(bendSign))
            {
                a1 = baseAngle - a;
                a2 = SecondAngle(l1, a1, d);
            }
        }

        private static float SecondAngle(float l1, float a1, Vector2 d)
        {
            Vector2 knee = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * l1;
            Vector2 rest = d - knee;
            return rest.sqrMagnitude > 1e-12f ? Mathf.Atan2(rest.y, rest.x) : a1;
        }

        /// 2D cross product of two unit segments given by their angles. Its sign is the bend
        /// direction of the linkage at the shared joint.
        private static float Cross(float a1, float a2)
            => Mathf.Cos(a1) * Mathf.Sin(a2) - Mathf.Sin(a1) * Mathf.Cos(a2);

        // ─────────── full limb ───────────

        /// Places the contact point at `target`, with the tip laid along `groundNormal`.
        ///
        /// Allocates the result's angle array. The per-frame path uses the `ref` overload below
        /// with an array allocated once per limb; this one is for tests and setup code.
        public static Result Solve(in Frame f, in WalkerLimbGeometry g, in Limits limits,
                                   Vector3 target, Vector3 groundNormal)
        {
            var result = new Result { Pitch = new float[Mathf.Max(1, g.PitchCount)] };
            Solve(f, g, limits, target, groundNormal, ref result);
            return result;
        }

        /// The same solve, writing into a result whose angle array the caller already owns.
        public static void Solve(in Frame f, in WalkerLimbGeometry g, in Limits limits,
                                 Vector3 target, Vector3 groundNormal, ref Result result)
        {
            int count = g.PitchCount;
            if (count == 0) return;
            if (result.Pitch == null || result.Pitch.Length < count) result.Pitch = new float[count];

            Vector3 toTarget = target - f.Hip;
            result.ReachFraction = toTarget.magnitude / Mathf.Max(g.MaxReach, 1e-4f);
            result.Roll = 0f;

            // ── stage 1: yaw the plane onto the target ──
            // Only the component across the yaw axis can be answered by turning, so the target is
            // flattened onto that plane before the angle is taken. A planar limb has no yaw joint,
            // so this stage is a no-op and the out-of-plane component is simply unreachable --
            // which is invariant I5, and why such a limb cannot hold a planted foot through a turn.
            float yaw = 0f;
            bool yawBound = false;
            if (g.HasYaw)
            {
                Vector3 flat = Vector3.ProjectOnPlane(toTarget, f.YawAxis);
                float wanted = flat.sqrMagnitude > 1e-10f
                    ? Vector3.SignedAngle(f.RestFwd, flat, f.YawAxis)
                    : 0f;
                yaw = Mathf.Clamp(wanted, -limits.Yaw, limits.Yaw);
                yawBound = !Mathf.Approximately(yaw, wanted);
            }
            result.Yaw = yaw;

            Quaternion turn = Quaternion.AngleAxis(yaw, f.YawAxis);
            Vector3 fwd = turn * f.RestFwd;
            Vector3 up = f.YawAxis;

            // ── stage 2: solve inside the yawed plane ──
            // With the plane turned onto the target the out-of-plane component is zero, except for
            // whatever the yaw limit refused. Dropping that residual is correct: no axle can reach it.
            Vector2 sole = new Vector2(Vector3.Dot(toTarget, fwd), Vector3.Dot(toTarget, up));

            // Tip direction: lay the sole flat by pointing the last segment against the ground
            // normal. Only the in-plane part of the normal is usable, because the tip's axle is
            // parallel to the rest and so it pitches within the plane; it cannot answer
            // cross-slope. On a 20 degree cross-slope that leaves the sole a fraction of a unit
            // out of true, which is below the threshold of noticing on a machine this size.
            Vector2 normal2 = new Vector2(Vector3.Dot(groundNormal, fwd), Vector3.Dot(groundNormal, up));
            if (normal2.sqrMagnitude < 1e-8f) normal2 = Vector2.up;
            normal2.Normalize();

            int free = count - 1;

            // The last joint sits one tip-length back from the contact, along the surface normal.
            Vector2 tipJoint = sole + normal2 * g.Pitch[count - 1].Length;

            bool reachBound = false;
            bool freeBound = false;

            if (free > 0)
            {
                WalkerPlanarChain.Solve(g, limits, free, tipJoint, result.Pitch, out reachBound);

                // ── joint limits ──
                // Applied to relative angles, which is what a hinge actually constrains: the first
                // joint against the body, each one after it against its parent.
                //
                // The delta is measured against the RAW parent the chain solved for, while the pose
                // is rebuilt from the ACHIEVED one. That is what keeps a limb whose parent has
                // bound from folding the remaining error into its child.
                float previousRaw = 0f;
                float previousRest = 0f;
                float previousSolved = 0f;

                for (int i = 0; i < free; i++)
                {
                    float raw = result.Pitch[i];
                    float restRelative = g.Pitch[i].RestAngle - previousRest;
                    float wanted = Mathf.DeltaAngle(
                        0f, (raw - previousRaw - restRelative) * Mathf.Rad2Deg);
                    float limit = limits.PitchAt(i);
                    float delta = Mathf.Clamp(wanted, -limit, limit);
                    if (!Mathf.Approximately(delta, wanted)) freeBound = true;

                    float solved = previousSolved + restRelative + delta * Mathf.Deg2Rad;
                    result.Pitch[i] = solved;

                    previousRaw = raw;
                    previousRest = g.Pitch[i].RestAngle;
                    previousSolved = solved;
                }
            }

            // ── the tip ──
            // It points from the ACHIEVED last free joint down to the contact. Recomputing from the
            // pose the limb actually reached, rather than the one it was asked for, keeps the tip
            // attached to its parent when a limit or the reach bound has bitten.
            Vector2 achieved = WalkerPlanarChain.JointPosition(g, result.Pitch, free);
            Vector2 tipDir = sole - achieved;
            float tipAngle = tipDir.sqrMagnitude > 1e-12f
                ? Mathf.Atan2(tipDir.y, tipDir.x)
                : Mathf.Atan2(-normal2.y, -normal2.x);

            float parentAngle = free > 0 ? result.Pitch[free - 1] : 0f;
            float parentRest = free > 0 ? g.Pitch[free - 1].RestAngle : 0f;
            float tipRest = g.Pitch[count - 1].RestAngle - parentRest;
            float tipDelta = Mathf.Clamp(
                Mathf.DeltaAngle(0f, (tipAngle - parentAngle - tipRest) * Mathf.Rad2Deg),
                -limits.PitchAt(count - 1), limits.PitchAt(count - 1));
            result.Pitch[count - 1] = parentAngle + tipRest + tipDelta * Mathf.Deg2Rad;

            // ── the sole's roll ──
            // Everything above pitches about a lateral axle, so it can only answer the part of
            // the ground normal lying in the limb's plane -- fine walking up a slope, useless
            // walking across one. The roll hinge takes the remainder.
            if (g.HasRoll)
            {
                // The sole normal is simply the reverse of the tip segment, and the roll axis is
                // the limb's own forward, so the correction is the angle between the achieved and
                // desired normals measured about that axis.
                float tip = result.Pitch[count - 1];
                Vector3 soleNormal = -(fwd * Mathf.Cos(tip) + up * Mathf.Sin(tip));
                Vector3 have = Vector3.ProjectOnPlane(soleNormal, fwd);
                Vector3 want = Vector3.ProjectOnPlane(groundNormal, fwd);
                if (have.sqrMagnitude > 1e-8f && want.sqrMagnitude > 1e-8f)
                    result.Roll = Mathf.Clamp(Vector3.SignedAngle(have, want, fwd), -limits.Roll, limits.Roll);
            }

            result.Clamped = reachBound || yawBound || freeBound;
        }

        // ─────────── applying the solve ───────────

        /// Writes the local rotations onto the rig. Angles about parallel axles add, so each pitch
        /// joint supplies only the remainder its parents have not already contributed.
        public static void Apply(WalkerRig.Limb limb, in Result r)
        {
            WalkerLimbGeometry g = limb.Geometry;

            if (g.HasYaw && limb.Root != null)
                limb.Root.localRotation = g.RestRootLocal * Quaternion.AngleAxis(r.Yaw, g.YawAxisLocalRoot);

            float parentDelta = 0f;
            for (int i = 0; i < g.PitchCount; i++)
            {
                float chainDelta = ChainDelta(r.Pitch[i], g.Pitch[i].RestAngle);
                limb.Pitch[i].localRotation = JointRotation(
                    g.Pitch[i].RestLocal, g.Pitch[i].AxleLocal, chainDelta, parentDelta);
                parentDelta = chainDelta;
            }

            // The roll hinge is not parallel to the pitch axles, so it does not join the additive
            // chain -- it is simply the sole turning on its own pin, about a pivot that sits on
            // the contact point and therefore moves nothing the solve just placed.
            if (g.HasRoll && limb.Tip != null)
                limb.Tip.localRotation = g.RestTipLocal * Quaternion.AngleAxis(r.Roll, g.RollAxisLocalTip);
        }

        /// Local rotation for one pitch joint: the rest pose, turned about the measured axle by
        /// whatever its parents have not already supplied.
        public static Quaternion JointRotation(Quaternion restLocal, Vector3 axleLocal,
                                               float chainDelta, float parentDelta)
            => restLocal * Quaternion.AngleAxis(chainDelta - parentDelta, axleLocal);

        /// How far a joint's chain has turned from rest, in degrees.
        public static float ChainDelta(float solvedAngle, float restAngle)
            => Mathf.DeltaAngle(0f, (solvedAngle - restAngle) * Mathf.Rad2Deg);

        /// Forward kinematics of a solved pose, back to a world contact point.
        ///
        /// The chain is walked in WalkerLimbPose, which reports every joint rather than only the
        /// last. Two copies of this arithmetic would be two things to keep in step, and the one
        /// that drifted would drift silently.
        public static Vector3 SoleFromResult(in Frame f, in WalkerLimbGeometry g, in Result r)
            => WalkerLimbPose.Resolve(f, g, r).Sole;
    }
}
