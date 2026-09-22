// What makes a passenger invisible to the machine carrying them, and to nothing else.
//
// The exemption is a single per-entity opt-out layered on top of the faction answer, and it is
// deliberately narrow: the player on the conjurer's shoulder is still a hostile member of
// HumansFaction to every other robot in the world. The tests here pin both halves of that — the
// carrier cannot see them, and everybody else still can — because the failure modes point in
// opposite directions. Too narrow and the machine turns round and fights its own passenger; too
// broad and riding a robot is a cloaking device.
//
// It lives on EntityFaction rather than on AgentTargeting on purpose, and one test guards that
// choice: AgentTargeting is not the only thing that hunts. DormantModule, FleeModule, WatchModule
// and KeepDistanceModule all ask EntityTargetRegistry directly, so an exemption those cannot see is one
// a sleeping conjurer wakes up in spite of.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class PassengerSeatTests
    {
        private const string FactionDir = "Assets/Game/ScriptableObjects/Factions/Core";
        private const string ClankerPath = FactionDir + "/ClankerFaction.asset";
        private const string HumansPath = FactionDir + "/HumansFaction.asset";
        private const string TablePath = FactionDir + "/GlobalRelationships.asset";
        private const string ConjurerPath = "Assets/Game/Prefabs/Agents/creatures/LightningConjurer.prefab";

        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"Missing asset: {path}");
            return asset;
        }

        /// Awake and OnEnable, by hand. Unity runs neither for a component created in edit mode, and
        /// AgentTargeting caches its own EntityFaction in Awake — without this it holds no faction,
        /// every exemption check short-circuits to false, and the tests pass on a component that was
        /// never switched on. Same helper, same reason, as ProvocationTests.
        private static void Boot(GameObject go)
        {
            foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
            {
                Call(mb, "Awake");
                Call(mb, "OnEnable");
            }
        }

        private static void Call(MonoBehaviour mb, string method)
        {
            MethodInfo m = mb.GetType().GetMethod(
                method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            m?.Invoke(mb, null);
        }

        private EntityFaction Entity(string name, string factionPath)
        {
            var go = new GameObject(name);
            spawned.Add(go);

            // TargetResolution wants a live IDamageable before it will call anything viable.
            go.AddComponent<HealthComponent>();

            EntityFaction faction = go.AddComponent<EntityFaction>();
            faction.SetFaction(Load<FactionDefinition>(factionPath), Load<FactionRelationshipTable>(TablePath));

            Boot(go);

            // SetFaction ran after OnEnable had already registered it; re-register so the registry
            // holds an entity whose faction is the one the test asked for.
            EntityTargetRegistry.Register(faction);
            return faction;
        }

        private static List<EntityFaction> HostilesSeenBy(EntityFaction owner)
        {
            var results = new List<EntityFaction>();
            EntityTargetRegistry.Query(owner, FactionRelationship.Hostile, owner.transform.position,
                                       1000f, results);
            return results;
        }

        // ─────────────────────────────────────────────
        //  The exemption, at the registry
        // ─────────────────────────────────────────────

        [Test]
        public void Carrier_CannotSeeThePassengerItIsCarrying()
        {
            EntityFaction robot = Entity("Conjurer", ClankerPath);
            EntityFaction player = Entity("Player", HumansPath);

            Assert.Contains(player, HostilesSeenBy(robot),
                "Precondition: ClankerFaction is Hostile toward HumansFaction in GlobalRelationships, " +
                "so an un-ridden conjurer must see the player. If this fails the relationship table " +
                "changed and the rest of these tests are asserting on nothing.");

            robot.Ignore(player);

            CollectionAssert.DoesNotContain(HostilesSeenBy(robot), player,
                "A seated passenger has to drop out of the carrier's own registry queries. Filtering " +
                "in AgentTargeting alone is not enough — DormantModule and FleeModule ask the " +
                "registry directly, so a sleeping conjurer would still wake for its own rider.");
        }

        [Test]
        public void Exemption_IsScopedToTheOneCarrier()
        {
            EntityFaction carrier = Entity("Carrier", ClankerPath);
            EntityFaction bystander = Entity("OtherRobot", ClankerPath);
            EntityFaction player = Entity("Player", HumansPath);

            carrier.Ignore(player);

            CollectionAssert.Contains(HostilesSeenBy(bystander), player,
                "Riding one robot must not hide the player from every other one. An exemption that " +
                "leaked to the whole faction would make a shoulder ride a cloaking device.");
        }

        [Test]
        public void Dismounting_MakesTheRiderVisibleAgain()
        {
            EntityFaction robot = Entity("Conjurer", ClankerPath);
            EntityFaction player = Entity("Player", HumansPath);

            robot.Ignore(player);
            robot.StopIgnoring(player);

            CollectionAssert.Contains(HostilesSeenBy(robot), player,
                "Getting off has to hand the player back. An exemption that outlived the ride would " +
                "leave one conjurer permanently unable to fight one player, with nothing to explain it.");
        }

        [Test]
        public void DisablingTheCarrier_ForgetsWhatItWasOverlooking()
        {
            EntityFaction robot = Entity("Conjurer", ClankerPath);
            EntityFaction player = Entity("Player", HumansPath);

            robot.Ignore(player);
            Call(robot, "OnDisable");

            // Re-register: OnDisable also unregistered it, and the question here is about the
            // exemption list rather than about registry membership.
            EntityTargetRegistry.Register(robot);

            CollectionAssert.Contains(HostilesSeenBy(robot), player,
                "A creature that is despawned or streamed out and brought back must not still be " +
                "carrying an exemption for a rider who may have got off in the meantime. The seat " +
                "re-grants it on the way back in.");
        }

        // ─────────────────────────────────────────────
        //  The exemption, at AgentTargeting
        // ─────────────────────────────────────────────

        private AgentTargeting Targeting(EntityFaction entity)
        {
            var targeting = entity.gameObject.AddComponent<AgentTargeting>();
            Call(targeting, "Awake");
            Call(targeting, "OnEnable");
            return targeting;
        }

        [Test]
        public void ForceTarget_RefusesAnExemptEntity()
        {
            EntityFaction robot = Entity("Conjurer", ClankerPath);
            EntityFaction player = Entity("Player", HumansPath);
            AgentTargeting targeting = Targeting(robot);

            robot.Ignore(player);
            targeting.ForceTarget(player.transform);

            Assert.IsFalse(targeting.HasTarget,
                "ForceTarget is how an ally alert and a heard noise acquire, and it deliberately " +
                "bypasses line of sight. Left unguarded it is a back door onto the passenger — one " +
                "shout from another robot and the carrier is fighting its own rider through a wall.");
        }

        [Test]
        public void ForgetIgnored_DropsATargetAcquiredBeforeTheRiderSatDown()
        {
            EntityFaction robot = Entity("Conjurer", ClankerPath);
            EntityFaction player = Entity("Player", HumansPath);
            AgentTargeting targeting = Targeting(robot);

            targeting.ForceTarget(player.transform);
            Assert.IsTrue(targeting.HasTarget, "Precondition: the robot acquired the player first.");

            robot.Ignore(player);
            targeting.ForgetIgnored();

            Assert.IsFalse(targeting.HasTarget,
                "Granting the exemption only keeps the rider out of FUTURE queries. Without this " +
                "call the held target survives until the next re-evaluation — long enough for a " +
                "conjurer to put a bolt on the player who has just climbed onto its shoulder.");
            Assert.IsFalse(targeting.HasLastKnownPosition,
                "The memory goes too, or SearchModule walks the machine over to investigate a " +
                "player it is carrying.");
        }

        // ─────────────────────────────────────────────
        //  The conjurer's own seat
        // ─────────────────────────────────────────────

        [Test]
        public void Conjurer_CarriesAPassengerSeatAndNoControls()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ConjurerPath);
            Assert.IsNotNull(prefab, $"Missing prefab: {ConjurerPath}. Run Tools > Creatures > Build Lightning Conjurer.");

            var mount = prefab.GetComponent<MountModule>();
            Assert.IsNotNull(mount, "The conjurer has no MountModule, so there is nothing to sit on. " +
                                    "Re-run Tools > Creatures > Build Lightning Conjurer.");
            Assert.IsNotNull(prefab.GetComponent<PassengerSeat>(),
                "MountModule alone seats a rider the creature can still see and shoot at.");
            Assert.IsNotNull(prefab.GetComponent<MountNetworkSync>(),
                "Without this the rider sits on the shoulder on their own screen only.");

            Assert.IsNull(prefab.GetComponent<SteerModule>(),
                "A passenger seat must have NO SteerModule. Its presence is what MountNetworkSync " +
                "reads to decide whether to hand the mount to the rider's client — and handing over " +
                "an AI creature moves its targeting and its combat onto the passenger's PC.");
            Assert.IsFalse(mount.RiderDrives,
                "RiderDrives is the netcode's question, and for a passenger seat the answer is no.");

            Assert.IsTrue(mount.AllowAISelfMovementWhenMounted,
                "Off, MountModule disables every other behaviour module while ridden and the " +
                "creature stands rooted to the spot with a player on its shoulder. The whole brief " +
                "is that it carries on wandering and fighting.");
        }

        [Test]
        public void Conjurer_PutsItsRiderDownOnTheGround()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ConjurerPath);
            Assert.IsNotNull(prefab, $"Missing prefab: {ConjurerPath}");

            Transform dismount = prefab.transform.Find("DismountPoint");
            Assert.IsNotNull(dismount,
                "MountModule's fallback puts the rider one body-width to the side of the mount's " +
                "own origin. On a seat fifteen metres up that is a fifteen-metre drop, so this " +
                "prefab has to carry an explicit dismount point at ground level.");
            Assert.Less(dismount.localPosition.y, 1f,
                "The dismount point is where the player is placed, so it belongs at the machine's " +
                "feet — not at the seat.");
        }

        // ─────────────────────────────────────────────
        //  The seat's own wiring, end to end
        // ─────────────────────────────────────────────
        //
        // Everything above tests EntityFaction.Ignore/StopIgnoring by calling them directly, which
        // proves the mechanism and proves nothing about whether PassengerSeat actually drives it.
        // That gap is exactly where a bug lived: the exemption survived the dismount and the
        // creature never fought the player again. These drive the real thing — TryMount, Dismount,
        // and the events between them — so the wiring is under test and not just the primitive.

        /// A carrier with the seat on it, booted so PassengerSeat has subscribed to the mount.
        private PassengerSeat Carrier(out EntityFaction faction, out MountModule mount,
                                      out AgentTargeting targeting)
        {
            var go = new GameObject("Conjurer");
            spawned.Add(go);

            faction = go.AddComponent<EntityFaction>();
            faction.SetFaction(Load<FactionDefinition>(ClankerPath), Load<FactionRelationshipTable>(TablePath));
            targeting = go.AddComponent<AgentTargeting>();
            mount = go.AddComponent<MountModule>();
            PassengerSeat seat = go.AddComponent<PassengerSeat>();
            ClearMountCooldown(mount);

            Boot(go);
            EntityTargetRegistry.Register(faction);
            return seat;
        }

        /// A rider MountModule will accept: it wants an Interactor with a PlayerMovement over it.
        private Interactor Rider(out EntityFaction faction)
        {
            var go = new GameObject("Player");
            spawned.Add(go);

            go.AddComponent<HealthComponent>();
            faction = go.AddComponent<EntityFaction>();
            faction.SetFaction(Load<FactionDefinition>(HumansPath), Load<FactionRelationshipTable>(TablePath));
            go.AddComponent<PlayerMovement>();
            var interactor = go.AddComponent<Interactor>();

            EntityTargetRegistry.Register(faction);
            return interactor;
        }

        [Test]
        public void RidingThenGettingOff_LeavesTheRiderVisibleAgain()
        {
            PassengerSeat seat = Carrier(out EntityFaction carrier, out MountModule mount, out _);
            Interactor interactor = Rider(out EntityFaction rider);

            Assert.Contains(rider, HostilesSeenBy(carrier),
                "Precondition: the creature can see the player before they climb on.");

            Assert.IsTrue(mount.TryMount(interactor, null), "The rider should have been seated.");
            CollectionAssert.DoesNotContain(HostilesSeenBy(carrier), rider,
                "A seated passenger has to be invisible to the machine carrying them.");

            mount.Dismount();

            CollectionAssert.Contains(HostilesSeenBy(carrier), rider,
                "The exemption outlived the ride. Getting off has to hand the player back, or that " +
                "one creature can never fight that one player again — with nothing on either of " +
                "them to explain why.");
        }

        [Test]
        public void ACarrierTornDownUnderItsRider_DoesNotStrandTheExemption()
        {
            PassengerSeat seat = Carrier(out EntityFaction carrier, out MountModule mount, out _);
            Interactor interactor = Rider(out EntityFaction rider);

            Assert.IsTrue(mount.TryMount(interactor, null));

            // The teardown path. MountModule.OnDisable hands an inactive mount to AbandonRider,
            // which empties the seat and raises no event at all — so a subscription alone would
            // leave the exemption standing for a rider who is no longer aboard.
            Call(seat, "OnDisable");

            CollectionAssert.Contains(HostilesSeenBy(carrier), rider,
                "A carrier that was switched off mid-ride kept its passenger invisible. The seat " +
                "has to give the exemption back on every way out, not just the tidy one.");
        }

        // ─────────────────────────────────────────────
        //  What a dismount is allowed to switch back on
        // ─────────────────────────────────────────────
        //
        // The reported bug — "after I dismount I am still invisible to the robot" — was not the
        // exemption at all. MountModule.RestoreModuleSuppression switched every behaviour module
        // ON at dismount, including ones it had never switched off. DormantModule disables ITSELF
        // once its wake animation finishes (that is how it hands the ladder down), so a dismount
        // put it back at the top of the ladder with its phase already Done, returning Idle every
        // frame and starving chase, wander and the cast. The creature stood frozen, which from
        // outside looks exactly like a passenger who never became visible again.

        private static void SetAllowAIWhileMounted(MountModule mount, bool value)
        {
            var so = new SerializedObject(mount);
            so.FindProperty("allowAISelfMovementWhenMounted").boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// TryMount is gated on <c>Time.time >= lastMountChangeTime + mountCooldown</c>, and
        /// Time.time does not advance outside Play mode — it sits wherever the last Play session
        /// left it, which is 0 on a session that hasn't entered Play mode at all. A freshly
        /// constructed MountModule starts with lastMountChangeTime at its default of 0 too, so the
        /// authored 0.25s cooldown reads as still running and TryMount refuses every rider. Every
        /// other MountModule test (MountSeatAddressingTests, NpcPassengerTests, WingPackLaunchTests,
        /// ...) zeroes this field before mounting for the same reason; these tests need the same seam.
        private static void ClearMountCooldown(MountModule mount)
        {
            var so = new SerializedObject(mount);
            so.FindProperty("mountCooldown").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void ADismount_LeavesAModuleItNeverSwitchedOffAlone()
        {
            var go = new GameObject("Conjurer");
            spawned.Add(go);

            var faction = go.AddComponent<EntityFaction>();
            faction.SetFaction(Load<FactionDefinition>(ClankerPath), Load<FactionRelationshipTable>(TablePath));
            go.AddComponent<AgentTargeting>();
            MountModule mount = go.AddComponent<MountModule>();
            WanderModule module = go.AddComponent<WanderModule>();
            go.AddComponent<PassengerSeat>();

            // A passenger seat: the mount keeps its own AI, so it suppresses nothing on the way in.
            SetAllowAIWhileMounted(mount, true);
            ClearMountCooldown(mount);
            Boot(go);

            // A module that has switched ITSELF off — what DormantModule does the instant its wake
            // animation finishes, and what it relies on staying done.
            module.enabled = false;

            Interactor interactor = Rider(out _);
            Assert.IsTrue(mount.TryMount(interactor, null), "The rider should have been seated.");
            mount.Dismount();

            Assert.IsFalse(module.enabled,
                "The dismount switched on a module the mount never switched off. For DormantModule " +
                "that is fatal rather than untidy: it comes back at Scripted priority with its " +
                "phase already Done, returns Idle every frame from the top of the ladder, and the " +
                "creature stands frozen — looking for all the world like it still cannot see you.");
        }

        [Test]
        public void ADismount_StillGivesBackTheModulesItDidSwitchOff()
        {
            var go = new GameObject("Ostrich");
            spawned.Add(go);

            var faction = go.AddComponent<EntityFaction>();
            faction.SetFaction(Load<FactionDefinition>(ClankerPath), Load<FactionRelationshipTable>(TablePath));
            go.AddComponent<AgentTargeting>();
            MountModule mount = go.AddComponent<MountModule>();
            WanderModule module = go.AddComponent<WanderModule>();

            // A steered mount: MountModule takes the creature's AI away for the ride.
            SetAllowAIWhileMounted(mount, false);
            ClearMountCooldown(mount);
            Boot(go);

            Interactor interactor = Rider(out _);
            Assert.IsTrue(mount.TryMount(interactor, null));
            Assert.IsFalse(module.enabled,
                "Precondition: a mount that does not allow AI self-movement suppresses the wander.");

            mount.Dismount();

            Assert.IsTrue(module.enabled,
                "The mount took this module away and never gave it back, so the creature would " +
                "stand inert after its rider got off. Restoring only what was taken must not " +
                "become restoring nothing.");
        }

        // ─────────────────────────────────────────────
        //  The arms are solid
        // ─────────────────────────────────────────────
        //
        // The creature shipped with one collider: a capsule on its centreline, sized to the
        // head/body sphere. Its arms hang a metre outside that radius, so everything you could see
        // out there was empty air to physics — a grapple aimed at the shoulder went through it and
        // only the eye, which is on the body column, could be hit. These pin the fix, and pin the
        // two ways the fix goes wrong: a box that swallows the next joint down, and a box that
        // swallows the staff.

        private GameObject Conjurer()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ConjurerPath);
            Assert.IsNotNull(prefab, $"Missing prefab: {ConjurerPath}");

            GameObject go = Object.Instantiate(prefab);
            spawned.Add(go);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Collider.bounds is read off the transform, and nothing has stepped physics since
            // these were placed. Without this the first query answers from the bind pose.
            Physics.SyncTransforms();
            return go;
        }

        private static Transform Bone(GameObject root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;

            Assert.Fail($"No bone '{name}' on the conjurer — did rig.py rename it?");
            return null;
        }

        [TestCase("UpperArm.L")]
        [TestCase("Forearm.L")]
        [TestCase("Hand.L")]
        [TestCase("UpperArm.R")]
        [TestCase("Forearm.R")]
        [TestCase("Hand.R")]
        public void EveryArmSegment_CarriesItsOwnCollider(string boneName)
        {
            GameObject conjurer = Conjurer();

            Assert.IsNotNull(Bone(conjurer, boneName).GetComponent<Collider>(),
                $"'{boneName}' has no collider, so that part of the arm cannot be shot, grappled " +
                "to, or stood on. The body capsule does not reach it: the arms hang at |x| 3.3 m " +
                "and the capsule's radius is 2.4 m.");
        }

        [Test]
        public void TheShoulderTheRiderSitsOn_StopsAGrapple()
        {
            GameObject conjurer = Conjurer();
            Vector3 seat = Bone(conjurer, "ArmRoot.L").position;

            // A hand's breadth BELOW the seat, not level with it. ArmRoot.L sits exactly on the
            // top face of the shoulder cap — that is what makes it the seat — so a ray at its own
            // height runs along an infinitely thin plane and slips past. A grapple aimed at the
            // shoulder is aimed at the shoulder, not at the plane above it.
            Vector3 aimedAt = seat + Vector3.down * 0.1f;

            bool hit = Physics.Raycast(aimedAt + Vector3.back * 30f, Vector3.forward,
                                       out RaycastHit info, 60f);

            Assert.IsTrue(hit,
                "Nothing was hit at the shoulder. This is the whole reported bug: the body capsule " +
                "is a 2.4 m radius on the centreline and the arms hang at |x| 3.3 m, so without a " +
                "collider of their own a grapple aimed at the shoulder passes through and only the " +
                "eye — which is on the body column — can be hit.");
            Assert.IsTrue(info.collider.transform.IsChildOf(conjurer.transform),
                $"The ray hit '{info.collider.name}', which is not part of the conjurer.");
        }

        [Test]
        public void TheSeat_RestsOnTheShoulderSurfaceRatherThanFloatingOverIt()
        {
            GameObject conjurer = Conjurer();

            float seatY = Bone(conjurer, "ArmRoot.L").position.y;
            Collider shoulder = Bone(conjurer, "UpperArm.L").GetComponent<Collider>();
            Assert.IsNotNull(shoulder, "UpperArm.L has no collider.");

            // The rider's own offset is measured down from this surface, so the two have to agree.
            // If the collider stopped short the rider would sit on nothing; if it reached past, the
            // seat would be buried inside the arm.
            Assert.AreEqual(shoulder.bounds.max.y, seatY, 0.05f,
                "The seat bone should sit on the shoulder cap's top face. PassengerSeat's offset " +
                "is measured down from exactly this surface, so if the collider and the bone stop " +
                "disagreeing by more than a few centimetres the rider is floating or sunk.");
        }

        [Test]
        public void TheHandCollider_DoesNotSwallowTheStaff()
        {
            GameObject conjurer = Conjurer();

            Collider hand = Bone(conjurer, "Hand.R").GetComponent<Collider>();
            Assert.IsNotNull(hand, "Hand.R has no collider.");

            float staffTip = Bone(conjurer, "StaffTip").position.y;
            float handTop = hand.bounds.max.y;

            Assert.Less(handTop, staffTip - 1f,
                "The staff is parented to Hand.R and its tip is ~11 m above the fist, so a hand " +
                "collider measured from everything underneath the bone wraps the hand in a box " +
                $"taller than the creature. Hand top {handTop:0.00}, staff tip {staffTip:0.00}.");
        }

        [Test]
        public void AnArmSegment_DoesNotSwallowTheOneBelowIt()
        {
            GameObject conjurer = Conjurer();

            Collider upper = Bone(conjurer, "UpperArm.L").GetComponent<Collider>();
            Collider fore = Bone(conjurer, "Forearm.L").GetComponent<Collider>();
            Assert.IsNotNull(upper, "UpperArm.L has no collider.");
            Assert.IsNotNull(fore, "Forearm.L has no collider.");

            // Each segment is the PARENT of the next, so a sweep of everything under the upper arm
            // collects the forearm and the hand too and boxes the whole limb as one slab. Two
            // colliders that cover the same span is what that failure looks like from here.
            Assert.Less(upper.bounds.min.y, fore.bounds.max.y + 0.5f,
                "Sanity: the upper arm should sit directly above the forearm.");
            Assert.Greater(upper.bounds.min.y, fore.bounds.min.y,
                "The upper arm's box reaches down past the bottom of the forearm, which means it " +
                "swallowed the joints below it instead of stopping at its own mesh.");
        }
    }
}
