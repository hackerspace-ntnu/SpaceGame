using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The claims that make a supply's fill bar worth having: it is a LENGTH and not just a colour,
    /// that length actually covers the permanently-lit geometry underneath it, and it still reads on
    /// a display copy with every script stripped off — which is two of the three places the player
    /// ever sees one.
    /// </summary>
    public class SupplyGaugeTests
    {
        private const string TankPrefab = "Assets/Game/Prefabs/Items/Supplies/OxygenTank.prefab";
        private const string CellPrefab = "Assets/Game/Prefabs/Items/Supplies/Battery.prefab";

        private GameObject spawned;

        [TearDown]
        public void TearDown()
        {
            if (spawned != null) Object.DestroyImmediate(spawned);
            spawned = null;
        }

        /// <summary>
        /// The bar reads as a length. This is the whole point of the rework — the gauge it replaced
        /// was a flat tint, and a hue-only reading is invisible to a red-green colourblind player
        /// (<c>GDC-L1-UX-0003</c>, <c>GDC-L1-UX-0006</c>). A regression that painted the colour and
        /// forgot the scale would look completely fine on any screenshot taken by someone who can
        /// see it.
        /// </summary>
        [Test]
        public void TheBarIsALengthAndNotOnlyAColour()
        {
            GameObject root = Rig();
            SupplyGauge gauge = SupplyGauge.Bind(root.transform);
            Assert.IsTrue(gauge.Exists, "the rig has no anchor to bind");

            Transform anchor = root.transform.Find(SupplyGauge.AnchorName);

            gauge.Paint(0.37f);
            Assert.That(anchor.localScale.x, Is.EqualTo(0.37f).Within(1e-4f),
                        "the bar's length does not follow the charge");

            gauge.Paint(0f);
            Assert.That(anchor.localScale.x, Is.EqualTo(0f).Within(1e-4f),
                        "an empty reservoir still draws a bar");

            // Out of range on both sides: every caller arrives by a delta from a drain or a fill.
            gauge.Paint(1.4f);
            Assert.That(anchor.localScale.x, Is.EqualTo(1f).Within(1e-4f), "the bar overran its track");
            gauge.Paint(-3f);
            Assert.That(anchor.localScale.x, Is.EqualTo(0f).Within(1e-4f), "the bar went negative");
        }

        /// <summary>
        /// The ramp goes through amber. A single green→red lerp is the obvious implementation and it
        /// crosses a muddy grey-brown at the half mark, which reads as a fault light rather than as
        /// "half full" — so the midpoint is the value worth pinning.
        /// </summary>
        [Test]
        public void TheRampPassesThroughAmberRatherThanThroughMud()
        {
            Assert.That(Delta(SupplyGauge.ColourAt(1f), SupplyGauge.Full), Is.LessThan(0.01f));
            Assert.That(Delta(SupplyGauge.ColourAt(SupplyGauge.MidStop), SupplyGauge.Mid),
                        Is.LessThan(0.01f));
            Assert.That(Delta(SupplyGauge.ColourAt(0f), SupplyGauge.Low), Is.LessThan(0.01f));

            // What "mud" means, measured: a colour a single green→red lerp would land on at the
            // middle is much darker than amber, because the two ends cancel rather than blend.
            Color muddy = Color.Lerp(SupplyGauge.Low, SupplyGauge.Full, 0.5f);
            Color amber = SupplyGauge.ColourAt(0.5f);
            Assert.That(amber.r + amber.g + amber.b, Is.GreaterThan(muddy.r + muddy.g + muddy.b),
                        "the half-full colour is no brighter than a straight green-to-red lerp, " +
                        "so the amber stop is not doing anything");
        }

        /// <summary>
        /// Both shipped items carry a bar, and its TRACK covers every scrap of the lit geometry it
        /// sits on.
        ///
        /// <para>
        /// This is the test that earns its place. Both models light their gauge permanently — the
        /// bottle's contents strip is an emissive material and the battery's ladder has three of five
        /// bars baked on — so lit geometry showing round the edge of the track reads as charge that
        /// is not there, and an empty battery would look 60% full. It is also the only check on the
        /// builder's mirror-to-symmetry rule, which is what stretches the battery's bar from the lit
        /// three to all five.
        /// </para>
        /// </summary>
        [Test]
        public void EveryShippedSupplyHasABarCoveringItsPermanentlyLitGauge()
        {
            foreach (string path in new[] { TankPrefab, CellPrefab })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, "No prefab at " + path +
                                         " — run Tools/SpaceGame/Items/Build Oxygen Gear.");

                spawned = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

                Assert.IsTrue(SupplyGauge.Bind(spawned.transform).Exists,
                              path + " has no " + SupplyGauge.AnchorName);

                Transform track = spawned.transform.Find(SupplyGauge.TrackName);
                Assert.IsNotNull(track, path + " has no " + SupplyGauge.TrackName);

                var supply = spawned.GetComponent<DockableSupply>();
                Assert.IsNotNull(supply, path + " has no DockableSupply");
                Assert.IsNotNull(supply.Readout, path + " has no gauge mesh");

                Bounds lit = LitBounds(spawned.transform, supply.Readout);
                Bounds cover = Flatten(spawned.transform, track);

                Assert.That(cover.min.x, Is.LessThanOrEqualTo(lit.min.x),
                            path + "'s track leaves lit gauge showing past one end of the bar");
                Assert.That(cover.max.x, Is.GreaterThanOrEqualTo(lit.max.x),
                            path + "'s track leaves lit gauge showing past the other end — for the " +
                            "battery this is the mirror-to-symmetry rule failing, and an empty one " +
                            "reads as part full.");

                Object.DestroyImmediate(spawned);
                spawned = null;
            }
        }

        /// <summary>
        /// A display copy still draws its charge. <c>DisplayCopy.Strip</c> takes every MonoBehaviour
        /// off, so the copy standing in the oxygen plant and the one lying on a pack mat have no
        /// <see cref="DockableSupply"/> to draw their own bar — the machine and the container paint
        /// them instead, and they can only do that by NAME. A gauge that quietly depended on its
        /// component would work perfectly in the hand and be frozen in both other places.
        /// </summary>
        [Test]
        public void AStrippedDisplayCopyStillDrawsItsCharge()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TankPrefab);
            Assert.IsNotNull(prefab, "No prefab at " + TankPrefab);

            spawned = new GameObject("Stage");
            GameObject copy = DisplayCopy.Make(prefab, spawned.transform);

            Assert.IsEmpty(copy.GetComponentsInChildren<MonoBehaviour>(true),
                           "the copy kept a script, so this test is not proving what it claims");

            SupplyGauge gauge = SupplyGauge.Bind(copy.transform);
            Assert.IsTrue(gauge.Exists, "the bar did not survive the strip");

            Transform anchor = copy.transform.Find(SupplyGauge.AnchorName);
            gauge.Paint(0.2f);
            Assert.That(anchor.localScale.x, Is.EqualTo(0.2f).Within(1e-4f),
                        "a stripped copy cannot be told how full it is");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>A bare anchor-and-fill pair, so the painter is tested without a built prefab.</summary>
        private GameObject Rig()
        {
            spawned = new GameObject("Item");

            var anchor = new GameObject(SupplyGauge.AnchorName).transform;
            anchor.SetParent(spawned.transform, false);

            GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = SupplyGauge.FillName;
            Object.DestroyImmediate(fill.GetComponent<Collider>());
            fill.transform.SetParent(anchor, false);

            return spawned;
        }

        private static float Delta(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        /// <summary>
        /// The emissive submesh's own extent, in the item root's frame and projected onto the bar's
        /// own axis — which is the axis the track has to cover and the only one a mirroring mistake
        /// can go wrong on.
        /// </summary>
        private static Bounds LitBounds(Transform root, Renderer readout)
        {
            var filter = readout.GetComponent<MeshFilter>();
            Mesh mesh = filter.sharedMesh;

            int submesh = readout.sharedMaterials
                .Select((m, i) => (m, i))
                .First(pair => pair.m != null && pair.m.name.StartsWith("Mat_Emissive")).i;

            Matrix4x4 toBar = BarFrame(root).inverse * root.worldToLocalMatrix *
                              readout.transform.localToWorldMatrix;

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.GetTriangles(submesh);

            var bounds = new Bounds(toBar.MultiplyPoint3x4(vertices[triangles[0]]), Vector3.zero);
            for (int i = 1; i < triangles.Length; i++)
                bounds.Encapsulate(toBar.MultiplyPoint3x4(vertices[triangles[i]]));

            return bounds;
        }

        /// <summary>The track's extent in the same frame, so the two are directly comparable.</summary>
        private static Bounds Flatten(Transform root, Transform track)
        {
            Matrix4x4 toBar = BarFrame(root).inverse * root.worldToLocalMatrix *
                              track.localToWorldMatrix;

            var unit = new Bounds(Vector3.zero, Vector3.one);
            var bounds = new Bounds(toBar.MultiplyPoint3x4(unit.min), Vector3.zero);

            for (int corner = 1; corner < 8; corner++)
            {
                var point = new Vector3((corner & 1) == 0 ? unit.min.x : unit.max.x,
                                        (corner & 2) == 0 ? unit.min.y : unit.max.y,
                                        (corner & 4) == 0 ? unit.min.z : unit.max.z);
                bounds.Encapsulate(toBar.MultiplyPoint3x4(point));
            }

            return bounds;
        }

        /// <summary>
        /// The bar's own frame, taken off the anchor: +X along the bar. Read from the built prefab
        /// rather than assumed, because which model axis a bar runs along is the builder's finding
        /// and differs between the bottle and the battery.
        /// </summary>
        private static Matrix4x4 BarFrame(Transform root)
        {
            Transform anchor = root.Find(SupplyGauge.AnchorName);
            return Matrix4x4.Rotate(anchor.localRotation);
        }
    }
}
