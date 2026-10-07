// Where a sky vessel sets its war party down. The finder is pure over IGroundProbe, so these
// tests describe terrain as a height function plus blocked circles plus NavMesh coverage and never
// touch physics. The cases are the drop-off rule the user asked for: land when the ground is flat
// ENOUGH (generous about slope and bumps), otherwise hover over open ground with NavMesh below.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Vehicles;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class LandingSiteFinderTests
    {
        private const float Footprint = 12f;
        private static readonly Vector3 Quarry = Vector3.zero;
        private static readonly Vector3 FromEast = new Vector3(500f, 0f, 0f);

        private static LandingSettings Settings => LandingSettings.Default;
        private static float MidRing => (Settings.ringMin + Settings.ringMax) * 0.5f;

        private static float FlatDistance(Vector3 a, Vector3 b) =>
            new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static DropSite? Find(FakeGroundProbe probe, Vector3 approachFrom) =>
            LandingSiteFinder.Find(Quarry, approachFrom, Footprint, Settings, probe);

        // ── Land ───────────────────────────────────────────────────────────────────

        [Test]
        public void FlatGroundLandsOnTheMidRingFacingTheApproach()
        {
            DropSite? site = Find(new FakeGroundProbe((x, z) => 0f), FromEast);

            Assert.IsTrue(site.HasValue, "flat open ground always has a site");
            Assert.AreEqual(DropMode.Land, site.Value.Mode);
            Assert.AreEqual(MidRing, FlatDistance(site.Value.Point, Quarry), 0.01f, "the preferred ring");
            Assert.AreEqual(MidRing, site.Value.Point.x, 0.01f, "on the side the vessel comes from");
            Assert.AreEqual(0f, site.Value.Point.y, 0.01f, "a Land site's point is the ground");

            Vector3 rampEnd = new Vector3(MidRing - Footprint, 0f, 0f);
            Assert.LessOrEqual(Vector3.Distance(site.Value.UnloadPoint, rampEnd), Settings.navMeshReach,
                               "the party walks off the ramp, which faces the quarry");
        }

        [Test]
        public void AGentleSlopeWithModestBumpsStillLands()
        {
            // Ground normals here are the bumps' own finite differences — up to 42° off vertical at
            // some samples — so a slope read from one normal would reject this field.
            float tan = Mathf.Tan(10f * Mathf.Deg2Rad);
            var probe = new FakeGroundProbe((x, z) => x * tan + Mathf.Sin(z * 0.9f));

            DropSite? site = Find(probe, FromEast);

            Assert.IsTrue(site.HasValue);
            Assert.AreEqual(DropMode.Land, site.Value.Mode, "10 degrees and 2 m of bumps is flat enough");
        }

        [Test]
        public void FlatGroundWithASteepStoneFaceUnderTheCentreStillLands()
        {
            Vector3 stoneFace = Quaternion.AngleAxis(60f, Vector3.forward) * Vector3.up;
            var probe = new FakeGroundProbe((x, z) => 0f,
                                            (x, z) => OnAnyRing(new Vector2(x, z).magnitude) ? stoneFace : Vector3.up);

            DropSite? site = Find(probe, FromEast);

            Assert.IsTrue(site.HasValue);
            Assert.AreEqual(DropMode.Land, site.Value.Mode, "one tilted pebble face does not make a field a slope");
            Assert.AreEqual(MidRing, site.Value.Point.x, 0.01f);
        }

        [Test]
        public void NavMeshJustOffTheRampFootStillLands()
        {
            // NavMesh starts 2 m beyond each mid-ring ramp foot (65 − 12 = 53 m out): within reach.
            var probe = new FakeGroundProbe((x, z) => 0f)
            {
                MeshAt = p => FlatDistance(p, Quarry) >= MidRing - Footprint + 2f,
            };

            DropSite? site = Find(probe, FromEast);

            Assert.IsTrue(site.HasValue);
            Assert.AreEqual(DropMode.Land, site.Value.Mode);
            Assert.AreEqual(MidRing, site.Value.Point.x, 0.01f);
            Assert.GreaterOrEqual(FlatDistance(site.Value.UnloadPoint, Quarry), MidRing - Footprint + 2f - 0.01f,
                                  "the party is put down on the NavMesh, not at the bare ramp foot");
        }

        // ── Hover ──────────────────────────────────────────────────────────────────

        [Test]
        public void ARockyFieldFallsBackToHovering()
        {
            var probe = new FakeGroundProbe(Rocks);

            DropSite? site = Find(probe, FromEast);

            Assert.IsTrue(site.HasValue, "rocks rule out landing, not dropping off");
            Assert.AreEqual(DropMode.Hover, site.Value.Mode);

            Vector3 p = site.Value.Point;
            Assert.GreaterOrEqual(p.y, BoulderHeight + Settings.hoverHeight, "hovers clear of the boulders under the hull");
            Assert.AreEqual(Rocks(p.x, p.z), site.Value.UnloadPoint.y, 0.01f, "the party drops onto the NavMesh below");
            Assert.AreEqual(0f, FlatDistance(site.Value.UnloadPoint, p), 0.01f);
        }

        [Test]
        public void NoNavMeshAtTheRampEndHovers()
        {
            // NavMesh only in thin bands on the ring radii: every centre is on it, no ramp end is.
            var probe = new FakeGroundProbe((x, z) => 0f)
            {
                MeshAt = p => OnAnyRing(FlatDistance(p, Quarry)),
            };

            DropSite? site = Find(probe, FromEast);

            Assert.IsTrue(site.HasValue);
            Assert.AreEqual(DropMode.Hover, site.Value.Mode);
            Assert.AreEqual(MidRing, site.Value.Point.x, 0.01f, "still the best-scored candidate");
        }

        // ── Neither ────────────────────────────────────────────────────────────────

        [Test]
        public void ARingBlockedOverheadHasNoSite()
        {
            var probe = new FakeGroundProbe((x, z) => 0f);
            probe.Blocked.Add((Vector2.zero, Settings.ringMax + Footprint + 10f));

            Assert.IsFalse(Find(probe, FromEast).HasValue, "nowhere to land or hover");
        }

        // ── Preference ─────────────────────────────────────────────────────────────

        [Test]
        public void TheChosenSitePrefersTheApproachSide()
        {
            var flat = new FakeGroundProbe((x, z) => 0f);
            Assert.Less(Find(flat, -FromEast).Value.Point.x, 0f, "coming from the west lands west");

            var probe = new FakeGroundProbe((x, z) => 0f);
            probe.Blocked.Add((new Vector2(MidRing, 0f), 4f));
            DropSite? site = Find(probe, FromEast);

            // The next-best candidates are the two either side of it on the same ring, one step round
            // from the approach each way. They score equally up to float rounding, so either will do.
            float step = 360f / Settings.candidatesPerRing;
            Vector3 left = Quaternion.AngleAxis(step, Vector3.up) * Vector3.right * MidRing;
            Vector3 right = Quaternion.AngleAxis(-step, Vector3.up) * Vector3.right * MidRing;

            Assert.IsTrue(site.HasValue);
            Assert.AreEqual(DropMode.Land, site.Value.Mode);
            Assert.AreEqual(0f, Mathf.Min(FlatDistance(site.Value.Point, left), FlatDistance(site.Value.Point, right)), 0.01f,
                            "the ideal spot is taken, so the nearest approach-side spot on the preferred ring");
        }

        // ── Fixtures ───────────────────────────────────────────────────────────────

        private const float BoulderHeight = 8f;

        // 8 m boulders everywhere except thin bands on the ring radii, so every candidate centre is
        // open ground and every footprint rests on boulders somewhere: an 8 m spread, deterministically.
        private static float Rocks(float x, float z) =>
            OnAnyRing(new Vector2(x, z).magnitude) ? 0f : BoulderHeight;

        // ── Re-checking a chosen site ───────────────────────────────────────────────

        [Test]
        public void AChosenSiteStaysClearUntilSomethingMovesIn()
        {
            var probe = new FakeGroundProbe((x, z) => 0f);
            DropSite site = Find(probe, FromEast).Value;

            Assert.IsTrue(LandingSiteFinder.IsStillClear(site, Quarry, Footprint, Settings, probe));

            probe.Blocked.Add((new Vector2(site.Point.x, site.Point.z), 3f));
            Assert.IsFalse(LandingSiteFinder.IsStillClear(site, Quarry, Footprint, Settings, probe),
                           "a camp pitched on the site since it was chosen");
        }

        [Test]
        public void TheGroundNormalIsTheFittedSlopeNotOneStone()
        {
            float tan = Mathf.Tan(15f * Mathf.Deg2Rad);
            var probe = new FakeGroundProbe((x, z) => x * tan, (x, z) => Vector3.right);

            Vector3 normal = LandingSiteFinder.GroundNormal(new Vector3(65f, 0f, 0f), Footprint, probe);

            Assert.AreEqual(15f, Vector3.Angle(Vector3.up, normal), 0.1f, "the hull lies along the slope");
            Assert.Less(normal.x, 0f, "leaning back from the uphill side");
        }

        private static bool OnAnyRing(float distance)
        {
            for (int i = 0; i < Settings.rings; i++)
            {
                float ring = Mathf.Lerp(Settings.ringMin, Settings.ringMax, i / (float)(Settings.rings - 1));
                if (Mathf.Abs(distance - ring) < 1f) return true;
            }
            return false;
        }

        /// <summary>
        /// Terrain as a height function. Ground normals come from finite differences unless a normal
        /// function is given, so a test can show the finder does not trust one normal. Obstacles are
        /// vertical cylinders; NavMesh covers every point <see cref="MeshAt"/> accepts.
        /// </summary>
        private sealed class FakeGroundProbe : IGroundProbe
        {
            private const float NormalStep = 0.5f;
            private const float MeshSearchStep = 0.5f;
            private const int MeshSearchDirections = 16;

            private readonly Func<float, float, float> height;
            private readonly Func<float, float, Vector3> normalAt;

            public readonly List<(Vector2 centre, float radius)> Blocked = new List<(Vector2, float)>();
            public Func<Vector3, bool> MeshAt = p => true;

            public FakeGroundProbe(Func<float, float, float> height, Func<float, float, Vector3> normalAt = null)
            {
                this.height = height;
                this.normalAt = normalAt ?? DifferenceNormal;
            }

            public bool TryGround(Vector3 xzPoint, out Vector3 point, out Vector3 normal)
            {
                point = new Vector3(xzPoint.x, height(xzPoint.x, xzPoint.z), xzPoint.z);
                normal = normalAt(xzPoint.x, xzPoint.z);
                return true;
            }

            public bool IsClear(Vector3 center, float radius, float clearHeight)
            {
                var flat = new Vector2(center.x, center.z);
                foreach ((Vector2 c, float r) in Blocked)
                    if (Vector2.Distance(flat, c) < r + radius) return false;
                return true;
            }

            // Like NavMesh.SamplePosition: the nearest covered point within maxDistance, searched
            // outward in half-metre rings.
            public bool TryNavMesh(Vector3 point, float maxDistance, out Vector3 onMesh)
            {
                for (float radius = 0f; radius <= maxDistance; radius += MeshSearchStep)
                {
                    int directions = radius > 0f ? MeshSearchDirections : 1;
                    for (int i = 0; i < directions; i++)
                    {
                        Vector3 offset = Quaternion.AngleAxis(360f * i / directions, Vector3.up) * Vector3.right * radius;
                        Vector3 probe = point + offset;
                        onMesh = new Vector3(probe.x, height(probe.x, probe.z), probe.z);
                        if (MeshAt(onMesh) && Vector3.Distance(onMesh, point) <= maxDistance) return true;
                    }
                }
                onMesh = point;
                return false;
            }

            private Vector3 DifferenceNormal(float x, float z)
            {
                float dx = (height(x + NormalStep, z) - height(x - NormalStep, z)) / (2f * NormalStep);
                float dz = (height(x, z + NormalStep) - height(x, z - NormalStep)) / (2f * NormalStep);
                return new Vector3(-dx, 1f, -dz).normalized;
            }
        }
    }
}
