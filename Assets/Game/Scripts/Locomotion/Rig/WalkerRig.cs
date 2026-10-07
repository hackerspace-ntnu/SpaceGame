// Finds a machine's limbs on the imported rig and measures each one from its rest pose.
//
// This used to look up five bones by name -- Coxa_/Hip_/Knee_/Ankle_/Foot_ -- which pinned the
// whole architecture to exactly one leg shape. It now WALKS THE BONE CHAIN from the limb's root and
// works out what each joint is from its measured axle, so a two-joint stub, a five-joint insect leg
// and an arm all import with no code change.
//
// ─────────── the classification rule ───────────
//
// Measure every joint's axle down the chain, then find the LONGEST RUN OF MUTUALLY-PARALLEL AXLES.
// That run is the pitch chain -- the planar linkage the IK solves inside. Joints before it are base
// DOFs (the yaw coxa, and optionally a roll trochanter); joints after it are tip DOFs (the sole's
// roll hinge).
//
// The simpler rule -- consume parallel joints until one is not -- looks equivalent and is not. An
// insect leg is often coxa (yaw) -> TROCHANTER (roll) -> femur -> tibia -> tarsus, and the simple
// rule stops dead at the trochanter and misclassifies it as the sole. The longest-run rule handles
// it, and both shipping rigs -- Coxa -> Hip -> Knee -> Ankle -> Foot, a run of three with one
// non-parallel joint either side -- come out exactly as they did under name lookup.
//
// Every hinge axis is measured from the modelled pin mesh rather than guessed from the hierarchy.
// A pin is a cylinder, so its longest axis IS the hinge; re-exporting with different proportions,
// or rescaling the rig, keeps working with no code change.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public static partial class WalkerRig
    {
        /// Prefixes a limb's root bone may carry, most specific first. `Hip_` is the fallback for a
        /// planar rig that never had a yaw joint modelled.
        ///
        /// The first three all mean LEG, because every rig that existed before roles did uses them
        /// and must keep meaning exactly what it meant. `Arm_` is the one that means anything else.
        private static readonly string[] RootPrefixes = { "Limb_", "Coxa_", "Hip_", "Arm_" };

        /// The prefix that marks a limb the machine does not walk on.
        private const string ArmPrefix = "Arm_";

        /// Longest chain the walk will follow before concluding the rig is malformed. Well clear of
        /// any real limb; this exists so a cycle in the hierarchy cannot hang the import.
        private const int MaxChainLength = 12;

        /// The classic four-joint leg, root-most first. A rig that uses these names is assembled
        /// from them directly; anything else is discovered by walking the bone hierarchy.
        private static readonly string[] ChainPrefixes =
            { "Limb_", "Coxa_", "Hip_", "Knee_", "Ankle_", "Foot_" };

        /// Discovers every limb under `armature` and measures it against `body`. Limbs whose
        /// geometry cannot be trusted are skipped, with a warning naming them, rather than silently
        /// producing a linkage that tears itself apart.
        public static List<Limb> Build(Transform armature, Transform body)
        {
            var limbs = new List<Limb>();
            if (armature == null || body == null) return limbs;

            Transform[] all = armature.GetComponentsInChildren<Transform>(true);

            // A limb is claimed by the most specific root prefix present, so a rig carrying both
            // Coxa_N1 and Hip_N1 is one limb rooted at the coxa -- never two with the same id.
            //
            // The claim is per ROLE, not per id. A humanoid naming its legs Coxa_L/Coxa_R and its
            // arms Arm_L/Arm_R is the obvious thing to do and shares both ids across the two roles;
            // one namespace would silently drop the arms.
            var claimed = new HashSet<(LimbRole, string)>();
            foreach (string prefix in RootPrefixes)
            {
                LimbRole role = RoleFor(prefix);
                foreach (Transform t in all)
                {
                    if (!t.name.StartsWith(prefix)) continue;
                    string id = t.name.Substring(prefix.Length);
                    if (id.Length == 0 || !claimed.Add((role, id))) continue;

                    Limb limb = Discover(all, t, id, role);
                    if (limb == null || !Measure(limb, body)) continue;

                    limb.Role = role;
                    limbs.Add(limb);
                }
            }
            return limbs;
        }

        /// The armature carrying the limb chains. The imported model contains more than one
        /// armature-looking node, so pick whichever holds the most limb roots.
        public static Transform FindArmature(Transform root)
        {
            Transform best = null;
            int bestCount = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                int n = 0;
                foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
                    if (IsRootBone(c.name)) n++;
                if (n > bestCount) { bestCount = n; best = t; }
            }
            return best != null ? best : root;
        }

        private static LimbRole RoleFor(string prefix)
            => prefix == ArmPrefix ? LimbRole.Arm : LimbRole.Leg;

        private static bool IsRootBone(string name)
        {
            foreach (string prefix in RootPrefixes)
                if (name.StartsWith(prefix)) return true;
            return false;
        }

        // ─────────── walking the chain ───────────

        /// Assembles the limb's chain of joints, then classifies what it found.
        ///
        /// Two ways in, and the order matters. A rig using the classic Coxa/Hip/Knee/Ankle/Foot
        /// names is assembled BY NAME, across the whole armature, because that is exactly what the
        /// name lookup this replaced did -- and those rigs are shipping. Their joints are not
        /// necessarily parented to one another: the pins and plates hang off the bones, and a
        /// hierarchy walk would be at the mercy of how the model happened to be exported.
        ///
        /// Everything else is discovered by WALKING the bone hierarchy, which is what lets a limb
        /// shape nobody has named yet import with no code change. Both paths hand the same chain to
        /// the same classifier, so a limb's shape is always decided by measurement.
        ///
        /// An ARM never takes the name path. `ChainPrefixes` is the classic LEG's vocabulary, and
        /// it is looked up by id across the whole armature -- so a humanoid with `Arm_L` and
        /// `Coxa_L` would have its arm assembled out of the leg's bones. An arm's chain is always
        /// found by walking the hierarchy, which is also what leaves its joints free to be named
        /// whatever they actually are: a shoulder is not a coxa.
        private static Limb Discover(Transform[] all, Transform root, string id, LimbRole role)
        {
            List<Transform> named = role == LimbRole.Leg ? NamedChain(all, id) : new List<Transform>();
            List<Transform> chain = named.Count >= 3 ? named : WalkChain(root, id);
            if (chain == null) return null;

            if (chain.Count < 2)
            {
                Debug.LogWarning(
                    $"[Walker] Limb {id} has no joints below its root ({root.name}). Joints are " +
                    "found by their *Pin* mesh child; check the export.", root);
                return null;
            }

            return Classify(chain, id);
        }

        /// The classic leg, looked up by name across the armature. Contiguous from the root: a gap
        /// ends the chain, so Coxa/Hip/Knee with no Ankle is a three-joint limb rather than an error.
        private static List<Transform> NamedChain(Transform[] all, string id)
        {
            var chain = new List<Transform>();
            foreach (string prefix in ChainPrefixes)
            {
                Transform joint = Find(all, prefix + id);
                if (joint == null)
                {
                    // Limb_ and Coxa_ are alternative roots, so missing one is not a gap.
                    if (chain.Count == 0) continue;
                    break;
                }
                chain.Add(joint);
            }
            return chain;
        }

        private static Transform Find(Transform[] pool, string name)
        {
            foreach (Transform t in pool)
                if (t.name == name) return t;
            return null;
        }

        /// Follows the bone chain from `root`, by geometry rather than by name.
        private static List<Transform> WalkChain(Transform root, string id)
        {
            var chain = new List<Transform> { root };

            // Only bones carrying a *Pin* mesh are followed. This is a hazard name lookup never
            // had: a chain walk will happily wander into leaf bones (`_end`, which an export
            // without add_leaf_bones=False produces) and into the COL_ collision boxes that hang
            // off every joint. A pin is what says "this is a real hinge".
            while (chain.Count < MaxChainLength)
            {
                Transform current = chain[chain.Count - 1];
                Transform next = null;
                Transform namedNext = null;
                int candidates = 0;

                for (int i = 0; i < current.childCount; i++)
                {
                    Transform child = current.GetChild(i);
                    if (!IsJoint(child)) continue;
                    candidates++;
                    if (next == null) next = child;
                    if (namedNext == null && IsChainName(child.name)) namedNext = child;
                }

                // A joint carrying a mounting empty as well as the next bone is not really a fork.
                // A name settles it where there is one; measurement decides everything after.
                if (candidates > 1 && namedNext != null)
                {
                    next = namedNext;
                }
                else if (candidates > 1)
                {
                    // A limb that genuinely forks is not a chain, and guessing which branch is
                    // "the" limb is a coin toss that shows up as a leg bending the wrong way.
                    Debug.LogWarning(
                        $"[Walker] Limb {id} branches at {current.name}: {candidates} joint " +
                        "children and none named as a limb joint. Not guessing.", current);
                    return null;
                }

                if (next == null) break;
                chain.Add(next);

                // A bone explicitly named Foot_<id> is the sole, whatever its pin measures. The
                // name wins here so a mis-measured pin cannot silently truncate the chain.
                if (next.name.StartsWith("Foot_")) break;
            }

            return chain;
        }

        private static bool IsChainName(string name)
        {
            foreach (string prefix in ChainPrefixes)
                if (name.StartsWith(prefix)) return true;
            return false;
        }

        /// A joint is a bone with a pin to measure, or the explicitly named sole.
        private static bool IsJoint(Transform t)
        {
            if (t.name.StartsWith("COL_")) return false;
            if (t.name.EndsWith("_end")) return false;
            if (t.name.StartsWith("Foot_")) return true;
            return FindPin(t) != null;
        }

        private static Transform FindPin(Transform joint)
        {
            for (int i = 0; i < joint.childCount; i++)
            {
                Transform child = joint.GetChild(i);
                if (!child.name.Contains("Pin")) continue;
                var mf = child.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) return child;
            }
            return null;
        }

        /// Splits a measured chain into base DOFs, the pitch chain, and tip DOFs.
        private static Limb Classify(List<Transform> chain, string id)
        {
            int n = chain.Count;

            // The sole, if the rig names one, is not a pitch candidate however its pin measures.
            int forcedTip = -1;
            for (int i = 1; i < n; i++)
                if (chain[i].name.StartsWith("Foot_")) { forcedTip = i; break; }
            int pitchLimit = forcedTip >= 0 ? forcedTip : n;

            var axles = new Vector3[n];
            for (int i = 0; i < n; i++) axles[i] = ProvisionalAxle(chain, i);

            int bestStart = 0, bestLength = 1;
            for (int i = 0; i < pitchLimit; i++)
            {
                int length = 1;
                for (int j = i + 1; j < pitchLimit; j++)
                {
                    bool parallel = true;
                    for (int k = i; k < j && parallel; k++)
                        parallel = WalkerAxle.AreParallel(axles[k], axles[j]);
                    if (!parallel) break;
                    length++;
                }
                if (length > bestLength) { bestLength = length; bestStart = i; }
            }

            var pitch = new Transform[bestLength];
            for (int i = 0; i < bestLength; i++) pitch[i] = chain[bestStart + i];

            var held = new List<Transform>();
            for (int i = 1; i < bestStart; i++) held.Add(chain[i]);
            if (held.Count > 0)
                Debug.LogWarning(
                    $"[Walker] Limb {id} has {held.Count} base joint(s) between its yaw axle and " +
                    "its pitch chain (a roll trochanter, most likely). They are measured but held " +
                    "at rest: the solver drives one yaw joint and one tip roll joint.", chain[1]);

            int afterPitch = bestStart + bestLength;
            Transform tip = null;
            if (forcedTip >= 0) tip = chain[forcedTip];
            else if (afterPitch < n) tip = chain[afterPitch];

            // Anything past the sole cannot be driven. Toe bones belong to the sole's mesh, not to
            // the IK, so say so rather than pretending they are being posed.
            int tipIndex = tip != null ? chain.IndexOf(tip) : n - 1;
            if (tipIndex >= 0 && tipIndex < n - 1)
                Debug.LogWarning(
                    $"[Walker] Limb {id} has {n - 1 - tipIndex} joint(s) below its sole " +
                    $"({chain[tipIndex + 1].name}). They are not driven.", chain[tipIndex + 1]);

            // Surfaces the one failure the old name lookup would have caught and this cannot: a
            // pitch pin measuring far enough out of true that the run breaks around it.
            WarnOnMisclassifiedClassicJoints(chain, id, bestStart, afterPitch);

            return new Limb
            {
                Id = id,
                Root = bestStart > 0 ? chain[0] : null,
                Pitch = pitch,
                Tip = tip,
                HeldBase = held.ToArray(),
            };
        }

        private static void WarnOnMisclassifiedClassicJoints(
            List<Transform> chain, string id, int pitchStart, int pitchEnd)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                if (i >= pitchStart && i < pitchEnd) continue;
                string name = chain[i].name;
                if (!name.StartsWith("Hip_") && !name.StartsWith("Knee_") &&
                    !name.StartsWith("Ankle_")) continue;

                Debug.LogWarning(
                    $"[Walker] Limb {id}: {name} measured outside the pitch chain, so its pin is " +
                    "not parallel to the others. The limb will solve with a shorter linkage than " +
                    "the model has; check the pin's orientation in the source file.", chain[i]);
            }
        }

        // ─────────── measurement ───────────

        /// Axle used for classification, before the limb's sign conventions are known.
        private static Vector3 ProvisionalAxle(List<Transform> chain, int i)
        {
            Vector3 outward = SegmentOut(chain, i);
            Vector3 inward = i > 0 ? SegmentOut(chain, i - 1) : outward;
            return MeasureAxle(chain[i], inward, outward, Vector3.up);
        }

        /// Bone-to-bone vector out of joint `i`. The last joint has no next bone, so it measures to
        /// whatever its meshes reach down to -- which is the contact point.
        private static Vector3 SegmentOut(List<Transform> chain, int i)
        {
            if (i + 1 < chain.Count) return chain[i + 1].position - chain[i].position;
            return LowestRendererPoint(chain[i], chain[i].position) - chain[i].position;
        }

        private static bool Measure(Limb limb, Transform body)
        {
            Transform[] pitch = limb.Pitch;
            int count = pitch.Length;

            var g = new WalkerLimbGeometry
            {
                HasYaw = limb.Root != null,
                RestRootLocal = limb.Root != null ? limb.Root.localRotation : Quaternion.identity,
                Pitch = new WalkerLimbSegment[count],
            };

            // Contact first: the lowest point of the tip joint's own meshes, which is what actually
            // touches the ground. Not the toe bone's tip, and not the last hinge.
            Transform tipJoint = pitch[count - 1];
            Vector3 contact = LowestRendererPoint(tipJoint, tipJoint.position);

            // Segment vectors down the chain, the last one running to the contact point.
            var segments = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                segments[i] = i + 1 < count
                    ? pitch[i + 1].position - pitch[i].position
                    : contact - tipJoint.position;
            }

            // Yaw axis first: it is the limb's "up", and every other measurement is taken against
            // it. A planar limb has no yaw joint, so the body's own up stands in.
            Vector3 yawAxis = body.up;
            if (g.HasYaw)
            {
                yawAxis = WalkerAxle.MatchSense(
                    MeasureAxle(limb.Root, segments[0], segments[Mathf.Min(1, count - 1)], body.up),
                    body.up);
            }

            // The middle joint's pin is the largest on a real leg, so it is the most reliable
            // measurement. Take it as the reference sense and make the others agree.
            int reference = count / 2;
            var axles = new Vector3[count];
            axles[reference] = MeasureAxle(pitch[reference], InSegment(segments, reference),
                                           segments[reference], yawAxis);
            for (int i = 0; i < count; i++)
            {
                if (i == reference) continue;
                axles[i] = WalkerAxle.MatchSense(
                    MeasureAxle(pitch[i], InSegment(segments, i), segments[i], yawAxis),
                    axles[reference]);
            }

            for (int i = 0; i < count; i++)
            {
                g.Pitch[i].Length = segments[i].magnitude;
                if (g.Pitch[i].Length >= 1e-4f) continue;

                if (i < count - 1)
                {
                    Debug.LogWarning(
                        $"[Walker] Limb {limb.Id} has a zero-length segment at {pitch[i].name}; " +
                        "skipped.", pitch[i]);
                    return false;
                }

                // No tip geometry at all: the last hinge IS the contact.
                g.Pitch[i].Length = 1e-3f;
                segments[i] = -yawAxis * 1e-3f;
                contact = tipJoint.position + segments[i];
            }

            // The plane basis. A pin is a line, not a direction, so its measured sense is
            // arbitrary; force `fwd` to point outboard toward the contact and carry the axles with
            // it. Fixing the sign once here is what keeps every yaw angle downstream readable.
            Vector3 planeFwd = WalkerAxle.PlaneForward(axles[reference], yawAxis);
            if (Vector3.Dot(planeFwd, contact - pitch[0].position) < 0f)
            {
                planeFwd = -planeFwd;
                for (int i = 0; i < count; i++) axles[i] = -axles[i];
            }

            g.YawAxisBody = body.InverseTransformDirection(yawAxis).normalized;
            if (g.HasYaw)
                g.YawAxisLocalRoot = limb.Root.InverseTransformDirection(yawAxis).normalized;
            g.RestFwdBody = body.InverseTransformDirection(planeFwd).normalized;

            for (int i = 0; i < count; i++)
            {
                g.Pitch[i].AxleLocal = pitch[i].InverseTransformDirection(axles[i]).normalized;
                g.Pitch[i].RestLocal = pitch[i].localRotation;
                g.Pitch[i].RestAngle = PlaneAngle(segments[i], planeFwd, yawAxis);
            }

            g.BendSign = 1f;
            if (count >= 2)
            {
                g.BendSign = Mathf.Sign(
                    Mathf.Cos(g.Pitch[0].RestAngle) * Mathf.Sin(g.Pitch[1].RestAngle) -
                    Mathf.Sin(g.Pitch[0].RestAngle) * Mathf.Cos(g.Pitch[1].RestAngle));
                if (Mathf.Approximately(g.BendSign, 0f)) g.BendSign = 1f;
            }

            g.ContactLocalTip = tipJoint.InverseTransformPoint(contact);

            Vector3 anchorToContact = contact - pitch[0].position;
            g.RestFootRadius = Vector3.ProjectOnPlane(anchorToContact, yawAxis).magnitude;

            // Stage 1 is exact only because the first pitch joint sits ON the yaw axis -- that is
            // why yawing the root does not move it, and why the two stages cannot interfere. A rig
            // that breaks it still solves, just approximately, so this warns rather than refusing.
            if (g.HasYaw)
            {
                Vector3 offAxis = Vector3.ProjectOnPlane(pitch[0].position - limb.Root.position, yawAxis);
                float tolerance = Mathf.Max(g.TotalLength * 0.02f, 1e-3f);
                if (offAxis.magnitude > tolerance)
                    Debug.LogWarning(
                        $"[Walker] Limb {limb.Id}: {pitch[0].name} sits {offAxis.magnitude:F3} off " +
                        $"the yaw axis. The IK assumes it lies on it, so yaw and pitch will " +
                        "interfere slightly and a planted foot may creep.", pitch[0]);
            }

            // The sole's roll hinge, if the model has one. Measured from its pin like every other
            // joint, and signed to agree with the limb's outboard direction so a positive roll
            // means the same thing on every limb.
            g.HasRoll = limb.Tip != null;
            if (g.HasRoll)
            {
                Vector3 rollAxis = WalkerAxle.MatchSense(
                    MeasureAxle(limb.Tip, segments[count - 1], segments[count - 1], planeFwd), planeFwd);
                g.RollAxisLocalTip = limb.Tip.InverseTransformDirection(rollAxis).normalized;
                g.RestTipLocal = limb.Tip.localRotation;

                if (Mathf.Abs(Vector3.Dot(rollAxis.normalized, yawAxis)) > 0.05f)
                    Debug.LogWarning(
                        $"[Walker] Limb {limb.Id} roll axis is not horizontal; the sole will tilt " +
                        "oddly across slopes.", limb.Tip);
            }

            limb.Geometry = g;
            return true;
        }

        private static Vector3 InSegment(Vector3[] segments, int i)
            => i > 0 ? segments[i - 1] : segments[i];

        /// Hinge axis of one joint: the longest axis of its modelled pin, or the rest pose's own
        /// plane normal when the model has no pin to measure.
        private static Vector3 MeasureAxle(Transform joint, Vector3 inward, Vector3 outward,
                                           Vector3 fallback)
        {
            Transform pin = FindPin(joint);
            if (pin != null)
            {
                var mf = pin.GetComponent<MeshFilter>();
                return WalkerAxle.LongestExtent(pin.localToWorldMatrix, mf.sharedMesh.bounds).normalized;
            }
            return WalkerAxle.FromRestPose(inward, outward, fallback);
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
