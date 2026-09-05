using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The three pieces of the standing terminal that decide what the player sees and that
    /// nothing else exercises without a scene: where the camera parks, where the glass is, and
    /// what the pages say.
    /// </summary>
    public class TerminalTests
    {
        /// <summary>
        /// The lens is fitted to the glass's HEIGHT: at the distance it lands, a screen this tall
        /// covers exactly the asked-for share of the frame. A wrong tangent or a half-angle
        /// mistake here puts the player's nose on the glass or leaves the screen a stamp in the
        /// middle of the view, and neither fails anywhere else.
        /// </summary>
        [Test]
        public void TerminalShot_ParksTheLensWhereTheGlassFillsTheFrame()
        {
            const float height = 0.445f, fov = 40f, fill = 0.8f;

            float distance = TerminalShot.Distance(height, fov, fill);

            float frameHeightAtDistance = 2f * distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            Assert.AreEqual(fill, height / frameHeightAtDistance, 1e-4f,
                            "The glass should fill the asked-for share of the frame at the fitted distance.");

            // A screen leaning back 24 degrees is looked DOWN onto, along its own normal.
            Vector3 normal = (Quaternion.AngleAxis(-24f, Vector3.right) * Vector3.forward).normalized;
            var plane = new ScreenPlane(new Vector3(0f, 1.7f, 0f), normal, Vector3.ProjectOnPlane(Vector3.up, normal).normalized,
                                        Vector3.right, 0.6f, height);
            Vector3 lens = TerminalShot.LensPosition(plane, distance);
            Assert.AreEqual(distance, Vector3.Distance(lens, plane.Centre), 1e-4f);
            Assert.Greater(lens.y, plane.Centre.y, "A lens on a back-leaning screen's normal sits above the glass.");
            Assert.AreEqual(24f, TerminalShot.PitchDown(plane), 0.01f, "…and looks down by the lean.");
            Assert.AreEqual(180f, TerminalShot.Yaw(plane), 0.01f, "…back along -Z, into the glass.");
        }

        /// <summary>
        /// The glass is found from the plate's triangles alone. The normal must be the big face
        /// that points AWAY from the housing — a plate has two big faces and the wrong one aims
        /// the camera into the cabinet — and the size must be measured in the plate's own plane,
        /// which for a leaning plate is not any world axis.
        /// </summary>
        [Test]
        public void ScreenPlane_FindsTheOutwardFaceOfALeaningPlate()
        {
            // A 0.6 x 0.4 x 0.002 plate, leaned back 24 degrees about X, its housing behind it.
            Quaternion lean = Quaternion.AngleAxis(-24f, Vector3.right);
            Vector3 centre = new Vector3(0.04f, 1.7f, 0.5f);
            Vector3[] corners = new Vector3[8];
            int[] tris = new int[36];
            int c = 0;
            for (int z = 0; z < 2; z++)
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
                corners[c++] = centre + lean * new Vector3((x - 0.5f) * 0.6f, (y - 0.5f) * 0.4f, (z - 0.5f) * 0.002f);
            int t = 0;
            void Quad(int a, int b, int d, int e) { tris[t++] = a; tris[t++] = b; tris[t++] = d; tris[t++] = a; tris[t++] = d; tris[t++] = e; }
            Quad(0, 1, 3, 2); Quad(4, 6, 7, 5); Quad(0, 4, 5, 1); Quad(2, 3, 7, 6); Quad(0, 2, 6, 4); Quad(1, 5, 7, 3);

            Vector3 housingCentre = centre + lean * new Vector3(0f, 0f, 0.3f);
            ScreenPlane plane = ScreenPlane.Measure(corners, tris, housingCentre);

            Vector3 expectedNormal = lean * Vector3.back;
            Assert.Greater(Vector3.Dot(plane.Normal, expectedNormal), 0.999f,
                           $"Normal {plane.Normal} should point out of the glass, {expectedNormal}.");
            Assert.AreEqual(0.6f, plane.Width, 1e-3f);
            Assert.AreEqual(0.4f, plane.Height, 1e-3f);
            Assert.Greater(plane.Up.y, 0.9f, "Up stays upright on a leaning screen.");
            Assert.AreEqual(0.001f, Vector3.Dot(plane.Centre - centre, expectedNormal), 1e-4f,
                            "The centre is the FRONT face's, half a thickness out from the plate's middle.");
        }

        /// <summary>
        /// A snapshot composes into the words a player reads. One realistic reading rather than
        /// a test per line: a powered plant filling a bottle, two crew, an advisory-strength
        /// storm, late afternoon.
        /// </summary>
        [Test]
        public void ShipTelemetry_ComposesThePagesFromOneReading()
        {
            var s = TelemetrySnapshot.Empty;
            s.OxygenPresent = true; s.OxygenPowered = true; s.OxygenBattery01 = 0.5f; s.OxygenTank01 = 0.2f; s.OxygenFilling = true;
            s.CrewNames = new[] { "Ada", "Bo" };
            s.CrewOffsets = new[] { new Vector2(3f, 1f), new Vector2(400f, 0f) };
            s.Storm01 = 0.2f;
            s.TimeOfDay01 = 0.75f;
            s.Position = new Vector3(1234.5f, 12.3f, -345.2f);
            s.HeadingDegrees = 87f;

            string status = ShipTelemetry.StatusPage(s);
            StringAssert.Contains("CELL 50%  FILLING", status);
            StringAssert.Contains("ADVISORY   20%", status);
            StringAssert.Contains("CREW             2  Ada, Bo", status);
            StringAssert.Contains("18:00", status);

            string gps = ShipTelemetry.GpsPage(s);
            StringAssert.Contains("1234.5", gps);
            StringAssert.Contains("-345.2", gps);
            StringAssert.Contains("087°  E", gps);
            StringAssert.Contains("CREW IN RANGE  1", gps, "Bo stands 400 m out, past the radar's range.");

            Assert.AreEqual(PipState.Ok, ShipTelemetry.OxygenPip(s));
            Assert.AreEqual(PipState.Warn, ShipTelemetry.WeatherPip(s));

            ShipTelemetry.Segment[] strip = ShipTelemetry.SummarySegments(s);
            Assert.AreEqual("O2 OK", strip[1].Text);
            Assert.AreEqual(PipState.Ok, strip[1].State);
        }

        /// <summary>
        /// The mask the ship replicates is read as the schematic reads it: which sockets are full,
        /// how many of a KIND are aboard, and what is still to find. Off by one here and a crew is
        /// sent looking for a motor that is already bolted on.
        /// </summary>
        [Test]
        public void ShipPartInfo_CountsTheHullByKind()
        {
            // Two motors, two cores, one gun; the port motor and one core are aboard.
            var kinds = new[]
            {
                ShipPartKind.NuclearMotor, ShipPartKind.NuclearMotor,
                ShipPartKind.ReactorCore, ShipPartKind.ReactorCore,
                ShipPartKind.Gun,
            };
            int mask = (1 << 0) | (1 << 3);

            Assert.AreEqual(2, ShipPartInfo.CountInstalled(mask, kinds.Length));
            Assert.IsTrue(ShipPartInfo.IsInstalled(mask, 0));
            Assert.IsFalse(ShipPartInfo.IsInstalled(mask, 1));

            Assert.AreEqual(1, ShipPartInfo.FittedOfKind(mask, kinds, ShipPartKind.NuclearMotor));
            Assert.AreEqual(2, ShipPartInfo.TotalOfKind(kinds, ShipPartKind.NuclearMotor));
            Assert.AreEqual(0, ShipPartInfo.FittedOfKind(mask, kinds, ShipPartKind.Gun));

            CollectionAssert.AreEqual(
                new[] { ShipPartKind.NuclearMotor, ShipPartKind.ReactorCore, ShipPartKind.Gun },
                ShipPartInfo.MissingKinds(mask, kinds),
                "A kind is listed once however many of its sockets are empty.");

            StringAssert.Contains("2 OF 5 FITTED", ShipPartInfo.OverviewCount(mask, kinds));

            var s = TelemetrySnapshot.Empty;
            s.PartKinds = kinds;
            s.PartsInstalledMask = mask;
            StringAssert.Contains("INCOMPLETE  2/5", ShipTelemetry.ModulesLine(s));
            Assert.AreEqual(PipState.Warn, ShipTelemetry.ModulesPip(s));

            s.PartsInstalledMask = 0b11111;
            Assert.AreEqual(PipState.Ok, ShipTelemetry.ModulesPip(s));
            StringAssert.Contains("COMPLETE", ShipTelemetry.ModulesLine(s));
        }

        /// <summary>
        /// Every module the ship can be missing has a name and a sentence saying what it was for.
        /// A kind appended to the enum without them draws a blank panel, and a blank panel is the
        /// one thing a readout must never be.
        /// </summary>
        [Test]
        public void ShipPartInfo_HasWordsForEveryKind()
        {
            foreach (ShipPartKind kind in System.Enum.GetValues(typeof(ShipPartKind)))
            {
                string name = ShipPartInfo.Name(kind);
                string function = ShipPartInfo.Function(kind);

                Assert.AreEqual(name.ToUpperInvariant(), name, $"{kind}: the glass draws names in upper case.");
                Assert.IsFalse(name.Contains("_"), $"{kind}: '{name}' is the enum name, not a name for a crew.");
                StringAssert.DoesNotContain("No entry", function, $"{kind} has no description in ShipPartInfo.");
                Assert.Greater(function.Length, 30, $"{kind}: '{function}' does not say what the ship does without it.");
            }
        }

        /// <summary>
        /// The lens frames the whole hull and stays on its centre. The framing is fitted to the
        /// box's DIAGONAL, so that turning the hull cannot swing a wing out of shot, and
        /// <c>Home</c> puts a turned, zoomed view back where it started — which is what a page
        /// being reopened relies on.
        /// </summary>
        [Test]
        public void ShipSchematicOrbit_FramesTheWholeHullAndComesHome()
        {
            var orbit = new ShipSchematicOrbit();
            var hull = new Bounds(new Vector3(0f, 0.1f, 0f), new Vector3(1f, 0.3f, 0.6f));

            orbit.Adopt(hull, 1.6f);
            float whole = orbit.Size;
            float homeYaw = orbit.Yaw, homePitch = orbit.Pitch;

            Assert.AreEqual(hull.center, orbit.Pivot);
            Assert.GreaterOrEqual(whole, hull.extents.magnitude,
                                  "A framing tighter than the hull's diagonal clips it at some angle.");

            orbit.Drag(new Vector2(120f, 40f));
            orbit.Zoom(3f);
            for (int i = 0; i < 240; i++) orbit.Step(1f / 60f);

            Assert.AreNotEqual(homeYaw, orbit.Yaw, "The drag should have turned it.");
            Assert.Less(orbit.Size, whole, "The wheel should have pulled it in.");
            Assert.AreEqual(hull.center, orbit.Pivot,
                            "Nothing turns or zooms the lens OFF the hull's centre — the pivot is fixed.");

            orbit.Home();

            Assert.AreEqual(whole, orbit.Size, 1e-4f);
            Assert.AreEqual(homeYaw, orbit.Yaw, 1e-4f);
            Assert.AreEqual(homePitch, orbit.Pitch, 1e-4f);
        }

        /// <summary>
        /// Where a point in the miniature lands on the viewport, which is what decides whether a
        /// click that missed every module still picks the one it was aimed at. The unit is the
        /// frame's HALF-height in BOTH axes — if x were a fraction of the width instead, a single
        /// pick radius would be a tall ellipse on a wide viewport and modules would be easier to
        /// catch vertically than horizontally for no reason a player could see.
        /// </summary>
        [Test]
        public void ShipSchematicOrbit_ProjectsOntoTheViewportInHeights()
        {
            var orbit = new ShipSchematicOrbit();
            var hull = new Bounds(new Vector3(0.2f, -0.1f, 0f), Vector3.one);
            orbit.Adopt(hull, 2f);

            Assert.AreEqual(Vector2.zero, orbit.ViewportOffset(hull.center),
                            "What the lens is pointed at sits in the middle of the frame.");

            orbit.Lens(out _, out Quaternion rotation);
            float half = orbit.Size;

            Vector2 right = orbit.ViewportOffset(hull.center + rotation * Vector3.right * half);
            Assert.AreEqual(1f, right.x, 1e-4f, "One half-height across reads as 1.");
            Assert.AreEqual(0f, right.y, 1e-4f);

            Vector2 up = orbit.ViewportOffset(hull.center + rotation * Vector3.up * half);
            Assert.AreEqual(0f, up.x, 1e-4f);
            Assert.AreEqual(1f, up.y, 1e-4f, "…and so does one half-height up: the same unit.");

            // Straight down the barrel changes nothing on screen — an orthographic lens has no
            // perspective, so depth may not leak into a pick radius.
            Vector2 behind = orbit.ViewportOffset(hull.center + rotation * Vector3.forward * 2f);
            Assert.AreEqual(Vector2.zero, behind);
        }

        /// <summary>
        /// Zoom is bounded at both ends. Without the clamp a wheel spun in one direction walks the
        /// hull down to a dot or up past the edges of the glass, and neither state says how to get
        /// back.
        /// </summary>
        [Test]
        public void ShipSchematicOrbit_ClampsZoom()
        {
            var orbit = new ShipSchematicOrbit();
            var hull = new Bounds(Vector3.zero, Vector3.one);
            orbit.Adopt(hull, 1.6f);
            float whole = orbit.Size;

            for (int i = 0; i < 100; i++) orbit.Zoom(1f);
            for (int i = 0; i < 600; i++) orbit.Step(1f / 60f);
            Assert.AreEqual(whole * ShipSchematicOrbit.MinZoom, orbit.Size, 1e-3f);

            for (int i = 0; i < 200; i++) orbit.Zoom(-1f);
            for (int i = 0; i < 600; i++) orbit.Step(1f / 60f);
            Assert.AreEqual(whole * ShipSchematicOrbit.MaxZoom, orbit.Size, 1e-3f);
        }

        /// <summary>
        /// Dragging up and down cannot tip the lens over its own pole: past vertical the hull comes
        /// back mirrored and the reader has no way to tell which way round it is.
        /// </summary>
        [Test]
        public void ShipSchematicOrbit_ClampsPitchShortOfThePoles()
        {
            var orbit = new ShipSchematicOrbit();
            orbit.Adopt(new Bounds(Vector3.zero, Vector3.one), 1.6f);

            for (int i = 0; i < 50; i++) orbit.Drag(new Vector2(0f, 100f));
            for (int i = 0; i < 600; i++) orbit.Step(1f / 60f);
            Assert.AreEqual(ShipSchematicOrbit.MaxPitch, orbit.Pitch, 0.01f);

            for (int i = 0; i < 100; i++) orbit.Drag(new Vector2(0f, -100f));
            for (int i = 0; i < 600; i++) orbit.Step(1f / 60f);
            Assert.AreEqual(ShipSchematicOrbit.MinPitch, orbit.Pitch, 0.01f);
        }
    }
}
