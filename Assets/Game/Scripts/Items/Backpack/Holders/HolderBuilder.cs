using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Puts a holder over a placed item: the strap, cord, sleeve or clip that makes it read as
    /// stowed rather than balanced on the mat.
    ///
    /// <para>
    /// A holder is <b>scenery</b>. It is never picked, never collides and never ticks, so it goes
    /// through the same <see cref="BackpackItemVisual.Strip"/> the item display copies do and
    /// leaves without a collider. Nothing about it is state: it is rebuilt from the placement every
    /// time the layout changes and torn down with the item it covers.
    /// </para>
    ///
    /// <para><b>The authoring convention, and why the code depends on it.</b></para>
    /// <para>
    /// Every holder prefab is modelled <b>1.00 m along +X</b> — its stretch axis — <b>1.00 m across
    /// +Z</b>, and <b>1.00 m tall in +Y</b>, with its origin at the gripping centre. Fitting one to
    /// an item is then a single non-uniform scale straight to the item's measured size, with no
    /// per-prefab tuning anywhere: one asset stretches over a 0.26 m Leash and a 1.35 m LaserStaff.
    /// </para>
    /// <para>
    /// That stretch is right for the soft parts and catastrophic for the rigid ones. A strap
    /// spanning the LaserStaff is 1.35 m of webbing carrying buckles the size of dinner plates —
    /// and it fails looking like a modelling mistake rather than a code one, which is why it is
    /// worth this much comment. So every rigid part sits under an empty whose name starts
    /// <c>HARD_</c>, and <see cref="CounterScaleHardware"/> multiplies each of those by the inverse
    /// of the fit, cancelling the stretch for that subtree only. Hardware keeps its authored size
    /// and rides along to wherever the stretch carried it.
    /// </para>
    /// <para>
    /// <b>The constraint this places on the art:</b> a <c>HARD_</c> empty must not be rotated
    /// relative to the holder root. A componentwise inverse is only the inverse of a non-uniform
    /// scale when the child's axes line up with the parent's; under a rotation the two do not
    /// commute and the part comes out sheared rather than restored. Rotate the <i>mesh</i> inside
    /// the <c>HARD_</c> empty if a buckle needs to sit at an angle, never the empty itself.
    /// </para>
    /// </summary>
    public static class HolderBuilder
    {
        /// <summary>Prefix marking a subtree that must keep its authored size through the stretch.</summary>
        public const string HardwarePrefix = "HARD_";

        /// <summary>
        /// Below this many metres on any axis an item is not really there, and dividing by it to
        /// counter-scale hardware would send the buckles to infinity.
        /// </summary>
        private const float MinimumFit = 1e-3f;

        /// <summary>
        /// Build the holder for <paramref name="itemPrefab"/> and fit it over the item lying at
        /// <paramref name="uv"/> on <paramref name="surface"/>, turned <paramref name="yaw"/>
        /// degrees.
        ///
        /// <para>
        /// Returns null — never throws, never logs per frame — when there is no library, no item,
        /// no surface, or no art mapped for the shape this item turned out to be. A missing holder
        /// is cosmetic: the item still lies there, just bare.
        /// </para>
        /// </summary>
        public static GameObject Build(
            HolderLibrary library, GameObject itemPrefab, PackSurface surface, Vector2 uv, float yaw)
        {
            if (library == null || itemPrefab == null || surface == null) return null;

            HolderKind kind = ItemFootprint.HolderFor(itemPrefab);

            GameObject holderPrefab = library.PrefabFor(kind);
            if (holderPrefab == null) return null;

            // The item's true size, read off the PREFAB for the same reason BackpackItemVisual
            // reads it there: ItemGrip is a MonoBehaviour and does not survive the strip.
            Vector3 fit = Fit(ItemFootprint.SizeOf(itemPrefab));

            // Instantiate under a DEACTIVATED parent so no Awake runs. Stripping after the fact is
            // already too late — the component has registered itself, spawned its effect, taken
            // its network identity. Same staging trick, same reason, as the item display copy.
            var stage = new GameObject("HolderStage");
            stage.SetActive(false);

            GameObject holder = Object.Instantiate(holderPrefab, stage.transform);
            BackpackItemVisual.Strip(holder);

            Transform t = holder.transform;

            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            Transform anchor = surface.transform;

            // The lossyScale divide, again. The pack's FBX arrives on the centimetre convention —
            // mesh data 100x small under transforms 100x large — which cancels for the pack itself
            // and multiplies anything parented under one of its sockets. Without it a strap fitted
            // to a 1.35 m staff is 135 m of webbing. (ModelImporter.useFileScale = false was tried
            // and inflated the whole pack to 34 x 53 x 23 m; do not go back there.)
            float surfaceScale = Mathf.Abs(anchor.lossyScale.x);
            if (surfaceScale < 1e-6f) surfaceScale = 1f;

            t.SetParent(anchor, false);

            // The one line the convention buys: authored at 1 m cubed, so the fit IS the scale —
            // times the face's display scale, because a holder has to stay over the item it holds
            // and the item was drawn at that same enlargement (see BackpackItemVisual). The
            // position below needs nothing: ToWorld already speaks the drawn frame.
            t.localScale = fit * surface.DisplayScale / surfaceScale;

            // Only about the surface normal, matching the item it covers — the item keeps its own
            // up and turns by the yaw the player chose, and a holder that did anything else would
            // no longer be over the thing it is holding.
            Quaternion orient = surface.WorldRotation(yaw);
            t.rotation = orient;

            // Origin at the gripping centre, seated ON the surface rather than at the item's
            // mid-height: the item's own height is already in the Y fit, so a cord ring authored at
            // 25% height lands a quarter of the way up the item it is cinching.
            t.position = surface.ToWorld(uv, 0f);

            CounterScaleHardware(t, fit);

            // No collider is added, deliberately. The cursor picks items, and a holder that could
            // be hit would shadow the very item it sits on. The layer still matters: the focus
            // camera's depth-of-field volume is bound to it, so a holder left on the rig's layer
            // would be the one sharp thing in a soft frame.
            SetLayer(t, BackpackItemVisual.ItemLayer >= 0
                ? BackpackItemVisual.ItemLayer
                : anchor.gameObject.layer);

            holder.name = $"Holder_{kind}";

            Object.DestroyImmediate(stage);
            return holder;
        }

        /// <summary>
        /// The scale the holder is stretched by: the item's true size, floored so a zero-measured
        /// item cannot produce a degenerate transform or a division by nothing.
        /// </summary>
        public static Vector3 Fit(Vector3 itemSize) => new(
            Mathf.Max(itemSize.x, MinimumFit),
            Mathf.Max(itemSize.y, MinimumFit),
            Mathf.Max(itemSize.z, MinimumFit));

        /// <summary>
        /// Undo the stretch for every <c>HARD_</c> subtree under <paramref name="holderRoot"/>.
        ///
        /// <para>
        /// The root carries the whole fit, so the factor from a point in the prefab's own space to
        /// world metres is exactly <paramref name="fit"/> on each axis — the surface's lossyScale
        /// divide having already cancelled the FBX convention. Multiplying a <c>HARD_</c> child's
        /// local scale by the componentwise reciprocal makes that factor 1 for its subtree, which
        /// is the definition of "authored size". Its <i>position</i> is untouched, so the buckle
        /// still travels to the end of the strap; only its size stops travelling with it.
        /// </para>
        /// <para>
        /// Nested <c>HARD_</c> empties are not double-corrected: the walk stops descending once it
        /// has corrected a subtree, because a second reciprocal inside an already-corrected one
        /// would shrink the part by the fit rather than restore it.
        /// </para>
        /// </summary>
        public static void CounterScaleHardware(Transform holderRoot, Vector3 fit)
        {
            if (holderRoot == null) return;

            var inverse = new Vector3(
                1f / Mathf.Max(Mathf.Abs(fit.x), MinimumFit),
                1f / Mathf.Max(Mathf.Abs(fit.y), MinimumFit),
                1f / Mathf.Max(Mathf.Abs(fit.z), MinimumFit));

            CounterScaleChildren(holderRoot, inverse);
        }

        private static void CounterScaleChildren(Transform parent, Vector3 inverse)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);

                if (child.name.StartsWith(HardwarePrefix, System.StringComparison.Ordinal))
                {
                    child.localScale = Vector3.Scale(child.localScale, inverse);
                    continue;   // corrected; anything below it is already at authored size
                }

                CounterScaleChildren(child, inverse);
            }
        }

        // The whole hierarchy, not just the root — a child left behind on another layer would still
        // render through a camera the pack is meant to be hidden from.
        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }
    }
}
