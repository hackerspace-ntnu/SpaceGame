// Assets/Game/Editor/Tests/TiltingSeatsTests.cs
//
// A monowheel's riders tip with their seats. MonowheelLean rolls and pitches the Body the seats hang
// under, but every networked seating path parents a rider to the vehicle's ROOT with the seat's pose
// folded in once (netcode allows no bare marker as a parent), so without TiltingSeats a rider stays
// where and how the seat was when they sat down. Each test reproduces that root-parented state by
// hand on the built prefab, tips the Body, and steps the hold.
using NUnit.Framework;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using S = SpaceGame.EditorTools.StriderMonowheelBuilder;

namespace SpaceGame.EditorTools
{
    public class TiltingSeatsTests
    {
        private Scene scene;
        private GameObject vehicle;
        private Transform body;
        private TiltingSeats tilting;

        [TearDown]
        public void TearDown()
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }

        private void Spawn(string prefabPath)
        {
            scene = EditorSceneManager.NewPreviewScene();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, $"{prefabPath} is not built");
            vehicle = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            body = vehicle.transform.Find(S.BodyName);
            tilting = vehicle.GetComponent<TiltingSeats>();
            Assert.IsNotNull(tilting, $"{prefabPath} has no TiltingSeats");
        }

        private Transform Seat(string name) => body.Find(name);

        private static Vector3 Offset(Object component, string field) =>
            new SerializedObject(component).FindProperty(field).vector3Value;

        // What MonowheelLean does to the Body in a hard right turn on a slope.
        private void TipTheBody() => body.localRotation = Quaternion.Euler(12f, 0f, -20f);

        private static void AssertOn(Transform rider, Transform seat, Vector3 offset)
        {
            Assert.Less(Vector3.Distance(seat.TransformPoint(offset), rider.position), 1e-3f, "the rider is on the seat");
            Assert.Less(Quaternion.Angle(seat.rotation, rider.rotation), 0.01f, "the rider tilts with the seat");
        }

        [Test]
        public void ASeatedStrider_TipsWithTheSeatTheyWereSeatedOn()
        {
            Spawn(S.PrefabPath("Double"));
            Vector3 offset = Offset(tilting, "npcSeatOffset");
            Transform side = Seat(S.SideSeatPrefix + "1");

            // What NpcSeating leaves behind: an NPC under the root at the seat's pose of the moment.
            var npc = new GameObject("Gunner");
            npc.AddComponent<AgentController>();
            npc.transform.SetParent(vehicle.transform, false);
            npc.transform.SetPositionAndRotation(side.TransformPoint(offset), side.rotation);
            tilting.Refresh();

            TipTheBody();
            tilting.Hold();

            AssertOn(npc.transform, side, offset);
        }

        [Test]
        public void AMountedPlayer_TipsWithTheSaddle()
        {
            Spawn(S.PlayerPrefabPath);
            var mount = vehicle.GetComponent<MountModule>();
            var cooldown = new SerializedObject(mount);
            // Edit mode never advances Time.time, so the cooldown would refuse every mount.
            cooldown.FindProperty("mountCooldown").floatValue = 0f;
            cooldown.ApplyModifiedPropertiesWithoutUndo();

            var rider = new GameObject("Rider");
            SceneManager.MoveGameObjectToScene(rider, scene);
            rider.AddComponent<PlayerMovement>();
            Interactor interactor = rider.AddComponent<Interactor>();
            Assert.IsTrue(mount.TryMount(interactor, null), "the rider should have been seated");

            // Where the networked path leaves a player: under the root, not the seat marker.
            rider.transform.SetParent(vehicle.transform, true);

            TipTheBody();
            tilting.Hold();

            AssertOn(rider.transform, mount.ActiveSeatPoint, mount.SeatOffset);
            mount.Dismount();
        }
    }
}
