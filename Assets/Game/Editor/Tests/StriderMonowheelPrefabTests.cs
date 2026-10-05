using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Ragdoll;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Monowheel;

namespace SpaceGame.EditorTools
{
    /// Read-back of StriderMonowheelBuilder's output: the five Strider wheels and the riderless
    /// player wheel. Run Tools/SpaceGame/Vehicles/Build Strider Monowheels first.
    public class StriderMonowheelPrefabTests
    {
        private static IEnumerable<string> StriderVariants() =>
            StriderMonowheelBuilder.Singles.Concat(StriderMonowheelBuilder.Doubles);

        private static IEnumerable<string> AllPaths() => StriderMonowheelBuilder.AllPrefabPaths;

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"missing {path}: run Tools/SpaceGame/Vehicles/Build Strider Monowheels");
            return prefab;
        }

        /// How far above a seat a seated rider's chest is -- a point that has to be in the open to be shot.
        private const float RiderChestAboveSeat = 1f;
        /// MonowheelPresentation's ground probe starts this far (m) above each contact.
        private const float PresentationGroundProbe = 0.6f;

        private static string ArtVariantOf(string path) =>
            path == StriderMonowheelBuilder.PlayerPrefabPath
                ? StriderMonowheelBuilder.PlayerVariant
                : System.IO.Path.GetFileNameWithoutExtension(path).Substring("StriderMonowheel_".Length);

        private static Transform BodyOf(GameObject wheel) => wheel.transform.Find(StriderMonowheelBuilder.BodyName);

        [TestCaseSource(nameof(AllPaths))]
        public void EveryWheelSeatsItsRiderInAChair(string path)
        {
            GameObject wheel = Load(path);
            Assert.IsNotNull(wheel.GetComponent<ChairPose>(),
                $"{path}: without a ChairPose on the wheel its rider stands upright in the saddle");
        }

        [TestCaseSource(nameof(StriderVariants))]
        public void TheNpcDriverRaisesTheHumanoidSeatedFlag(string variant)
        {
            var passenger = Load(StriderMonowheelBuilder.PrefabPath(variant)).GetComponent<NpcPassenger>();
            Assert.IsNotNull(passenger, variant);
            string flag = new SerializedObject(passenger).FindProperty("seatedAnimatorBool").stringValue;
            Assert.AreEqual(SpaceGame.Presentation.HumanoidParams.Seated, flag,
                            $"{variant}: the humanoid controller has no other seated flag; a wrong name is skipped silently");
        }

        private static Transform[] Seats(GameObject wheel) =>
            BodyOf(wheel).GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith(StriderMonowheelBuilder.SeatPrefix)).ToArray();

        /// Whether a world point is inside a sphere or box collider (a prefab asset has no physics scene,
        /// so Collider.ClosestPoint and bounds are not available).
        private static bool Inside(Collider collider, Vector3 point)
        {
            Transform t = collider.transform;
            switch (collider)
            {
                case SphereCollider sphere:
                    return Vector3.Distance(t.TransformPoint(sphere.center), point) < sphere.radius;
                case BoxCollider box:
                    Vector3 local = t.InverseTransformPoint(point) - box.center;
                    return Mathf.Abs(local.x) < box.size.x * 0.5f && Mathf.Abs(local.y) < box.size.y * 0.5f
                        && Mathf.Abs(local.z) < box.size.z * 0.5f;
                default:
                    Assert.Fail($"unexpected collider {collider.GetType().Name} on {collider.name}");
                    return false;
            }
        }

        /// The art's sockets import pitched -90 degrees: a rider seated on one lies on their back. The
        /// seat is an upright marker under Body (so it tips with the chassis pose) at the seat's back
        /// corner: on top of the seat cushion, against the front of the back cushion.
        private static void AssertUprightSeatOn(GameObject wheel, Transform seat, string cushionPrefix, string backPrefix)
        {
            Assert.IsNotNull(seat);
            Transform body = BodyOf(wheel);
            Assert.AreSame(body, seat.parent);
            Assert.AreEqual(Quaternion.identity, seat.localRotation, "an unrotated seat, or the rider lies down");
            Bounds cushion = MeshBoundsInRoot(wheel, cushionPrefix), back = MeshBoundsInRoot(wheel, backPrefix);
            Vector3 corner = wheel.transform.InverseTransformPoint(seat.position);
            Assert.AreEqual(cushion.max.y, corner.y, 1e-3f, $"{seat.name} sits on top of the cushion");
            Assert.AreEqual(back.max.z, corner.z, 1e-3f, $"{seat.name} is against the back cushion");
            Assert.AreEqual(cushion.center.x, corner.x, 1e-3f, $"{seat.name} is in the middle of the cushion");
        }

        /// A mesh part's bounds in the wheel's root space, from its vertices (a prefab asset has no
        /// renderer bounds).
        private static Bounds MeshBoundsInRoot(GameObject wheel, string prefix)
        {
            MeshFilter part = BodyOf(wheel).GetComponentsInChildren<MeshFilter>(true).Single(f => f.name.StartsWith(prefix));
            Vector3[] points = part.sharedMesh.vertices
                .Select(v => wheel.transform.InverseTransformPoint(part.transform.TransformPoint(v))).ToArray();
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 point in points) bounds.Encapsulate(point);
            return bounds;
        }

        private static Object Field(Object target, string name) =>
            new SerializedObject(target).FindProperty(name).objectReferenceValue;

        // -- every monowheel -----------------------------------------------------------------------

        [TestCaseSource(nameof(AllPaths))]
        public void IsANonKinematicBody_ThatTheMotorYaws(string path)
        {
            GameObject wheel = Load(path);
            var body = wheel.GetComponent<Rigidbody>();
            Assert.IsNotNull(body);
            Assert.IsFalse(body.isKinematic);
            Assert.IsTrue(body.useGravity);
            Assert.AreEqual(RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ, body.constraints,
                "pitch and roll free tip the wheel over; the lean is MonowheelLean's roll on Body");
            Assert.AreEqual(RigidbodyInterpolation.Interpolate, body.interpolation);

            var motor = wheel.GetComponent<MonowheelMotor>();
            Assert.IsNotNull(motor);
            Assert.AreSame(motor, Field(wheel.GetComponent<AgentController>(), "MotorComponent"));
        }

        [TestCaseSource(nameof(AllPaths))]
        public void NestsItsArtAsBody_Connected_AndNeverYawed(string path)
        {
            GameObject wheel = Load(path);
            Transform body = BodyOf(wheel);
            Assert.IsNotNull(body, "the art is nested as a child named Body");
            var art = AssetDatabase.LoadAssetAtPath<GameObject>(StriderMonowheelBuilder.ArtPrefabPath(ArtVariantOf(path)));
            Assert.AreSame(art, PrefabUtility.GetCorrespondingObjectFromSource(body.gameObject),
                "Body must stay a connected instance of the art prefab, so its rebuilds reach us");
            Assert.AreEqual(Quaternion.identity, body.localRotation,
                "MonowheelPresentation reads speed along Body's forward: a yawed Body spins the rings wrong");
            Assert.AreSame(body, Field(wheel.GetComponent<MonowheelLean>(), "body"));
        }

        [TestCaseSource(nameof(AllPaths))]
        public void RollsOnAContactSpherePerRing_InsideAHollowHoop(string path)
        {
            GameObject wheel = Load(path);
            var presentation = BodyOf(wheel).GetComponent<MonowheelPresentation>();
            SphereCollider[] contacts = wheel.GetComponents<SphereCollider>();
            Assert.AreEqual(presentation.Wheels.Count, contacts.Length, "one contact sphere per ring");
            for (int i = 0; i < contacts.Length; i++)
            {
                MonowheelWheel ring = presentation.Wheels[i];
                Assert.GreaterOrEqual(contacts[i].radius, PresentationGroundProbe,
                    "the presentation's probe must start inside the contact sphere, or it reads the wheel as ground");
                Assert.AreEqual(ring.localContact.y, contacts[i].center.y - contacts[i].radius, 1e-4f, "it touches where the paddles do");
                Assert.IsNotNull(contacts[i].sharedMaterial);
                Assert.AreEqual(0f, contacts[i].sharedMaterial.dynamicFriction, "the motor owns grip");

                Transform hoop = wheel.transform.Find($"Hoop_{i}");
                Assert.IsNotNull(hoop, "the ring is a hoop of boxes under the root, not under Body");
                BoxCollider[] segments = hoop.GetComponentsInChildren<BoxCollider>();
                Assert.Greater(segments.Length, 0);
                Vector3 hubInRoot = wheel.transform.InverseTransformPoint(BodyOf(wheel).TransformPoint(ring.localHub));
                Vector3 axle = wheel.transform.InverseTransformDirection(ring.ringBone.TransformDirection(ring.localAxle)).normalized;
                foreach (BoxCollider segment in segments)
                {
                    // Radially, in the ring's plane: the hoop's width along the axle is the ring's own.
                    float reach = BoxCorners(segment).Max(c =>
                        Vector3.ProjectOnPlane(wheel.transform.InverseTransformPoint(c) - hubInRoot, axle).magnitude);
                    Assert.LessOrEqual(reach, ring.paddleRadius + 1e-3f,
                        $"{segment.name} reaches past the paddles: the vehicle rests on its corners with the wheel in the air");
                    Assert.Greater(reach, ring.paddleRadius - 0.05f, $"{segment.name} sits on the ring");
                    Assert.AreSame(contacts[i].sharedMaterial, segment.sharedMaterial);
                }
            }
        }

        private static Vector3[] BoxCorners(BoxCollider box)
        {
            var corners = new Vector3[8];
            for (int c = 0; c < 8; c++)
            {
                var sign = new Vector3((c & 1) == 0 ? -0.5f : 0.5f, (c & 2) == 0 ? -0.5f : 0.5f, (c & 4) == 0 ? -0.5f : 0.5f);
                corners[c] = box.transform.TransformPoint(box.center + Vector3.Scale(box.size, sign));
            }
            return corners;
        }

        [TestCaseSource(nameof(AllPaths))]
        public void TheChassis_IsAKinematicBodyPosedWithTheArt_ThatNeverStandsOnTheGround(string path)
        {
            GameObject wheel = Load(path);
            Assert.IsEmpty(wheel.GetComponents<BoxCollider>(),
                "a chassis box on the upright root holds the vehicle up on rising ground while the art tips the other way");

            Transform chassis = BodyOf(wheel).Find(StriderMonowheelBuilder.ChassisName);
            Assert.IsNotNull(chassis, "the chassis hangs under Body, so the pose tips it with the frame");
            var body = chassis.GetComponent<Rigidbody>();
            Assert.IsNotNull(body, "its own body, or its colliders join the root's and prop the wheel up again");
            Assert.IsTrue(body.isKinematic, "kinematic: it makes no contact with static ground");
            Assert.IsFalse(body.useGravity);
            Assert.IsNotNull(chassis.GetComponent<MonowheelChassis>(), "without it the chassis shoves its own wheel");
            Assert.IsNotEmpty(chassis.GetComponents<BoxCollider>());
        }

        [TestCaseSource(nameof(AllPaths))]
        public void ASeatedRider_IsInTheOpen_SoTheyCanBeShot(string path)
        {
            GameObject wheel = Load(path);
            Collider[] colliders = wheel.GetComponentsInChildren<Collider>(true);
            Transform[] seats = Seats(wheel);
            Assert.IsNotEmpty(seats);
            foreach (Transform seat in seats)
            foreach (Vector3 point in new[] { seat.position, seat.position + Vector3.up * RiderChestAboveSeat })
            foreach (Collider collider in colliders)
                Assert.IsFalse(Inside(collider, point), $"{seat.name} at {point} is inside {collider.name}'s {collider.GetType().Name}");
        }

        [TestCaseSource(nameof(AllPaths))]
        public void APlayerCanDriveIt(string path)
        {
            GameObject wheel = Load(path);
            var mount = wheel.GetComponent<MountModule>();
            Assert.IsNotNull(mount);
            var seat = (Transform)Field(mount, "seatPoint");
            AssertUprightSeatOn(wheel, seat, StriderMonowheelBuilder.RiderCushionPrefix, StriderMonowheelBuilder.RiderBackCushionPrefix);
            var steer = wheel.GetComponent<SteerModule>();
            Assert.IsNotNull(steer);
            Assert.AreEqual(ModulePriority.Scripted, steer.Priority, "SteerModule.Reset's default, which AddComponent never runs");
            Assert.AreEqual(StriderMonowheelBuilder.RiderSeatName, seat.name);
            Assert.IsNotNull(wheel.GetComponent<MountNetworkSync>());
        }

        [TestCaseSource(nameof(AllPaths))]
        public void ReplicatesAndSaves(string path)
        {
            GameObject wheel = Load(path);
            var net = wheel.GetComponent<NetworkObject>();
            Assert.IsNotNull(net);
            Assert.AreNotEqual(0u, net.PrefabIdHash);
            Assert.IsTrue(net.SceneMigrationSynchronization, "clients stop seeing it move once it crosses into another chunk scene");
            Assert.IsTrue(net.DontDestroyWithOwner, "a driver disconnecting would delete the wheel");
            Assert.IsNotNull(wheel.GetComponent<ClientNetworkTransform>());
            var authority = wheel.GetComponent<NetAuthority>();
            Assert.IsNotNull(authority);
            Assert.IsTrue(new SerializedObject(authority).FindProperty("freezePhysicsOnRemote").boolValue);
            // Registration in the NetworkManager's list is NetworkPrefabRegistrationTests' job, for every prefab.
            Assert.IsFalse(string.IsNullOrEmpty(wheel.GetComponent<SaveableEntity>().PrefabId));
            Assert.IsNotNull(wheel.GetComponent<TransformSaveable>(), "a wheel left somewhere must come back there");
            var tracked = wheel.GetComponent<SpaceGame.World.SceneTracked>();
            Assert.IsNotNull(tracked);
            SerializedProperty policy = new SerializedObject(tracked).FindProperty("policy");
            Assert.AreEqual(nameof(SpaceGame.World.SceneTracked.UnloadPolicy.Migrate), policy.enumNames[policy.enumValueIndex],
                "a wheel driven into another chunk must move with it, not vanish with the chunk it left");
            Assert.IsFalse(tracked.KeepChunksLoaded,
                "a parked wheel must not pin nine chunks resident -- no other player vehicle does");
            Assert.IsNotNull(wheel.GetComponent<SpaceGame.World.Safety.UnderTerrainGuard>(),
                "a fast body that tunnels through a chunk seam is lost without the guard");
        }

        [TestCaseSource(nameof(AllPaths))]
        public void IsAMachine_NeverARagdoll_AndNeverAttacks(string path)
        {
            GameObject wheel = Load(path);
            Assert.IsNull(wheel.GetComponent<AgentRagdoll>(), "a monowheel is a vehicle: it must live under Prefabs/Agents/Vehicles/");
            Assert.IsNull(wheel.GetComponent<CloseCombatModule>(), "the mount carries, the riders shoot");
        }

        // -- the Strider wheels --------------------------------------------------------------------

        [TestCaseSource(nameof(StriderVariants))]
        public void IsDrivenByAStrider(string variant)
        {
            GameObject wheel = Load(StriderMonowheelBuilder.PrefabPath(variant));
            var passenger = wheel.GetComponent<NpcPassenger>();
            Assert.IsNotNull(passenger);
            var rider = (GameObject)Field(passenger, "riderPrefab");
            Assert.IsNotNull(rider);
            StringAssert.StartsWith("Strider_", rider.name, "faction is not replicated: the rider must ship as a Strider");
            Assert.AreSame(Field(wheel.GetComponent<MountModule>(), "seatPoint"), Field(passenger, "seatPoint"));
        }

        [TestCaseSource(nameof(StriderVariants))]
        public void IsAStrider_ThatRidesInTheColumn(string variant)
        {
            GameObject wheel = Load(StriderMonowheelBuilder.PrefabPath(variant));
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.StriderFactionPath),
                            Field(wheel.GetComponent<EntityFaction>(), "faction"));
            Assert.IsNotNull(wheel.GetComponent<HealthComponent>());

            var formation = wheel.GetComponent<FormationModule>();
            Assert.AreEqual(ModulePriority.Social, formation.Priority);
            var so = new SerializedObject(formation);
            Assert.AreEqual(string.Empty, so.FindProperty("formationId").stringValue);
            Assert.IsTrue(so.FindProperty("holdSlotAtRest").boolValue, "nothing parks (user 2026-09-24)");
            Assert.AreEqual(ModulePriority.Fallback + 1, wheel.GetComponent<GoalTravelModule>().Priority);
        }

        [TestCaseSource(nameof(StriderVariants))]
        public void OnlyDrivesWhileSomebodyDrives_AndStopsDeadWhenKilled(string variant)
        {
            GameObject wheel = Load(StriderMonowheelBuilder.PrefabPath(variant));
            var gate = wheel.GetComponent<MonowheelDriverGate>();
            Assert.IsNotNull(gate, "a riderless wheel would keep riding in formation on its own");
            var so = new SerializedObject(gate);
            Assert.AreSame(wheel.GetComponent<NpcPassenger>(), so.FindProperty("passenger").objectReferenceValue);
            Assert.AreSame(wheel.GetComponent<MountModule>(), so.FindProperty("mount").objectReferenceValue);
            SerializedProperty driven = so.FindProperty("drivenModules");
            var modules = Enumerable.Range(0, driven.arraySize).Select(i => driven.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            CollectionAssert.AreEquivalent(new Object[] { wheel.GetComponent<FormationModule>(), wheel.GetComponent<GoalTravelModule>() }, modules);

            Assert.IsNotNull(wheel.GetComponent<MonowheelWreck>(), "a killed wheel would roll on and stay mountable");
            Assert.IsNotNull(wheel.GetComponent<HealthReactionModule>(), "the corpse path that switches a killed wheel's brain off");
        }

        [TestCaseSource(nameof(StriderVariants))]
        public void TopSpeed_MatchesItsClass(string variant)
        {
            bool isDouble = StriderMonowheelBuilder.Doubles.Contains(variant);
            float expected = isDouble ? StriderMonowheelBuilder.DoubleTopSpeed : StriderMonowheelBuilder.SingleTopSpeed;
            Assert.AreEqual(expected, Load(StriderMonowheelBuilder.PrefabPath(variant)).GetComponent<MonowheelMotor>().Settings.topSpeed);
            Assert.Less(StriderMonowheelBuilder.CruiseSpeed, expected, "a convoy cruising at a follower's top speed leaves it behind");
        }

        [TestCaseSource(nameof(StriderVariants))]
        public void Goals_AreRiddenAtCruiseSpeed(string variant)
        {
            GameObject wheel = Load(StriderMonowheelBuilder.PrefabPath(variant));
            float topSpeed = wheel.GetComponent<MonowheelMotor>().Settings.topSpeed;
            float multiplier = new SerializedObject(wheel.GetComponent<GoalTravelModule>()).FindProperty("speedMultiplier").floatValue;
            Assert.AreEqual(StriderMonowheelBuilder.CruiseSpeed, topSpeed * multiplier, 0.01f,
                "a convoy leader riding its goal flat out leaves every follower behind");
        }

        [TestCaseSource(nameof(StriderVariants))]
        public void OnlyDoublesCarryGunners(string variant)
        {
            GameObject wheel = Load(StriderMonowheelBuilder.PrefabPath(variant));
            var seats = wheel.GetComponent<VesselSeats>();
            if (!StriderMonowheelBuilder.Doubles.Contains(variant))
            {
                Assert.IsNull(seats);
                Assert.IsNull(wheel.GetComponent<MountedGunners>());
                return;
            }

            Assert.IsNotNull(seats);
            Assert.AreEqual(3, seats.Capacity);
            SerializedProperty markers = new SerializedObject(seats).FindProperty("seats");
            AssertUprightSeatOn(wheel, (Transform)markers.GetArrayElementAtIndex(0).objectReferenceValue,
                                StriderMonowheelBuilder.PassengerCushionPrefix, StriderMonowheelBuilder.PassengerBackCushionPrefix);
            Transform body = BodyOf(wheel);
            MeshRenderer[] cushions = body.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.name.StartsWith("Mesh_SideSeatCushion")).ToArray();
            for (int i = 1; i < markers.arraySize; i++)
            {
                var side = (Transform)markers.GetArrayElementAtIndex(i).objectReferenceValue;
                StringAssert.StartsWith(StriderMonowheelBuilder.SideSeatPrefix, side.name);
                Assert.AreSame(body, side.parent);
                Assert.AreEqual(Quaternion.identity, side.localRotation);
                // On top of its cushion: the nearest cushion (in plan) sits just below it.
                MeshRenderer cushion = cushions.OrderBy(c => Vector2.Distance(
                    new Vector2(c.transform.position.x, c.transform.position.z),
                    new Vector2(side.position.x, side.position.z))).First();
                Assert.Greater(side.position.y, cushion.transform.position.y, $"{side.name} sits above {cushion.name}");
                Bounds top = MeshBoundsInRoot(wheel, cushion.name);
                Vector3 corner = wheel.transform.InverseTransformPoint(side.position);
                Assert.AreEqual(top.max.y, corner.y, 1e-3f, $"{side.name} sits on top of {cushion.name}");
                Assert.AreEqual(top.min.z, corner.z, 1e-3f, $"{side.name} is at the back edge of {cushion.name}");
            }
            Assert.IsNotNull(wheel.GetComponent<ChairPose>());
            var gunner = (GameObject)Field(wheel.GetComponent<MountedGunners>(), "gunnerPrefab");
            Assert.IsNotNull(gunner);
            StringAssert.StartsWith("Strider_", gunner.name);
        }

        // -- seats ---------------------------------------------------------------------------------

        private static StriderMonowheelBuilder.RiderOffsets measured;
        private static bool hasMeasured;

        /// Sitting two rigs down takes a moment; measure once for the fixture.
        private static StriderMonowheelBuilder.RiderOffsets Measured()
        {
            if (!hasMeasured) { measured = StriderMonowheelBuilder.MeasureRiderOffsets(); hasMeasured = true; }
            return measured;
        }

        private static Vector3 OffsetOf(Object component, string field = "seatOffset") =>
            new SerializedObject(component).FindProperty(field).vector3Value;

        [TestCaseSource(nameof(AllPaths))]
        public void APlayerSitsOnTheCushion_ByTheirOwnMeasuredBody(string path)
        {
            GameObject wheel = Load(path);
            Vector3 expected = Measured().Player;
            Assert.Less(Vector3.Distance(expected, OffsetOf(wheel.GetComponent<MountModule>())), 1e-3f,
                "the player's origin is a metre above its soles: a Strider's drop sinks them into the chassis");
            Assert.Greater(expected.y, 0f, "a seated player's origin is ABOVE the cushion (PlayerCharacter.md: root 1 m above the soles)");
        }

        [TestCaseSource(nameof(StriderVariants))]
        public void EveryStriderSitsOnTheCushion_ByTheirOwnMeasuredBody(string variant)
        {
            GameObject wheel = Load(StriderMonowheelBuilder.PrefabPath(variant));
            Vector3 expected = Measured().Strider;
            Assert.Less(Vector3.Distance(expected, OffsetOf(wheel.GetComponent<NpcPassenger>())), 1e-3f, "the driver");
            Assert.Less(Vector3.Distance(expected, OffsetOf(wheel.GetComponent<TiltingSeats>(), "npcSeatOffset")), 1e-3f,
                "TiltingSeats must hold an NPC where it was seated");
            var gunners = wheel.GetComponent<VesselSeats>();
            if (gunners != null) Assert.Less(Vector3.Distance(expected, OffsetOf(gunners)), 1e-3f, "the gunners");
        }

        [TestCaseSource(nameof(AllPaths))]
        public void EveryRiderTiltsWithTheirSeat(string path)
        {
            GameObject wheel = Load(path);
            var tilting = wheel.GetComponent<TiltingSeats>();
            Assert.IsNotNull(tilting, "without it a rider stays upright while the chassis rolls and pitches under them");
            Assert.AreSame(wheel.GetComponent<MountModule>(), Field(tilting, "mount"));
            SerializedProperty listed = new SerializedObject(tilting).FindProperty("seats");
            Transform[] seats = Seats(wheel);
            Assert.AreEqual(seats.Length, listed.arraySize);
            for (int i = 0; i < listed.arraySize; i++)
                CollectionAssert.Contains(seats, listed.GetArrayElementAtIndex(i).objectReferenceValue);
        }

        // -- the player's wheel --------------------------------------------------------------------

        [Test]
        public void ThePlayersWheel_HasNobodyAboard_NoSide_AndNoBrain()
        {
            GameObject wheel = Load(StriderMonowheelBuilder.PlayerPrefabPath);
            Assert.IsNull(wheel.GetComponent<NpcPassenger>(), "the player's wheel waits empty");
            Assert.IsNull(wheel.GetComponent<EntityFaction>());
            Assert.IsNull(wheel.GetComponent<VesselSeats>());
            BehaviourModuleBase[] modules = wheel.GetComponents<BehaviourModuleBase>();
            CollectionAssert.AreEquivalent(new[] { typeof(MountModule), typeof(SteerModule) }, modules.Select(m => m.GetType()).ToArray(),
                "any module but the mount-aware pair would drive a parked wheel away");
            Assert.AreEqual(StriderMonowheelBuilder.SingleTopSpeed, wheel.GetComponent<MonowheelMotor>().Settings.topSpeed);
        }

        [Test]
        public void ThePlayersWheel_StaysWhereItWasParked()
        {
            GameObject wheel = Load(StriderMonowheelBuilder.PlayerPrefabPath);
            Assert.IsNotNull(wheel.GetComponent<TransformSaveable>());
            Assert.IsNotNull(wheel.GetComponent<RigidbodySaveable>());
            Assert.IsNotNull(wheel.GetComponent<MountSaveable>());
        }
    }
}
