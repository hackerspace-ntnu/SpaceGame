// Who gets the wheel, and does every machine agree.
//
// The property under test is the one the whole VehicleStation protocol exists for: a station's
// occupancy is decided in ONE place and everybody else is told, rather than each machine deciding
// for itself and both being right. Two players pressing E on the same frame is the case that used
// to have two answers.
//
// Driven with no NetworkManager, which is what an EditMode test, a scene opened straight from the
// editor and a torn-down session all look like. That is not a compromise — it is the degradation
// contract NetMessaging is built on: with no wire every send runs the handler locally, so a claim
// and its answer both land inside the call that made them and the whole round trip is observable
// without a session. What it deliberately cannot cover is ownership handoff and the disconnect
// poll, which need real client ids; those are the two-process run's job.
//
// In Editor/ rather than beside the other EditMode tests because VehicleStation lives in the default
// assembly, and an asmdef cannot reference Assembly-CSharp.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public class VehicleStationTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);

            spawned.Clear();
        }

        // ─────────── Rig ───────────

        /// <summary>
        /// A station whose only behaviour is to record what the base class told it.
        ///
        /// Awake and OnEnable do not run in edit mode, so <see cref="Boot"/> stands in for the
        /// enable. Nothing else is faked: the messages below travel the real NetMessaging path.
        /// </summary>
        private class TestStation : VehicleStation
        {
            public int MannedCalls;
            public int UnmannedCalls;
            public int ApplyCalls;

            public bool exclusive = true;
            public bool refuseEverybody;

            protected override bool Exclusive => exclusive;
            protected override bool CanBeManned(GameObject player) => !refuseEverybody;

            protected override void OnManned(GameObject player) => MannedCalls++;

            protected override void OnUnmanned(GameObject player) => UnmannedCalls++;

            protected override void ApplyValue(float position) => ApplyCalls++;

            // The station's own two buttons. Nothing under test presses them — the tests drive the
            // claim API directly — but IInteractable is abstract on the base and a station has to
            // answer the crosshair.
            public override bool CanInteract() => true;

            public override void Interact(Interactor interactor) => RequestClaim(interactor);

            /// <summary>
            /// Stand in for the OnEnable and OnDisable Unity never runs in edit mode. Called
            /// explicitly so the tests exercise the code path rather than depending on which
            /// lifecycle messages the editor happens to deliver outside play mode.
            /// </summary>
            public void Boot() => OnEnable();

            public void Shutdown() => OnDisable();

            /// <summary>Public doors onto the protected request API, so a test can press E.</summary>
            public void Claim(Interactor who, float wanted = 0f) => RequestClaim(who, wanted);

            public void StandDown(Interactor who) => RequestRelease(who);
        }

        /// <summary>A hull carrying <paramref name="count"/> stations, the way a craft is built.</summary>
        private TestStation[] BuildVehicle(int count)
        {
            var hull = new GameObject("Hull");
            spawned.Add(hull);

            var stations = new TestStation[count];
            for (int i = 0; i < count; i++)
            {
                var node = new GameObject($"Station_{i}");
                node.transform.SetParent(hull.transform, false);
                stations[i] = node.AddComponent<TestStation>();
                stations[i].Boot();
            }
            return stations;
        }

        /// <summary>A body with an Interactor on it, which is how the real player is put together.</summary>
        private Interactor BuildPlayer(string name)
        {
            var player = new GameObject(name);
            spawned.Add(player);
            return player.AddComponent<Interactor>();
        }

        // ─────────── Claiming ───────────

        [Test]
        public void ClaimingAFreeStationSeatsTheClaimant()
        {
            TestStation station = BuildVehicle(1)[0];
            Interactor player = BuildPlayer("helmsman");

            station.Claim(player);

            Assert.IsTrue(station.IsManned, "Nobody was at the wheel, so the claim had to take.");
            Assert.AreSame(player.gameObject, station.Occupant);
            Assert.AreEqual(1, station.MannedCalls);
        }

        [Test]
        public void TwoPlayersRacingForTheWheel_OnlyTheFirstGetsIt()
        {
            TestStation station = BuildVehicle(1)[0];
            Interactor first = BuildPlayer("first");
            Interactor second = BuildPlayer("second");

            station.Claim(first);
            station.Claim(second);

            Assert.AreSame(first.gameObject, station.Occupant,
                "An exclusive station must not change hands under the person already at it.");
            Assert.AreEqual(1, station.MannedCalls,
                "The refused claim must not have re-seated anybody, not even the same person.");
            Assert.AreEqual(0, station.UnmannedCalls,
                "...and must not have knocked the first player off on the way past.");
        }

        [Test]
        public void ANonExclusiveStationChangesHandsToTheNewestPress()
        {
            TestStation station = BuildVehicle(1)[0];
            station.exclusive = false;

            Interactor first = BuildPlayer("first");
            Interactor second = BuildPlayer("second");

            station.Claim(first);
            station.Claim(second);

            Assert.AreSame(second.gameObject, station.Occupant,
                "A winch two crew are both hauling on is a crew, not a conflict.");
            Assert.AreEqual(1, station.UnmannedCalls, "The first player has to be let go of exactly once.");
            Assert.AreEqual(2, station.MannedCalls);
        }

        [Test]
        public void ARefusedClaimLeavesTheStationFree()
        {
            TestStation station = BuildVehicle(1)[0];
            station.refuseEverybody = true;

            station.Claim(BuildPlayer("hopeful"));

            Assert.IsFalse(station.IsManned);
            Assert.AreEqual(0, station.MannedCalls);
        }

        // ─────────── Standing down ───────────

        [Test]
        public void OnlyTheOccupantMayStandDown()
        {
            TestStation station = BuildVehicle(1)[0];
            Interactor helmsman = BuildPlayer("helmsman");
            Interactor bystander = BuildPlayer("bystander");

            station.Claim(helmsman);
            station.StandDown(bystander);

            Assert.IsTrue(station.IsManned,
                "A second player looking at the wheel must not be able to take it out from under " +
                "the helmsman by pressing E at it.");
            Assert.AreSame(helmsman.gameObject, station.Occupant);
        }

        [Test]
        public void TheOccupantMayStandDown()
        {
            TestStation station = BuildVehicle(1)[0];
            Interactor helmsman = BuildPlayer("helmsman");

            station.Claim(helmsman);
            station.StandDown(helmsman);

            Assert.IsFalse(station.IsManned);
            Assert.AreEqual(1, station.UnmannedCalls);
        }

        [Test]
        public void StandingDownFromAFreeStationChangesNothing()
        {
            TestStation station = BuildVehicle(1)[0];

            station.StandDown(BuildPlayer("nobody"));

            Assert.IsFalse(station.IsManned);
            Assert.AreEqual(0, station.UnmannedCalls,
                "An unmanned station told to unman itself must not raise the hook that gives a " +
                "player their legs back — there is no player.");
        }

        // ─────────── Idempotency ───────────

        [Test]
        public void ReClaimingByTheSamePlayerIsARenewalAndNotASecondSeating()
        {
            TestStation station = BuildVehicle(1)[0];
            Interactor helmsman = BuildPlayer("helmsman");

            station.Claim(helmsman);
            station.Claim(helmsman, 0.5f);
            station.Claim(helmsman, -0.5f);

            Assert.AreEqual(1, station.MannedCalls,
                "The helm's ten-a-second heartbeat is a renewal. Treated as a fresh claim it would " +
                "re-run everything OnManned does — including taking the player's movement away and " +
                "re-measuring their stance — once every hundred milliseconds for the whole voyage.");
            Assert.AreEqual(0, station.UnmannedCalls);
        }

        [Test]
        public void TheOccupantIsNotToldTheValueItJustSent()
        {
            TestStation station = BuildVehicle(1)[0];
            Interactor helmsman = BuildPlayer("helmsman");

            station.Claim(helmsman, 0.75f);

            // Offline this machine both decides and occupies, so the echo of its own input must not
            // come back round and overwrite the control it is already driving. On a client that is
            // the difference between a wheel that answers and a wheel that stutters a round trip
            // behind the hand turning it.
            Assert.AreEqual(0, station.ApplyCalls,
                "A station must never apply the published value on the machine that is the occupant.");
        }

        // ─────────── Late joiners ───────────

        [Test]
        public void TheLateJoinersQuestionIsAnsweredWithWhoIsActuallyThere()
        {
            TestStation station = BuildVehicle(1)[0];
            Interactor helmsman = BuildPlayer("helmsman");
            station.Claim(helmsman, 0.4f);

            // Listen on the vehicle's channel for the answer, which is the same thing a client that
            // connected after the helm was taken is doing. Without it a station claimed before you
            // arrived reads as free, and walking up to an occupied wheel offers you the helm.
            NetArg answer = default;
            int answers = 0;

            void Record(in NetArg arg, ulong sender)
            {
                answer = arg;
                answers++;
            }

            station.NetOn(NetMsg.StationState, Record);
            try
            {
                station.NetToServer(NetMsg.StationClaim,
                                    new NetArg { A = station.StationIndex, B = -1 });
            }
            finally
            {
                station.NetOff(NetMsg.StationState, Record);
            }

            Assert.AreEqual(1, answers, "The query must be answered exactly once.");
            Assert.AreEqual(1, answer.B, "B = 1 is 'manned'. Answered 0, the joiner sees a free wheel.");
            Assert.AreSame(helmsman.gameObject, answer.Resolve(),
                "The answer has to name the helmsman, not merely say that somebody is there.");
        }

        [Test]
        public void TheQuestionAboutAFreeStationIsAnsweredFree()
        {
            TestStation station = BuildVehicle(1)[0];

            NetArg answer = default;
            int answers = 0;

            void Record(in NetArg arg, ulong sender)
            {
                answer = arg;
                answers++;
            }

            station.NetOn(NetMsg.StationState, Record);
            try
            {
                station.NetToServer(NetMsg.StationClaim,
                                    new NetArg { A = station.StationIndex, B = -1 });
            }
            finally
            {
                station.NetOff(NetMsg.StationState, Record);
            }

            Assert.AreEqual(1, answers);
            Assert.AreEqual(0, answer.B);
            Assert.AreEqual(0UL, answer.Target, "A free station names nobody.");
            Assert.IsNull(answer.Resolve());
        }

        // ─────────── Numbering ───────────

        [Test]
        public void StationsOnOneVehicleAreNumberedDistinctly()
        {
            TestStation[] stations = BuildVehicle(4);

            var seen = new HashSet<int>();
            foreach (TestStation station in stations)
                Assert.IsTrue(seen.Add(station.StationIndex),
                    $"Two stations answer to index {station.StationIndex}, so a message meaning the " +
                    "wheel would also work the jib sheet.");

            Assert.AreEqual(4, seen.Count);
        }

        [Test]
        public void AMessageForOneStationDoesNotReachItsNeighbours()
        {
            TestStation[] stations = BuildVehicle(3);
            Interactor player = BuildPlayer("crew");

            stations[1].Claim(player);

            Assert.IsTrue(stations[1].IsManned);
            Assert.IsFalse(stations[0].IsManned, "Every station on the hull shares one channel, so " +
                                                 "each has to filter on its own index.");
            Assert.IsFalse(stations[2].IsManned);
        }

        // ─────────── Teardown ───────────

        [Test]
        public void DisablingAMannedStationGivesTheOccupantBack()
        {
            TestStation station = BuildVehicle(1)[0];
            Interactor helmsman = BuildPlayer("helmsman");

            station.Claim(helmsman);
            station.Shutdown();

            Assert.IsFalse(station.IsManned);
            Assert.AreEqual(1, station.UnmannedCalls,
                "A chunk unloading under a helmsman must not leave them with their movement " +
                "switched off and nothing left to switch it back on.");
        }
    }
}
