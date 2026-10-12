// The simulation-distance maths from the 2026-10-06 spec, without a scene.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class SimulationRulesTests
    {
        private static readonly SimulationCells Cells = new(Vector3.zero, 125f, 250f, 360f);

        [Test]
        public void CellOf_FloorsAndNeverClamps()
        {
            Assert.AreEqual(new Vector2Int(0, 0), SimulationRules.CellOf(new Vector3(10f, 50f, 124.9f), Vector3.zero, 125f));
            Assert.AreEqual(new Vector2Int(-1, -1), SimulationRules.CellOf(new Vector3(-0.1f, 0f, -0.1f), Vector3.zero, 125f));
            Assert.AreEqual(new Vector2Int(132, 0), SimulationRules.CellOf(new Vector3(16500f, 0f, 0f), Vector3.zero, 125f),
                "the arena 16.5 km east is its own cell, not the world's corner");
        }

        [Test]
        public void CellOf_HonoursTheOrigin()
        {
            Assert.AreEqual(new Vector2Int(0, 0), SimulationRules.CellOf(new Vector3(0f, 0f, -1000f), new Vector3(0f, 0f, -1000f), 125f));
        }

        [Test]
        public void DistanceToCell_IsZeroInsideAndHorizontal()
        {
            Assert.AreEqual(0f, SimulationRules.DistanceToCell(Vector2Int.zero, new Vector3(60f, 900f, 60f), Vector3.zero, 125f));
            Assert.AreEqual(75f, SimulationRules.DistanceToCell(Vector2Int.zero, new Vector3(200f, 0f, 60f), Vector3.zero, 125f), 1e-4f);
        }

        [Test]
        public void CornerDistanceIsDiagonal()
        {
            Assert.AreEqual(5f, SimulationRules.DistanceToCell(Vector2Int.zero, new Vector3(128f, 0f, 129f), Vector3.zero, 125f), 1e-4f);
        }

        [Test]
        public void CellsWithinWakeRadiusWake_AndFartherOnesDoNot()
        {
            var awake = new HashSet<Vector2Int>();
            SimulationRules.Advance(awake, new[] { new Vector3(60f, 0f, 60f) }, Cells, new List<Vector2Int>());

            Assert.IsTrue(awake.Contains(new Vector2Int(0, 0)));
            Assert.IsTrue(awake.Contains(new Vector2Int(2, 0)), "nearest point 190 m away");
            Assert.IsFalse(awake.Contains(new Vector2Int(3, 0)), "nearest point 315 m away");
        }

        [Test]
        public void AnAwakeCellStaysAwakeUntilBeyondSleepRadius()
        {
            var awake = new HashSet<Vector2Int>();
            var scratch = new List<Vector2Int>();
            SimulationRules.Advance(awake, new[] { new Vector3(60f, 0f, 60f) }, Cells, scratch);
            Assert.IsTrue(awake.Contains(new Vector2Int(2, 0)));

            // Walk 100 m west: cell (2,0)'s nearest point is now 290 m away — between wake and sleep.
            SimulationRules.Advance(awake, new[] { new Vector3(-40f, 0f, 60f) }, Cells, scratch);
            Assert.IsTrue(awake.Contains(new Vector2Int(2, 0)), "hysteresis keeps it awake");

            // Another 100 m: 390 m — beyond sleep.
            SimulationRules.Advance(awake, new[] { new Vector3(-140f, 0f, 60f) }, Cells, scratch);
            Assert.IsFalse(awake.Contains(new Vector2Int(2, 0)));
        }

        [Test]
        public void TwoSourcesWakeTheUnion()
        {
            var awake = new HashSet<Vector2Int>();
            SimulationRules.Advance(awake, new[] { new Vector3(60f, 0f, 60f), new Vector3(2060f, 0f, 60f) }, Cells, new List<Vector2Int>());

            Assert.IsTrue(awake.Contains(new Vector2Int(0, 0)));
            Assert.IsTrue(awake.Contains(new Vector2Int(16, 0)));
            Assert.IsFalse(awake.Contains(new Vector2Int(8, 0)));
        }

        [Test]
        public void NoSourcesPutsEveryCellToSleep()
        {
            var awake = new HashSet<Vector2Int> { Vector2Int.zero, Vector2Int.one };
            SimulationRules.Advance(awake, new Vector3[0], Cells, new List<Vector2Int>());
            Assert.AreEqual(0, awake.Count);
        }

        [Test]
        public void SleepsOnlyInASleepingCellWithNoExemption()
        {
            Assert.IsTrue(SimulationRules.ShouldSleep(Inputs(), 30f));
            Assert.IsFalse(SimulationRules.ShouldSleep(Inputs(cellAwake: true), 30f));
            Assert.IsFalse(SimulationRules.ShouldSleep(Inputs(inGroup: true), 30f));
            Assert.IsFalse(SimulationRules.ShouldSleep(Inputs(seatedAloft: true), 30f));
            Assert.IsFalse(SimulationRules.ShouldSleep(Inputs(flying: true), 30f));
            Assert.IsFalse(SimulationRules.ShouldSleep(Inputs(huntsPlayer: true), 30f));
            Assert.IsFalse(SimulationRules.ShouldSleep(Inputs(sinceHurt: 29f), 30f));
            Assert.IsTrue(SimulationRules.ShouldSleep(Inputs(sinceHurt: 30f), 30f));
        }

        private static DormancyInputs Inputs(bool cellAwake = false, bool inGroup = false, bool seatedAloft = false,
            bool flying = false, bool huntsPlayer = false, float sinceHurt = float.PositiveInfinity) =>
            new(cellAwake, inGroup, seatedAloft, flying, huntsPlayer, sinceHurt);
    }
}
