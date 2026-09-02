using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How big an item really is, and what should hold it.
    ///
    /// <para>
    /// The size is <see cref="ItemGrip.PackSize"/> — a real metre value in the same units as the
    /// hand, and for most items literally <see cref="ItemGrip.HoldSize"/>, which is what that
    /// property falls back to. An item only carries a pack size of its own where being sized for
    /// the feel of a 1.7x hand made it absurd lying on a mat: the Leash and Lasso came out as long
    /// as sidearms, and the Grappling Hook at 1.00 m fit on no surface but the rack. See
    /// <see cref="ItemGrip.PackSize"/> for what that asymmetry buys and what it costs.
    /// </para>
    /// <para>
    /// <b>Then multiplied by <see cref="PackScale.Factor"/>, and this is the ONE place that
    /// happens.</b> The pack was enlarged uniformly on 2026-09-01 — the cell, every face, the rig
    /// model itself — and gear that did not grow with it would occupy fewer cells than it used to
    /// and read as scattered toys on an oversized mat. Doing it here rather than by re-typing
    /// every prefab's <c>packSize</c> is deliberate on three counts: half the item prefabs are
    /// rebuilt wholesale by builder scripts that would quietly put the old number back; an item
    /// with no authored <c>packSize</c> at all (the majority — they fall back to
    /// <c>holdSize</c>, and to <see cref="DefaultHoldSize"/> with no grip) would be missed
    /// entirely; and this function is already the single choke point every consumer goes through,
    /// so the display copy, the derived shape, the holder fit and the layout footprint cannot
    /// disagree about how big an item is. Nothing about the item IN THE HAND moves —
    /// <c>EquipItemSocket</c> reads <see cref="ItemGrip.HoldSize"/> and never comes through here.
    /// </para>
    /// <para>
    /// Measured once per prefab and cached. Measuring walks every renderer's bounds, and the drag
    /// controller asks for this on every frame of a drag.
    /// </para>
    /// <para>
    /// The cache never expires by itself, which is right for a play session — a prefab cannot
    /// change shape mid-game — and wrong in the Editor, where <c>packSize</c> is edited and FBXs
    /// are reimported. <c>ItemFootprintCacheInvalidator</c> in the neighbouring <c>Editor</c> folder
    /// calls <see cref="ClearCache"/> on every asset import so a stale measurement cannot outlive
    /// the asset it measured.
    /// </para>
    /// </summary>
    public static class ItemFootprint
    {
        /// Under this, an item is small enough to hang off a clip rather than be strapped down.
        /// A length on the mat, so it rides <see cref="PackScale.Factor"/> with everything else
        /// there — left unscaled it would demote a whole bracket of gear from clips to bungees the
        /// moment the pack grew, which is a change nobody asked for dressed as a scale-up.
        private static readonly float ClipSize = PackScale.Apply(0.12f);

        /// A bottle stands taller than it is wide. Below this it is just a lump on its end.
        private const float CordSlenderness = 1.5f;

        /// Fraction of its own length a long item's bulk must sit off centre to be a hand tool.
        private const float SleeveOffset = 0.15f;

        /// An <see cref="ItemGrip"/> whose sizes are both 0 is saying "keep the size the artist
        /// built" — a deliberate choice, honoured. It is NOT the same as having no grip at all.
        private const float Unset = 0f;

        /// <summary>
        /// The size assumed for a prefab with no <see cref="ItemGrip"/> on it at all.
        /// <para>
        /// Must stay equal to <c>EquipItemSocket.DefaultHoldSize</c>. The two are the same question
        /// asked in two places — how big is an item nobody sized? — and an item that answers 0.30 m
        /// in the hand and 11 m on the mat is a bug in whichever one disagrees. The pack's answer
        /// is then multiplied by <see cref="PackScale.Factor"/> like every other length on the mat,
        /// which is not a disagreement: it is the same item, drawn in a world that is 1.5x.
        /// </para>
        /// </summary>
        private const float DefaultHoldSize = 0.30f;

        /// Footprint given to an item with nothing to measure. A clip-sized square — small enough
        /// that it never crowds a surface, big enough to be picked up with a cursor. Scaled with
        /// the mat, or the one class of item that has no geometry at all would be the only gear
        /// that shrank when the pack grew.
        private static readonly float Unmeasurable = PackScale.Apply(0.1f);

        /// <summary>
        /// A measured prefab: how big it is, and where its bulk sits relative to its own pivot.
        ///
        /// <para>
        /// The centre is not decoration. It is the only thing a measurement carries that says
        /// "the mass is at one end" — the difference between a rod (<see cref="HolderKind.Webbing"/>)
        /// and a hand tool (<see cref="HolderKind.Sleeve"/>). A <c>Vector3</c> size alone cannot
        /// tell them apart, so caching the size alone made <c>Sleeve</c> unreachable.
        /// </para>
        /// </summary>
        private readonly struct Measurement
        {
            /// <summary>True size in metres.</summary>
            public readonly Vector3 Size;

            /// <summary>Bounds centre, in the same metres as <see cref="Size"/>.</summary>
            public readonly Vector3 Centre;

            public Measurement(Vector3 size, Vector3 centre)
            {
                Size = size;
                Centre = centre;
            }
        }

        private static readonly Dictionary<GameObject, Measurement> cache = new();

        /// <summary>True size in metres, from ItemGrip.PackSize where authored.</summary>
        public static Vector3 SizeOf(GameObject itemPrefab)
        {
            if (itemPrefab == null) return Vector3.zero;

            return MeasurementOf(itemPrefab).Size;
        }

        /// <summary>
        /// Where the item's bulk sits relative to its own pivot, in the same metres as
        /// <see cref="SizeOf"/>. Large along one axis means the mass is at one end of it.
        /// </summary>
        public static Vector3 CentreOffsetOf(GameObject itemPrefab)
        {
            if (itemPrefab == null) return Vector3.zero;

            return MeasurementOf(itemPrefab).Centre;
        }

        private static Measurement MeasurementOf(GameObject itemPrefab)
        {
            if (cache.TryGetValue(itemPrefab, out Measurement cached)) return cached;

            Measurement measured = Measure(itemPrefab);

            // Warned from here, not from Measure, so it fires exactly once per prefab per session
            // rather than on every cache hit — the drag controller asks for this every frame.
            WarnIfImplausible(itemPrefab, measured.Size);

            cache[itemPrefab] = measured;
            return measured;
        }

        /// <summary>The shadow the item casts on the surface it lies on: its two widest axes.</summary>
        public static Vector2 FootprintOf(Vector3 size) => new(size.x, size.z);

        public static Vector2 FootprintOf(GameObject itemPrefab) => FootprintOf(SizeOf(itemPrefab));

        /// <summary>
        /// The footprint of an inventory item, <b>never zero on either axis</b>.
        ///
        /// <para>
        /// Every path that rebuilds a layout — a save restore, a client adopting the server's list,
        /// a world pickup — has an <see cref="InventoryItem"/> and needs a footprint from it, and
        /// all three must agree or the same pack measures differently on two machines. Hence one
        /// function rather than three call sites doing <c>FootprintOf(item.itemPrefab)</c>.
        /// </para>
        /// <para>
        /// The floor is the load-bearing part. An item with no <c>itemPrefab</c> — a quest token, a
        /// currency, anything that only ever exists as an icon — measures zero, and
        /// <see cref="PackLayout.TryPlace"/> refuses a zero footprint outright. Without a floor
        /// those items could not be carried at all, and the refusal would be silent: no warning,
        /// no visual, just an item that never arrives in the pack.
        /// </para>
        /// </summary>
        public static Vector2 FootprintOf(InventoryItem item)
        {
            if (item == null) return Vector2.zero;

            Vector2 measured = FootprintOf(item.itemPrefab);

            return new Vector2(
                measured.x > 1e-4f ? measured.x : Unmeasurable,
                measured.y > 1e-4f ? measured.y : Unmeasurable);
        }

        public static HolderKind HolderFor(GameObject itemPrefab)
        {
            if (itemPrefab == null) return HolderKind.Bungee;

            Measurement measured = MeasurementOf(itemPrefab);

            return Classify(measured.Size, measured.Centre);
        }

        /// <summary>
        /// Which holder suits these proportions, knowing nothing about where the item's bulk sits.
        /// Never returns <see cref="HolderKind.Sleeve"/> — that needs the centre offset, so use the
        /// two-argument overload wherever one is available.
        /// </summary>
        public static HolderKind Classify(Vector3 size) => Classify(size, Vector3.zero);

        /// <summary>
        /// Which holder suits these proportions.
        ///
        /// Deliberately a guess rather than authored data. There are enough shipped items that
        /// requiring an author pass up front would stall the feature, and the guess is only ever
        /// cosmetic — a wrong holder looks slightly odd, it never breaks a placement.
        ///
        /// <para>
        /// <paramref name="centreOffset"/> is the measured bounds centre relative to the item's own
        /// pivot, in the same metres as <paramref name="size"/>. It is what separates a rod from a
        /// hand tool: both are long and thin, but a hand tool's bulk is bunched at one end.
        /// </para>
        /// </summary>
        public static HolderKind Classify(Vector3 size, Vector3 centreOffset)
        {
            // An INDEX, not the value. `longest == size.y` is a float identity test against a
            // number produced from the same components, so it never misses — but on a tie (a cube,
            // where x, y and z are all "the longest") it claims uprightness for an axis that was
            // not the one chosen, and a cube came out a bottle.
            int longestAxis = MaxAxis(size);
            float longest = size[longestAxis];

            if (longest <= 0f) return HolderKind.Bungee;

            if (longest < ClipSize) return HolderKind.Clip;

            // The two axes that are not the longest. If both are small relative to it, the item
            // is a rod and wants straps across it.
            float a, b;

            if (longestAxis == 1)      { a = size.x; b = size.z; }
            else if (longestAxis == 0) { a = size.y; b = size.z; }
            else                       { a = size.x; b = size.y; }

            float thickest = Mathf.Max(a, b);
            float slenderness = thickest > 1e-5f ? longest / thickest : float.MaxValue;

            // Upright and round-ish: a bottle or a tank. Tested before slenderness, because a tall
            // canister is also fairly slender and would otherwise be strapped down like a rifle.
            //
            // The LOWER bound is what stops "upright" from meaning "vaguely taller than it is
            // wide". Without it a cube and any lump that happens to be modelled standing up are
            // both bottles.
            bool upright = longestAxis == 1;
            bool round = Mathf.Abs(size.x - size.z) < 0.25f * Mathf.Max(size.x, size.z);

            if (upright && round && slenderness > CordSlenderness && slenderness < 4f)
                return HolderKind.Cord;

            if (slenderness > 3f)
            {
                // Long, with the bulk bunched at one end of that length: a hand tool, which hangs
                // head-down in an open sleeve rather than being strapped flat along its whole span.
                float alongLongest = Mathf.Abs(centreOffset[longestAxis]);

                return alongLongest > SleeveOffset * longest ? HolderKind.Sleeve : HolderKind.Webbing;
            }

            return HolderKind.Bungee;
        }

        /// <summary>
        /// Index of the largest component: 0 for x, 1 for y, 2 for z. Ties resolve to the earlier
        /// axis, so a cube is never "upright".
        /// </summary>
        public static int MaxAxis(Vector3 v)
        {
            if (v.x >= v.y && v.x >= v.z) return 0;

            return v.y >= v.z ? 1 : 2;
        }

        /// <summary>Forget every cached measurement. For tests and for asset reimports.</summary>
        public static void ClearCache() => cache.Clear();

        private static Measurement Measure(GameObject itemPrefab)
        {
            var grip = itemPrefab.GetComponentInChildren<ItemGrip>(true);

            Bounds local = ItemBounds.Measure(itemPrefab, grip != null ? grip.SizeReference : null);
            Vector3 measured = local.size;

            // No ItemGrip at all means nobody ever sized this prefab for a human hand, so its raw
            // mesh bounds are whatever the modeller happened to build at and must not be believed.
            // Measured across the shipped roster: CixinGunEquipped comes out 11.13 m (child meshes
            // at local scales up to 16.8), Gun 2.54 m, Sphere 1.00 m. Dragged in front of a camera
            // 1.9 m away, an 11 m item fills the entire screen — which is exactly the bug this
            // guard removes, and exactly the bug the old uniform 0.3 m scaling used to hide.
            //
            // DefaultHoldSize is not a new invention: EquipItemSocket has answered this same
            // question with the same 0.30 m since long before the pack existed
            // (`grip != null ? grip.HoldSize : DefaultHoldSize`). Diverging from it was the bug.
            // PackSize, not HoldSize: an item is allowed to be a different size on the mat than
            // in the hand, and falls back to the hand size when it has not asked to be.
            // PackScale.Factor is applied HERE, to the authored metres, rather than to the
            // measured proportions afterwards: `authored` names the item's longest axis, so
            // scaling it scales the whole measurement below by exactly the same factor, and the
            // "keep the artist's scale" branch is the only one that needs the multiply spelled out
            // a second time.
            float authored = PackScale.Apply(grip != null ? grip.PackSize : DefaultHoldSize);

            // An ItemGrip that IS present and sizes to 0 means something different and deliberate:
            // "keep the size the artist built". That is honoured, because somebody made that call
            // — but "the size the artist built" is still a size ON THE MAT, so it takes the pack's
            // scale like every other length there. Otherwise these few prefabs would be the only
            // gear that did not grow with the rig.
            if (authored <= Unset)
                return new Measurement(measured * PackScale.Factor, local.center * PackScale.Factor);

            // The authored size names the LONGEST axis, so scale the measured proportions to match
            // it rather than making a cube of it.
            float longest = Mathf.Max(measured.x, Mathf.Max(measured.y, measured.z));
            if (longest < 1e-5f) return new Measurement(new Vector3(authored, authored, authored), Vector3.zero);

            // The centre rides the same scale, or the "bulk at one end" test would compare a
            // resized item against an unresized offset.
            float fit = authored / longest;

            return new Measurement(measured * fit, local.center * fit);
        }

        /// Larger than the rig's widest surface plus a generous margin. Nothing legitimate reaches
        /// this; a measurement that does is an authoring mistake, not a big item. On the mat, so it
        /// scales with the mat: the lash line alone is 18 cells, and a fixed 2 m would have made
        /// the warning fire on the one surface built to take long goods at any factor above 1.11.
        private static readonly float Implausible = PackScale.Apply(2f);

        /// <summary>
        /// Complain once about a measurement that cannot be right.
        ///
        /// <para>
        /// This exists because the failure it catches is silent and spectacular in equal measure: a
        /// prefab with no <see cref="ItemGrip"/> measured 11.13 m, and the only symptom was the drag
        /// ghost swallowing the whole screen with no console output to explain it. A warning naming
        /// the prefab turns half an hour of confusion into a one-line fix.
        /// </para>
        /// </summary>
        private static void WarnIfImplausible(GameObject itemPrefab, Vector3 size)
        {
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (longest <= Implausible) return;

            Debug.LogWarning(
                $"ItemFootprint: '{itemPrefab.name}' measures {longest:F2} m on its longest axis, " +
                "which is larger than any surface on the pack. Give its prefab an ItemGrip with a " +
                "holdSize (or a packSize) in metres — without one the raw mesh bounds are used, " +
                "and an oversized item fills the screen while it is being carried.", itemPrefab);
        }
    }
}
