using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// One item lying somewhere on the pack: which surface, where on it, which way round, and how
    /// full it is.
    ///
    /// <para>
    /// <see cref="ItemId"/> is a <c>string</c>, so this struct is managed and <b>cannot</b> go in a
    /// <c>NetworkList</c>. The wire form is a separate unmanaged struct; the two are deliberately
    /// not the same type.
    /// </para>
    /// </summary>
    public readonly struct PackPlacement
    {
        /// <summary>
        /// The <see cref="PackItemKey"/> naming this placement — an instance handle whose prefix
        /// is the item's <c>InventoryItem.ID</c>. Resolve it with <c>PackContainer.ItemFor</c>,
        /// never by treating it as an asset id.
        /// </summary>
        public readonly string ItemId;

        public readonly PackSurfaceId Surface;

        /// <summary>Centre of the item's footprint, in metres from the surface's (0,0) corner.</summary>
        public readonly Vector2 Uv;

        /// <summary>Degrees, turning surface +X toward surface +Z.</summary>
        public readonly float Yaw;

        /// <summary>
        /// How full this one is, 0..1, or <see cref="SupplyCharge.None"/> for the great majority of
        /// items, which hold nothing.
        ///
        /// <para>
        /// The charge rides the PLACEMENT rather than sitting in a table beside the layout because
        /// every path that already moves an item correctly — the wire, the save codec, a drag, a
        /// swap with the hotbar — moves a placement. A parallel table would have to be kept in step
        /// with all of them, and the first one anybody forgot would empty somebody's tank silently.
        /// </para>
        /// </summary>
        public readonly float Charge;

        public PackPlacement(string itemId, PackSurfaceId surface, Vector2 uv, float yaw)
            : this(itemId, surface, uv, yaw, SupplyCharge.None) { }

        public PackPlacement(string itemId, PackSurfaceId surface, Vector2 uv, float yaw, float charge)
        {
            ItemId = itemId;
            Surface = surface;
            Uv = uv;
            Yaw = yaw;
            Charge = charge;
        }

        /// <summary>The same placement holding a different amount. Everything else is untouched.</summary>
        public PackPlacement WithCharge(float charge) =>
            new(ItemId, Surface, Uv, Yaw, charge);
    }
}
