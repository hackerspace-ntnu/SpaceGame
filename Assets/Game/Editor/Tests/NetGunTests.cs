// What the net has to keep being true about itself.
//
// The failures this file exists to catch are the silent ones. A lattice with no shear resistance
// still simulates, still draws, and collapses into a line the first time it touches anything — no
// error, no warning, just a net that is visibly a bundle of parallel strings. A constraint solver
// that splits corrections 50/50 regardless of mass still converges; it just never blooms, because
// the rim weights that are supposed to drag the hem outward weigh exactly as much as the mesh they
// are dragging. Those two are not pinned here yet; they arrive with the passes that introduce them.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class NetGunTests
    {
        private const int NodesPerSide = 9;
        private const float HalfWidth = 3f;

        /// <summary>How far past rest a strand may sit before it is stretch rather than residual.</summary>
        private const float SolverResidual = 1.05f;

        private const float Substep = 1f / 90f;
        private const int SettleSteps = 240;

        /// <summary>Metres of diagonal opening treated as float noise rather than as a push.</summary>
        private const float OpeningTolerance = 0.001f;

        /// <summary>
        /// Pinned here rather than left to the Inspector, so authoring a looser net cannot quietly
        /// move the bar these tests measure against.
        /// </summary>
        private const float LatticeShearLimit = 1.30f;

        /// <summary>Rest spacing of a fully open net, from the geometry these tests hand Deploy.</summary>
        private const float OpenSpacing = 2f * HalfWidth / (NodesPerSide - 1);

        /// <summary>One second of substeps, comfortably past any sane unfurl.</summary>
        private const int UnfurlSteps = 90;

        /// <summary>
        /// How much of its flat open span a net in free fall actually reaches.
        ///
        /// Not 1: a net falling through air flutters and folds, so corner-to-corner never equals
        /// the span of a perfectly flat sheet. The correct solver peaks around 75% a second after
        /// the muzzle; a net whose unfurl never runs sits near 3%, and one bloomed into pos rather
        /// than prev reaches 45%. The bar goes between them.
        /// </summary>
        private const float OpenFraction = 0.65f;

        /// <summary>Substeps for a net to fall four metres, land on a captive and settle.</summary>
        private const int DrapeSteps = 400;

        private const float GroundHeight = 0f;

        /// <summary>Metres under the floor a settled hem may sit — float noise on the clamp, no more.</summary>
        private const float GroundTolerance = 0.01f;

        private const float PoolSeconds = 30f;

        /// <summary>Well past the longest hold any of these loads produces, so a stuck pool ends.</summary>
        private const int PoolTickCeiling = 60 * 300;

        /// <summary>A creature's authored NavMeshAgent speed, for the hobble-restore test.</summary>
        private const float AuthoredSpeed = 5f;

        /// <summary>Cord thickness the mesh tests build with, metres.</summary>
        private const float CordWidth = 0.03f;

        /// <summary>Comfortably past the longest possible flight, so a net that never lands ends.</summary>
        private const int FlightStepCeiling = 600;

        private readonly System.Collections.Generic.List<GameObject> spawned =
            new System.Collections.Generic.List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        private GameObject NewCreature(string name)
        {
            GameObject go = NewObject(name);
            go.AddComponent<CapsuleCollider>();
            return go;
        }

        /// <summary>
        /// A gun with a real magazine.
        ///
        /// UsableItem.maxUses is private and defaults to -1, meaning UNLIMITED — and an unlimited
        /// gun has no charges to spend and none to refill, so TickRecharge returns immediately and
        /// SpendAllChargesForTest loops zero times. A bare AddComponent would let both ammo tests
        /// below pass while testing nothing at all, so the field is authored here the way the
        /// prefab authors it.
        /// </summary>
        private NetGunArtifact NewGun(int charges)
        {
            GameObject go = NewObject("NetGun");
            var gun = go.AddComponent<NetGunArtifact>();

            var serialized = new UnityEditor.SerializedObject(gun);
            UnityEditor.SerializedProperty max = serialized.FindProperty("maxUses");

            Assert.IsNotNull(max,
                "UsableItem.maxUses is gone or renamed — these tests can no longer author a magazine.");

            max.intValue = charges;
            serialized.ApplyModifiedProperties();

            Assert.AreEqual(charges, gun.ChargesRemaining,
                "The magazine did not take, so the ammo tests below would prove nothing.");

            return gun;
        }

        /// <summary>How many 60 Hz ticks a fresh net survives under a steady struggling mass.</summary>
        private static int TicksToTear(float strugglingMass)
        {
            var pool = new SnareIntegrity();
            pool.Reset(PoolSeconds);

            int ticks = 0;
            while (!pool.IsSpent && ticks < PoolTickCeiling)
            {
                pool.Drain(strugglingMass, 1f / 60f);
                ticks++;
            }

            return ticks;
        }

        private static SnareLattice NewLattice()
        {
            var lattice = new SnareLattice();
            lattice.ConfigureForTest(NodesPerSide, rimMassMultiplier: 6f, shearLimit: LatticeShearLimit);
            return lattice;
        }

        /// <summary>
        /// Run the net out to its open size.
        ///
        /// <para>
        /// A lattice is born bundled and grows into itself, so a test that Deploys and measures
        /// straight away is measuring a net the size of a fist. Every test below except the bloom
        /// is about a net that has already opened, so they open it first.
        /// </para>
        /// <para>
        /// The assertion here is not redundant with the tests that follow. Those measure against
        /// RestSpacing — whatever the unfurl happened to drive it to — which is self-consistent by
        /// construction and just as happy with a net that stopped growing half way. This is the one
        /// place the live value is checked against the geometry the caller actually asked for.
        /// </para>
        /// </summary>
        private static void Unfurl(SnareLattice lattice)
        {
            for (int i = 0; i < UnfurlSteps; i++) lattice.Step(Substep);

            AssertFullyUnfurled(lattice);
        }

        /// <summary>
        /// The net finished growing. Split out of <see cref="Unfurl"/> because the drape test
        /// cannot use it — that one has to keep collision on while the net opens — but needs
        /// exactly the same guarantee before it measures anything.
        /// </summary>
        private static void AssertFullyUnfurled(SnareLattice lattice)
        {
            Assert.That(lattice.RestSpacing, Is.EqualTo(OpenSpacing).Within(1e-4f),
                $"The net is still at {lattice.RestSpacing:F4} m spacing against an open " +
                $"{OpenSpacing:F4} m — either unfurlSeconds is authored longer than these tests " +
                "allow for, or the unfurl never completes.");
        }

        /// <summary>One strand is no longer than its rest length, give or take solver residual.</summary>
        private static void AssertStrand(SnareLattice lattice, float rest,
                                         int rowA, int colA, int rowB, int colB)
        {
            float length = Vector3.Distance(lattice.NodeAt(rowA, colA), lattice.NodeAt(rowB, colB));

            Assert.That(length, Is.LessThanOrEqualTo(rest * SolverResidual),
                $"Strand ({rowA},{colA})-({rowB},{colB}) stretched to {length:F3} m, past the " +
                $"{rest * SolverResidual:F3} m limit (rest {rest:F3} m).");
        }

        /// <summary>One cell diagonal has not racked past the angle at which the mesh locks.</summary>
        private static void AssertDiagonal(SnareLattice lattice, float locked,
                                           int rowA, int colA, int rowB, int colB)
        {
            float diagonal = Vector3.Distance(lattice.NodeAt(rowA, colA), lattice.NodeAt(rowB, colB));

            Assert.That(diagonal, Is.LessThanOrEqualTo(locked),
                $"Cell diagonal ({rowA},{colA})-({rowB},{colB}) racked out to {diagonal:F3} m, past " +
                $"the {locked:F3} m lock — the mesh never locked.");
        }

        // ── SnareLattice: strands ──────────────────────────────────────────────

        [Test]
        public void StrandsDoNotStretchUnderGravity()
        {
            // The defining property of cord. A mass-spring net that stretches is a trampoline, and
            // the drape over a captive turns into a slow sag that never settles.
            //
            // One corner is dragged well out of place first, because a lattice left alone does not
            // test this at all: with nothing pinned and gravity uniform, every node falls at the
            // same rate, every strand stays at exactly its rest length, and the solver is handed
            // zero error to correct. That version passed with the constraint pass deleted.
            SnareLattice lattice = NewLattice();
            lattice.Deploy(Vector3.up * 10f, Vector3.forward, HalfWidth);
            Unfurl(lattice);

            lattice.SetNodeForTest(0, 0, lattice.NodeAt(0, 0) + new Vector3(12f, 6f, 0f));

            for (int i = 0; i < SettleSteps; i++) lattice.Step(Substep);

            float rest = lattice.RestSpacing;

            // BOTH directions. Measuring only the row strands leaves the column pass of the solver
            // completely untested: delete it and vertical strands stretch to better than six times
            // their rest length while a row-only assertion still passes.
            for (int line = 0; line < NodesPerSide; line++)
            for (int offset = 0; offset < NodesPerSide - 1; offset++)
            {
                AssertStrand(lattice, rest, line, offset, line, offset + 1);   // along a row
                AssertStrand(lattice, rest, offset, line, offset + 1, line);   // down a column
            }
        }

        // ── SnareLattice: shear ────────────────────────────────────────────────

        [Test]
        public void ShearedLatticeLocksInsteadOfCollapsing()
        {
            // Without diagonals a square grid has NOTHING to say about the angle at a corner: a
            // lattice racked flat into a line satisfies every four-neighbour distance constraint
            // exactly, so the solver has no reason to undo it. That is a bundle of parallel
            // strings, and it is what a net looks like when this test is missing.
            SnareLattice lattice = NewLattice();
            lattice.Deploy(Vector3.zero, Vector3.up, HalfWidth);
            Unfurl(lattice);

            // Rack every row sideways in proportion to its distance from the centre.
            lattice.RackForTest(shearMetres: 4f);

            for (int i = 0; i < SettleSteps; i++) lattice.Step(Substep);

            float diagonalRest = lattice.RestSpacing * Mathf.Sqrt(2f);
            float locked = diagonalRest * lattice.ShearLimit * SolverResidual;

            // A cell of side s cannot physically exceed a 2s diagonal — that IS a flat cell, the
            // fully collapsed trellis this test exists to catch. So if the authored shear limit
            // puts the bar above 2s, the loop below cannot fail however broken the solver is, and
            // would report green while checking nothing. shearLimit's own Range reaches 1.41 and
            // anything past roughly 1.347 disarms this test, so the trap is one Inspector drag away.
            Assert.That(locked, Is.LessThan(2f * lattice.RestSpacing),
                "shearLimit is loose enough that the lock bar exceeds a flat cell, so this test " +
                "can no longer fail. Lower shearLimit or this assertion is checking nothing.");

            // BOTH diagonals of every cell, for the same reason the strand test measures both
            // directions. Racking grows one diagonal and shrinks the other, and WHICH one grows
            // depends on the sign of the shove — so an assertion that names only one of them is
            // hostage to that sign, and silently stops testing anything if it ever flips.
            for (int row = 0; row < NodesPerSide - 1; row++)
            for (int col = 0; col < NodesPerSide - 1; col++)
            {
                AssertDiagonal(lattice, locked, row, col, row + 1, col + 1);
                AssertDiagonal(lattice, locked, row, col + 1, row + 1, col);
            }
        }

        [Test]
        public void DiagonalsNeverPushApart()
        {
            // A net conforms to any shape because its cells are free to CLOSE. A two-sided diagonal
            // constraint holds the cell square, which turns the net into a rigid sheet that tents
            // over a captive instead of wrapping it. So the diagonal is a maximum, not a length.
            SnareLattice lattice = NewLattice();
            lattice.Deploy(Vector3.zero, Vector3.up, HalfWidth);
            Unfurl(lattice);

            // Squeeze one cell's diagonal well below rest and confirm nothing pushes it back out.
            lattice.SetNodeForTest(0, 0, lattice.NodeAt(1, 1));

            Vector3 before = lattice.NodeAt(0, 0);
            lattice.Step(Substep);
            Vector3 after = lattice.NodeAt(0, 0);

            float openedBy = Vector3.Distance(after, lattice.NodeAt(1, 1))
                           - Vector3.Distance(before, lattice.NodeAt(1, 1));

            Assert.That(openedBy, Is.LessThanOrEqualTo(OpeningTolerance),
                "A slack diagonal pushed the cell back open — the net is behaving as a sheet.");
        }

        // ── SnareLattice: the weighted hem ───────────────────────────────────

        [Test]
        public void HeavyRimMakesTheMeshYieldInstead()
        {
            // The bloom and the purse both come from this ratio and nothing else. Split corrections
            // 50/50 and the hem weighs the same as the mesh, so there is nothing to carry the skirt
            // outward in flight or past a target on impact — the net falls as a flat square.
            //
            // Measured as the SAME node under two different hem weights, never as one node against
            // its neighbour. Over a whole Step a node's movement is dominated by its entire
            // neighbourhood rather than by the one constraint under test: a lifted rim node is
            // hauled straight back by the two rim-to-rim strands either side of it, and those split
            // 50/50 however heavy the hem is. Comparing it against the mesh node below measures
            // that neighbourhood and reports the rim moving FURTHER, which says nothing about mass.
            float heavyHem = MeshYieldAfterLiftingRim(rimMassMultiplier: 6f);
            float evenWeights = MeshYieldAfterLiftingRim(rimMassMultiplier: 1f);

            Assert.That(heavyHem, Is.GreaterThan(evenWeights * 1.5f),
                $"The mesh node yielded {heavyHem:F3} m against a heavy hem and {evenWeights:F3} m " +
                "against even weights. The correction is not being split by inverse mass, so the " +
                "hem has no weight to drag the mesh with.");
        }

        /// <summary>
        /// Lift one rim node clear of the plane and report how far the mesh node beneath it is
        /// dragged in a single substep. The heavier the hem, the more of the shared correction the
        /// mesh node has to absorb — which is the whole of what the inverse-mass split buys.
        /// </summary>
        private static float MeshYieldAfterLiftingRim(float rimMassMultiplier)
        {
            var lattice = new SnareLattice();
            lattice.ConfigureForTest(NodesPerSide, rimMassMultiplier, LatticeShearLimit);
            lattice.Deploy(Vector3.zero, Vector3.up, HalfWidth);
            Unfurl(lattice);

            const int Row = 0;             // rim row
            const int Col = 4;             // mid-span, so both neighbours are ordinary mesh

            Vector3 meshBefore = lattice.NodeAt(Row + 1, Col);

            lattice.SetNodeForTest(Row, Col, lattice.NodeAt(Row, Col) + Vector3.up * 2f);
            lattice.Step(Substep);

            return Vector3.Distance(lattice.NodeAt(Row + 1, Col), meshBefore);
        }

        // ── SnareLattice: the unfurl ─────────────────────────────────────────

        [Test]
        public void DeployBloomsTheHemOutward()
        {
            // A thrown net opens because it is let out, not because an animation says so. The rest
            // length is what travels — a lattice whose strands are already at rest cannot be pushed
            // open, and trying it either explodes the net (laid out compressed) or racks the mesh
            // up and makes it SMALLER (laid out full size). The bloom's own job is the momentum
            // that makes it lead with its hem.
            //
            // Deployed face-up, because a net falling on edge folds along its own plane and the
            // span then measures the fold rather than the opening.
            SnareLattice lattice = NewLattice();
            lattice.Deploy(Vector3.up * 20f, Vector3.up, HalfWidth);

            lattice.Bloom();
            for (int i = 0; i < UnfurlSteps; i++) lattice.Step(Substep);

            // Corner to corner of a flat open net. The bar is a fraction of the GEOMETRY rather
            // than a multiple of the bundled start: the start is a fist-sized net whose span the
            // solver can beat without ever opening, and a bar built on it once sat above the
            // largest span the strands could physically reach.
            float openSpan = 2f * HalfWidth * Mathf.Sqrt(2f);
            float span = lattice.SpanForTest();

            Assert.That(span, Is.GreaterThan(openSpan * OpenFraction),
                $"The net reached {span:F2} m corner to corner against an open {openSpan:F2} m — " +
                $"under the {openSpan * OpenFraction:F2} m this has to clear, so it never opened.");
        }

        // ── SnareDrape ───────────────────────────────────────────────────────

        [Test]
        public void NetDrapesOverACaptiveInsteadOfThroughIt()
        {
            // The whole visual payoff. A net that ignores the body under it settles into a flat
            // square on the ground with the animal standing through it.
            //
            // Collision runs from the first substep rather than after an Unfurl(), because the net
            // is dropped from 4 m and a second of unopposed free fall would carry it through both
            // the captive and the floor before the drape loop started. It would still finish in
            // roughly the right place — the ground clamp hauls it back up — but it would be
            // measuring a net teleported out of the floor rather than one that landed on a
            // shoulder.
            SnareLattice lattice = NewLattice();
            lattice.Deploy(Vector3.up * 4f, Vector3.up, HalfWidth);

            var drape = new SnareDrape();
            var obstacle = new SnareDrape.Capsule
            {
                Bottom = Vector3.zero,
                Top = Vector3.up * 1.8f,
                Radius = 0.6f,
            };

            for (int i = 0; i < DrapeSteps; i++)
            {
                lattice.Step(Substep);
                drape.Resolve(lattice, new[] { obstacle }, GroundHeight);
            }

            AssertFullyUnfurled(lattice);

            // The centre of the net must be held up by the shoulder, not lying on the floor.
            Assert.That(lattice.NodeAt(NodesPerSide / 2, NodesPerSide / 2).y, Is.GreaterThan(1f),
                "The net fell through the captive instead of draping over it.");

            // And the hem must be ON the floor around it. Bounded below as well as above, because
            // a hem that has fallen to seventeen metres underground satisfies "it came down" every
            // bit as well as one lying on the ground does — a one-sided bar here cannot tell a
            // drape from the ground clamp having been deleted outright.
            Assert.That(lattice.NodeAt(0, 0).y,
                Is.InRange(GroundHeight - GroundTolerance, 0.5f),
                "The net is tenting rather than wrapping — the hem never came down.");
        }

        [Test]
        public void ANodeOnTheSpineIsPushedClearSideways()
        {
            // The degenerate case: a node with no direction of its own to be pushed along. Pushing
            // it along Vector3.up slides it UP an upright capsule's own axis and leaves it exactly
            // as deep inside as it started, so the branch written to rescue this node is the one
            // case it fails. Nothing else here can catch that — across a 400-substep drape no node
            // ever lands within a hundredth of a millimetre of the axis — so it is posed directly.
            SnareLattice lattice = NewLattice();
            lattice.Deploy(Vector3.up * 4f, Vector3.up, HalfWidth);

            var obstacle = new SnareDrape.Capsule
            {
                Bottom = Vector3.zero,
                Top = Vector3.up * 1.8f,
                Radius = 0.6f,
            };

            const int MidRow = 4;
            const int MidColumn = 4;
            lattice.SetNodeForTest(MidRow, MidColumn, Vector3.up * 0.9f);   // dead on the centre line

            new SnareDrape().Resolve(lattice, new[] { obstacle }, GroundHeight);

            // The capsule stands upright through the origin, so distance from its axis is simply
            // how far the node ends up from the world Y line.
            Vector3 pushed = lattice.NodeAt(MidRow, MidColumn);
            float fromAxis = new Vector2(pushed.x, pushed.z).magnitude;

            Assert.That(fromAxis, Is.GreaterThanOrEqualTo(obstacle.Radius - 1e-3f),
                $"A node on the capsule's axis ended {fromAxis:F4} m from it against a " +
                $"{obstacle.Radius:F2} m radius — it was pushed along the axis, not off it.");
        }

        // ── SnareIntegrity ───────────────────────────────────────────────────

        [Test]
        public void AnEmptyNetRotsButDoesNotTearEarly()
        {
            var pool = new SnareIntegrity();
            pool.Reset(holdSeconds: PoolSeconds);

            for (int i = 0; i < 29 * 60; i++) pool.Drain(0f, 1f / 60f);

            Assert.IsFalse(pool.IsSpent, "A net with nothing in it tore before its time was up.");

            // Bounded from below as well. A pool that never drains at all survives 29 seconds just
            // as happily, so the check above on its own cannot tell a slow rot from no rot — and a
            // net that never rots is one that lies on the sand for the rest of the session.
            Assert.That(pool.Fraction, Is.LessThan(1f),
                "An empty net did not decay at all.");
        }

        [Test]
        public void ThreeCaptivesTearOutSoonerThanOne()
        {
            // The tradeoff that stops a 6 m net dominating a careful single shot.
            int alone = TicksToTear(SnareIntegrity.ReferenceLoad);
            int crowd = TicksToTear(SnareIntegrity.ReferenceLoad * 3f);

            // Bounded both ways. "Sooner" alone passes on a single tick's difference, which is not
            // a tradeoff any player could feel; and it passes just as well if three captives tear
            // the net instantly, which is a different failure with the same sign.
            Assert.That(crowd, Is.LessThan(alone / 2),
                $"Three captives took {crowd} ticks against one captive's {alone} — not a " +
                "difference a player would notice.");
            Assert.That(crowd, Is.GreaterThan(alone / 10),
                $"Three captives tore the net out in {crowd} ticks — it may as well not have " +
                "caught them at all.");
        }

        [Test]
        public void APowerfulCaptiveTearsOutEarly()
        {
            int heavy = TicksToTear(900f);
            int ordinary = TicksToTear(SnareIntegrity.ReferenceLoad);

            Assert.That(heavy, Is.LessThan(ordinary),
                $"A 900 kg captive took {heavy} ticks against an ordinary one's {ordinary} — " +
                "weight is not costing the net anything.");
            Assert.That(heavy, Is.GreaterThan(60),
                $"A heavy captive tore the net out in {heavy} ticks — under a second, so the net " +
                "never caught it at all.");
        }

        [Test]
        public void AnOrdinaryCaptiveIsHeldForTheAuthoredTime()
        {
            // The authored number has to mean what it says, or a designer tuning "how long does a
            // net hold something" is tuning something else. Drain taking the GREATER of idle rot
            // and load rather than their sum is what keeps this true: summing them held an ordinary
            // captive for exactly half the rating.
            int ticks = TicksToTear(SnareIntegrity.ReferenceLoad);
            float seconds = ticks / 60f;

            Assert.That(seconds, Is.EqualTo(PoolSeconds).Within(0.5f),
                $"A net rated {PoolSeconds:F0} s held one ordinary captive for {seconds:F1} s.");
        }

        // ── SnareTether ──────────────────────────────────────────────────────

        [Test]
        public void ANettedCreatureMayShuffleButNotLeave()
        {
            // "Hobbled, not frozen". A captive pinned rigidly in place reads as a dead animation;
            // one that can walk out from under the net is not caught at all.
            GameObject creature = NewCreature("Creature");
            GameObject anchor = NewObject("Anchor");

            SnareTether tether = SnareTether.Ensure(creature);
            tether.Bind(anchor.transform, new SnareStruggle());

            // Try to walk 20 m away, one step at a time.
            for (int i = 0; i < 200; i++)
            {
                creature.transform.position += Vector3.forward * 0.1f;
                tether.Step(1f / 60f);
            }

            float strayed = Vector3.Distance(creature.transform.position, anchor.transform.position);

            Assert.That(strayed, Is.LessThan(2f),
                $"The creature walked {strayed:F1} m from the net.");
            Assert.That(strayed, Is.GreaterThan(0.05f),
                "The creature is pinned rigidly in place rather than hobbled.");
        }

        [Test]
        public void ASecondNetCannotStealABoundCreature()
        {
            // One net at a time, for the reason LassoTether.Bind documents: two nets sharing one
            // tether means whichever expires first frees the captive from both.
            GameObject creature = NewCreature("Creature");
            GameObject first = NewObject("First");
            GameObject second = NewObject("Second");

            SnareTether tether = SnareTether.Ensure(creature);

            Assert.IsTrue(tether.Bind(first.transform, new SnareStruggle()));
            Assert.IsFalse(tether.Bind(second.transform, new SnareStruggle()),
                "A second net took a creature that was already caught.");
        }

        [Test]
        public void ReleasingByTheWrongNetDoesNothing()
        {
            GameObject creature = NewCreature("Creature");
            GameObject first = NewObject("First");
            GameObject second = NewObject("Second");

            SnareTether tether = SnareTether.Ensure(creature);
            tether.Bind(first.transform, new SnareStruggle());

            tether.Release(second.transform);

            Assert.IsTrue(tether.IsBound,
                "An unrelated net's expiry freed a creature it never caught.");
        }

        [Test]
        public void ANetNeverLeavesACreaturePermanentlySlow()
        {
            // The persistence hazard the whole design is built around. NavMeshAgent.speed is a
            // SERIALIZED field, so a hobble that outlives the net is captured by a quit-time
            // autosave, and the world reloads with a creature that cannot move and nothing in the
            // log to say why — the class of failure LassoTether's header describes paying for once.
            GameObject creature = NewCreature("Creature");
            var agent = creature.AddComponent<NavMeshAgent>();

            // Disabled deliberately: nothing here needs it pathing, and an enabled agent with no
            // NavMesh under it complains. The speed property is readable and writable regardless,
            // which is all the hobble touches.
            agent.enabled = false;
            agent.speed = AuthoredSpeed;

            GameObject net = NewObject("Net");
            SnareTether tether = SnareTether.Ensure(creature);

            tether.Bind(net.transform, new SnareStruggle());

            Assert.That(agent.speed, Is.LessThan(AuthoredSpeed),
                "The net did not slow the creature down at all.");

            // Bound a second time by the same net, which is what a re-catch looks like. The cap
            // must not record the already-hobbled speed as the authored one, or every re-catch
            // ratchets the creature slower with no way back.
            tether.Bind(net.transform, new SnareStruggle());
            tether.Release(net.transform);

            Assert.That(agent.speed, Is.EqualTo(AuthoredSpeed).Within(1e-3f),
                $"The creature was left at {agent.speed:F2} against an authored {AuthoredSpeed:F2}.");
        }

        // ── SnaredBody ───────────────────────────────────────────────────────

        [Test]
        public void ANetNeverGivesAPlayerSpeed()
        {
            // A net may take a player's speed away and it may drag them; it may never give them
            // speed, or a well-timed catch is a launch. Same rule LeashEnd.Restrain and
            // LassoedBody.Step both state, and the leash rework's finding that a rope must never
            // become a way to get around.
            //
            // Swept over directions rather than tested once outward, because the branch that could
            // break the rule is the one where the radial and the velocity disagree — a player
            // running along the net's edge, or an anchor moving toward them.
            Vector3[] velocities =
            {
                new Vector3(9f, 0f, 0f),      // straight out
                new Vector3(-9f, 0f, 0f),     // straight back in
                new Vector3(0f, 0f, 9f),      // square across
                new Vector3(6f, 4f, -6f),     // oblique, with a vertical component
            };

            foreach (Vector3 velocity in velocities)
            {
                GameObject player = NewObject("Player");
                Rigidbody rb = player.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.isKinematic = false;

                GameObject anchor = NewObject("Anchor");

                SnaredBody snared = SnaredBody.Ensure(player);
                snared.Bind(anchor.transform, new SnareStruggle());

                // Standing well outside the shuffle radius.
                rb.position = new Vector3(6f, 0f, 0f);
                rb.linearVelocity = velocity;

                float before = rb.linearVelocity.magnitude;
                snared.Step();
                float after = rb.linearVelocity.magnitude;

                Assert.That(after, Is.LessThanOrEqualTo(before + 1e-3f),
                    $"The net accelerated the player from {before:F2} to {after:F2} m/s while " +
                    $"moving {velocity}.");
            }
        }

        [Test]
        public void ANetActuallyRestrainsAPlayer()
        {
            // The bar above is one-sided: a Step that does nothing at all never gives speed either,
            // so on its own it cannot tell a working constraint from a dead one. This is the other
            // half — the net has to take the outward speed off and haul the player back in.
            GameObject player = NewObject("Player");
            Rigidbody rb = player.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = false;

            GameObject anchor = NewObject("Anchor");

            var settings = new SnareStruggle();
            SnaredBody snared = SnaredBody.Ensure(player);
            snared.Bind(anchor.transform, settings);

            rb.position = new Vector3(6f, 0f, 0f);
            rb.linearVelocity = new Vector3(9f, 0f, 0f);

            snared.Step();

            Assert.That(rb.linearVelocity.magnitude, Is.LessThan(1f),
                $"The player kept {rb.linearVelocity.magnitude:F2} m/s of outward speed.");

            float distance = Vector3.Distance(rb.position, anchor.transform.position);

            Assert.That(distance, Is.EqualTo(settings.ShuffleRadius).Within(1e-3f),
                $"The player was left {distance:F2} m out against a {settings.ShuffleRadius:F2} m " +
                "shuffle radius.");
        }

        [Test]
        public void ANetDoesNotBrakeAPlayerMovingTowardIt()
        {
            // The third side of the same rule. Dropping the sign check on the outward component
            // still never GIVES speed, so neither bar above catches it — it just cancels a player
            // running back toward the net as eagerly as one running away, which reads as walking
            // into treacle from several metres out.
            GameObject player = NewObject("Player");
            Rigidbody rb = player.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = false;

            GameObject anchor = NewObject("Anchor");

            SnaredBody snared = SnaredBody.Ensure(player);
            snared.Bind(anchor.transform, new SnareStruggle());

            rb.position = new Vector3(6f, 0f, 0f);
            rb.linearVelocity = new Vector3(-9f, 0f, 0f);   // straight back toward the anchor

            snared.Step();

            Assert.That(rb.linearVelocity.magnitude, Is.EqualTo(9f).Within(1e-3f),
                $"The net took {9f - rb.linearVelocity.magnitude:F2} m/s off a player who was " +
                "already running back toward it.");
        }

        // ── SnareMesh ────────────────────────────────────────────────────────

        [Test]
        public void TheDrawnNetSitsWhereTheSimulatedNetIs()
        {
            // The bug this exists to stop, which shipped and was found by playing rather than by
            // any of the twenty-eight tests around it: lattice positions are WORLD space — the
            // drape clamps them against world ground heights and pushes them out of world-space
            // capsules — and Unity renders a vertex buffer THROUGH the renderer's transform. Write
            // the nodes out raw and a renderer sitting anywhere but the origin draws the net at its
            // own position PLUS the net's, so a net fired by a player standing 500 m out is drawn
            // 500 m past them. Hence the origin argument, and hence this test.
            //
            // Every other mesh test here measures something RELATIVE — segment counts, the span of
            // a quad, cord width, winding — and every one of those is invariant under a uniform
            // translation. That is exactly how a completely invisible net passed the whole suite.
            SnareLattice lattice = NewLattice();
            lattice.Deploy(new Vector3(500f, 30f, 800f), Vector3.up, HalfWidth);
            Unfurl(lattice);

            GameObject holder = NewObject("NetRenderer");
            holder.transform.position = new Vector3(500f, 30f, 800f);

            var builder = new SnareMesh();
            var filter = holder.AddComponent<MeshFilter>();
            filter.sharedMesh = builder.Build(lattice, Vector3.forward, CordWidth,
                                              holder.transform.position);

            // Where the first strand's quad actually lands on screen, transform included.
            Vector3 drawn = holder.transform.TransformPoint(filter.sharedMesh.vertices[0]);
            Vector3 simulated = lattice.NodeAt(0, 0);

            Assert.That(Vector3.Distance(drawn, simulated), Is.LessThan(CordWidth),
                $"The net is drawn at {drawn} while it is simulated at {simulated}. Build must " +
                "put the vertices in the renderer's own space, not the world's.");

            builder.Dispose();
        }

        // ── The shot ─────────────────────────────────────────────────────────

        [Test]
        public void TheShotFollowsTheCrosshairRatherThanTheBarrel()
        {
            // A held gun is posed by ItemGrip and the hold animation: it points along the fingers,
            // which sits right of and below where the player is looking and barely pitches when
            // they look up. Sending muzzle.rotation as the aim therefore sent the ARM. The net went
            // right and low of the crosshair, and a shot aimed at the sky went out flat and landed
            // in the sand — which is what a player reports as "it does not go where I shoot".
            //
            // The muzzle still has to be where the net comes FROM, so both halves are asserted:
            // taking the camera for both would spawn the bundle inside the player's head.
            GameObject holder = NewObject("Holder");
            Camera eye = NewObject("Eye").AddComponent<Camera>();
            eye.transform.SetParent(holder.transform);
            eye.transform.position = new Vector3(0f, 1.7f, 0f);
            eye.transform.rotation = Quaternion.Euler(-40f, 15f, 0f);   // looking up and to the side

            var aim = holder.AddComponent<AimProvider>();
            Wire(aim, "playerCamera", eye);

            GameObject gun = NewObject("NetGun");
            var artifact = gun.AddComponent<NetGunArtifact>();

            // The barrel, deliberately pointing somewhere the camera is not.
            GameObject bore = NewObject("Muzzle");
            bore.transform.SetParent(gun.transform);
            bore.transform.position = new Vector3(0.3f, 1.4f, 0.5f);
            bore.transform.rotation = Quaternion.Euler(10f, -25f, 0f);
            Wire(artifact, "muzzle", bore.transform);

            artifact.OnEquipped(holder);

            var arg = new NetArg();
            artifact.OnRequestUse(ref arg);

            Assert.That(Vector3.Angle(arg.R * Vector3.forward, eye.transform.forward),
                Is.LessThan(1f),
                "the shot is aimed " +
                Vector3.Angle(arg.R * Vector3.forward, eye.transform.forward).ToString("F1") +
                " degrees off the camera. It is following the gun's pose, not the player's aim.");

            Assert.That(Vector3.Distance(arg.P, bore.transform.position), Is.LessThan(0.01f),
                "the net is leaving from " + arg.P + " rather than from the muzzle at " +
                bore.transform.position + ".");
        }

        /// <summary>
        /// Set a serialized field the tests have no other way to reach. The artifact's wiring is
        /// authored on a prefab, and a bare AddComponent has none of it.
        /// </summary>
        private static void Wire(Object target, string field, Object value)
        {
            var so = new UnityEditor.SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── SnareCatch ───────────────────────────────────────────────────────

        [Test]
        public void TheFlightEndsOnWhatItHitsRatherThanOnTheClock()
        {
            // Landing used to be a stopwatch and nothing else, so a net that reached a wall in a
            // third of a second went on being dragged along its arc THROUGH the wall for the rest
            // of the flight while the drape flattened it every frame. On screen that is a net
            // rolling and tumbling on the spot, which is precisely what it looked like.
            GameObject wall = NewPrimitive(PrimitiveType.Cube, "Wall");
            wall.transform.position = new Vector3(0f, 2f, 12f);
            wall.transform.localScale = new Vector3(10f, 6f, 0.5f);
            Physics.SyncTransforms();

            SnareCatch net = FireNet(new Vector3(0f, 1.6f, 0f), Vector3.forward, shooter: null);
            float flown = AdvanceUntilLanded(net);

            Assert.That(flown, Is.LessThan(NetGunFlight.MaxFlightSeconds),
                "the net flew its whole flight time with a wall 12 m in front of it — nothing " +
                "stopped it.");
            Assert.That(net.Footprint.center.z, Is.LessThan(12f).And.GreaterThan(8f),
                "the net came to rest at z=" + net.Footprint.center.z.ToString("F1") +
                " against a wall at z=11.75.");
        }

        [Test]
        public void TheNetDoesNotLandOnTheShooterItLeftFrom()
        {
            // The net is born at the muzzle, which is inside the player holding the gun. An impact
            // cast that does not skip its own shooter reports a hit at zero distance on the very
            // first step, so every shot in the game lands at the player's feet.
            GameObject body = NewPrimitive(PrimitiveType.Capsule, "Shooter");
            body.transform.position = new Vector3(0f, 1f, 0f);
            Physics.SyncTransforms();

            Vector3 muzzle = new Vector3(0f, 1.6f, 0.4f);
            SnareCatch net = FireNet(muzzle, Vector3.forward, shooter: body);
            AdvanceUntilLanded(net);

            Assert.That(net.Footprint.center.z, Is.GreaterThan(5f),
                "the net stopped " + net.Footprint.center.z.ToString("F1") +
                " m out — it hit the shooter it came from.");
        }

        [Test]
        public void ALandedNetSettlesInsteadOfSkatingOnForever()
        {
            // SnareDrape is a height clamp and says nothing about sliding, so before GripGround a
            // net that landed with any speed left kept that speed and travelled along the surface
            // instead of through it — for the rest of its life.
            GameObject floor = NewPrimitive(PrimitiveType.Cube, "Floor");
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(200f, 1f, 200f);
            Physics.SyncTransforms();

            SnareCatch net = FireNet(new Vector3(0f, 1.6f, 0f), Vector3.forward, shooter: null);
            AdvanceUntilLanded(net);

            for (int i = 0; i < SettleSteps; i++) net.Advance(Substep);
            Vector3 settled = net.Footprint.center;

            for (int i = 0; i < 90; i++) net.Advance(Substep);

            Assert.That(Vector3.Distance(settled, net.Footprint.center), Is.LessThan(0.2f),
                "the net drifted " +
                Vector3.Distance(settled, net.Footprint.center).ToString("F2") +
                " m in a second after it had already settled — it is skating, not lying there.");
        }

        [Test]
        public void ALandedNetKeepsAnsweringWhatItCameDownOn()
        {
            // The capture pass used to run on the single frame a net first reported landing, which
            // is the worst possible moment to ask: the net has only just met the ground and has not
            // draped over anything yet. SnareReceiver now keeps asking for a settle window, and
            // this is the clock it reads.
            GameObject floor = NewPrimitive(PrimitiveType.Cube, "Floor");
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(200f, 1f, 200f);
            Physics.SyncTransforms();

            SnareCatch net = FireNet(new Vector3(0f, 1.6f, 0f), Vector3.forward, shooter: null);

            Assert.AreEqual(0f, net.SecondsSinceLanding,
                "a net still in the air has not landed, so it has no settle window to spend.");

            AdvanceUntilLanded(net);
            for (int i = 0; i < 45; i++) net.Advance(Substep);

            Assert.That(net.SecondsSinceLanding, Is.GreaterThan(0.4f),
                "the landing clock is not running, so the capture window closes immediately and " +
                "the net catches only whatever happened to be under it on one frame.");
        }

        private GameObject NewPrimitive(PrimitiveType shape, string name)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            spawned.Add(go);
            return go;
        }

        private SnareCatch FireNet(Vector3 muzzle, Vector3 aim, GameObject shooter)
        {
            GameObject go = NewObject("SnareNet");
            go.transform.position = muzzle;

            var net = go.AddComponent<SnareCatch>();
            net.Begin(netId: 4242, muzzle, aim, HalfWidth, CordWidth,
                      NewLattice(), new SnareStruggle(), authority: true, firedBy: shooter);
            return net;
        }

        /// <summary>Run the net until it touches down, and report how long that took.</summary>
        private static float AdvanceUntilLanded(SnareCatch net)
        {
            float flown = 0f;

            for (int i = 0; i < FlightStepCeiling && !net.HasLanded; i++)
            {
                net.Advance(Substep);
                flown += Substep;
            }

            Assert.IsTrue(net.HasLanded, "the net never landed at all.");
            return flown;
        }


        [Test]
        public void AFiredNetIsDrawnAtItsOwnCordsRatherThanTwiceAsFarOut()
        {
            // The same defect as the test above, asked of the assembled net instead of the mesh
            // builder — which is the level it actually existed at and the level no unit test here
            // could see. The lattice was right, the mesh was right, the flight was right, and the
            // net was invisible, because the one thing nothing owned was which SPACE the two halves
            // met in. Pinning it only inside SnareMesh would leave SnareCatch free to hand it the
            // wrong origin, which is precisely the mistake that shipped.
            Vector3 muzzle = new Vector3(500f, 30f, 800f);

            GameObject go = NewObject("SnareNet");
            go.transform.position = muzzle;

            var net = go.AddComponent<SnareCatch>();
            net.Begin(netId: 1234, muzzle, Vector3.forward, HalfWidth, CordWidth,
                      NewLattice(), new SnareStruggle(), authority: true, firedBy: null);

            // Far enough into the flight that the net has left the muzzle: a net still sitting on
            // its origin would pass a same-space check by coincidence rather than by construction.
            for (int i = 0; i < 20; i++) net.Advance(Substep);

            var filter = go.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter, "Begin did not put a MeshFilter on the net.");
            Assert.Greater(filter.sharedMesh.vertexCount, 0, "The net drew no cord at all.");

            Bounds drawn = filter.sharedMesh.bounds;
            drawn.center = go.transform.TransformPoint(drawn.center);

            Bounds simulated = net.Footprint;

            Assert.That(Vector3.Distance(drawn.center, simulated.center),
                Is.LessThan(HalfWidth),
                $"The net is drawn around {drawn.center} while it is simulated around " +
                $"{simulated.center}, {Vector3.Distance(drawn.center, simulated.center):F1} m " +
                "apart. On screen that is a gun that fires and produces nothing.");
        }

        [Test]
        public void MeshCoversEveryStrandSegment()
        {
            SnareLattice lattice = NewLattice();
            lattice.Deploy(Vector3.zero, Vector3.up, HalfWidth);
            Unfurl(lattice);

            var builder = new SnareMesh();
            Mesh mesh = builder.Build(lattice, Vector3.forward, CordWidth, Vector3.zero);

            // Counted by walking the grid and asking each node for its right-hand and downward
            // neighbour, NOT with the closed form the implementation uses. A test that recomputes
            // 2 * n * (n - 1) alongside the code proves only that the two agree, and would pass
            // just as happily if both were wrong the same way.
            int segments = 0;
            for (int row = 0; row < NodesPerSide; row++)
            for (int col = 0; col < NodesPerSide; col++)
            {
                if (col + 1 < NodesPerSide) segments++;
                if (row + 1 < NodesPerSide) segments++;
            }

            Assert.AreEqual(segments * 4, mesh.vertexCount,
                "One quad of four vertices per strand segment.");
            Assert.AreEqual(segments * 6, mesh.triangles.Length,
                "Two triangles per strand segment.");

            builder.Dispose();
        }

        [Test]
        public void EveryRibbonSpansOneRealStrand()
        {
            // The counting test can only say there is the right AMOUNT of geometry. This says the
            // geometry is in the right PLACE: each quad has to be one strand long and one cord
            // wide, which no amount of self-consistent arithmetic can fake.
            SnareLattice lattice = NewLattice();
            lattice.Deploy(Vector3.zero, Vector3.up, HalfWidth);
            Unfurl(lattice);

            var builder = new SnareMesh();
            Mesh mesh = builder.Build(lattice, Vector3.forward, CordWidth, Vector3.zero);
            Vector3[] verts = mesh.vertices;

            for (int v = 0; v < verts.Length; v += 4)
            {
                // v0/v1 straddle one node, v2/v3 the next, so their midpoints are the two nodes.
                Vector3 startNode = (verts[v + 0] + verts[v + 1]) * 0.5f;
                Vector3 endNode = (verts[v + 2] + verts[v + 3]) * 0.5f;

                Assert.That(Vector3.Distance(startNode, endNode),
                    Is.EqualTo(lattice.RestSpacing).Within(lattice.RestSpacing * 0.05f),
                    $"Ribbon {v / 4} spans {Vector3.Distance(startNode, endNode):F3} m against a " +
                    $"{lattice.RestSpacing:F3} m strand — it is not built on adjacent nodes.");

                Assert.That(Vector3.Distance(verts[v + 0], verts[v + 1]),
                    Is.EqualTo(CordWidth).Within(1e-4f),
                    $"Ribbon {v / 4} is {Vector3.Distance(verts[v + 0], verts[v + 1]):F4} m wide " +
                    $"against an authored {CordWidth:F4} m.");
            }

            builder.Dispose();
        }

        [Test]
        public void RibbonsFaceTheViewerRatherThanAwayFromThem()
        {
            // Get the winding backwards and every quad in the net is back-facing, so with ordinary
            // culling the net renders as nothing whatsoever — no error, no warning, an item that
            // fires and produces an invisible catch. Task 12's two-sided material would paper over
            // it, which is exactly why it is worth pinning here instead.
            SnareLattice lattice = NewLattice();
            lattice.Deploy(Vector3.zero, Vector3.up, HalfWidth);
            Unfurl(lattice);

            Vector3 toViewer = new Vector3(0.3f, 1f, 0.2f).normalized;

            var builder = new SnareMesh();
            Mesh mesh = builder.Build(lattice, toViewer, CordWidth, Vector3.zero);

            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;

            for (int t = 0; t < tris.Length; t += 3)
            {
                // Unity's front face is cross(v1 - v0, v2 - v0).
                Vector3 face = Vector3.Cross(verts[tris[t + 1]] - verts[tris[t + 0]],
                                             verts[tris[t + 2]] - verts[tris[t + 0]]);

                Assert.That(Vector3.Dot(face.normalized, toViewer), Is.GreaterThan(0f),
                    $"Triangle {t / 3} is wound away from the viewer — the net would be culled.");
            }

            builder.Dispose();
        }

        [Test]
        public void RebuildingReusesTheSameMesh()
        {
            // Rebuilt every frame for the life of the net. Allocating a Mesh per frame is a
            // garbage-collection spike that shows up as a stutter exactly when the net is on screen.
            SnareLattice lattice = NewLattice();
            lattice.Deploy(Vector3.zero, Vector3.up, HalfWidth);

            var builder = new SnareMesh();
            Mesh first = builder.Build(lattice, Vector3.forward, CordWidth, Vector3.zero);
            Mesh second = builder.Build(lattice, Vector3.forward, CordWidth, Vector3.zero);

            // Asserted before the sameness check, because AreSame(null, null) passes and a builder
            // that returned nothing at all would sail through on its own.
            Assert.IsNotNull(first, "The builder produced no mesh.");
            Assert.AreSame(first, second, "A new Mesh was allocated on rebuild.");

            builder.Dispose();
        }

        // ── NetGunFlight ─────────────────────────────────────────────────────

        [Test]
        public void TwoMachinesIntegrateTheSameFlight()
        {
            // Every machine draws the net's flight from the same origin, aim and seed, so no
            // message is needed to make them agree. The Dragon Bazooka precedent: a seeded
            // closed-form path agrees across machines, a per-frame integration does not.
            Vector3 origin = new Vector3(3f, 2f, -4f);
            Vector3 aim = new Vector3(0.3f, 0.1f, 1f).normalized;
            const int Seed = 20260825;

            Vector3 once = NetGunFlight.PositionAt(origin, aim, Seed, 0.75f);
            Vector3 again = NetGunFlight.PositionAt(origin, aim, Seed, 0.75f);

            Assert.AreEqual(once, again, "The flight is not a pure function of its inputs.");

            // Bounded the other way too: a function that ignored its inputs entirely would agree
            // with itself just as well, and would fly every net down exactly the same line.
            Vector3 elsewhere = NetGunFlight.PositionAt(origin, aim, Seed + 1, 0.75f);

            Assert.AreNotEqual(once, elsewhere,
                "Two different seeds produced the identical flight — the scatter does nothing.");
        }

        [Test]
        public void TheNetFallsAsItFlies()
        {
            Vector3 origin = Vector3.up * 2f;
            Vector3 aim = Vector3.forward;

            float near = NetGunFlight.PositionAt(origin, aim, 1, 0.2f).y;
            float far = NetGunFlight.PositionAt(origin, aim, 1, 1.0f).y;

            Assert.That(far, Is.LessThan(near), "The net flew flat — there is no gravity on it.");

            // And it has to actually travel, or a net that merely dropped at the muzzle would pass
            // the drop check above without ever leaving the barrel.
            float range = Vector3.Distance(origin, NetGunFlight.PositionAt(origin, aim, 1, 1.0f));

            Assert.That(range, Is.GreaterThan(NetGunFlight.MuzzleSpeed * 0.5f),
                $"The net covered {range:F1} m in a second at a {NetGunFlight.MuzzleSpeed:F0} m/s " +
                "muzzle speed — it is not being carried anywhere.");
        }

        [Test]
        public void CarryingTheNetDoesNotDisturbIt()
        {
            // The flight moves every node at once, through prev as well as pos, so the solver never
            // sees it move. Translating pos alone would read as the whole net being flung, and the
            // constraint passes would tear it apart within a substep.
            SnareLattice carried = NewLattice();
            carried.Deploy(Vector3.zero, Vector3.up, HalfWidth);
            Unfurl(carried);

            SnareLattice still = NewLattice();
            still.Deploy(Vector3.zero, Vector3.up, HalfWidth);
            Unfurl(still);

            var shift = new Vector3(40f, 12f, -25f);
            carried.Translate(shift);

            for (int i = 0; i < 60; i++)
            {
                carried.Step(Substep);
                still.Step(Substep);
            }

            // Same shape, just somewhere else: every node has to sit exactly the shift away from
            // where the undisturbed net's matching node sits.
            for (int row = 0; row < NodesPerSide; row++)
            for (int col = 0; col < NodesPerSide; col++)
            {
                Vector3 drift = carried.NodeAt(row, col) - still.NodeAt(row, col) - shift;

                // A centimetre, and the bar is float32 rather than physics: at a 47 m offset a
                // single-precision node has about a micrometre of resolution, and sixty substeps
                // of it accumulate to roughly 1.3 mm. Moving only pos instead drifts a KILOMETRE,
                // so nothing is lost by leaving that much room.
                Assert.That(drift.magnitude, Is.LessThan(1e-2f),
                    $"Node ({row},{col}) drifted {drift.magnitude:F4} m — carrying the net " +
                    "injected velocity into it.");
            }
        }

        // ── Ammo ─────────────────────────────────────────────────────────────

        [Test]
        public void RechargingNeverDepletesTheGun()
        {
            // EquipmentController.ItemDepleted REMOVES the item from the inventory. A gun that
            // recharges must never reach that path, or firing three shots deletes it.
            NetGunArtifact gun = NewGun(charges: 3);

            bool depleted = false;
            gun.OnItemDepleted += _ => depleted = true;

            gun.SpendAllChargesForTest();

            Assert.IsFalse(depleted, "The net gun announced itself depleted and will be deleted.");
        }

        [Test]
        public void ChargesComeBackOverTime()
        {
            NetGunArtifact gun = NewGun(charges: 3);

            gun.SpendAllChargesForTest();
            Assert.AreEqual(0, gun.ChargesRemaining, "Spending every charge should leave none.");

            gun.AdvanceRechargeForTest(seconds: 13f);

            Assert.AreEqual(1, gun.ChargesRemaining, "A charge did not come back after its timer.");

            // Bounded above as well: a refund that ran per call rather than per elapsed interval
            // would hand back the whole magazine in one tick and the gun would never be empty.
            Assert.That(gun.ChargesRemaining, Is.LessThan(2),
                "One recharge interval gave back more than one charge.");
        }

        [Test]
        public void RimNodesAreHeavierThanMeshNodes()
        {
            SnareLattice lattice = NewLattice();
            lattice.Deploy(Vector3.zero, Vector3.up, HalfWidth);

            Assert.That(lattice.InverseMassAt(0, 4), Is.LessThan(lattice.InverseMassAt(4, 4)),
                "A rim node must have a lower inverse mass — a heavier node — than a mesh node.");
        }
    }
}
