// Why MountModule.OnDisable branches on activeInHierarchy before dismounting.
//
// Dismounting reparents the rider, and Unity refuses Transform.SetParent while the parent GameObject
// is mid-activation-change. Returning to the main menu hits that: PauseMenuUI shuts the
// NetworkManager down (which deliberately skips deparenting children — NetworkSpawnManager
// .OnDespawnObject) and then loads MainMenu single, so the rider is still parented, no longer
// spawned, and the world scene tears down underneath it. OnDisable then tried a raw SetParent and
// logged "Cannot set the parent of the GameObject 'PlayerCharacterNetworked(Clone)' while activating
// or deactivating the parent GameObject 'Ostrich'".
//
// These pin the platform behaviour the fix reads, not MountModule itself: Unity does not deliver
// OnEnable/OnDisable to a plain MonoBehaviour in edit mode, so MountModule.OnDisable is unreachable
// from an EditMode test. The branch itself was verified in play mode — case A (mount deactivated)
// leaves the rider parented and logs nothing, case B (module switched off) still dismounts.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp.
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceGame.EditorTools
{
    public class MountTeardownTests
    {
        private GameObject parent;
        private GameObject child;

        [TearDown]
        public void TearDown()
        {
            if (parent != null) Object.DestroyImmediate(parent);
            if (child != null) Object.DestroyImmediate(child);
        }

        /// A parent that tries to push its child out to the root the moment it is disabled — the
        /// shape of MountModule.OnDisable calling Dismount.
        [ExecuteAlways]
        private class DetachOnDisable : MonoBehaviour
        {
            public Transform Child;
            public bool SawActiveInHierarchy;

            private void OnDisable()
            {
                SawActiveInHierarchy = gameObject.activeInHierarchy;
                if (Child != null) Child.SetParent(null, true);
            }
        }

        private DetachOnDisable Build()
        {
            parent = new GameObject("Mount");
            child = new GameObject("Rider");
            child.transform.SetParent(parent.transform, true);

            var detach = parent.AddComponent<DetachOnDisable>();
            detach.Child = child.transform;
            return detach;
        }

        [Test]
        public void DeactivatingTheMount_MakesTheReparentIllegal()
        {
            DetachOnDisable detach = Build();

            LogAssert.Expect(LogType.Error, new Regex(
                "Cannot set the parent of the GameObject .* while activating or deactivating"));

            parent.SetActive(false);

            Assert.IsFalse(detach.SawActiveInHierarchy,
                "activeInHierarchy is already false inside OnDisable — that is the flag " +
                "MountModule reads to tell teardown from a plain component disable.");
            Assert.AreSame(parent.transform, child.transform.parent,
                "Unity refused the reparent, so a Dismount here achieves nothing but the error. " +
                "This is the case MountModule.OnDisable must route to AbandonRider.");
        }

        [Test]
        public void DisablingOnlyTheComponent_LeavesTheReparentLegal()
        {
            DetachOnDisable detach = Build();

            detach.enabled = false;

            Assert.IsTrue(detach.SawActiveInHierarchy,
                "The GameObject is untouched when only the component is switched off.");
            Assert.IsNull(child.transform.parent,
                "The reparent succeeds here, so this is the case that must still run a real " +
                "Dismount and put the rider back on their feet.");
        }
    }
}
