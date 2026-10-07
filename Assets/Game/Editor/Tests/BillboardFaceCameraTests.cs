// BillboardFaceCamera turns a flat quad toward the viewer — the ball lightning orb is one: a 23 m
// Quad painted by Custom/BallLighting, readable only face-on. It used to turn toward Camera.main,
// which is not the camera drawing the frame in most of the game's states: riding (the orbit camera
// is spawned Untagged and the player's own is switched off), spectating, a cutscene or a focus shot.
// Camera.main is then null or someone else's, the quad keeps whatever heading the projectile gave
// it, and from the side the orb is a card seen edge-on — a line, or nothing.
//
// These render through cameras that are deliberately NOT tagged MainCamera, so a regression to
// "whatever Camera.main says" fails here rather than in a playtest from a saddle.
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Weapons;

namespace SpaceGame.EditorTools
{
    public class BillboardFaceCameraTests
    {
        private const string BallLightningPrefabPath =
            "Assets/Game/Prefabs/Items/Artifacts/ArtifactResources/BallLightningProjectile.prefab";

        private GameObject billboard;
        private Camera first;
        private Camera second;
        private RenderTexture target;

        [SetUp]
        public void SetUp()
        {
            target = new RenderTexture(64, 64, 24);

            billboard = GameObject.CreatePrimitive(PrimitiveType.Quad);
            billboard.name = "BillboardUnderTest";
            billboard.transform.SetPositionAndRotation(new Vector3(0f, 2f, 0f), Quaternion.identity);
            billboard.AddComponent<BillboardFaceCamera>();

            first = MakeCamera("FirstViewer", new Vector3(-12f, 6f, 3f));
            second = MakeCamera("SecondViewer", new Vector3(5f, 1f, -15f));

            Invoke(billboard.GetComponent<BillboardFaceCamera>(), "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            if (billboard != null)
            {
                Invoke(billboard.GetComponent<BillboardFaceCamera>(), "OnDisable");
                Object.DestroyImmediate(billboard);
            }

            if (first != null) Object.DestroyImmediate(first.gameObject);
            if (second != null) Object.DestroyImmediate(second.gameObject);
            if (target != null)
            {
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ItFacesTheCameraThatIsDrawingEvenWhenNoCameraIsTaggedMain()
        {
            Assert.IsFalse(first.CompareTag("MainCamera"),
                "the test camera is tagged MainCamera, so this proves nothing about the mounted case");

            first.Render();

            Assert.Less(Vector3.Angle(billboard.transform.forward, first.transform.forward), 0.5f,
                "the billboard did not turn toward the camera rendering it. It is following " +
                "Camera.main again, which is null while riding and someone else's in a cutscene.");
        }

        [Test]
        public void EachCameraSeesItFaceOn()
        {
            first.Render();
            Assert.Less(Vector3.Angle(billboard.transform.forward, first.transform.forward), 0.5f);

            second.Render();
            Assert.Less(Vector3.Angle(billboard.transform.forward, second.transform.forward), 0.5f,
                "a second camera drew the billboard at the first camera's angle");
        }

        [Test]
        public void ThePinnedCameraIsTheOnlyOneItTurnsFor()
        {
            var component = billboard.GetComponent<BillboardFaceCamera>();
            var so = new SerializedObject(component);
            so.FindProperty("targetCamera").objectReferenceValue = first;
            so.ApplyModifiedPropertiesWithoutUndo();

            first.Render();
            second.Render();

            Assert.Less(Vector3.Angle(billboard.transform.forward, first.transform.forward), 0.5f,
                "a billboard pinned to one camera turned for another");
        }

        [Test]
        public void TheBallLightningOrbIsBillboardedOnItsOwnQuad()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BallLightningPrefabPath);
            Assert.IsNotNull(prefab, $"missing: {BallLightningPrefabPath}");

            var face = prefab.GetComponent<BillboardFaceCamera>();
            Assert.IsNotNull(face, "the orb's quad has no BillboardFaceCamera, so it is a flat card " +
                                   "that vanishes edge-on");
            Assert.IsTrue(face.enabled, "the orb's BillboardFaceCamera is disabled");

            var renderer = prefab.GetComponent<MeshRenderer>();
            Assert.IsNotNull(renderer, "the billboard is no longer on the object that draws the orb");
            Assert.IsNotNull(renderer.sharedMaterial, "the orb has no material");
            Assert.AreEqual("Custom/BallLighting", renderer.sharedMaterial.shader.name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader),
                "Custom/BallLighting does not compile");
        }

        private Camera MakeCamera(string name, Vector3 position)
        {
            var go = new GameObject(name) { tag = "Untagged" };
            var cam = go.AddComponent<Camera>();
            cam.targetTexture = target;
            go.transform.position = position;
            go.transform.LookAt(billboard.transform.position + new Vector3(0.7f, -0.4f, 0.2f));
            return cam;
        }

        private static void Invoke(Component component, string method)
        {
            MethodInfo info = component.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(info, $"{component.GetType().Name} has no {method}");
            info.Invoke(component, null);
        }
    }
}
