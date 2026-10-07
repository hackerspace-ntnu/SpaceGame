using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// How big an item is once it is lying in the world, and the one place that answers it.
    ///
    /// <para>
    /// An item is drawn in four frames and each one sizes its own copy: the hand from
    /// <see cref="ItemGrip.HoldSize"/> (<see cref="EquipItemSocket"/>), the backpack mat and the
    /// ship's gear wall from <see cref="ItemGrip.PackSize"/> (<see cref="ItemFootprint"/>), and the
    /// world — from here. Until this existed the world was the only frame with no answer at all: a
    /// dropped item came out at whatever scale the modeller happened to build the prefab at, which
    /// is the one number this codebase already says out loud must not be believed (see
    /// <see cref="ItemFootprint"/>'s note on the 11 m CixinGun). The same Portal Gun was 1.25 m in
    /// the hand, 1.03 m on the gear wall and 0.44 m in the sand.
    /// </para>
    /// <para>
    /// <b>The size is the gear wall's.</b> That is a decision, taken on 2026-09-03: the wall is
    /// where the player last saw the thing before they carried it out and put it down, so matching
    /// it is what removes the pop. It also keeps a dropped item findable on a cluttered deck, which
    /// its true modelled size — a 0.26 m scanner lying against a floor seam — does not.
    /// <c>GDC-L1-FEEL-0007</c>'s caution is the one that applies: keep the simulated space
    /// internally consistent even where it is not life size, and this is the number that makes it
    /// so.
    /// </para>
    /// <para>
    /// <b>Derived from the wall rather than typed</b>, so the two cannot drift apart — that
    /// agreement IS the decision, and a typed copy would quietly stop honouring it the first time
    /// the wall moved. If the wall is ever resized for the room again, this is the line to
    /// reconsider, and it is a design call rather than a maintenance one.
    /// </para>
    /// </summary>
    public static class ItemWorldScale
    {
        /// <summary>
        /// How much bigger than its authored metres an item is drawn lying in the world.
        ///
        /// <para>
        /// An item on the gear wall is drawn at <c>PackSize</c> times <see cref="PackScale.Factor"/>
        /// times that container's <c>PackSurface.DisplayScale</c>, which for the wall is
        /// <see cref="PackScale.WallDisplay"/> — so the product is exactly
        /// <see cref="PackScale.WallDrawn"/>, with the rig's own factor cancelling out. Stating it
        /// as the product rather than as 1.908 is what keeps the world and the wall equal through a
        /// change to either.
        /// </para>
        /// </summary>
        public const float Factor = PackScale.Factor * PackScale.WallDisplay;

        /// <summary>
        /// Longest-axis size in metres for this item lying in the world.
        ///
        /// <para>
        /// <see cref="ItemGrip.PackSize"/> and not <see cref="ItemGrip.HoldSize"/>: the pack sizes
        /// are the ones authored in true-world metres, and the hold sizes are brackets on a ladder
        /// tuned against a hand roughly 1.7x a human's. Sized off the hand ladder a coiled leash
        /// would lie in the sand as long as a rifle.
        /// </para>
        /// <para>
        /// A grip that sizes to zero means "keep the size the artist built" in the hand, and the
        /// mat honours that. The world does not, deliberately: on the mat the artist's scale is
        /// still a size the player reads against the other gear beside it, and out here it is the
        /// raw prefab number this whole class exists because nobody may trust. An item that never
        /// declared a size gets the same <see cref="ItemBounds.DefaultSize"/> the hand and the mat
        /// give it.
        /// </para>
        /// </summary>
        public static float SizeOf(GameObject item)
        {
            if (item == null) return 0f;

            ItemGrip grip = item.GetComponentInChildren<ItemGrip>(true);
            float authored = grip != null && grip.PackSize > 0f ? grip.PackSize : ItemBounds.DefaultSize;

            return authored * Factor;
        }

        /// <summary>
        /// The local scale that puts <paramref name="item"/> at <see cref="SizeOf"/>, measured
        /// against the scale it is carrying now.
        ///
        /// <para>
        /// Pure, so the caller decides when it lands and nothing has to reason about being run
        /// twice. Applying the result and asking again returns the same value — the measurement it
        /// divides by carries the scale it multiplies back in.
        /// </para>
        /// <para>
        /// Applied to the world INSTANCE and never baked into the prefab, which would look tidier
        /// and would break four items. <see cref="EquipItemSocket"/>'s zero-hold-size branch means
        /// literally "the prefab root's own scale", so a world size baked there follows the item
        /// into the hand — and the four pinned Fitted items (SuckerPuncher, RepulsorGauntlet,
        /// ItemScanner, WingPack) are exactly the ones that take that branch.
        /// </para>
        /// </summary>
        public static Vector3 LocalScaleFor(GameObject item)
        {
            if (item == null) return Vector3.one;

            Transform t = item.transform;
            Vector3 authored = t.localScale;

            ItemGrip grip = item.GetComponentInChildren<ItemGrip>(true);

            // Narrowed exactly as the hand and the mat narrow it, or the Lasso's 4.4 m of rope
            // sizes the handle it is coiled around down to nothing.
            Bounds local = ItemBounds.Measure(item, grip != null ? grip.SizeReference : null);

            Vector3 world = Vector3.Scale(local.size, new Vector3(
                Mathf.Abs(authored.x), Mathf.Abs(authored.y), Mathf.Abs(authored.z)));

            float longest = Mathf.Max(world.x, Mathf.Max(world.y, world.z));

            // Nothing measurable — a pure-effect item, or geometry that only exists at use time.
            // The artist's scale beats a resize off a bogus measurement, exactly as in the hand.
            if (longest < 1e-5f) return authored;

            return authored * (SizeOf(item) / longest);
        }
    }
}
