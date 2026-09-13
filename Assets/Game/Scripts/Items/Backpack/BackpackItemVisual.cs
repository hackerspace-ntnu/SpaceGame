using UnityEngine;

namespace SpaceGame.Items
{
    // Turns an item prefab into an inert display copy lying at true size on one of the pack's
    // surfaces.
    //
    // Being inert is DisplayCopy's half — see there for why nothing on a copy may run. What this
    // class adds is the pack's half: true size, a seat on the surface, and the one BoxCollider
    // the interaction ray needs, since a stripped copy has none of its own left.
    //
    // BackpackSeat is gone with the sockets. It offered a choice — stand on a shelf, or lie flat
    // with the thinnest axis into the panel — and free placement removes the choice rather than
    // resolving it: ItemFootprint.FootprintOf is DEFINED as (size.x, size.z), the shadow an item
    // casts with its own up still up. Turning a copy onto its side would make the space it visibly
    // occupies disagree with the rectangle the layout reserved for it, so every item now keeps its
    // own up and only turns about the surface normal, by the yaw the player chose.
    public static class BackpackItemVisual
    {
        /// <summary>Layer 10 in <c>ProjectSettings/TagManager.asset</c>.</summary>
        public const string ItemLayerName = "PackItem";

        private static int cachedItemLayer = -2;

        /// <summary>
        /// The layer every display copy is put on, or -1 if the project has no such layer.
        ///
        /// <para>
        /// It exists so focus mode's cursor ray can ask for placed items and nothing else — one
        /// layer mask instead of walking every hit's parent chain looking for a PackSurface. It is
        /// also what keeps the focus camera's depth-of-field volume off every other camera in the
        /// scene, which is a second reason not to leave these copies on the rig's own layer.
        /// </para>
        /// <para>
        /// Resolved once. <see cref="LayerMask.NameToLayer"/> is a string lookup and this is asked
        /// per item per rebuild.
        /// </para>
        /// </summary>
        public static int ItemLayer
        {
            get
            {
                if (cachedItemLayer == -2) cachedItemLayer = LayerMask.NameToLayer(ItemLayerName);
                return cachedItemLayer;
            }
        }

