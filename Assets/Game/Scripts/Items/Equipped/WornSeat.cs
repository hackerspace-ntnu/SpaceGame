using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Seat something on the trunk — the spine for back gear, the chest for chest gear. There is no
    /// anatomy to derive a frame from on either, so the pose is the prefab's <see cref="WornFit"/>,
    /// or the bone itself without one.
    ///
    /// <para>
    /// The real worn item and the body screen's ghost of it both come through here, which is what
    /// makes a ghost sit exactly where the item will. Two copies of this arithmetic would drift
    /// the moment either side was tuned, and the drift would show as a ghost that promises one
    /// place and gear that lands in another.
    /// </para>
    /// </summary>
    public static class WornSeat
    {
        /// <summary>
        /// Which trunk bone a kind sits on. The one place that decision is made, shared by the
        /// controller (wearing the real thing) and <c>BodySite</c> (previewing a ghost of it), so
        /// the two can never disagree about where a chest item goes.
        ///
        /// <para>
        /// Falls back to the spine when a rig has no chest bone at all, which is the same answer
        /// the seat gave before chest gear existed: a rig that cannot tell its chest from its spine
        /// wears everything on the spine rather than dropping the item on the floor.
        /// </para>
        /// </summary>
        public static Transform BoneFor(EquipKind kind, Transform spine, Transform chest) =>
            kind == EquipKind.Chest && chest != null ? chest : spine;

        /// <param name="instance">Already created; parented and posed here. The controller
        /// instantiates it under the bone, so for it the parenting below is a no-op; the gear
        /// screen's ghost arrives unparented and needs it.</param>
        /// <param name="bone">The trunk bone from <see cref="BoneFor"/>. The item follows every
        /// pose from here on.</param>
        /// <param name="fit">The prefab's authored pose, or null to sit at the bone unchanged.</param>
        /// <param name="mount">The fixture the item clips to — the pack's lash rail for back gear —
        /// or null when there is none. Given one, the item's POSITION is taken from it and the fit's
        /// own offset is not used; the rotation and the size are the fit's either way.</param>
        /// <param name="form">Which of the item's worn models to show and size to. The world wears
        /// <see cref="WornVisual.Form.Worn"/>; the gear screen wears
        /// <see cref="WornVisual.Form.Inspected"/>, which is the wing pack's spread wings and, for
        /// every other item, the same model again. Re-seating with a different form is all that
        /// swapping between them takes — both models are authored on the same mount, so only the
        /// measured size below actually changes.</param>
        public static void Apply(GameObject instance, Transform bone, WornFit fit, Transform mount = null,
                                 WornVisual.Form form = WornVisual.Form.Worn)
        {
            Transform t = instance.transform;

            // Parented to the BONE even when a mount is given, and that is deliberate. The mount is
            // part of the pack, and the pack leaves: deploy it and anything parented to its rail
            // would be flung onto the sand with it. The bone is what the gear belongs to; the rail
            // only says where on the bone to sit.
            //
            // Skipped when it is already there, which is the ordinary case — the controller
            // instantiates gear under the bone, so this only really runs for the gear screen's
            // ghosts. Every worn item carries a NetworkObject for its life on the ground, and an
            // UNSPAWNED one throws `SpawnStateException` out of OnTransformParentChanged rather
            // than reparenting. That message never fires today only because the parent usually
            // does not change; asking the question first makes it a rule instead of a coincidence.
            if (t.parent != bone) t.SetParent(bone, false);

            // Before the measurement below, and that order is the whole reason this call is here
            // rather than in the controller. An item with a WornVisual is a different shape in
            // each form — the wing pack is a folded bundle in the hand, a pair of stowed wings on
            // the back, and a pair of spread ones on the gear screen — and ItemBounds reads only
            // what is switched on. Swapped after the measure, each model would be scaled to the
            // size of whichever one happened to be showing when the measure ran.
            WornVisual.SetForm(instance, form);

            float target = fit != null ? fit.SizeFor(form) : 0f;
            if (target > 0f)
            {
                // Measured, not authored: the fit names the size the item is DRAWN at, so the scale
                // that gets there depends on how big the model happens to be. Divided by the bone's
                // own scale as well, because the item inherits it — a rig scaled to half size would
                // otherwise wear a half-size pack.
                Bounds bounds = ItemBounds.Measure(instance, null);
                float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                float boneScale = Mathf.Max(0.0001f, bone.lossyScale.x);
                if (longest > 0f) t.localScale = Vector3.one * (target / (longest * boneScale));
            }

            Pose(t, fit, mount);
        }

        /// <summary>
        /// Put a already-seated item at the pose the fit and the mount currently call for.
        ///
        /// <para>
        /// Split out of <see cref="Apply"/> because it is the only part of seating that has to be
        /// able to run EVERY FRAME. The rest of <c>Apply</c> — the form swap, the bounds
        /// measurement, the scale — answers questions whose answers do not change while the item is
        /// worn, and <c>ItemBounds.Measure</c> walks every mesh in the prefab, so running it per
        /// frame would be paying for a fixed answer over and over.
        /// </para>
        /// <para>
        /// <b>The mount is a live transform, not a remembered one, and that is the point.</b> The
        /// pack's rail comes and goes — <c>BackpackController.GearMount</c> answers null while the
        /// pack is on the sand and a real transform again once it is shouldered — and this used to
        /// be read exactly once, at wear. So the pose was a snapshot of a relationship that then
        /// changed underneath it: gear put on with the pack off the back seated at the fit's
        /// fallback and stayed 0.65 m off the rail forever after the pack came home, and gear worn
        /// with the pack on stayed where the rail had been once it was deployed. Nothing re-seated
        /// on either transition, and nothing said so. <see cref="WornAnchor"/> now calls this every
        /// LateUpdate, so the answer is re-derived rather than remembered and both transitions look
        /// after themselves.
        /// </para>
        /// <para>
        /// Position off the rail is measured off the rig, not typed: the rail's transform sits at
        /// the middle of the lash line, so gear centres on it and its ends reach out along the two
        /// protruding bars. A hand-authored offset from the spine would mean the same thing only
        /// until somebody moved the pack's worn pose or rescaled the rig, and the failure then is
        /// silent.
        /// </para>
        /// <para>
        /// <b>Only the position comes off the rail.</b> The rail's own rotation is the pack's leaf
        /// angle — the rig is mounted turned about Y — and says nothing about which way up a wing
        /// pack goes, so the orientation stays the fit's, in the BONE's frame. That is not a
        /// mismatch now that the position is live: the rail is a rigid child of the pack and the
        /// pack is parented to the same trunk bone, so the two frames turn together and the item is
        /// rigid to the rig. It was only ever a mismatch while one half was stale.
        /// </para>
        /// <para>
        /// <see cref="WornFit.AnchorToBone"/> opts out of the rail entirely, for gear shaped around
        /// the WEARER rather than clipped to the pack. Asking the fit rather than guessing from the
        /// item is what keeps this one seam honest: both places that seat worn gear, the real thing
        /// and the gear screen's ghost of it, come through here.
        /// </para>
        /// </summary>
        public static void Pose(Transform t, WornFit fit, Transform mount)
        {
            if (t == null) return;

            t.localRotation = fit != null ? fit.LocalRotation : Quaternion.identity;

            bool useMount = mount != null && (fit == null || !fit.AnchorToBone);
            if (useMount) t.position = mount.position;
            else t.localPosition = fit != null ? fit.LocalPosition : Vector3.zero;
        }
    }
}
