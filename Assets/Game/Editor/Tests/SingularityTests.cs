using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceGame.Core.Persistence;
using SpaceGame.Core;
using SpaceGame.Gameplay.Status;
using SpaceGame.Items;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The bottled singularity, and the two things about it that fail without saying anything.
    ///
    /// <para>
    /// The bottle is born in a fist, inside a capsule half a metre across. Its flight traces its
    /// own arc on every machine, so what that trace throws away is the difference between an item
    /// that works and one that opens a singularity at the world origin every time — which is what
    /// it did, and what nothing in the console mentioned.
    /// </para>
    /// <para>
    /// The rest is wiring the item cannot report on itself: the four assets pointing at each other,
    /// the two network prefab entries, and the well staying OUT of the save system.
    /// </para>
    /// </summary>
    public class SingularityTests
    {
        private const string BottlePrefabPath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/BottledSingularity.prefab";
        private const string WellPrefabPath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/SingularityWell.prefab";
        private const string ItemAssetPath =
            "Assets/Game/Resources/Items/Artifacts/BottledSingularity.asset";
        private const string NetworkManagerPrefabPath =
            "Assets/Game/Prefabs/Systems/NetworkManager.prefab";
        private const string VoidScenePath =
            "Assets/Game/Scenes/Interiors/SingularityVoid.unity";
        private const string VoidInteriorPath =
            "Assets/Game/Resources/Interiors/Interior_SingularityVoid.asset";

        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void DestroyScratchObjects()
        {
            foreach (GameObject go in spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            spawned.Clear();
        }

        // ── The flight trace ───────────────────────────────────────────────────

        /// <summary>
        /// The thrower's own body is not a landing.
        ///
        /// <para>
        /// This is the defect the artifact shipped with. The bottle leaves a hand that is well
        /// inside the player's own capsule, so the very first sweep of the very first physics step
        /// stops on the thrower — every throw, on every machine, before the bottle has travelled
        /// anywhere. The net gun and the sucker puncher each carry the same exclusion.
        /// </para>
        /// </summary>
        [Test]
        public void TheFlightTraceDoesNotLandOnTheThrower()
        {
            Transform thrower = Body("Thrower", out Collider chest);
            Transform arm = Body("Arm", out Collider fist);
            arm.SetParent(thrower);

            Assert.IsFalse(SingularityWell.IsLandingHit(chest, 0.4f, Bottle(), thrower),
                           "The bottle landed on the person who threw it.");
            Assert.IsFalse(SingularityWell.IsLandingHit(fist, 0.4f, Bottle(), thrower),
                           "The bottle landed on a limb of the person who threw it — the exclusion " +
                           "has to be by ROOT, because a hand is not the root of a player.");
        }

        /// <summary>
        /// A hit at zero distance is not a landing.
        ///
        /// <para>
        /// The other half of the same defect, and the worse half. A SphereCast that STARTS already
        /// overlapping something reports distance 0 and leaves its hit point at the origin — so the
        /// bottle does not open at the thrower's feet, which would at least be visible. It opens at
        /// Vector3.zero, kilometres away, and the item reads as doing nothing at all.
        /// </para>
        /// </summary>
        [Test]
        public void AnOverlapAtTheStartOfTheSweepIsNotALanding()
        {
            Body("Wall", out Collider wall);

            Assert.IsFalse(SingularityWell.IsLandingHit(wall, 0f, Bottle(), null),
                           "A sweep that began inside a collider was read as a landing, which puts " +
                           "the singularity at the world origin.");
        }

        /// <summary>The bottle does not land on itself.</summary>
        [Test]
        public void TheBottleDoesNotLandOnItsOwnCollider()
        {
            Transform bottle = Body("Bottle", out Collider shell);

            Assert.IsFalse(SingularityWell.IsLandingHit(shell, 0.4f, bottle, null));
        }

        /// <summary>Everything else at a real distance is exactly what the bottle is looking for.</summary>
        [Test]
        public void AnythingElseAheadOfTheBottleIsALanding()
        {
            Body("Dune", out Collider ground);

            Assert.IsTrue(SingularityWell.IsLandingHit(ground, 0.4f, Bottle(), Body("Thrower", out _)),
                          "The bottle refused a clear hit, so it would fly until its flight timeout.");
        }

        // ── The timeline every machine reads off one stamp ─────────────────────

        /// <summary>
        /// The six moments happen in order, and each one gets the time it was given.
        ///
        /// <para>
        /// This is the whole of the effect's clock. Every machine derives its phase from the same
        /// stamp and the same durations, so a boundary that is off by one comparison is not a
        /// visual glitch — it is one machine swallowing a body while another is still pulling it.
        /// </para>
        /// </summary>
        [Test]
        public void ThePhasesRunInOrderAndForTheTimeTheyWereGiven()
        {
            const float inhale = 3f, flare = 0.25f, collapse = 1f, hold = 5f, spit = 0.15f;

            SingularityPhase At(float age) =>
                SingularityWell.PhaseAt(age, inhale, flare, collapse, hold, spit);

            Assert.AreEqual(SingularityPhase.Inhaling, At(0f));
            Assert.AreEqual(SingularityPhase.Inhaling, At(2.99f));
            Assert.AreEqual(SingularityPhase.Flaring, At(3.01f));
            Assert.AreEqual(SingularityPhase.Collapsing, At(3.3f));
            Assert.AreEqual(SingularityPhase.Collapsing, At(4.2f));
            Assert.AreEqual(SingularityPhase.Held, At(4.3f));
            Assert.AreEqual(SingularityPhase.Held, At(9.2f));
            Assert.AreEqual(SingularityPhase.Spitting, At(9.3f));
            Assert.AreEqual(SingularityPhase.Spent, At(9.5f));
            Assert.AreEqual(SingularityPhase.Spent, At(600f));
        }

        /// <summary>
        /// No phase is ever skipped, however coarsely the clock is sampled.
        ///
        /// The boundaries are running totals, so a duration retuned to zero collapses its own phase
        /// and moves everything after it — what must never happen is a gap, an overlap, or an order
        /// that goes backwards.
        /// </summary>
        [Test]
        public void ThePhasesNeverGoBackwards()
        {
            SingularityPhase previous = SingularityPhase.Inhaling;

            for (float age = 0f; age < 12f; age += 0.01f)
            {
                SingularityPhase now = SingularityWell.PhaseAt(age, 3f, 0.25f, 1f, 5f, 0.15f);

                Assert.GreaterOrEqual((int)now, (int)previous,
                                      $"The bottle went back from {previous} to {now} at {age:F2} s.");
                previous = now;
            }

            Assert.AreEqual(SingularityPhase.Spent, previous);
        }

        // ── Being swallowed ────────────────────────────────────────────────────

        /// <summary>
        /// The veil puts back exactly what it took, and nothing else.
        ///
        /// <para>
        /// The half that goes wrong silently is the second clause. A body arrives with some of its
        /// renderers already off — a head hidden from its owner's own camera, a spent variant's
        /// mesh — and a veil that switched everything on at the end would hand those back visible.
        /// The player sees their own head from the inside and nothing has thrown.
        /// </para>
        /// </summary>
        [Test]
        public void TheVeilRestoresWhatItTookAndLeavesTheRestAlone()
        {
            var body = new GameObject("Body");
            spawned.Add(body);

            MeshRenderer lit = body.AddComponent<MeshRenderer>();
            Collider solid = body.AddComponent<BoxCollider>();

            var alreadyOff = new GameObject("Hidden");
            alreadyOff.transform.SetParent(body.transform);
            MeshRenderer off = alreadyOff.AddComponent<MeshRenderer>();
            off.enabled = false;

            var holder = new object();

            BodyVeil.Hide(body, holder);

            Assert.IsFalse(lit.enabled, "The veil left a renderer visible.");
            Assert.IsFalse(solid.enabled, "The veil left a collider in every physics query.");
            Assert.IsTrue(BodyVeil.IsHidden(body));

            BodyVeil.Show(body, holder);

            Assert.IsTrue(lit.enabled, "The veil did not give the renderer back.");
            Assert.IsTrue(solid.enabled, "The veil did not give the collider back.");
            Assert.IsFalse(off.enabled, "The veil switched ON a renderer somebody else had off.");
            Assert.IsFalse(BodyVeil.IsHidden(body));
        }

        /// <summary>
        /// Two holders, one body: the first to let go does not un-hide it.
        ///
        /// Two wells whose radii overlap is the ordinary case, and a body given back while the
        /// second one still has it would pop into view inside a black sphere.
        /// </summary>
        [Test]
        public void AVeiledBodyStaysHiddenUntilEveryHolderLetsGo()
        {
            var body = new GameObject("Body");
            spawned.Add(body);

            MeshRenderer lit = body.AddComponent<MeshRenderer>();

            var first = new object();
            var second = new object();

            BodyVeil.Hide(body, first);
            BodyVeil.Hide(body, second);
            BodyVeil.Show(body, first);

            Assert.IsFalse(lit.enabled, "One holder letting go gave the body back to everybody.");

            BodyVeil.Show(body, second);
            Assert.IsTrue(lit.enabled);
        }

        /// <summary>
        /// A holder that goes away without saying what it took gives all of it back.
        ///
        /// The well is despawned mid-hold on every path that is not the spit — the budget retires
        /// it, its chunk unloads, the world is quit under it. A body left veiled by a dead well is
        /// invisible for the rest of the session with nothing alive that knows why.
        /// </summary>
        [Test]
        public void AbandoningAHolderGivesEveryBodyBack()
        {
            var body = new GameObject("Body");
            spawned.Add(body);

            MeshRenderer lit = body.AddComponent<MeshRenderer>();
            var holder = new object();

            BodyVeil.Hide(body, holder);
            BodyVeil.Abandon(holder);

            Assert.IsTrue(lit.enabled, "A body outlived the thing hiding it, still hidden.");
        }

        /// <summary>
        /// Being swallowed leaves a body able to act, and cannot be applied twice.
        ///
        /// <para>
        /// <b>Not suppressing is the claim worth pinning.</b> The void is a room you walk around
        /// in, not a holding cell, so this condition is deliberately not <c>Frozen</c> under
        /// another name — and every other suppressing condition in the game takes the player's
        /// input through <c>PlayerRagdoll</c>, which would leave a swallowed player standing
        /// motionless in the white room wondering what broke.
        /// </para>
        /// <para>
        /// The refusal to refresh matters more here than for any other kind: a second application
        /// sends a body that is ALREADY in the void into it again, which <c>InteriorManager</c>
        /// reads as a re-entry and answers by overwriting the return position with a point inside
        /// the void. That body could never be put back in the desert.
        /// </para>
        /// </summary>
        [Test]
        public void BeingSwallowedDoesNotSuppressAndCannotBeAppliedTwice()
        {
            var status = new SwallowedStatus();

            Assert.IsFalse(status.Suppresses,
                           "Swallowed suppresses, so a body in the void cannot move — the void is " +
                           "a room to walk around in, not a hold.");
            Assert.AreEqual(StatusKind.Swallowed, status.Kind);
            Assert.IsFalse(status.CanApply(null, running: true),
                           "A second bottle re-applied a hold that was already running, which " +
                           "overwrites the return position with a point inside the void itself.");
            Assert.IsTrue(status.CanApply(null, running: false));
        }

        // ── The void ───────────────────────────────────────────────────────────

        /// <summary>
        /// The white room exists, is in Build Settings, and the bottle knows where it is.
        ///
        /// <para>
        /// Four assets that have to agree and cannot see each other: the scene file, its entry in
        /// Build Settings, the <c>InteriorScene</c> naming it, and the well pointing at that. Any
        /// one of them missing is a singularity that eats and takes nothing anywhere — and the
        /// Build Settings one fails on CLIENTS ONLY, because Netcode resolves a scene by a hash of
        /// its path and a scene missing from a build is a join that dies rather than a room that is
        /// empty.
        /// </para>
        /// <para>
        /// Run <c>Tools > SpaceGame > World > Build Singularity Void</c> to make all four at once.
        /// </para>
        /// </summary>
        [Test]
        public void TheVoidIsBuiltRegisteredAndWiredToTheBottle()
        {
            Assert.IsTrue(System.IO.File.Exists(VoidScenePath),
                          $"No void scene at {VoidScenePath}. Run Tools > SpaceGame > World > " +
                          "Build Singularity Void.");

            bool registered = UnityEditor.EditorBuildSettings.scenes
                .Any(entry => entry.path == VoidScenePath && entry.enabled);

            Assert.IsTrue(registered,
                          "The void scene is not enabled in Build Settings, so LoadSceneAsync " +
                          "fails at runtime and a client cannot resolve it at all.");

            var interior = AssetDatabase.LoadAssetAtPath<InteriorScene>(VoidInteriorPath);
            Assert.IsNotNull(interior, $"No InteriorScene asset at {VoidInteriorPath}.");
            Assert.AreEqual(SingularityVoid.SceneName, interior.SceneName);
            Assert.AreEqual(SingularityVoid.AnchorId, interior.SpawnAnchorId);

            GameObject well = AssetDatabase.LoadAssetAtPath<GameObject>(WellPrefabPath);
            Assert.IsNotNull(well, $"No prefab at {WellPrefabPath}.");

            var serialized = new SerializedObject(well.GetComponent<SingularityWell>());
            Assert.AreEqual(interior,
                            serialized.FindProperty("voidInterior").objectReferenceValue,
                            "The well does not point at the void, so a swallowed body is only " +
                            "hidden where it stood rather than taken anywhere.");
        }

        /// <summary>
        /// The receiver's per-kind arrays are sized to the number of kinds there actually are.
        ///
        /// <c>StatusKinds.Count</c> is written out rather than reflected, precisely so that it
        /// indexes an array on a hot path — which means adding a sixth kind and forgetting the
        /// count is an <c>IndexOutOfRange</c> on the first body that takes one, or worse a kind
        /// that silently never runs.
        /// </summary>
        [Test]
        public void EveryStatusKindHasASlot()
        {
            Assert.AreEqual(System.Enum.GetValues(typeof(StatusKind)).Length, StatusKinds.Count,
                            "StatusKinds.Count and the StatusKind enum have drifted apart.");
        }

        // ── The scatter every machine has to agree on ──────────────────────────

        /// <summary>
        /// The release's fan is a function of the seed and the direction, and nothing else.
        ///
        /// Two machines that know the seed must throw the same crate the same way. Anything that
        /// consulted a per-machine RNG would pass on one machine and diverge on the other, which is
        /// a pile that disagrees between players rather than an exception anyone can see.
        /// </summary>
        [Test]
        public void TheScatterIsTheSameOnEveryMachineAndKeepsItsSpeed()
        {
            var velocity = new Vector3(3f, 8f, -5f);

            Vector3 first = SingularityMath.Scatter(12345, velocity, 22f, 3f);
            Vector3 second = SingularityMath.Scatter(12345, velocity, 22f, 3f);

            Assert.AreEqual(first, second, "The same seed and direction scattered two different ways.");
            Assert.AreEqual(velocity.magnitude, first.magnitude, 1e-3f,
                            "The scatter changed how hard the body was thrown, not just where.");
            Assert.AreNotEqual(SingularityMath.Scatter(999, velocity, 22f, 3f), first,
                               "Every seed fans the pile out the same way, so the seed buys nothing.");
        }

        /// <summary>A scatter of zero degrees is the throw itself, untouched.</summary>
        [Test]
        public void AZeroScatterLeavesTheFlingAlone()
        {
            var velocity = new Vector3(1f, 2f, 3f);

            Assert.AreEqual(velocity, SingularityMath.Scatter(7, velocity, 0f, 3f));
        }

        /// <summary>
        /// The drawn arc and the traced arc are the same arc: the reported velocity is the
        /// derivative of the reported position. If they drift, the bottle points one way and
        /// travels another.
        /// </summary>
        [Test]
        public void TheBottlesHeadingIsTheDerivativeOfItsArc()
        {
            var origin = new Vector3(2f, 5f, -1f);
            var launch = new Vector3(6f, 9f, 2f);
            var gravity = new Vector3(0f, -18f, 0f);
            const float at = 0.7f;
            const float h = 1e-3f;

            Vector3 measured = (SingularityMath.FlightPoint(origin, launch, gravity, at + h) -
                                SingularityMath.FlightPoint(origin, launch, gravity, at - h)) / (2f * h);

            Vector3 reported = SingularityMath.FlightVelocity(launch, gravity, at);

            Assert.AreEqual(measured.x, reported.x, 1e-2f);
            Assert.AreEqual(measured.y, reported.y, 1e-2f);
            Assert.AreEqual(measured.z, reported.z, 1e-2f);
        }

        // ── The four assets, and the two entries ───────────────────────────────

        /// <summary>
        /// The item asset and the bottle prefab point at each other, and the bottle knows which
        /// well to throw. Any one of the three going null is a silent failure: an item that
        /// equips and does nothing, or a hotbar slot that comes back empty from a save.
        /// </summary>
        [Test]
        public void TheItemThePrefabAndTheWellAllPointAtEachOther()
        {
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemAssetPath);
            Assert.IsNotNull(item, $"No item asset at {ItemAssetPath}. It must live under " +
                                   "Resources/Items or the registry never sees it.");

            GameObject bottle = AssetDatabase.LoadAssetAtPath<GameObject>(BottlePrefabPath);
            Assert.IsNotNull(bottle, $"No prefab at {BottlePrefabPath}.");
            Assert.AreEqual(bottle, item.itemPrefab, "The item asset does not point at the bottle prefab.");

            var pickup = bottle.GetComponent<PickupableItem>();
            Assert.IsNotNull(pickup, "The bottle prefab has no PickupableItem, so a dropped bottle " +
                                     "cannot be picked back up.");
            Assert.AreEqual(item, new SerializedObject(pickup).FindProperty("item").objectReferenceValue,
                            "The bottle prefab does not point back at its item asset.");

            var artifact = bottle.GetComponent<BottledSingularityArtifact>();
            Assert.IsNotNull(artifact, "The bottle prefab has no BottledSingularityArtifact.");

            var serialized = new SerializedObject(artifact);
            Assert.IsNotNull(serialized.FindProperty("wellPrefab").objectReferenceValue,
                             "No well prefab is wired, so every throw produces nothing.");
            Assert.IsNotNull(serialized.FindProperty("throwPivot").objectReferenceValue,
                             "No throw pivot, so the bottle leaves from the eye rather than the hand.");
        }

        /// <summary>
        /// Both prefabs are registered network prefabs.
        ///
        /// The bottle because dropping a hotbar slot spawns it, the well because throwing one does.
        /// An unregistered prefab fails on CLIENTS ONLY — the host instantiates its own copy and
        /// never consults the list — so single-player, which runs as a host of one, cannot find it.
        /// </summary>
        [Test]
        public void BothTheBottleAndTheWellAreRegisteredNetworkPrefabs()
        {
            List<GameObject> registered = RegisteredPrefabs();

            Assert.Contains(AssetDatabase.LoadAssetAtPath<GameObject>(BottlePrefabPath), registered,
                            "The dropped bottle is not a registered network prefab.");
            Assert.Contains(AssetDatabase.LoadAssetAtPath<GameObject>(WellPrefabPath), registered,
                            "The thrown well is not a registered network prefab, so a client's " +
                            "throw produces nothing and says nothing.");
        }

        /// <summary>
        /// The horizon is on the prefab, wired to the shell, and is not a surface.
        ///
        /// <para>
        /// The sphere and the ring are generated by <c>SingularityBuilder</c> rather than modelled,
        /// so nothing but this notices if a run of it is skipped: the well would work perfectly and
        /// be completely invisible. A collider on either of them is the other half — the horizon is
        /// a picture of a field, and a solid one would stop the next bottle's landing trace, block
        /// every aim ray inside eight metres, and be caught by the pull it is drawing.
        /// </para>
        /// </summary>
        [Test]
        public void TheHorizonIsBuiltWiredAndIntangible()
        {
            GameObject well = AssetDatabase.LoadAssetAtPath<GameObject>(WellPrefabPath);
            Assert.IsNotNull(well, $"No prefab at {WellPrefabPath}.");

            var shell = well.GetComponentInChildren<SingularityShell>(true);
            Assert.IsNotNull(shell, "The well prefab has no SingularityShell.");

            var serialized = new SerializedObject(shell);

            var sphere = serialized.FindProperty("sphere").objectReferenceValue as Transform;
            var ring = serialized.FindProperty("ring").objectReferenceValue as Transform;

            Assert.IsNotNull(sphere, "No horizon sphere is wired, so the singularity is invisible. " +
                                     "Run Tools > SpaceGame > Items > Build Singularity Horizon.");
            Assert.IsNotNull(ring, "No horizon ring is wired.");
            Assert.IsNotNull(serialized.FindProperty("sphereRenderer").objectReferenceValue,
                             "No renderer is wired, so the horizon can never turn black.");

            Assert.IsNull(sphere.GetComponent<Collider>(),
                          "The horizon sphere is solid, so it is something the world can hit.");
            Assert.IsNull(ring.GetComponent<Collider>(), "The horizon ring is solid.");

            Assert.IsFalse(sphere.gameObject.activeSelf,
                           "The horizon is switched on in the prefab, so an unopened bottle wears " +
                           "an eight-metre ball from the moment it is thrown.");
        }

        /// <summary>
        /// The well stays out of the save system.
        ///
        /// <para>
        /// A bottle mid-effect is three seconds of world state, and the design says a save taken
        /// during it loads with everything at rest. That is not a decision the saver makes: it is a
        /// decision about what the prefab CARRIES, because SaveablePolicy opts in anything with a
        /// non-kinematic Rigidbody, a HealthComponent, a PickupableItem or a NavMeshAgent. Adding
        /// any one of them later gives the well a SaveableEntity with no stamped prefab id, which
        /// is captured into every save file and dropped with a warning on every load.
        /// </para>
        /// </summary>
        [Test]
        public void TheThrownWellIsNotSaved()
        {
            GameObject well = AssetDatabase.LoadAssetAtPath<GameObject>(WellPrefabPath);
            Assert.IsNotNull(well, $"No prefab at {WellPrefabPath}.");

            Assert.IsFalse(SaveablePolicy.NeedsSaving(well, out string why),
                           $"The well opted itself into the save system ({why}). A three-second " +
                           "effect must not reach a save file.");

            var body = well.GetComponent<Rigidbody>();
            if (body != null)
                Assert.IsTrue(body.isKinematic,
                              "The well's Rigidbody is not kinematic, which both opts it into the " +
                              "save system and makes its own placement fight the physics step.");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>A throwaway object with a collider on it, cleaned up after the test.</summary>
        private Transform Body(string name, out Collider collider)
        {
            var go = new GameObject(name);
            spawned.Add(go);

            collider = go.AddComponent<BoxCollider>();
            return go.transform;
        }

        /// <summary>A stand-in for the bottle itself, when the test does not care what it is.</summary>
        private Transform Bottle() => Body("Bottle", out _);

        private static List<GameObject> RegisteredPrefabs()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
            Assert.IsNotNull(prefab, $"No NetworkManager prefab at {NetworkManagerPrefabPath}.");

            var manager = prefab.GetComponent<NetworkManager>();
            Assert.IsNotNull(manager, "The NetworkManager prefab has no NetworkManager component.");

            return manager.NetworkConfig.Prefabs.NetworkPrefabsLists
                .Where(list => list != null)
                .SelectMany(list => list.PrefabList)
                .Where(entry => entry?.Prefab != null)
                .Select(entry => entry.Prefab)
                .ToList();
        }
    }
}

