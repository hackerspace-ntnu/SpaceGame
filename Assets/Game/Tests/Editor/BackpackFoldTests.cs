using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The fold: what the rig looks like once it has gone back on somebody's back, and what it can
    /// still carry there.
    ///
    /// <para>
    /// Three things are being pinned. First, the front closes like a BOX: the side panels' pivots
    /// are children of <c>PIVOT_Leaf</c>, so closing folds each panel ±90 up off the board and the
    /// rising flap carries them round to hug the pack's flanks — nothing on the front can be left
    /// behind on the sand (or,
    /// for one memorable morning, buried under it). Second, a pack stowed from the RACK and a
    /// pack stowed flat are the same pack: rack pose and stow pose are the same place for every
    /// part of the flap, so the handover costs zero motion. Third, gear on the one face the fold
    /// leaves pointing outward stays on it through a whole deploy/stow/deploy cycle, because that
    /// face is the only place on a worn pack anything can be.
    /// </para>
    /// <para>
    /// The rigs here are DEACTIVATED on purpose. <c>SetOpen</c>, <c>SetRacked</c> and
    /// <c>SettleRack</c> all answer an object that is not running with the settled pose instead of
    /// the animation — a coroutine cannot be started on one — so an inactive rig is how an EditMode
    /// test drives the beat sheet to either of its ends without a play mode frame.
    /// </para>
    /// </summary>
    public class BackpackFoldTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<InventoryItem> created = new();
        private readonly List<GameObject> spawned = new();

        [SetUp]
        public void ClearMeasurementCache() => ItemFootprint.ClearCache();

        [TearDown]
        public void CleanUp()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);

            foreach (InventoryItem item in created)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);

            spawned.Clear();
            created.Clear();
        }

        /// <summary>
        /// An item with an id, because the layout is keyed by id. With no prefab it measures the
        /// 0.1 m square <see cref="ItemFootprint"/> gives anything it cannot measure.
        /// </summary>
        private InventoryItem Item(string name)
        {
            var item = ScriptableObject.CreateInstance<InventoryItem>();
            item.itemName = name;
            item.ID = name;
            created.Add(item);
            return item;
        }

        /// <summary>The moving parts of one test rig, so a test can read their poses.</summary>
        private sealed class Rig
        {
            public BackpackObject Pack;
            public Transform[] Pivots;
        }

        /// <summary>
        /// A rig wired like <c>expedition_rig</c>: four hinges with its fold angles and axes,
        /// <c>restIsOpen</c> on all of them, the wing pivots as CHILDREN of the leaf pivot the
        /// way the reparented model ships them, and two faces — the leaf, which is inside the
        /// fold, and the rack, which is not.
        ///
        /// <para>
        /// Every pivot is given a rest rotation that is NOT identity, which is the trap the whole
        /// hinge design exists for: an FBX hands its empties back at their authored rotation, so a
        /// fold applied as an absolute <c>Quaternion.Euler</c> reorients the part instead of turning
        /// it about its hinge. A test on identity pivots would pass either way.
        /// </para>
        /// <para>
        /// <c>SURF_Leaf</c> comes first in the hierarchy deliberately: it is what first-fit would
        /// reach for if the exterior rule were not working.
        /// </para>
        /// </summary>
        private Rig BuildRig()
        {
            var root = new GameObject("TestRig");
            spawned.Add(root);

            Transform panel = Pivot(root, "PIVOT_Back", new Vector3(270.02f, 0f, 0f));
            Transform leaf = Pivot(root, "PIVOT_Leaf", new Vector3(12f, 34f, 56f));

            // Children of the leaf, exactly as the model parents them: their ±90 fold is
            // relative to the board, and whatever the leaf's hinge does they ride.
            Transform wingL = Pivot(leaf.gameObject, "PIVOT_Wing_L", new Vector3(0f, 17f, 0f));
            Transform wingR = Pivot(leaf.gameObject, "PIVOT_Wing_R", new Vector3(0f, -17f, 0f));

            Surface(root, PackSurfaceId.Leaf, new Vector2(0.78f, 0.50f));
            Surface(root, PackSurfaceId.Rack, new Vector2(0.80f, 0.60f));

            // Inactive before the component goes on, so nothing anywhere near this can try to start
            // a coroutine. AddComponent outside play mode raises no Awake either way.
            root.SetActive(false);

            var pack = root.AddComponent<BackpackObject>();

            var hinges = new[]
            {
                Hinge(panel, BackpackHingePart.Panel,     Vector3.right,   25f),
                Hinge(leaf,  BackpackHingePart.Leaf,      Vector3.right,  -90f),
                Hinge(wingL, BackpackHingePart.WingLeft,  Vector3.up,     -90f),
                Hinge(wingR, BackpackHingePart.WingRight, Vector3.up,      90f),
            };

            typeof(BackpackObject).GetField("hinges", Hidden).SetValue(pack, hinges);

            return new Rig { Pack = pack, Pivots = new[] { panel, leaf, wingL, wingR } };
        }

        /// <summary>The indices into <see cref="Rig.Pivots"/>, in <c>HingeTable</c> order.</summary>
        private const int Panel = 0, Leaf = 1, WingL = 2, WingR = 3;

        /// <summary>The fold angles those pivots are wired with, in the same order.</summary>
        private static readonly float[] Folds = { 25f, -90f, -90f, 90f };

        /// <summary>The hinge axes, in the same order.</summary>
        private static readonly Vector3[] Axes = { Vector3.right, Vector3.right, Vector3.up, Vector3.up };

        /// <summary>
        /// The signed degrees a pivot has turned about its own hinge axis since <paramref name="rest"/>.
        ///
        /// <para>
        /// Read as a turn rather than compared against an absolute <c>Quaternion.Euler</c>, because
        /// that is the invariant the whole hinge design exists to protect: the pivots here are
        /// authored at rotations that are not identity, exactly as an FBX hands them back, so the
        /// only meaningful question about one is how far it has moved from where it started.
        /// </para>
        /// </summary>
        private static float TurnAbout(Transform pivot, Quaternion rest, Vector3 axis)
        {
            (Quaternion.Inverse(rest) * pivot.localRotation).ToAngleAxis(out float angle,
                                                                        out Vector3 about);

            // ToAngleAxis always reports a positive angle and picks the axis to suit, so which of
            // the two antiparallel answers it gave is what carries the sign.
            return Vector3.Dot(about.normalized, axis.normalized) < 0f ? -angle : angle;
        }

        private static Transform Pivot(GameObject root, string name, Vector3 restEuler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localRotation = Quaternion.Euler(restEuler);
            return go.transform;
        }

        /// <summary><see cref="PackSurface"/> authors its id and size privately, so a test writes them.</summary>
        private static void Surface(GameObject root, PackSurfaceId id, Vector2 size)
        {
            var go = new GameObject("SURF_" + id);
            go.transform.SetParent(root.transform, false);

            var surface = go.AddComponent<PackSurface>();
            typeof(PackSurface).GetField("id", Hidden).SetValue(surface, id);
            typeof(PackSurface).GetField("size", Hidden).SetValue(surface, size);
        }

        private static BackpackHinge Hinge(Transform pivot, BackpackHingePart part,
                                           Vector3 axis, float fold) =>
            new()
            {
                pivot = pivot,
                part = part,
                localAxis = axis,
                foldAngle = fold,
                restIsOpen = true,
            };

        private static Quaternion[] Pose(Rig rig)
        {
            var pose = new Quaternion[rig.Pivots.Length];

            for (int i = 0; i < rig.Pivots.Length; i++) pose[i] = rig.Pivots[i].localRotation;

            return pose;
        }

        private static bool TryPlacementOf(BackpackObject pack, InventoryItem item,
                                           out PackPlacement found)
        {
            foreach (PackPlacement placement in pack.Layout.Placements)
            {
                if (placement.ItemId != item.ID) continue;

                found = placement;
                return true;
            }

            found = default;
            return false;
        }

        // ---------------------------------------------------------------- the fold

        /// <summary>
        /// Closing is a box being closed: the side panels fold square UP off the board — ±90
        /// about hinges that are CHILDREN of the leaf — and the rising flap carries them round
        /// to hug the pack's flanks. Pinned as: racked, each panel stands at exactly its stow
        /// fold relative to the board, and the fold reverses when the rack comes down. Exactly the stow angle and no other
        /// number, because that is what lets a pack stowed from the rack cost zero motion — the
        /// property the test below this one pins.
        /// </summary>
        [Test]
        public void RackingFoldsTheSidePanelsUpAndRaisesTheBoard()
        {
            Rig rig = BuildRig();

            Quaternion[] rest = Pose(rig);

            rig.Pack.SetWorn(false);
            rig.Pack.SetOpen(true);

            Assert.Less(Quaternion.Angle(rest[WingL], rig.Pivots[WingL].localRotation), 0.01f,
                        "a deployed pack has its side panels out flat — the sheet is at zero " +
                        "for them");
            Assert.Less(Quaternion.Angle(rest[WingR], rig.Pivots[WingR].localRotation), 0.01f);

            rig.Pack.SetRacked(true);

            Assert.AreEqual(Folds[Leaf], TurnAbout(rig.Pivots[Leaf], rest[Leaf], Axes[Leaf]), 0.01f,
                            "the board itself goes up, which is what the rack always did");

            // Compared as poses rather than through TurnAbout, so the assertion survives the
            // fold angle being retuned to 180 (where angle-axis signs become noise).
            Assert.Less(Quaternion.Angle(rest[WingL] * Quaternion.AngleAxis(Folds[WingL], Axes[WingL]),
                                         rig.Pivots[WingL].localRotation), 0.01f,
                        "the left panel has to fold up with it, to its stow angle exactly");
            Assert.Less(Quaternion.Angle(rest[WingR] * Quaternion.AngleAxis(Folds[WingR], Axes[WingR]),
                                         rig.Pivots[WingR].localRotation), 0.01f,
                        "and the right panel mirrored — the two fold inward toward each other");

            // The panel is not the rack's business. It is what tells the deployed pack apart from
            // the stowed one once the flap has stopped moving in the same places.
            Assert.Less(Mathf.Abs(TurnAbout(rig.Pivots[Panel], rest[Panel], Axes[Panel])), 0.01f,
                        "the kickstand panel stays where the deploy left it — the rack moves the " +
                        "front flap and nothing else");

            // Racked, the panels have flipped face-down and ridden round with the mat, so
            // first-fit may not offer them any more than it may offer the mat.
            Assert.IsFalse(rig.Pack.Reaches(PackSurfaceId.WingLeft),
                           "a racked side panel is wrapped against the pack's flank, out of reach");
            Assert.IsFalse(rig.Pack.Reaches(PackSurfaceId.Leaf));

            rig.Pack.SetRacked(false);

            Assert.Less(Quaternion.Angle(rest[WingL], rig.Pivots[WingL].localRotation), 0.01f,
                        "and the panels fold back out with the board, or the rack is a one-way trip");
            Assert.Less(Quaternion.Angle(rest[WingR], rig.Pivots[WingR].localRotation), 0.01f);
        }

        [Test]
        public void StowingFromTheRackLandsTheRigInTheSamePoseAsStowingFlat()
        {
            Rig flat = BuildRig();
            flat.Pack.SetWorn(false);
            flat.Pack.SetOpen(true);

            Quaternion[] deployed = Pose(flat);
            Quaternion wingWorldDeployed = flat.Pivots[WingL].rotation;

            flat.Pack.SnapStowed();
            Quaternion[] fromFlat = Pose(flat);

            Rig racked = BuildRig();
            racked.Pack.SetWorn(false);
            racked.Pack.SetOpen(true);
            racked.Pack.SetRacked(true);

            // Before the fold, not after. The rack pose and the stow pose are the same place for
            // the whole flap, so the handover ResolveRackForStow performs is free. Measuring here
            // is what distinguishes "ends in the same pose" from "ends in the same pose after
            // dropping the flap flat and picking it straight back up", which looks identical
            // once the rig has settled and is not the same fold at all.
            Quaternion[] beforeFold = Pose(racked);

            racked.Pack.SnapStowed();

            Quaternion[] fromRack = Pose(racked);

            foreach (int i in new[] { Leaf, WingL, WingR })
                Assert.Less(Quaternion.Angle(beforeFold[i], fromRack[i]), 0.01f,
                            $"pivot {racked.Pivots[i].name} moved while a racked pack was stowed — " +
                            "racked and stowed are supposed to be the same place for it, so the " +
                            "fold should have cost it exactly zero degrees");

            for (int i = 0; i < fromFlat.Length; i++)
                Assert.Less(Quaternion.Angle(fromFlat[i], fromRack[i]), 0.01f,
                            $"pivot {racked.Pivots[i].name} came to rest somewhere else because " +
                            "the pack happened to be racked when it was stowed");

            Assert.IsFalse(racked.Pack.IsRacked,
                           "the rack has to be given up as part of stowing, not left set on a " +
                           "pack that is folded on somebody's back");

            // The pose actually moved. Without this the test above would pass just as happily on a
            // rig whose hinges never turn at all.
            Assert.Greater(Quaternion.Angle(deployed[Leaf], fromFlat[Leaf]), 45f,
                           "the leaf must actually fold — a rig that never moves proves nothing");
            Assert.Greater(Quaternion.Angle(deployed[WingL], fromFlat[WingL]), 45f,
                           "and the side panels must actually fold up, which is the 'closing " +
                           "the box' half of it");
            Assert.Greater(Quaternion.Angle(wingWorldDeployed, flat.Pivots[WingL].rotation), 45f,
                           "and be carried up with the board in the world as well");
        }

        /// <summary>
        /// A worn pack is not seven faces, it is a folded sandwich with six of them inside it. What
        /// a world pickup overflows onto has to be the face the fold leaves pointing out, or the
        /// gear goes where nobody can see it and where the leaf closes through it.
        /// </summary>
        [Test]
        public void AWornPackOffersItsExteriorFaceAndNothingElse()
        {
            Rig rig = BuildRig();

            Assert.IsTrue(rig.Pack.IsWorn, "a pack starts on somebody's back");

            Assert.IsTrue(rig.Pack.Reaches(PackSurfaceId.Rack),
                          "the leaf's underside is the outside of the folded pack");
            Assert.IsFalse(rig.Pack.Reaches(PackSurfaceId.Leaf),
                           "the mat has turned to face the back panel — it is inside the fold");
            Assert.IsFalse(rig.Pack.Reaches(PackSurfaceId.BackPanelLeft),
                           "and the back panels are underneath the leaf");

            InventoryItem salvage = Item("salvage");

            Assert.IsTrue(rig.Pack.TryStow(salvage),
                          "a world pickup must still overflow onto a worn pack");

            Assert.IsTrue(TryPlacementOf(rig.Pack, salvage, out PackPlacement where));
            Assert.AreEqual(PackSurfaceId.Rack, where.Surface,
                            "and it must land on the outside of it, where SURF_Leaf is listed " +
                            "first and would otherwise have taken it");
        }

        /// <summary>
        /// The round trip. Gear lashed to the outside is the thing anybody looking at a worn pack
        /// actually sees, so it may not be moved, dropped or rearranged by the pack folding up and
        /// opening again.
        /// </summary>
        [Test]
        public void GearOnTheExteriorFaceSurvivesADeployStowDeployRoundTrip()
        {
            Rig rig = BuildRig();

            rig.Pack.SetWorn(false);
            rig.Pack.SetOpen(true);
            rig.Pack.SetRacked(true);

            InventoryItem lamp = Item("lamp");
            var spot = new Vector2(0.40f, 0.30f);

            // TryPlace is deliberately NOT gated on Reaches (BackpackObject.cs:1330), so it would
            // place onto the Rack face here whether or not the leaf being racked actually made it
            // reachable. Asserted separately so this test still proves the thing its message
            // claims rather than only proving TryPlace can write to an id.
            Assert.IsTrue(rig.Pack.Reaches(PackSurfaceId.Rack),
                          "the exterior face is reachable while the leaf is racked, which is the " +
                          "only configuration it points at the focus camera in");

            Assert.IsTrue(rig.Pack.TryPlace(lamp, PackSurfaceId.Rack, spot, 0f));

            rig.Pack.SnapStowed();
            rig.Pack.SetWorn(true);

            Assert.IsTrue(rig.Pack.TryFindAt(PackSurfaceId.Rack, spot, out PackPlacement worn),
                          "it must still be on the pack once the pack is on a back");
            Assert.AreEqual(lamp.ID, worn.ItemId);
            Assert.AreEqual(spot, worn.Uv, "and at the same spot, not first-fitted somewhere else");

            rig.Pack.SetWorn(false);
            rig.Pack.SetOpen(true);

            Assert.IsTrue(rig.Pack.TryFindAt(PackSurfaceId.Rack, spot, out PackPlacement back),
                          "and it must come back out of the fold where it went in");
            Assert.AreEqual(lamp.ID, back.ItemId);
            Assert.AreEqual(spot, back.Uv);
        }
    }
}
