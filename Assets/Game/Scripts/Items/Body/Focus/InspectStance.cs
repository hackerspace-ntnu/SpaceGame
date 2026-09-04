using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Holds a body's arms out while its gear is being looked at.
    ///
    /// <para>
    /// The gear screen is a camera flown round to look AT the character, and what it is looking at
    /// is worn equipment. An idle stance hides most of it: arms at the sides put both forearms —
    /// and both gauntlets — against the hips, and they bury a worn wing between the arm and the
    /// body it is stretched to. An arms-out stance is the pose that shows worn gear, which is why
    /// every character sheet in every game that has one uses it.
    /// </para>
    /// <para>
    /// A <b>relaxed 45 degrees</b>, not a full T. A true T reads as a mannequin on a rack rather
    /// than as a person standing still, and it is a long way from any pose the character is ever
    /// in — which is the whole objection to it. 45 keeps both forearms clear of the hips and still
    /// looks like somebody holding their arms out.
    /// </para>
    /// <para>
    /// <b>Bone writes, not a clip.</b> An animation would need authoring, an Animator state and a
    /// transition, and it would have to be mirrored onto every rig that can open this screen. Two
    /// aimed bones per arm need none of that and work on any humanoid rig, because the aim is
    /// expressed as "point the segment this way in the world" rather than as an angle in some
    /// bone's own frame — the same reason <c>ForearmSeat</c> derives its axes instead of typing
    /// them. It has to run in <c>LateUpdate</c>: the Animator writes the pose in between, and
    /// anything written before it is simply overwritten.
    /// </para>
    /// <para>
    /// Nothing is restored on the way out and nothing needs to be. These are pose writes, not
    /// state: stop making them and the Animator owns the arms again on the very next frame. That
    /// is also what makes this safe on a replicated body — it is local presentation on the machine
    /// whose player opened the screen, and no peer is told anything.
    /// </para>
    /// </summary>
    public static class InspectStance
    {
        /// <summary>
        /// How far below horizontal each arm hangs, degrees. 45 — a relaxed A, not a starfish
        /// (user, 2026-09-03: "the T pose needs to be much more low key").
        ///
        /// <para>
        /// <b>The worn wingsuit's leading edge is authored along exactly this line</b>
        /// (`wingsuit_worn.py: INSPECT_DROOP`), so the two numbers are one number and moving this
        /// one alone floats the cloth off the arm. It lives here rather than only on the prefab
        /// so the editor's worn-gear preview and the shipped screen cannot disagree about the
        /// pose they are showing gear in.
        /// </para>
        /// </summary>
        public const float DefaultDroop = 45f;

        /// <summary>
        /// How far below horizontal each arm rests when no worn or carried item asks for the
        /// fuller <see cref="DefaultDroop"/> pose — every gauntlet-only session on the gear
        /// screen, which is most of them. Close enough to a natural hang to still read as idle,
        /// but enough of an outward lift to keep both forearms — and their gauntlet sites —
        /// clear of the torso's hit-box (user, 2026-09-04: clicking a gauntlet was landing on
        /// the torso once arms-down became the default for everything but the wingsuit).
        /// </summary>
        public const float SeparationDroop = 70f;

        /// <summary>
        /// Put both arms out. <paramref name="body"/> supplies the frame — its own right and
        /// forward, so the pose follows the character rather than the world's axes.
        /// </summary>
        /// <param name="droopDegrees">How far below horizontal each arm hangs. Zero is a true T;
        /// <see cref="DefaultDroop"/> is what the screen and the worn wingsuit are built to.</param>
        /// <param name="forwardDegrees">How far in front of the shoulder line each arm reaches.</param>
        public static void Apply(Animator animator, Transform body,
                                 float droopDegrees, float forwardDegrees)
        {
            if (animator == null || body == null || !animator.isHuman) return;

            PoseArm(animator, body, +1f, droopDegrees, forwardDegrees,
                    HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                    HumanBodyBones.RightHand);
            PoseArm(animator, body, -1f, droopDegrees, forwardDegrees,
                    HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
                    HumanBodyBones.LeftHand);
        }

        private static void PoseArm(Animator animator, Transform body, float side,
                                    float droopDegrees, float forwardDegrees,
                                    HumanBodyBones upperBone, HumanBodyBones lowerBone,
                                    HumanBodyBones handBone)
        {
            Transform upper = animator.GetBoneTransform(upperBone);
            Transform lower = animator.GetBoneTransform(lowerBone);
            if (upper == null || lower == null) return;

            Vector3 outward = body.right * side;
            Vector3 direction = (outward
                                 + Vector3.down * Mathf.Tan(droopDegrees * Mathf.Deg2Rad)
                                 + body.forward * Mathf.Tan(forwardDegrees * Mathf.Deg2Rad))
                                .normalized;

            // Upper arm first, and the order is load-bearing: aiming it MOVES the elbow, so the
            // forearm's own aim has to be computed from where the elbow ended up. Read the
            // positions back rather than predicting them — Unity applies a parent's rotation to
            // its children's world transforms as soon as it is written.
            Aim(upper, lower, direction);

            Transform hand = animator.GetBoneTransform(handBone);
            if (hand != null) Aim(lower, hand, direction);
        }

        /// <summary>
        /// Turn <paramref name="from"/> so the segment from it to <paramref name="to"/> points
        /// along <paramref name="direction"/>, leaving its twist about that segment alone.
        /// </summary>
        private static void Aim(Transform from, Transform to, Vector3 direction)
        {
            Vector3 current = to.position - from.position;

            // A zero-length segment has no direction to turn, and FromToRotation of one is
            // undefined — a rig with a coincident elbow and wrist would otherwise inject a NaN
            // rotation into the hierarchy, which propagates to every child and never recovers.
            if (current.sqrMagnitude < 1e-8f) return;

            from.rotation = Quaternion.FromToRotation(current, direction) * from.rotation;
        }
    }
}
