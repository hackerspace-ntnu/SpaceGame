using System;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Keeps a worn item on its seat for as long as it is worn.
    ///
    /// <para>
    /// <see cref="WornSeat"/> and <see cref="ForearmSeat"/> both compute the pose in WORLD space —
    /// they have to, because both derive it from the rig (the elbow-to-wrist line, the pack's lash
    /// rail) rather than from numbers authored in the bone's frame — and then write it once. From
    /// then on the item was expected to ride the bone, and nothing checked that it did. That made
    /// the seat a single unguarded moment, and it failed in two directions at once:
    /// </para>
    /// <list type="number">
    /// <item><b>A foreign write was permanent.</b> One write to the instance's transform, from any
    /// system, in any later frame, moved the gear off the body for good and in silence.</item>
    /// <item><b>A stale seat was permanent too, and this is the one that was actually biting.</b>
    /// A torso item's position comes off the pack's lash rail, and the rail comes and goes:
    /// <c>BackpackController.GearMount</c> answers null while the pack is on the sand and a real
    /// transform again once it is shouldered. Read once, at wear, the pose became a snapshot of a
    /// relationship that then changed underneath it — gear put on with the pack off the back seated
    /// at the fit's fallback and stayed 0.65 m off the rail forever after the pack came home; gear
    /// worn with the pack on stayed where the rail had been once it was deployed. Nothing re-seated
    /// on either transition, and nothing said so.</item>
    /// </list>
    /// <para>
    /// So the pose is <b>re-derived</b> here every LateUpdate rather than remembered:
    /// <see cref="WornSeat.Pose"/> for torso gear, from the fit and whatever the mount is right
    /// now. There is no snapshot left to go stale, and no window in which a bad moment can be
    /// captured — an item seated during one is corrected on the next frame instead of living with
    /// it forever.
    /// </para>
    /// <para>
    /// <b>A gauntlet is pinned rather than re-derived, and that is deliberate.</b>
    /// <see cref="ForearmSeat"/> reads the wrist and the hand's thumb side to find the back of the
    /// arm, and it says in as many words that it takes them from the pose at the moment of seating
    /// because the thumb side barely moves with wrist flexion. Re-deriving would make the cuff
    /// chase every flex of the wrist. There is no mount in that path either, so there is nothing
    /// that can go stale: pinning the local pose is the whole job.
    /// </para>
    /// <para>
    /// <b>The root only.</b> An item's own parts move for their own reasons while it is worn — the
    /// jetpack's nozzles gimbal, the wingsuit's membranes fold — and this says nothing about them.
    /// What it holds is the one transform the wearer's body owns.
    /// </para>
    /// <para>
    /// Added by <c>BodyEquipmentController</c> to the instances it owns, and deliberately not by
    /// the two seats themselves: the gear screen's ghosts come through the same seats, and a ghost
    /// is a <see cref="DisplayCopy"/> — something that holds no state and runs no gameplay code.
    /// Giving one a component that ticks would make it an item again.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class WornAnchor : MonoBehaviour
    {
        /// <summary>
        /// How far a worn item may sit from its bone before something is wrong.
        ///
        /// <para>
        /// Generous on purpose. The furthest anything legitimately sits is the wing pack on the
        /// rail, ~0.63 m out in the spine's frame, and a rig may be scaled; this is not a tuning
        /// knob, it is the line past which "on the body" has stopped being true. What it catches is
        /// the failure the player reports as gear that vanished — an item written to a pose that
        /// has nothing to do with the wearer, which is invisible in the console and looks from the
        /// outside like the item was never equipped.
        /// </para>
        /// </summary>
        private const float ImplausibleDistance = 3f;

        /// <summary>The bone the item hangs off, and the frame everything here is judged against.</summary>
        private Transform bone;

        /// <summary>The item's authored fit; null for a gauntlet, which is pinned instead.</summary>
        private WornFit fit;

        /// <summary>
        /// Where the mount is NOW. A delegate rather than a transform because the answer changes:
        /// the pack's rail is null while the pack is deployed and a live transform again once it is
        /// back, and holding the transform is exactly the staleness this class exists to remove.
        /// Null for gear that has no mount, and for gauntlets.
        /// </summary>
        private Func<Transform> mount;

        /// <summary>Set for a gauntlet: hold the local pose recorded at seat time, do not re-derive.</summary>
        private bool pinned;

        private Vector3 localPosition;
        private Quaternion localRotation;
        private Vector3 localScale;

        /// <summary>Said once per instance, so a fault is reported rather than re-reported every
        /// frame for as long as the item exists.</summary>
        private bool warnedDetached;
        private bool warnedImplausible;

        /// <summary>
        /// Pin <paramref name="instance"/> at the pose it is sitting at now.
        ///
        /// <para>
        /// For gear seated from the wearer's own anatomy rather than from a fixture — the gauntlets.
        /// Call it AFTER the seat, with the item at its final pose: this reads the pose rather than
        /// computing one, so <see cref="ForearmSeat"/> stays the only place that arithmetic lives.
        /// </para>
        /// </summary>
        public static void Pin(GameObject instance, Transform bone)
        {
            WornAnchor anchor = Attach(instance, bone);
            if (anchor == null) return;

            anchor.pinned = true;
            anchor.fit = null;
            anchor.mount = null;
            anchor.Record();
        }

        /// <summary>
        /// Have <paramref name="instance"/> re-derive its pose from <paramref name="fit"/> and
        /// whatever <paramref name="mount"/> answers, every frame.
        ///
        /// <para>
        /// For torso gear, whose position comes off a fixture that comes and goes. The scale is
        /// still read from the pose the seat produced, because that is the half of seating that
        /// does not change while the item is worn — and re-measuring it per frame would walk every
        /// mesh in the prefab.
        /// </para>
        /// <para>
        /// Idempotent, and re-recording is the point of that: <c>SetTorsoForm</c> re-seats worn gear
        /// at the gear screen's own size, and that new scale is the one to keep from then on.
        /// </para>
        /// </summary>
        public static void Follow(GameObject instance, Transform bone, WornFit fit, Func<Transform> mount)
        {
            WornAnchor anchor = Attach(instance, bone);
            if (anchor == null) return;

            anchor.pinned = false;
            anchor.fit = fit;
            anchor.mount = mount;
            anchor.Record();
        }

        private static WornAnchor Attach(GameObject instance, Transform bone)
        {
            if (instance == null || bone == null) return null;

            var anchor = instance.GetComponent<WornAnchor>();
            if (anchor == null) anchor = instance.AddComponent<WornAnchor>();

            anchor.bone = bone;
            return anchor;
        }

        private void Record()
        {
            Transform t = transform;
            localPosition = t.localPosition;
            localRotation = t.localRotation;
            localScale = t.localScale;
        }

        /// <summary>
        /// LateUpdate, because the Animator writes the bones during the animation update and an
        /// earlier pass would be posing against a skeleton the frame then moves. This is the last
        /// word before the frame is drawn.
        /// </summary>
        private void LateUpdate() => Reassert();

        /// <summary>
        /// Put the item back on its seat now, rather than at the end of the frame.
        ///
        /// <para>
        /// The frame timing is about when the answer is needed, not part of the decision, so tests
        /// drive this directly instead of running a frame — the same door
        /// <c>UnderTerrainGuard.RunCheckNow</c> opens for the same reason.
        /// </para>
        /// </summary>
        public void Reassert()
        {
            if (bone == null) return;

            Transform t = transform;

            if (t.parent != bone)
            {
                // Not repaired here. Every worn item carries a NetworkObject, and an UNSPAWNED one
                // reverts a re-parent and logs for it (NetworkObject.OnTransformParentChanged), so
                // a frame-by-frame retry would be a frame-by-frame exception rather than a fix.
                // Whatever took the gear off the bone is the bug; this says so once and stops.
                Warn(ref warnedDetached,
                     $"is no longer parented to '{bone.name}'. Worn gear is seated on a bone and " +
                     "follows it; something re-parented this instance, and it will not follow the " +
                     "wearer any more.");
                return;
            }

            if (t.localScale != localScale) t.localScale = localScale;

            if (pinned)
            {
                // Compared before writing rather than written unconditionally. Assigning a transform
                // marks it and everything under it dirty, and this runs every frame for every worn
                // item on every character — so the ordinary frame, where nothing has touched the
                // pose, should cost two comparisons and no work at all.
                if (t.localPosition != localPosition) t.localPosition = localPosition;
                if (t.localRotation != localRotation) t.localRotation = localRotation;
            }
            else
            {
                WornSeat.Pose(t, fit, mount?.Invoke());
            }

            CheckPlausible(t);
        }

        /// <summary>
        /// Notice gear that has ended up somewhere it cannot be, and say so once.
        ///
        /// <para>
        /// The failures this class fixes were all invisible: nothing threw, nothing logged, and the
        /// player saw an item that had simply stopped being on their body. A seat is cheap to
        /// sanity-check and the check is the difference between "it happened again" and a line
        /// naming the item, the bone and the distance. It does not correct anything — a pose this
        /// wrong means an input to the seat was wrong, and quietly clamping it would hide that.
        /// </para>
        /// </summary>
        private void CheckPlausible(Transform t)
        {
            float distance = Vector3.Distance(t.position, bone.position);
            if (distance <= ImplausibleDistance) return;

            Warn(ref warnedImplausible,
                 $"is {distance:0.0} m from '{bone.name}', which is further than worn gear can " +
                 $"sit. Its pose was built from fit '{(fit == null ? "none" : fit.name)}' and " +
                 $"mount '{(mount?.Invoke() == null ? "none" : mount.Invoke().name)}'. " +
                 "The item is on the body's slot but not on the body.");
        }

        private void Warn(ref bool said, string what)
        {
            if (said) return;
            said = true;
            Debug.LogWarning($"[WornAnchor] '{name}' {what}", this);
        }
    }
}
