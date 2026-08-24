using UnityEditor;

namespace SpaceGame.Items.EditorTools
{
    /// <summary>
    /// Throws away <see cref="ItemFootprint"/>'s measurements whenever anything is imported,
    /// moved or deleted.
    ///
    /// <para>
    /// The cache is keyed by prefab <c>GameObject</c> and, left alone, never expires — it is sized
    /// for a play session, where a prefab's geometry cannot change. In the Editor it can, and
    /// silently: edit <c>holdSize</c> on the LaserStaff, or reimport its FBX, and every consumer
    /// goes on using the size measured the first time anything asked, for the rest of the session.
    /// The pack then reserves a rectangle of one size and draws an item of another, and nothing in
    /// the console says why.
    /// </para>
    /// <para>
    /// Deliberately unconditional rather than filtered to item prefabs. A footprint is derived from
    /// the item's meshes, reached through <c>ItemGrip.sizeReference</c> and whatever nested prefabs
    /// and model assets sit under it — so the set of paths whose reimport can change an answer is
    /// not one this class can enumerate from a filename. Measuring is cheap and this runs only in
    /// the Editor: clearing too often costs a few bounds walks, clearing too rarely costs a pack
    /// that reserves one size and draws another.
    /// </para>
    /// </summary>
    public sealed class ItemFootprintCacheInvalidator : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] movedTo, string[] movedFrom)
        {
            ItemFootprint.ClearCache();
        }
    }
}
