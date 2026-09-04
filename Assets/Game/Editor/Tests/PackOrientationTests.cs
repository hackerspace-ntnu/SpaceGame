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
        /// 4 x 5 is 20 cells of the rig's 255, up from the 16 it cost while
        /// <see cref="GauntletPrefab.PackSize"/> was a chosen 0.54. That number went to 0 in the
        /// same change: it existed to shrink a bracer too bulky to lie on a mat, and what lies on
        /// the mat now is a 0.39 m lamp. So the count is a consequence again, not a decision.
        /// </para>
        /// </summary>
        [Test]
        public void RuinScanner_StandsDeckUpAndCostsFourByFive()
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

            string cells = $"the ruin scanner is {shape.Width}x{shape.Height} cells, not the 4x5 " +
                           "silhouette a deck-up gauntlet casts. Either the model's frame moved, " +
                           "or ItemGrip.packSize moved underneath it.";

            Assert.AreEqual(4, shape.Width, cells);
            Assert.AreEqual(5, shape.Height, cells);

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

    }
}
