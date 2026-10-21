using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// A gear door's puzzle as authored (the gate's GearDoorLayout.json, the vault's VaultDoorLayout.json): it can be
    /// solved, only with every good gear in the pile, and the gate's pile is not in the leaves' way when they swing.
    /// </summary>
    [TestFixture(GearDoorLayout.Path)]
    [TestFixture(GearDoorLayout.VaultPath)]
    public class GearDoorLayoutTests
    {
        private static readonly Dictionary<string, int> Teeth = new()
            { { "XS", 10 }, { "S", 14 }, { "M", 20 }, { "L", 28 }, { "XL", 40 } };

        private readonly string path;
        private GearDoorLayout layout;
        private GearGeometry geometry;

        public GearDoorLayoutTests(string layoutPath) => path = layoutPath;

        [SetUp]
        public void SetUp()
        {
            layout = GearDoorLayout.Load(path);
            geometry = GearDoorBuilder.Geometry(layout);
        }

        private GearSpec Drive => new(GearDoorLayout.V(layout.driver.at), layout.driver.teeth);
        private GearSpec Lock => new(GearDoorLayout.V(layout.output.at), layout.output.teeth);
        private GearSpec OnPeg(int peg, int teeth) => new(GearDoorLayout.V(layout.pegs[peg]), teeth);

        [Test]
        public void TheRecordedSolutionTurnsTheLock()
        {
            var gears = new List<GearSpec> { Drive, Lock };
            gears.AddRange(layout.solution.Select(s => OnPeg(s.peg, Teeth[s.kind])));
            var speeds = new float[gears.Count];

            Assert.IsFalse(GearTrain.Drive(gears, 0, geometry, speeds), "the solution locks up");
            Assert.AreNotEqual(0f, speeds[1], "the solution does not reach the lock");
            for (int i = 0; i < gears.Count; i++)
                for (int j = i + 1; j < gears.Count; j++)
                    Assert.AreNotEqual(GearContact.Hit, GearTrain.Between(gears[i], gears[j], geometry), $"gears {i} and {j} hit");
        }

        /// <summary>No spare gears: the pile is exactly what the gate still needs, so every gear picked up has a place.</summary>
        [Test]
        public void ThePileHoldsExactlyTheSolutionsGearsNotAlreadyOnTheGate()
        {
            var pile = layout.pile.Select(p => p.kind).OrderBy(k => k).ToList();
            var needed = layout.solution.Where(s => !layout.preinstalled.Any(p => p.peg == s.peg))
                                        .Select(s => s.kind).OrderBy(k => k).ToList();
            CollectionAssert.AreEqual(needed, pile);
        }

        /// <summary>
        /// A gear already on the gate is a piece of the way to the lock, on its own peg, so it is a hint. The gate has
        /// some; the vault's door starts bare, on purpose: it is the harder one.
        /// </summary>
        [Test]
        public void TheGearsAlreadyOnTheGateAreOnTheirSolutionPegs()
        {
            if (path == GearDoorLayout.Path) Assert.IsNotEmpty(layout.preinstalled);
            foreach (GearDoorLayout.Placed standing in layout.preinstalled)
                Assert.IsTrue(layout.solution.Any(s => s.peg == standing.peg && s.kind == standing.kind),
                              $"peg {standing.peg} starts with {standing.kind}, which the solution does not put there");
        }

        /// <summary>
        /// Every way of carrying the drive to the lock with the pile's good gears, searched exhaustively, uses all of them.
        /// A shorter way would open the gate with gears left over, which is not the puzzle.
        /// </summary>
        [Test]
        public void EveryWayToTheLockNeedsEveryGoodGear()
        {
            var counts = new Dictionary<string, int>();
            foreach (GearDoorLayout.Placed s in layout.solution) counts[s.kind] = counts.TryGetValue(s.kind, out int n) ? n + 1 : 1;

            var lengths = new List<int>();
            Search(Drive, new List<(int peg, GearSpec gear)>(), counts, lengths);

            Assert.IsNotEmpty(lengths, "no way to the lock at all");
            Assert.That(lengths, Is.All.EqualTo(layout.solution.Length), "a way to the lock leaves gears over");
        }

        private void Search(GearSpec at, List<(int peg, GearSpec gear)> path, Dictionary<string, int> counts, List<int> lengths)
        {
            if (path.Count > 0 && GearTrain.Between(at, Lock, geometry) == GearContact.Mesh)
            {
                lengths.Add(path.Count);
                return;
            }

            for (int peg = 0; peg < layout.pegs.Length; peg++)
            {
                if (path.Any(p => p.peg == peg)) continue;
                foreach (string kind in counts.Keys.ToList())
                {
                    if (counts[kind] == 0) continue;
                    GearSpec next = OnPeg(peg, Teeth[kind]);
                    if (GearTrain.Between(at, next, geometry) != GearContact.Mesh) continue;
                    if (GearTrain.Between(next, Lock, geometry) == GearContact.Hit) continue;
                    if (path.Count > 0 && GearTrain.Between(next, Drive, geometry) == GearContact.Hit) continue;
                    if (path.Take(path.Count - 1).Any(p => GearTrain.Between(next, p.gear, geometry) == GearContact.Hit)) continue;

                    counts[kind]--;
                    path.Add((peg, next));
                    Search(next, path, counts, lengths);
                    path.RemoveAt(path.Count - 1);
                    counts[kind]++;
                }
            }
        }

        /// <summary>The leaves swing out over the ground; a gear lying where one sweeps would be cut through.</summary>
        [Test]
        public void ThePileLiesOutOfBothLeavesSweep()
        {
            if (path != GearDoorLayout.Path) Assert.Ignore("the vault's leaf swings in, away from its pile");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorkshopPrefabBuilder.PrefabPath);
            GearDoor door = GearDoor.GateOf(prefab);
            Assert.IsNotNull(door, "Workshop.prefab has no gear door: rebuild it");

            // The door's own leaves, not a name match: the LOD pass hangs a Gate_Door_*_LOD1 under each.
            List<Transform> leaves = Leaves(door);
            Assert.AreEqual(2, leaves.Count);

            Transform pile = door.transform.Find("Pile");
            Assert.AreEqual(layout.pile.Length, pile.childCount);
            for (int i = 0; i < pile.childCount; i++)
            {
                Transform spot = pile.GetChild(i);
                int teeth = Teeth[layout.pile[i].kind];
                foreach (Transform leaf in leaves)
                {
                    float reach = LeafReach(leaf);
                    float distance = Flat(spot.position - leaf.position).magnitude;
                    Assert.Greater(distance, reach + geometry.OuterRadius(teeth), $"{spot.name} lies in {leaf.name}'s sweep");
                }
            }
        }

        private static List<Transform> Leaves(GearDoor door)
        {
            SerializedProperty leaves = new SerializedObject(door).FindProperty("leaves");
            var nodes = new List<Transform>();
            for (int i = 0; i < leaves.arraySize; i++)
                nodes.Add((Transform)leaves.GetArrayElementAtIndex(i).FindPropertyRelative("node").objectReferenceValue);
            return nodes;
        }

        private static float LeafReach(Transform leaf)
        {
            // From the meshes, not Renderer.bounds: a prefab asset is in no scene and its renderers have no bounds.
            float reach = 0f;
            foreach (MeshFilter filter in leaf.GetComponentsInChildren<MeshFilter>(true))
            {
                Bounds b = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var local = new Vector3((corner & 1) == 0 ? b.min.x : b.max.x, (corner & 2) == 0 ? b.min.y : b.max.y,
                                            (corner & 4) == 0 ? b.min.z : b.max.z);
                    reach = Mathf.Max(reach, Flat(filter.transform.TransformPoint(local) - leaf.position).magnitude);
                }
            }
            return reach;
        }

        private static Vector2 Flat(Vector3 v) => new(v.x, v.z);
    }
}
