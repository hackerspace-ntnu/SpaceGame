// NPC worn gear: the same three slots and rules as the player's body, the wing pack worn FOLDED on the
// spine (D3, NPCs have no lash rail), gauntlets strapped to the forearms, and the pack put away while
// its wearer rides a carrier that stows it.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    public class EntityBodyEquipmentTests
    {
        internal const string WingPackPath = "Assets/Game/Resources/Items/Artifacts/WingPack.asset";
        internal const string RepulsorPath = "Assets/Game/Resources/Items/Artifacts/RepulsorGauntlet.asset";
        internal const string GunPath = "Assets/Game/Resources/Items/Artifacts/basicgun.asset";

        // A stand-in carrier that puts its rider's torso gear away (NpcAviator implements the interface
        // for real, Task 8). NESTED, not in a file of its own: a top-level MonoBehaviour in an editor
        // assembly has a MonoScript that marks it an editor script, and AddComponent then returns null.
        private sealed class TestTorsoStower : MonoBehaviour, IStowsTorsoGear { }

        private readonly List<Object> junk = new();
        private GameObject npc;
        private EntityBodyEquipment body;

        [SetUp]
        public void SetUp()
        {
            npc = Npc(junk);
            body = npc.GetComponent<EntityBodyEquipment>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        internal static T Asset<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, path);
            return asset;
        }

        /// <summary>A body with the bone names BoneResolver falls back to (no Animator), and worn gear.</summary>
        internal static GameObject Npc(List<Object> junk)
        {
            var go = new GameObject("Npc");
            junk.Add(go);
            Transform Bone(string name, Transform parent, Vector3 local)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent, false);
                t.localPosition = local;
                return t;
            }
            Transform hips = Bone("Hips", go.transform, new Vector3(0f, 1.5f, 0f));
            Transform spine = Bone("Spine", hips, new Vector3(0f, 0.3f, 0f));
            Transform leftArm = Bone("LeftForeArm", spine, new Vector3(-0.6f, 0.4f, 0f));
            Bone("LeftHand", leftArm, new Vector3(-0.35f, 0f, 0f));
            Transform rightArm = Bone("RightForeArm", spine, new Vector3(0.6f, 0.4f, 0f));
            Bone("RightHand", rightArm, new Vector3(0.35f, 0f, 0f));
            go.AddComponent<EntityBodyEquipment>();
            return go;
        }

        // EditMode AddComponent runs no Start; invoke it the way Unity would.
        private void StartBody() =>
            typeof(EntityBodyEquipment).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(body, null);

        private void StartWearing(string torsoItemPath)
        {
            var so = new SerializedObject(body);
            so.FindProperty("startingWorn").GetArrayElementAtIndex(0).objectReferenceValue = Asset<InventoryItem>(torsoItemPath);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject Stower(List<Object> junk)
        {
            var carrier = new GameObject("Craft");
            junk.Add(carrier);
            carrier.AddComponent<TestTorsoStower>();
            return carrier;
        }

        [Test]
        public void TheWingPack_GoesInTheTorso_OnTheSpine_Folded()
        {
            var pack = Asset<InventoryItem>(WingPackPath);
            Assert.IsTrue(body.TryWear(pack));

            Assert.AreEqual(pack, body.ItemIn(BodySlot.Torso));
            GameObject worn = body.InstanceIn(BodySlot.Torso);
            Assert.IsNotNull(worn);
            Assert.AreEqual("Spine", worn.transform.parent.name);
            Transform stowedWings = WornVisual.Of(worn);
            Assert.IsTrue(stowedWings == null || !stowedWings.gameObject.activeSelf,
                          "an NPC wears the folded bundle, not the stowed wings (D3)");

            var fit = pack.itemPrefab.GetComponent<WornFit>();
            Assert.IsTrue(fit.HasFoldedPose, "WingPack.prefab carries no folded pose");
            Assert.That(Vector3.Distance(worn.transform.localPosition, fit.FoldedLocalPosition), Is.LessThan(1e-3f),
                        "the folded pack is not at its folded offset from the spine");
            // Measure reads the item root's own space, before its scale: the drawn size is that times the scale.
            Bounds bounds = ItemBounds.Measure(worn, null);
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * worn.transform.lossyScale.x;
            Assert.AreEqual(fit.FoldedSize, longest, 0.02f, "the folded pack is not drawn at its folded size");
        }

        private const string SkyPeople = "Assets/Game/Prefabs/Agents/Characters/SkyTribe/";

        // Anything wholly further behind the spine than this, in the body's root space, hangs on its back.
        private const float BehindTheSpine = -0.05f;

        /// <summary>
        /// The Sky people's own baked back gear (pack, pouches, scarves) used to swallow most of the folded
        /// wing pack: on the REAL prefabs, the pack's middle must lie behind the deepest of that gear.
        /// </summary>
        [TestCase("SkyNomad_Tan")]
        [TestCase("SkyNomad_Umber")]
        [TestCase("SkyNomad_Maroon")]
        [TestCase("SkyNomad_StrawHat")]
        [TestCase("SkySoldier")]
        public void TheFoldedWingPack_SitsOutsideTheWearersBakedBackGear(string person)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            var mesh = new Mesh();
            try
            {
                var prefab = Asset<GameObject>(SkyPeople + person + ".prefab");
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                var wearer = root.GetComponent<EntityBodyEquipment>();
                typeof(EntityBodyEquipment).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(wearer, null);
                GameObject pack = wearer.InstanceIn(BodySlot.Torso);
                Assume.That(pack, Is.Not.Null, $"{person} does not start wearing the wing pack");

                float packZ = 0f;
                int packVertices = 0;
                float deepestGear = 0f;
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    List<Vector3> vertices = RootSpaceVertices(r, root.transform, mesh);
                    if (r.transform.IsChildOf(pack.transform))
                    {
                        foreach (Vector3 v in vertices) packZ += v.z;
                        packVertices += vertices.Count;
                    }
                    else if (vertices.Count > 0 && vertices.TrueForAll(v => v.z < BehindTheSpine))
                    {
                        foreach (Vector3 v in vertices) deepestGear = Mathf.Min(deepestGear, v.z);
                    }
                }

                Assume.That(packVertices, Is.GreaterThan(0), "the folded pack draws nothing");
                Assume.That(deepestGear, Is.LessThan(BehindTheSpine), $"{person} wears no back gear to measure against");
                Assert.Less(packZ / packVertices, deepestGear,
                            $"{person}'s folded wing pack sits inside its own back gear: move WornFit.foldedLocalPosition back");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private const string PlayerPrefab = "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";

        /// <summary>
        /// A Sky person's folded pack is drawn as big, in the world, as the player's own worn pack: at
        /// 1.26 m it read as a toy on the back of a man who flies it. Both sides are measured on the real
        /// prefabs through their own seating paths, so a rescaled body or a retuned fit on either breaks it.
        /// </summary>
        [TestCase("SkyNomad_Tan")]
        [TestCase("SkyNomad_StrawHat")]
        [TestCase("SkySoldier")]
        public void TheFoldedWingPack_IsAsBigAsThePlayersWornOne(string person)
        {
            float players = WorldSpan(PlayerWornPack);
            float npcs = WorldSpan(root =>
            {
                var wearer = root.GetComponent<EntityBodyEquipment>();
                typeof(EntityBodyEquipment).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(wearer, null);
                return wearer.InstanceIn(BodySlot.Torso);
            }, SkyPeople + person + ".prefab");

            Assert.AreEqual(players, npcs, 0.1f * players,
                            $"{person}'s folded pack is {npcs:F2} m against the player's worn {players:F2} m: retune WornFit.foldedSize");
        }

        /// <summary>The player's worn pack, seated the way <c>BodyEquipmentController.WearOnTorso</c> seats it.</summary>
        private static GameObject PlayerWornPack(GameObject player)
        {
            Transform spine = BoneResolver.Resolve(player.GetComponentInChildren<Animator>(true), player.transform,
                                                   HumanBodyBones.Spine, WornBones.BackHints);
            GameObject pack = Object.Instantiate(Asset<InventoryItem>(WingPackPath).itemPrefab, spine);
            EquipItemSocket.Sanitize(pack);
            WornSeat.Apply(pack, spine, pack.GetComponent<WornFit>());
            return pack;
        }

        /// <summary>The longest world-space axis of the pack <paramref name="wear"/> puts on a fresh copy of a prefab.</summary>
        private static float WorldSpan(System.Func<GameObject, GameObject> wear, string prefabPath = PlayerPrefab)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(Asset<GameObject>(prefabPath), preview);
                GameObject pack = wear(root);
                Assume.That(pack, Is.Not.Null, $"{prefabPath} wears no wing pack");
                Bounds bounds = ItemBounds.Measure(pack, null);
                return Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * pack.transform.lossyScale.x;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static List<Vector3> RootSpaceVertices(Renderer renderer, Transform root, Mesh scratch)
        {
            var vertices = new List<Vector3>();
            if (renderer is SkinnedMeshRenderer skinned) skinned.BakeMesh(scratch, true);
            else if (renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) scratch = filter.sharedMesh;
            else return vertices;

            Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            foreach (Vector3 v in scratch.vertices) vertices.Add(toRoot.MultiplyPoint3x4(v));
            return vertices;
        }

        [Test]
        public void AHandItem_IsNeverWorn()
        {
            Assert.IsFalse(body.TryWear(Asset<InventoryItem>(GunPath)));
            for (int i = 0; i < 3; i++) Assert.IsNull(body.ItemIn((BodySlot)i));
        }

        [Test]
        public void ASecondTorsoItem_IsRefused_WhileTheFirstIsWorn()
        {
            var pack = Asset<InventoryItem>(WingPackPath);
            Assert.IsTrue(body.TryWear(pack));
            Assert.IsFalse(body.TryWear(pack));
        }

        [Test]
        public void AGauntlet_GoesOnTheFirstFreeForearm()
        {
            Assert.IsTrue(body.TryWear(Asset<InventoryItem>(RepulsorPath)));
            Assert.AreEqual("LeftForeArm", body.InstanceIn(BodySlot.LeftGauntlet).transform.parent.name);
            Assert.IsTrue(body.TryWear(Asset<InventoryItem>(RepulsorPath)));
            Assert.AreEqual("RightForeArm", body.InstanceIn(BodySlot.RightGauntlet).transform.parent.name);
        }

        [Test]
        public void Remove_HandsTheItemBack_AndTakesTheVisualOff()
        {
            var pack = Asset<InventoryItem>(WingPackPath);
            body.TryWear(pack);
            Assert.AreEqual(pack, body.Remove(BodySlot.Torso));
            Assert.IsNull(body.ItemIn(BodySlot.Torso));
            Assert.IsNull(body.InstanceIn(BodySlot.Torso));
        }

        [Test]
        public void RidingACarrierThatStowsIt_HidesThePack_AndGettingOffShowsIt()
        {
            body.TryWear(Asset<InventoryItem>(WingPackPath));
            GameObject carrier = Stower(junk);

            npc.transform.SetParent(carrier.transform, true);
            body.RefreshStowed();
            Assert.IsFalse(body.TorsoShown);
            foreach (Renderer r in body.InstanceIn(BodySlot.Torso).GetComponentsInChildren<Renderer>(true))
                Assert.IsFalse(r.enabled, "the folded pack still shows while the craft it IS is deployed");

            npc.transform.SetParent(null, true);
            body.RefreshStowed();
            Assert.IsTrue(body.TorsoShown);
        }

        [Test]
        public void ABodyAlreadyUnderACarrierThatStowsIt_WakesWithThePackHidden()
        {
            npc.transform.SetParent(Stower(junk).transform, true);
            StartWearing(WingPackPath);

            StartBody();   // the parenting came first: no parent change is left to cue the stow

            Assert.IsFalse(body.TorsoShown);
            foreach (Renderer r in body.InstanceIn(BodySlot.Torso).GetComponentsInChildren<Renderer>(true))
                Assert.IsFalse(r.enabled, "a nomad parented under its craft before waking shows its pack while flying");
        }

        [Test]
        public void StartingGear_IsWornOffline_AndARestoredEmptyBodyStaysEmpty()
        {
            StartWearing(WingPackPath);

            body.RestoreWorn(new InventoryItem[] { null, null, null });   // a save says "nothing"
            StartBody();

            Assert.IsNull(body.ItemIn(BodySlot.Torso), "the prefab's starting pack overrode a save that said empty");
        }

        [Test]
        public void StartingGear_IsWornOffline_WhenNothingWasRestored()
        {
            StartWearing(WingPackPath);

            StartBody();

            Assert.IsNotNull(body.ItemIn(BodySlot.Torso));
        }
    }
}
