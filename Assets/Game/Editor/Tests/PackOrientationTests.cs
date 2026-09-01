using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// How an item lies down on the pack, pinned on disk.
    ///
    /// <para>
    /// The pack draws every item with its OWN up still up — <c>ItemFootprint.FootprintOf</c> is
    /// defined as <c>(size.x, size.z)</c> — so "this thing is standing on its end on the mat" is
    /// authored data and is corrected by turning the prefab's contents in
    /// <c>ItemPackOrientation</c>. The correction has two halves and the second one is invisible:
    /// turning the contents by R and <c>ItemGrip.rotationOffset</c> by R inverse multiplies back
    /// out, because <c>rotation = handRotation * Euler(offset)</c>. Do the first half and forget
    /// the second and the item lies correctly on the mat while pointing sideways out of the
    /// player's fist — which nothing warns about, and which no pack test would ever see.
    /// </para>
    /// <para>
    /// Both tests here are about <c>RuinScanner</c>, the one item on the roster whose hand offset
    /// was NOT identity before it was turned, so it is the one where "compensate by the inverse"
    /// and "leave it at zero" give different answers.
    /// </para>
    /// </summary>
    public class PackOrientationTests
    {
        private const string ScannerPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/RuinScanner.prefab";
        private const string ScannerAssetPath = "Assets/Game/Resources/Items/Artifacts/RuinScanner.asset";
        private const string RigPath = "Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab";

        private const string RunTheFix =
            "Run Tools ▸ SpaceGame ▸ Items ▸ Fix Artifact Pack Orientation and read its verify lines.";

        /// <summary>
        /// Where the scanner's hand pose was before the turn: the model child at (90, -90, 0) under
        /// a <c>rotationOffset</c> of (0, 90, 0). The PRODUCT of those two is what the hand sees,
        /// and it is the thing that must not move.
        /// </summary>
        private static readonly Quaternion PoseInTheHand =
            Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(90f, -90f, 0f);

        /// <summary>Degrees of slop, matching <c>ItemPackOrientation</c>'s own.</summary>
        private const float Slack = 1f;

        /// <summary>
        /// The scanner lies on a flank instead of standing on the rear face of its body slab.
        ///
        /// <para>
        /// It is a pistol-grip survey unit — a body slab with the readout on its broad face, a grip
        /// hanging off one side, an emitter at one end and an antenna at the other — and the prefab
        /// used to point that emitter at the sky, reserving the 8 x 3 cells of a slab balanced on
        /// its edge. The measurement, not the eye, is what says so: the axis that is up must be the
        /// SMALLEST one for a slab put down flat, and here that is the 0.204 m across the flanks
        /// rather than the 0.467 m through the body.
        /// </para>
        /// <para>
        /// The cell count is asserted as well as the axis because the two say different things. The
        /// axis says which way up; the count says what it costs, and 8 x 6 is the honest silhouette
        /// of the device — 48 cells of the rig's 255, against 24 for the same object stood on end.
        /// A turn that changed the footprint to anything else is a turn about the wrong axis.
        /// </para>
        /// </summary>
        [Test]
        public void RuinScanner_LiesOnAFlank()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InventoryItem>(ScannerAssetPath);
            Assert.IsNotNull(asset, $"no InventoryItem at {ScannerAssetPath}");

            ItemFootprint.ClearCache();
            Vector3 size = ItemFootprint.SizeOf(asset.itemPrefab);
            PackShape shape = PackShapes.For(asset, null);

            Assert.IsTrue(size.y <= size.x && size.y <= size.z,
                $"the ruin scanner measures {size.ToString("F3")}, so a bigger axis than y is up " +
                $"and it is not lying flat. {RunTheFix}");

            string cells = $"the ruin scanner is {shape.Width}x{shape.Height} cells, not the 8x6 " +
                           "silhouette a flank-down scanner casts. Either the turn went about the " +
                           "wrong axis, or ItemGrip.packSize moved underneath it.";

            Assert.AreEqual(8, shape.Width, cells);
            Assert.AreEqual(6, shape.Height, cells);

            GameObject rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            Assert.IsNotNull(rig, $"no rig at {RigPath}");

            // Strictly, on a real face, with no help from PackOverhang: laying an item down is only
            // worth its cells if it still has an honest home afterwards.
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
        /// Turning the contents did not move the item in the player's hand.
        ///
        /// <para>
        /// This is the half of the correction that fails silently. <c>EquipItemSocket</c> seats an
        /// item as <c>handRotation * Euler(rotationOffset)</c> and then puts the grip point in the
        /// palm, so the hand only ever sees the PRODUCT of the offset and whatever the contents are
        /// rotated by. Turn the contents by R without dividing R back out of the offset and the
        /// scanner still lies correctly on the mat while sticking out of the fist at a right angle
        /// — no warning, no failed test anywhere in the pack suite, and it only shows up in a
        /// screenshot.
        /// </para>
        /// <para>
        /// The product is compared as a quaternion on purpose. A right angle has several equally
        /// valid euler spellings and Unity picks whichever one it likes out of gimbal lock, so an
        /// assertion on <c>rotationOffset</c>'s numbers would fail on a rotation that is correct.
        /// </para>
        /// </summary>
        [Test]
        public void RuinScanner_PoseInTheHandIsUnchanged()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScannerPath);
            Assert.IsNotNull(prefab, $"no prefab at {ScannerPath}");

            var grip = prefab.GetComponent<ItemGrip>();
            Assert.IsNotNull(grip, $"{ScannerPath} has no ItemGrip, so nothing seats it in the hand");

            // The same child the orientation tool turns: the one that carries the geometry.
            Transform model = prefab.transform.Cast<Transform>()
                .FirstOrDefault(c => c.GetComponentInChildren<Renderer>(true) != null);
            Assert.IsNotNull(model, $"{ScannerPath} has no child carrying geometry");

            Quaternion seated = Quaternion.Euler(grip.RotationOffset) * model.localRotation;

            Assert.Less(Quaternion.Angle(seated, PoseInTheHand), Slack,
                $"the scanner now seats at {seated.eulerAngles.ToString("F1")} in the hand instead " +
                $"of {PoseInTheHand.eulerAngles.ToString("F1")}: its contents were turned without " +
                $"dividing the same turn back out of ItemGrip.rotationOffset. {RunTheFix}");
        }
    }
}
