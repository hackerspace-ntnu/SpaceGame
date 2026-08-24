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

        // ── Paint pools where it lands, and widens the hole ──────────────────

        [Test]
        public void HoldingTheStreamStillWidensTheHoleInsteadOfStackingCircles()
        {
            var stencil = new PortalStencil();
            stencil.AddDab(Vector2.zero, 0.6f);

            float first = stencil.InscribedRadius;

            // The same spot, ten more times — a player holding the nozzle still.
            for (int i = 0; i < 10; i++) stencil.AddDab(Vector2.zero, 0.6f);

            Assert.AreEqual(1, stencil.Count, "pooled into one blob, not stacked as eleven");
            Assert.Greater(stencil.InscribedRadius, first * 1.5f, "and the hole actually widened");
        }

        [Test]
        public void PoolingStopsAtTheCapSoOneSpotCannotEatTheWall()
        {
            var stencil = new PortalStencil();
            for (int i = 0; i < 200; i++) stencil.AddDab(Vector2.zero, 0.6f);

            Assert.Less(stencil.InscribedRadius, 0.6f * 3f,
                        "paint stops spreading; sweeping is what makes a big portal");
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

        [Test]
        public void TheStreamFallsUnderGravity()
        {
            Vector3 level = PortalJet.Sample(Vector3.zero, Vector3.forward, 13f, 1f, 0.8f);

            Assert.Less(level.y, -1f, "half a second out, the stream has visibly dropped");
            Assert.Greater(level.z, 5f, "and is still going forward");
        }

        [Test]
        public void TheStreamDoesNotReachAcrossARoom()
        {
            // Held level, a 13 m/s stream from chest height is on the floor well inside 30 m — the
            // range the hitscan version used to place a portal at.
            Assert.Less(PortalJet.BallisticRange(13f, 1f), 20f);
        }

        [Test]
        public void TheStreamStopsAtTheFirstThingItHits()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 0f, 6f);
            wall.transform.localScale = new Vector3(20f, 20f, 0.5f);
            spawned.Add(wall);

            Physics.SyncTransforms();

            Assert.IsTrue(PortalJet.Trace(Vector3.zero, Vector3.forward, 13f, 1f, 1.6f, ~0,
                                          out RaycastHit hit, out float flight));

            Assert.Less(Mathf.Abs(hit.point.z - 5.75f), 0.3f, "stopped at the wall's near face");
            Assert.Greater(flight, 0f, "and took time to get there");
            Assert.Less(flight, 1.6f, "well inside its flight budget");

            // Gravity has pulled it below the muzzle by the time it arrives.
            Assert.Less(hit.point.y, -0.05f);
        }

        [Test]
        public void AStreamAimedAtNothingReportsNoHit()
        {
            Assert.IsFalse(PortalJet.Trace(new Vector3(0f, 500f, 0f), Vector3.up, 13f, 1f, 1.6f, ~0,
                                           out RaycastHit _, out float _));
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
        public void ADryBarrelLaysNoPaint()
        {
            var go = new GameObject("Portal Gun");
            spawned.Add(go);

            var gun = go.AddComponent<PortalGunItem>();

            Assert.IsTrue(gun.TrySpend(PortalPair.Primary, 1), "a full tank pays for a blob");

            for (int i = 0; i < 200; i++) gun.TrySpend(PortalPair.Primary, 1);

            Assert.IsFalse(gun.TrySpend(PortalPair.Primary, 1), "and an empty one does not");
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
