// Inverse kinematics for one walker leg: yaw the leg's plane onto the target, then solve inside it.
//
// The leg is a vertical hinge (coxa) carrying three parallel pitch hinges. That decomposes the IK
// exactly, with no iteration and no approximation, because of one property of the rig: the hip
// sits ON the yaw axis, so yawing the coxa does not move the hip. The two-link solve therefore
// always starts from the same point, and the two stages cannot interfere.
//
//   1. Turn the leg's plane about the yaw axis until it contains the target.
//   2. Solve the two-link chain inside that plane, then aim the foot at the ground.
//
// The result is four scalars, one per hinge. An out-of-plane rotation is not representable, so the
// pins can never twist in their sockets whatever target this is handed -- and unlike the planar
// rig this replaced, there is no residual out-of-plane error to discard: any reachable point can
// be hit exactly, which is what lets a planted foot stay planted while the hull moves.
//
// Every joint is bounded. Limits are applied to the quantity a real joint would limit -- the
// child's angle relative to its parent, not its absolute angle in the plane -- and a bound joint
// reports itself so the gait can step that leg early rather than stretch toward a pose it cannot
// reach.
using UnityEngine;

namespace SpaceGame.Walker
{
    public static class WalkerLegSolver
    {
        /// Where the leg is attached this frame, in world space. Rebuilt each frame from the
        /// body's transform; the attachment is rigid relative to the body, so this is a basis
        /// change and nothing more.
        public struct Frame
        {
            /// Hinge centre of the hip. Lies on the yaw axis, so it is invariant under yaw.
            public Vector3 Hip;
            /// Yaw hinge, pointing up.
            public Vector3 YawAxis;
            /// In-plane horizontal axis at zero yaw, pointing outboard.
            public Vector3 RestFwd;
        }

        /// Symmetric travel about the rest pose, in degrees. This is the whole of what stops the
        /// machine folding itself into poses no linkage could hold.
        public struct Limits
        {
            public float Yaw;
            public float Hip;
            public float Knee;
            public float Ankle;
            public float Roll;

            public static Limits Default =>
                new Limits { Yaw = 40f, Hip = 45f, Knee = 60f, Ankle = 45f, Roll = 30f };
        }

        public struct Result
        {
            /// Degrees about the yaw axis, measured from the rest plane.
            public float Yaw;

            /// Absolute plane angles, radians, measured as atan2(up, fwd) of each segment.
            public float HipAngle;
            public float KneeAngle;
            public float AnkleAngle;

            /// Degrees about the sole's fore-aft hinge. Answers the part of the ground normal
            /// that lies across the leg's plane, which no pitch joint can reach.
            public float Roll;

            /// The linkage could not honour the target: out of reach, or a joint limit bound.
            /// The gait watches this and steps the leg rather than letting it stretch.
            public bool Clamped;

            /// Distance from hip to target as a fraction of the linkage's maximum.
            public float ReachFraction;
        }

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

        // ─────────── full leg ───────────