        /// <summary>
        /// Build a display-only copy of an item prefab at its true size, lying on
        /// <paramref name="surface"/> with its footprint centred on <paramref name="uv"/> and
        /// turned <paramref name="yaw"/> degrees, and given exactly one BoxCollider for the
        /// cursor ray. Returns null if either the prefab or the surface is missing.
        /// </summary>
        public static GameObject Build(GameObject itemPrefab, PackSurface surface, Vector2 uv, float yaw)
        {
            if (itemPrefab == null || surface == null) return null;

            // Read BEFORE the copy is stripped. The true size comes from ItemGrip.PackSize, and
            // ItemGrip is a MonoBehaviour — Strip destroys it — so this has to be measured off the
            // PREFAB, which still has its grip, rather than off the copy.
            Vector3 trueSize = ItemFootprint.SizeOf(itemPrefab);

            // The bounds the fit scale is measured against — the same ones EquipItemSocket and
            // ItemFootprint use, which is the whole point of reading them here.
            //
            // ItemGrip.sizeReference exists because some prefabs carry geometry that is not the
            // item: the Lasso's rope is a separate mesh in the same prefab as its coil, and
            // measuring the pair is what made "scale the handle to fit a hand" shrink the handle
            // to nothing. Scaling this copy off the WHOLE prefab instead would put the item on the
            // mat at a different size from the rectangle ItemFootprint reserved for it — and from
            // the size the same item is in the player's hand. Nothing on the shipped roster hits
            // that today (every sizeReference happens to cover all the active geometry), which is
            // exactly why it has to be spelled out: the next prefab that uses the field for what
            // it is for would break the pack silently.
            var grip = itemPrefab.GetComponentInChildren<ItemGrip>(true);
            Bounds reference = ItemBounds.Measure(itemPrefab, grip != null ? grip.SizeReference : null);

            GameObject copy = DisplayCopy.Make(itemPrefab, surface.transform);
            Transform t = copy.transform;

            // The whole copy, for seating and for the collider: everything visible has to clear
            // the surface and be hittable, sizeReference or not.
            Bounds local = ItemBounds.Measure(copy, null);

            float measured = Mathf.Max(reference.size.x, Mathf.Max(reference.size.y, reference.size.z));
            float target = Mathf.Max(trueSize.x, Mathf.Max(trueSize.y, trueSize.z));

            // The one number that makes the pack read as physical: how big this item ACTUALLY is,
            // in metres, next to everything else on the mat. It used to be a per-compartment
            // constant, so a Leash and a 1.35 m LaserStaff came out the same length.
            //
            // Uniform, and taken from the longest axis of the REFERENCE bounds, because that is
            // EquipItemSocket's `holdSize / longest` fit with the pack's own size substituted for
            // the hand's — so a copy is drawn at exactly the size the layout reserved for it. Most
            // items answer both questions with one number; the few that do not (see
            // ItemGrip.PackSize) are deliberately smaller here than they are in the hand.
            float worldScale = measured > 1e-6f && target > 1e-6f ? target / measured : 1f;

            Transform anchor = surface.transform;

            // True size is metres of finished, on-screen size, so the surface's own scale has to
            // come out of the local scale. It is not 1: the pack's FBX arrives on the centimetre
            // convention, mesh data 100x small under transforms 100x large, which cancels for the
            // pack itself and multiplies anything parented under it. Without this divide a rifle
            // lying on a wing is 100 m long.
            float surfaceScale = Mathf.Abs(anchor.lossyScale.x);
            if (surfaceScale < 1e-6f) surfaceScale = 1f;

            // How big the copy is actually DRAWN, which on the ship's gear wall is not the same as
            // how big the item is: the wall's whole frame is enlarged by PackSurface.DisplayScale,
            // and gear drawn at its logical size on an enlarged board would sit inside cells too
            // big for it. This is the ONLY place the enlargement reaches an item — `worldScale`
            // stays the logical size below, because the height handed to ToWorld is a uv-frame
            // length and ToWorld applies the display scale to it itself. Multiplying both would
            // float every item off the board by 6% of its own height.
            float drawnScale = worldScale * surface.DisplayScale;

            t.localScale = Vector3.one * (drawnScale / surfaceScale);

            // Only about the surface normal. The item keeps its own up — see the note above the
            // class on why turning it over would contradict its footprint.
            Quaternion orient = surface.WorldRotation(yaw);
            t.rotation = orient;

            // Seated by the FACE that meets the surface and centred on the uv, not by the pivot:
            // item prefabs in this project pivot anywhere from grip to muzzle, so a pivot-seated
            // copy either sinks through the mat or floats above it — and, worse under free
            // placement, sits somewhere other than the rectangle the layout reserved.
            Vector3 centre = surface.ToWorld(uv, local.size.y * worldScale * 0.5f);
            t.position = centre - orient * (local.center * drawnScale);

            // BoxCollider.size is in local space, so the transform scale above already resizes it
            // to true size. Passing the scaled numbers here would square the scaling.
            BoxCollider box = copy.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;
            box.isTrigger = false;

            // For the ray and NOTHING else. DisplayCopy strips the Rigidbody off a copy, so this
            // collider joins the nearest body ABOVE it: on a worn pack that is the player, and on
            // the ship's gear wall it is the ship. Left in the simulation it is exactly the fault
            // BackpackObject switches its own body collider off to avoid — a box bolted onto the
            // player's capsule that wedges them in doorways they used to fit through — once per
            // stowed item, and at the DRAWN size, so on the wall a shade larger still.
            //
            // excludeLayers rather than a trigger or a layer-matrix row: a trigger still reports
            // contacts and still belongs to that compound body, the project's matrix is fully open
            // and closing one row would miss the copies that fall back to the rig's own layer when
            // there is no PackItem layer, and switching the collider off would take the cursor ray
            // with it. Queries do not consult it — verified 2026-09-03 by raycasting a built copy.
            box.excludeLayers = Physics.AllLayers;

            // The dedicated layer where the project has one, the rig's own where it does not. A
            // copy left on the rig's layer still displays correctly — it just costs focus mode a
            // broad-phase raycast against the world instead of a one-layer one.
            SetLayer(t, ItemLayer >= 0 ? ItemLayer : anchor.gameObject.layer);

            return copy;
        }

        // The whole hierarchy, not just the root: a child left behind on another layer would still
        // render through a camera the pack is meant to be hidden from.
        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }
    }
}
