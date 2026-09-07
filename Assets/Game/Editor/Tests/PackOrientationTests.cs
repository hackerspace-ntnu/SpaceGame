using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// How a gauntlet lies down on the pack, pinned on disk.
    ///
    /// <para>
    /// The pack draws every item with its OWN up still up — <c>ItemFootprint.FootprintOf</c> is
    /// defined as <c>(size.x, size.z)</c> — so how an item is set down is authored data. For most
    /// of the roster that means a correction in <c>ItemPackOrientation</c>; for the gauntlets it
    /// means the opposite, and that is what these tests hold.
    /// </para>
    /// <para>
    /// A gauntlet's model IS its frame. Since the family was rebuilt on
    /// <c>components/props/gauntlet_base.blend</c> (2026-09-02) every one of them arrives with the
    /// arm's own axis on Z, across the arm on X and the back of the arm on Y, and
    /// <c>BodyEquipmentController.WearOnForearm</c> reads those axes directly off the transform to
    /// strap it on. So the two things that would once have been fixed here are now the two things
    /// that must never be touched: the model child stays unrotated and
    /// <c>ItemGrip.rotationOffset</c> stays at identity. Turn either and the gauntlet lies
    /// beautifully on the mat and sits sideways on the arm, which no other test would see.
    /// </para>
    /// <para>
    /// The consequence on the mat is that a gauntlet does NOT lie flat: its tallest axis is the
    /// one standing off the back of the arm, because the device is up there. That is deliberate —
    /// a bracer rolled onto its flank hides the device behind its own shell, and the device is how
    /// a player tells one gauntlet from another at a glance (<c>GDC-L1-UX-0003</c>). The footprint
    /// it reserves is its honest silhouette seen from above: across the arm by along the arm.
    /// </para>
    /// </summary>
    public class PackOrientationTests
    {
        private const string ScannerPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/RuinScanner.prefab";
        private const string ScannerAssetPath = "Assets/Game/Resources/Items/Artifacts/RuinScanner.asset";
        private const string RigPath = "Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab";

        private const string Rebuild =
            "Run Tools ▸ SpaceGame ▸ Items ▸ Reseat Gauntlets On The Base and read its verify lines.";

        /// <summary>Degrees of slop, matching <c>ItemPackOrientation</c>'s own.</summary>
        private const float Slack = 1f;

        /// <summary>
        /// The scanner stands on the mat the way it sits on the arm: deck up, arm axis flat.
        ///
        /// <para>
        /// Measured, not judged. The dorsal check is that NO part of the scanner reaches below the
        /// arm axis: a device bolted to the deck stands entirely on the back of the arm, so a model
        /// turned to lie prettier on the mat drags geometry down through y = 0 and is caught here.
        /// </para>
        /// <para>
        /// It used to assert instead that the dorsal axis was the LARGEST of the three, which was
        /// never quite the same claim — it held because the bracer's ventral shell hung 0.19 m
        /// below the arm and made Y the long axis by itself. Since 2026-09-04 the bracer is worn
        /// rather than carried and the model is the device alone, whose longest axis is along the
        /// arm. The old assertion would now fail while reporting that the model had been turned,
        /// which would have been a false diagnosis.
        /// </para>
        /// <para>
        /// The item scanner is the family's exception to the dorsal rule and is deliberately not
        /// tested here: the lead hand-rotated its console onto the arm's flank, so it does dip
        /// below the axis.
        /// </para>
        /// <para>
        /// <b>2 x 3 = 6 cells is a decision, and the only one on this page that is.</b> The count
        /// was a consequence of the model for as long as the scanner was drawn at the size the
        /// artist built it — 4 x 5 = 20 cells of the rig's 255, and that is what backlog INV-03
        /// called too big on 2026-09-06. It is now the consequence of a chosen
        /// <c>ItemGrip.packSize</c> of 0.225 instead; <c>PackSizeTests.ScannerWhy</c> owns the
        /// number and the reasoning, and this test's job is only to notice that the orientation
        /// still agrees with it. A 3-wide result here means the width crossed its cell boundary —
        /// 0.2326 m, which 0.225 is deliberately under.
        /// </para>
        /// </summary>
        [Test]
        public void RuinScanner_StandsDeckUpAndCostsTwoByThree()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(ScannerAssetPath);
            Assert.IsNotNull(asset, $"no InventoryItem at {ScannerAssetPath}");

            ItemFootprint.ClearCache();
            Vector3 size = ItemFootprint.SizeOf(asset.itemPrefab);
            PackShape shape = PackShapes.For(asset, null);

            GameObject scanner = AssetDatabase.LoadAssetAtPath<GameObject>(ScannerPath);
            Assert.IsNotNull(scanner, $"no prefab at {ScannerPath}");
            Bounds local = ItemBounds.Measure(scanner, null);

            Assert.Greater(local.min.y, 0f,
                $"the ruin scanner reaches to y {local.min.y:F3} in its own frame, below the arm " +
                $"axis. A gauntlet is the device alone and stands entirely on the deck on the BACK " +
                $"of the arm, so this means the model was turned and it will be worn rolled onto " +
                $"its flank — measured {size.ToString("F3")}. {Rebuild}");

            string cells = $"the ruin scanner is {shape.Width}x{shape.Height} cells, not the 2x3 " +
                           "silhouette a deck-up gauntlet casts at its chosen 0.225 m packSize. " +
                           "Either the model's frame moved, or ItemGrip.packSize moved underneath " +
                           "it — see PackSizeTests.ScannerWhy, and GauntletReseat, which rewrites " +
                           "that field.";

            Assert.AreEqual(2, shape.Width, cells);
            Assert.AreEqual(3, shape.Height, cells);

            GameObject rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            Assert.IsNotNull(rig, $"no rig at {RigPath}");

            // Strictly, on a real face, with no help from PackOverhang: a size is only worth its
            // cells if the item still has an honest home afterwards.
            var layout = new PackLayout();
            PackSurface[] surfaces = rig.GetComponentsInChildren<PackSurface>(true);

            PackSurface home = surfaces.FirstOrDefault(
                s => shape.Width <= s.Cells.x && shape.Height <= s.Cells.y &&
                     layout.TryFindSpot(s.Id, s.Size, shape, out _, out _));

            Assert.IsNotNull(home,
                $"the ruin scanner is {shape.Width}x{shape.Height} cells and no face takes it " +
                "without overhang: " +
                string.Join(", ", surfaces.Select(s => $"{s.Id} {s.Cells.x}x{s.Cells.y}")));
        }

        /// <summary>
        /// Nothing has turned the scanner's contents, and nothing has turned its hand offset.
        ///
        /// <para>
        /// This is the half that fails silently. A gauntlet is seated by
        /// <c>BodyEquipmentController.WearOnForearm</c>, which puts the model's own -Z along the
        /// wrist-to-elbow line and its +Y on the back of the arm — it reads the axes off the
        /// transform and trusts them. A turn applied to the model child to make the item lie
        /// prettier on the mat, or a <c>rotationOffset</c> left over from the era when these were
        /// seated in the HAND frame, rotates the whole gauntlet on the wearer's arm and nothing
        /// complains: it still hugs the forearm, it is just wearing its device on the palm.
        /// </para>
        /// <para>
        /// Compared as quaternions on purpose. A right angle has several equally valid euler
        /// spellings and Unity picks whichever one falls out of gimbal lock, so an assertion on
        /// the numbers would fail on a rotation that is correct.
        /// </para>
        /// </summary>
        [Test]
        public void RuinScanner_IsStillInTheGauntletFrame()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScannerPath);
            Assert.IsNotNull(prefab, $"no prefab at {ScannerPath}");

            var grip = prefab.GetComponent<ItemGrip>();
            Assert.IsNotNull(grip, $"{ScannerPath} has no ItemGrip, so nothing seats it at all");

            // The child that carries the geometry: the one a "fix" would turn.
            Transform model = prefab.transform.Cast<Transform>()
                .FirstOrDefault(c => c.GetComponentInChildren<Renderer>(true) != null);
            Assert.IsNotNull(model, $"{ScannerPath} has no child carrying geometry");

            Assert.Less(Quaternion.Angle(model.localRotation, Quaternion.identity), Slack,
                "the ruin scanner's model child has been turned. Its own axes are what " +
                $"WearOnForearm reads, so it will be worn at that angle. {Rebuild}");

            Assert.Less(Quaternion.Angle(Quaternion.Euler(grip.RotationOffset), Quaternion.identity), Slack,
                "the ruin scanner carries a rotationOffset. That offset turns an item in the HAND " +
                "frame, which a forearm gauntlet is never in; on the arm it is a tilt nobody asked " +
                $"for. {Rebuild}");
        }

        // ── The laser staff: the opposite case ───────────────────────────────

        private const string StaffPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/LaserStaff.prefab";
        private const string StaffAssetPath = "Assets/Game/Resources/Items/Artifacts/LaserStaff.asset";
        private const string StaffModelPath = "Assets/Game/Art/Models/Weapons/LaserStaff/laser_staff.fbx";

        private const string RebuildStaff =
            "Run Tools ▸ Build Laser Staff Artifact, which owns the turn as LaserStaffBuilder.LieDown.";

        /// <summary>
        /// The staff lies DOWN, and it lies down on the axis the lash line was cut for.
        ///
        /// <para>
        /// A gauntlet is the case where the model's own frame must not be touched; this is the
        /// case where it must. The FBX importer's −90&#176; about X leaves the staff's long axis on
        /// prefab +Y, and <c>ItemFootprint.FootprintOf</c> is defined as <c>(size.x, size.z)</c> —
        /// the shadow an item casts with its own up still up — so the staff reserved a 2 x 1 cell
        /// stub and was drawn balanced on its tip with 1.42 m of it standing out of the mat. That
        /// is backlog INV-01, and it was authored data.
        /// </para>
        /// <para>
        /// <b>1 cell wide is the whole point and it is won by 0.1%.</b> The gnarled staff measures
        /// 0.1199 x 0.1030 across, which on the mat is 1.163 cells by 0.999 — so which cross-axis
        /// stands up decides whether the footprint is 1 x 15 or 2 x 15, and only the first is
        /// something the 18 x 1 cell <c>LongGoods</c> lash line will take. That lash line exists
        /// for this staff and nothing else (<c>PackSurfaceId.LongGoods</c> carries the arithmetic),
        /// so a 2 here is not a cosmetic drift: it evicts the staff onto the rack, ski-fashion,
        /// as the only home left. Any move in <c>PackScale.Factor</c>, <c>PackGrid.Cell</c>, the
        /// staff's <c>holdSize</c> or the exported mesh can spend that 0.1%, and this is where it
        /// gets noticed.
        /// </para>
        /// </summary>
        [Test]
        public void LaserStaff_LiesDownAndFitsTheLashLine()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(StaffAssetPath);
            Assert.IsNotNull(asset, $"no InventoryItem at {StaffAssetPath}");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StaffPath);
            Assert.IsNotNull(prefab, $"no prefab at {StaffPath}");

            var grip = prefab.GetComponent<ItemGrip>();
            Assert.IsNotNull(grip, $"{StaffPath} has no ItemGrip, so nothing sizes it at all");

            // Through the grip's own size reference, which is the subtree ItemFootprint measures:
            // asking a different question from the one the pack asks is how a check passes on a
            // prefab the pack then draws differently.
            Bounds local = ItemBounds.Measure(prefab, grip.SizeReference);

            Assert.Greater(local.size.z, local.size.y,
                $"the laser staff measures {local.size.ToString("F3")} in its own frame. Its length " +
                $"is not on Z, so it is not lying down — the pack will stand it on its end. " +
                $"{RebuildStaff}");

            Assert.Less(local.size.y, local.size.z * 0.25f,
                $"the laser staff stands {local.size.y:F3} m off the mat against a {local.size.z:F3} m " +
                $"length. A pole put down on a shelf is thinner than that. {RebuildStaff}");

            ItemFootprint.ClearCache();
            PackShape shape = PackShapes.For(asset, null);

            Assert.AreEqual(1, shape.Width,
                $"the laser staff is {shape.Width} cells across, not 1. Its two cross-sections are " +
                "1.163 and 0.999 cells, so this is either the wrong one standing up or 0.999 has " +
                $"crept past a whole cell — see the note on this test. {RebuildStaff}");

            Assert.AreEqual(15, shape.Height,
                $"the laser staff is {shape.Height} cells long, not 15 — its 1.35 m holdSize on a " +
                $"{PackGrid.Cell:F4} m cell. {RebuildStaff}");

            GameObject rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            Assert.IsNotNull(rig, $"no rig at {RigPath}");

            PackSurface lashLine = rig.GetComponentsInChildren<PackSurface>(true)
                .FirstOrDefault(s => s.Id == PackSurfaceId.LongGoods);
            Assert.IsNotNull(lashLine, $"{RigPath} has lost its LongGoods face");

            Vector2Int cells = PackGrid.CellsOn(lashLine.Size);
            PackShape across = shape.Rotated(1);

            Assert.IsTrue(across.Width <= cells.x && across.Height <= cells.y,
                $"the laser staff is {across.Width}x{across.Height} cells laid across the " +
                $"{cells.x}x{cells.y} lash line, which was cut to take it square on and has no " +
                $"overhang to lend it. {RebuildStaff}");
        }

        /// <summary>
        /// The turn that laid it down was taken back out of the hand offset, exactly.
        ///
        /// <para>
        /// This is the half that fails silently, and it fails differently from the gauntlets'. The
        /// staff is <em>meant</em> to be turned; what must not change is where it sits in the fist,
        /// and <c>rotation = handRotation * Euler(rotationOffset)</c> only multiplies back out if
        /// the offset is the exact inverse of the turn. <c>-LieDown</c> — the negated euler, which
        /// is the right answer for <c>JumpingRodBuilder</c>'s single-axis turn and looks like the
        /// idiom — is 120&#176; away from the inverse of this one. A staff a third of a turn out of
        /// the fist is what that shortcut ships, with a prefab that looks perfectly fine on disk.
        /// </para>
        /// <para>
        /// Measured against the imported model's OWN root rotation rather than a typed −90, so the
        /// day the FBX is re-exported on another convention this test still asks the question it
        /// means to ask. Compared as quaternions: a right angle has several euler spellings.
        /// </para>
        /// </summary>
        [Test]
        public void LaserStaff_KeepsItsPoseInTheHand()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StaffPath);
            Assert.IsNotNull(prefab, $"no prefab at {StaffPath}");

            var grip = prefab.GetComponent<ItemGrip>();
            Assert.IsNotNull(grip, $"{StaffPath} has no ItemGrip, so nothing seats it at all");

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(StaffModelPath);
            Assert.IsNotNull(model, $"no model at {StaffModelPath}");

            Transform instance = prefab.transform.Cast<Transform>()
                .FirstOrDefault(c => c.GetComponentInChildren<Renderer>(true) != null);
            Assert.IsNotNull(instance, $"{StaffPath} has no child carrying geometry");

            // What the builder added on top of the importer's own axis conversion.
            Quaternion turn = instance.localRotation *
                              Quaternion.Inverse(model.transform.localRotation);

            Assert.Greater(Quaternion.Angle(turn, Quaternion.identity), Slack,
                "the laser staff's contents are unturned, so it is still standing on its end in " +
                $"the pack. {RebuildStaff}");

            Assert.Less(Quaternion.Angle(turn * Quaternion.Euler(grip.RotationOffset), Quaternion.identity),
                        Slack,
                $"the laser staff's contents are turned by {turn.eulerAngles.ToString("F1")} and its " +
                $"rotationOffset is {grip.RotationOffset.ToString("F1")}, which do not multiply back " +
                "out to identity. The two are supposed to cancel, so that laying the staff down on " +
                "the mat moves nothing in the hand. Take the inverse as a quaternion, not by " +
                $"negating the euler. {RebuildStaff}");

            Assert.AreEqual(Vector3.zero, grip.PositionOffset,
                "the laser staff's origin IS its grip, so nothing needs offsetting along it. A " +
                "position offset here means the model moved rather than turned, and the staff will " +
                $"be held somewhere along its own shaft. {RebuildStaff}");
        }
    }
}