        /// Places the sole contact at `target`, with the foot laid along `groundNormal`.
        public static Result Solve(in Frame f, in WalkerLegGeometry g, in Limits limits,
                                   Vector3 target, Vector3 groundNormal)
        {
            Result result = default;
            Vector3 toTarget = target - f.Hip;
            result.ReachFraction = toTarget.magnitude / Mathf.Max(g.MaxReach, 1e-4f);

            // ── stage 1: yaw the plane onto the target ──
            // Only the component across the yaw axis can be answered by turning, so the target is
            // flattened onto that plane before the angle is taken.
            Vector3 flat = Vector3.ProjectOnPlane(toTarget, f.YawAxis);
            float yaw = flat.sqrMagnitude > 1e-10f
                ? Vector3.SignedAngle(f.RestFwd, flat, f.YawAxis)
                : 0f;
            float clampedYaw = Mathf.Clamp(yaw, -limits.Yaw, limits.Yaw);
            bool yawBound = !Mathf.Approximately(clampedYaw, yaw);
            result.Yaw = clampedYaw;

            Quaternion turn = Quaternion.AngleAxis(clampedYaw, f.YawAxis);
            Vector3 fwd = turn * f.RestFwd;
            Vector3 up = f.YawAxis;

            // ── stage 2: solve inside the yawed plane ──
            // With the plane turned onto the target the out-of-plane component is zero, except for
            // whatever the yaw limit refused. Dropping that residual is correct: no axle can reach it.
            Vector2 sole = new Vector2(Vector3.Dot(toTarget, fwd), Vector3.Dot(toTarget, up));

            // Foot direction: lay the sole flat by pointing the foot segment against the ground
            // normal. Only the in-plane part of the normal is usable, because the ankle axle is
            // parallel to the knee's and so the foot pitches within the plane; it cannot answer
            // cross-slope. On a 20 degree cross-slope that leaves the sole a fraction of a unit
            // out of true, which is below the threshold of noticing on a machine this size.
            Vector2 normal2 = new Vector2(Vector3.Dot(groundNormal, fwd), Vector3.Dot(groundNormal, up));
            if (normal2.sqrMagnitude < 1e-8f) normal2 = Vector2.up;
            normal2.Normalize();

            // The ankle sits one foot-length back from the contact, along the surface normal.
            Vector2 ankle = sole + normal2 * g.FootLength;

            SolveTwoLink(g.UpperLength, g.LowerLength, ankle, g.BendSign,
                         out float hipAngle, out float kneeAngle, out bool reachBound);

            // ── joint limits ──
            // Applied to relative angles, which is what a hinge actually constrains: the hip is
            // limited against the body, the knee against the thigh, the ankle against the shin.
            float hipDelta = Mathf.Clamp(
                Mathf.DeltaAngle(0f, (hipAngle - g.RestHipAngle) * Mathf.Rad2Deg),
                -limits.Hip, limits.Hip);
            bool hipBound = !Mathf.Approximately(
                hipDelta, Mathf.DeltaAngle(0f, (hipAngle - g.RestHipAngle) * Mathf.Rad2Deg));

            float restBend = g.RestKneeAngle - g.RestHipAngle;
            float kneeDelta = Mathf.Clamp(
                Mathf.DeltaAngle(0f, (kneeAngle - hipAngle - restBend) * Mathf.Rad2Deg),
                -limits.Knee, limits.Knee);
            bool kneeBound = !Mathf.Approximately(
                kneeDelta, Mathf.DeltaAngle(0f, (kneeAngle - hipAngle - restBend) * Mathf.Rad2Deg));

            result.HipAngle = g.RestHipAngle + hipDelta * Mathf.Deg2Rad;
            result.KneeAngle = result.HipAngle + restBend + kneeDelta * Mathf.Deg2Rad;

            // The foot points from the ACHIEVED ankle down to the contact. Recomputing from the
            // pose the leg actually reached, rather than the one it was asked for, keeps the foot
            // attached to the shin when a limit or the reach bound has bitten.
            Vector2 achievedAnkle =
                new Vector2(Mathf.Cos(result.HipAngle), Mathf.Sin(result.HipAngle)) * g.UpperLength +
                new Vector2(Mathf.Cos(result.KneeAngle), Mathf.Sin(result.KneeAngle)) * g.LowerLength;
            Vector2 footDir = sole - achievedAnkle;
            float ankleAngle = footDir.sqrMagnitude > 1e-12f
                ? Mathf.Atan2(footDir.y, footDir.x)
                : Mathf.Atan2(-normal2.y, -normal2.x);

            float restFoot = g.RestAnkleAngle - g.RestKneeAngle;
            float ankleDelta = Mathf.Clamp(
                Mathf.DeltaAngle(0f, (ankleAngle - result.KneeAngle - restFoot) * Mathf.Rad2Deg),
                -limits.Ankle, limits.Ankle);
            result.AnkleAngle = result.KneeAngle + restFoot + ankleDelta * Mathf.Deg2Rad;

            // ── the sole's roll ──
            // Everything above pitches about a lateral axle, so it can only answer the part of
            // the ground normal lying in the leg's plane -- fine walking up a slope, useless
            // walking across one. The roll hinge takes the remainder.
            if (g.HasRoll)
            {
                // The sole normal is simply the reverse of the foot segment, and the roll axis is
                // the leg's own forward, so the correction is the angle between the achieved and
                // desired normals measured about that axis.
                Vector3 soleNormal = -(fwd * Mathf.Cos(result.AnkleAngle) + up * Mathf.Sin(result.AnkleAngle));
                Vector3 have = Vector3.ProjectOnPlane(soleNormal, fwd);
                Vector3 want = Vector3.ProjectOnPlane(groundNormal, fwd);
                if (have.sqrMagnitude > 1e-8f && want.sqrMagnitude > 1e-8f)
                    result.Roll = Mathf.Clamp(Vector3.SignedAngle(have, want, fwd), -limits.Roll, limits.Roll);
            }

            result.Clamped = reachBound || yawBound || hipBound || kneeBound;
            return result;
        }

