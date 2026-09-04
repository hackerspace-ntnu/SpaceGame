// A display copy is scenery: it must not be able to tick, collide, own a network identity or run
// a script, and it must keep the prefab's hierarchy so a grip point can be found on it by path.
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class DisplayCopyTests
    {
        private class Ticker : MonoBehaviour { }

        private GameObject prefab;
        private GameObject parent;
        private GameObject copy;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("Item");
            prefab.AddComponent<Rigidbody>();
            prefab.AddComponent<BoxCollider>();
            prefab.AddComponent<Ticker>();
            prefab.AddComponent<NetworkObject>();

            var body = new GameObject("Body");
            body.transform.SetParent(prefab.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            body.AddComponent<MeshRenderer>();

            var grip = new GameObject("Grip");
            grip.transform.SetParent(body.transform, false);
            grip.transform.localPosition = new Vector3(0f, 0.1f, 0f);

            parent = new GameObject("Parent");
            parent.transform.position = new Vector3(5f, 0f, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (copy != null) Object.DestroyImmediate(copy);
            if (prefab != null) Object.DestroyImmediate(prefab);
            if (parent != null) Object.DestroyImmediate(parent);
        }

        [Test]
        public void StripsEverythingThatCouldRun()
        {
            copy = DisplayCopy.Make(prefab, parent.transform);

            Assert.IsNotNull(copy);
            Assert.AreEqual(0, copy.GetComponentsInChildren<Rigidbody>(true).Length);
            Assert.AreEqual(0, copy.GetComponentsInChildren<Collider>(true).Length);
            Assert.AreEqual(0, copy.GetComponentsInChildren<MonoBehaviour>(true).Length,
                "no script may survive — NetworkObject and the Ticker are both MonoBehaviours");
        }

        [Test]
        public void KeepsTheHierarchyAndTheRenderers()
        {
            copy = DisplayCopy.Make(prefab, parent.transform);

            Transform grip = copy.transform.Find("Body/Grip");
            Assert.IsNotNull(grip, "Strip removes components, never GameObjects");
            Assert.AreEqual(new Vector3(0f, 0.1f, 0f), grip.localPosition);
            Assert.AreEqual(1, copy.GetComponentsInChildren<MeshRenderer>(true).Length);
        }

        [Test]
        public void SitsUnderTheParentAtIdentity()
        {
            copy = DisplayCopy.Make(prefab, parent.transform);

            Assert.AreEqual(parent.transform, copy.transform.parent);
            Assert.AreEqual(Vector3.zero, copy.transform.localPosition);
            Assert.AreEqual(Quaternion.identity, copy.transform.localRotation);
            Assert.AreEqual(Vector3.one, copy.transform.localScale);
        }

        [Test]
        public void LeavesNoStageBehind()
        {
            copy = DisplayCopy.Make(prefab, parent.transform);

            // Not GameObject.Find: it skips inactive objects, and the stage is deactivated — so
            // that check would pass whether or not the stage leaked, which is the whole failure
            // this test exists to catch. FindObjectsInactive.Include is what actually asks.
            //
            // Scene objects only, deliberately: Resources.FindObjectsOfTypeAll would sweep every
            // loaded prefab asset too, which costs a great deal and cannot add anything — the
            // stage is only ever a scene object. NetworkBootstrap draws the same distinction.
            GameObject leaked = System.Array.Find(
                Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                go => go.name == "DisplayCopyStage");

            Assert.IsNull(leaked, "the staging object must not outlive Make");
        }
    }
}
