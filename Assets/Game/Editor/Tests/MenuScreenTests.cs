// Tests for MenuScreen's canvas hiding — specifically its blast radius.
//
// The property pinned here is scoping: a screen switches every other canvas in its OWN scene off
// while it is up, and must leave canvases from other scenes alone. The bug this guards against was
// invisible for weeks: HandOff() launches into gameplay without restoring what it hid (reasonable —
// the menu scene is about to be unloaded), but WorldOverlay, ChatUI and the other DontDestroyOnLoad
// surfaces are NOT in the menu scene and survive the load. Hiding them stranded their canvases
// disabled for the whole play session, and every nameplate and damage number quietly stopped
// rendering while the components kept running.
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    public class MenuScreenTests
    {
        /// <summary>The smallest possible concrete screen — the canvas mechanics are all inherited.</summary>
        private sealed class TestScreen : MenuScreen
        {
            protected override void Build() { }
            public void PresentForTest() => Present();
        }

        private readonly System.Collections.Generic.List<GameObject> spawned = new();
        private Scene otherScene;
        private bool hadEventSystem;

        [SetUp]
        public void SetUp() => hadEventSystem = EventSystem.current != null;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();

            // Present() creates one through UIBuilder.EnsureEventSystem when the open scene has
            // none; leaving it behind would dirty whatever scene the editor happens to have open.
            if (!hadEventSystem && EventSystem.current != null)
                Object.DestroyImmediate(EventSystem.current.gameObject);

            if (otherScene.IsValid())
                EditorSceneManager.CloseScene(otherScene, true);
        }

        private GameObject NewCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas));
            spawned.Add(go);
            return go;
        }

        [Test]
        public void PresentHidesAndCloseRestoresCanvasesInItsOwnScene()
        {
            GameObject menuCanvas = NewCanvas("MainMenuCanvas");

            var screen = new GameObject("screen").AddComponent<TestScreen>();
            spawned.Add(screen.gameObject);
            screen.PresentForTest();

            Assert.IsFalse(menuCanvas.GetComponent<Canvas>().enabled,
                "A screen switches the menu's own canvases off so only its text sits over the scene.");

            screen.Close();

            Assert.IsTrue(menuCanvas.GetComponent<Canvas>().enabled,
                "Backing out of a screen must put the canvases it hid back on.");
        }

        [Test]
        public void PresentLeavesCanvasesFromOtherScenesAlone()
        {
            // Stands in for WorldOverlay/ChatUI: at runtime those live in the DontDestroyOnLoad
            // scene, which is never the scene the screen itself spawns into.
            Scene home = SceneManager.GetActiveScene();
            try
            {
                otherScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            }
            catch (System.InvalidOperationException)
            {
                // Unity permits only one untitled scene, and a new additive scene is untitled
                // until saved. With an unsaved untitled scene open in the editor this test cannot
                // build its second scene — and must not fix that by touching the user's scene.
                Assert.Inconclusive("An unsaved untitled scene is open; cannot create the additive scene this test needs.");
            }
            SceneManager.SetActiveScene(home);

            GameObject survivor = NewCanvas("WorldOverlayStandIn");
            SceneManager.MoveGameObjectToScene(survivor, otherScene);

            var screen = new GameObject("screen").AddComponent<TestScreen>();
            spawned.Add(screen.gameObject);
            screen.PresentForTest();

            Assert.IsTrue(survivor.GetComponent<Canvas>().enabled,
                "A canvas outside the screen's scene outlives the scene load HandOff precedes, and " +
                "HandOff never restores — so hiding it strands it disabled for the whole session.");
        }
    }
}
