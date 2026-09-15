using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// How far a worn forearm device has to be swung to point at what the player is looking at.
    ///
    /// <para>
    /// Separated from <see cref="PlayerArmAim"/> for the reason <see cref="HeadAim"/> is separated
    /// from <see cref="PlayerHeadLook"/>: nothing here has state, a frame, an Animator or a network
    /// session in it, so the rules can be reasoned about — and tested — on their own.
    /// </para>
    /// </summary>
    public static class ArmAim
    {
        /// <summary>
        /// The point the arm is aimed AT, rather than the direction it is aimed along.
        ///
        /// <para>
        /// A device on the wrist is not at the eye, so pointing it parallel to the look leaves its
        /// beam beside the crosshair by however far the wrist is from the eye — worst on near
        /// surfaces, which is exactly where a torch is looked at. Converging on a point puts the
        /// two together at <paramref name="range"/> and keeps the error small either side of it.
        /// </para>
        /// </summary>
        public static Vector3 Convergence(Vector3 eye, Vector3 forward, float range) =>
            eye + forward * Mathf.Max(0.01f, range);

        /// <summary>
        /// The rotation that swings <paramref name="from"/> onto <paramref name="to"/>, limited to
        /// <paramref name="maxDegrees"/>.
        ///
        /// <para>
        /// The limit is a SHOULDER limit, not a preference: this is laid on top of an animated pose,
        /// and an unlimited swing answers "look behind you" by putting the arm through the chest. At
        /// the limit the arm stops following and the beam falls behind the crosshair, which is the
        /// readable failure of the two.
        /// </para>
        /// </summary>
        public static Quaternion Swing(Vector3 from, Vector3 to, float maxDegrees)
        {
            if (from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f) return Quaternion.identity;

            Quaternion full = Quaternion.FromToRotation(from.normalized, to.normalized);
            float angle = Quaternion.Angle(Quaternion.identity, full);
            float limit = Mathf.Max(0f, maxDegrees);

            if (angle <= limit || angle < 1e-4f) return full;

            // A share of the full swing rather than a rebuilt one: the axis is already right, and
            // only how far along it the arm travels is in question. The same power HeadAim splits a
            // neck turn with — one bone taking part of a rotation is one rule, not two.
            return HeadAim.Share(full, limit / angle);
        }

        /// <summary>
        /// Swing an arm so <paramref name="pointer"/> faces <paramref name="target"/>, by
        /// <paramref name="weight"/> of the way.
        ///
        /// <para>
        /// The shoulder takes its share first and the elbow closes what is left, with the pointer
        /// re-read in between: it hangs off both bones, so by the time the elbow is written it
        /// already carries the shoulder's share and reading one swing and splitting it would apply
        /// that share twice.
        /// </para>
        /// <para>
        /// The elbow runs more than once because the pointer is not AT the elbow. Bending the elbow
        /// MOVES the device as well as turning it, so the direction to the target is not the same
        /// direction it was before the bend and a single pass always lands a little short. Two close
        /// it to well under a degree at arm's length; more is available and costs nothing but the
        /// iteration.
        /// </para>
        /// </summary>
        public static void Point(Transform upper, Transform lower, Transform pointer, Vector3 target,
                                 float shoulderShare, float maxDegrees, float weight, int elbowPasses)
        {
            if (upper == null || lower == null || pointer == null) return;

            upper.rotation = HeadAim.Share(Remaining(pointer, target, maxDegrees, weight), shoulderShare)
                             * upper.rotation;

            for (int pass = 0; pass < Mathf.Max(1, elbowPasses); pass++)
                lower.rotation = Remaining(pointer, target, maxDegrees, weight) * lower.rotation;
        }

        /// <summary>What is left of the swing onto <paramref name="target"/>, at this weight.</summary>
        private static Quaternion Remaining(Transform pointer, Vector3 target, float maxDegrees, float weight) =>
            HeadAim.Share(Swing(pointer.forward, target - pointer.position, maxDegrees),
                          Mathf.Clamp01(weight));
    }
}
