// The sprayed aperture: its shape, the spray session that builds one, and the two things that
// only break once a portal stops being an ellipse.
//
// The shape is the interesting half. A portal used to be one Vector2, and a great deal of this
// file exists to pin the promise that an aperture with NO paint on it still answers exactly as it
// did — PortalLifecycleTests and PortalTraversalTests are the rest of that proof, and they are
// expected to pass untouched.
//
// The two new failures worth a test of their own:
//
//   • THE ORIGIN MUST NOT FOLLOW THE PAINT. The transform is what TransferFrom composes, so a
//     portal that recentred itself as it grew would move the exit out from under anyone walking
//     through a portal still being sprayed.
//
//   • AN EXIT MUST LAND INSIDE THE FAR OUTLINE. Two ellipses share an outline and the raw transfer
//     is safe; two sprayed blobs do not, and entering through a lobe the destination has no copy
//     of would drop a traveller inside the wall.
//
// Edit mode, so nothing here may lean on Awake, OnEnable or LateUpdate — AddComponent raises none
// of them outside play mode. Same constraint the other portal suites work under.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Portals;

namespace SpaceGame.EditorTools
{
    public class SprayedPortalTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [SetUp]
        public void SetUp() => Portal.All.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (Portal portal in new List<Portal>(Portal.All))
                if (portal != null) Object.DestroyImmediate(portal.gameObject);

            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
            Portal.All.Clear();
        }

        // ── The stencil with no dabs: today's ellipse, unchanged ──────────────

        [Test]
        public void EmptyStencilIsTheEllipseInscribedInItsSize()
        {
            var stencil = new PortalStencil();
            stencil.SetEllipse(new Vector2(4f, 2f));

            Assert.IsTrue(stencil.IsEllipse, "no dabs means ellipse mode");
            Assert.IsTrue(stencil.Contains(Vector2.zero), "the centre is inside");
            Assert.IsTrue(stencil.Contains(new Vector2(1.9f, 0f)), "just inside the wide semi-axis");
            Assert.IsFalse(stencil.Contains(new Vector2(2.1f, 0f)), "just outside it");
            Assert.IsFalse(stencil.Contains(new Vector2(1.5f, 0.9f)),
                           "outside the ellipse though inside the box");
            Assert.AreEqual(1f, stencil.InscribedRadius, 1e-3f, "half the narrow axis");
        }

        [Test]
        public void EllipseBoundsAreItsSize()
        {
            var stencil = new PortalStencil();
            stencil.SetEllipse(new Vector2(4f, 2f));

            Assert.AreEqual(4f, stencil.Bounds.width, 1e-3f);
            Assert.AreEqual(2f, stencil.Bounds.height, 1e-3f);
            Assert.AreEqual(Vector2.zero, stencil.Bounds.center);
        }

        // ── The stencil with dabs ─────────────────────────────────────────────

        [Test]
        public void OneDabIsACircleAtItsOwnCentre()
        {
            var stencil = new PortalStencil();
            stencil.AddDab(new Vector2(1f, 0f), 0.6f);

            Assert.IsFalse(stencil.IsEllipse);
            Assert.AreEqual(1, stencil.Count);
            Assert.IsTrue(stencil.Contains(new Vector2(1f, 0f)));
            Assert.IsTrue(stencil.Contains(new Vector2(1.5f, 0f)));
            Assert.IsFalse(stencil.Contains(new Vector2(2.0f, 0f)));
            Assert.AreEqual(0.6f, stencil.InscribedRadius, 0.08f);
        }

        [Test]
        public void SweptDabsMakeOneShapeWiderThanEitherOfThem()
        {
            var stencil = new PortalStencil();
            for (int i = 0; i < 5; i++) stencil.AddDab(new Vector2(i * 0.4f, 0f), 0.6f);

            Assert.IsTrue(stencil.Contains(new Vector2(0.2f, 0f)), "between two dab centres");
            Assert.IsTrue(stencil.Contains(new Vector2(1.6f, 0f)), "at the far end of the stroke");
            Assert.IsFalse(stencil.Contains(new Vector2(2.8f, 0f)), "past it");

            Assert.Greater(stencil.Bounds.width, 2.2f, "the box spans the stroke");
            Assert.Less(stencil.Bounds.height, 1.7f, "and not much across it");
        }

        [Test]
        public void ATallStrokeFitsATallTravellerAndRefusesAWideOne()
        {
            var stencil = new PortalStencil();
            for (int i = 0; i < 6; i++) stencil.AddDab(new Vector2(0f, i * 0.35f), 0.55f);

            Assert.Less(stencil.InscribedRadius, 0.8f,
                        "the hole is as wide as the stroke, not as long");
            Assert.IsTrue(stencil.Fits(new Vector2(0.3f, 0.3f)), "a small thing goes through");
            Assert.IsFalse(stencil.Fits(new Vector2(1.2f, 0.3f)), "a wide thing does not");
        }

        [Test]
        public void DabsBeyondTheCapMergeInsteadOfBeingDropped()
        {
            var stencil = new PortalStencil();
            for (int i = 0; i < PortalStencil.MaxDabs + 10; i++)
                stencil.AddDab(new Vector2(i * 0.3f, 0f), 0.5f);

            Assert.AreEqual(PortalStencil.MaxDabs, stencil.Count, "never more than the cap");
            Assert.IsTrue(stencil.Contains(new Vector2((PortalStencil.MaxDabs + 9) * 0.3f, 0f)),
                          "and the last dab still shows up in the shape");
        }

        // ── Paint merges where it lands; only painting widens the hole ───────

        [Test]
        public void HoldingTheStreamStillDoesNotWidenTheHole()
        {
            var stencil = new PortalStencil();
            stencil.AddDab(Vector2.zero, 0.6f);

            float first = stencil.InscribedRadius;

            // The same spot, ten more times — a player holding the nozzle still.
            for (int i = 0; i < 10; i++) stencil.AddDab(Vector2.zero, 0.6f);

            Assert.AreEqual(1, stencil.Count, "merged into one blob, not stacked as eleven");
            Assert.AreEqual(first, stencil.InscribedRadius, 1e-3f,
                            "and the hole did not widen — growth comes from painting, not waiting");
        }

        [Test]
        public void AWobblingStreamHeldInPlaceConvergesInsteadOfSpreading()
        {
            // A hand is never perfectly still: dabs land jittered a few centimetres apart. The
            // hole may grow to COVER the wobble, and no further — waiting must never be a way to
            // open a hole the size of the wall.
            var stencil = new PortalStencil();
            for (int i = 0; i < 60; i++)
            {
                float angle = i * 2.4f;
                var jitter = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.05f;
                stencil.AddDab(jitter, 0.6f);
            }

            Assert.AreEqual(1, stencil.Count, "one puddle");
            Assert.Less(stencil.InscribedRadius, 0.75f, "covering the wobble and nothing more");
        }

        [Test]
        public void NearbyPaintMergesIntoTheCircleEnclosingBoth()
        {
            var stencil = new PortalStencil();
            stencil.AddDab(Vector2.zero, 0.6f);
            stencil.AddDab(new Vector2(0.2f, 0f), 0.6f);

            Assert.AreEqual(1, stencil.Count, "close enough to be the same puddle");
            Assert.IsTrue(stencil.Contains(new Vector2(0.75f, 0f)),
                          "the merged blob reaches the new paint's far edge");
            Assert.IsTrue(stencil.Contains(new Vector2(-0.55f, 0f)),
                          "and still covers the old");
        }

        [Test]
        public void PaintLandingElsewhereStartsANewBlob()
        {
            var stencil = new PortalStencil();
            stencil.AddDab(Vector2.zero, 0.6f);
            stencil.AddDab(new Vector2(2.5f, 0f), 0.6f);

            Assert.AreEqual(2, stencil.Count, "far enough apart to be its own blob");
        }

        [Test]
        public void TheReferenceScaleDoesNotFollowTheGrowth()
        {
            var stencil = new PortalStencil();
            stencil.AddDab(Vector2.zero, 0.6f);

            float reference = stencil.ReferenceScale;

            for (int i = 0; i < 6; i++) stencil.AddDab(new Vector2(i * 0.4f, 0f), 0.6f);

            // The whole point: the shader normalises the rim, throat and vortex against this, so if
            // it grew with the paint the aperture would visibly rescale as it was sprayed.
            Assert.AreEqual(reference, stencil.ReferenceScale, 1e-4f);
            Assert.Greater(stencil.InscribedRadius, 0f);
        }

        // ── The jet arcs, and does not reach far ─────────────────────────────

        /// <summary>The shipped hose. Kept here so the sums below read as one set of numbers.</summary>
        private const float Speed = 24f;
        private const float Flight = 2f;

        [Test]
        public void TheStreamFallsUnderGravity()
        {
            Vector3 level = SprayArc.Sample(Vector3.zero, Vector3.forward, Speed, 1f, 0.8f);

            Assert.Less(level.y, -1f, "most of a second out, the stream has visibly dropped");
            Assert.Greater(level.z, 5f, "and is still going forward");
        }

        [Test]
        public void TheStreamIsLobbedRatherThanAimed()
        {
            // The pressure went up so a wall across a room is reachable — but the shape of the
            // thing did not change. Held level from chest height the stream is on the floor around
            // ten metres out, so the far wall still has to be LOBBED at, which is the whole
            // difference between this and the hitscan gun that placed a portal at 30 m on a ray.
            //
            // Everything here is measured against Physics.gravity, which is 18 in this project.
            // Doing the same sums against 9.81 is what left the shipped hose reaching half what
            // its own comments claimed.
            float lobbed = SprayArc.BallisticRange(Speed, 1f);

            Assert.Greater(lobbed, 25f, "the hose has no pressure; a room is out of reach");
            Assert.Less(lobbed, 45f, "at this range it is a paint gun again, not a hose");

            float chestHeight = 1.5f;
            float level = Speed * Mathf.Sqrt(2f * chestHeight / Physics.gravity.magnitude);
            Assert.Less(level, lobbed * 0.5f, "pointing straight at it must not be the best throw");

            // And the arc must fit in the flight budget, or the paint is given up on in mid-air
            // and a full lob silently falls short of the range above.
            Assert.Greater(Flight, 2f * Speed * 0.7071f / Physics.gravity.magnitude * 0.8f,
                           "the stream is abandoned before the lob has landed");
        }

        [Test]
        public void TheStreamStopsAtTheFirstThingItHits()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 0f, 6f);
            wall.transform.localScale = new Vector3(20f, 20f, 0.5f);
            spawned.Add(wall);

            Physics.SyncTransforms();

            Assert.IsTrue(SprayArc.Trace(Vector3.zero, Vector3.forward, Speed, 1f, Flight, ~0,
                                          out RaycastHit hit, out float flight));

            Assert.Less(Mathf.Abs(hit.point.z - 5.75f), 0.3f, "stopped at the wall's near face");
            Assert.Greater(flight, 0f, "and took time to get there");
            Assert.Less(flight, Flight, "well inside its flight budget");

            // Gravity has pulled it below the muzzle by the time it arrives.
            Assert.Less(hit.point.y, -0.05f);
        }

        [Test]
        public void AStreamAimedAtNothingReportsNoHit()
        {
            Assert.IsFalse(SprayArc.Trace(new Vector3(0f, 500f, 0f), Vector3.up, Speed, 1f,
                                           Flight, ~0, out RaycastHit _, out float _));
        }

        /// <summary>
        /// A solved launch lands on the target, not near it.
        ///
        /// This is what a machine that does not own the gun aims its droplets with: it knows where
        /// the paint landed and nothing else, so the arc is recovered from the landing. Checked by
        /// flying it — sampling the parabola the solve claims and asking whether it passes through
        /// the point — because an angle that is merely plausible is exactly the failure this is for.
        /// </summary>
        [Test]
        public void ALaunchSolvedForAPointFliesThroughIt()
        {
            var origin = new Vector3(0f, 1.5f, 0f);

            foreach (Vector3 target in new[]
                     {
                         new Vector3(0f, 1.2f, 6f),      // across a room, roughly level
                         new Vector3(4f, 4.5f, 9f),      // high on a wall, off to one side
                         new Vector3(-3f, -2f, 2f),      // close and below, a floor at your feet
                     })
            {
                Assert.IsTrue(SprayArc.TryAimAt(origin, target, Speed, 1f, out Vector3 direction),
                              $"{target} is inside the hose's reach and was refused");

                // Finely enough that the residual is the solve's, not the sampling's: at 24 m/s
                // even a 5 ms step steps 12 cm along the arc.
                float best = float.PositiveInfinity;
                for (int i = 0; i <= 4000; i++)
                {
                    Vector3 point = SprayArc.Sample(origin, direction, Speed, 1f,
                                                     Flight * i / 4000f);
                    best = Mathf.Min(best, Vector3.Distance(point, target));
                }

                Assert.Less(best, 0.02f, $"the solved arc misses {target} by {best} m");
            }
        }

        /// <summary>
        /// The flat arc, not the lob. Both reach the target; only one is the shot the player took.
        /// </summary>
        [Test]
        public void ASolvedLaunchTakesTheFlatArc()
        {
            Assert.IsTrue(SprayArc.TryAimAt(Vector3.zero, new Vector3(0f, 0f, 8f), Speed, 1f,
                                             out Vector3 direction));

            float elevation = Mathf.Asin(Mathf.Clamp(direction.normalized.y, -1f, 1f))
                            * Mathf.Rad2Deg;

            Assert.Greater(elevation, 0f, "a level target still has to be thrown slightly up");
            Assert.Less(elevation, 45f, "the lob was chosen over the throw");
        }

        /// <summary>Out of reach is refused rather than answered with a wrong angle.</summary>
        [Test]
        public void APointBeyondTheHoseIsNotSolved()
        {
            Assert.IsFalse(SprayArc.TryAimAt(Vector3.zero,
                                              new Vector3(0f, 0f, SprayArc.BallisticRange(Speed, 1f) + 20f),
                                              Speed, 1f, out Vector3 _));
        }

        // ── The aperture stays on top of what it is painted on ───────────────

        [Test]
        public void APortalSprayedOverARiseIsPushedClearOfIt()
        {
            Portal portal = NewPortal(Vector3.zero, Quaternion.LookRotation(Vector3.back));
            portal.BeginStroke();
            for (int i = 0; i < 6; i++) portal.AddDab(new Vector2(i * 0.4f, 0f), 0.6f);

            // Placed at the WORLD position of the far end of the stroke, derived rather than
            // assumed: LookRotation puts local +X on world -X here, and hand-computing that is how
            // the first version of this test ended up putting the obstacle behind the probes.
            Vector3 farEnd = portal.transform.TransformPoint(2f, 0f, 0f);
            Vector3 outward = portal.transform.forward;

            var rise = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rise.transform.position = farEnd + outward * 0.4f;
            rise.transform.localScale = new Vector3(2f, 2f, 1f);
            spawned.Add(rise);

            Physics.SyncTransforms();

            Vector3 before = portal.transform.position;
            portal.ConformToSurface();

            Vector3 moved = portal.transform.position - before;

            Assert.Greater(Vector3.Dot(moved, outward), 0.85f,
                           "the aperture slid out along its normal to clear the rise");
            Assert.Less(Vector3.ProjectOnPlane(moved, outward).magnitude, 1e-3f,
                        "and moved along the normal ONLY — the lateral origin is what " +
                        "TransferFrom is built on");
        }

        [Test]
        public void APortalOnFlatGroundIsNotMoved()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 0f, 5f);
            wall.transform.localScale = new Vector3(20f, 20f, 0.5f);
            spawned.Add(wall);

            Physics.SyncTransforms();

            // Sitting just off the wall's near face, the way a placed aperture does.
            Portal portal = NewPortal(new Vector3(0f, 0f, 4.74f), Quaternion.LookRotation(Vector3.back));
            portal.BeginStroke();
            portal.AddDab(Vector2.zero, 0.6f);

            Vector3 before = portal.transform.position;
            portal.ConformToSurface();

            Assert.AreEqual(before, portal.transform.position,
                            "nothing pokes through a flat wall, so nothing moves");
        }

        [Test]
        public void AnUnsprayedApertureIsNeverConformed()
        {
            var rise = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rise.transform.position = new Vector3(0f, 0f, -0.4f);
            rise.transform.localScale = new Vector3(4f, 4f, 1f);
            spawned.Add(rise);

            Physics.SyncTransforms();

            // A hand-placed scene portal is an ellipse and is exactly where the designer put it.
            Portal portal = NewPortal(Vector3.zero, Quaternion.LookRotation(Vector3.back));

            Vector3 before = portal.transform.position;
            portal.ConformToSurface();

            Assert.AreEqual(before, portal.transform.position);
        }

        // ── Clamping an exit into the destination ─────────────────────────────

        [Test]
        public void APointAlreadyInsideIsNotMoved()
        {
            var stencil = new PortalStencil();
            stencil.AddDab(Vector2.zero, 1.2f);

            Vector2 clamped = stencil.ClampInside(new Vector2(0.2f, 0.1f), clearance: 0.3f);

            Assert.AreEqual(0.2f, clamped.x, 1e-4f);
            Assert.AreEqual(0.1f, clamped.y, 1e-4f);
        }

        [Test]
        public void APointOutsideIsPulledInsideWithClearance()
        {
            var stencil = new PortalStencil();
            stencil.AddDab(Vector2.zero, 1.2f);

            Vector2 clamped = stencil.ClampInside(new Vector2(4f, 0f), clearance: 0.3f);

            Assert.Less(clamped.magnitude, 0.95f, "inside the dab, minus the clearance");
            Assert.IsTrue(stencil.Contains(clamped), "and inside the shape");
            Assert.Greater(clamped.x, 0f, "pulled in along the direction it came from");
        }

        [Test]
        public void AnEllipseClampsTheSameWay()
        {
            var stencil = new PortalStencil();
            stencil.SetEllipse(new Vector2(4f, 2f));

            Vector2 clamped = stencil.ClampInside(new Vector2(0f, 5f), clearance: 0.2f);

            Assert.IsTrue(stencil.Contains(clamped));
            Assert.Less(clamped.y, 0.85f);
            Assert.Greater(clamped.y, 0f);
        }

        // ── Stroke arithmetic ─────────────────────────────────────────────────

        [Test]
        public void AShortStrokeIsOneDabAndALongOneIsSeveral()
        {
            Assert.AreEqual(1, PortalStencil.StrokeSteps(0.1f, 0.6f));
            Assert.AreEqual(1, PortalStencil.StrokeSteps(0.3f, 0.6f));
            Assert.Greater(PortalStencil.StrokeSteps(3f, 0.6f), 4);
            Assert.LessOrEqual(PortalStencil.StrokeSteps(50f, 0.6f), PortalStencil.MaxStrokeSteps);
        }

        // ── Portal delegates its shape to the stencil ─────────────────────────

        private Portal NewPortal(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("Portal");
            go.transform.SetPositionAndRotation(position, rotation);
            spawned.Add(go);

            var portal = go.AddComponent<Portal>();
            portal.SetSize(new Vector2(3.45f, 6.15f));
            return portal;
        }

        [Test]
        public void AnUnsprayedPortalStillAnswersLikeAnEllipse()
        {
            Portal portal = NewPortal(Vector3.zero, Quaternion.identity);

            Assert.IsTrue(portal.WithinAperture(new Vector3(1.6f, 0f, 0f)));
            Assert.IsFalse(portal.WithinAperture(new Vector3(1.8f, 0f, 0f)));
            Assert.AreEqual(3.45f, portal.Size.x, 1e-3f);
        }

        [Test]
        public void ADabMovesTheApertureOffTheTransformWithoutMovingTheTransform()
        {
            Portal portal = NewPortal(Vector3.zero, Quaternion.identity);
            portal.BeginStroke();
            portal.AddDab(new Vector2(2f, 0f), 0.6f);

            Assert.AreEqual(Vector3.zero, portal.transform.position,
                            "the origin is fixed by the first dab and never moves again");
            Assert.IsTrue(portal.WithinAperture(new Vector3(2f, 0f, 0f)), "the paint is the hole");
            Assert.IsFalse(portal.WithinAperture(Vector3.zero),
                           "and the origin need not be inside it");
        }

        /// <summary>
        /// Both quads carry the whole shape, and each material is told the span of its OWN quad.
        ///
        /// This is the invariant behind "the portal and its outline do not line up". Each shader
        /// rebuilds a metric position from its uv times _Extents, so a material handed anything
        /// other than half the mesh it is drawn on draws the entire outline at the wrong scale —
        /// and the two quads are deliberately different sizes, so there is no single number to
        /// fall back on. The rim's sheet also has to be the larger of the two, because the halo it
        /// draws is outside the opening by construction.
        /// </summary>
        [Test]
        public void EachQuadCarriesTheShapeAndItsMaterialKnowsItsOwnSpan()
        {
            Portal portal = NewPortal(Vector3.zero, Quaternion.identity);
            Renderer surface = AttachQuad(portal, "surfaceRenderer", "SpaceGame/Portal/PortalSurface");
            Renderer rim = AttachQuad(portal, "rimRenderer", "SpaceGame/Portal/PortalRim");

            // Instances the shape is actually pushed to, rather than the shared assets.
            portal.SetColour(Color.white);

            portal.BeginStroke();
            portal.AddStroke(new Vector2(-1.2f, -0.7f), new Vector2(1.2f, 0.7f), 6, 0.85f);

            foreach (Renderer renderer in new[] { surface, rim })
            {
                Vector3 scale = renderer.transform.localScale;
                Vector4 extents = renderer.sharedMaterial.GetVector("_Extents");

                Assert.AreEqual(scale.x * 0.5f, extents.x, 1e-4f,
                                $"{renderer.name}: told a different width than its mesh covers");
                Assert.AreEqual(scale.y * 0.5f, extents.y, 1e-4f,
                                $"{renderer.name}: told a different height than its mesh covers");

                Assert.Greater(scale.x, portal.Size.x, $"{renderer.name} clips the shape across");
                Assert.Greater(scale.y, portal.Size.y, $"{renderer.name} clips the shape along");
            }

            Assert.Greater(rim.transform.localScale.x, surface.transform.localScale.x,
                           "the halo spills outside the opening, so its sheet must be the larger");
        }

        /// <summary>A quad wearing one of the portal shaders, wired into a private field.</summary>
        private Renderer AttachQuad(Portal portal, string field, string shader)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = field;
            quad.transform.SetParent(portal.transform, false);

            Shader found = Shader.Find(shader);
            Assert.IsNotNull(found, $"shader missing from the project: {shader}");

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(found) { hideFlags = HideFlags.HideAndDontSave };

            var so = new UnityEditor.SerializedObject(portal);
            so.FindProperty(field).objectReferenceValue = renderer;
            so.ApplyModifiedPropertiesWithoutUndo();

            return renderer;
        }

        [Test]
        public void ThePortalsSizeFollowsTheSprayedBounds()
        {
            Portal portal = NewPortal(Vector3.zero, Quaternion.identity);
            portal.BeginStroke();
            portal.AddDab(Vector2.zero, 0.6f);

            float narrow = portal.Size.x;
            for (int i = 1; i < 5; i++) portal.AddDab(new Vector2(i * 0.4f, 0f), 0.6f);

            Assert.Greater(portal.Size.x, narrow + 1.2f, "the box grew along the stroke");
            Assert.AreEqual(narrow, portal.Size.y, 0.05f, "and not across it");
        }

        // ── An exit lands inside the far outline ──────────────────────────────

        [Test]
        public void ExitingThroughALobeTheFarSideDoesNotHaveLandsInsideIt()
        {
            Portal entry = NewPortal(Vector3.zero, Quaternion.identity);
            Portal exit = NewPortal(new Vector3(0f, 0f, 40f), Quaternion.identity);

            // Entry is a long horizontal stroke; the exit is a single round dab.
            entry.BeginStroke();
            for (int i = 0; i < 6; i++) entry.AddDab(new Vector2(i * 0.4f, 0f), 0.6f);

            exit.BeginStroke();
            exit.AddDab(Vector2.zero, 0.6f);

            Portal.Link(entry, exit);

            // A point out at the far end of the stroke, well outside the exit's single dab.
            Vector3 world = entry.transform.TransformPoint(2.0f, 0f, 0f);
            Vector3 landed = entry.ExitPointFor(world, clearance: 0.2f);

            Assert.IsTrue(exit.WithinAperture(landed),
                          "the exit point was pulled into the destination's own shape");
        }

        [Test]
        public void TwoMatchingAperturesAreNotMovedAtAll()
        {
            Portal entry = NewPortal(Vector3.zero, Quaternion.identity);
            Portal exit = NewPortal(new Vector3(0f, 0f, 40f), Quaternion.identity);
            Portal.Link(entry, exit);

            // Two ellipses of the same size: the clamp must be a no-op, or every unsprayed
            // traversal in the game moves by a few centimetres for no reason.
            Vector3 world = entry.transform.TransformPoint(1.2f, 2f, 0f);
            Vector3 landed = entry.ExitPointFor(world, clearance: 0.2f);
            Vector3 raw = exit.TransferFrom(entry).MultiplyPoint3x4(world);

            Assert.Less(Vector3.Distance(landed, raw), 1e-3f);
        }

        // ── Paint sticking, and refusing to ──────────────────────────────────

        [Test]
        public void PaintRefusesASurfaceMarkedNonPortalable()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 0f, 5f);
            wall.transform.localScale = new Vector3(10f, 10f, 0.5f);
            wall.AddComponent<NonPortalable>();
            spawned.Add(wall);

            Physics.SyncTransforms();

            Assert.IsTrue(Physics.Raycast(Vector3.zero, Vector3.forward, out RaycastHit info, 20f,
                                          ~0, QueryTriggerInteraction.Ignore),
                          "the ray reached the wall");

            Assert.IsFalse(PortalPlacement.FitDab(info, ~0, Vector3.forward,
                                                  out Vector3 _, out Quaternion _),
                           "paint does not stick to a NonPortalable surface");
        }

        [Test]
        public void PaintSticksToAnOrdinaryWallFacingTheShooter()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 0f, 5f);
            wall.transform.localScale = new Vector3(10f, 10f, 0.5f);
            spawned.Add(wall);

            Physics.SyncTransforms();
            Physics.Raycast(Vector3.zero, Vector3.forward, out RaycastHit info, 20f,
                            ~0, QueryTriggerInteraction.Ignore);

            Assert.IsTrue(PortalPlacement.FitDab(info, ~0, Vector3.forward,
                                                 out Vector3 position, out Quaternion rotation));

            Assert.Less(Vector3.Distance(position, info.point), 0.05f, "on the wall it hit");
            Assert.Greater(Vector3.Dot(rotation * Vector3.forward, Vector3.back), 0.99f,
                           "facing back out of the wall, towards the shooter");
        }

        // ── The spray session ────────────────────────────────────────────────

        private Portal SprayPrefab()
        {
            var go = new GameObject("Portal Prefab");
            go.SetActive(false);
            spawned.Add(go);
            return go.AddComponent<Portal>();
        }

        private PortalPair NewPair()
        {
            var player = new GameObject("Player");
            spawned.Add(player);
            return PortalPair.Of(player);
        }

        [Test]
        public void TheFirstDabOfASprayOpensTheAperture()
        {
            PortalPair pair = NewPair();
            Portal prefab = SprayPrefab();

            pair.BeginSpray(PortalPair.Primary, grow: false);
            Assert.IsNull(pair.Get(PortalPair.Primary), "nothing opens until paint lands");

            Portal portal = pair.LayDab(prefab, new Vector3(0f, 0f, 5f), Quaternion.identity,
                                        radius: 0.6f, steps: 1, colour: Color.red, lifetime: 20f,
                                        host: null);

            Assert.IsNotNull(portal);
            Assert.AreSame(portal, pair.Get(PortalPair.Primary));
            Assert.AreEqual(1, portal.Stencil.Count, "one blob landed");
            Assert.AreEqual(new Vector3(0f, 0f, 5f), portal.transform.position,
                            "the first blob fixed the origin");
        }

        [Test]
        public void LaterDabsGrowTheSameApertureWithoutMovingIt()
        {
            PortalPair pair = NewPair();
            Portal prefab = SprayPrefab();

            pair.BeginSpray(PortalPair.Primary, grow: false);
            Portal portal = pair.LayDab(prefab, new Vector3(0f, 0f, 5f), Quaternion.identity,
                                        0.6f, 1, Color.red, 20f, null);

            pair.LayDab(prefab, new Vector3(1.2f, 0f, 5f), Quaternion.identity,
                        0.6f, 3, Color.red, 20f, null);

            Assert.AreEqual(4, portal.Stencil.Count, "one blob, then three interpolated");
            Assert.AreEqual(new Vector3(0f, 0f, 5f), portal.transform.position, "still fixed");
            Assert.Greater(portal.Size.x, 1.6f, "and wider than a single blob");
        }

        [Test]
        public void PaintThatHasTurnedACornerIsRefused()
        {
            PortalPair pair = NewPair();
            Portal prefab = SprayPrefab();

            pair.BeginSpray(PortalPair.Primary, grow: false);
            Portal portal = pair.LayDab(prefab, new Vector3(0f, 0f, 5f), Quaternion.identity,
                                        0.6f, 1, Color.red, 20f, null);

            // Two metres off the aperture's plane — round a corner, not on this wall any more.
            pair.LayDab(prefab, new Vector3(1f, 0f, 7f), Quaternion.identity,
                        0.6f, 1, Color.red, 20f, null);

            Assert.AreEqual(1, portal.Stencil.Count, "the off-plane blob was refused");
        }

        [Test]
        public void SprayingBesideYourOwnApertureGrowsItInsteadOfOpeningTheOther()
        {
            PortalPair pair = NewPair();
            Portal prefab = SprayPrefab();

            pair.BeginSpray(PortalPair.Primary, grow: false);
            pair.LayDab(prefab, new Vector3(0f, 0f, 5f), Quaternion.identity,
                        0.6f, 1, Color.red, 20f, null);
            pair.EndSpray();
            pair.CommitBarrel(PortalPair.Primary);

            Assert.AreEqual(PortalPair.Primary,
                            pair.ChooseSprayBarrel(new Vector3(0.4f, 0f, 5f), 0.5f, out bool grow),
                            "aiming at your own paint");
            Assert.IsTrue(grow, "which is a top-up, not a new portal");

            Assert.AreEqual(PortalPair.Secondary,
                            pair.ChooseSprayBarrel(new Vector3(30f, 0f, 5f), 0.5f, out bool fresh),
                            "aiming at bare wall across the room");
            Assert.IsFalse(fresh, "which is the other barrel");
        }

        // ── The gun ──────────────────────────────────────────────────────────

        [Test]
        public void TheGunIsContinuousAndDoesNotSelfSustain()
        {
            var go = new GameObject("Portal Gun");
            spawned.Add(go);

            var gun = go.AddComponent<PortalGunItem>();

            Assert.IsTrue(gun.IsContinuous, "the spray is a hold, not a click");
            Assert.IsFalse(gun.WantsHold, "and it ends when the finger comes up");
        }

        [Test]
        public void ADryBarrelRefusesAndTheGaugeClampsAtEmpty()
        {
            var go = new GameObject("Portal Gun");
            spawned.Add(go);

            var gun = go.AddComponent<PortalGunItem>();

            Assert.IsTrue(gun.CanSpend(PortalPair.Primary, 1), "a full tank pays for a blob");

            // Spend is unconditional — it is the peer-side debit for dabs the OWNER already paid
            // for — so a machine whose gauge drifted low clamps at empty instead of going negative.
            for (int i = 0; i < 200; i++) gun.Spend(PortalPair.Primary, 1);

            Assert.IsFalse(gun.CanSpend(PortalPair.Primary, 1), "an empty tank refuses");
            Assert.AreEqual(0f, gun.ChargeOf(PortalPair.Primary), 1e-3f, "and never reads negative");
            Assert.AreEqual(1f, gun.ChargeOf(PortalPair.Secondary), 1e-3f,
                            "draining one barrel leaves the other alone");
        }

        // ── Save and load ────────────────────────────────────────────────────

        [Test]
        public void ASprayedShapeSurvivesACaptureAndRestore()
        {
            PortalPair pair = NewPair();
            Portal prefab = SprayPrefab();

            pair.BeginSpray(PortalPair.Primary, grow: false);
            Portal portal = pair.LayDab(prefab, new Vector3(0f, 0f, 5f), Quaternion.identity,
                                        0.6f, 1, Color.red, 20f, null);
            pair.LayDab(prefab, new Vector3(1.2f, 0f, 5f), Quaternion.identity,
                        0.6f, 3, Color.red, 20f, null);
            pair.EndSpray();

            Vector3[] captured = PortalPairSaveable.DescribeDabs(portal);
            Assert.AreEqual(4, captured.Length, "every blob was written down");

            Portal restored = NewPortal(new Vector3(0f, 0f, 5f), Quaternion.identity);
            PortalPairSaveable.ApplyDabs(restored, captured);

            Assert.AreEqual(portal.Stencil.Count, restored.Stencil.Count);
            Assert.AreEqual(portal.Size.x, restored.Size.x, 1e-3f);
            Assert.IsTrue(restored.WithinAperture(portal.transform.TransformPoint(1.2f, 0f, 0f)));
        }

        [Test]
        public void ARecordWithNoDabsComesBackAsAnEllipse()
        {
            Portal restored = NewPortal(Vector3.zero, Quaternion.identity);

            PortalPairSaveable.ApplyDabs(restored, null);

            Assert.IsTrue(restored.Stencil.IsEllipse, "an old save is still an ellipse");
            Assert.AreEqual(3.45f, restored.Size.x, 1e-3f);
        }
    }
}
