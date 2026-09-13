using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The one readout in focus mode that is not about where the cursor is pointing: a face
    /// RESERVED for what is in the player's hand lights its free cells, wherever on the rig that
    /// face happens to be.
    ///
    /// <para>
    /// It exists because the ordinary readout cannot reach the problem. Cells go green or red on
    /// the face the cursor is already on, which only helps somebody who has guessed the right face
    /// — and the rig's one socket, the centre back panel, looks exactly like the two ordinary back
    /// panels either side of it. A player holding an oxygen bottle has no way to find it. So the
    /// invitation goes out on its own.
    /// </para>
    /// <para>
    /// <b>Which face qualifies is <see cref="PackContainer.SocketFor"/>'s judgement</b>, and that
    /// is where most of this fixture points: the rules that keep it from promising a landing the
    /// drop would refuse. <see cref="PackGridVisual"/> then draws whatever it is handed, so only
    /// the drawing's own two facts — it appears, and it goes away — are checked below.
    /// </para>
    /// </summary>
    public class PackSocketInviteTests
    {
        /// <summary><see cref="PackSurfaceId.BackPanelCentre"/>: 3 x 6 cells.</summary>
        private static readonly Vector2 SocketSize = new(3f * PackGrid.Cell, 6f * PackGrid.Cell);

        /// <summary>An ordinary face, big enough that anything in this fixture fits it.</summary>
        private static readonly Vector2 LeafSize = new(8f * PackGrid.Cell, 8f * PackGrid.Cell);

        /// <summary>Ring (8) plus fill (4) — what <c>AddCell(fill: true)</c> emits per cell.</summary>
        private const int VertsPerCell = 12;

        /// <summary>A container that can be told which of its faces are out of reach.</summary>
        private sealed class TestPack : PackContainer
        {
            public PackSurfaceId? Unreachable;

            public override bool Reaches(PackSurfaceId id) => Unreachable != id;

            public override void RequestTake(PackSurfaceId surface, Vector2 uv, Interactor interactor) { }

            public override void RequestStow(int slotIndex, PackSurfaceId surfaceId, Vector2 uv,
                                             float yaw, Interactor interactor) { }
        }

        private GameObject root;
        private GameObject bottlePrefab;
        private GameObject toolPrefab;
        private InventoryItem bottle;
        private InventoryItem tool;
        private PackGridVisual grid;

        private TestPack pack;
        private PackSurface socket;
        private PackSurface leaf;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Rig");
            pack = root.AddComponent<TestPack>();

            bottle = Item("test-oxygen-tank", out bottlePrefab);
            tool = Item("test-spanner", out toolPrefab);

            socket = Face(PackSurfaceId.BackPanelCentre, SocketSize, reservedFor: bottle);
            leaf = Face(PackSurfaceId.Leaf, LeafSize, reservedFor: null);
        }

        [TearDown]
        public void TearDown()
        {
            if (grid != null) grid.Dispose();
            grid = null;

            if (root != null) Object.DestroyImmediate(root);
            if (bottlePrefab != null) Object.DestroyImmediate(bottlePrefab);
            if (toolPrefab != null) Object.DestroyImmediate(toolPrefab);
            if (bottle != null) Object.DestroyImmediate(bottle);
            if (tool != null) Object.DestroyImmediate(tool);

            root = null;
            bottlePrefab = null;
            toolPrefab = null;
            bottle = null;
            tool = null;

            ItemFootprint.ClearCache();
        }

        /// <summary>One face on the rig, optionally reserved for exactly one item.</summary>
        private PackSurface Face(PackSurfaceId id, Vector2 size, InventoryItem reservedFor)
        {
            var go = new GameObject("SURF_" + id);
            go.transform.SetParent(root.transform, false);

            var surface = go.AddComponent<PackSurface>();

            var so = new SerializedObject(surface);
            so.FindProperty("id").enumValueIndex = (int)id;
            so.FindProperty("size").vector2Value = size;

            SerializedProperty accepts = so.FindProperty("acceptsOnly");
            accepts.arraySize = reservedFor != null ? 1 : 0;

            if (reservedFor != null)
                accepts.GetArrayElementAtIndex(0).objectReferenceValue = reservedFor;

            so.ApplyModifiedPropertiesWithoutUndo();

            return surface;
        }

        /// <summary>An item three cells square, so it fits the socket square on.</summary>
        private static InventoryItem Item(string id, out GameObject prefab)
        {
            prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = id + "-prefab";
            Object.DestroyImmediate(prefab.GetComponent<Collider>());

            var grip = prefab.AddComponent<ItemGrip>();

            var gripSo = new SerializedObject(grip);
            gripSo.FindProperty("packSize").floatValue = 0.25f;
            gripSo.ApplyModifiedPropertiesWithoutUndo();

            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.name = id;
            item.ID = id;
            item.itemPrefab = prefab;

            return item;
        }

        /// <summary>The invitation's live mesh on a face, or null when nothing is drawn there.</summary>
        private static Mesh DrawnOn(PackSurface surface)
        {
            Transform drawn = surface.transform.Find("PackSocketInvite");

            if (drawn == null || !drawn.gameObject.activeSelf) return null;

            return drawn.GetComponent<MeshFilter>().sharedMesh;
        }

        // -- Which face is offered --------------------------------------------

        [Test]
        public void TheReservedFaceIsOfferedForTheItemItIsReservedFor()
        {
            Assert.AreSame(socket, pack.SocketFor(bottle),
                           "a bottle in hand has to name the bottle's socket");
        }

        [Test]
        public void AnOrdinaryFaceIsNeverOffered()
        {
            Assert.IsNull(pack.SocketFor(tool),
                          "the spanner fits the leaf perfectly well, and the leaf takes everything " +
                          "— a face that would light up for every item in the game says nothing.");
        }

        [Test]
        public void AFullSocketIsNotOffered()
        {
            Assert.IsTrue(pack.TryPlace(bottle, socket.Id, socket.Size * 0.5f, 0f));

            Assert.IsNull(pack.SocketFor(bottle),
                          "the socket already holds a bottle, so lighting it up would send the " +
                          "player to a face that answers red the moment they aim at it.");
        }

        [Test]
        public void TheSocketAnItemWasLiftedOffIsStillOffered()
        {
            Assert.IsTrue(pack.TryPlace(bottle, socket.Id, socket.Size * 0.5f, 0f));

            // A lift is local: the bottle is in the player's hand and still on the layout. Without
            // the exclusion the socket reads full and goes dark exactly when it is picked up.
            Assert.AreSame(socket, pack.SocketFor(bottle, bottle.ID));
        }

        [Test]
        public void AnUnreachableSocketIsNotOffered()
        {
            pack.Unreachable = PackSurfaceId.BackPanelCentre;

            Assert.IsNull(pack.SocketFor(bottle),
                          "a face against the sand cannot be dropped onto, so it must not be " +
                          "advertised either.");
        }

        [Test]
        public void NothingIsOfferedForAnEmptyHand()
        {
            Assert.IsNull(pack.SocketFor(null));
        }

        // -- What is drawn -----------------------------------------------------

        [Test]
        public void TheInvitationCoversEveryFreeCellOfTheFace()
        {
            grid = new PackGridVisual();
            grid.ShowSocket(socket, pack.Layout, null);

            Mesh mesh = DrawnOn(socket);
            Assert.IsNotNull(mesh, "an empty socket should light every one of its cells");

            Vector2Int cells = PackGrid.CellsOn(socket.Size);

            Assert.AreEqual(cells.x * cells.y * VertsPerCell, mesh.vertexCount,
                            "one outlined, filled cell per free cell of the face");
        }

        [Test]
        public void TakenCellsAreLeftDark()
        {
            Assert.IsTrue(pack.TryPlace(bottle, socket.Id, socket.Size * 0.5f, 0f));

            grid = new PackGridVisual();
            grid.ShowSocket(socket, pack.Layout, null);

            Mesh mesh = DrawnOn(socket);
            Vector2Int cells = PackGrid.CellsOn(socket.Size);

            int lit = mesh == null ? 0 : mesh.vertexCount / VertsPerCell;

            Assert.Less(lit, cells.x * cells.y,
                        "the invitation is a reading of what is FREE — a cell under placed gear " +
                        "is not somewhere the thing in hand can go.");
        }

        [Test]
        public void HidingTheInvitationTakesItOffTheFace()
        {
            grid = new PackGridVisual();
            grid.ShowSocket(socket, pack.Layout, null);
            Assert.IsNotNull(DrawnOn(socket));

            grid.HideSocket();

            Assert.IsNull(DrawnOn(socket),
                          "the hand emptied, so the invitation has to go with it");
        }

        [Test]
        public void ALayoutChangeRebuildsTheInvitationEvenAfterTheLatticeHasRun()
        {
            grid = new PackGridVisual();

            grid.ShowSocket(socket, pack.Layout, null);
            int before = DrawnOn(socket).vertexCount;

            Assert.IsTrue(pack.TryPlace(bottle, socket.Id, socket.Size * 0.5f, 0f));
            grid.MarkDirty();

            // The lattice runs FIRST, on a different face, exactly as it does in a real frame
            // where the cursor is over the leaf. Sharing one stale flag between the two passes
            // would let this call clear it and leave the invitation frozen on stale geometry.
            grid.ShowLattice(leaf, pack.Layout, null);
            grid.ShowSocket(socket, pack.Layout, null);

            Mesh mesh = DrawnOn(socket);
            int after = mesh == null ? 0 : mesh.vertexCount;

            Assert.Less(after, before,
                        "the socket filled up, so fewer of its cells are free — an unchanged " +
                        "count means the invitation early-outed on a flag the lattice had " +
                        "already cleared.");
        }
    }
}
