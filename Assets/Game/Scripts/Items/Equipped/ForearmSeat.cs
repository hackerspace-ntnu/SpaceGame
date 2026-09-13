using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Strap a gauntlet to a forearm: origin at the wrist, the model's own arm axis along the
    /// elbow-to-wrist line, its dorsal face turned to the back of the arm, at the scale its
    /// <see cref="GauntletFit"/> asks for.
    ///
    /// <para>
    /// The real worn gauntlet and the body screen's ghost of it both come through here, so a ghost
    /// sits exactly where the device will. Two copies of this arithmetic would drift the moment
    /// either side was tuned.
    /// </para>
    /// <para>
    /// The forearm rather than the hand: the hand's grip frame is the wrong seat for a gauntlet
    /// twice over, for the reasons <see cref="GauntletFit"/> gives — it aligns the item with the
    /// fingers instead of the arm, and it sizes per item rather than per family.
    /// </para>
    /// <para>
    /// The back of the forearm is derived, not authored: the hand's grip frame knows which way the
    /// thumb points, and the thumb side crossed with the arm axis is the dorsal direction on a
    /// right arm (the mirror on a left). The order matters and was got wrong once: with the
    /// operands the other way round the deck of every gauntlet sat on the palm side of both arms,
    /// which a folded rest pose hides. Verified 2026-09-02 by placing a camera on the computed
    /// dorsal side of the rig's hands and seeing knuckles, not curled fingers, and by the proximal
    /// phalanges flexing AWAY from it. Taken from the pose at the moment of seating, which is close
    /// enough — the thumb side barely moves with wrist flexion, and the item is parented to the
    /// forearm so it follows every pose from then on.
    /// </para>
    /// <para>
    /// The left arm gets a negative X scale rather than a mirrored model: the cuff's buckles are on
    /// the little-finger side of a right forearm, and a plain rotation onto the left arm would put
    /// them on the thumb side.
    /// </para>
    /// </summary>
    public static class ForearmSeat
    {
        /// <summary>Below this squared length the elbow and the wrist are the same point — see the
        /// degenerate-arm note in <see cref="Apply"/>. A tenth of a millimetre of arm.</summary>
        private const float MinArmSqrLength = 1e-8f;

        /// <summary>Below this the thumb side and the arm axis are parallel and the cross product
        /// that gives the dorsal direction has collapsed.</summary>
        private const float MinDorsalSqrLength = 1e-4f;

        /// <param name="instance">Already created; parented and posed here.</param>
        /// <param name="forearm">The LowerArm bone. The gauntlet follows every pose from here on.</param>
        /// <param name="hand">The hand bone, for the wrist end of the arm.</param>
        /// <param name="gripRotation">The hand's grip frame rotation — used only for its thumb side.</param>
        /// <param name="left">The left arm mirrors on X.</param>
        /// <param name="fit">How this gauntlet is strapped on. Required: it is how a prefab says
        /// which frame its model is in.</param>
        public static void Apply(GameObject instance, Transform forearm, Transform hand,
                                 Quaternion gripRotation, bool left, GauntletFit fit)
        {
            // Nothing to strap, or nowhere to strap it. The controller has already declined and
            // said so — Wear logs that the rig has nowhere to wear the item — and a ghost whose
            // site is not resolved yet has nothing to draw. Either way there is no pose to compute.
            if (instance == null || forearm == null || hand == null || fit == null) return;

            // A zero-length arm — the hand sitting exactly on its own elbow — is what an unposed or
            // half-built skeleton looks like, and it collapses everything below it:
            // Quaternion.LookRotation on a zero forward logs an error and leaves the pose at
            // identity. Fall back to the bone's own forward, which is finite and silent; a rig that
            // is not posed yet gets the wrong arm axis for as long as it stays that way, and the
            // right one the next time something is worn on it.
            Vector3 arm = hand.position - forearm.position;
            Vector3 toHand = arm.sqrMagnitude > MinArmSqrLength ? arm.normalized : forearm.forward;

            Vector3 thumbSide = gripRotation * Vector3.up;
            Vector3 dorsal = left ? Vector3.Cross(toHand, thumbSide) : Vector3.Cross(thumbSide, toHand);
            if (dorsal.sqrMagnitude < MinDorsalSqrLength) dorsal = Vector3.up;

            Transform t = instance.transform;
            t.SetParent(forearm, false);

            t.rotation = Quaternion.LookRotation(toHand, dorsal) * Quaternion.AngleAxis(fit.RollDegrees, Vector3.forward);
            t.position = hand.position - toHand * fit.WristGap;

            // Across the arm on X and Y, along it on Z (the model's arm axis). Not uniform: the
            // cuff is twice as long as wide, the forearm is not — see GauntletFit.
            float boneScale = Mathf.Max(0.0001f, forearm.lossyScale.x);
            float across = fit.CuffScale / boneScale;
            float along = fit.LengthScale / boneScale;
            t.localScale = new Vector3(left ? -across : across, across, along);
        }
    }
}
