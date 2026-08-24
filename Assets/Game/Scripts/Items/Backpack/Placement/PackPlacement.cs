using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// One item lying somewhere on the pack: which surface, where on it, and which way round.
    ///
    /// <para>
    /// <see cref="ItemId"/> is a <c>string</c>, so this struct is managed and <b>cannot</b> go in a
    /// <c>NetworkList</c>. The wire form is a separate unmanaged struct; the two are deliberately
    /// not the same type.
    /// </para>
    /// </summary>
    public readonly struct PackPlacement
    {
        /// <summary>The item's <c>InventoryItem.ID</c> — the asset GUID.</summary>
        public readonly string ItemId;

        public readonly PackSurfaceId Surface;

        /// <summary>Centre of the item's footprint, in metres from the surface's (0,0) corner.</summary>
        public readonly Vector2 Uv;

        /// <summary>Degrees, turning surface +X toward surface +Z.</summary>
        public readonly float Yaw;

        public PackPlacement(string itemId, PackSurfaceId surface, Vector2 uv, float yaw)
        {
            ItemId = itemId;
            Surface = surface;
            Uv = uv;
            Yaw = yaw;
        }
    }
}
