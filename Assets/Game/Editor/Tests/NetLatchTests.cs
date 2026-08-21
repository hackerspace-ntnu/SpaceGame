// Doors, levers and volume triggers, and the two different reasons they used to desync.
//
// A door and a lever were plain MonoBehaviours: the swing happened on the machine that pressed E and
// nowhere else, which meant two players in the same hull disagreed about whether the hatch was shut
// — and SandstormShelter reads exactly that flag. A volume trigger had the mirror-image problem:
// every player's body exists on every machine, so it fired on all of them, for all of them.
//
// The property under test throughout is the same degradation contract NetMessagingTests asserts.
// With no NetworkManager — which is what an EditMode test, a scene opened from the editor and a
// torn-down session all look like — every send falls through to a local dispatch, so single-player
// runs the ENTIRE server round trip on one machine, in one frame. That is what makes the protocol
// testable here at all, and it is also the thing that must not regress: whatever these tests do to
// the messages, offline behaviour has to stay exactly what it was before any of this was networked.
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.EditorTools
{
    public class NetLatchTests
    {
        // The wire verbs, spelled out rather than imported, so a silent change to the protocol in
        // NetLatch shows up here as a failing test rather than as two files agreeing on the wrong
        // thing. The table lives above NetMsg.LatchSet.
        private const int Ask = -1;
        private const int Off = 0;
        private const int On = 1;
        private const int OffInstant = 2;
        private const int OnInstant = 3;

        private readonly System.Collections.Generic.List<GameObject> spawned = new();

        private GameObject NewObject(string name = "entity")
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
        }

        // ─────────── Test plumbing ───────────

        /// <summary>
        /// Speak as the server would.
        ///
        /// Offline every direction collapses to a local dispatch, so sending a LatchState to "the
        /// server" delivers it to the handlers on this entity — which is precisely what a real
        /// announcement does when it arrives over the wire. See NetMessaging.Send.
        /// </summary>
        private static void Announce(Component fixture, int index, int verb) =>
            fixture.NetToServer(NetMsg.LatchState, new NetArg { A = index, B = verb });

        /// <summary>Speak as a client pressing the key would.</summary>
        private static void RequestSet(Component fixture, int index, int verb) =>
            fixture.NetToServer(NetMsg.LatchSet, new NetArg { A = index, B = verb });

        /// <summary>
        /// Unity does not run Awake or OnEnable for a component added outside play mode, and every
        /// fixture here builds its latch in one and subscribes it in the other.
        /// </summary>
        private static void Life(Component target, string method) =>
            target.GetType()
                  .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                  ?.Invoke(target, null);

        private static FieldInfo Field<T>(string name) =>
            typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        private static object Get<T>(T target, string name) => Field<T>(name).GetValue(target);

        private static void Set<T>(T target, string name, object value) =>
            Field<T>(name).SetValue(target, value);

        /// <summary>A stand-in for any fixture that owns a latch, with nothing else attached to it.</summary>
        private class LatchProbe : MonoBehaviour, ILatchHost
        {
            public NetLatch Latch;
            public int Applies;
            public bool LastOn;
            public bool LastInstant;
            public bool Blocked;

            public int LatchCount => 1;

            public void Build(bool oneWay = false) =>
                Latch = new NetLatch(this, Apply, canChange: () => !Blocked, oneWay: oneWay);

            private void Apply(bool on, bool instant)
            {
                Applies++;
                LastOn = on;
                LastInstant = instant;
            }
        }

        private LatchProbe NewProbe(GameObject host = null, bool oneWay = false)
        {
            var probe = (host ?? NewObject()).AddComponent<LatchProbe>();
            probe.Build(oneWay);
            probe.Latch.Enable();
            return probe;
        }

        // ─────────── The protocol ───────────

        [Test]
        public void TheIdsAreTheOnesTheProtocolWasWrittenAgainst()
        {
            // Ids travel between builds. If these move, every shipped client talks to the wrong
            // handler, and the failure is a door that opens somebody else's ramp.
            Assert.AreEqual(63, NetMsg.LatchSet);
            Assert.AreEqual(64, NetMsg.LatchState);
        }

        [Test]
        public void APressGoesToTheServerAndComesBackAsAStateEverybodyApplies()
        {
            LatchProbe probe = NewProbe();

            probe.Latch.Toggle();

            Assert.IsTrue(probe.Latch.IsOn, "The press must have reached the server and been decided.");
            Assert.IsFalse(probe.LastInstant, "A live press animates; only a late joiner lands in a pose.");

            // The single most important number in this file. Offline — and on a host — the request
            // handler applies AND broadcasts, and the broadcast comes straight back to this same
            // machine. Two applies would mean every host-side door restarting its swing halfway
            // through, and every lever firing its UnityEvent twice.
            Assert.AreEqual(1, probe.Applies, "One press must move the fixture exactly once.");
        }

        [Test]
        public void ASecondRequestForTheStateWeAreAlreadyInIsDropped()
        {
            LatchProbe probe = NewProbe();

            RequestSet(probe, probe.Latch.Index, On);
            RequestSet(probe, probe.Latch.Index, On);

            Assert.AreEqual(1, probe.Applies,
                "Two players pressing the same panel on one frame must not double-apply.");
        }

        [Test]
        public void ARefusalIsRecheckedOnTheServerAndNotTrustedFromTheSender()
        {
            LatchProbe probe = NewProbe();
            probe.Blocked = true;

            // Straight onto the wire, bypassing the local Accepts the way a client whose message was
            // already in flight when the fixture became busy would.
            RequestSet(probe, probe.Latch.Index, On);

            Assert.AreEqual(0, probe.Applies, "The authority has the last word, not the presser.");
            Assert.IsFalse(probe.Latch.IsOn);

            probe.Blocked = false;
            probe.Latch.Toggle();
            Assert.IsTrue(probe.Latch.IsOn, "...and it takes the press once the fixture is free again.");
        }

        [Test]
        public void AOneWayLatchTravelsOnceAndRefusesTheJourneyBack()
        {
            LatchProbe probe = NewProbe(oneWay: true);

            Assert.IsTrue(probe.Latch.Next, "A one-way latch only ever asks for 'on'.");
            probe.Latch.Toggle();
            Assert.IsTrue(probe.Latch.IsOn);

            Assert.IsFalse(probe.Latch.Accepts(true), "It has already gone.");
            Assert.IsFalse(probe.Latch.Accepts(false), "And it does not come back.");

            RequestSet(probe, probe.Latch.Index, Off);
            Assert.AreEqual(1, probe.Applies, "Not even straight off the wire.");
            Assert.IsTrue(probe.Latch.IsOn);
        }

        [Test]
        public void ATogglingLatchGoesBothWays()
        {
            LatchProbe probe = NewProbe();

            probe.Latch.Toggle();
            probe.Latch.Toggle();

            Assert.IsFalse(probe.Latch.IsOn);
            Assert.AreEqual(2, probe.Applies);
            Assert.IsFalse(probe.LastOn);
        }

        // ─────────── Late joiners ───────────

        [Test]
        public void AJoinerLandsInTheStateItMissedRatherThanWatchingItHappen()
        {
            LatchProbe joiner = NewProbe();

            Announce(joiner, joiner.Latch.Index, OnInstant);

            Assert.IsTrue(joiner.Latch.IsOn);
            Assert.IsTrue(joiner.LastInstant,
                "A door opened before you arrived should already be open, not swing open in your face.");
        }

        [Test]
        public void AnsweringAJoinersQuestionDoesNotDisturbAnybodyElse()
        {
            LatchProbe probe = NewProbe();
            probe.Latch.Toggle();
            Assert.AreEqual(1, probe.Applies);

            // The query, and the server's answer to it, which this layer has no way to send to one
            // machine — so it goes to everyone. Every machine that already agrees has to ignore it,
            // or one player walking up to a door snaps it shut on somebody else's screen mid-swing.
            RequestSet(probe, probe.Latch.Index, Ask);

            Assert.AreEqual(1, probe.Applies, "The answer must be a no-op for anyone already in it.");
            Assert.IsTrue(probe.Latch.IsOn);
        }

        // ─────────── Several latches on one entity ───────────

        [Test]
        public void LatchesOnOneEntityAreNumberedAndOnlyAnswerToTheirOwnNumber()
        {
            // A corridor with two doors, a ship with a cockpit hatch and a garage ramp: one channel,
            // one NetworkObject, several independent latches.
            GameObject entity = NewObject("ship");
            LatchProbe first = NewProbe(entity);
            LatchProbe second = NewProbe(entity);

            Assert.AreEqual(0, first.Latch.Index);
            Assert.AreEqual(1, second.Latch.Index, "Numbering is positional over the entity's hosts.");

            Announce(first, second.Latch.Index, OnInstant);

            Assert.AreEqual(0, first.Applies, "The wrong door must not open.");
            Assert.AreEqual(1, second.Applies);
        }

        [Test]
        public void ALatchOnAChildIsNumberedWithTheRestOfTheEntity()
        {
            GameObject entity = NewObject("ship");
            var childHost = new GameObject("hatch");
            childHost.transform.SetParent(entity.transform);

            LatchProbe onRoot = NewProbe(entity);
            LatchProbe onChild = NewProbe(childHost);

            Assert.AreEqual(0, onRoot.Latch.Index);
            Assert.AreEqual(1, onChild.Latch.Index);

            // And the child hears a message addressed to the ship, because they share the channel.
            Announce(onRoot, onChild.Latch.Index, OnInstant);
            Assert.AreEqual(1, onChild.Applies);
        }

        // ─────────── Lifetime ───────────

        [Test]
        public void ADisabledLatchStopsListening()
        {
            LatchProbe probe = NewProbe();
            probe.Latch.Disable();

            Announce(probe, probe.Latch.Index, OnInstant);

            Assert.AreEqual(0, probe.Applies,
                "Every Enable needs a matching Disable, or a switched-off fixture still swings.");
        }

        [Test]
        public void EnablingTwiceDoesNotSubscribeTwice()
        {
            LatchProbe probe = NewProbe();
            probe.Latch.Enable();

            Announce(probe, probe.Latch.Index, OnInstant);

            Assert.AreEqual(1, probe.Applies, "A double subscription would apply every message twice.");
        }

        // ─────────── DoorInteraction ───────────

        private DoorInteraction NewDoor(GameObject host = null)
        {
            GameObject go = host ?? NewObject("door");

            var left = new GameObject("LeftDoors");
            var right = new GameObject("RightDoors");
            left.transform.SetParent(go.transform, false);
            right.transform.SetParent(go.transform, false);

            var door = go.AddComponent<DoorInteraction>();

            // Silence the audio before Awake wires anything up. Sfx.Play returns immediately for
            // SfxId.None with no override, which keeps FMOD — which has no business being spun up by
            // an EditMode run — entirely out of these tests.
            Set(door, "openId", SfxId.None);
            Set(door, "closeId", SfxId.None);

            Life(door, "Awake");
            Life(door, "OnEnable");
            return door;
        }

        private static bool IsSwinging(DoorInteraction door) => (bool)Get(door, "_isRotating");

        [Test]
        public void PressingADoorAsksTheServerAndTheAnswerSwingsItForEveryone()
        {
            DoorInteraction door = NewDoor();

            door.Interact(null);

            Assert.IsTrue(door.IsOpen,
                "IsOpen is what SandstormShelter reads — it has to be the session's answer.");
            Assert.IsTrue(IsSwinging(door), "A live press animates.");
        }

        [Test]
        public void ADoorFollowsTheSessionEvenWhenThisMachineNeverTouchedIt()
        {
            // The whole bug, in one test: nobody pressed anything here, and the door still opens.
            DoorInteraction door = NewDoor();

            Announce(door, 0, On);

            Assert.IsTrue(door.IsOpen);
        }

        [Test]
        public void ADoorThatWasAlreadyOpenIsLandedInRatherThanSwungOpen()
        {
            DoorInteraction door = NewDoor();
            var left = door.transform.Find("LeftDoors");

            Announce(door, 0, OnInstant);

            Assert.IsTrue(door.IsOpen);
            Assert.IsFalse(IsSwinging(door), "A joiner should not watch every door in the world open.");
            Assert.AreEqual(0f, Quaternion.Angle(left.localRotation, Quaternion.Euler(0f, -90f, 0f)), 0.01f,
                "...and should be looking at an open door, not one still in its shut pose.");
        }

        [Test]
        public void BeingToldTheSameStateTwiceDoesNotWalkTheDoorFurtherOpen()
        {
            // The reason the swing targets are absolute — the shut pose times ninety degrees —
            // rather than relative to wherever the leaf happens to be standing. A relative target
            // turns a duplicated message into a door at 180 degrees, and duplicated messages are
            // routine: the host applies its own change and then receives its own broadcast.
            DoorInteraction door = NewDoor();
            var left = door.transform.Find("LeftDoors");

            Announce(door, 0, OnInstant);
            Quaternion afterFirst = left.localRotation;

            Announce(door, 0, OnInstant);
            Announce(door, 0, OnInstant);

            Assert.AreEqual(0f, Quaternion.Angle(afterFirst, left.localRotation), 0.01f);
        }

        [Test]
        public void ADoorRefusesAPressWhileItIsStillSwinging()
        {
            DoorInteraction door = NewDoor();
            door.Interact(null);

            Assert.IsFalse(door.CanInteract(), "The crosshair and the key have to agree about this.");

            door.Interact(null);
            Assert.IsTrue(door.IsOpen, "A press that was refused must not have quietly shut it again.");

            // Finish the swing the way Update would, then it takes a press again.
            Set(door, "_isRotating", false);
            Assert.IsTrue(door.CanInteract());
            door.Interact(null);
            Assert.IsFalse(door.IsOpen);
        }

        [Test]
        public void TwoDoorsOnOneShipDoNotOpenEachOther()
        {
            GameObject ship = NewObject("ship");
            var cockpitHost = new GameObject("cockpit");
            var garageHost = new GameObject("garage");
            cockpitHost.transform.SetParent(ship.transform, false);
            garageHost.transform.SetParent(ship.transform, false);

            DoorInteraction cockpit = NewDoor(cockpitHost);
            DoorInteraction garage = NewDoor(garageHost);

            garage.Interact(null);

            Assert.IsTrue(garage.IsOpen);
            Assert.IsFalse(cockpit.IsOpen, "One entity, one channel, two independently numbered doors.");
        }

        // ─────────── LeverInteraction ───────────

        private LeverInteraction NewLever(bool oneShot = true, bool replayOnJoin = true)
        {
            var lever = NewObject("lever").AddComponent<LeverInteraction>();

            Set(lever, "pullId", SfxId.None);
            Set(lever, "oneShot", oneShot);
            Set(lever, "replayOnJoin", replayOnJoin);

            Life(lever, "Awake");
            Life(lever, "OnEnable");
            return lever;
        }

        /// <summary>Counts what the designer's UnityEvent did, from outside the closure.</summary>
        private class PullSpy { public int Fired; }

        private static PullSpy WatchPulls(LeverInteraction lever)
        {
            // The UnityEvent is authored in the inspector, so a code-built lever has to be given one.
            var spy = new PullSpy();
            var pulled = new UnityEvent();
            pulled.AddListener(() => spy.Fired++);
            Set(lever, "onPulled", pulled);
            return spy;
        }

        [Test]
        public void AJoinerReplaysALeverWhoseEventDescribesState()
        {
            LeverInteraction lever = NewLever();
            PullSpy spy = WatchPulls(lever);

            // The lever was pulled before this machine existed. The hidden door it opened has to be
            // open here too, or this player is standing in a world nobody else is in and nothing
            // will ever tell them.
            Announce(lever, 0, OnInstant);

            Assert.IsTrue(lever.IsPulled);
            Assert.AreEqual(1, spy.Fired);
        }

        [Test]
        public void AJoinerDoesNotReplayALeverWiredToAOneShotEffect()
        {
            LeverInteraction lever = NewLever(replayOnJoin: false);
            PullSpy spy = WatchPulls(lever);

            Announce(lever, 0, OnInstant);

            Assert.AreEqual(0, spy.Fired,
                "A portal must not fire in a joiner's face for something that happened an hour ago.");
            Assert.IsTrue(lever.IsPulled, "...but the handle is still down, because it is.");
        }

        [Test]
        public void ALeverThatHasBeenPulledRefusesAnotherPress()
        {
            LeverInteraction lever = NewLever(oneShot: true);

            Announce(lever, 0, OnInstant);

            Assert.IsFalse(lever.CanInteract(),
                "A one-shot lever is a latch that can only travel one way, and it has travelled.");
        }

        [Test]
        public void ADisabledLeverStopsFollowingTheSession()
        {
            LeverInteraction lever = NewLever();
            PullSpy spy = WatchPulls(lever);

            Life(lever, "OnDisable");
            Announce(lever, 0, OnInstant);

            Assert.IsFalse(lever.IsPulled);
            Assert.AreEqual(0, spy.Fired);
        }

        // ─────────── VolumeTrigger ───────────

        private class TriggerSpy : MonoBehaviour, ITriggerable
        {
            public int Fired;
            public GameObject LastInitiator;

            public bool CanTrigger(GameObject initiator) => true;

            public Coroutine Trigger(GameObject initiator)
            {
                Fired++;
                LastInitiator = initiator;

                // Null is a legal answer — ITriggerable documents it as "declined to start" — and
                // this one has no coroutine runner to hand back in an EditMode run.
                return null;
            }
        }

        private static void WalkInto(VolumeTrigger volume, Collider body) =>
            typeof(VolumeTrigger)
                .GetMethod("OnTriggerEnter", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(volume, new object[] { body });

        [Test]
        public void OnlyTheAuthorityDecidesThatAVolumeFired()
        {
            // The gate itself, read directly, because the interesting half of it cannot be built
            // here: making it false needs a live NetworkManager with this machine as a client.
            // What CAN be pinned down is the half that must never change — offline is always the
            // authority, so single-player behaves exactly as it did before any of this was gated.
            var decides = typeof(VolumeTrigger)
                .GetProperty("ThisMachineDecides", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(decides, "VolumeTrigger must keep an explicit, named authority gate.");
            Assert.IsTrue((bool)decides.GetValue(null),
                "With no session, this machine is every machine — the volume has to fire.");
        }

        [Test]
        public void AVolumeStillFiresForAPlayerInSinglePlayer()
        {
            GameObject host = NewObject("volume");

            // Added explicitly rather than left to [RequireComponent(typeof(Collider))], which
            // cannot instantiate an abstract component type.
            host.AddComponent<BoxCollider>().isTrigger = true;

            var volume = host.AddComponent<VolumeTrigger>();
            var spy = host.AddComponent<TriggerSpy>();

            GameObject player = NewObject("player");
            player.tag = "Player";
            var body = player.AddComponent<BoxCollider>();

            WalkInto(volume, body);

            Assert.AreEqual(1, spy.Fired, "Gating the volume must not have cost single-player its caves.");
            Assert.AreSame(player, spy.LastInitiator,
                "...and the action still gets the specific body that walked in, which is what " +
                "InteriorManager keys a return position on.");
        }

        [Test]
        public void AVolumeIgnoresSomethingThatIsNeitherPlayerNorAgent()
        {
            GameObject host = NewObject("volume");

            // Added explicitly rather than left to [RequireComponent(typeof(Collider))], which
            // cannot instantiate an abstract component type.
            host.AddComponent<BoxCollider>().isTrigger = true;

            var volume = host.AddComponent<VolumeTrigger>();
            var spy = host.AddComponent<TriggerSpy>();

            GameObject crate = NewObject("crate");
            var body = crate.AddComponent<BoxCollider>();

            WalkInto(volume, body);

            Assert.AreEqual(0, spy.Fired);
        }
    }
}
