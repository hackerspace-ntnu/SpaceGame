using System;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The frame a held item is posed in: a position and rotation expressed in the hand bone's own
    /// local space, describing not where the bone points but where the <em>grip</em> is.
    ///
    /// <para>
    /// This exists because a hand bone's axes are meaningless. The player's rig is Mixamo, its
    /// bone rotations are whatever the exporter wrote, and the next character to arrive will have
    /// different ones. Parenting an item to the bone and copying the bone's rotation — which is
    /// what this project did — gives every item a different arbitrary angle, and the angle changes
    /// when the rig is re-exported.
    /// </para>
    /// <para>
    /// So the frame is derived from anatomy instead, out of the finger bones, whose offsets from
    /// the hand are constant in hand-local space no matter what the animation is doing:
    /// </para>
    /// <list type="bullet">
    /// <item>+Y is out the thumb side — where a torch's flame would be.</item>
    /// <item>+Z is out the back of the hand — where an aimed item points.</item>
    /// <item>the origin sits in the middle of the fist, not at the wrist.</item>
    /// </list>
    /// <para>
    /// That means the same thing on every rig, so an <see cref="ItemGrip"/> tuned once against the
    /// player still holds correctly on an NPC built from a different skeleton.
    /// </para>
    /// </summary>
    public readonly struct HandGripFrame
    {
        /// <summary>Grip origin in hand-bone local space.</summary>
        public readonly Vector3 LocalPosition;

        /// <summary>Grip orientation in hand-bone local space.</summary>
        public readonly Quaternion LocalRotation;

        /// <summary>How this frame was arrived at. Logged, not branched on.</summary>
        public readonly string Source;

        /// <summary>Rough wrist-to-knuckle length in metres. Scales the grip origin to hand size.</summary>
        public readonly float HandLength;

        public HandGripFrame(Vector3 localPosition, Quaternion localRotation, float handLength, string source)
        {
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            HandLength = handLength <= 0f ? DefaultHandLength : handLength;
            Source = source;
        }

        /// <summary>The bone's own axes, unaltered. What this project used to do everywhere.</summary>
        public static HandGripFrame Identity(string reason) =>
            new HandGripFrame(Vector3.zero, Quaternion.identity, DefaultHandLength, reason);

        // ── Tuning constants ─────────────────────────────────────────────────────
        //
        // All three are fractions of the hand's own length rather than absolute metres, so a
        // giant's fist and a child's hold the same item in the same relative place.

        /// <summary>How far along the fingers the grip sits. 0 is the wrist, 1 the knuckles.</summary>
        private const float GripDepthAlongFingers = 0.45f;

        /// <summary>How far off the palm the grip sits — the middle of the tunnel a fist makes.</summary>
        private const float GripLiftOffPalm = 0.18f;

        /// <summary>Stand-in hand length for rigs that give us nothing to measure.</summary>
        private const float DefaultHandLength = 0.17f;

        private static readonly string[] IndexHints  = { "index" };
        private static readonly string[] MiddleHints = { "middle" };
        private static readonly string[] PinkyHints  = { "pinky", "little" };
        private static readonly string[] ThumbHints  = { "thumb" };

        // ── Derivation ───────────────────────────────────────────────────────────

        /// <summary>
        /// Work out the grip frame for a hand.
        /// </summary>
        /// <param name="animator">The rig's animator, used for humanoid bone lookup. May be null.</param>
        /// <param name="hand">The resolved hand bone. Required.</param>
        /// <param name="isRightHand">Which hand, for humanoid bone lookup.</param>
        public static HandGripFrame Derive(Animator animator, Transform hand, bool isRightHand)
        {
            if (hand == null) return Identity("no hand bone");

            // Anatomy first: fingers give an unambiguous frame and are the only source that stays
            // correct across re-exports.
            if (TryDeriveFromFingers(animator, hand, isRightHand, out HandGripFrame fromFingers))
                return fromFingers;

            // No fingers on this rig. The forearm still tells us which way the hand points, which
            // fixes one axis of three; the roll around the arm is a guess.
            if (TryDeriveFromForearm(animator, hand, isRightHand, out HandGripFrame fromArm))
                return fromArm;

            return Identity("rig exposes neither finger nor forearm bones");
        }

        /// <summary>
        /// The good path. Three finger roots and a thumb pin the frame completely, and every sign
        /// in it is decided by measurement rather than by knowing which hand this is — a left hand
        /// falls out of the same arithmetic.
        /// </summary>
        private static bool TryDeriveFromFingers(Animator animator, Transform hand, bool isRightHand,
                                                 out HandGripFrame frame)
        {
            frame = default;

            Transform index  = FindFinger(animator, hand, isRightHand, HumanBodyBones.RightIndexProximal,  HumanBodyBones.LeftIndexProximal,  IndexHints);
            Transform middle = FindFinger(animator, hand, isRightHand, HumanBodyBones.RightMiddleProximal, HumanBodyBones.LeftMiddleProximal, MiddleHints);
            Transform pinky  = FindFinger(animator, hand, isRightHand, HumanBodyBones.RightLittleProximal, HumanBodyBones.LeftLittleProximal, PinkyHints);
            Transform thumb  = FindFinger(animator, hand, isRightHand, HumanBodyBones.RightThumbProximal,  HumanBodyBones.LeftThumbProximal,  ThumbHints);

            if (index == null || pinky == null || thumb == null) return false;

            // Hand-local, so constant regardless of what the animation is doing to the arm.
            Vector3 indexLocal  = hand.InverseTransformPoint(index.position);
            Vector3 pinkyLocal  = hand.InverseTransformPoint(pinky.position);
            Vector3 thumbLocal  = hand.InverseTransformPoint(thumb.position);
            Vector3 middleLocal = middle != null
                ? hand.InverseTransformPoint(middle.position)
                : (indexLocal + pinkyLocal) * 0.5f;

            float handLength = middleLocal.magnitude;
            if (handLength < 1e-4f) return false;

            Vector3 fingersDir = middleLocal / handLength;

            // Across the knuckles. Orthogonalised against the fingers so the frame stays square
            // even on a rig whose knuckle row is not perfectly perpendicular to the fingers.
            Vector3 sideDir = pinkyLocal - indexLocal;
            sideDir = Vector3.ProjectOnPlane(sideDir, fingersDir);
            if (sideDir.sqrMagnitude < 1e-8f) return false;
            sideDir.Normalize();

            // Which end of the knuckle row the thumb is on. That end is the top of anything held in
            // a fist — the flame end of a torch, the head of a hammer. Measured rather than assumed
            // from handedness because it is the one thing the thumb reports unambiguously: it sits
            // far along this axis (~0.017 on the player's rig) and the sign is never in doubt.
            Vector3 thumbOffset = Vector3.ProjectOnPlane(thumbLocal, fingersDir);
            Vector3 thumbSide = Vector3.Dot(thumbOffset, sideDir) >= 0f ? sideDir : -sideDir;

            // Out of the palm. The sign comes from handedness, NOT from the thumb.
            //
            // Asking the thumb was the obvious thing and it is wrong: in a rest pose the thumb lies
            // very nearly IN the plane of the hand, so its out-of-plane component is tiny — 0.009
            // against 0.017 sideways on the player's rig — and noise at that scale flips the whole
            // frame. When it flipped, every held item moved to the back of the hand and every gun
            // pointed into the palm.
            //
            // Handedness has no such ambiguity. Fingers forward and index→pinky rightwards is a
            // palm-down right hand, and a left hand is its mirror.
            Vector3 palmNormal = Vector3.Cross(sideDir, fingersDir).normalized;
            if (!isRightHand) palmNormal = -palmNormal;

            // An item points the way the fingers point.
            //
            // This was "out of the back of the hand" and that was wrong by a quarter turn. The
            // rig settles it: sampling the pose the artists authored for holding a gun
            // (HumanM@Gun_Aim01, the state the Hold bool drives to) and asking where a barrel
            // would end up gave 94 degrees of error against the character's forward. Along the
            // fingers gives 4.
            //
            // It is also the more sensible reading of a fist. The fingers curl around a pistol
            // grip, but the barrel carries on down the line of the hand and forearm; nothing
            // useful points out through the knuckles.
            //
            // Taken as fingersDir rather than as cross(thumbSide, palmNormal), which is the same
            // vector on a right hand and its negative on a left — that form would have left a
            // left-handed item pointing backwards.
            Vector3 forward = fingersDir;
            Vector3 up = thumbSide;

            if (Vector3.Cross(forward, up).sqrMagnitude < 1e-8f) return false;

            Vector3 origin = fingersDir * (handLength * GripDepthAlongFingers)
                           + palmNormal * (handLength * GripLiftOffPalm);

            frame = new HandGripFrame(origin, Quaternion.LookRotation(forward, up), handLength,
                                      "finger bones");
            return true;
        }

        /// <summary>
        /// The degraded path, for rigs whose hands end at the wrist. The forearm fixes which way
        /// the fingers would point; the roll around that axis is taken from the character's own
        /// up, which is right for a hand held naturally and wrong for one that is not. Items on
        /// such a rig will want an <see cref="ItemGrip.RotationOffset"/>.
        /// </summary>
        private static bool TryDeriveFromForearm(Animator animator, Transform hand, bool isRightHand,
                                                 out HandGripFrame frame)
        {
            frame = default;

            Transform forearm = null;
            if (animator != null && animator.isHuman)
            {
                forearm = animator.GetBoneTransform(isRightHand
                    ? HumanBodyBones.RightLowerArm
                    : HumanBodyBones.LeftLowerArm);
            }
            if (forearm == null) forearm = hand.parent;
            if (forearm == null) return false;

            Vector3 armLocal = hand.InverseTransformPoint(forearm.position);
            float armLength = armLocal.magnitude;
            if (armLength < 1e-4f) return false;

            // Away from the elbow is the way the fingers would continue.
            Vector3 fingersDir = -armLocal / armLength;

            // Roll: the character's up, brought into hand space and squared against the fingers.
            Transform characterRoot = animator != null ? animator.transform : hand.root;
            Vector3 worldUp = characterRoot != null ? characterRoot.up : Vector3.up;
            Vector3 upLocal = Vector3.ProjectOnPlane(hand.InverseTransformDirection(worldUp), fingersDir);
            if (upLocal.sqrMagnitude < 1e-8f)
            {
                // Hand pointing straight up or down. Any perpendicular will do; pick a stable one.
                upLocal = Vector3.ProjectOnPlane(Vector3.forward, fingersDir);
                if (upLocal.sqrMagnitude < 1e-8f) upLocal = Vector3.ProjectOnPlane(Vector3.right, fingersDir);
            }
            upLocal.Normalize();

            // Same convention as the finger path: an item points the way the hand points, and its
            // own top is the remaining axis. Here that top is taken from the character's up rather
            // than from a thumb this rig does not have, so it is only as good as the assumption
            // that the wrist is not rolled.
            Vector3 forward = fingersDir;
            Vector3 up = upLocal;

            float handLength = Mathf.Clamp(armLength * 0.4f, 0.05f, 0.5f);
            Vector3 origin = fingersDir * (handLength * GripDepthAlongFingers);

            frame = new HandGripFrame(origin, Quaternion.LookRotation(forward, up), handLength,
                                      "forearm direction (no finger bones — expect to tune ItemGrip.rotationOffset)");
            return true;
        }

        // ── Bone lookup ──────────────────────────────────────────────────────────

        /// <summary>
        /// Humanoid mapping first, then a name search under the hand.
        ///
        /// <para>
        /// The name search is not belt-and-braces. Unity's humanoid auto-configuration maps the
        /// body and routinely leaves fingers unmapped even when the bones are right there in the
        /// hierarchy — <c>GetBoneTransform(RightIndexProximal)</c> returns null on rigs whose
        /// <c>RightHandIndex1</c> is a direct child of the hand.
        /// </para>
        /// </summary>
        private static Transform FindFinger(Animator animator, Transform hand, bool isRightHand,
                                            HumanBodyBones rightBone, HumanBodyBones leftBone,
                                            string[] nameHints)
        {
            if (animator != null && animator.isHuman)
            {
                Transform mapped = animator.GetBoneTransform(isRightHand ? rightBone : leftBone);
                if (mapped != null && mapped.IsChildOf(hand)) return mapped;
            }

            Transform[] descendants = hand.GetComponentsInChildren<Transform>(true);
            Transform best = null;
            int bestDepth = int.MaxValue;

            for (int i = 0; i < descendants.Length; i++)
            {
                Transform t = descendants[i];
                if (t == hand) continue;
                if (!MatchesAny(t.name, nameHints)) continue;

                // Shallowest match wins: we want the proximal joint, and every distal joint of the
                // same finger also carries the finger's name.
                int depth = DepthBelow(t, hand);
                if (depth < bestDepth)
                {
                    bestDepth = depth;
                    best = t;
                }
            }

            return best;
        }

        private static bool MatchesAny(string name, string[] hints)
        {
            for (int i = 0; i < hints.Length; i++)
                if (name.IndexOf(hints[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static int DepthBelow(Transform t, Transform root)
        {
            int depth = 0;
            while (t != null && t != root)
            {
                depth++;
                t = t.parent;
            }
            return depth;
        }
    }
}
