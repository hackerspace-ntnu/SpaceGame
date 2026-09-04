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
        public static void Apply(GameObject instance, Transform bone, WornFit fit, Transform mount = null)
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
            // rather than in the controller. An item with a WornVisual is a different shape worn
            // than carried — the wing pack is a folded aircraft in the hand and a 3.5 m pair of
            // wings on the back — and ItemBounds reads only what is switched on. Swapped after
            // the measure, the wings would be scaled to the folded bundle's size.
            WornVisual.SetWorn(instance, true);

            if (fit != null && fit.Size > 0f)
            {
                // Measured, not authored: the fit names the size the item is DRAWN at, so the scale
                // that gets there depends on how big the model happens to be. Divided by the bone's
                // own scale as well, because the item inherits it — a rig scaled to half size would
                // otherwise wear a half-size pack.
                Bounds bounds = ItemBounds.Measure(instance, null);
                float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                float boneScale = Mathf.Max(0.0001f, bone.lossyScale.x);
                if (longest > 0f) t.localScale = Vector3.one * (fit.Size / (longest * boneScale));
            }

            t.localRotation = fit != null ? fit.LocalRotation : Quaternion.identity;

            // Measured off the rig, not typed: the rail's transform sits at the middle of the lash
            // line, so gear centres on it and its ends reach out along the two protruding bars. A
            // hand-authored offset from the spine would mean the same thing only until somebody
            // moved the pack's worn pose or rescaled the rig, and the failure then is silent — the
            // screen's ghost would keep promising the rail while the gear drifted off it.
            //
            // Only the position. The rail's own rotation is the pack's leaf angle and says nothing
            // about which way up a wing pack goes, so the orientation stays the fit's.
            //
            // AnchorToBone opts out of the rail entirely, for gear shaped around the WEARER rather
            // than clipped to the pack — see WornFit.AnchorToBone. Asking the fit rather than
            // guessing from the item is what keeps this one seam honest: both places that seat
            // worn gear, the real thing and the gear screen's ghost of it, come through here.
            bool useMount = mount != null && (fit == null || !fit.AnchorToBone);
            if (useMount) t.position = mount.position;
            else t.localPosition = fit != null ? fit.LocalPosition : Vector3.zero;
        }
    }
}
