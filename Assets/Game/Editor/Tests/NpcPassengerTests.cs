// Tests for putting an NPC in a saddle.
//
// Same constraint as AgentAuthorityTests: there is no session here, so what is provable is the
// arithmetic and the bookkeeping rather than a live reparent. That covers the two failures this
// class has actually had — a seat offset folded into the wrong space, and a dismount that handed
// back drivers it never switched off — and the netcode rule it exists under is asserted where it
// can be: an unspawned NetworkObject must never be reparented, because Netcode refuses and puts
// the parent straight back, which is what left a caravan's riders standing in the empty desert.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay;

namespace SpaceGame.Tests
{
    public class NpcPassengerTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);

            spawned.Clear();
        }

        // ─────────── The fold that the netcode path depends on ───────────

        [Test]
        public void TheSeatIsTheSamePlaceInEitherSpace()
        {
            (Transform root, Transform seat) = NewMountRig();

            (Vector3 local, Quaternion localRotation) =
                NpcPassenger.SeatPoseIn(root, seat, new Vector3(0f, -0.85f, 0f), new Vector3(0f, 15f, 0f));
            (Vector3 world, Quaternion worldRotation) =
                NpcPassenger.SeatPoseIn(null, seat, new Vector3(0f, -0.85f, 0f), new Vector3(0f, 15f, 0f));

            Assert.That(Vector3.Distance(root.TransformPoint(local), world), Is.LessThan(1e-4f),
                "Netcode will not parent a rider to the seat marker itself, so the marker's offset " +
                "is folded into the mount root's local space instead. If the fold is wrong the " +
                "rider rides somewhere other than the saddle.");
            Assert.That(Quaternion.Angle(root.rotation * localRotation, worldRotation), Is.LessThan(0.01f));
        }

        [Test]
        public void AMountThatIsNotAtTheOriginFoldsTheSameWay()
        {
            (Transform root, Transform seat) = NewMountRig();
            root.SetPositionAndRotation(new Vector3(913f, 27f, -455f), Quaternion.Euler(0f, 214f, 0f));

            (Vector3 local, _) = NpcPassenger.SeatPoseIn(root, seat, Vector3.down, Vector3.zero);
            (Vector3 world, _) = NpcPassenger.SeatPoseIn(null, seat, Vector3.down, Vector3.zero);

            Assert.That(Vector3.Distance(root.TransformPoint(local), world), Is.LessThan(1e-3f),
                "Caravans live kilometres from the origin — a fold that only holds at the origin " +
                "holds nowhere the player will ever see one.");
        }

        // ─────────── Seating an unnetworked rider ───────────

        [Test]
        public void TheRiderEndsUpOnTheSeat()
        {
            (NpcPassenger passenger, Transform seat) = NewPassenger(new Vector3(0f, -0.85f, 0f));
            GameObject rider = NewObject("rider");

            passenger.Seat(rider);

            Assert.AreSame(seat, rider.transform.parent);
            Assert.That(Vector3.Distance(rider.transform.position, seat.TransformPoint(new Vector3(0f, -0.85f, 0f))),
                        Is.LessThan(1e-4f),
                "The offset exists because a character's origin is between its feet: seat them at " +
                "the saddle's origin and they stand on it instead of sitting in it.");
        }

        [Test]
        public void SeatingRefusesASecondRider()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject first = NewObject("first");
            GameObject second = NewObject("second");

            passenger.Seat(first);
            passenger.Seat(second);

            Assert.AreSame(first, passenger.Rider);
            Assert.IsNull(second.transform.parent, "One saddle, one rider.");
        }

        // ─────────── What dismounting is allowed to switch back on ───────────

        [Test]
        public void DismountingGivesBackOnlyWhatSeatingTookAway()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject rider = NewObject("rider");

            var controller = rider.AddComponent<AgentController>();
            controller.enabled = false;

            passenger.Seat(rider);
            passenger.Dismount();

            Assert.IsFalse(controller.enabled,
                "This rider's brain was already switched off by something else. Dismounting must " +
                "not hand them a working one it never took.");
        }

        [Test]
        public void DismountingUnparentsAndRestores()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject rider = NewObject("rider");
            var controller = rider.AddComponent<AgentController>();

            passenger.Seat(rider);
            Assert.IsFalse(controller.enabled, "A passenger must not walk out from under its mount.");

            GameObject dismounted = passenger.Dismount();

            Assert.AreSame(rider, dismounted);
            Assert.IsNull(rider.transform.parent);
            Assert.IsTrue(controller.enabled);
            Assert.IsFalse(passenger.HasRider);
        }

        // ─────────── The rider is still part of the world while they ride ───────────

        [Test]
        public void ASeatedRiderKeepsASolidBody()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject rider = NewObject("rider");
            var capsule = rider.AddComponent<CapsuleCollider>();

            passenger.Seat(rider);

            Assert.IsTrue(capsule.enabled,
                "Switching the rider's colliders off is what made mounted nomads impossible to " +
                "shoot, lasso, rope or even aim at: every one of those is a query, and a query " +
                "passes straight through a disabled collider.");
        }

        [Test]
        public void ASeatedRiderCannotShoveTheMount()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            var mountCollider = passenger.gameObject.AddComponent<BoxCollider>();
            GameObject rider = NewObject("rider");
            var riderCollider = rider.AddComponent<CapsuleCollider>();

            passenger.Seat(rider);

            Assert.IsTrue(Physics.GetIgnoreCollision(riderCollider, mountCollider),
                "A rider's collider sits inside the mount's, and physics resolves that overlap by " +
                "shoving one of them. Suspending the pair is how the body stays solid to queries " +
                "without the mount spinning under its own rider.");
        }

        [Test]
        public void DismountingHandsTheCollisionBack()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            var mountCollider = passenger.gameObject.AddComponent<BoxCollider>();
            GameObject rider = NewObject("rider");
            var riderCollider = rider.AddComponent<CapsuleCollider>();

            passenger.Seat(rider);
            passenger.Dismount();

            Assert.IsFalse(Physics.GetIgnoreCollision(riderCollider, mountCollider),
                "IgnoreCollision is global and permanent until it is undone. A rider who walks " +
                "away still ignoring the animal walks through it.");
        }

        // ─────────── Who this machine thinks it is carrying ───────────

        [Test]
        public void SeatingPosesTheRiderItSeated()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject rider = NewObject("rider");

            passenger.Seat(rider);

            Assert.AreSame(rider.transform, passenger.PosedRider);
        }

        [Test]
        public void AWatchingMachineAdoptsTheRiderNetcodeParentedIn()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject rider = NewObject("rider");
            rider.AddComponent<AgentController>();

            // What a client is handed: the parenting, and nothing else. Seat() was never called
            // here, so Rider stays null and the pose has to be worked out from what is visible.
            rider.transform.SetParent(passenger.transform, worldPositionStays: false);
            passenger.RefreshSeatedRider();

            Assert.IsFalse(passenger.HasRider, "Only the authority seats anybody.");
            Assert.AreSame(rider.transform, passenger.PosedRider,
                "A machine that was told nothing still has to sit the rider in the saddle, or the " +
                "caravan rides past every client with its nomads standing bolt upright.");
        }

        [Test]
        public void TheMountsOwnBrainIsNotItsRider()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            passenger.gameObject.AddComponent<AgentController>();

            passenger.RefreshSeatedRider();

            Assert.IsNull(passenger.PosedRider,
                "The mount IS the agent here, so its own AgentController is the first thing a " +
                "search below itself finds.");
        }

        [Test]
        public void APlayerRiderIsLeftToTheMountModule()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject player = NewObject("player");

            // No AgentController — that is exactly what tells the two kinds of rider apart, and
            // adopting a player here would mean two components posing one body.
            player.transform.SetParent(passenger.transform, worldPositionStays: false);
            passenger.RefreshSeatedRider();

            Assert.IsNull(passenger.PosedRider);
        }

        [Test]
        public void ADismountDuringTeardownIsRefused()
        {
            (NpcPassenger passenger, Transform seat) = NewPassenger(Vector3.zero);
            GameObject rider = NewObject("rider");

            passenger.Seat(rider);
            passenger.gameObject.SetActive(false);

            Assert.IsNull(passenger.Dismount(),
                "Unity will not reparent out of an inactive hierarchy, so a teardown-time dismount " +
                "would leave the rider parented to something about to be destroyed.");
            Assert.AreSame(seat, rider.transform.parent);
            Assert.IsTrue(passenger.HasRider);
        }

        // ─────────── Taking the saddle off them ───────────

        [Test]
        public void MountingTurfsTheNpcRiderOut()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            MountModule mount = NewMount(passenger);

            GameObject nomad = NewObject("nomad");
            nomad.AddComponent<AgentController>();
            passenger.Seat(nomad);

            GameObject player = NewObject("player");
            player.AddComponent<PlayerMovement>();
            var interactor = player.AddComponent<Interactor>();

            Assert.IsTrue(mount.TryMount(interactor, null), "The player should have got the seat.");

            Assert.IsFalse(passenger.HasRider,
                "MountModule tracks only its own PlayerMovement rider, so an occupied saddle read " +
                "as free and the player was seated straight through the nomad already in it.");
            Assert.IsNull(nomad.transform.parent, "The evicted rider is put down beside the animal.");
            Assert.AreSame(player.transform, mount.MountedPlayerTransform);
        }

        [Test]
        public void MountingAnEmptySaddleAsksNothingOfNobody()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            MountModule mount = NewMount(passenger);

            GameObject player = NewObject("player");
            player.AddComponent<PlayerMovement>();
            var interactor = player.AddComponent<Interactor>();

            Assert.IsTrue(mount.TryMount(interactor, null));
            Assert.IsFalse(passenger.HasRider);
        }

        // ─────────── Hurting a rider ───────────

        [Test]
        public void AWoundedRiderGetsOffAndCanFightBack()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject rider = NewObject("rider");
            var health = rider.AddComponent<HealthComponent>();
            var brain = rider.AddComponent<AgentController>();

            passenger.Seat(rider);
            health.Damage(1);

            Assert.IsFalse(passenger.HasRider);
            Assert.IsTrue(brain.enabled,
                "A passenger's brain is off — that is what makes them a passenger. Shootable and " +
                "unable to answer reads as a broken enemy, not a peaceful one.");
        }

        [Test]
        public void ADeadRiderIsNotHandedItsBrainBack()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject rider = NewObject("rider");
            var health = rider.AddComponent<HealthComponent>();
            var brain = rider.AddComponent<AgentController>();

            passenger.Seat(rider);
            health.Damage(health.GetMaxHealth);

            Assert.IsFalse(passenger.HasRider);
            Assert.IsFalse(brain.enabled,
                "HealthReactionModule has already switched the brain off and started the despawn " +
                "timer by now. Handing back a working AgentController stands the corpse up.");
        }

        // ─────────── Ropes take a rider off ───────────

        [Test]
        public void ARopeOnASeatedRiderTakesThemOutOfTheSaddle()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject rider = NewObject("rider");
            passenger.Seat(rider);

            Assert.IsTrue(NpcPassenger.UnseatRider(rider));

            Assert.IsFalse(passenger.HasRider);
            Assert.IsNull(rider.transform.parent,
                "A seated rider's transform belongs to the animal, so a rope tied to one hauls on " +
                "a body that cannot move while the mount walks on regardless.");
        }

        [Test]
        public void ARopeOnSomebodyWhoIsNotRidingChangesNothing()
        {
            (NpcPassenger passenger, _) = NewPassenger(Vector3.zero);
            GameObject seated = NewObject("seated");
            passenger.Seat(seated);

            GameObject bystander = NewObject("bystander");

            Assert.IsFalse(NpcPassenger.UnseatRider(bystander));
            Assert.IsTrue(passenger.HasRider, "Roping a bystander must not empty somebody's saddle.");
        }

        // ─────────── Fixtures ───────────

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        /// <summary>A mount root with a seat marker on its back, posed so no axis is trivially zero.</summary>
        private (Transform root, Transform seat) NewMountRig()
        {
            GameObject mount = NewObject("mount");
            mount.transform.SetPositionAndRotation(new Vector3(4f, 1f, -7f), Quaternion.Euler(0f, 63f, 0f));

            var seat = new GameObject("seat").transform;
            seat.SetParent(mount.transform, worldPositionStays: false);
            seat.SetLocalPositionAndRotation(new Vector3(0f, 2.3f, -0.15f), Quaternion.identity);

            return (mount.transform, seat);
        }

        // AddComponent runs no Awake outside play mode, so the serialized fields are planted by
        // hand — the same ones the Inspector fills in on the prefab.
        private (NpcPassenger passenger, Transform seat) NewPassenger(Vector3 seatOffset)
        {
            (Transform root, Transform seat) = NewMountRig();

            var passenger = root.gameObject.AddComponent<NpcPassenger>();
            Plant(passenger, "seatPoint", seat);
            Plant(passenger, "seatOffset", seatOffset);
            Plant(passenger, "dismountSideOffset", 1.6f);
            Plant(passenger, "dismountSampleDistance", 6f);

            return (passenger, seat);
        }

        /// <summary>
        /// A mount whose saddle can actually be taken.
        ///
        /// <para>
        /// The cooldown is zeroed rather than left at its authored 0.25 s because
        /// <c>IsAvailableForMount</c> reads <c>Time.time</c>, and <c>Time.time</c> is 0 in edit mode
        /// — it keeps whatever value play mode left behind and a domain reload resets it. So the
        /// gate is <c>0 >= 0.25</c> and no mount can ever be seated here. Left in, the failure looks
        /// intermittent (it passes only if somebody has been in play mode since the last reload) and
        /// reads exactly like a regression in whatever was edited last.
        /// </para>
        /// </summary>
        private static MountModule NewMount(NpcPassenger passenger)
        {
            var mount = passenger.gameObject.AddComponent<MountModule>();
            Plant(mount, "mountCooldown", 0f);
            return mount;
        }

        private static void Plant(Component component, string field, object value)
        {
            FieldInfo info = component.GetType()
                .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(info,
                $"{component.GetType().Name}.{field} was renamed; this test plants it directly.");
            info.SetValue(component, value);
        }
    }
}
