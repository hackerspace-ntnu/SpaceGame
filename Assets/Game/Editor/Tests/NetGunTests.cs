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
using SpaceGame.Gameplay.Ragdoll;
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

        /// <summary>
        /// The two solver stiffnesses, pinned here for the same reason the shear limit is: authoring
        /// a softer net on the prefab must not quietly move the bar these tests measure against.
        /// </summary>
        private const float ShearStiffness = 0.30f;
        private const float BendStiffness = 0.016f;

        /// <summary>
        /// The authored cinch stiffness, pinned here for the same reason. Mirrors the field
        /// initialiser on SnareLattice rather than reading it, so a retune there is a visible
        /// two-place edit instead of a silent move of everything measured below.
        /// </summary>
        private const float DefaultCinchStiffness = 0.22f;

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

        /// <summary>
        /// The two adapters that implement the hold. Both are read as text below: the guards that
        /// make a hold indefinite have no runtime state to assert against, and the components need
        /// an Awake that AddComponent does not raise in EditMode.
        /// </summary>
        private const string PlayerRagdollSource =
            "Assets/Game/Scripts/Gameplay/Ragdoll/PlayerRagdoll.cs";

        private const string AgentRagdollSource =
            "Assets/Game/Scripts/Gameplay/Ragdoll/AgentRagdoll.cs";

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
        /// A component with its <c>Awake</c> actually raised.
        ///
        /// <para>
        /// <c>AddComponent</c> does not raise Awake outside play mode — the trap <c>SnaredBody</c>
        /// and <c>LassoedBody</c> both carry a note about — and both ragdoll adapters cache their
        /// <c>RagdollRig</c> there. Left unwoken, <c>HoldDown</c> throws a NullReferenceException on
        /// its first line that touches the rig, and a test written to expect a REFUSAL would then be
        /// passing on an exception instead of on the refusal it names. Raising Awake by hand is the
        /// smallest honest way to get a component into the state play mode would have given it.
        /// </para>
        /// </summary>
        private static T Woken<T>(GameObject on) where T : MonoBehaviour
        {
            T component = on.AddComponent<T>();

            System.Reflection.MethodInfo awake = typeof(T).GetMethod(
                "Awake",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.IsNotNull(awake,
                $"{typeof(T).Name} no longer has an Awake to raise. This helper would silently " +
                "hand back a half-built component and every test using it would be measuring that.");

            awake.Invoke(component, null);
            return component;
        }

        /// <summary>
        /// A body <c>RagdollRig</c> can really build a skeleton out of, without a skinned mesh.
        ///
        /// <para>
        /// Rigid mesh parts are a first-class path through <c>RagdollRig.Build</c> rather than a
        /// trick played on it: the golem, the six-legged crab and the humanoid robot have no
        /// SkinnedMeshRenderer between them, and the part measure is the answer written for exactly
        /// those. Five equal cubes clear both the weight floor and the four-bone minimum, so
        /// <c>GoLimp</c> keeps bones and <c>IsLimp</c> goes true — which is the difference between
        /// a test of a hold that took and a test of a hold that could never have taken.
        /// </para>
        /// <para>
        /// The primitives' own colliders go: <c>BuildBone</c> adds a box around each part's mesh, so
        /// leaving them would put two identical colliders on every bone.
        /// </para>
        /// </summary>
        private GameObject NewRagdollBody(string name)
        {
            GameObject root = NewObject(name);

            for (int i = 0; i < 5; i++)
            {
                GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.name = $"Part{i}";
                part.transform.SetParent(root.transform);
                part.transform.localPosition = new Vector3(0f, i * 0.5f, 0f);

                Object.DestroyImmediate(part.GetComponent<Collider>());
            }

            return root;
        }

        /// <summary>
        /// A creature's NavMeshAgent, authored and switched off.
        ///
        /// Disabled deliberately: nothing here needs it pathing, and an enabled agent with no
        /// NavMesh under it complains. The speed property is readable and writable regardless,
        /// which is all the hobble touches.
        /// </summary>
        private static NavMeshAgent AuthoredAgent(GameObject creature)
        {
            var agent = creature.AddComponent<NavMeshAgent>();
            agent.enabled = false;
            agent.speed = AuthoredSpeed;
            return agent;
        }

        /// <summary>A player already netted, for the struggle tests to drive.</summary>
        private SnaredBody NewCaptive()
        {
            GameObject player = NewRagdollBody("Player");
            Woken<PlayerRagdoll>(player);

            GameObject anchor = NewObject("Anchor");
            SnaredBody snared = SnaredBody.Ensure(player);

            Assert.IsTrue(snared.Bind(anchor.transform, new SnareStruggle()),
                "The captive was never caught, so nothing below is measuring a struggle.");

            return snared;
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

        private static SnareLattice NewLattice() =>
            NewLattice(ShearStiffness, BendStiffness);

        /// <summary>
        /// A lattice with the two solver stiffnesses named outright.
        ///
        /// Used where a test has to isolate ONE constraint family — the diagonals cannot be shown
        /// to be one-sided while bending is also pushing the same nodes around, and a test that
        /// leaves both on is measuring their sum rather than either.
        /// </summary>
        private static SnareLattice NewLattice(float shearStiffness, float bendStiffness)
        {
            var lattice = new SnareLattice();
            lattice.ConfigureForTest(NodesPerSide, rimMassMultiplier: 6f,
                                     shearLimit: LatticeShearLimit,
                                     shearStiffness: shearStiffness,
                                     bendStiffness: bendStiffness);
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
            //
            // Bending off, because folding a corner right over is a sharp crease as well as a
            // closed cell, and bending is entitled to push back on a crease. With both on, this
            // measures their sum and cannot say which one moved the node — a two-sided diagonal
            // would hide behind the bending, which is exactly the failure it exists to catch.
            SnareLattice lattice = NewLattice(ShearStiffness, bendStiffness: 0f);
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

        // ── SnareLattice: bending ────────────────────────────────────────────

        [Test]
        public void BendingMovesTheNetsShapeWithoutMovingTheNet()
        {
            // Bending used to be a Laplacian smoothing pass: each interior node was dragged toward
            // the average of its four neighbours and NOTHING was applied to those neighbours in
            // return. A correction with nothing on the other end of it is a force from nowhere, so
            // a creased net accelerated under its own bending — and because the pass ran after the
            // constraint solve rather than inside it, every substep also ended off-constraint for
            // the next one to yank back. Ninety times a second, that is the shivering.
            //
            // Isolated as bending ON against bending OFF from an IDENTICAL creased shape, over a
            // SINGLE substep. Everything else in that substep — gravity, damping, face drag, the
            // strands, the diagonals — then acts on identical input, and of those only face drag is
            // capable of moving the net at all (it is a real external force, and a shape-dependent
            // one). Give the two lattices time to drift apart and face drag starts telling them
            // apart too, which is a comparison that proves nothing about bending.
            SnareLattice loose = OpenLattice(bendStiffness: 0f);
            SnareLattice stiff = OpenLattice(BendStiffness);

            // ONE shape, imposed on both. Opening a net is itself eighty-odd substeps of solving,
            // so two lattices that differ in bend stiffness have already drifted apart by the time
            // they finish unfurling — reading the crease off each of them separately compares two
            // different nets and reports the drift as the answer.
            Vector3[] crease = CreasedShape(loose);
            Impose(loose, crease);
            Impose(stiff, crease);

            Assert.That(Vector3.Distance(MassCentre(loose), MassCentre(stiff)), Is.LessThan(1e-5f),
                "the two lattices did not start identical, so the comparison below would be " +
                "measuring the setup rather than the solver.");

            loose.Step(Substep);
            stiff.Step(Substep);

            // Bending has to have actually DONE something, or a stiffness of zero passes the real
            // assertion below by doing nothing at all — which is the one way this test could go
            // quietly green while proving nothing.
            float shaped = Vector3.Distance(loose.NodeAt(1, 4), stiff.NodeAt(1, 4));
            Assert.That(shaped, Is.GreaterThan(1e-4f),
                "bending changed no node by more than " + shaped.ToString("F6") + " m, so this " +
                "test is not exercising it.");

            float drift = Vector3.Distance(MassCentre(loose), MassCentre(stiff));

            Assert.That(drift, Is.LessThan(1e-3f),
                "bending moved the net's centre of mass " + drift.ToString("F5") + " m while " +
                "changing its shape by " + shaped.ToString("F4") + " m. A bending correction has " +
                "to be shared with the nodes it bends against; one applied to the middle node " +
                "alone is a force from nowhere and the net accelerates under its own creases.");
        }

        /// <summary>A net at its open size, with the given bend stiffness and nothing done to it yet.</summary>
        private static SnareLattice OpenLattice(float bendStiffness)
        {
            SnareLattice lattice = NewLattice(ShearStiffness, bendStiffness);
            lattice.Deploy(Vector3.up * 20f, Vector3.forward, HalfWidth);
            Unfurl(lattice);
            return lattice;
        }

        /// <summary>That net folded into the sharpest zigzag bending can see, as bare positions.</summary>
        private static Vector3[] CreasedShape(SnareLattice lattice)
        {
            var shape = new Vector3[NodesPerSide * NodesPerSide];

            for (int row = 0; row < NodesPerSide; row++)
            for (int col = 0; col < NodesPerSide; col++)
            {
                float sign = (row & 1) == 0 ? 1f : -1f;
                shape[row * NodesPerSide + col] = lattice.NodeAt(row, col) + Vector3.up * (sign * 0.25f);
            }

            return shape;
        }

        /// <summary>
        /// Put a lattice into an exact shape, at rest.
        ///
        /// SetNodeForTest writes prev as well as pos, so this also clears whatever velocity the
        /// unfurl left behind — which the comparison needs just as much as the positions, since a
        /// Verlet node's velocity IS the gap between the two.
        /// </summary>
        private static void Impose(SnareLattice lattice, Vector3[] shape)
        {
            for (int row = 0; row < NodesPerSide; row++)
            for (int col = 0; col < NodesPerSide; col++)
                lattice.SetNodeForTest(row, col, shape[row * NodesPerSide + col]);
        }

        /// <summary>
        /// The centre of MASS, not the average position.
        ///
        /// The hem is six times heavier than the mesh, so the plain average of the node positions
        /// is not the quantity an internal force has to leave alone — a perfectly well-behaved
        /// constraint moves it every time it corrects a rim node against a mesh one.
        /// </summary>
        private static Vector3 MassCentre(SnareLattice lattice)
        {
            Vector3 weighted = Vector3.zero;
            float total = 0f;

            for (int row = 0; row < NodesPerSide; row++)
            for (int col = 0; col < NodesPerSide; col++)
            {
                float mass = 1f / lattice.InverseMassAt(row, col);
                weighted += lattice.NodeAt(row, col) * mass;
                total += mass;
            }

            return weighted / total;
        }

        [Test]
        public void ASettledNetGoesQuiet()
        {
            // What "jumpy" is, measured. A solver whose substep ends off-constraint never reaches
            // equilibrium: the next substep starts by repairing the damage, which is itself a
            // displacement, so the net twitches for as long as it exists. Every rule the cord obeys
            // is now a constraint the same solve relaxes, so a net with nothing acting on it but a
            // floor it is already resting on has somewhere to come to rest.
            //
            // The bed is LandedLattice, which is this test's own setup extracted so the cinch tests
            // can measure absolute motion the same way. Collision from the first substep, which is
            // why it steps through the drape rather than unfurling first: a second of unopposed
            // free fall at this gravity puts the whole net seven metres under the floor and makes
            // the first contact a teleport rather than a landing.
            SnareLattice lattice = LandedLattice(out SnareDrape drape);

            float worst = WorstStepAgainstFloor(lattice, drape);

            Assert.That(worst, Is.LessThan(0.01f),
                "a node moved " + worst.ToString("F4") + " m in one substep of a net that has been " +
                "lying still for " + (DrapeSteps * Substep).ToString("F1") + " seconds. It is not " +
                "settling, it is vibrating.");
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
            lattice.ConfigureForTest(NodesPerSide, rimMassMultiplier, LatticeShearLimit,
                                     ShearStiffness, BendStiffness);
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
        public void ANettedCreatureIsPutOnTheFloorRatherThanSlowedDown()
        {
            // The rework in one assertion. A net used to hobble whatever it caught and leave it
            // walking; it now fells the body, and the speed cap is only what is left for the bodies
            // that refuse to fall.
            GameObject creature = NewRagdollBody("Creature");

            NavMeshAgent agent = AuthoredAgent(creature);
            AgentRagdoll ragdoll = Woken<AgentRagdoll>(creature);

            GameObject net = NewObject("Net");
            SnareTether tether = SnareTether.Ensure(creature);

            Assert.IsTrue(tether.Bind(net.transform, new SnareStruggle()),
                "The net did not take a creature it could fell.");

            Assert.IsTrue(ragdoll.IsHeld,
                "The net caught the creature without putting it down.");
            Assert.That(agent.speed, Is.EqualTo(AuthoredSpeed).Within(1e-3f),
                $"A felled creature was hobbled as well, down to {agent.speed:F2} m/s. The cap is " +
                "the fallback for a body that would not go down, not an extra applied to one that " +
                "did — and NavMeshAgent.speed is serialized, so an unnecessary one is a permanent " +
                "one waiting for a path that forgets to restore it.");

            tether.Release(net.transform);

            Assert.IsFalse(ragdoll.IsHeld, "The net rotted and the creature stayed down.");
        }

        [Test]
        public void ANetHobblesACreatureItCannotFell()
        {
            // The other side of the same branch, and the reason the fallback survived at all: a
            // mount with somebody aboard refuses to go limp (AgentRagdoll.CanBeKnockedDown), and so
            // does a rig whose skeleton build kept no bones — which is what this creature is. A net
            // that did nothing to a body it visibly landed on is worse than one that slows it.
            GameObject creature = NewCreature("Creature");

            NavMeshAgent agent = AuthoredAgent(creature);
            AgentRagdoll ragdoll = Woken<AgentRagdoll>(creature);

            GameObject net = NewObject("Net");
            SnareTether tether = SnareTether.Ensure(creature);

            Assert.IsTrue(tether.Bind(net.transform, new SnareStruggle()));

            Assert.IsFalse(ragdoll.IsHeld,
                "This creature has no skeleton to ragdoll, so the hold cannot have taken — the " +
                "test is no longer measuring the fallback.");

            // The authored share exactly, not merely "slower". A hobble that ignores the field and
            // applies a number of its own would satisfy a less-than, and the whole reason the field
            // was kept is that a designer moves it.
            float hobbled = AuthoredSpeed * new SnareStruggle().HobbleSpeed;

            Assert.That(agent.speed, Is.EqualTo(hobbled).Within(1e-3f),
                $"The creature is at {agent.speed:F2} m/s against the {hobbled:F2} m/s the " +
                "authored hobble asks for — either the fallback did not run at all, and the " +
                "creature walks out from under the net, or it is not reading SnareStruggle.");

            tether.Release(net.transform);

            Assert.That(agent.speed, Is.EqualTo(AuthoredSpeed).Within(1e-3f),
                $"The creature was left at {agent.speed:F2} against an authored {AuthoredSpeed:F2}.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ANetLetsACreatureUpOnlyWhenNothingElseHoldsIt(bool tiedAsWell)
        {
            // AgentRagdoll.ReleaseHold is one flag with no notion of how many claims are out, so
            // the FIRST captor to let go stands the body up for everyone. Task 9's Hogtie outlives
            // the net that caught the creature, and this is the guard that keeps it holding.
            GameObject creature = NewRagdollBody("Creature");
            AgentRagdoll ragdoll = Woken<AgentRagdoll>(creature);

            GameObject net = NewObject("Net");
            SnareTether tether = SnareTether.Ensure(creature);

            Assert.IsTrue(tether.Bind(net.transform, new SnareStruggle()));
            Assert.IsTrue(ragdoll.IsHeld, "Nothing was holding the creature to begin with.");

            if (tiedAsWell) creature.AddComponent<StandInHold>();

            tether.Release(net.transform);

            Assert.AreEqual(tiedAsWell, ragdoll.IsHeld,
                tiedAsWell
                    ? "The net rotted and stood up a creature that is still tied."
                    : "The net let go and left the creature down with nothing holding it.");
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
            // No AgentRagdoll on this one, so the hold cannot even be attempted and the fallback is
            // what runs — which is the path that leaves a serialized field behind.
            GameObject creature = NewCreature("Creature");
            NavMeshAgent agent = AuthoredAgent(creature);

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
        public void ANetTakesNoCaptiveItCannotPutDown()
        {
            // A capture recorded over a body that never went down is worse than no capture at all:
            // the net spends its shared pool holding somebody who is walking about, and it is the
            // shooter who pays for it. PlayerRagdoll.HoldDown reports that refusal rather than
            // failing silently, and this is the caller honouring it.
            //
            // This player's rig has no skeleton to build, which is one of the two ways HoldDown
            // says no — RagdollRig.GoLimp returns without a word when the build keeps no bones.
            GameObject player = NewObject("Player");
            PlayerRagdoll ragdoll = Woken<PlayerRagdoll>(player);
            RagdollRig rig = player.GetComponent<RagdollRig>();

            GameObject anchor = NewObject("Anchor");
            SnaredBody snared = SnaredBody.Ensure(player);

            Assert.IsFalse(snared.Bind(anchor.transform, new SnareStruggle()),
                "The net reported a capture over a player it never put down.");
            Assert.IsFalse(snared.IsBound,
                "The net answered false and bound the player anyway, so it will hold them until " +
                "it rots and let nothing else take them in the meantime.");

            Assert.IsFalse(rig.IsLimp,
                "This rig went limp after all, so the refusal under test was not the one intended " +
                "and this test is measuring nothing.");
            Assert.IsFalse(ragdoll.IsHeldOrDown, "The player is down despite the refusal.");
        }

        [Test]
        public void ANetTakesNoCaptiveWithNoRagdollAtAll()
        {
            // The same rule reached the other way. Every player prefab carries a PlayerRagdoll
            // (RagdollWiring puts it there), so this is the case where that wiring has been lost —
            // and a net that quietly held such a player would be a wiring bug presenting as a
            // balance one, days later, as nets that seem to expire early.
            GameObject player = NewObject("Player");
            GameObject anchor = NewObject("Anchor");

            SnaredBody snared = SnaredBody.Ensure(player);

            Assert.IsFalse(snared.Bind(anchor.transform, new SnareStruggle()),
                "The net took hold of a body with nothing to put it down with.");
            Assert.IsFalse(snared.IsBound);
        }

        [Test]
        public void ANetTakesNoCaptiveWhoIsAlreadyBeingCarried()
        {
            // A mounted player is PARENTED into the saddle, so one put limp there is dragged
            // wherever the animal walks, through the ground included. AgentRagdoll has always
            // refused a knockdown for the mount's sake; PlayerRagdoll now refuses the hold for the
            // rider's, and CarriedBody is how it knows — both riding systems register their claim
            // there, on the paths that parent the rider and on the one that does not.
            GameObject player = NewRagdollBody("Player");

            // CarriedBody records a Rigidbody. Without one Hold is a silent no-op and this test
            // would pass while carrying nobody, which is the failure it exists to rule out.
            player.AddComponent<Rigidbody>();
            Woken<PlayerRagdoll>(player);
            RagdollRig rig = player.GetComponent<RagdollRig>();

            GameObject anchor = NewObject("Anchor");
            SnaredBody snared = SnaredBody.Ensure(player);

            var saddle = new object();
            SpaceGame.Agents.CarriedBody.Hold(player, saddle);

            Assert.IsTrue(SpaceGame.Agents.CarriedBody.IsHeld(player),
                "The carry did not take, so the refusal below would prove nothing.");

            Assert.IsFalse(snared.Bind(anchor.transform, new SnareStruggle()),
                "The net put a seated rider limp in the saddle.");
            Assert.IsFalse(rig.IsLimp, "The body went limp despite the refusal.");

            // The control, on the same fixture: everything else about this player is unchanged, so
            // a Bind that succeeds now can only have been refused above because of the carry.
            SpaceGame.Agents.CarriedBody.Release(player, saddle);

            Assert.IsTrue(snared.Bind(anchor.transform, new SnareStruggle()),
                "The same player could not be netted after dismounting either, so the refusal " +
                "above was not about being carried.");
        }

        [Test]
        public void ANettedPlayerIsPutOnTheFloorAndLetUpAgain()
        {
            // The other half of the refusal above: a Bind that always answered false would satisfy
            // both tests before this one and hold nobody.
            GameObject player = NewRagdollBody("Player");
            PlayerRagdoll ragdoll = Woken<PlayerRagdoll>(player);
            RagdollRig rig = player.GetComponent<RagdollRig>();

            GameObject anchor = NewObject("Anchor");
            SnaredBody snared = SnaredBody.Ensure(player);

            Assert.IsTrue(snared.Bind(anchor.transform, new SnareStruggle()),
                "The net refused a player it could put down.");
            Assert.IsTrue(snared.IsBound);

            Assert.IsTrue(rig.IsLimp, "The player was caught without going limp.");

            // The hold's own mark, and the one visible from here. PlayerRagdoll keeps `held`
            // private, and IsHeldOrDown answers true for any limp body — so it cannot tell a held
            // captive from one merely knocked over. BudgetExempt can: HoldDown sets it, ReleaseHold
            // clears it, and the only other writers are death and revive, neither of which this
            // test reaches.
            Assert.IsTrue(rig.BudgetExempt,
                "The player is limp but not exempt from RagdollBudget, so the first crowd to go " +
                "down elsewhere evicts the captive and stands them up mid-net.");

            snared.Release(anchor.transform);

            Assert.IsFalse(snared.IsBound);
            Assert.IsFalse(rig.BudgetExempt, "The net rotted and never let the player up.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ANetLetsAPlayerUpOnlyWhenNothingElseHoldsThem(bool tiedAsWell)
        {
            // PlayerRagdoll.ReleaseHold is one flag with no notion of how many claims are out, so
            // the first captor to let go stands the body up for everyone. Task 9's Hogtie outlives
            // the net that caught the player; this is the guard that keeps it holding.
            GameObject player = NewRagdollBody("Player");
            Woken<PlayerRagdoll>(player);
            RagdollRig rig = player.GetComponent<RagdollRig>();

            GameObject anchor = NewObject("Anchor");
            SnaredBody snared = SnaredBody.Ensure(player);

            Assert.IsTrue(snared.Bind(anchor.transform, new SnareStruggle()));
            Assert.IsTrue(rig.BudgetExempt, "Nothing was holding the player to begin with.");

            if (tiedAsWell) player.AddComponent<StandInHold>();

            snared.Release(anchor.transform);

            Assert.AreEqual(tiedAsWell, rig.BudgetExempt,
                tiedAsWell
                    ? "The net rotted and let up a player who is still tied."
                    : "The net let go and left the player held with nothing holding them.");
        }

        // ── SnaredBody: the struggle ─────────────────────────────────────────

        [Test]
        public void AJumpCountsAsAStruggle()
        {
            SnaredBody snared = NewCaptive();

            Assert.That(snared.StruggleLevel, Is.EqualTo(0f).Within(1e-4f),
                "A captive who has done nothing is already struggling.");

            snared.Step(Substep, jumpPressed: true, move: Vector2.zero);

            Assert.That(snared.StruggleLevel, Is.GreaterThan(0f),
                "The captive pressed jump and the net never noticed.");
        }

        [TestCase(-1f, 0f, true, TestName = "ReversingCountsAsAStruggle")]
        [TestCase(0f, 1f, false, TestName = "TurningDoesNotCountAsAStruggle")]
        public void OnlyAReversalOfTheMoveCountsAsAStruggle(float thenX, float thenY, bool counts)
        {
            // Throwing yourself the other way is the struggle; steering is not. Without the
            // distinction, holding one direction against a net registers as fighting it — which
            // would make the escape a matter of leaning on a key rather than of doing anything.
            SnaredBody snared = NewCaptive();

            snared.Step(Substep, jumpPressed: false, move: Vector2.right);

            Assert.That(snared.StruggleLevel, Is.EqualTo(0f).Within(1e-4f),
                "The first push of a direction counted as a reversal of nothing.");

            snared.Step(Substep, jumpPressed: false, move: new Vector2(thenX, thenY));

            if (counts)
                Assert.That(snared.StruggleLevel, Is.GreaterThan(0f),
                    "The captive threw themselves the opposite way and it counted for nothing.");
            else
                Assert.That(snared.StruggleLevel, Is.EqualTo(0f).Within(1e-4f),
                    "A ninety-degree turn was read as a reversal, so simply steering while netted " +
                    "drains the net.");
        }

        [Test]
        public void AStruggleFadesOnceTheCaptiveStops()
        {
            // Step has to advance the meter on every call, not only on the ones carrying an input.
            // A meter advanced only when something happens never decays, so one burst of struggling
            // would keep draining the net for the rest of its life.
            SnaredBody snared = NewCaptive();

            snared.Step(Substep, jumpPressed: true, move: Vector2.zero);
            float peak = snared.StruggleLevel;

            Assert.That(peak, Is.GreaterThan(0f), "Nothing to fade — the push never registered.");

            const int Idle = 500;   // 5.5 s at the substep, against a 1.2 s decay
            for (int i = 0; i < Idle; i++)
                snared.Step(Substep, jumpPressed: false, move: Vector2.zero);

            Assert.That(snared.StruggleLevel, Is.LessThan(peak * 0.1f),
                $"The captive stopped fighting {Idle * Substep:F1} s ago and the meter is still at " +
                $"{snared.StruggleLevel:F3} against a peak of {peak:F3}.");
        }

        [Test]
        public void AnUnboundBodyIgnoresStruggleInput()
        {
            // The component outlives every net that uses it: Ensure adds one and hands the same one
            // back to the next net, and a refused Bind leaves it here too. So a body nothing is
            // holding has to count nothing, or a player who was netted an hour ago walks around
            // with a meter that fills every time they press jump.
            GameObject player = NewRagdollBody("Player");
            Woken<PlayerRagdoll>(player);

            SnaredBody snared = SnaredBody.Ensure(player);

            snared.Step(Substep, jumpPressed: true, move: Vector2.zero);

            Assert.That(snared.StruggleLevel, Is.EqualTo(0f).Within(1e-4f),
                "A body no net is holding counted a struggle.");
        }

        // ── SnareReceiver: how many nets one gun may have out ────────────────

        [Test]
        public void AFourthNetTearsTheOldestOne()
        {
            // The gun's three charges do NOT bound this on their own, which is the whole reason the
            // cap exists separately. A charge comes back on a timer while a net lasts up to its
            // full hold, so firing three, waiting for the recharge and firing again leaves four
            // lattices in the world being solved at ninety substeps a second — and nothing stops
            // that going to five.
            GameObject shooter = NewObject("Shooter");
            SnareReceiver receiver = SnareReceiver.Ensure(shooter);

            var nets = new SnareCatch[4];
            for (int i = 0; i < nets.Length; i++)
            {
                nets[i] = FireNet(new Vector3(i * 20f, 1.6f, 0f), Vector3.forward, shooter: null);
                receiver.Track(netId: i, nets[i], catchableLayers: ~0, captureHeight: 2.5f);
            }

            Assert.AreEqual(3, receiver.LiveNetCount,
                "the shooter is watching " + receiver.LiveNetCount + " nets. A fourth shot has to " +
                "cost the oldest net, not add to the pile.");

            Assert.IsTrue(nets[0].IsTearing,
                "the fourth shot did not tear the FIRST net. Evicting any other one makes which " +
                "net dies depend on dictionary order, which is not the same on two machines.");

            for (int i = 1; i < nets.Length; i++)
                Assert.IsFalse(nets[i].IsTearing,
                    "net " + i + " was torn as well. Only the oldest should go.");
        }

        [Test]
        public void RefiringTheSameNetIdDoesNotCostANet()
        {
            // Track is called from Present, which runs on every machine — and a machine that hears
            // about the same shot twice must not answer by tearing a net. Same idempotence rule the
            // capture and tear handlers hold to.
            GameObject shooter = NewObject("Shooter");
            SnareReceiver receiver = SnareReceiver.Ensure(shooter);

            SnareCatch net = FireNet(new Vector3(0f, 1.6f, 0f), Vector3.forward, shooter: null);

            for (int i = 0; i < 5; i++)
                receiver.Track(netId: 7, net, catchableLayers: ~0, captureHeight: 2.5f);

            Assert.AreEqual(1, receiver.LiveNetCount,
                "the same net was counted " + receiver.LiveNetCount + " times.");
            Assert.IsFalse(net.IsTearing, "the net tore itself out of its own re-registration.");
        }

        // ── SnareDrape: landing on a body ────────────────────────────────────

        [Test]
        public void ANetDoesNotBounceOffWhatItLandsOn()
        {
            // The defect this exists to stop, and it is not subtle once it is named. A contact used
            // to move the node's POSITION out of the capsule and leave prev where it was — but
            // velocity in a Verlet lattice IS the gap between those two, so every substep of
            // contact handed the node its own penetration depth as outward speed. A net coming down
            // on an animal took that off every node at once and jumped clear of it.
            //
            // Measured as the net's own upward speed, because that is the shape the failure has:
            // not one node jittering, the whole sheet leaving.
            SnareDrape.Capsule body = StandingBody();

            SnareLattice lattice = NewLattice();
            lattice.Deploy(new Vector3(0f, 2.4f, 0f), Vector3.up, HalfWidth);

            // No Unfurl first, for the reason the drape test gives: a second of unopposed free fall
            // carries the net through the captive AND the floor, and the first contact after that
            // is a four-metre teleport back up, which measures as a colossal upward speed and has
            // nothing to do with bouncing.
            var drape = new SnareDrape();
            var captives = new[] { body };

            float worstRise = 0f;

            for (int i = 0; i < DrapeSteps; i++)
            {
                Vector3 before = lattice.Centre();

                lattice.Step(Substep);
                drape.Resolve(lattice, captives, GroundHeight);
                lattice.GripGround(GroundHeight);

                worstRise = Mathf.Max(worstRise, (lattice.Centre().y - before.y) / Substep);
            }

            Assert.That(worstRise, Is.LessThan(0.5f),
                "the net drove itself upward at " + worstRise.ToString("F2") + " m/s at some point " +
                "while landing on a body. Nothing pushes a net up: it is being kicked off by its " +
                "own contact response.");
        }

        [Test]
        public void TheWeightedHemFallsBelowTheMesh()
        {
            // What "hold the sides down" is, measured. rimMassMultiplier alone cannot do it —
            // gravity is an acceleration, so a heavier hem falls at exactly the rate the mesh does
            // and its weight shows up only where the two fight over a strand. hemWeight is the lead
            // line: a real pull, which is what drives the skirt down past a body instead of leaving
            // the net tented on top of it.
            SnareDrape.Capsule body = StandingBody();

            SnareLattice lattice = NewLattice();
            lattice.Deploy(new Vector3(0f, 2.4f, 0f), Vector3.up, HalfWidth);

            var drape = new SnareDrape();
            var captives = new[] { body };

            for (int i = 0; i < DrapeSteps; i++)
            {
                lattice.Step(Substep);
                drape.Resolve(lattice, captives, GroundHeight);
                lattice.GripGround(GroundHeight);
            }

            float hem = 0f;
            int hemNodes = 0;
            float mesh = 0f;
            int meshNodes = 0;

            for (int row = 0; row < NodesPerSide; row++)
            for (int col = 0; col < NodesPerSide; col++)
            {
                bool rim = row == 0 || col == 0 || row == NodesPerSide - 1 || col == NodesPerSide - 1;

                if (rim) { hem += lattice.NodeAt(row, col).y; hemNodes++; }
                else { mesh += lattice.NodeAt(row, col).y; meshNodes++; }
            }

            hem /= hemNodes;
            mesh /= meshNodes;

            Assert.That(hem, Is.LessThan(mesh),
                "the hem settled at " + hem.ToString("F2") + " m against a mesh at " +
                mesh.ToString("F2") + " m. The skirt is not being pulled down past the body, so " +
                "the net is a sheet lying on top of one.");
        }

        [Test]
        public void ANetStaysOnTheBodyItLandedOn()
        {
            // The other half of the weighted hem: a skirt that drives down hard and has nothing to
            // grip with just drags the whole net off onto the floor, and the animal is left
            // standing free in the middle of a ring of cord. Friction against the body is what
            // turns a heavy hem from a reason to slide off into a reason to close underneath.
            int held = NodesAgainstTheBody(gripping: true);
            int slipped = NodesAgainstTheBody(gripping: false);

            Assert.That(held, Is.GreaterThan(slipped),
                "the net left " + held + " nodes on the body with friction and " + slipped +
                " without it. The cord is sliding off rather than holding.");
        }

        /// <summary>A standing, player-sized captive.</summary>
        private static SnareDrape.Capsule StandingBody() => new SnareDrape.Capsule
        {
            Bottom = new Vector3(0f, 0.4f, 0f),
            Top = new Vector3(0f, 1.5f, 0f),
            Radius = 0.4f,
        };

        /// <summary>How many nodes end up resting against a body, with the body's friction on or off.</summary>
        private static int NodesAgainstTheBody(bool gripping)
        {
            SnareDrape.Capsule body = StandingBody();

            SnareLattice lattice = NewLattice();
            lattice.ConfigureGripForTest(bodyGrip: gripping ? 0.6f : 0f);
            lattice.Deploy(new Vector3(0f, 2.4f, 0f), Vector3.up, HalfWidth);

            var drape = new SnareDrape();
            var captives = new[] { body };

            for (int i = 0; i < DrapeSteps; i++)
            {
                lattice.Step(Substep);
                drape.Resolve(lattice, captives, GroundHeight);
                lattice.GripGround(GroundHeight);
            }

            int touching = 0;

            foreach (Vector3 node in lattice.Positions)
            {
                float radial = new Vector2(node.x - body.Bottom.x, node.z - body.Bottom.z).magnitude;

                if (node.y > body.Bottom.y && radial < body.Radius + 0.3f) touching++;
            }

            return touching;
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

        // ── SnareCinch ────────────────────────────────────────────────────────

        [Test]
        public void CinchRadius_EasesFromStartToTarget()
        {
            Assert.AreEqual(3f, SnareCinch.RadiusAt(3f, 0.4f, 0f, 0.7f), 1e-4f,
                            "At t=0 the target is the radius the net already had.");
            Assert.AreEqual(0.4f, SnareCinch.RadiusAt(3f, 0.4f, 0.7f, 0.7f), 1e-4f,
                            "At t=duration the target is the authored cinch radius.");

            float mid = SnareCinch.RadiusAt(3f, 0.4f, 0.35f, 0.7f);
            Assert.Less(mid, 3f, "Half way through the window the ring must already have moved off the start radius.");
            Assert.Greater(mid, 0.4f, "Half way through the window the ring must not already be at the target radius.");
        }

        [Test]
        public void CinchRadius_IsMonotonic()
        {
            float previous = float.MaxValue;

            for (int i = 0; i <= 20; i++)
            {
                float radius = SnareCinch.RadiusAt(3f, 0.4f, i / 20f * 0.7f, 0.7f);
                Assert.LessOrEqual(radius, previous + 1e-5f,
                                   "A cinch that widens at any point pumps energy into the cloth.");
                previous = radius;
            }
        }

        [Test]
        public void CinchRadius_ClampsPastTheEnd()
        {
            Assert.AreEqual(0.4f, SnareCinch.RadiusAt(3f, 0.4f, 5f, 0.7f), 1e-4f,
                            "Past the window the target holds, it does not keep shrinking.");
        }

        [Test]
        public void CinchRadius_NeverOpens()
        {
            Assert.AreEqual(0.4f, SnareCinch.RadiusAt(0.4f, 1f, 0.35f, 0.7f), 1e-4f,
                            "A net that landed tighter than the authored radius holds where it " +
                            "is. A cinch that opens hands the cloth outward corrections it then " +
                            "has to take back, which is energy the solver did not have.");
        }

        [Test]
        public void CinchCorrection_PullsInwardOnlyWhenOutsideTheRadius()
        {
            var axis = new SnareCinch.Axis(Vector3.zero, Vector3.up);

            Vector3 outside = SnareCinch.Correction(new Vector3(2f, 1f, 0f), axis, 1f, 1f);
            Assert.Less(outside.x, 0f, "A node outside the radius is pulled toward the axis.");
            Assert.AreEqual(0f, outside.y, 1e-5f, "The pull is radial — it must not move a node along the axis.");

            Vector3 inside = SnareCinch.Correction(new Vector3(0.5f, 1f, 0f), axis, 1f, 1f);
            Assert.AreEqual(Vector3.zero, inside,
                            "A node already inside the radius is left alone. A two-sided cinch " +
                            "would inflate the net into a tube, which is the capsule the design refuses.");
        }

        [Test]
        public void CinchCorrection_PullsOntoTheRingNotOntoTheAxis()
        {
            var axis = new SnareCinch.Axis(Vector3.zero, Vector3.up);

            Assert.AreEqual(new Vector3(-1f, 0f, 0f),
                            SnareCinch.Correction(new Vector3(2f, 0f, 0f), axis, 1f, 1f),
                            "A node 2 m out with a 1 m ring moves 1 m, not 2. A cinch that pulls " +
                            "the whole way to the axis collapses the cord into a line.");
        }

        [Test]
        public void CinchCorrection_FollowsATiltedAxisAtWorldScale()
        {
            // A tilted axis — a body toppled and lying at an angle, which is not an edge case here
            // but the ordinary one: the victim falls, and the net closes around them where they
            // land. Off every world axis on purpose, so a cinch that quietly measured about world
            // up finds this node well off its own axis and answers with the wrong radius.
            //
            // At 4 km out it doubles as the precision check, and THAT half needs the coordinates to
            // be chosen with care. An earlier version of this test put the axis at (4000, 30, 3000)
            // and the node at (4000, 32, 3000): the x and z terms cancelled bit for bit, the offset
            // came out as exactly (0, 2, 0), and the whole thing passed identically with the axis
            // at the world origin — a good tilted-axis test and a vacuous precision one. Its exact
            // Vector3 equality only held BECAUSE of that cancellation.
            //
            // So the radial offset is added on out at 4 km, in float, leaving the subtraction
            // inside Correction something real to survive. The net gun has already shipped one bug
            // of this shape, where a 0.028 m cord written in absolute world coordinates lost its
            // width.
            var origin = new Vector3(4137.31f, 30.17f, 2988.53f);
            Vector3 direction = new Vector3(1f, 0.3f, -0.2f).normalized;

            // Square to the axis and 2 m long, so a 1 m ring leaves exactly 1 m to correct.
            Vector3 radial = Vector3.Cross(direction, Vector3.up).normalized * 2f;

            Vector3 correction = SnareCinch.Correction(origin + radial,
                                                       new SnareCinch.Axis(origin, direction), 1f, 1f);
            Vector3 expected = -radial.normalized;

            // Per component, at 1e-3 m. Well above the ~2.4e-4 m a float ULP spans at 4 km, so it
            // does not chase noise, and far below the 0.028 m of cord width the shipped defect lost,
            // so it would still catch that. An exact comparison here would be asserting that the
            // arithmetic happens to cancel, which is the mistake this test used to make.
            Assert.AreEqual(expected.x, correction.x, 1e-3f, "radial x lost at world scale");
            Assert.AreEqual(expected.y, correction.y, 1e-3f, "radial y lost at world scale");
            Assert.AreEqual(expected.z, correction.z, 1e-3f, "radial z lost at world scale");
        }

        [Test]
        public void CinchCorrection_SettlesOnTheRingAndStaysThere()
        {
            var axis = new SnareCinch.Axis(Vector3.zero, Vector3.up);
            Vector3 node = new Vector3(2f, 1f, 0f);

            node += SnareCinch.Correction(node, axis, 1f, 1f);

            Assert.AreEqual(1f, new Vector2(node.x, node.z).magnitude, 1e-5f,
                            "One full-stiffness pass lands the node ON the ring, not past it.");
            Assert.AreEqual(Vector3.zero, SnareCinch.Correction(node, axis, 1f, 1f),
                            "A node at the radius is left alone. A correction that alternates " +
                            "sign across passes pumps energy in at 90 substeps a second.");
        }

        [Test]
        public void CinchCorrection_ScalesWithStiffness()
        {
            var axis = new SnareCinch.Axis(Vector3.zero, Vector3.up);

            Vector3 full = SnareCinch.Correction(new Vector3(2f, 0f, 0f), axis, 1f, 1f);
            Vector3 half = SnareCinch.Correction(new Vector3(2f, 0f, 0f), axis, 1f, 0.5f);

            Assert.AreEqual(full.magnitude * 0.5f, half.magnitude, 1e-5f,
                            "Halving stiffness must halve the correction's magnitude exactly — " +
                            "the solver blends this against other constraints assuming linearity.");
        }

        [Test]
        public void CinchCorrection_IgnoresANodeOnTheAxis()
        {
            var axis = new SnareCinch.Axis(Vector3.zero, Vector3.up);

            Assert.AreEqual(Vector3.zero, SnareCinch.Correction(new Vector3(0f, 3f, 0f), axis, 1f, 1f),
                            "A node exactly on the axis has no radial direction. Normalising a " +
                            "zero vector is a NaN that spreads through the whole lattice in one pass.");
        }

        [Test]
        public void CinchAxis_IsSampledUpright()
        {
            var axis = new SnareCinch.Axis(new Vector3(5f, 2f, 5f), new Vector3(0f, 3f, 0f));

            Assert.AreEqual(Vector3.up, axis.Direction,
                            "A non-unit up must be normalised, or the radius is measured in the " +
                            "wrong units and the cinch overshoots.");
        }

        // ── The cinch, wired into the solver ──────────────────────────────────

        /// <summary>
        /// A lattice unfurled and settled into its open shape, ready to be cinched. Shared by the
        /// tests below so they measure the cinch and not the deploy.
        ///
        /// <para>
        /// Deployed facing along world forward, so the sheet stands as a VERTICAL curtain in the
        /// x-y plane and closes about world up like a purse seine. It has no floor and never
        /// settles in the resting sense — it is in free fall for its whole life, which is fine for
        /// anything measuring a shape and useless for anything measuring absolute motion. Use
        /// <see cref="LandedLattice"/> for the latter. The distinction is easy to lose because the
        /// bed below genuinely is horizontal.
        /// </para>
        /// <para>
        /// Through <see cref="NewLattice"/>, so this runs at the same resolution and the same
        /// authored stiffnesses as every other lattice test in this file. Calling the constructor
        /// directly would leave these four tests as the only ones reading the live field
        /// initialisers — and <c>bendStiffness</c>'s own tooltip advertises a cliff just above its
        /// current value, so a retune there would move them with no cinch code touched.
        /// </para>
        /// </summary>
        private static SnareLattice CinchableLattice()
        {
            SnareLattice lattice = NewLattice();
            lattice.Deploy(Vector3.zero, Vector3.forward, HalfWidth);

            for (int i = 0; i < SettleSteps; i++) lattice.Step(Substep);

            return lattice;
        }

        /// <summary>
        /// A net dropped onto the floor and left to come to rest there, handing back the drape that
        /// put it there so the caller can keep stepping it the same way.
        ///
        /// <para>
        /// The bed anything measuring ABSOLUTE per-substep motion needs, because only a net resting
        /// on something has somewhere to come to rest. The cinch is radial about its axis and
        /// constrains nothing along it, so a cinching net with no floor under it is still in free
        /// fall — around 0.056 m a substep at this gravity and damping, fifty times any threshold
        /// worth asserting, and no implementation of anything could pass.
        /// </para>
        /// <para>
        /// It is also the faithful bed rather than merely a convenient one. SnareCatch runs the
        /// drape every substep alongside the solver while the net closes, so a net cinching in
        /// contact with the ground is the case the game actually has.
        /// </para>
        /// </summary>
        private static SnareLattice LandedLattice(out SnareDrape drape)
        {
            SnareLattice lattice = NewLattice();
            lattice.Deploy(Vector3.up * 4f, Vector3.up, HalfWidth);

            drape = new SnareDrape();

            for (int i = 0; i < DrapeSteps; i++) StepAgainstFloor(lattice, drape);

            return lattice;
        }

        /// <summary>
        /// One substep of a net with a floor under it — solve, then resolve contacts, then let the
        /// ground hold what is lying on it.
        ///
        /// <para>
        /// Through <see cref="SnareDrape"/> and <see cref="SnareLattice.GripGround"/> rather than
        /// through a clamp written locally for the convenience of a test. A clamp that only moves
        /// the position is precisely the defect the drape exists to fix, so a test that rolls its
        /// own measures the bug rather than the code.
        /// </para>
        /// </summary>
        private static void StepAgainstFloor(SnareLattice lattice, SnareDrape drape)
        {
            lattice.Step(Substep);
            drape.Resolve(lattice, System.Array.Empty<SnareDrape.Capsule>(), GroundHeight);
            lattice.GripGround(GroundHeight);
        }

        /// <summary>Worst per-substep node movement, the quantity <see cref="ASettledNetGoesQuiet"/>
        /// defines "settled" as. Every node, because a buzz confined to one corner is still a
        /// buzz.</summary>
        private static float WorstStepAgainstFloor(SnareLattice lattice, SnareDrape drape)
        {
            Vector3[] before = (Vector3[])lattice.Positions.Clone();

            StepAgainstFloor(lattice, drape);

            float worst = 0f;
            for (int i = 0; i < before.Length; i++)
                worst = Mathf.Max(worst, Vector3.Distance(before[i], lattice.Positions[i]));

            return worst;
        }

        [Test]
        public void Cinch_DrawsTheNetInTowardTheAxis()
        {
            SnareLattice lattice = CinchableLattice();
            float before = lattice.WorldBounds().extents.x;

            lattice.BeginCinch(new SnareCinch.Axis(Vector3.zero, Vector3.up), 0.5f, 0.7f);
            for (int i = 0; i < 90; i++) lattice.Step(Substep);

            Assert.Less(lattice.WorldBounds().extents.x, before * 0.6f,
                        "The net is meant to close around the body, not sit where it landed.");
        }

        [Test]
        public void Cinch_KeepsItsCordLength()
        {
            var axis = new SnareCinch.Axis(Vector3.zero, Vector3.up);

            SnareLattice lattice = CinchableLattice();
            float before = TotalStrandLength(lattice);

            lattice.BeginCinch(axis, 0.5f, 0.7f);
            for (int i = 0; i < 90; i++) lattice.Step(Substep);

            float after = TotalStrandLength(lattice);

            // Necessary, not sufficient. If the cinch were allowed to shorten the cord the net
            // would end up as a smooth tube the size of the body — the capsule the design refuses —
            // and inextensible strands mean the length has to survive and come out as folds
            // instead. But a taut length-conserving arrangement satisfies this too: cord wound
            // evenly onto a cylinder is exactly as long as cord folded beside it.
            Assert.AreEqual(before, after, before * 0.06f,
                            "Cord length must survive the cinch. Length that vanishes is a net " +
                            "that shrink-wrapped instead of folding.");

            // So here is the half that separates the two. SnareCinch.Correction is one-sided by
            // design: it pulls a node in and never pushes one back out, so cord that ends up inside
            // the ring stays there and the net keeps slack. A two-sided cinch — a projection onto a
            // cylinder — would drag every one of these nodes back out onto the surface and leave a
            // tube with nothing inside it, which is what this population being non-trivial rules
            // out. A column's worth is a low bar deliberately; the failure it guards is total.
            int inside = NodesInsideRing(lattice, axis, 0.5f * 0.5f);

            Assert.GreaterOrEqual(inside, lattice.Resolution,
                                  "only " + inside + " of " + (lattice.Resolution * lattice.Resolution) +
                                  " nodes ended up well inside the ring. A net that has folded " +
                                  "carries slack cord in its middle; a net with every node ON the " +
                                  "ring is a drawn cylinder, which is the shape this feature exists " +
                                  "to avoid.");
        }

        /// <summary>How many nodes sit strictly inside a radius about a line.</summary>
        private static int NodesInsideRing(SnareLattice lattice, SnareCinch.Axis axis, float radius)
        {
            int side = lattice.Resolution;
            int inside = 0;

            for (int row = 0; row < side; row++)
            for (int col = 0; col < side; col++)
            {
                Vector3 offset = lattice.NodeAt(row, col) - axis.Origin;
                Vector3 radial = offset - axis.Direction * Vector3.Dot(offset, axis.Direction);

                if (radial.magnitude < radius) inside++;
            }

            return inside;
        }

        /// <summary>Summed length of every strand segment. Translation-invariant on purpose.</summary>
        private static float TotalStrandLength(SnareLattice lattice)
        {
            int side = lattice.Resolution;
            float total = 0f;

            for (int row = 0; row < side; row++)
            {
                for (int col = 0; col < side; col++)
                {
                    if (col + 1 < side)
                        total += Vector3.Distance(lattice.NodeAt(row, col), lattice.NodeAt(row, col + 1));
                    if (row + 1 < side)
                        total += Vector3.Distance(lattice.NodeAt(row, col), lattice.NodeAt(row + 1, col));
                }
            }

            return total;
        }

        /// <summary>
        /// Cinch a net about the vertical line through wherever it was deployed, and report how
        /// much of its width survived. A fraction rather than a width, so runs at different places
        /// in the world are comparable.
        /// </summary>
        private static float ClosedFractionAbout(Vector3 centre, float cinchStiffness)
        {
            SnareLattice lattice = NewLattice();
            lattice.ConfigureCinchForTest(cinchStiffness);
            lattice.Deploy(centre, Vector3.forward, HalfWidth);

            for (int i = 0; i < SettleSteps; i++) lattice.Step(Substep);

            float before = lattice.WorldBounds().extents.x;

            lattice.BeginCinch(new SnareCinch.Axis(centre, Vector3.up), 0.5f, 0.7f);
            for (int i = 0; i < 90; i++) lattice.Step(Substep);

            return lattice.WorldBounds().extents.x / before;
        }

        [Test]
        public void Cinch_ClosesTheSameWayFourKilometresOut()
        {
            // Every other cinch test here runs at the world origin, and this net gun has already
            // shipped a world-scale defect — cord width lost to float precision in absolute
            // coordinates. The solver is where that would actually bite, since a cinch correction
            // near convergence is a few millimetres against a ~2.4e-4 m ULP at 4 km.
            float atOrigin = ClosedFractionAbout(Vector3.zero, DefaultCinchStiffness);
            float farOut = ClosedFractionAbout(new Vector3(4137.31f, 0f, 2988.53f), DefaultCinchStiffness);

            Assert.Less(farOut, 0.6f,
                        "the net closed to " + farOut.ToString("F3") + " of its width 4 km out. " +
                        "Same bar as Cinch_DrawsTheNetInTowardTheAxis holds at the origin.");

            // Generous on purpose, and the generosity is the point rather than a hedge. Which folds
            // a crumpling sheet picks is chaotic and will differ between the two runs; how far the
            // ring drew the cord in is not. This catches a close that degrades with distance, which
            // is what a precision defect looks like, without asserting that two chaotic systems
            // agree fold for fold.
            Assert.AreEqual(atOrigin, farOut, 0.15f,
                            "the net closed to " + atOrigin.ToString("F3") + " of its width at the " +
                            "origin but " + farOut.ToString("F3") + " of it 4 km out. The cinch has " +
                            "to be the same size wherever the fight happens.");
        }

        [Test]
        public void Cinch_ClosesFurtherWhenItIsStiffer()
        {
            // The relationship the tunable claims, and the only test that names cinchStiffness at
            // all. Without it the one unverified number in this feature is also the one number no
            // test can reach, so a retune to zero would leave every other cinch test passing on
            // whatever the strands and gravity happened to do.
            //
            // Monotonic rather than absolute: the authored 0.22 gets its verdict from the Editor
            // and from play, and pinning a measured width here would freeze a tuning decision into
            // a regression test.
            float loose = ClosedFractionAbout(Vector3.zero, 0.02f);
            float stiff = ClosedFractionAbout(Vector3.zero, 0.8f);

            Assert.Less(stiff, loose,
                        "a stiffer cinch closed to " + stiff.ToString("F3") + " and a looser one to " +
                        loose.ToString("F3") + ". If those do not order, the tunable is not " +
                        "reaching the solver — check that PerPass and Clone both carry it.");
        }

        [Test]
        public void Cinch_SettlesInsteadOfVibrating()
        {
            // On a floor, and cinched about the vertical line through its own centre. Free fall is
            // no bed for this one — see LandedLattice for why an absolute measurement needs a net
            // that has somewhere to come to rest.
            SnareLattice lattice = LandedLattice(out SnareDrape drape);

            lattice.BeginCinch(new SnareCinch.Axis(Vector3.zero, Vector3.up), 0.5f, 0.7f);
            for (int i = 0; i < 270; i++) StepAgainstFloor(lattice, drape);

            float worst = WorstStepAgainstFloor(lattice, drape);

            // Worst node, not a chosen one: an earlier version of this sampled NodeAt(0, 0), a rim
            // corner gripped against the floor for three seconds and so the most damped node in the
            // lattice, which would have let a buzz anywhere else through.
            //
            // Same quantity, same bed and therefore the same bar as ASettledNetGoesQuiet. The
            // shear/bend sweep measured 0.0003 m of per-substep motion for a healthy net against
            // 0.0142 m for the full-stiffness case, so a passing run should report a number nearer
            // the first — 0.01 is where a vibration becomes undeniable, not where a good net sits.
            // If a cinch applied AFTER the constraint loop also passes at this bar, the bar wants
            // tightening toward 0.0003 with both measurements in hand.
            Assert.Less(worst, 0.01f,
                        "a node moved " + worst.ToString("F4") + " m in one substep of a net that " +
                        "has been cinched and holding for " + (270 * Substep).ToString("F1") +
                        " seconds. A cinch relaxed outside the constraint loop leaves every " +
                        "substep ending off-constraint for the next one to yank back — a " +
                        "permanent vibration.");
        }

        [Test]
        public void Freeze_StopsTheSolverAndKeepsTheShape()
        {
            SnareLattice lattice = CinchableLattice();

            lattice.BeginCinch(new SnareCinch.Axis(Vector3.zero, Vector3.up), 0.5f, 0.7f);
            for (int i = 0; i < 90; i++) lattice.Step(Substep);

            Vector3 before = lattice.NodeAt(2, 2);
            lattice.Freeze();

            Assert.IsTrue(lattice.Frozen);

            for (int i = 0; i < 90; i++) lattice.Simulate(Substep);

            Assert.AreEqual(before, lattice.NodeAt(2, 2),
                            "A frozen lattice keeps the shape it froze with. This is the whole " +
                            "saving: a bound net costs nothing per frame.");
        }

        [Test]
        public void Freeze_SurvivesGravity()
        {
            SnareLattice lattice = CinchableLattice();
            lattice.Freeze();

            Vector3 before = lattice.NodeAt(4, 4);
            for (int i = 0; i < 300; i++) lattice.Simulate(Substep);

            Assert.AreEqual(before, lattice.NodeAt(4, 4),
                            "Freeze has to stop the integrator too, not only the constraints — a " +
                            "frozen net that still falls is a net that sinks through the floor.");
        }

        [Test]
        public void Freeze_IgnoresACinchThatArrivesLate()
        {
            // Step's guard, which the two tests above do not reach: they go through Simulate, which
            // returns on its own guard before Step is ever called.
            //
            // This is not a hypothetical ordering. A later task drives BeginCinch from a network
            // message, and a message that arrives just after the bind has completed is exactly what
            // this absorbs. Without the guard the frozen net would start closing again on a client.
            SnareLattice lattice = CinchableLattice();
            lattice.Freeze();

            Vector3 before = lattice.NodeAt(3, 3);

            lattice.BeginCinch(new SnareCinch.Axis(Vector3.zero, Vector3.up), 0.5f, 0.7f);
            for (int i = 0; i < 90; i++) lattice.Step(Substep);

            Assert.AreEqual(before, lattice.NodeAt(3, 3),
                            "A cinch that arrives after the freeze must be ignored outright. A " +
                            "bound net that starts closing again has nothing to close onto.");
        }

        [Test]
        public void Freeze_IsIdempotent()
        {
            // The second Freeze must not re-pin prev against a pos that has since been read or
            // nudged by anything else — and, more simply, must not be a way to quietly restart
            // anything. Same reason as above: the call comes from a message, and messages repeat.
            SnareLattice lattice = CinchableLattice();
            lattice.Freeze();

            Vector3 before = lattice.NodeAt(3, 3);
            lattice.Freeze();

            Assert.IsTrue(lattice.Frozen);
            Assert.AreEqual(before, lattice.NodeAt(3, 3),
                            "Freezing twice must be the same as freezing once.");
        }

        [Test]
        public void Deploy_ClearsAFrozenCinch()
        {
            // The lattice instance outlives the net. SnareLattice.Clone hands a gun a fresh
            // instance per shot today, but Deploy promises a fresh net on its own terms, and a
            // redeployed lattice that stayed frozen would be a net that never moves at all.
            SnareLattice lattice = CinchableLattice();
            lattice.BeginCinch(new SnareCinch.Axis(Vector3.zero, Vector3.up), 0.5f, 0.7f);
            for (int i = 0; i < 90; i++) lattice.Step(Substep);
            lattice.Freeze();

            lattice.Deploy(Vector3.zero, Vector3.forward, HalfWidth);

            Assert.IsFalse(lattice.Frozen, "Deploy promises a fresh net; a frozen one is not that.");

            Vector3 before = lattice.NodeAt(3, 3);
            lattice.Step(Substep);

            Assert.AreNotEqual(before, lattice.NodeAt(3, 3),
                               "A redeployed net has to move again. It must also not resume the " +
                               "old ring: a stale cinch with its clock already past the window " +
                               "snaps the new net onto the last catch's radius on substep one.");
        }

        // ── SnareBinding ──────────────────────────────────────────────────────

        [Test]
        public void Binding_HoldsNodesStillWhenTheBonesDoNotMove()
        {
            var bone = NewObject("Bone").transform;
            bone.position = new Vector3(1f, 0f, 0f);

            var nodes = new[] { new Vector3(1.1f, 0f, 0f), new Vector3(0.9f, 0f, 0f) };

            var binding = new SnareBinding();
            binding.Capture(nodes, new[] { bone });

            var resolved = new Vector3[nodes.Length];
            binding.Resolve(resolved);

            Assert.AreEqual(nodes[0].x, resolved[0].x, 1e-4f);
            Assert.AreEqual(nodes[1].x, resolved[1].x, 1e-4f);
        }

        [Test]
        public void Binding_CarriesNodesWithTheirBone()
        {
            var bone = NewObject("Bone").transform;
            bone.position = Vector3.zero;

            var nodes = new[] { new Vector3(0f, 0.2f, 0f) };

            var binding = new SnareBinding();
            binding.Capture(nodes, new[] { bone });

            bone.position = new Vector3(10f, 0f, 0f);

            var resolved = new Vector3[1];
            binding.Resolve(resolved);

            Assert.AreEqual(new Vector3(10f, 0.2f, 0f), resolved[0]);
        }

        [Test]
        public void Binding_RotatesNodesWithTheirBone()
        {
            var bone = NewObject("Bone").transform;

            var nodes = new[] { new Vector3(1f, 0f, 0f) };

            var binding = new SnareBinding();
            binding.Capture(nodes, new[] { bone });

            bone.rotation = Quaternion.Euler(0f, 90f, 0f);

            var resolved = new Vector3[1];
            binding.Resolve(resolved);

            Assert.AreEqual(0f, resolved[0].x, 1e-4f);
            Assert.AreEqual(-1f, resolved[0].z, 1e-4f,
                            "A node bound to a limb has to turn with it, or the net stays flat " +
                            "while the body folds up inside it.");
        }

        [Test]
        public void Binding_RoundTripsThroughAScaledBone()
        {
            // Uniform scale is a weak discriminator on its own — for a bone whose scale never
            // changes between Capture and Resolve, InverseTransformPoint's divide-by-scale and
            // TransformPoint's multiply-by-scale cancel exactly, so a hand-rolled position+rotation
            // implementation that drops scale entirely lands on the SAME answer. This is still worth
            // pinning down: it is the regression guard for the actual TransformPoint/
            // InverseTransformPoint round trip (a doubled or halved scale application anywhere in
            // that pair would show up here), and it documents that a scaled bone is not mishandled.
            var bone = NewObject("Bone").transform;
            bone.position = new Vector3(1f, 0f, 0f);
            bone.localScale = Vector3.one * 2f;

            var nodes = new[] { new Vector3(2f, 0f, 0f) };

            var binding = new SnareBinding();
            binding.Capture(nodes, new[] { bone });

            bone.position = new Vector3(5f, 0f, 0f);
            bone.rotation = Quaternion.Euler(0f, 90f, 0f);

            var resolved = new Vector3[1];
            binding.Resolve(resolved);

            Assert.AreEqual(5f, resolved[0].x, 1e-4f);
            Assert.AreEqual(0f, resolved[0].y, 1e-4f);
            Assert.AreEqual(-1f, resolved[0].z, 1e-4f,
                            "The captured offset has to come back out through the same scale it " +
                            "went in with, or a scaled limb drags its cord to the wrong place.");
        }

        [Test]
        public void Binding_PicksTheNearestBonePerNode()
        {
            var near = NewObject("Near").transform;
            near.position = Vector3.zero;
            var far = NewObject("Far").transform;
            far.position = new Vector3(10f, 0f, 0f);

            var nodes = new[] { new Vector3(0.1f, 0f, 0f), new Vector3(9.9f, 0f, 0f) };

            var binding = new SnareBinding();
            binding.Capture(nodes, new[] { near, far });

            near.position = new Vector3(0f, 5f, 0f);

            var resolved = new Vector3[2];
            binding.Resolve(resolved);

            Assert.AreEqual(5f, resolved[0].y, 1e-4f, "The near node follows the bone it was nearest.");
            Assert.AreEqual(0f, resolved[1].y, 1e-4f, "The far node must not have moved with it.");
        }

        [Test]
        public void Binding_FreezesANodeWhoseBoneIsDestroyed()
        {
            // The bone has to move between Capture and the pre-destroy Resolve, and the post-destroy
            // read has to land in a FRESH array. Otherwise two different bugs hide behind a pass:
            // an implementation that seeds the fallback at Capture and never updates it (a net that
            // freezes at its pre-cinch pose instead of wherever the limb last was), and an
            // implementation that just skips the write and leaves the caller's own array holding a
            // stale value (works only because the test reused one buffer, and says nothing about
            // whether the binding itself remembers anything).
            var bone = NewObject("Bone").transform;
            bone.position = new Vector3(3f, 1f, 0f);

            var nodes = new[] { new Vector3(3f, 2f, 0f) };

            var binding = new SnareBinding();
            binding.Capture(nodes, new[] { bone });

            bone.position = new Vector3(3f, 4f, 0f);
            binding.Resolve(new Vector3[1]); // the node's last LIVE position, (3, 5, 0)

            Object.DestroyImmediate(bone.gameObject);

            var afterDeath = new Vector3[1]; // fresh, so a stale caller slot proves nothing
            Assert.DoesNotThrow(() => binding.Resolve(afterDeath),
                                "A netted creature can be despawned by world streaming while a " +
                                "peer is still drawing the net that caught it. That must not " +
                                "throw once per node per frame.");

            Assert.AreEqual(new Vector3(3f, 5f, 0f), afterDeath[0],
                            "The node holds the last world position it actually resolved to WHILE " +
                            "the bone was alive. Writing the stored LOCAL offset here instead — the " +
                            "coordinate is in the dead bone's space — teleports that cord toward the " +
                            "origin for a frame, and DoesNotThrow on its own cannot see the difference.");
        }

        [Test]
        public void Binding_ReportsWhetherItBound()
        {
            var binding = new SnareBinding();
            Assert.IsFalse(binding.IsBound, "Nothing captured yet.");

            var bone = NewObject("Bone").transform;
            binding.Capture(new[] { Vector3.zero }, new[] { bone });
            Assert.IsTrue(binding.IsBound, "A capture against a real bone has something to bind to.");

            binding.Capture(new[] { Vector3.zero }, new Transform[0]);
            Assert.IsFalse(binding.IsBound,
                           "A rig whose skeleton build kept zero bones — RagdollRig.Build()'s " +
                           "`if (kept.Count == 0) return;` guard logs nothing when that happens — " +
                           "cannot be bound to. This also proves the re-capture actually clears the " +
                           "old bind rather than leaving the first Capture's bones in place.");
        }
        // ── The ragdoll hold ──────────────────────────────────────────────────

        [Test]
        public void Budget_NeverEvictsAHeldBody()
        {
            // A netted player must not be stood back up because a firefight elsewhere in the world
            // filled the ragdoll budget. Their limpness is a gameplay state with an owner, not a
            // corpse lying around waiting to be reclaimed.
            //
            // Three rigs, not two. With two, an exemption written as "give up on eviction the
            // moment an exempt rig is seen" passes just as well as one that skips past it — and
            // those are different budgets: the first lets a single captive suspend eviction for
            // every corpse in the world. The ordinary rig registered in the middle is the one that
            // has to be taken, which only happens if the search walked PAST the exempt rig sitting
            // in front of it.
            //
            // Every rig here is unbuilt, so IsSettled short-circuits true off `!IsLimp` — which
            // means these three tests only ever exercise OldestEvictable's "prefer a settled body"
            // branch, never its fallback. Nothing below says anything about how an exempt rig
            // interacts with a body that is still falling.
            var held = NewObject("Held").AddComponent<RagdollRig>();
            held.BudgetExempt = true;

            var ordinary = NewObject("Ordinary").AddComponent<RagdollRig>();
            var filler = NewObject("Filler").AddComponent<RagdollRig>();

            try
            {
                RagdollBudget.Register(held, cap: 2);
                RagdollBudget.Register(ordinary, cap: 2);
                RagdollBudget.Register(filler, cap: 2);

                Assert.IsTrue(RagdollBudget.IsLive(held),
                              "The exempt rig was evicted. A net that frees its captive when an " +
                              "unrelated creature dies is a bug nobody will reproduce on purpose.");

                Assert.IsFalse(RagdollBudget.IsLive(ordinary),
                               "Skipping the exempt rig has to mean stepping past it to the next " +
                               "evictable body, not abandoning the eviction. Otherwise one " +
                               "captive switches the whole budget off for as long as they are held.");
            }
            finally
            {
                // Unconditional, because these tests share one static list and a failed assertion
                // partway through would otherwise leave an exempt rig in it for whatever NUnit runs
                // next. RagdollRig.OnDestroy unregisters as well, so this is the belt to that brace
                // rather than the only cleanup.
                RagdollBudget.Unregister(held);
                RagdollBudget.Unregister(ordinary);
                RagdollBudget.Unregister(filler);
            }
        }

        [Test]
        public void Budget_StillEvictsOrdinaryBodies()
        {
            var first = NewObject("First").AddComponent<RagdollRig>();
            var second = NewObject("Second").AddComponent<RagdollRig>();

            try
            {
                RagdollBudget.Register(first, cap: 1);
                RagdollBudget.Register(second, cap: 1);

                Assert.IsFalse(RagdollBudget.IsLive(first),
                               "Exempting held bodies must not have exempted everything.");
            }
            finally
            {
                RagdollBudget.Unregister(first);
                RagdollBudget.Unregister(second);
            }
        }

        [Test]
        public void Budget_RunsOverCapRatherThanStallingWhenEverythingIsHeld()
        {
            // Register's eviction loop is `while (live.Count > cap)`, and the only thing that ends
            // it when the count cannot come down is OldestEvictable answering -1. Exempting rigs
            // adds a new way for every candidate to be refused at once, so the answer has to still
            // be -1 rather than an index that names a body which then never leaves the list. A
            // budget full of captives is allowed to run over cap; it is not allowed to hang.
            //
            // The termination half can only be OBSERVED, never asserted: the failure mode is a
            // spin, and a spinning Register never reaches an Assert to fail. What is pinned here is
            // the state on the way out. If this test ever stops reporting at all, that is the
            // result.
            var a = NewObject("HeldA").AddComponent<RagdollRig>();
            var b = NewObject("HeldB").AddComponent<RagdollRig>();
            a.BudgetExempt = true;
            b.BudgetExempt = true;

            try
            {
                RagdollBudget.Register(a, cap: 1);
                RagdollBudget.Register(b, cap: 1);

                Assert.IsTrue(RagdollBudget.IsLive(a) && RagdollBudget.IsLive(b),
                              "Both captives stay live and the budget simply runs one over.");
            }
            finally
            {
                RagdollBudget.Unregister(a);
                RagdollBudget.Unregister(b);
            }
        }

        /// <summary>
        /// The source of one ragdoll adapter, failed by name when the file has moved.
        ///
        /// EditMode tests run with the project root as their working directory — the assumption
        /// LeashConstraintTests already makes with a bare "Assets/..." path.
        /// </summary>
        private static string RagdollSource(string path)
        {
            Assert.That(System.IO.File.Exists(path), path + " moved — update these tests.");
            return System.IO.File.ReadAllText(path);
        }

        /// <summary>Where <paramref name="needle"/> next appears, failed by name when it does not.</summary>
        private static int IndexAfter(string source, string needle, int from, string what)
        {
            int at = source.IndexOf(needle, from, System.StringComparison.Ordinal);
            Assert.Greater(at, -1, what);
            return at;
        }

        [TestCase(PlayerRagdollSource)]
        [TestCase(AgentRagdollSource)]
        public void Hold_IsNotEndedByTheSettleCeiling(string path)
        {
            // RagdollRig.maxLimpSeconds is 4, and IsSettled goes true there whether the body agrees
            // or not. That is correct for a knockdown and must NOT end a hold: a captive is up when
            // the pool runs out, which can be thirty seconds or two minutes later. Settling means
            // the bodies sleep, which is the look we want — it does not mean standing up.
            //
            // Pinned by reading the source, because the distinction lives in a control-flow guard
            // with no runtime state to assert on — the same technique LeashConstraintTests uses to
            // pin the absence of a SetTethered call.
            //
            // Both adapters, because they are two independent copies of the same guard: with only
            // the player's pinned, deleting the creature's leaves the suite green while every
            // netted animal stands back up on the next budget eviction.
            //
            // Every index is measured from the start of Update rather than from the start of the
            // file. A bare IndexOf over the whole source is satisfied by a guard sitting in
            // HoldDown, in ReleaseHold or in a comment, none of which keeps anybody down.
            string source = RagdollSource(path);

            int update = source.IndexOf("private void Update()", System.StringComparison.Ordinal);
            Assert.Greater(update, -1, path + " lost its Update.");

            int guard = IndexAfter(source, "if (held) return;", update,
                                   path + ".Update lost its held guard.");
            int budgetRescue = IndexAfter(source, "if (!rig.IsLimp)", update,
                                          path + ".Update lost its budget-eviction rescue.");
            int recovery = IndexAfter(source, "if (Time.time < downUntil", update,
                                      path + ".Update lost its settle-and-timer recovery.");

            Assert.Less(guard, budgetRescue,
                        path + ": the held guard has to come before the `!rig.IsLimp` rescue as " +
                        "well. That branch calls Restore() the instant the rig stops being limp, " +
                        "and it is the exact path RagdollBudget used to stand a netted body up — a " +
                        "guard placed after it fixes nothing while still passing an ordering check " +
                        "against the timer alone.");

            Assert.Less(guard, recovery,
                        path + ": the held guard has to come BEFORE the settle-and-timer " +
                        "recovery, or a held captive stands up four seconds into a two-minute tie.");
        }

        [TestCase(PlayerRagdollSource)]
        [TestCase(AgentRagdollSource)]
        public void Hold_ClaimsTheBudgetExemptionAndGivesItBack(string path)
        {
            // The exemption is the whole reason a firefight across the valley cannot free a
            // captive, and nothing else in this file can see it: the components need Awake to
            // resolve `rig`, and AddComponent does not raise Awake in EditMode. Without this,
            // deleting every `rig.BudgetExempt = ` line leaves the suite green and puts the leak
            // straight back.
            //
            // Four claims, one per lifecycle edge: taken by HoldDown, given back by ReleaseHold,
            // and dropped by BOTH ends of death — OnDeath, so a corpse stops being an un-evictable
            // captive, and OnRevive, so a body that somehow kept the claim is not barred from ever
            // being netted again.
            string source = RagdollSource(path);

            int holdDown = source.IndexOf("public bool HoldDown()", System.StringComparison.Ordinal);
            Assert.Greater(holdDown, -1, path + " lost HoldDown, or stopped reporting whether it took.");

            int releaseHold = IndexAfter(source, "public void ReleaseHold()", holdDown,
                                         path + " lost ReleaseHold.");

            int claimed = IndexAfter(source, "rig.BudgetExempt = true;", holdDown,
                                     path + ".HoldDown never claims the budget exemption. A held " +
                                     "body the budget can still freeze is stood back up by the " +
                                     "`!rig.IsLimp` rescue with the net still drawn around it.");
            Assert.Less(claimed, releaseHold, path + ": the claim has to be inside HoldDown.");

            // Minor 7's refusal. Without it a rig whose skeleton build kept no bones is suspended
            // with its input switched off and never picked back up, because `held` skips the very
            // rescue that would have caught it.
            int refusal = IndexAfter(source, "if (rig.IsLimp) return true;", holdDown,
                                     path + ".HoldDown does not check that the rig actually went " +
                                     "limp. GoLimp declines a skeleton-less rig without a word, " +
                                     "and a caller told it succeeded leaves the body suspended " +
                                     "for good.");
            Assert.Less(refusal, releaseHold, path + ": the refusal has to be inside HoldDown.");

            int givenBack = IndexAfter(source, "rig.BudgetExempt = false;", releaseHold,
                                       path + ".ReleaseHold never gives the exemption back.");
            int releaseEnd = IndexAfter(source, "downUntil = 0f;", releaseHold,
                                        path + ".ReleaseHold stopped clearing downUntil.");
            Assert.Less(givenBack, releaseEnd, path + ": the release has to be inside ReleaseHold.");

            AssertClearsTheClaim(source, path, "OnDeath", "Suspend();",
                                 "a captive who dies still netted keeps an un-evictable place in " +
                                 "RagdollBudget for the rest of the session.");

            AssertClearsTheClaim(source, path, "OnRevive", "if (rig.IsLimp) Restore();",
                                 "Restore calls rig.Recover, which unregisters from the budget " +
                                 "while leaving the claim set — and HoldDown returns early on " +
                                 "`held`, so that body could never be netted again.");
        }

        /// <summary>
        /// Both halves of the claim are dropped inside one method, before the statement that ends
        /// the part of it we care about.
        ///
        /// The bound matters: asserting the two lines merely EXIST anywhere in the file passes on
        /// an implementation that clears them somewhere else entirely, which is most of the ways
        /// this can be got wrong.
        /// </summary>
        private static void AssertClearsTheClaim(string source, string path, string method,
                                                 string endsBefore, string cost)
        {
            int start = source.IndexOf("private void " + method + "()", System.StringComparison.Ordinal);
            Assert.Greater(start, -1, path + " lost " + method + ".");

            int end = IndexAfter(source, endsBefore, start,
                                 path + "." + method + " no longer contains `" + endsBefore + "`, " +
                                 "which this test uses to bound it. Re-read the method.");

            int flag = IndexAfter(source, "held = false;", start,
                                  path + "." + method + " does not clear `held`. Cost: " + cost);
            int exemption = IndexAfter(source, "rig.BudgetExempt = false;", start,
                                       path + "." + method + " does not clear the budget " +
                                       "exemption. Cost: " + cost);

            Assert.Less(flag, end, path + ": the `held` clear has to be inside " + method + ".");
            Assert.Less(exemption, end,
                        path + ": the exemption clear has to be inside " + method + ".");
        }

        // Every combination of the three facts the eviction scan has about one candidate. Exhaustive
        // rather than sampled, because the mistake this exists to catch is a plausible-looking
        // conjunction — `settled && !exempt` reads like a correct guard and evicts a captive
        // whenever nothing in the budget has come to rest yet, which is a fresh blast: the one case
        // the budget exists for. The Budget_* tests below cannot see it, because every rig a
        // scene-free test can build is unbuilt and an unbuilt rig reports IsSettled true.
        [TestCase(false, false, false, RagdollBudget.Verdict.Consider)]
        [TestCase(false, false, true, RagdollBudget.Verdict.Take)]
        [TestCase(false, true, false, RagdollBudget.Verdict.Skip)]
        [TestCase(false, true, true, RagdollBudget.Verdict.Skip)]
        [TestCase(true, false, false, RagdollBudget.Verdict.Skip)]
        [TestCase(true, false, true, RagdollBudget.Verdict.Skip)]
        [TestCase(true, true, false, RagdollBudget.Verdict.Skip)]
        [TestCase(true, true, true, RagdollBudget.Verdict.Skip)]
        public void Budget_JudgesACandidateOnAllThreeFacts(bool excluded, bool exempt, bool settled,
                                                           RagdollBudget.Verdict expected)
        {
            Assert.AreEqual(expected, RagdollBudget.Judge(excluded, exempt, settled),
                            "excluded=" + excluded + " exempt=" + exempt + " settled=" + settled +
                            ". An exempt body is not a worse candidate than a moving one, it is " +
                            "not a candidate — so exemption has to outrank settling rather than " +
                            "being anded into it.");
        }

        // ── The struggle meter ────────────────────────────────────────────────

        /// <summary>Feed the meter <paramref name="hz"/> inputs a second for <paramref name="durationSeconds"/>, then read whatever the level happens to be at that instant.</summary>
        private static float StruggleAfterDurationAt(float hz, float durationSeconds)
        {
            var meter = new SnareStruggleMeter(maxUsefulRate: 2.5f, decaySeconds: 1.2f);

            int steps = Mathf.RoundToInt(durationSeconds * 600f);
            float step = 1f / 600f;
            float sinceInput = 0f;

            for (int i = 0; i < steps; i++)
            {
                sinceInput += step;

                if (sinceInput >= 1f / hz)
                {
                    meter.Push();
                    sinceInput = 0f;
                }

                meter.Advance(step);
            }

            return meter.Level;
        }

        /// <summary>
        /// Feed the meter <paramref name="hz"/> inputs a second for long enough that its cyclic
        /// pattern has converged (20s is more than enough at every rate this file exercises — the
        /// level's own time constant is 1.2s), and read it at the one instant in that cycle that
        /// does not depend on an arbitrary sample time: immediately after the last accepted push.
        ///
        /// <para>
        /// Sampling at an arbitrary wall-clock instant instead (as <see cref="StruggleAfterDurationAt"/>
        /// does) is unusable for a sub-cap comparison here: cooldown and decaySeconds are only a
        /// factor of 3 apart, so between accepted pushes the level swings across most of its own
        /// range, and where an arbitrary sample lands in that swing depends on how the requested
        /// duration happens to line up with the push period — moving the duration by 0.1s at 0.5Hz
        /// moves the reading across roughly a third of the whole 0-1 scale. Sampling right after a
        /// push removes the swing from the measurement.
        /// </para>
        /// <para>
        /// This is also why it is the wrong tool for <see cref="Struggle_Saturates"/>: a correctly
        /// gated meter and one with no cooldown at all converge to the exact same reading here, both
        /// having saturated to the same ceiling under sustained pushing — this helper cannot see a
        /// missing cooldown. It is the right tool for a gradient between genuinely different rates,
        /// where a stable reading matters more than catching a rate that never should have counted.
        /// </para>
        /// </summary>
        private static float PeakStruggleAt(float hz)
        {
            var meter = new SnareStruggleMeter(maxUsefulRate: 2.5f, decaySeconds: 1.2f);

            const float settleSeconds = 20f;
            int steps = Mathf.RoundToInt(settleSeconds * 600f);
            float step = 1f / 600f;
            float sinceInput = 0f;
            float peak = 0f;

            for (int i = 0; i < steps; i++)
            {
                sinceInput += step;

                if (sinceInput >= 1f / hz)
                {
                    if (meter.Push()) peak = meter.Level;
                    sinceInput = 0f;
                }

                meter.Advance(step);
            }

            return peak;
        }

        /// <summary>
        /// Feed the meter <paramref name="hz"/> inputs a second, past convergence, and read the
        /// <b>time-average</b> level over a further stretch — the figure a drain or a UI meter
        /// actually integrates against, as opposed to <see cref="PeakStruggleAt"/>'s post-push
        /// instant.
        ///
        /// <para>
        /// Below the cap this average has a closed form independent of the cooldown/decaySeconds
        /// ratio that makes the peak so awkward: it is exactly <c>hz / maxUsefulRate</c>. Every
        /// accepted push adds <c>cooldown / decaySeconds</c>, and in the steady state that has to
        /// balance the average proportional loss over the same stretch (<c>average / decaySeconds</c>)
        /// — solving that balance cancels decaySeconds out entirely and leaves <c>hz * cooldown</c>.
        /// </para>
        /// <para>
        /// At or above the cap the accepted rate is pinned at maxUsefulRate by <see cref="SnareStruggleMeter.Push"/>'s
        /// leaky bucket, but the average is not perfectly flat there: an input rate whose ratio to
        /// the cap is a small rational fraction (3Hz is 6:5 against a 2.5Hz cap) settles into a
        /// bursty accept-five-then-wait-double pattern rather than perfectly even spacing, and
        /// unevenly spaced pushes average a little lower than evenly spaced ones at the same
        /// long-run accepted rate — measured, about 0.825 at 3Hz against 0.849 exactly at the cap.
        /// That is a real property of the leaky bucket, not a bug (see
        /// <see cref="Struggle_SustainedLevelDoesNotCollapseAboveTheCap"/>), so a rate comparison at
        /// or above the cap needs a tolerance wide enough to absorb it — an exact-equality check
        /// against the cap's own value would fail on a correct meter.
        /// </para>
        /// </summary>
        private static float SustainedLevelAt(float hz)
        {
            var meter = new SnareStruggleMeter(maxUsefulRate: 2.5f, decaySeconds: 1.2f);

            const float settleSeconds = 20f;
            float step = 1f / 600f;
            float sinceInput = 0f;

            int settleSteps = Mathf.RoundToInt(settleSeconds * 600f);
            for (int i = 0; i < settleSteps; i++)
            {
                sinceInput += step;
                if (sinceInput >= 1f / hz) { meter.Push(); sinceInput = 0f; }
                meter.Advance(step);
            }

            // Averaged over a window that is an exact number of input periods, so the window's own
            // start/end phase doesn't bias the reading — an unaligned window can be off by more than
            // the effect being measured: shifting an unaligned one-second-ish window by 0.1s at
            // 0.5Hz moved a trial reading from 0.08 to 0.38, entirely a sampling artefact.
            const int periodsToAverage = 200;
            int sampleSteps = Mathf.RoundToInt(periodsToAverage / hz * 600f);
            float total = 0f;

            for (int i = 0; i < sampleSteps; i++)
            {
                sinceInput += step;
                if (sinceInput >= 1f / hz) { meter.Push(); sinceInput = 0f; }
                meter.Advance(step);
                total += meter.Level;
            }

            return total / sampleSteps;
        }

        [Test]
        public void Struggle_Saturates()
        {
            float atCap = StruggleAfterDurationAt(2.5f, 1f);
            float spamming = StruggleAfterDurationAt(20f, 1f);

            // Tolerance is 0.22, measured against this file's current decay formula — re-measure
            // this number again if the formula changes underneath it (it moved from 0.06 to 0.18 to
            // 0.16 across this file's last two decay-formula changes, all from the same underlying
            // cause below, never from an anti-macro regression). The two runs sample the meter at the
            // same wall-clock instant (t=1s) but their last accepted pushes do not land at the same
            // phase against that instant: StruggleAfterDurationAt's own input-timing loop fits one
            // extra accepted push into the 20Hz run before t=1s (three vs two, because the very first
            // Push() call is always accepted and 20Hz reaches its first attempt sooner than 2.5Hz
            // does). That phase gap is irreducible — it comes from the test's own timing loop, not
            // from the meter — and it is worth about 0.16 of level here. 0.22 clears that measured
            // 0.16 with headroom while still catching real defects: an accumulator with no cooldown
            // gate at all lands the two runs about 0.50 apart here, and a gate that only enforces
            // half the real cooldown still lands about 0.42 apart — both comfortably outside 0.22.
            Assert.AreEqual(atCap, spamming, 0.22f,
                            "Mashing twenty times a second must be worth exactly what mashing " +
                            "2.5 times a second is worth. This is the anti-macro property and the " +
                            "accessibility property at the same time — they are one property.");
        }

        [Test]
        public void Struggle_RewardsGettingUpToTheCap()
        {
            // Measured, not guessed — but read this alongside SustainedLevelAt's doc comment before
            // trusting the shape of it. The mean level below the cap IS exactly rate/maxUsefulRate,
            // as an identity, regardless of how cooldown and decaySeconds compare to each other — see
            // SustainedLevelAt. What this test reads is the different quantity PeakStruggleAt
            // measures, the level immediately after a push, which is NOT that identity: at this
            // meter's shipped parameters (cooldown 0.4s, decaySeconds 1.2s — only a factor of 3
            // apart) the peak settles at (cooldown / decaySeconds) / (1 - e^(-Δt / decaySeconds)),
            // clamped to 1, which is measurably higher than rate/cap at every rate below the cap. The
            // exact numbers matter less than the shape they trace out: struggling harder keeps paying
            // off, continuously, all the way up to the cap — and only up to the cap
            // (Struggle_ReachesNearMaxAtTheCap below pins that top end, for both quantities).
            float atOneFifthCap = PeakStruggleAt(0.5f);
            float atHalfCap = PeakStruggleAt(1.25f);
            float atCap = PeakStruggleAt(2.5f);

            Assert.AreEqual(0.411f, atOneFifthCap, 0.02f,
                            "0.5Hz — a fifth of the cap rate — should settle around four-tenths of " +
                            "the way up the range, not near zero and not near the cap.");
            Assert.AreEqual(0.685f, atHalfCap, 0.02f,
                            "1.25Hz — half the cap rate — should settle noticeably above the 0.5Hz " +
                            "reading, and noticeably below the cap.");
            Assert.Less(atOneFifthCap, atHalfCap, "Below the cap, struggling harder has to do more.");
            Assert.Less(atHalfCap, atCap, "And the cap itself has to beat all of it.");
        }

        [Test]
        public void Struggle_ReachesNearMaxAtTheCap()
        {
            // Two different numbers, both worth pinning, because a later task's escape timing has to
            // pick one to calibrate against and getting that choice wrong is off by a real margin.
            //
            // The PEAK (PeakStruggleAt) genuinely reaches the top of the range: >0.99, confirmed
            // below. But nothing reads the meter only in the instant right after a push — a drain
            // ticking every frame, or anything else sampling continuously, sees the SUSTAINED
            // (time-averaged, SustainedLevelAt) level, which oscillates 0.72-1.00 and averages about
            // 0.85 at the cap, not 1. An escape time authored against "Level reaches 1" would run
            // roughly 18% slower than intended once it is actually reading the oscillating value —
            // 0.85 is the number to calibrate a drain against, not 1.
            //
            // Struggle_IsBoundedToOne only checks the ceiling is not exceeded, never that either of
            // these is actually met — that is what this test is for.
            Assert.Greater(PeakStruggleAt(2.5f), 0.99f,
                           "Struggling at the cap has to reach the top of the range, not merely " +
                           "clear some middling threshold on the way there.");
            Assert.AreEqual(0.849f, SustainedLevelAt(2.5f), 0.01f,
                            "The sustained (time-averaged) level at the cap is what a continuously-" +
                            "reading drain actually sees, and it is not the same number as the peak " +
                            "above — a later task's escape timing has to calibrate against this one, " +
                            "not against 1.");
        }

        [Test]
        public void Struggle_SustainedLevelDoesNotCollapseAboveTheCap()
        {
            // The regression this exists to catch: Push() used to reset its cooldown clock to zero
            // on every accepted press, discarding whatever the press had overshot the cooldown by.
            // That is invisible at every rate this file tested until now, because 2.5Hz and below
            // never overshoot the cooldown at all, and 20Hz aliases against it cleanly (0.05s divides
            // 0.4s exactly, so the discarded overshoot is always zero). 3Hz — the top of the user's
            // own stated 2-3 press/second band — does neither: measured, the old reset-to-zero
            // behaviour paid only 0.60 sustained here, worse than struggling at an honest 2Hz (0.80),
            // because it only let every other press land. The fix (Push's leaky bucket) restores 0.82.
            //
            // Note this compares 3Hz against 2Hz, not against the cap's own 2.5Hz value: the leaky
            // bucket's bursty accept-pattern at rates like 3Hz (see SustainedLevelAt's doc comment)
            // makes the correct, fixed sustained level at 3Hz about 0.025 BELOW the value at 2.5Hz
            // itself — a real property of the fix, not a residual bug — so a "3Hz >= 2.5Hz" assertion
            // would fail on the correct implementation. "3Hz clearly beats 2Hz" is the comparison that
            // is actually true of a correct meter and false of the regression this guards against.
            Assert.Greater(SustainedLevelAt(3f), SustainedLevelAt(2f),
                           "The top of the user's stated struggle-rate band must not sustain a lower " +
                           "level than a slower, honest rate inside that band — that is what a " +
                           "cooldown that discards its own overshoot does.");
        }

        [Test]
        public void Struggle_IsBoundedToOne()
        {
            var meter = new SnareStruggleMeter(maxUsefulRate: 2.5f, decaySeconds: 1.2f);

            float maxLevel = 0f;

            for (int i = 0; i < 500; i++)
            {
                meter.Push();
                meter.Advance(1f / 60f);
                maxLevel = Mathf.Max(maxLevel, meter.Level);
            }

            // Checked against the running maximum, not the level at frame 500. This meter oscillates
            // continuously once pushed at the cap rate, and a version with the clamp deleted entirely
            // still spends part of every cycle at or below 1 — so a check against whatever the level
            // happens to be at one arbitrary frame can land on the wrong part of that cycle and pass
            // by accident. Confirmed by running an unclamped mutant through this exact loop: its
            // reading at frame 500 was 0.88 (would have passed), while its true peak across the run
            // was 1.15 (correctly fails against the bound below).
            Assert.LessOrEqual(maxLevel, 1f,
                               "The level feeds a mass multiplier. Unbounded, it is an instant escape.");
        }

        [Test]
        public void Struggle_DecaysWhenTheVictimStops()
        {
            var meter = new SnareStruggleMeter(maxUsefulRate: 2.5f, decaySeconds: 1.2f);

            for (int i = 0; i < 20; i++)
            {
                meter.Push();
                meter.Advance(0.4f);
            }

            float fighting = meter.Level;

            for (int i = 0; i < 120; i++) meter.Advance(1f / 60f);

            Assert.Less(meter.Level, fighting * 0.5f,
                        "A captive who stops fighting has to stop draining the net, or the pool " +
                        "empties on the strength of a struggle that ended ten seconds ago.");
        }

        [Test]
        public void Struggle_RejectsInputInsideTheCooldown()
        {
            var meter = new SnareStruggleMeter(maxUsefulRate: 2.5f, decaySeconds: 1.2f);

            Assert.IsTrue(meter.Push(), "The first input always counts.");
            Assert.IsFalse(meter.Push(),
                           "A second input in the same instant is discarded. This is what throttles " +
                           "the message send as well as the meter.");

            meter.Advance(0.5f);
            Assert.IsTrue(meter.Push(), "Past the cooldown it counts again.");
        }
    }

    /// <summary>
    /// A stand-in for the tie that outlives a net — Task 9's <c>Hogtie</c>, which does not exist
    /// yet.
    ///
    /// <para>
    /// A real component rather than a mock, because what is under test is that the net ASKS the
    /// object it is letting go of: <c>SnaredBody.Release</c> looks for
    /// <see cref="SpaceGame.Items.IHoldsBodyDown"/> on the body itself, so nothing an interface
    /// double could stand in for would exercise the same lookup.
    /// </para>
    /// </summary>
    public sealed class StandInHold : MonoBehaviour, SpaceGame.Items.IHoldsBodyDown
    {
        public bool IsHoldingBodyDown => true;
    }
}
