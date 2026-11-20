// Where the lander puts a seated body, measured against the chair it is sitting in.
//
// A seat marker is a body PIVOT, not a point on the chair: the player's pivot is a metre above its
// soles, and a seated pivot rests a little over half a metre above the cushion. Nothing reads the
// chair when it seats somebody — SeatedRider and MountModule both put the pivot exactly on their
// marker — so a marker authored at the wrong height sinks its rider into the chair, with a clean
// console. It is also nearly invisible to the player in that chair: first person, the camera is
// only lower. It is their crewmates who see them sitting inside the seat.
//
// That is how it shipped: ArrivalSeat2 and PassengerSeat1's SeatPoint were both 0.65 m below their
// twin on the helm chair, on an identical chair. The host always takes seat 1, so the host looked
// right, and the first client to join sat inside the co-pilot's chair on every other screen.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class ShipSeatPlacementTests
    {
        private const string ShipPrefabPath =
            "Assets/Game/Prefabs/Agents/Vehicles/Spacecraft/PlayerShip.prefab";

        /// <summary>The chairs' renderers, as exported from the lander's .blend.</summary>
        private const string ChairPrefix = "Cockpit_Seat_Command";

        /// <summary>
        /// How far over the cushion a seated pivot may rest. The chair sit's hips are about 0.55 m
        /// under a pivot that is a metre above the soles; the band allows for the cockpit and the
        /// aft chairs being different models, and nothing more.
        /// </summary>
        private const float MinPivotAboveCushion = 0.4f;
        private const float MaxPivotAboveCushion = 0.7f;

        /// <summary>Metres of drift treated as the same point.</summary>
        private const float PositionTolerance = 0.01f;

        private static GameObject Ship()
        {
            var ship = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            Assert.IsNotNull(ship, $"No ship prefab at {ShipPrefabPath}.");
            return ship;
        }

        [Test]
        public void EveryArrivalSeatRestsItsRiderOnTheCushion()
        {
            ShipSeat[] seats = Ship().GetComponentsInChildren<ShipSeat>(true);
            Assert.IsNotEmpty(seats, "PlayerShip has no ShipSeat markers, so nobody is seated in it.");

            foreach (ShipSeat seat in seats)
                AssertRestsOnCushion(seat.transform);
        }

        [Test]
        public void EveryMountSeatRestsItsRiderOnTheCushion()
        {
            foreach (MountModule mount in Ship().GetComponentsInChildren<MountModule>(true))
                AssertRestsOnCushion(SeatPointOf(mount));
        }

        [Test]
        public void TheDescentAndTheChairSeatARiderInTheSamePlace()
        {
            // Two systems seat a player in the same four chairs: the arrival through its markers,
            // a player who sits down later through the chair's own MountModule. A rider who changes
            // height between the crash and the next time they sit in the same chair is one of the
            // two markers being wrong.
            GameObject ship = Ship();
            var mountSeats = new List<Transform>();
            foreach (MountModule mount in ship.GetComponentsInChildren<MountModule>(true))
                mountSeats.Add(SeatPointOf(mount));

            foreach (ShipSeat seat in ship.GetComponentsInChildren<ShipSeat>(true))
            {
                Transform nearest = null;
                foreach (Transform point in mountSeats)
                    if (nearest == null || Flat(point.position, seat.transform.position) <
                        Flat(nearest.position, seat.transform.position))
                        nearest = point;

                Assert.IsNotNull(nearest, "PlayerShip has arrival seats but no mounted chairs.");
                Assert.Less(Vector3.Distance(nearest.position, seat.transform.position), PositionTolerance,
                    $"'{seat.name}' seats its rider at {seat.transform.position:F3}, but the chair under " +
                    $"it ('{nearest.parent.name}/{nearest.name}') seats them at {nearest.position:F3}.");
            }
        }

        private static Transform SeatPointOf(MountModule mount)
        {
            var seatPoint = new SerializedObject(mount).FindProperty("seatPoint").objectReferenceValue as Transform;
            Assert.IsNotNull(seatPoint, $"MountModule on '{mount.name}' has no seatPoint.");
            return seatPoint;
        }

        private static void AssertRestsOnCushion(Transform pivot)
        {
            Assert.IsTrue(TryCushionUnder(pivot.position, out float cushion, out string chair),
                $"'{pivot.parent.name}/{pivot.name}' at {pivot.position:F3} is over no {ChairPrefix} chair.");

            float above = pivot.position.y - cushion;
            Assert.That(above, Is.InRange(MinPivotAboveCushion, MaxPivotAboveCushion),
                $"'{pivot.parent.name}/{pivot.name}' rests its rider's pivot {above:F3} m over the " +
                $"cushion of '{chair}' (y {cushion:F3}); a seated body needs " +
                $"{MinPivotAboveCushion}-{MaxPivotAboveCushion} m or it sinks into the chair.");
        }

        /// <summary>
        /// The top of the chair straight under <paramref name="point"/>: the highest triangle of the
        /// nearest chair's mesh that a vertical line through it crosses. Mesh, not collider — the
        /// chairs' collision is a convex bake, and convex fill sits above the cushion it wraps.
        /// </summary>
        private static bool TryCushionUnder(Vector3 point, out float height, out string chair)
        {
            height = float.NegativeInfinity;
            chair = null;
            MeshFilter nearest = null;

            foreach (MeshFilter filter in Ship().GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith(ChairPrefix) || filter.sharedMesh == null) continue;
                if (nearest == null || Flat(BoundsCentre(filter), point) < Flat(BoundsCentre(nearest), point))
                    nearest = filter;
            }

            if (nearest == null) return false;

            Vector3[] vertices = nearest.sharedMesh.vertices;
            int[] triangles = nearest.sharedMesh.triangles;
            Transform space = nearest.transform;
            var at = new Vector2(point.x, point.z);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = space.TransformPoint(vertices[triangles[i]]);
                Vector3 b = space.TransformPoint(vertices[triangles[i + 1]]);
                Vector3 c = space.TransformPoint(vertices[triangles[i + 2]]);

                if (TryHeightAt(at, a, b, c, out float y) && y < point.y && y > height)
                    height = y;
            }

            chair = nearest.name;
            return !float.IsNegativeInfinity(height);
        }

        /// <summary>Where a vertical line through <paramref name="at"/> crosses a triangle, if it does.</summary>
        private static bool TryHeightAt(Vector2 at, Vector3 a, Vector3 b, Vector3 c, out float y)
        {
            y = 0f;
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(d) < 1e-9f) return false;

            float wa = ((b.z - c.z) * (at.x - c.x) + (c.x - b.x) * (at.y - c.z)) / d;
            float wb = ((c.z - a.z) * (at.x - c.x) + (a.x - c.x) * (at.y - c.z)) / d;
            float wc = 1f - wa - wb;
            if (wa < 0f || wb < 0f || wc < 0f) return false;

            y = wa * a.y + wb * b.y + wc * c.y;
            return true;
        }

        private static Vector3 BoundsCentre(MeshFilter filter) => filter.GetComponent<Renderer>().bounds.center;

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
