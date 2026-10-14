using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The workshop's story through a save, on the wire, and as the visor reads it: the start writes nothing, a hunt comes
    /// back with its band spawned (so its thieves are never spawned twice) and its clues found, the vault comes back with
    /// its core laid (so it is never laid twice), a save from before the vault goes on to it, every step of the story
    /// has the objective it should, and the main room counts as shut to the hidden hallways only while it faces the lane.
    /// </summary>
    public class WorkshopMissionTests
    {
        private static readonly Vector3 Camp = new(2565f, 104f, -95f);

        private GameObject host;
        private WorkshopMission mission;
        private WorkshopMissionSaveable saveable;
        private GearThiefCampSite site;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Workshop under test");
            host.AddComponent<Workshop>();
            saveable = host.AddComponent<WorkshopMissionSaveable>();

            var below = new GameObject("Mission");
            below.transform.SetParent(host.transform, false);
            mission = below.AddComponent<WorkshopMission>();

            site = ScriptableObject.CreateInstance<GearThiefCampSite>();
            site.Record(Camp, "Assets/Game/Scenes/world/Chunks/Chunk_5_1.unity");
            var so = new SerializedObject(mission);
            so.FindProperty("campSite").objectReferenceValue = site;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(site);
        }

        private JObject Saved() => JObject.FromObject(saveable.CaptureState(), SaveSerializer.Serializer);

        [Test]
        public void TheAuthoredStartIsNotWritten()
        {
            Assert.IsNull(saveable.CaptureState());
        }

        [Test]
        public void AHuntComesBackWithItsBandSpawnedAndItsCluesFound()
        {
            mission.Restore(WorkshopMissionPhase.Hunting, true, 2, false);
            JObject saved = Saved();

            mission.Restore(WorkshopMissionPhase.Locked, false, 0, false);
            Assert.AreEqual(WorkshopMissionPhase.Locked, mission.Phase);

            saveable.RestoreState(saved);

            Assert.AreEqual(WorkshopMissionPhase.Hunting, mission.Phase);
            Assert.IsTrue(mission.BandSpawned, "a band that was spawned is never spawned again");
            Assert.AreEqual(2, mission.CluesFound);
        }

        [Test]
        public void AnOpenGateThatNobodyHasMetAboutComesBackGathering()
        {
            mission.Restore(WorkshopMissionPhase.Gathering, false, 0, false);
            JObject saved = Saved();
            mission.Restore(WorkshopMissionPhase.Locked, false, 0, false);

            saveable.RestoreState(saved);

            Assert.AreEqual(WorkshopMissionPhase.Gathering, mission.Phase);
            Assert.IsFalse(mission.BandSpawned, "no thieves before the mission is given");
        }

        [Test]
        public void TheVaultComesBackWithItsCoreLaid()
        {
            mission.Restore(WorkshopMissionPhase.Vault, true, mission.Trail.Count, true);
            JObject saved = Saved();
            mission.Restore(WorkshopMissionPhase.Locked, false, 0, false);

            saveable.RestoreState(saved);

            Assert.AreEqual(WorkshopMissionPhase.Vault, mission.Phase);
            Assert.IsTrue(mission.CoreSpawned, "a core that was laid in the vault is never laid again");
        }

        [Test]
        public void ASaveFromBeforeTheVaultWithTheGearHomeGoesOnToTheVault()
        {
            saveable.RestoreState(JObject.Parse("{\"phase\":\"Done\",\"bandSpawned\":true,\"clues\":5}"));

            Assert.AreEqual(WorkshopMissionPhase.Vault, mission.Phase);
            Assert.IsFalse(mission.CoreSpawned, "the core is still to be laid");
        }

        [Test]
        public void APhaseThisBuildDoesNotHaveStartsTheStoryAgainLoudly()
        {
            mission.Restore(WorkshopMissionPhase.Repaired, false, 0, false);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("phase 'Feasting'"));

            saveable.RestoreState(JObject.Parse("{\"phase\":\"Feasting\"}"));

            Assert.AreEqual(WorkshopMissionPhase.Locked, mission.Phase);
        }

        [Test]
        public void TheStateCrossesTheWireWhole()
        {
            var sent = new WorkshopMissionState
            {
                phase = WorkshopMissionPhase.Vault, carried = true, bandSpawned = true, carrierHasGear = true, clues = 5,
                coreSpawned = true,
            };

            int wire = sent.WithLive(true);
            WorkshopMissionState heard = WorkshopMissionState.FromWire(wire);

            Assert.IsTrue(WorkshopMissionState.IsLive(wire));
            Assert.AreEqual(sent, heard);
            Assert.IsFalse(WorkshopMissionState.IsLive(heard.Bits), "the live flag is the message's, not the state's");
        }

        [Test]
        public void TheCampIsKnownBeforeItsChunkIsLoaded()
        {
            Assert.IsTrue(mission.TryGetCamp(out Vector3 camp), "the site asset, not a loaded GearThiefCamp");
            Assert.AreEqual(Camp, camp);
        }

        [Test]
        public void EveryStepOfTheStoryHasTheObjectiveItShould()
        {
            var objective = new WorkshopMissionObjective(mission);

            mission.Restore(WorkshopMissionPhase.Gathering, false, 0, false);
            Assert.AreEqual("Meet the Shellbacks in the main hall", objective.Title);

            mission.Restore(WorkshopMissionPhase.Hunting, false, 0, false);
            Assert.AreEqual("Follow the gear thieves' trail", objective.Title, "the trail is laid before the band exists");
            Assert.IsTrue(objective.TryGetWaypoint(out Vector3 search));
            Assert.AreNotEqual(Camp, search, "the trail's first search area, not the camp");

            mission.Restore(WorkshopMissionPhase.Hunting, true, mission.Trail.Count, false);
            Assert.AreEqual("Get the Heart Gear back from the thieves' camp", objective.Title);
            Assert.IsTrue(objective.TryGetWaypoint(out Vector3 waypoint));
            Assert.AreEqual(Camp, waypoint, "every clue found: the camp itself");

            mission.Restore(WorkshopMissionPhase.Vault, true, mission.Trail.Count, false);
            Assert.AreEqual("Take the reactor core from the vault", objective.Title, "no vault door under test: nothing locks it");

            mission.Restore(WorkshopMissionPhase.Repaired, true, mission.Trail.Count, true);
            Assert.AreEqual(string.Empty, objective.Title);
            Assert.IsFalse(objective.TryGetWaypoint(out _));
        }

        [Test]
        public void TheMainRoomIsShutToTheHallwaysOnlyWhileItFacesTheLane()
        {
            var wheel = new GameObject("Main hand cog").AddComponent<WorkshopWheel>();
            wheel.transform.SetParent(host.transform, false);
            var so = new SerializedObject(mission);
            so.FindProperty("mainWheel").objectReferenceValue = wheel;
            so.ApplyModifiedPropertiesWithoutUndo();

            wheel.Restore(0f);
            Assert.IsTrue(mission.MainRoomFacesLane, "as modelled: facing the lane");
            wheel.Restore(365f);
            Assert.IsTrue(mission.MainRoomFacesLane, "a whole turn round and a little: the lane again");
            wheel.Restore(48.7f);
            Assert.IsFalse(mission.MainRoomFacesLane, "facing J5, the nearest hallway to the lane");
            wheel.Restore(-54.2f);
            Assert.IsFalse(mission.MainRoomFacesLane, "facing J1, the nearest the other way");
        }
    }
}
