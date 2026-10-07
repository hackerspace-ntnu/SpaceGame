// A sky vessel's drop-off run as a pure state machine: Cruise → Approach → Descend → Unload →
// Climb → Depart → Done. The pilot feeds it what its sensors read each tick; these tests feed it by
// hand. Passengers are only a count — swarmers flying alongside are never seated, so nothing here
// may assume the party is aboard.
using NUnit.Framework;
using SpaceGame.Vehicles;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class VesselMissionTests
    {
        private const float Dt = 0.25f;
        private const float CruiseAltitude = 300f;
        private const float Far = 10000f;

        private static readonly Vector3 Home = new Vector3(4000f, 228f, 1000f);
        private static readonly DropSite LandSite =
            new DropSite(new Vector3(65f, 12f, 0f), DropMode.Land, new Vector3(53f, 12f, 0f));
        private static readonly DropSite HoverSite =
            new DropSite(new Vector3(65f, 24f, 0f), DropMode.Hover, new Vector3(65f, 16f, 0f));

        private static VesselMissionSettings Settings => new VesselMissionSettings
        {
            approachDistance = 250f,
            arrivalTolerance = 3f,
            altitudeTolerance = 0.75f,
            unloadInterval = 0.5f,
            clearDelay = 1f,
            despawnDistance = 350f,
        };

        private static VesselSensors Read(float distance = Far, float altitudeError = 0f,
                                          int passengers = 0, float nearestPlayer = 100f) =>
            new VesselSensors(distance, altitudeError, passengers, nearestPlayer);

        // ── The whole run ──────────────────────────────────────────────────────────

        [Test]
        public void ALandingRunGoesFromCruiseToDone() => RunsToDone(LandSite);

        [Test]
        public void AHoverRunGoesFromCruiseToDone() => RunsToDone(HoverSite);

        private static void RunsToDone(DropSite site)
        {
            var mission = new VesselMission(Home, Settings);
            Assert.AreEqual(VesselMissionState.Cruise, mission.State);
            Assert.AreEqual(VesselDestination.Quarry, mission.Destination);

            mission.Tick(Read(distance: 1000f), Dt);
            Assert.AreEqual(VesselMissionState.Cruise, mission.State, "still far from the quarry");

            mission.Tick(Read(distance: 240f), Dt);
            Assert.AreEqual(VesselMissionState.Approach, mission.State);
            Assert.AreEqual(VesselDestination.Quarry, mission.Destination, "no site found yet");
            Assert.AreEqual(CruiseAltitude, mission.TargetAltitude(CruiseAltitude));

            mission.Tick(Read(distance: 0f), Dt);
            Assert.AreEqual(VesselMissionState.Approach, mission.State, "cannot descend without a site");

            mission.SetSite(site);
            Assert.AreEqual(VesselDestination.Site, mission.Destination);
            mission.Tick(Read(distance: 10f), Dt);
            Assert.AreEqual(VesselMissionState.Approach, mission.State, "not over the site yet");

            mission.Tick(Read(distance: 1f, altitudeError: 200f, passengers: 2), Dt);
            Assert.AreEqual(VesselMissionState.Descend, mission.State);
            Assert.AreEqual(site.Point.y, mission.TargetAltitude(CruiseAltitude),
                            site.Mode == DropMode.Land ? "down to the ground" : "down to hover height");

            mission.Tick(Read(distance: 1f, altitudeError: 5f, passengers: 2), Dt);
            Assert.AreEqual(VesselMissionState.Descend, mission.State, "still coming down");

            mission.Tick(Read(distance: 1f, altitudeError: 0.2f, passengers: 2), Dt);
            Assert.AreEqual(VesselMissionState.Unload, mission.State);

            int released = 0, passengers = 2;
            for (int i = 0; i < 40 && mission.State == VesselMissionState.Unload; i++)
            {
                int now = mission.Tick(Read(distance: 1f, passengers: passengers), Dt);
                released += now;
                passengers -= now;
            }
            Assert.AreEqual(2, released, "everyone aboard got off");
            Assert.AreEqual(VesselMissionState.Climb, mission.State);
            Assert.AreEqual(CruiseAltitude, mission.TargetAltitude(CruiseAltitude));
            Assert.AreEqual(VesselDestination.Site, mission.Destination, "climbs straight up first");

            mission.Tick(Read(altitudeError: -100f), Dt);
            Assert.AreEqual(VesselMissionState.Climb, mission.State);

            mission.Tick(Read(altitudeError: -0.5f), Dt);
            Assert.AreEqual(VesselMissionState.Depart, mission.State);
            Assert.AreEqual(VesselDestination.Home, mission.Destination);
            Assert.AreEqual(Home, mission.Home);

            mission.Tick(Read(nearestPlayer: 349f), Dt);
            Assert.AreEqual(VesselMissionState.Depart, mission.State, "a player could still see it vanish");

            mission.Tick(Read(nearestPlayer: 351f), Dt);
            Assert.AreEqual(VesselMissionState.Done, mission.State);
        }

        // ── Unloading ──────────────────────────────────────────────────────────────

        [Test]
        public void UnloadReleasesOnePassengerPerInterval()
        {
            VesselMission mission = AtUnload(passengers: 3);

            int passengers = 3;
            var releaseTicks = new System.Collections.Generic.List<int>();
            for (int tick = 1; tick <= 8; tick++)
            {
                int now = mission.Tick(Read(distance: 0f, passengers: passengers), Dt);
                Assert.LessOrEqual(now, 1, "never two at once");
                if (now == 1) releaseTicks.Add(tick);
                passengers -= now;
            }

            CollectionAssert.AreEqual(new[] { 2, 4, 6 }, releaseTicks, "one every 0.5 s");
            Assert.AreEqual(VesselMissionState.Unload, mission.State, "waits for the last one to clear the hull");

            mission.Tick(Read(distance: 0f, passengers: 0), Dt);
            mission.Tick(Read(distance: 0f, passengers: 0), Dt);
            Assert.AreEqual(VesselMissionState.Climb, mission.State, "then lifts off after the clear delay");
        }

        [Test]
        public void ALongTickNeverReleasesMoreThanAreAboard()
        {
            VesselMission mission = AtUnload(passengers: 1);

            Assert.AreEqual(1, mission.Tick(Read(distance: 0f, passengers: 1), 10f));
        }

        [Test]
        public void NoPassengersSkipsUnload()
        {
            var mission = new VesselMission(Home, Settings);
            mission.Tick(Read(distance: 0f), Dt);
            mission.SetSite(LandSite);
            mission.Tick(Read(distance: 0f, altitudeError: 50f), Dt);
            Assert.AreEqual(VesselMissionState.Descend, mission.State);

            mission.Tick(Read(distance: 0f, altitudeError: 0f, passengers: 0), Dt);

            Assert.AreEqual(VesselMissionState.Climb, mission.State, "nobody aboard, nothing to wait for");
        }

        // ── Changing site ──────────────────────────────────────────────────────────

        [Test]
        public void ANewSiteWhileDescendingGoesBackToApproach()
        {
            var mission = new VesselMission(Home, Settings);
            mission.Tick(Read(distance: 0f), Dt);
            mission.SetSite(LandSite);
            mission.Tick(Read(distance: 0f, altitudeError: 50f, passengers: 2), Dt);
            Assert.AreEqual(VesselMissionState.Descend, mission.State);

            mission.SetSite(HoverSite);

            Assert.AreEqual(VesselMissionState.Approach, mission.State, "no longer over the site it is descending onto");
            Assert.AreEqual(HoverSite.Point, mission.Site.Value.Point);
            Assert.AreEqual(VesselDestination.Site, mission.Destination);
            Assert.AreEqual(CruiseAltitude, mission.TargetAltitude(CruiseAltitude));
        }

        [Test]
        public void ANewSiteWhileUnloadingGoesBackToApproach()
        {
            VesselMission mission = AtUnload(passengers: 2);

            mission.SetSite(LandSite);

            Assert.AreEqual(VesselMissionState.Approach, mission.State);
            mission.Tick(Read(distance: 1f, altitudeError: 50f, passengers: 2), Dt);
            Assert.AreEqual(VesselMissionState.Descend, mission.State, "and flies the new site from the top");
        }

        // ── Aborting ───────────────────────────────────────────────────────────────

        [Test]
        public void AbortClimbsAwayWithWhoeverIsAboard()
        {
            var mission = new VesselMission(Home, Settings);
            mission.Tick(Read(distance: 0f), Dt);
            Assert.AreEqual(VesselMissionState.Approach, mission.State);

            mission.Abort();

            Assert.AreEqual(VesselMissionState.Climb, mission.State);
            mission.Tick(Read(altitudeError: 0f), Dt);
            Assert.AreEqual(VesselMissionState.Depart, mission.State);

            mission.Abort();
            Assert.AreEqual(VesselMissionState.Depart, mission.State, "already leaving");
        }

        [Test]
        public void ADescentOnlyUnloadsOnceOverTheSite()
        {
            var mission = new VesselMission(Home, Settings);
            mission.Tick(Read(distance: 0f), Dt);
            mission.SetSite(LandSite);
            mission.Tick(Read(distance: 0f, altitudeError: 40f, passengers: 1), Dt);
            Assert.AreEqual(VesselMissionState.Descend, mission.State);

            mission.Tick(Read(distance: 8f, altitudeError: 0f, passengers: 1), Dt);
            Assert.AreEqual(VesselMissionState.Descend, mission.State, "down at the height but not over the site");

            mission.Tick(Read(distance: 1f, altitudeError: 0f, passengers: 1), Dt);
            Assert.AreEqual(VesselMissionState.Unload, mission.State);
        }

        // ── Going home ─────────────────────────────────────────────────────────────

        [Test]
        public void ADepartingVesselIsDoneOnceItIsBackHome()
        {
            var mission = new VesselMission(Home, Settings);
            mission.Tick(Read(distance: 0f), Dt);
            mission.Abort();
            mission.Tick(Read(altitudeError: 0f), Dt);
            Assert.AreEqual(VesselMissionState.Depart, mission.State);

            mission.Tick(Read(distance: 50f, nearestPlayer: 20f), Dt);
            Assert.AreEqual(VesselMissionState.Depart, mission.State, "still on its way");

            mission.Tick(Read(distance: 1f, nearestPlayer: 20f), Dt);
            Assert.AreEqual(VesselMissionState.Done, mission.State,
                "docked at home: a player standing on the city must not keep it circling there");
        }

        private static VesselMission AtUnload(int passengers)
        {
            var mission = new VesselMission(Home, Settings);
            mission.Tick(Read(distance: 0f), Dt);
            mission.SetSite(HoverSite);
            mission.Tick(Read(distance: 0f, altitudeError: 50f, passengers: passengers), Dt);
            mission.Tick(Read(distance: 0f, altitudeError: 0f, passengers: passengers), Dt);
            Assert.AreEqual(VesselMissionState.Unload, mission.State);
            return mission;
        }
    }
}
