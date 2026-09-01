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
    /// Sizes here are cell-aligned on purpose: 0.54, 0.36 and 0.27 are exactly 6, 4 and 3 cells on
    /// the longest axis, and none of them wastes a fraction of a cell it cannot use. That survived
    /// the 2026-09-01 enlargement untouched, because an authored <c>packSize</c> reaches the mat
    /// through <see cref="PackScale.Factor"/> and the cell moved by the same factor — see
    /// <see cref="M"/>.
    /// </para>
    /// <para>
    /// The wing pack is the exception to both halves of that. It is the only row that diverges
    /// UPWARDS, and its number is aligned to a FACE rather than to the cell — it is solved from the
    /// rack's width so the folded craft fills the pack's back, and so it is the only row the
    /// enlargement did not leave alone. Its literal below is therefore a snapshot of a derivation,
    /// not a chosen size; the property it stands for is pinned independently by
    /// <c>WingPackStowTests</c>.
    /// </para>
    /// </summary>
    public class PackSizeTests
    {
        /// <summary>
        /// An authored size, in the metres it is actually drawn at ON THE MAT.
        ///
        /// <para>
        /// <see cref="ItemFootprint"/> multiplies every authored <c>packSize</c> by
        /// <see cref="PackScale.Factor"/>, because the pack was enlarged uniformly on 2026-09-01
        /// and gear that did not grow with it would read as toys on an oversized mat. The prefabs
        /// did NOT change — an item is the same size in the hand as it was — so the authored halves
        /// of the table below are bare literals and only the MEASURED half goes through here.
        /// </para>
        /// <para>
        /// Written as the cell's own ratio rather than as <c>PackScale.Factor</c> so that it is the
        /// same helper, spelled the same way, as the one in <c>PackLayoutTests</c> and the rest of
        /// the pack suites; <c>PackScaleTests</c> pins the two to the same number.
        /// </para>
        /// </summary>
        private static float M(float authoredMetres) =>
            authoredMetres * (PackGrid.Cell / PackScale.LegacyCell);

        private const string Gadgets = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/";
        private const string Portals = "Assets/Game/Prefabs/Items/Artifacts/Portals/";
        private const string ShipParts = "Assets/Game/Prefabs/Items/ShipParts/";
        private const string Equipment = "Assets/Game/Prefabs/Items/Equipment/";

        /// <summary>
        /// Why every hull module diverges, in one place rather than seven near-identical
        /// sentences. They are the only items in the game whose world size is not a size chosen
        /// for a player at all: a nuclear motor is 11 m because that is how long the motor on the
        /// roof is, and the whole point is that the thing in the sand IS the thing that bolts on.
        /// </summary>
        private const string ModuleWhy =
            "a hull module is authored at true ship scale, so neither its world size nor its " +
            "hand size is a number the pack could use; 0.80 m is the rack it goes on, measured " +
            "in the frame packSize is authored in";

        /// <summary>Metres of slop when matching an authored value.</summary>
        private const float Slack = 1e-3f;

        /// <summary>An item that is one size in the hand and a different one on the mat, and why.</summary>
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

            new(Portals + "PortalGun.prefab", 1.25f, 0.54f,
                "a 0.4445 m fire extinguisher carried at the ladder's 1.25 m Anchor bracket, " +
                "which is 2.8x life size — the hand is inflated for a 3 m astronaut and the mat " +
                "is in true-world metres, so following holdSize drew it 1.875 m tall on a 1.08 m " +
                "leaf and spent 36 of the rig's 255 cells on one bottle; 0.54 is its true size " +
                "rounded up to the next 0.09 m webbing pitch plus a cell, which is 2 x 4 = 8 " +
                "cells and fits every face but LongGoods strictly"),

            // The one item that diverges UPWARDS, and the one sized against a face rather than
            // against a bracket. Its value is derived, not chosen — see the Why — so if this row
            // fails after the rack was re-cut or `wing_pack_folded.fbx` was re-exported, the
            // literal here is what is stale: take the new number from
            // `WingPackBuilder.PackSizeForRack` and put it in. If it fails on its own, a builder
            // run or a hand edit dropped the field and the craft is back to reading as a toy.
            new(Equipment + "WingPack.prefab", 1.26f, 1.824f,
                "the folded ornithopter, and the only item whose stowed size is decided by the " +
                "SURFACE: WingPackBuilder.PackSizeForRack solves it from the rack's 9-cell width " +
                "at a 0.96 fill, the folded mesh's short:long proportions and PackScale.Factor, " +
                "so the craft fills the pack's back edge to edge and hangs off both ends. Its " +
                "1.26 m hand size is the wearer's span and predates the 1.5x enlargement, which " +
                "left it six of nine columns wide — a machine drawn as a trinket. This is also " +
                "the ceiling: a tenth column is refused by the rack and by the ship's gear wall " +
                "alike, so a bigger wing pack is a wing pack with nowhere to go"),

            // The seven salvageable hull modules. All 0.80 m on the mat, which is the rack face
            // they occupy; their hand sizes are ItemScaleLadder brackets.
            new(ShipParts + "AntiGravity.prefab",  1.40f, 0.80f, ModuleWhy),
            new(ShipParts + "NuclearMotor.prefab", 1.40f, 0.80f, ModuleWhy),
            new(ShipParts + "LongTurbine.prefab",  1.40f, 0.80f, ModuleWhy),
            new(ShipParts + "ReactorCore.prefab",  1.25f, 0.80f, ModuleWhy),
            new(ShipParts + "Gun.prefab",          1.25f, 0.80f, ModuleWhy),
            new(ShipParts + "SmallMotor.prefab",   1.00f, 0.80f, ModuleWhy),
            new(ShipParts + "AirIntake.prefab",    1.00f, 0.80f, ModuleWhy),
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
        ///
        /// <para>
        /// The measured answer is the authored one through <see cref="M"/>, which is the second
        /// half of the same claim: the enlargement multiplies what the mat draws and nothing that
        /// is on disk, so an item that stopped following <c>packSize</c> and an item that stopped
        /// being scaled up are both caught here, and by different amounts.
        /// </para>
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

                Assert.AreEqual(M(item.Pack), longest, Slack,
                    item.Path + " measures at its hand size on the mat — ItemFootprint must read " +
                    "ItemGrip.PackSize, and scale it by PackScale.Factor");
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
