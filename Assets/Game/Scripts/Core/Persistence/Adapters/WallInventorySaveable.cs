using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists what is on an inventory wall, and where.
    ///
    /// <para>
    /// A child saver, not an entity of its own: the wall is bolted into a ship, so its record
    /// belongs to the ship's. Save collection stops at any nested <c>SaveableEntity</c>, and the
    /// wall deliberately has none — giving it one would produce a second, competing record and a
    /// wall that could be restored somewhere the ship no longer is.
    /// </para>
    /// <para>
    /// The format is <see cref="PackSaveCodec"/>'s, shared verbatim with the backpack, under this
    /// saver's own key. The two containers place items by the same arithmetic, so they have to read
    /// records by the same arithmetic too.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(WallInventory))]
    public class WallInventorySaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "wallInventory";         // written into save files — never rename

        private WallInventory wall;

        // Lazy-resolved, NOT cached in Awake: EditMode tests never run Awake, so a saver that
        // caches there cannot be round-trip tested by PersistenceProbe.
        private WallInventory Wall => wall != null ? wall : wall = GetComponent<WallInventory>();

        public string SaveKey => Key;

        /// <summary>
        /// The version and the placements. <see cref="PackSaveCodec.State"/> also carries where a
        /// backpack was set down, which for something bolted to a bulkhead would be three fields of
        /// zeroes in every ship's record forever.
        ///
        /// <para>
        /// <b><see cref="version"/> is not optional, and dropping it is silent.</b> This struct
        /// exists to write a NARROWER record than the codec's, and the first version of it narrowed
        /// away the version field along with the pack pose. <see cref="PackSaveCodec.Restore"/>
        /// reads a missing version as "older than versioning", which means the pre-enlargement
        /// frame, and multiplies every uv by <see cref="PackScale.Factor"/> to bring it forward —
        /// so every wall record was rescaled by 1.5 on EVERY load, compounding each time, until the
        /// gear walked off the face and first-fit scattered it. Nothing threw. Any field the codec
        /// uses to interpret the numbers has to be carried here; only fields that are purely a
        /// backpack's may be left out.
        /// </para>
        /// </summary>
        public struct State
        {
            /// <summary>Which frame the uvs below are in. See <see cref="PackSaveCodec.Version"/>.</summary>
            public int version;

            public List<PackSaveCodec.PackPlacementRecord> placements;
        }

        // null stores nothing — the right answer for an empty wall, and it keeps a ship that has
        // never been used out of the file entirely.
        public object CaptureState()
        {
            if (Wall == null) return null;

            PackSaveCodec.State captured = PackSaveCodec.Capture(Wall.Layout);

            return captured.placements == null || captured.placements.Count == 0
                ? null
                // Taken from the codec rather than restated, so the two cannot drift apart the next
                // time the format moves.
                : new State { version = captured.version, placements = captured.placements };
        }

        public void RestoreState(JObject state)
        {
            if (Wall == null || state == null) return;

            // Server only. The contents are server-authoritative — WallInventoryNetwork's list is
            // the truth on every other machine — so a client that restored from a file it should
            // not have would be overwritten by the next wire update anyway, and in between would
            // show gear nobody else can see.
            //
            // Network.Simulates rather than Network.Server: an editor-launched session has no
            // NetworkManager at all, and Network.Server is false there. Simulates answers true
            // offline, which is where most loading actually happens.
            if (!Network.Simulates(this)) return;

            // Reads the "placements" array this saver wrote. The v1 backpack migration inside the
            // codec keys off "strapItemIds"/"mainItemIds", which no wall record has ever had, so it
            // is never reached from here.
            PackSaveCodec.Restore(Wall.Layout, Wall.Surfaces, Wall.Shapes, state, this);
        }
    }
}
