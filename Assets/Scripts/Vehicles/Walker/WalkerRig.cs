// Finds the walking station's legs on the imported rig and measures each one from its rest pose.
//
// A leg is four joints: Coxa (yaw, vertical) -> Hip -> Knee -> Ankle (all pitch, all parallel and
// horizontal). The coxa is what makes the machine work. With only the three pitch hinges a leg is
// trapped in one vertical plane, and a planted foot cannot stay planted while the hull travels in
// any other direction; with the coxa, a foot can be held anywhere in a 3D annulus around the hip,
// so stance feet are exactly fixed and the gait needs no compensation for slip.
//
// Every hinge axis is measured from the modelled pin mesh rather than guessed from the hierarchy,
// which is why adding the coxa needed no change here beyond looking for one more joint: a pin is
// a cylinder, so its longest axis IS the hinge. Re-exporting with different proportions, or
// rescaling the rig, keeps working with no code change.
//
// Naming on this rig: Coxa_<id> -> Hip_<id> -> Knee_<id> -> Ankle_<id>, ids N1..N3 and P1..P3,
// with pins LEG3_CoxaPin_<id>, LEG3_HipPin_<id>, LEG3_KneePin_<id>, LEG3_AnklePin_<id>.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Walker
{
    /// Measured, immutable geometry of one leg. Captured once from the rest pose; angles are
    /// radians, lengths are world units, and each axle is stored in the space its own joint's
    /// `localRotation = rest * AngleAxis(theta, axle)` needs.
    public struct WalkerLegGeometry
    {
        // ─────────── yaw joint ───────────
        /// Vertical hinge, in body space. Signed so it points up.
        public Vector3 YawAxisBody;
        public Vector3 YawAxisLocalCoxa;
        public Quaternion RestCoxaLocal;

        // ─────────── pitch joints ───────────
        // All three denote one world line at rest and stay parallel to each other at any yaw,
        // because yawing the coxa turns all of them together. Angles about parallel axles add,
        // so a child joint only supplies the remainder of its parent's rotation.
        public Vector3 AxleLocalHip;
        public Vector3 AxleLocalKnee;
        public Vector3 AxleLocalAnkle;
        public Quaternion RestHipLocal;
        public Quaternion RestKneeLocal;
        public Quaternion RestAnkleLocal;

        /// In-plane horizontal axis at zero yaw, in body space. Signed to point outboard, toward
        /// the foot, so a yaw angle measured against it reads the way you would expect.
        public Vector3 RestFwdBody;

        /// Absolute plane angles of each segment at rest, measured as atan2(up, fwd).
        public float RestHipAngle;
        public float RestKneeAngle;
        public float RestAnkleAngle;

        /// hip->knee, knee->ankle and ankle->sole lengths, in world units.
        public float UpperLength;
        public float LowerLength;
        public float FootLength;

        /// Sign of the 2D cross product of upper and lower at rest. Pins the elbow solution so
        /// the knee can never pop through to the mirrored pose.
        public float BendSign;

        /// Sole contact point in the ankle's local space, at rest. This is what meets the ground.
        public Vector3 ContactLocalAnkle;

        // ─────────── roll joint ───────────
        // The sole's own hinge, running fore-aft along the leg. Every other joint pitches about a
        // lateral axle, so without this the sole can meet a slope it is walking up but not one it
        // is walking across. Its pivot sits ON the contact point, so rolling tilts the sole
        // without dragging the point the IK just placed.
        public Vector3 RollAxisLocalFoot;
        public Quaternion RestFootLocal;
        public bool HasRoll;

        /// Horizontal distance from the yaw axis to the rest foothold. The radius of the arc the
        /// foot sweeps when the coxa turns, so it is what sets the machine's stride.
        public float RestFootRadius;

        /// Furthest the sole can get from the hip, with a margin so the solver is never handed a
        /// target exactly at the singularity.
        public float MaxReach => (UpperLength + LowerLength + FootLength) * 0.97f;
    }

    public static class WalkerRig
    {
        public class Leg
        {
            public string Id;
            public Transform Coxa, Hip, Knee, Ankle;
            /// Sole roll joint. Optional: a rig without one still walks, with a sole that can
            /// only pitch, so this degrades rather than fails.
            public Transform Foot;
            public WalkerLegGeometry Geometry;
        }

        /// Discovers every complete Coxa/Hip/Knee/Ankle chain under `armature` and measures it
        /// against `body`. Legs whose geometry cannot be trusted are skipped, with a warning
        /// naming them, rather than silently producing a linkage that tears itself apart.
        public static List<Leg> Build(Transform armature, Transform body)
        {
            var legs = new List<Leg>();
            if (armature == null || body == null) return legs;

            Transform[] all = armature.GetComponentsInChildren<Transform>(true);
            foreach (Transform hip in all)
            {
                if (!hip.name.StartsWith("Hip_")) continue;
                string id = hip.name.Substring("Hip_".Length);

                Transform coxa = Find(all, "Coxa_" + id);
                Transform knee = Find(all, "Knee_" + id);
                Transform ankle = Find(all, "Ankle_" + id);

                if (coxa == null)
                {
                    // Without a yaw axle the leg is planar and cannot hold a planted foot. Better
                    // to say so plainly than to walk badly and leave someone guessing why.
                    Debug.LogWarning(
                        $"[Walker] Leg {id} has no Coxa_{id} bone, so it has no yaw axle and is " +
                        "skipped. Run Assets/Models/.../robot_rig/walker_add_coxa.py over the " +
                        "model to add the azimuth joint.", hip);
                    continue;
                }
                if (knee == null || ankle == null) continue;

                var leg = new Leg
                {
                    Id = id, Coxa = coxa, Hip = hip, Knee = knee, Ankle = ankle,
                    Foot = Find(all, "Foot_" + id),
                };
                if (Measure(leg, body)) legs.Add(leg);
            }
            return legs;
        }

        private static Transform Find(Transform[] pool, string name)
        {
            foreach (Transform t in pool)
                if (t.name == name) return t;
            return null;
        }

        /// The armature carrying the leg chains. The imported model contains more than one
        /// armature-looking node, so pick whichever holds the most hips.
        public static Transform FindArmature(Transform root)
        {
            Transform best = null;
            int bestCount = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                int n = 0;
                foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
                    if (c.name.StartsWith("Hip_")) n++;
                if (n > bestCount) { bestCount = n; best = t; }
            }
            return best != null ? best : root;
        }

        // ─────────── measurement ───────────

        private static bool Measure(Leg leg, Transform body)
        {
            var g = new WalkerLegGeometry
            {
                RestCoxaLocal = leg.Coxa.localRotation,
                RestHipLocal = leg.Hip.localRotation,
                RestKneeLocal = leg.Knee.localRotation,
                RestAnkleLocal = leg.Ankle.localRotation,
            };

            Vector3 upper = leg.Knee.position - leg.Hip.position;
            Vector3 lower = leg.Ankle.position - leg.Knee.position;
            g.UpperLength = upper.magnitude;
            g.LowerLength = lower.magnitude;
            if (g.UpperLength < 1e-4f || g.LowerLength < 1e-4f)
            {
                Debug.LogWarning($"[Walker] Leg {leg.Id} has a zero-length segment; skipped.", leg.Hip);
                return false;
            }

            // Yaw axis first: it is the leg's "up", and every other measurement is taken against it.
            Vector3 yawAxis = WalkerAxle.MatchSense(
                MeasureAxle(leg.Coxa, upper, lower, body.up), body.up);

            // The knee pin is the largest of the three, so it is the most reliable measurement.
            // Take it as the reference sense and make the others agree.
            Vector3 kneeAxle = MeasureAxle(leg.Knee, upper, lower, yawAxis);
            Vector3 hipAxle = WalkerAxle.MatchSense(MeasureAxle(leg.Hip, upper, lower, yawAxis), kneeAxle);
            Vector3 ankleAxle = WalkerAxle.MatchSense(MeasureAxle(leg.Ankle, upper, lower, yawAxis), kneeAxle);

            // The solver treats the three pitch joints as one rigid plane hanging off the yaw
            // axle. If the pins genuinely disagree that assumption is void, so say so loudly and
            // fall back to the knee rather than producing a leg that quietly tears itself apart.
            if (!WalkerAxle.AreParallel(hipAxle, kneeAxle) || !WalkerAxle.AreParallel(ankleAxle, kneeAxle))
            {
                Debug.LogWarning(
                    $"[Walker] Leg {leg.Id} pitch pins are not parallel " +
                    $"(hip.knee={Vector3.Dot(hipAxle, kneeAxle):F3}, ankle.knee={Vector3.Dot(ankleAxle, kneeAxle):F3}). " +
                    "Using the knee pin for all three; the leg is not a planar linkage.", leg.Hip);
                hipAxle = kneeAxle;
                ankleAxle = kneeAxle;
            }

            // Sole contact: the lowest point of the foot's own meshes, which is what actually
            // touches the ground. Not the toe bone's tip, and not the ankle.
            Vector3 contact = LowestRendererPoint(leg.Ankle, leg.Ankle.position);
            Vector3 foot = contact - leg.Ankle.position;
            g.FootLength = foot.magnitude;
            if (g.FootLength < 1e-3f)
            {
                g.FootLength = 1e-3f;                 // no foot geometry: the ankle is the contact
                foot = -yawAxis * 1e-3f;
            }
            g.ContactLocalAnkle = leg.Ankle.InverseTransformPoint(contact);

            // The plane basis. A pin is a line, not a direction, so its measured sense is
            // arbitrary; force `fwd` to point outboard toward the foot and carry the axle with it.
            // Fixing the sign once here is what keeps every yaw angle downstream readable.
            Vector3 planeFwd = WalkerAxle.PlaneForward(kneeAxle, yawAxis);
            if (Vector3.Dot(planeFwd, contact - leg.Hip.position) < 0f)
            {
                planeFwd = -planeFwd;
                kneeAxle = -kneeAxle;
                hipAxle = -hipAxle;
                ankleAxle = -ankleAxle;
            }

            g.YawAxisBody = body.InverseTransformDirection(yawAxis).normalized;
            g.YawAxisLocalCoxa = leg.Coxa.InverseTransformDirection(yawAxis).normalized;
            g.RestFwdBody = body.InverseTransformDirection(planeFwd).normalized;

            g.AxleLocalHip = leg.Hip.InverseTransformDirection(hipAxle).normalized;
            g.AxleLocalKnee = leg.Knee.InverseTransformDirection(kneeAxle).normalized;
            g.AxleLocalAnkle = leg.Ankle.InverseTransformDirection(ankleAxle).normalized;

            g.RestHipAngle = PlaneAngle(upper, planeFwd, yawAxis);
            g.RestKneeAngle = PlaneAngle(lower, planeFwd, yawAxis);
            g.RestAnkleAngle = PlaneAngle(foot, planeFwd, yawAxis);

            g.BendSign = Mathf.Sign(
                Mathf.Cos(g.RestHipAngle) * Mathf.Sin(g.RestKneeAngle) -
                Mathf.Sin(g.RestHipAngle) * Mathf.Cos(g.RestKneeAngle));
            if (Mathf.Approximately(g.BendSign, 0f)) g.BendSign = 1f;

            Vector3 hipToContact = contact - leg.Hip.position;
            g.RestFootRadius = Vector3.ProjectOnPlane(hipToContact, yawAxis).magnitude;

            // The sole's roll hinge, if the model has one. Measured from its pin like every other
            // joint, and signed to agree with the leg's outboard direction so a positive roll
            // means the same thing on every leg.
            g.HasRoll = leg.Foot != null;
            if (g.HasRoll)
            {
                Vector3 rollAxis = WalkerAxle.MatchSense(
                    MeasureAxle(leg.Foot, upper, lower, planeFwd), planeFwd);
                g.RollAxisLocalFoot = leg.Foot.InverseTransformDirection(rollAxis).normalized;
                g.RestFootLocal = leg.Foot.localRotation;

                if (Mathf.Abs(Vector3.Dot(rollAxis.normalized, yawAxis)) > 0.05f)
                    Debug.LogWarning(
                        $"[Walker] Leg {leg.Id} roll axis is not horizontal; the sole will tilt " +
                        "oddly across slopes.", leg.Foot);
            }

            leg.Geometry = g;
            return true;
        }

        /// Hinge axis of one joint: the longest axis of its modelled pin, or the rest pose's own
        /// plane normal when the model has no pin to measure.
        private static Vector3 MeasureAxle(Transform joint, Vector3 upper, Vector3 lower, Vector3 fallback)
        {
            for (int i = 0; i < joint.childCount; i++)
            {
                Transform child = joint.GetChild(i);
                if (!child.name.Contains("Pin")) continue;
                var mf = child.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                return WalkerAxle.LongestExtent(child.localToWorldMatrix, mf.sharedMesh.bounds).normalized;
            }
            return WalkerAxle.FromRestPose(upper, lower, fallback);
        }

        private static float PlaneAngle(Vector3 segment, Vector3 fwd, Vector3 up)
            => Mathf.Atan2(Vector3.Dot(segment, up), Vector3.Dot(segment, fwd));

        /// Lowest world point of the renderers under `t`, ignoring collision proxies.
        private static Vector3 LowestRendererPoint(Transform t, Vector3 fallback)
        {
            Vector3 best = fallback;
            float bestY = float.MaxValue;
            foreach (MeshRenderer r in t.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name.StartsWith("COL_")) continue;
                Bounds b = r.bounds;
                if (b.min.y < bestY)
                {
                    bestY = b.min.y;
                    best = new Vector3(b.center.x, b.min.y, b.center.z);
                }
            }
            return best;
        }
    }
}
