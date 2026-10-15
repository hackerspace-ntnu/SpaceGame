// Assets/Game/Editor/Tests/MonowheelLeanTests.cs
//
// The chassis pose MonowheelLean puts on a real Strider monowheel: the ski brought down onto the
// sand by tipping about the hub, and the helm swung with the rider's weight. Each test drops the
// built prefab over a ground box in a PREVIEW scene (Lean probes through its own physics scene, so
// the open scene is never touched) and steps Pose by hand, because EditMode runs no LateUpdate.
using NUnit.Framework;
using SpaceGame.Vehicles.Monowheel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using S = SpaceGame.EditorTools.StriderMonowheelBuilder;

namespace SpaceGame.EditorTools
{
    public class MonowheelLeanTests
    {
        private const float Dt = 1f / 60f;
        private Scene scene;
        private GameObject vehicle;
        private MonowheelLean lean;
        private MonowheelPresentation art;

        [TearDown]
        public void TearDown()
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }

        // The prefab at the origin over flat ground whose top is where its wheel touches.
        private void Spawn(string variant, bool withGround = true)
        {
            scene = EditorSceneManager.NewPreviewScene();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(S.PrefabPath(variant));
            Assert.IsNotNull(prefab, $"{S.PrefabPath(variant)} is not built");
            vehicle = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            lean = vehicle.GetComponent<MonowheelLean>();
            art = vehicle.GetComponentInChildren<MonowheelPresentation>();
            Assert.IsNotNull(lean, variant);
            Assert.IsNotNull(art, variant);

            if (withGround)
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.layer = LayerMask.NameToLayer("Ground");
                ground.transform.localScale = new Vector3(60f, 1f, 60f);
                ground.transform.position = new Vector3(0f, ContactY() - 0.5f, 0f);
            }
            Physics.SyncTransforms();
        }

        private float ContactY() => art.transform.TransformPoint(art.Wheels[0].localContact).y;
        private Vector3 Hub() => art.transform.TransformPoint(art.Wheels[0].localHub);
        // The helm panel's far end from its hinge, in the chassis' own frame, so the chassis' roll and
        // pitch do not count as the helm moving.
        private Vector3 HelmTrailingEdge()
        {
            Bounds b = art.Helm.GetComponent<MeshFilter>().sharedMesh.bounds;
            Vector3 hingeLocal = art.Helm.InverseTransformPoint(art.transform.TransformPoint(art.HelmHinge));
            Vector3 far = (b.min - hingeLocal).sqrMagnitude > (b.max - hingeLocal).sqrMagnitude ? b.min : b.max;
            return art.transform.InverseTransformPoint(art.Helm.TransformPoint(new Vector3(b.center.x, b.center.y, far.z)));
        }

        private void Pose(int frames, Vector3 movePerFrame = default, float yawPerFrame = 0f)
        {
            for (int i = 0; i < frames; i++)
            {
                vehicle.transform.position += movePerFrame;
                vehicle.transform.Rotate(0f, yawPerFrame, 0f, Space.World);
                Physics.SyncTransforms();
                lean.Pose(Dt);
            }
        }

        [Test]
        public void OnFlatGround_TheSkiSettlesOntoTheSand_AndTheWheelStaysPut()
        {
            Spawn("Runner");
            float ground = ContactY();
            Vector3 hubBefore = Hub();

            Pose(240);

            float ski = art.transform.TransformPoint(art.SkiLowPoint).y;
            Assert.AreEqual(ground, ski, 0.03f, "the ski's lowest point rests on the sand");
            Assert.AreEqual(0f, Vector3.Distance(hubBefore, Hub()), 0.01f, "the chassis tips about the hub");
        }

        [Test]
        public void TheDoubleWide_HasNoSki_AndIsNeverPitched()
        {
            Spawn("DoubleWide");
            Quaternion rest = art.transform.localRotation;

            Pose(240);

            Assert.Less(Quaternion.Angle(rest, art.transform.localRotation), 0.01f);
        }

        [Test]
        public void InTheAir_TheChassisRelaxesToItsRestPitch()
        {
            Spawn("Runner", withGround: false);
            Quaternion rest = art.transform.localRotation;

            Pose(240);

            Assert.Less(Quaternion.Angle(rest, art.transform.localRotation), 0.5f);
        }

        [Test]
        public void TurningRight_SwingsTheHelmToTheInsideOfTheTurn()
        {
            Spawn("Runner");
            Pose(30);
            Vector3 before = HelmTrailingEdge();

            // 12 m/s, turning right at 30°/s.
            for (int i = 0; i < 90; i++)
                Pose(1, vehicle.transform.forward * (12f * Dt), 30f * Dt);

            Assert.Greater(HelmTrailingEdge().x - before.x, 0.05f, "the helm's weight swings to the inside of the turn");
        }

        [Test]
        public void TheHelmStaysOnItsHinge()
        {
            Spawn("Runner");
            Vector3 hinge = art.HelmHinge;
            Vector3 hingeInHelm = art.Helm.InverseTransformPoint(art.transform.TransformPoint(hinge));

            for (int i = 0; i < 90; i++)
                Pose(1, vehicle.transform.forward * (12f * Dt), 30f * Dt);

            Vector3 hingeNow = art.transform.InverseTransformPoint(art.Helm.TransformPoint(hingeInHelm));
            Assert.AreEqual(0f, Vector3.Distance(hinge, hingeNow), 0.005f, "the panel turns about the post, it does not slide off it");
        }
    }
}
