// Assets/Game/Editor/Tests/MonowheelGroundContactTests.cs
//
// What carries a monowheel: its wheel, on flat ground and on slopes either way. Each test drops the
// built prefab onto a ground box in a PREVIEW scene and steps that scene's own physics, so the open
// scene is never touched. The motor runs no FixedUpdate in edit mode, so the wheel only settles
// under gravity -- exactly the question: what does it come to rest on?
//
// It used to come to rest on anything but the wheel. The hoop's boxes poked 13 cm past the paddles,
// and the level chassis boxes met rising ground first, so the pose put the ski on the sand and the
// wheel hung in the air.
using NUnit.Framework;
using SpaceGame.Vehicles.Monowheel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using S = SpaceGame.EditorTools.StriderMonowheelBuilder;

namespace SpaceGame.EditorTools
{
    public class MonowheelGroundContactTests
    {
        private const float Step = 0.02f;
        private const int SettleSteps = 200;
        /// The hoop is inscribed in the wheel: its flat faces sit at most this far (m) inside it.
        private const float HoopFlatTolerance = 0.04f;

        private Scene scene;

        [TearDown]
        public void TearDown()
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }

        private static readonly object[] Cases =
        {
            new object[] { "Runner", 0f }, new object[] { "Runner", 10f }, new object[] { "Runner", 20f },
            new object[] { "Runner", -20f }, new object[] { "Double", 0f }, new object[] { "Double", 20f },
            new object[] { "Double", -20f }, new object[] { "DoubleWide", 20f },
        };

        /// <param name="slope">Degrees the ground rises ahead of the wheel (negative: falls away).</param>
        [TestCaseSource(nameof(Cases))]
        public void TheWheel_CarriesTheVehicle(string variant, float slope)
        {
            scene = EditorSceneManager.NewPreviewScene();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(S.PrefabPath(variant));
            Assert.IsNotNull(prefab, $"{S.PrefabPath(variant)} is not built");
            var vehicle = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var art = vehicle.GetComponentInChildren<MonowheelPresentation>();
            // Edit mode raises no Awake.
            foreach (MonowheelChassis chassis in vehicle.GetComponentsInChildren<MonowheelChassis>()) chassis.IgnoreOwnWheel();

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.localScale = new Vector3(30f, 1f, 60f);
            ground.transform.rotation = Quaternion.Euler(-slope, 0f, 0f);
            ground.transform.position = -ground.transform.up * 0.5f;   // its top face passes through the origin

            // The wheel's contact half a metre above the ground, so it falls onto it.
            Vector3 contact = art.transform.TransformPoint(art.Wheels[0].localContact);
            vehicle.transform.position += Vector3.up * 0.5f - contact;

            var body = vehicle.GetComponent<Rigidbody>();
            PhysicsScene physics = scene.GetPhysicsScene();
            for (int i = 0; i < SettleSteps; i++)
            {
                // Held in place along the slope: the question is what it rests on, not where it slides.
                body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 0f);
                physics.Simulate(Step);
            }

            Vector3 up = ground.transform.up;
            for (int w = 0; w < art.Wheels.Count; w++)
            {
                MonowheelWheel wheel = art.Wheels[w];
                // The ring is a band, as wide as its hoop: its lowest point lies between its centre
                // circle's (hub height less the radius it reaches toward the ground, which a cambered
                // ring's lean shortens) and the band's downhill edge.
                Vector3 axle = wheel.ringBone.TransformDirection(wheel.localAxle).normalized;
                float tilt = Mathf.Abs(Vector3.Dot(axle, up));
                float halfWidth = vehicle.transform.Find($"Hoop_{w}").GetComponentInChildren<BoxCollider>().size.x * 0.5f;
                float centreGap = Vector3.Dot(art.transform.TransformPoint(wheel.localHub), up)
                                  - wheel.paddleRadius * Mathf.Sqrt(1f - tilt * tilt);
                Assert.GreaterOrEqual(centreGap, -HoopFlatTolerance, $"{variant} on {slope}°: the wheel sinks into the ground");
                Assert.LessOrEqual(centreGap - halfWidth * tilt, HoopFlatTolerance,
                    $"{variant} on {slope}°: the wheel should stand on the ground. A gap means something else is " +
                    "holding the vehicle up while its wheel hangs in the air");
            }
        }
    }
}
