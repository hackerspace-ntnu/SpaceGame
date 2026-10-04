// Every street style that uses the terrace kit agrees with the kit's prefabs. The paver never
// stretches a terrace-kit piece, so a style whose stepHeight, flightRun or landingRun disagrees with
// the meshes lays stairs and walls that float or sink -- with a clean console. Checked against the
// prefabs' measured footprints and their collision boxes (Collision/<name>), which follow the meshes.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class TerraceKitAssetTests
    {
        // A flight's treads overhang its run by their nosing.
        private const float NosingAllowance = 0.1f;
        private const float Tolerance = 0.01f;

        private static List<SettlementStreetStyle> KitStyles() =>
            AssetDatabase.FindAssets("t:SettlementStreetStyle")
                         .Select(guid => AssetDatabase.LoadAssetAtPath<SettlementStreetStyle>(AssetDatabase.GUIDToAssetPath(guid)))
                         .Where(style => style != null && (style.IsTileMode || style.ChainsStairs || style.StacksWalls))
                         .ToList();

        [Test]
        public void AStyleUsesTheTerraceKit() =>
            Assert.IsNotEmpty(KitStyles(), "no SettlementStreetStyle has terrace-kit pieces, so nothing below was checked");

        [Test]
        public void FlightsClimbOneStepPerFlightRunAlongTheirRamp()
        {
            foreach (var style in KitStyles())
            {
                foreach (var flight in new[] { style.flightWide, style.flightNarrow })
                {
                    if (flight == null) continue;
                    Rect footprint = Footprint(flight);
                    // Tuck tread behind the origin to last nosing: one flight run, since the next flight's tuck starts where this one's ends.
                    Assert.That(footprint.height, Is.InRange(style.flightRun - Tolerance, style.flightRun + NosingAllowance),
                                $"{style.name}: {flight.name} is {footprint.height:0.###} m deep, but flightRun is {style.flightRun}");

                    BoxCollider ramp = Box(flight, "Ramp");
                    float pitch = Mathf.DeltaAngle(0f, ramp.transform.localEulerAngles.x);
                    float expected = Mathf.Atan2(style.stepHeight, style.flightRun) * Mathf.Rad2Deg;
                    Assert.AreEqual(expected, pitch, 0.5f, $"{style.name}: {flight.name}'s ramp climbs at {pitch:0.#} degrees; stepHeight over flightRun is {expected:0.#}");
                }
            }
        }

        [Test]
        public void FlightsAreAsWideAsTheStreetsThatClimbThem()
        {
            foreach (var style in KitStyles())
            {
                float tileWidth = SettlementStreetPaver.TileSize(style).x;
                if (style.flightWide != null && tileWidth > 0f)
                    Assert.AreEqual(tileWidth, Box(style.flightWide, "Ramp").size.x, Tolerance, $"{style.name}: the wide flight's walk is not a road tile wide");
                if (style.flightNarrow != null)
                    Assert.AreEqual(style.stonePathWidth, Box(style.flightNarrow, "Ramp").size.x, Tolerance, $"{style.name}: the narrow flight's walk is not stonePathWidth");
            }
        }

        [Test]
        public void TheLandingIsLandingRunLong()
        {
            foreach (var style in KitStyles())
            {
                if (style.landing == null) continue;
                Assert.AreEqual(style.landingRun, Box(style.landing, "Deck").size.z, Tolerance, $"{style.name}: {style.landing.name}'s deck is not landingRun long");
            }
        }

        [Test]
        public void RoadTilesAllHaveOneLength()
        {
            foreach (var style in KitStyles())
            {
                var tiles = style.road.tiles.Where(piece => piece.prefab != null).Select(piece => piece.prefab).ToList();
                if (tiles.Count == 0) continue;
                float length = Footprint(tiles[0]).height;
                foreach (var tile in tiles)
                    Assert.AreEqual(length, Footprint(tile).height, Tolerance, $"{style.name}: {tile.name} is not as long as {tiles[0].name}; runs would open gaps");
            }
        }

        [Test]
        public void CoursesAreOneStepTallAndStackFlushUnderTheTopPiece()
        {
            foreach (var style in KitStyles())
            {
                foreach (var course in style.wallCourses.Where(piece => piece.prefab != null).Select(piece => piece.prefab))
                {
                    BoxCollider body = Box(course, "Body");
                    Assert.AreEqual(style.stepHeight, body.size.y, Tolerance, $"{style.name}: {course.name} is not stepHeight tall");
                    Assert.AreEqual(0f, Centre(body).y + body.size.y * 0.5f, Tolerance, $"{style.name}: {course.name}'s top is not at its origin");
                    Assert.Greater(style.courseBatter, 0f, $"{style.name}: courses with no batter stand flush and their faces z-fight");

                    foreach (var top in style.terraceWalls.Where(piece => piece.prefab != null).Select(piece => piece.prefab))
                    {
                        BoxCollider topBody = Box(top, "Body");
                        Assert.AreEqual(Front(topBody), Front(body), Tolerance,
                                        $"{style.name}: {course.name}'s face is not in line with {top.name}'s, so courseBatter does not set it forward by exactly the batter");
                    }
                }
            }
        }

        [Test]
        public void ThePillarReachesPastAFullStack()
        {
            foreach (var style in KitStyles())
            {
                if (style.wallPillar == null) continue;
                BoxCollider shaft = Box(style.wallPillar, "Shaft");
                float bottom = Centre(shaft).y - shaft.size.y * 0.5f;
                float stack = (style.maxWallCourses + 1) * style.stepHeight;
                Assert.LessOrEqual(bottom, -stack, $"{style.name}: {style.wallPillar.name} reaches {-bottom:0.##} m down, a full stack {stack:0.##} m");
            }
        }

        // In the prefab root's frame; the unrotated boxes these are read from sit on their own child objects.
        private static Vector3 Centre(BoxCollider box) => box.transform.parent.localPosition + box.transform.localPosition + box.center;

        private static float Front(BoxCollider box) => Centre(box).z + box.size.z * 0.5f;

        private static Rect Footprint(GameObject prefab) => SettlementPlacementUtil.MeasureFootprint(prefab, Vector2.one);

        private static BoxCollider Box(GameObject prefab, string name)
        {
            Transform t = prefab.transform.Find("Collision/" + name);
            Assert.IsNotNull(t, $"{prefab.name} has no Collision/{name}");
            var box = t.GetComponent<BoxCollider>();
            Assert.IsNotNull(box, $"{prefab.name}'s Collision/{name} has no BoxCollider");
            return box;
        }
    }
}
