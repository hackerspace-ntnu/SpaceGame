// ViewCamera is "the camera this machine is drawing the game from". Camera.main is not that: it is the
// player's own on-foot camera, which riding switches off in favour of an Untagged orbit camera, so while
// mounted Camera.main is null and every distance LOD that asked it went dark or stopped fading.
//
// These render through cameras deliberately NOT tagged MainCamera, so a regression to "whatever
// Camera.main says" fails here rather than in a playtest from a saddle.
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SpaceGame.Core;

namespace SpaceGame.EditorTools
{
    public class ViewCameraTests
    {
        private Camera screen;
        private Camera other;
        private RenderTexture target;

        [SetUp]
        public void SetUp() => target = new RenderTexture(64, 64, 24);

        [TearDown]
        public void TearDown()
        {
            if (screen != null) Object.DestroyImmediate(screen.gameObject);
            if (other != null) Object.DestroyImmediate(other.gameObject);
            target.Release();
            Object.DestroyImmediate(target);
        }

        private static Camera MakeCamera(string name, Vector3 position)
        {
            var go = new GameObject(name) { tag = "Untagged" };
            go.transform.position = position;
            return go.AddComponent<Camera>();
        }

        private static void Draw(Camera cam)
        {
            // A camera drawn to the screen outside the player loop makes URP's final UI-overlay blit
            // log a size mismatch against whatever the Game view last was. That is the edit-mode
            // harness, not ViewCamera; and it is the screen camera this has to test. Set per draw:
            // the test runner resets it per test, after SetUp.
            LogAssert.ignoreFailingMessages = true;
            cam.Render();
        }

        [Test]
        public void ItIsTheGameCameraThatLastDrewToTheScreen_EvenWhenNoneIsTaggedMain()
        {
            screen = MakeCamera("OrbitCamera", new Vector3(40f, 10f, -300f));
            Assert.IsFalse(screen.CompareTag("MainCamera"), "a MainCamera-tagged camera proves nothing about riding");

            Draw(screen);

            Assert.AreSame(screen, ViewCamera.Current,
                "the camera drawing the frame was not reported: ViewCamera is following Camera.main again, " +
                "which is null while riding");
            Assert.AreEqual(Vector3.Distance(screen.transform.position, Vector3.zero), ViewCamera.DistanceTo(Vector3.zero), 1e-3f);
        }

        [Test]
        public void ACameraDrawingIntoATexture_IsNotTheView()
        {
            screen = MakeCamera("OrbitCamera", new Vector3(0f, 5f, -20f));
            other = MakeCamera("MapCamera", new Vector3(0f, 400f, 0f));
            other.targetTexture = target;

            Draw(screen);
            Draw(other);

            Assert.AreSame(screen, ViewCamera.Current, "a render-texture camera (a map, a preview) was taken for the view");
        }

        [Test]
        public void ANonGameCamera_IsNotTheView()
        {
            screen = MakeCamera("OrbitCamera", new Vector3(0f, 5f, -20f));
            other = MakeCamera("PreviewCamera", new Vector3(0f, 400f, 0f));
            other.cameraType = CameraType.Preview;

            Draw(screen);
            Draw(other);

            Assert.AreSame(screen, ViewCamera.Current, "a preview/scene-view camera was taken for the view");
        }

        [Test]
        public void OnceTheCameraThatDrewIsGone_ItFallsBackToCameraMain()
        {
            screen = MakeCamera("OrbitCamera", new Vector3(0f, 5f, -20f));
            Draw(screen);
            Object.DestroyImmediate(screen.gameObject);

            Assert.AreSame(Camera.main, ViewCamera.Current);
            Assert.AreEqual(Camera.main == null, float.IsNaN(ViewCamera.DistanceTo(Vector3.zero)),
                "no camera at all must read as NaN, the consumers' 'no camera' contract");
        }

        [Test]
        public void ADisabledCamera_IsNotTheView()
        {
            screen = MakeCamera("OrbitCamera", new Vector3(0f, 5f, -20f));
            Draw(screen);
            screen.enabled = false;

            Assert.AreSame(Camera.main, ViewCamera.Current, "a camera switched off (a dismount) is still being measured from");
        }
    }
}
