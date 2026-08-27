using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    public class ItemFootprintTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> spawned = new();

        [SetUp]
        public void ClearMeasurementCache()
        {
            // Measurements are cached per prefab GameObject, and these tests mint and destroy their
            // own. Left alone the cache would carry a previous test's answer for a prefab that no
            // longer exists.
            ItemFootprint.ClearCache();
        }

        [TearDown]
        public void CleanUp()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
            ItemFootprint.ClearCache();
        }

        /// <summary>
        /// A prefab whose mesh measures <paramref name="meshSize"/> metres, optionally carrying an
        /// <see cref="ItemGrip"/> that declares a hold size.
        ///
        /// <para>
        /// The mesh hangs off a CHILD deliberately: <c>ItemBounds</c> measures in the root's own
        /// local space, so a mesh on the root itself comes back at its raw mesh bounds however the
        /// root is scaled. <c>holdSize</c> is a private serialized field, so it is written the same
        /// way the inspector writes it rather than making <see cref="ItemGrip"/> grow a setter.
        /// </para>
        /// </summary>
        /// <param name="holdSize">Authored metres. Only meaningful with <paramref name="withGrip"/>.</param>
        /// <param name="packSize">
        /// Authored metres for the pack. 0 — the default, and what nearly every shipped prefab
        /// carries — means "the same size as in the hand", so a plain Prefab(size, holdSize) call
        /// still asks the question the older tests were written against.
        /// </param>
        /// <param name="withGrip">
        /// Whether the prefab carries an <see cref="ItemGrip"/> at all. Distinct from a holdSize of
        /// zero, and the distinction matters: no grip means nobody ever sized this thing for a hand,
        /// so its raw mesh bounds are not to be trusted; a grip that says zero is somebody choosing
        /// the authored size on purpose. Defaults to true so a plain Prefab(size) call still asks
        /// the "authored size, deliberately" question the older tests were written against.
        /// </param>
        private GameObject Prefab(Vector3 meshSize, float holdSize = 0f, bool withGrip = true,
                                  float packSize = 0f)
        {
            var root = new GameObject("prefab");
            spawned.Add(root);

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = meshSize;

            if (withGrip)
            {
                var grip = root.AddComponent<ItemGrip>();
                typeof(ItemGrip).GetField("holdSize", Hidden).SetValue(grip, holdSize);
                typeof(ItemGrip).GetField("packSize", Hidden).SetValue(grip, packSize);
            }

            return root;
        }

        [Test]
        public void ALongThinItemGetsWebbingStraps()
        {
            // LaserStaff: 1.35 m long, slim.
            Assert.AreEqual(HolderKind.Webbing,
                ItemFootprint.Classify(new Vector3(0.06f, 0.06f, 1.35f)));
        }

        [Test]
        public void ATallRoundItemGetsShockCord()
        {
            // AntiGravityPotion: a 0.30 m bottle.
            Assert.AreEqual(HolderKind.Cord,
                ItemFootprint.Classify(new Vector3(0.11f, 0.30f, 0.11f)));
        }

        [Test]
        public void ALumpGetsBungee()
        {
            Assert.AreEqual(HolderKind.Bungee,
                ItemFootprint.Classify(new Vector3(0.22f, 0.18f, 0.20f)));
        }

        /// <summary>
        /// A cube is not a bottle. It used to be one: the Cord branch had no lower bound on
        /// slenderness, and the "is it standing up" test was <c>longest == size.y</c>, a float
        /// identity against a value <c>Mathf.Max</c> had just produced from the same components. On
        /// a tie that is true for y whether or not y was the axis chosen, so a 0.30 m cube came out
        /// upright, round (|x - z| = 0) and slender (0.30 / 0.30 = 1.0, under the 4.0 ceiling) —
        /// three yeses and a shock cord round a box.
        /// </summary>
        [Test]
        public void ACubeIsNotABottle()
        {
            Assert.AreEqual(HolderKind.Bungee,
                ItemFootprint.Classify(new Vector3(0.30f, 0.30f, 0.30f)));
        }

        /// <summary>
        /// The same lump, modelled three ways up, gets the same holder. The upright one is the case
        /// that used to fail: 0.22 / 0.20 = 1.1 is barely taller than it is wide, nothing like the
        /// 0.30 / 0.11 = 2.7 of a real bottle, and the missing lower bound called it Cord anyway.
        /// </summary>
        [Test]
        public void ALumpGetsBungeeWhicheverWayUpItIsModelled()
        {
            Assert.AreEqual(HolderKind.Bungee,
                ItemFootprint.Classify(new Vector3(0.18f, 0.22f, 0.20f)), "tallest axis y");

            Assert.AreEqual(HolderKind.Bungee,
                ItemFootprint.Classify(new Vector3(0.22f, 0.18f, 0.20f)), "tallest axis x");

            Assert.AreEqual(HolderKind.Bungee,
                ItemFootprint.Classify(new Vector3(0.20f, 0.18f, 0.22f)), "tallest axis z");
        }

        /// <summary>
        /// <see cref="HolderKind.Sleeve"/> means "long, with the mass at one end", which a size
        /// alone cannot say — so before the bounds centre was threaded through, nothing could ever
        /// return it and <c>Holder_Sleeve</c> was dead art.
        ///
        /// <para>
        /// A 0.90 m haft with its head bunched 0.20 m off the pivot: 0.20 is 22% of 0.90, over the
        /// 15% the classifier asks for.
        /// </para>
        /// </summary>
        [Test]
        public void ALongItemWithItsMassAtOneEndGetsASleeve()
        {
            Assert.AreEqual(HolderKind.Sleeve,
                ItemFootprint.Classify(new Vector3(0.06f, 0.06f, 0.90f), new Vector3(0f, 0f, 0.20f)));
        }

        /// <summary>
        /// And a rod of the same proportions with its bulk in the middle is still Webbing. 0.05 m
        /// off centre on a 0.90 m item is 5.6%, well under the 15% threshold — the guard has to be
        /// a threshold rather than "is it non-zero", or every item whose pivot is a millimetre off
        /// becomes a hand tool.
        /// </summary>
        [Test]
        public void ACentredLongItemIsStillWebbing()
        {
            Assert.AreEqual(HolderKind.Webbing,
                ItemFootprint.Classify(new Vector3(0.06f, 0.06f, 0.90f), new Vector3(0f, 0f, 0.05f)));
        }

        [Test]
        public void SomethingTinyGetsAClip()
        {
            Assert.AreEqual(HolderKind.Clip,
                ItemFootprint.Classify(new Vector3(0.05f, 0.08f, 0.04f)));
        }

        /// Footprint is the shadow on the surface, so the vertical axis must not appear in it —
        /// a standing bottle occupies its diameter, not its height.
        [Test]
        public void FootprintDropsTheVerticalAxis()
        {
            Vector2 f = ItemFootprint.FootprintOf(new Vector3(0.11f, 0.30f, 0.13f));

            Assert.AreEqual(0.11f, f.x, 1e-4f);
            Assert.AreEqual(0.13f, f.y, 1e-4f);
        }

        /// <summary>
        /// The single behaviour that makes the pack read as physical: an item measures the metres
        /// its <c>holdSize</c> declares, at its own proportions.
        ///
        /// <para>
        /// The mesh is 0.20 x 0.10 x 0.50 m, so its longest axis is 0.50 and a declared hold size
        /// of 1.35 m scales everything by 1.35 / 0.50 = 2.7: (0.54, 0.27, 1.35). Longest axis 1.35
        /// as asked, and the 2 : 1 : 5 proportions untouched. Getting this wrong in either
        /// direction is invisible in a unit test of <c>Classify</c> and glaring in the game — a
        /// cube of the item at 1.35 m each way, or a staff still at its authored 0.50 m.
        /// </para>
        /// </summary>
        [Test]
        public void HoldSizeScalesTheMeshToTrueMetresKeepingProportions()
        {
            Vector3 size = ItemFootprint.SizeOf(Prefab(new Vector3(0.20f, 0.10f, 0.50f), 1.35f));

            Assert.AreEqual(1.35f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)), 1e-3f,
                            "holdSize names the longest axis in metres");

            Assert.AreEqual(0.54f, size.x, 1e-3f);
            Assert.AreEqual(0.27f, size.y, 1e-3f);
            Assert.AreEqual(1.35f, size.z, 1e-3f);
        }

        /// <summary>
        /// And with no <c>holdSize</c> authored, the mesh's own measurement stands. Without this
        /// the test above passes just as well against code that returns a cube of <c>holdSize</c>
        /// and ignores the mesh entirely.
        /// </summary>
        [Test]
        public void WithNoHoldSizeTheMeasuredMeshStands()
        {
            Vector3 size = ItemFootprint.SizeOf(Prefab(new Vector3(0.20f, 0.10f, 0.50f)));

            Assert.AreEqual(0.20f, size.x, 1e-3f);
            Assert.AreEqual(0.10f, size.y, 1e-3f);
            Assert.AreEqual(0.50f, size.z, 1e-3f);
        }

        /// <summary>
        /// A prefab with <b>no ItemGrip at all</b> is a different case, and it is the one that
        /// caused a real bug: the dragged ghost swallowing the entire screen.
        ///
        /// <para>
        /// Measured across the shipped roster, prefabs with no grip come out at whatever scale the
        /// modeller happened to build at — <c>CixinGunEquipped</c> at <b>11.13 m</b>,
        /// <c>Gun</c> at 2.54 m. Held 1.9 m from the focus camera, an 11 m item is the screen. The
        /// previous code hid this by forcing every pocketed item to a flat 0.3 m; true scale
        /// exposed it.
        /// </para>
        /// <para>
        /// The answer is not a new rule — <c>EquipItemSocket</c> has resolved the same question the
        /// same way since long before the pack existed, falling back to 0.30 m when there is no
        /// grip. This test pins the two together.
        /// </para>
        /// </summary>
        [Test]
        public void AnUnsizedPrefabFallsBackToTheDefaultRatherThanItsRawMesh()
        {
            var huge = new Vector3(2.65f, 2.91f, 11.13f);   // CixinGunEquipped, measured

            Vector3 size = ItemFootprint.SizeOf(Prefab(huge, withGrip: false));
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            Assert.AreEqual(0.30f, longest, 1e-3f,
                            "a prefab nobody sized must fall back to EquipItemSocket's 0.30 m");

            // Proportions still honoured — it is scaled down, not turned into a cube.
            Assert.AreEqual(huge.x / huge.z, size.x / size.z, 1e-3f);
        }

        // ── The pack size is allowed to disagree with the hand ───────────────────

        /// <summary>
        /// The point of the whole asymmetry: an item authored with a <c>packSize</c> lies on the
        /// mat at THAT size, not at the size it is in the hand.
        ///
        /// <para>
        /// The Grappling Hook is the case it was added for. At its hand size of 1.00 m it is
        /// twelve cells long, and the widest face on the deployed rig is eight — so it fit nowhere
        /// but the rack, while reading as a rifle-length object lying next to actual rifles.
        /// </para>
        /// </summary>
        [Test]
        public void PackSizeOverridesHoldSizeOnTheMat()
        {
            Vector3 size = ItemFootprint.SizeOf(
                Prefab(new Vector3(0.20f, 0.10f, 0.50f), holdSize: 1.00f, packSize: 0.54f));

            Assert.AreEqual(0.54f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)), 1e-3f,
                            "packSize names the longest axis on the pack, in metres");

            // Proportions still come from the mesh, exactly as they do for holdSize.
            Assert.AreEqual(0.20f / 0.50f, size.x / size.z, 1e-3f);
        }

        /// <summary>
        /// And with no <c>packSize</c> the hand size stands. This is what keeps every prefab that
        /// predates the field — which is nearly all of them, since the YAML simply has no such key
        /// and deserializes to 0 — measuring exactly as it did before.
        /// </summary>
        [Test]
        public void WithNoPackSizeTheHoldSizeStands()
        {
            Vector3 size = ItemFootprint.SizeOf(Prefab(new Vector3(0.20f, 0.10f, 0.50f), 1.35f));

            Assert.AreEqual(1.35f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)), 1e-3f);
        }

        /// <summary>
        /// A <c>packSize</c> must not be able to resurrect an item whose grip deliberately says
        /// "keep the size the artist built" — but the reverse, sizing the pack copy of an
        /// otherwise-unsized item, has to work. The Item Scanner is the shipped holdSize-0 case.
        /// </summary>
        [Test]
        public void PackSizeSizesAnItemWhoseHoldSizeIsZero()
        {
            Vector3 size = ItemFootprint.SizeOf(
                Prefab(new Vector3(0.20f, 0.10f, 0.50f), holdSize: 0f, packSize: 0.25f));

            Assert.AreEqual(0.25f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)), 1e-3f);
        }

        /// <summary>
        /// A negative <c>packSize</c> typed into the inspector must read as "unset", not as a
        /// mirrored item. <c>OnValidate</c> clamps it, and the property clamps again so a value
        /// written by reflection or by an older serialized file cannot get past.
        /// </summary>
        [Test]
        public void ANegativePackSizeIsTreatedAsUnset()
        {
            Vector3 size = ItemFootprint.SizeOf(
                Prefab(new Vector3(0.20f, 0.10f, 0.50f), holdSize: 0.80f, packSize: -1f));

            Assert.AreEqual(0.80f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)), 1e-3f);
        }
    }
}
