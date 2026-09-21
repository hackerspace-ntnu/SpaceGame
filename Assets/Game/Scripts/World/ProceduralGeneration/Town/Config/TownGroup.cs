// "N of these prefabs, somewhere in this band, turned this way."
//
// One generic group replaces RobotSettlementRecipe's hardcoded noun slots — shieldGeneratorPrefab,
// barracksPrefab, ecoHubPrefab, satelliteDishPrefab, turretPrefab — which is why that recipe can
// only ever describe a robot compound. Buildings, props, ground scatter and people are four LISTS
// of this one type, so a nomad camp and a mining post are data rather than new fields.
using System;
using UnityEngine;

namespace SpaceGame.World.Towns
{
    /// <summary>
    /// Which list a group came from. Carried on a slot so the generator can find its group again
    /// without the slot holding a reference to it.
    ///
    /// Sections are emitted in declaration order and that order is part of the seed: inserting a
    /// section would move every town ever generated. Append only.
    /// </summary>
    public enum TownSection
    {
        Centrepiece = 0,
        Building    = 1,
        Prop        = 2,
        Scatter     = 3,
        Person      = 4,
    }

    /// <summary>How a placed thing is turned.</summary>
    public enum TownYaw
    {
        /// <summary>Leave the prefab's own rotation alone. For anything with a designed facing.</summary>
        Keep = 0,

        /// <summary>Any angle. Right for rocks, corpses, scatter — anything with no front.</summary>
        Random = 1,

        /// <summary>A random multiple of 90°. Keeps axis-aligned architecture reading square.</summary>
        RandomQuarter = 2,

        /// <summary>Point at the middle of the town.</summary>
        FaceCentre = 3,

        /// <summary>
        /// Point at the middle, then snap to the nearest 90°. What the Clanker town does: a
        /// boxy prefab turned 37° to face the centre reads as a mistake, not as a choice.
        /// </summary>
        FaceCentreSnapped = 4,
    }

    [Serializable]
    public class TownGroup
    {
        [Tooltip("What this group is, for your own reading. Not used for anything.")]
        public string label = string.Empty;

        [Tooltip("Pick one of these at random per instance. All variants of one role belong here.")]
        public GameObject[] prefabs = Array.Empty<GameObject>();

        [Tooltip("How many to place — a RANGE, inclusive at both ends. Put the same number in both " +
                 "boxes for exactly that many. Leaving 'At most' at 0 means the group can legally " +
                 "roll none, which looks like the generator doing nothing.\n\n" +
                 "For a clustered group this counts CLUSTERS, not bodies.")]
        [RangeLabels("At least", "At most")]
        public Vector2Int count = new Vector2Int(1, 1);

        [Tooltip("Ring to place inside, in metres. Leave both at 0 to use the whole town out to " +
                 "its outer radius.")]
        [RangeLabels("Inner", "Outer")]
        public Vector2 band = Vector2.zero;

        public TownYaw yaw = TownYaw.Random;

        [Tooltip("Keep other clearance-reserving things out of this one's footprint, and respect " +
                 "theirs. On for buildings. OFF for people, props and scatter — a guard standing " +
                 "in the square is not an obstruction, and reserving space for one would push the " +
                 "buildings apart for no reason.")]
        public bool reservesClearance = false;

        [Tooltip("Lay a slab under it when the ground drops away. On for buildings; pointless for " +
                 "anything that can stand on a slope.")]
        public bool foundationPad = false;

        [Tooltip("Random uniform scale per instance. Leave both at 1 for anything whose size matters.")]
        [RangeLabels("Min", "Max")]
        public Vector2 scaleRange = Vector2.one;

        [Tooltip("Bodies per cluster, inclusive. Both at 1 places them one at a time, which is the " +
                 "ordinary case. Higher gives you a pile of scrap or a knot of guards.")]
        [RangeLabels("At least", "At most")]
        public Vector2Int clusterSize = new Vector2Int(1, 1);

        [Tooltip("How far cluster members scatter from their cluster's centre, in metres.")]
        public float clusterSpread = 3f;

        /// <summary>
        /// True when there is actually something to place. A group with a count but no prefabs is a
        /// configuration mistake, not an empty group — <c>TownGenerator.Verify</c> refuses it rather
        /// than silently placing nothing, which is how <c>SettlementConfig.asset</c> sat with every
        /// building slot empty for months without anybody noticing.
        /// </summary>
        public bool HasPrefabs
        {
            get
            {
                if (prefabs == null) return false;
                for (int i = 0; i < prefabs.Length; i++)
                    if (prefabs[i] != null) return true;

                return false;
            }
        }

        public int MaxCount => Mathf.Max(count.x, count.y);
        public int MinCount => Mathf.Max(0, Mathf.Min(count.x, count.y));
    }
}
