using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The rig's two status lamps say whether the pack is SUPPLYING air, which is not the same
    /// claim as "a tank is in the socket".
    ///
    /// <para>
    /// The distinction is the whole reason this component is not one line of
    /// <c>lamp.enabled = pack.TryFindSocketed(...)</c>. <see cref="Gameplay.OxygenSocket"/>
    /// deliberately reports a dead tank as still connected — it has to keep hold of it to write
    /// the last of the drain back — so a lamp taking "connected" at face value would sit green
    /// over a tank the wearer is suffocating beside.
    /// </para>
    /// <para>
    /// Every assertion is on <c>Renderer.enabled</c> rather than on any state the component
    /// exposes, because visibility IS the feature; a correct internal bool that never reached a
    /// renderer would be the failure this is here to catch. And the two lamps are checked in
    /// opposition every time — exactly one lit — since they are modelled at the same point and
    /// the same size, so both lit is a z-fight and neither lit is a hole in the valve block.
    /// </para>
    /// </summary>
    public class PackSocketLampTests
    {
        /// <summary>The socket: <see cref="PackSurfaceId.BackPanelCentre"/>, 3 x 6 cells.</summary>
        private static readonly Vector2 SocketSize = new(3f * PackGrid.Cell, 6f * PackGrid.Cell);

        /// <summary>A container with nothing to send anything to: these tests never transfer.</summary>
        private sealed class TestPack : PackContainer
        {
            public override void RequestTake(PackSurfaceId surface, Vector2 uv, Interactor interactor) { }

            public override void RequestStow(int slotIndex, PackSurfaceId surfaceId, Vector2 uv,
                                             float yaw, Interactor interactor) { }
        }

        private GameObject root;
        private GameObject tankPrefab;
        private InventoryItem tank;

        private TestPack pack;
        private PackSurface socket;
        private PackSocketLamp lamp;
        private Renderer supplied;
        private Renderer starved;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Rig");
            pack = root.AddComponent<TestPack>();

            var surfaceGo = new GameObject("SURF_BackPanel_C");
            surfaceGo.transform.SetParent(root.transform, false);
            socket = surfaceGo.AddComponent<PackSurface>();

            tank = Tank();

            var surfaceSo = new SerializedObject(socket);
            surfaceSo.FindProperty("id").enumValueIndex = (int)PackSurfaceId.BackPanelCentre;
            surfaceSo.FindProperty("size").vector2Value = SocketSize;

            // The reservation is what makes this face a SOCKET rather than a shelf, and
            // TryFindSocketed finds it by that list and by the kind on the item's own prefab.
            SerializedProperty accepts = surfaceSo.FindProperty("acceptsOnly");
            accepts.arraySize = 1;
            accepts.GetArrayElementAtIndex(0).objectReferenceValue = tank;
            surfaceSo.ApplyModifiedPropertiesWithoutUndo();

            supplied = Lamp("green_light");
            starved = Lamp("red_light");

            lamp = root.AddComponent<PackSocketLamp>();

            var lampSo = new SerializedObject(lamp);
            lampSo.FindProperty("container").objectReferenceValue = pack;
            lampSo.FindProperty("kind").enumValueIndex = (int)SupplyKind.Oxygen;
            lampSo.FindProperty("suppliedLamp").objectReferenceValue = supplied;
            lampSo.FindProperty("starvedLamp").objectReferenceValue = starved;
            lampSo.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (tankPrefab != null) Object.DestroyImmediate(tankPrefab);
            if (tank != null) Object.DestroyImmediate(tank);

            root = null;
            tankPrefab = null;
            tank = null;

            ItemFootprint.ClearCache();
        }

        /// <summary>A lamp bulb: a renderer and nothing else. Both start ON, so a test that
        /// asserts one went off is measuring a change rather than an initial condition.</summary>
        private Renderer Lamp(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);

            Renderer renderer = go.GetComponent<Renderer>();
            renderer.enabled = true;

            return renderer;
        }

        /// <summary>
        /// A tank: an item carrying an oxygen reservoir, authored full. The
        /// <see cref="DockableSupply"/> is not decoration — <c>TryFindSocketed</c> asks the
        /// PREFAB what kind it holds, so an item without one is invisible to the socket.
        /// </summary>
        private InventoryItem Tank()
        {
            tankPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tankPrefab.name = "OxygenTankPrefab";
            Object.DestroyImmediate(tankPrefab.GetComponent<Collider>());

            var grip = tankPrefab.AddComponent<ItemGrip>();
            var gripSo = new SerializedObject(grip);
            gripSo.FindProperty("packSize").floatValue = 0.25f;
            gripSo.ApplyModifiedPropertiesWithoutUndo();

            var supply = tankPrefab.AddComponent<DockableSupply>();
            var supplySo = new SerializedObject(supply);
            supplySo.FindProperty("kind").enumValueIndex = (int)SupplyKind.Oxygen;
            supplySo.FindProperty("capacity").floatValue = 1800f;
            supplySo.FindProperty("startingCharge").floatValue = 1f;
            supplySo.ApplyModifiedPropertiesWithoutUndo();

            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.name = "OxygenTank";
            item.ID = "test-oxygen-tank";
            item.itemPrefab = tankPrefab;

            return item;
        }

        private void Place(float charge) =>
            Assert.IsTrue(pack.TryPlace(tank, socket.Id, socket.Size * 0.5f, 0f, charge),
                          "the socket has to take the tank before the lamps mean anything");

        private void AssertLit(bool wantSupplied, string because)
        {
            Assert.AreEqual(wantSupplied, supplied.enabled,
                            $"the SUPPLIED lamp should be {(wantSupplied ? "lit" : "dark")} {because}");
            Assert.AreEqual(!wantSupplied, starved.enabled,
                            $"the STARVED lamp should be {(wantSupplied ? "dark" : "lit")} {because}");
        }

        [Test]
        public void AnEmptySocketLightsTheStarvedLamp()
        {
            lamp.Refresh();

            AssertLit(false, "when there is no tank in the socket at all");
        }

        [Test]
        public void ATankWithAirInItLightsTheSuppliedLamp()
        {
            Place(0.5f);
            lamp.Refresh();

            AssertLit(true, "when a half-full tank is plugged in");
        }

        [Test]
        public void AConnectedButEmptyTankLightsTheStarvedLamp()
        {
            Place(0f);
            lamp.Refresh();

            Assert.IsTrue(pack.TryFindSocketed(SupplyKind.Oxygen, out _),
                          "the fixture is only meaningful while the socket still reports the dead " +
                          "tank as connected — that is the case the lamp has to disagree with");

            AssertLit(false, "when the tank in the socket has no air left in it");
        }

        [Test]
        public void TheLampsFollowADrainWithoutBeingTold()
        {
            Place(1f);
            lamp.Refresh();
            AssertLit(true, "with a full tank");

            // No Refresh() call: PackLayout.SetCharge raises OnChanged, and OxygenSocket forces a
            // write-back exactly ON empty rather than at the whole-percent step below it. That is
            // what makes the last of the air and the change of lamp the same moment.
            pack.SetCharge(pack.Layout.Placements[0].ItemId, 0f);

            AssertLit(false, "once the drain has written the tank down to empty");
        }

        [Test]
        public void ATankThatHasNeverCarriedAChargeReadsAsItsAuthoredFill()
        {
            // SupplyCharge.None is "has never been through a container that knows about charges",
            // which every other path reads as the item's authored starting charge. Read as zero it
            // would light the starved lamp over a full tank nobody has touched.
            //
            // Through AdoptPlacements, because that is the ONE path that can produce a None on a
            // live placement: TryPlace defaults the charge on the way in (DefaultedCharge), and a
            // restore deliberately does not — it replays the record it was given.
            Place(1f);
            pack.AdoptPlacements(new[] { pack.Layout.Placements[0].WithCharge(SupplyCharge.None) });

            Assert.AreEqual(SupplyCharge.None, pack.Layout.Placements[0].Charge,
                            "the fixture is only meaningful if the restored placement really did " +
                            "keep its None — if a default crept in, this test proves nothing");

            lamp.Refresh();

            AssertLit(true, "for a restored tank with no charge recorded, which means 'authored full'");
        }
    }
}
