using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The breathing hose is drawn under a MARKER on the rig, and the rig's FBX is on the
    /// centimetre convention — <c>PIVOT_Back</c> arrives with <c>Lcl Scaling</c> 100 and every
    /// empty parented to it inherits it. So a world length written straight onto a child of that
    /// marker is drawn a hundred times over.
    ///
    /// <para>
    /// <b>That fault hid behind a half-correct hose for a build.</b> The tube's LENGTH comes back
    /// from <c>InverseTransformPoint</c> and is therefore already in the marker's frame, so the
    /// hose reached the bottle exactly right; only its THICKNESS was raw metres, and a 14 mm hose
    /// came out 2.8 m across — a near-black slug (<c>Mat_Plastic_Rubber_Black</c>, #1A1A1A) wider
    /// than the whole 1.81 m rig, appearing the moment a bottle went into the socket and never
    /// when it was empty. Both halves are measured here, because a guard on the thickness alone
    /// would pass a hose that no longer reaches the bottle.
    /// </para>
    /// <para>
    /// Measured in WORLD metres off the built tube, never in the local numbers the component
    /// writes: the local numbers were never wrong, the frame they were written in was.
    /// </para>
    /// </summary>
    public class PackHoseTests
    {
        /// <summary>
        /// The scale the rig's own <c>PIVOT_Back</c> arrives at, and with it every SURF_ empty and
        /// every marker under it. The whole point of the fixture.
        /// </summary>
        private const float CentimetreConvention = 100f;

        /// <summary>The hose's authored radius, in the rig's original frame — <c>PackHose</c>'s
        /// own default, and the number the assertions below are stated against.</summary>
        private const float Radius = 0.014f;

        /// <summary>The socket: <see cref="PackSurfaceId.BackPanelCentre"/>, 3 x 6 cells.</summary>
        private static readonly Vector2 SocketSize = new(3f * PackGrid.Cell, 6f * PackGrid.Cell);

        /// <summary>Metres of slack on a measurement of a metre-scale drawn object.</summary>
        private const float Tolerance = 1e-3f;

        /// <summary>A container with nothing to send anything to: these tests never transfer.</summary>
        private sealed class TestPack : PackContainer
        {
            public override void RequestTake(PackSurfaceId surface, Vector2 uv, Interactor interactor) { }

            public override void RequestStow(int slotIndex, PackSurfaceId surfaceId, Vector2 uv,
                                             float yaw, Interactor interactor) { }
        }

        private GameObject root;
        private GameObject itemPrefab;
        private InventoryItem bottle;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (itemPrefab != null) Object.DestroyImmediate(itemPrefab);
            if (bottle != null) Object.DestroyImmediate(bottle);

            root = null;
            itemPrefab = null;
            bottle = null;

            ItemFootprint.ClearCache();
        }

        /// <summary>
        /// The rig, in miniature and in its own units:
        /// <code>
        /// root                       the container
        ///  └─ PIVOT_Back  x100       the FBX's centimetre convention
        ///      ├─ SURF_BackPanel_C   the socket
        ///      └─ Marker_HoseOutlet  the hose hangs off this
        /// </code>
        /// </summary>
        private PackHose BuildRig(out PackSurface socket, out Transform outlet)
        {
            root = new GameObject("Rig");
            var pack = root.AddComponent<TestPack>();

            var pivot = new GameObject("PIVOT_Back");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localScale = Vector3.one * CentimetreConvention;

            var surfaceGo = new GameObject("SURF_BackPanel_C");
            surfaceGo.transform.SetParent(pivot.transform, false);

            socket = surfaceGo.AddComponent<PackSurface>();

            var so = new SerializedObject(socket);
            so.FindProperty("id").enumValueIndex = (int)PackSurfaceId.BackPanelCentre;
            so.FindProperty("size").vector2Value = SocketSize;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Off to one side of the face and clear of it, so the hose has a real span to draw and
            // the two ends of the bottle are measurably different distances away.
            var outletGo = new GameObject("Marker_Rig_HoseOutlet");
            outletGo.transform.SetParent(pivot.transform, false);
            outletGo.transform.localPosition = new Vector3(-0.2f, 0f, -0.1f) * CentimetreConvention;

            outlet = outletGo.transform;

            var hose = outletGo.AddComponent<PackHose>();

            var hoseSo = new SerializedObject(hose);
            hoseSo.FindProperty("container").objectReferenceValue = pack;
            hoseSo.FindProperty("outlet").objectReferenceValue = outlet;
            hoseSo.FindProperty("socket").enumValueIndex = (int)PackSurfaceId.BackPanelCentre;
            hoseSo.FindProperty("radius").floatValue = Radius;
            hoseSo.ApplyModifiedPropertiesWithoutUndo();

            return hose;
        }

        /// <summary>
        /// A bottle-sized item: one visible box, no collider, measured like any other. Sized to
        /// three cells across so it fits the socket square on, with no overhang rule in play —
        /// this fixture is about the hose, not about what the socket accepts.
        /// </summary>
        private InventoryItem Bottle()
        {
            itemPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            itemPrefab.name = "BottlePrefab";
            Object.DestroyImmediate(itemPrefab.GetComponent<Collider>());

            var grip = itemPrefab.AddComponent<ItemGrip>();

            var gripSo = new SerializedObject(grip);
            gripSo.FindProperty("packSize").floatValue = 0.25f;
            gripSo.ApplyModifiedPropertiesWithoutUndo();

            bottle = ScriptableObject.CreateInstance<InventoryItem>();
            bottle.name = "OxygenTank";
            bottle.ID = "test-oxygen-tank";
            bottle.itemPrefab = itemPrefab;

            return bottle;
        }

        /// <summary>The hose's live tube, or null when there is none drawn.</summary>
        private static Transform Tube(Transform outlet)
        {
            Transform tube = outlet.Find("Hose");

            return tube != null && tube.gameObject.activeSelf ? tube : null;
        }

        [Test]
        public void TheHoseIsDrawnAtItsOwnThicknessUnderTheRigsCentimetreScale()
        {
            PackHose hose = BuildRig(out PackSurface socket, out Transform outlet);

            PackContainer pack = root.GetComponent<TestPack>();
            Assert.IsTrue(pack.TryPlace(Bottle(), socket.Id, socket.Size * 0.5f, 0f),
                          "the socket has to take the bottle before there is a hose at all");

            hose.Refresh();

            Transform tube = Tube(outlet);
            Assert.IsNotNull(tube, "a bottle is in the socket, so a hose should be drawn");

            float drawn = tube.lossyScale.x;
            float wanted = PackScale.Apply(Radius) * 2f;

            Assert.That(drawn, Is.EqualTo(wanted).Within(Tolerance),
                        $"the hose is drawn {drawn:0.###} m across where it should be " +
                        $"{wanted:0.###} m. The outlet's lossyScale is " +
                        $"{outlet.lossyScale.x:0.#} — the rig's FBX centimetre convention — and a " +
                        "world length written onto a child of it has to be divided by that first.");
        }

        [Test]
        public void TheHoseStillReachesTheBottleItIsPlumbedInto()
        {
            PackHose hose = BuildRig(out PackSurface socket, out Transform outlet);

            PackContainer pack = root.GetComponent<TestPack>();
            Assert.IsTrue(pack.TryPlace(Bottle(), socket.Id, socket.Size * 0.5f, 0f));

            hose.Refresh();

            Transform tube = Tube(outlet);
            Assert.IsNotNull(tube);

            // Unity's cylinder is 2 units tall, so its world length is twice its world Y scale.
            float length = tube.lossyScale.y * 2f;

            // The tube is centred on the span, so its far end is where the hose has to arrive.
            float span = (tube.position - outlet.position).magnitude * 2f;

            Assert.That(length, Is.EqualTo(span).Within(Tolerance),
                        "the tube must span exactly the gap it is centred on — the half of this " +
                        "that was always right, and the half a thickness-only guard would miss.");

            Assert.That(length, Is.GreaterThan(0.05f),
                        "a hose collapsed to nothing is not a hose; the fixture stands the outlet " +
                        "well clear of the face so this cannot pass by accident.");
        }

        [Test]
        public void ThereIsNoHoseWithNothingInTheSocket()
        {
            PackHose hose = BuildRig(out PackSurface _, out Transform outlet);

            hose.Refresh();

            Assert.IsNull(Tube(outlet),
                          "the socket is empty, and a hose running to thin air is the whole " +
                          "reason this is drawn rather than modelled.");
        }

        [Test]
        public void TheHoseCarriesNoCollider()
        {
            PackHose hose = BuildRig(out PackSurface socket, out Transform outlet);

            PackContainer pack = root.GetComponent<TestPack>();
            pack.TryPlace(Bottle(), socket.Id, socket.Size * 0.5f, 0f);

            hose.Refresh();

            Transform tube = Tube(outlet);
            Assert.IsNotNull(tube);

            Assert.IsNull(tube.GetComponent<Collider>(),
                          "a collider here joins the nearest Rigidbody ABOVE it — on a worn pack " +
                          "that is the PLAYER, which is the fault BackpackObject switches its own " +
                          "body collider off to avoid. The hose is scenery.");
        }
    }
}
