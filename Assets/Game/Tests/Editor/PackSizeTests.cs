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
        private const string Supplies = "Assets/Game/Prefabs/Items/Supplies/";

        /// <summary>
        /// Why the oxygen bottle diverges, why it is the one row NOT derived from the roster's
        /// usual rule, and why its two variants must agree.
        ///
        /// <para>
        /// Every other mat-tuned item is "true size rounded up to the next 0.09 m webbing pitch,
        /// plus a cell". That rule assumes an item STANDS on the face, so its height costs nothing.
        /// This one lies down — a bottle standing on a vertical back panel points straight out of
        /// the wearer's back — so its whole length is in the footprint and the rule's 0.72 would
        /// cost 4 x 8 = 32 cells, more than either back panel holds. 0.50 draws it 0.525 m, which
        /// is life size to within 3%, for <b>3 x 6 = 18 cells</b>: exactly a back panel, and inside
        /// the leaf, the rack and both wings with room.
        /// </para>
        /// <para>
        /// Carried at the hand's own 0.90 m it would be 0.945 m on the mat — 4 x 10 cells, which
        /// fits no face on the rig at all.
        /// </para>
        /// <para>
        /// The drained and the charged bottle are one model and must carry the SAME number:
        /// filling a bottle must not make it unstowable.
        /// </para>
        /// </summary>
        private const string BottleWhy =
            "a 0.5414 m pressure bottle carried at the BigTool bracket's 0.90 m, which is 1.66x " +
            "life size. It LIES DOWN on the mat (OxygenGearBuilder.BottleLiesDown), so its length " +
            "is part of its footprint rather than standing up out of it, and the roster's usual " +
            "'+ a cell' rule would cost 4 x 8 = 32 cells; 0.50 draws it 0.525 m, life size to " +
            "within 3%, for 3 x 6 = 18 — exactly a back panel. Both variants share it";

        /// <summary>
        /// Why the power cell's extra cell of margin is deliberately NOT taken.
        ///
        /// <para>
        /// At 0.72 the slab measures exactly the leaf's eight cells across — and eight cells is a
        /// float division landing precisely on an integer, which rounds either way and so decides
        /// at random whether the item fits the leaf at all. 0.63 is 7 x 3 = 21 cells with a column
        /// to spare. The formula is a rule of thumb; a cell boundary is not.
        /// </para>
        /// </summary>
        private const string CellWhy =
            "a 0.55 m battery carried at the BigTool bracket's 0.90 m; 0.63 is its true size " +
            "rounded up to the next webbing pitch and the usual extra cell deliberately left off, " +
            "because at 0.72 it measures EXACTLY the leaf's eight cells and a float rounding " +
            "either way then decides whether it fits at all. 7 x 3 = 21 cells";

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

        /// <summary>
        /// <b>The seven gauntlets used to be listed here and deliberately are not any more.</b>
        ///
        /// <para>
        /// Until 2026-09-04 a gauntlet was a device wrapped in a bracer, and in true metres the
        /// pair was a 0.6-0.8 m object that would have eaten 8 x 7 of the rig's cells — so it
        /// declared `holdSize` 0 ("keep the size the artist built", right on an arm) and a chosen
        /// `packSize` of 0.54, and that divergence was recorded here. The bracer is now worn
        /// permanently and is not part of the item: what goes on the mat is the device alone,
        /// 0.39 m for the flashlight up to 0.60 for the grappling hook. Those are gadget-sized,
        /// so `GauntletPrefab.PackSize` went to 0 and the family stopped diverging at all.
        /// </para>
        /// <para>
        /// It is written down because the absence is the interesting part: a gauntlet reappearing
        /// in this list means someone gave the family a pack size again, and the question to ask
        /// is whether a bracer has crept back into the models.
        /// </para>
        /// </summary>

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
            new(Gadgets + "Lasso.prefab", 0.60f, 0.36f,
                "a coil of rope, and at hand size it lay on the mat as long as a sidearm"),

            // ── The oxygen plant's three supplies ─────────────────────────────────────────
            new(Supplies + "OxygenTank.prefab", 0.90f, 0.50f, BottleWhy),
            new(Supplies + "Battery.prefab", 0.90f, 0.63f, CellWhy),

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
