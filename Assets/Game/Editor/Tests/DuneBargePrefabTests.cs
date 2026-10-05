// The built dune-barge prefabs, read back from disk, one case per variant. Each check is a way a rebuild
// has silently shipped something broken on another vehicle: a NetworkObject with hash 0 (NGO drops it),
// no save id (the barge vanishes on load), marker empties left as plain empties (ladders that do nothing,
// collision meshes drawn in the world), doors that are meshes rather than parts, hatches the player
// cannot use, a reveal with nothing to reveal.
using System.Linq;
using NUnit.Framework;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools
{
    public class DuneBargePrefabTests
    {
        private static readonly string[] Prefabs = DuneBargeBuilder.Variants.Select(v => v.Prefab).ToArray();
        private const string PlayerPrefab = "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";
        private const float SetDownLift = 0.1f;     // m: the marks sit a few cm over their floor; so does a set-down body

        private static GameObject Load(string prefab)
        {
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(DuneBargeBuilder.PrefabPath(prefab));
            if (go == null) Assert.Ignore($"{prefab} not built yet.");
            return go;
        }

        [TestCaseSource(nameof(Prefabs))]
        public void IsNetworkedAndSaveable(string prefab)
        {
            GameObject go = Load(prefab);
            NetworkObject net = go.GetComponent<NetworkObject>();
            Assert.IsNotNull(net, "no NetworkObject on the root");
            Assert.AreNotEqual(0u, net.PrefabIdHash, "GlobalObjectIdHash is 0 - NGO will drop the prefab");
            Assert.IsTrue(net.DontDestroyWithOwner, "a driver disconnecting would delete the barge");
            SaveableEntity save = go.GetComponent<SaveableEntity>();
            Assert.IsNotNull(save, "no SaveableEntity on the root");
            Assert.IsFalse(string.IsNullOrEmpty(save.PrefabId), "no prefabId stamp - the barge vanishes on load");
        }

        [TestCaseSource(nameof(Prefabs))]
        public void EveryMarkerBecameItsComponent(string prefab)
        {
            GameObject go = Load(prefab);
            Assert.AreEqual(4, go.GetComponentsInChildren<Ladder>(true).Length,
                            "four ladders in every variant: two onto the fenders, two onto the roof (the hold's is the crawl's)");
            Assert.IsEmpty(go.GetComponentsInChildren<Renderer>(true)
                             .Where(r => r.name.StartsWith(DuneBargeBuilder.CollisionPrefix)).Select(r => r.name),
                           "collision islands left as drawn meshes");
            Assert.Greater(go.GetComponentsInChildren<MeshCollider>(true).Count(mc => mc.convex && mc.sharedMesh != null), 0);
        }

        [TestCaseSource(nameof(Prefabs))]
        public void DoorsLidsAndHatchesCanBeUsed(string prefab)
        {
            GameObject go = Load(prefab);
            Transform[] hinges = go.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith(DuneBargeBuilder.DoorBonePrefix) || t.name.StartsWith(DuneBargeBuilder.LidBonePrefix)
                            || t.name.StartsWith(DuneBargeBuilder.HatchBonePrefix)).ToArray();
            Assert.GreaterOrEqual(hinges.Length, 4, "two doors, two side hatches at least");
            foreach (Transform hinge in hinges)
            {
                Assert.IsNotNull(hinge.GetComponent<ArticulatedPart>(), $"{hinge.name} is not articulated");
                Collider leaf = hinge.GetComponentInChildren<Collider>(true);
                Assert.IsNotNull(leaf, $"{hinge.name}'s leaf has no collider - nothing to look at, nothing blocks");
                Assert.IsTrue(leaf.GetComponentInParent<ArticulatedPartInteraction>(true) != null || leaf.GetComponentInParent<HatchPassage>(true) != null,
                              $"{hinge.name}'s leaf is not interactable");     // the Interactor resolves up from the collider
            }
            HatchPassage[] hatches = go.GetComponentsInChildren<HatchPassage>(true);
            Assert.AreEqual(2, hatches.Length, "a crawl hatch in each side");
            Assert.IsTrue(hatches.All(h => h.CanInteract()), "a hatch is missing its lid or crawl marks");
        }

        // Every spot the barge sets the player down - a ladder's step-off, either end of a hatch crawl - has
        // room for the player's standing capsule among the barge's own colliders, lids shut. A spot that does
        // not is where physics shoves the body out sideways: through the hull, or off the fender.
        [TestCaseSource(nameof(Prefabs))]
        public void EverySetDownSpotHasRoomForThePlayer(string prefab)
        {
            CapsuleCollider player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab).GetComponentInChildren<CapsuleCollider>(true);
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(Load(prefab), scene);
                Physics.SyncTransforms();
                var spots = go.GetComponentsInChildren<Ladder>(true).Select(l => (l.name + " exit", l.ExitPoint)).ToList();
                foreach (HatchPassage hatch in go.GetComponentsInChildren<HatchPassage>(true))
                {
                    var so = new SerializedObject(hatch);
                    spots.Add((hatch.name + " outer", ((Transform)so.FindProperty("outer").objectReferenceValue).position));
                    spots.Add((hatch.name + " inner", ((Transform)so.FindProperty("inner").objectReferenceValue).position));
                }
                var hits = new Collider[16];
                float r = player.radius * player.transform.lossyScale.x, h = player.height * player.transform.lossyScale.y;
                var blocked = spots.Select(s =>
                {
                    Vector3 feet = s.Item2 + Vector3.up * SetDownLift;
                    int n = scene.GetPhysicsScene().OverlapCapsule(feet + Vector3.up * r, feet + Vector3.up * (h - r), r, hits,
                                                                   ~0, QueryTriggerInteraction.Ignore);
                    return n == 0 ? null : $"{s.Item1}: {string.Join(", ", hits.Take(n).Select(c => c.name))}";
                }).Where(s => s != null).ToArray();
                Assert.IsEmpty(blocked, "set-down spots inside the barge's colliders");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [TestCaseSource(nameof(Prefabs))]
        public void TheInteriorIsRevealedNotAlwaysDrawn(string prefab)
        {
            GameObject go = Load(prefab);
            var so = new SerializedObject(go.GetComponent<InteriorReveal>());
            Assert.Greater(so.FindProperty("interiorRenderers").arraySize, 0, "nothing to reveal");
            Assert.Greater(so.FindProperty("localVolumes").arraySize, 0, "no rooms");
            Assert.Greater(so.FindProperty("openings").arraySize, 0, "nowhere to look in from");
        }
    }
}
