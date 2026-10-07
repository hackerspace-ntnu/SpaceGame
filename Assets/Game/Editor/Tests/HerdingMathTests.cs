// Where a herder rides relative to the herd it drives. The drive reads as a drive only if the
// herders hold the drag and the flanks and the nearest one goes after a stray — so assert the
// geometry rather than watch three Appas and form an opinion.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class HerdingMathTests
    {
        [Test]
        public void OneHerder_RidesTheDrag_StraightBehind()
        {
            Assert.AreEqual(0f, HerdingMath.StationAngle(0, 1, 150f), 0.001f);
        }

        [Test]
        public void TwoHerders_TakeOppositeFlanks_AtTheEndsOfTheArc()
        {
            Assert.AreEqual(-75f, HerdingMath.StationAngle(0, 2, 150f), 0.001f);
            Assert.AreEqual(75f, HerdingMath.StationAngle(1, 2, 150f), 0.001f);
        }

        [Test]
        public void ThreeHerders_TwoFlanksAndTheDrag()
        {
            Assert.AreEqual(-60f, HerdingMath.StationAngle(0, 3, 120f), 0.001f);
            Assert.AreEqual(0f, HerdingMath.StationAngle(1, 3, 120f), 0.001f);
            Assert.AreEqual(60f, HerdingMath.StationAngle(2, 3, 120f), 0.001f);
        }

        [Test]
        public void TheDragStation_IsBehindTheHerd_PastItsEdge()
        {
            // Herd heading north (+Z), radius 8, stand-off 5: the drag rides 13 m south of the centre.
            Vector3 station = HerdingMath.StationPoint(new Vector3(10f, 2f, 10f), Vector3.forward, 8f, 5f, 0f);

            Assert.AreEqual(10f, station.x, 0.001f);
            Assert.AreEqual(2f, station.y, 0.001f, "the station keeps the centre's height; the NavMesh sample fixes it");
            Assert.AreEqual(-3f, station.z, 0.001f);
        }

        [Test]
        public void AFlankStation_SwingsRoundTowardsTheSide()
        {
            // 90 degrees from behind is beside the herd: on the right (+X) for a positive angle.
            Vector3 station = HerdingMath.StationPoint(Vector3.zero, Vector3.forward, 8f, 2f, 90f);

            Assert.AreEqual(10f, station.x, 0.001f);
            Assert.AreEqual(0f, station.z, 0.001f);
        }

        [Test]
        public void AHeadingWithHeight_IsFlattened()
        {
            Vector3 station = HerdingMath.StationPoint(Vector3.zero, new Vector3(0f, 5f, 1f), 1f, 1f, 0f);

            Assert.AreEqual(0f, station.y, 0.001f);
            Assert.AreEqual(-2f, station.z, 0.001f);
        }

        [Test]
        public void NoHeading_FallsBackToSouthOfTheHerd_RatherThanNaN()
        {
            Vector3 station = HerdingMath.StationPoint(Vector3.zero, Vector3.zero, 1f, 1f, 0f);

            Assert.IsFalse(float.IsNaN(station.x) || float.IsNaN(station.z));
            Assert.AreEqual(2f, new Vector2(station.x, station.z).magnitude, 0.001f);
        }

        [Test]
        public void HerdRadius_IsTheFarthestHead_NeverBelowTheFloor()
        {
            var heads = new List<Vector3> { new(1f, 0f, 0f), new(0f, 9f, -4f), new(-2f, 0f, 0f) };

            Assert.AreEqual(4f, HerdingMath.HerdRadius(heads, Vector3.zero, 3f), 0.001f, "measured flat");
            Assert.AreEqual(6f, HerdingMath.HerdRadius(heads, Vector3.zero, 6f), 0.001f);
        }

        [Test]
        public void Centroid_AveragesTheHeads()
        {
            var heads = new List<Vector3> { new(0f, 0f, 0f), new(4f, 2f, 0f), new(2f, 4f, 6f) };

            Vector3 centre = HerdingMath.Centroid(heads);

            Assert.AreEqual(2f, centre.x, 0.001f);
            Assert.AreEqual(2f, centre.y, 0.001f);
            Assert.AreEqual(2f, centre.z, 0.001f);
        }

        [Test]
        public void TheStray_IsTheFarthestHeadBeyondTheRadius()
        {
            var heads = new List<Vector3> { new(5f, 0f, 0f), new(0f, 0f, 30f), new(-25f, 0f, 0f) };

            Assert.AreEqual(1, HerdingMath.StrayIndex(heads, Vector3.zero, 20f));
        }

        [Test]
        public void NoHeadBeyondTheRadius_IsNoStray()
        {
            var heads = new List<Vector3> { new(5f, 0f, 0f), new(0f, 0f, -10f) };

            Assert.AreEqual(-1, HerdingMath.StrayIndex(heads, Vector3.zero, 20f));
        }

        [Test]
        public void TheFetchPoint_IsOnTheFarSideOfTheStray()
        {
            Vector3 fetch = HerdingMath.FetchPoint(new Vector3(0f, 0f, 30f), Vector3.zero, 6f);

            Assert.AreEqual(0f, fetch.x, 0.001f);
            Assert.AreEqual(36f, fetch.z, 0.001f);
        }

        [Test]
        public void OnlyTheNearestHerder_GoesAfterTheStray()
        {
            var herders = new List<Vector3> { new(0f, 0f, 0f), new(0f, 0f, 25f), new(40f, 0f, 0f) };

            Assert.AreEqual(1, HerdingMath.NearestIndex(herders, new Vector3(0f, 0f, 30f)));
        }
    }
}