        // ─────────── applying the solve ───────────

        /// Writes the four local rotations onto the rig. Angles about parallel axles add, so each
        /// pitch joint supplies only the remainder its parents have not already contributed.
        public static void Apply(WalkerRig.Leg leg, in Result r)
        {
            WalkerLegGeometry g = leg.Geometry;

            leg.Coxa.localRotation = g.RestCoxaLocal * Quaternion.AngleAxis(r.Yaw, g.YawAxisLocalCoxa);

            float hipDelta = ChainDelta(r.HipAngle, g.RestHipAngle);
            float kneeDelta = ChainDelta(r.KneeAngle, g.RestKneeAngle);

            leg.Hip.localRotation = JointRotation(g.RestHipLocal, g.AxleLocalHip, hipDelta, 0f);
            leg.Knee.localRotation = JointRotation(g.RestKneeLocal, g.AxleLocalKnee, kneeDelta, hipDelta);
            leg.Ankle.localRotation = JointRotation(
                g.RestAnkleLocal, g.AxleLocalAnkle, ChainDelta(r.AnkleAngle, g.RestAnkleAngle), kneeDelta);

            // The roll hinge is not parallel to the pitch axles, so it does not join the additive
            // chain -- it is simply the sole turning on its own pin, about a pivot that sits on
            // the contact point and therefore moves nothing the solve just placed.
            if (g.HasRoll && leg.Foot != null)
                leg.Foot.localRotation = g.RestFootLocal * Quaternion.AngleAxis(r.Roll, g.RollAxisLocalFoot);
        }

        /// Local rotation for one pitch joint: the rest pose, turned about the measured axle by
        /// whatever its parents have not already supplied.
        public static Quaternion JointRotation(Quaternion restLocal, Vector3 axleLocal,
                                               float chainDelta, float parentDelta)
            => restLocal * Quaternion.AngleAxis(chainDelta - parentDelta, axleLocal);

        /// How far a joint's chain has turned from rest, in degrees.
        public static float ChainDelta(float solvedAngle, float restAngle)
            => Mathf.DeltaAngle(0f, (solvedAngle - restAngle) * Mathf.Rad2Deg);

        /// Forward kinematics of a solved pose, back to a world sole position. Used by the tests
        /// to prove the solve round-trips, which is the property the whole design rests on.
        public static Vector3 SoleFromResult(in Frame f, in WalkerLegGeometry g, in Result r)
        {
            Vector3 fwd = Quaternion.AngleAxis(r.Yaw, f.YawAxis) * f.RestFwd;
            Vector2 p = new Vector2(Mathf.Cos(r.HipAngle), Mathf.Sin(r.HipAngle)) * g.UpperLength
                      + new Vector2(Mathf.Cos(r.KneeAngle), Mathf.Sin(r.KneeAngle)) * g.LowerLength
                      + new Vector2(Mathf.Cos(r.AnkleAngle), Mathf.Sin(r.AnkleAngle)) * g.FootLength;
            return f.Hip + fwd * p.x + f.YawAxis * p.y;
        }
    }
}
