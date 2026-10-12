using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// A workshop through a save: what <c>WorkshopSaveable</c> writes is what it reads back, and a save
    /// written before the rooms had wheels, or while the cranes were still worked by hand, still loads.
    /// </summary>
    public class WorkshopSaveableTests
    {
        private GameObject host;
        private WorkshopSaveable saveable;
        private WorkshopWheel wheel;
        private WorkshopTurntable room;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Workshop under test");
            host.AddComponent<Workshop>();
            saveable = host.AddComponent<WorkshopSaveable>();

            var turning = new GameObject("Room");
            turning.transform.SetParent(host.transform, false);
            room = turning.AddComponent<WorkshopTurntable>();

            var post = new GameObject("Wheel");
            post.transform.SetParent(host.transform, false);
            wheel = post.AddComponent<WorkshopWheel>();
            var so = new SerializedObject(wheel);
            so.FindProperty("id").stringValue = "R07";
            so.FindProperty("room").objectReferenceValue = room;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(host);

        private JObject Saved() => JObject.FromObject(saveable.CaptureState(), SaveSerializer.Serializer);

        [Test]
        public void ARoomComesBackTurnedAsFarAsItWasLeft()
        {
            wheel.Restore(212.5f);
            JObject saved = Saved();

            saveable.RestoreState(null);
            Assert.AreEqual(0f, room.Angle, 1e-4f, "no save is the modelled pose");

            saveable.RestoreState(saved);

            Assert.AreEqual(212.5f, wheel.Angle, 1e-3f);
            Assert.AreEqual(212.5f, room.Angle, 1e-3f, "the room itself, not only the number");
        }

        [Test]
        public void ASaveFromBeforeTheWheelsLoadsWithEveryRoomAsModelled()
        {
            wheel.Restore(40f);
            saveable.RestoreState(JObject.Parse("{\"mainStop\":1,\"secondStop\":2,\"gearSeat\":1,\"craneStep\":3}"));

            Assert.AreEqual(0f, room.Angle, 1e-4f);
        }

        [Test]
        public void ASaveFromWhenTheCranesWereWorkedByHandStillBringsItsRoomsBack()
        {
            saveable.RestoreState(JObject.Parse("{\"craneStep\":3,\"hoistBridge\":2,\"gantryHook\":1,\"rooms\":{\"R07\":95.5}}"));

            Assert.AreEqual(95.5f, room.Angle, 1e-3f);
        }

        [Test]
        public void TheAuthoredStartIsNotWrittenButATurnedRoomAloneIs()
        {
            Assert.IsNull(saveable.CaptureState());

            wheel.Restore(-15f);

            Assert.AreEqual(-15f, Saved()["rooms"]["R07"].Value<float>(), 1e-4f);
        }
    }
}
