using System.Collections.Generic;
using System.Globalization;

namespace SpaceGame.Items
{
    /// <summary>
    /// The key a <see cref="PackContainer"/> identifies one placed item by — an <b>instance</b>
    /// handle, not an asset id.
    ///
    /// <para>
    /// <b>The bug this exists to fix.</b> <see cref="PackLayout"/> is keyed by this string and
    /// refuses a second placement carrying one it already holds, deliberately: an item is one
    /// object, and placing one that is already down is a caller bug rather than a second copy. So
    /// while the key WAS <c>InventoryItem.ID</c>, no container could hold two of anything. Nobody
    /// noticed, because until 2026-09-04 the only item a player would plausibly want two of was an
    /// oxygen bottle — and a full one and an empty one were two different assets, so two bottles
    /// happened to work for a reason that had nothing to do with wanting them to. Merging those
    /// two assets into one tank with a charge would have quietly taken the pack from two tanks to
    /// one.
    /// </para>
    /// <para>
    /// <b>The shape.</b> <c>&lt;assetId&gt;</c> for the first of a kind and
    /// <c>&lt;assetId&gt;#2</c>, <c>#3</c> … for the rest. The first is bare on purpose: it is
    /// exactly what every existing save file and every authored starting list already contains, so
    /// no world has to be migrated and no record has to be rewritten. The suffix is a within-container
    /// disambiguator and nothing more — it is not stable across a take and a put-back, and nothing
    /// may use it to mean anything.
    /// </para>
    /// <para>
    /// Every key still fits a <c>FixedString64Bytes</c>: an asset GUID is 32 characters and the
    /// wire form has 63 to spend.
    /// </para>
    /// </summary>
    public static class PackItemKey
    {
        /// <summary>
        /// Separates the asset id from the copy number. <c>#</c> because a GUID is hexadecimal and
        /// can never contain one, so <see cref="AssetOf"/> can never cut a key in the wrong place.
        /// </summary>
        public const char Separator = '#';

        /// <summary>
        /// The asset id a key names. A key with no suffix is already an asset id, which is what
        /// makes every pre-existing record read correctly.
        /// </summary>
        public static string AssetOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;

            int cut = key.IndexOf(Separator);
            return cut < 0 ? key : key[..cut];
        }

        /// <summary>Does this key name a placement of <paramref name="assetId"/>?</summary>
        public static bool NamesAsset(string key, string assetId) =>
            !string.IsNullOrEmpty(assetId) && AssetOf(key) == assetId;

        /// <summary>
        /// A key for one more copy of <paramref name="assetId"/> that
        /// <paramref name="taken"/> does not already hold.
        ///
        /// <para>
        /// Linear, and that is not a performance oversight: a container holds a handful of items,
        /// the loop runs once for all but the second copy of anything, and a counter kept beside
        /// the layout would be a second piece of state that has to survive the same saves, wires
        /// and rebuilds the layout does.
        /// </para>
        /// </summary>
        public static string Mint(string assetId, IReadOnlyCollection<PackPlacement> taken)
        {
            if (string.IsNullOrEmpty(assetId)) return assetId;
            if (taken == null || taken.Count == 0) return assetId;

            if (!Held(assetId, taken)) return assetId;

            // From 2: the bare id IS copy one.
            for (int copy = 2; copy < int.MaxValue; copy++)
            {
                string candidate = assetId + Separator + copy.ToString(CultureInfo.InvariantCulture);
                if (!Held(candidate, taken)) return candidate;
            }

            return assetId;
        }

        private static bool Held(string key, IReadOnlyCollection<PackPlacement> taken)
        {
            foreach (PackPlacement placement in taken)
                if (placement.ItemId == key) return true;

            return false;
        }
    }
}
