using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The items whose size in the pack is deliberately NOT their size in the hand.
    ///
    /// <para>
    /// Both halves of the asymmetry are pinned here, because both can be lost silently. The prefab
    /// half goes if somebody re-authors one of these files or a builder script replaces it — no
    /// error, the item just quietly grows back. The code half goes if <see cref="ItemFootprint"/>
    /// is ever pointed back at <see cref="ItemGrip.HoldSize"/>, which is what it read for its whole
    /// life before this — again with no error, since every other item on the roster answers both
    /// questions with the same number and would not notice.
    /// </para>
    /// <para>
    /// Sizes here are cell-aligned on purpose: <c>PackGrid.Cell</c> is 0.09 m, so 0.54, 0.36 and
    /// 0.27 are exactly 6, 4 and 3 cells on the longest axis, and none of them wastes a fraction of
    /// a cell it cannot use.
    /// </para>
    /// </summary>
    public class PackSizeTests
    {
        private const string Gadgets = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/";

        /// <summary>Metres of slop when matching an authored value.</summary>
        private const float Slack = 1e-3f;

        /// <summary>An item that is one size in the hand and a smaller one on the mat, and why.</summary>
        private readonly struct Divergence
        {
            public readonly string Path;
            public readonly float Hold;
            public readonly float Pack;
            public readonly string Why;

            public Divergence(string path, float hold, float pack, string why)
            {
                Path = path;
                Hold = hold;
                Pack = pack;
                Why = why;
            }
        }

        private static readonly Divergence[] Asymmetric =
        {
            new(Gadgets + "GrapplingHook.prefab", 1.00f, 0.54f,
                "at its hand size it is 12 cells long against a widest face of 8, so it fit on no " +
                "surface but the rack"),

            new(Gadgets + "Lasso.prefab", 0.60f, 0.36f,
                "a coil of rope, and at hand size it lay on the mat as long as a sidearm"),

            new(Gadgets + "Leash.prefab", 0.55f, 0.27f,
                "the smallest thing the player owns; its hand size exists only because this rig's " +
                "hand is 1.7x a human's, and none of that reason survives it being put down"),
        };

        [SetUp]
        [TearDown]
        public void ClearMeasurementCache() => ItemFootprint.ClearCache();

        [Test]
        public void TheAsymmetricItemsCarryBothSizesOnDisk()
        {
            foreach (Divergence item in Asymmetric)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(item.Path);
                Assert.IsNotNull(prefab, item.Path + " is missing from disk");

                var grip = prefab.GetComponent<ItemGrip>();
                Assert.IsNotNull(grip, item.Path + " has no ItemGrip to size it");

                Assert.AreEqual(item.Hold, grip.HoldSize, Slack,
                    item.Path + " left the hand ladder; ItemScaleLadder owns holdSize");

                Assert.AreEqual(item.Pack, grip.PackSize, Slack,
                    item.Path + " lost its packSize — " + item.Why);
            }
        }

        /// <summary>
        /// And the footprint the layout actually reserves follows the pack size, not the hand size.
        /// Without this the field could be authored on every prefab and read by nothing.
        /// </summary>
        [Test]
        public void TheMeasuredSizeFollowsThePackSizeNotTheHandSize()
        {
            foreach (Divergence item in Asymmetric)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(item.Path);
                Assert.IsNotNull(prefab, item.Path + " is missing from disk");

                Vector3 size = ItemFootprint.SizeOf(prefab);
                float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

                Assert.AreEqual(item.Pack, longest, Slack,
                    item.Path + " measures at its hand size on the mat — ItemFootprint must read " +
                    "ItemGrip.PackSize");
            }
        }

        /// <summary>
        /// Every other gripped prefab keeps answering both questions with one number. This is the
        /// guard against the asymmetry spreading by accident: a packSize typed onto an item nobody
        /// decided about is a silent divergence between what the player holds and what they stow,
        /// and the whole reason it is opt-in is that the divergence has a cost.
        /// </summary>
        [Test]
        public void NoOtherShippedItemDivergesWithoutBeingListedHere()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs/Items" });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var grip = prefab != null ? prefab.GetComponent<ItemGrip>() : null;

                if (grip == null || Mathf.Abs(grip.PackSize - grip.HoldSize) <= Slack) continue;

                bool listed = System.Array.Exists(Asymmetric, row => row.Path == path);

                Assert.IsTrue(listed,
                    path + " has a packSize of " + grip.PackSize.ToString("F3") +
                    " against a holdSize of " + grip.HoldSize.ToString("F3") +
                    ", but is not listed in PackSizeTests. Add it with the reason it diverges, or " +
                    "clear packSize to 0 so it follows the hand.");
            }
        }
    }
}
