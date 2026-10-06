// NPC worn gear: the same three slots and rules as the player's body, the wing pack worn FOLDED on the
// spine (D3, NPCs have no lash rail), gauntlets strapped to the forearms, and the pack put away while
// its wearer rides a carrier that stows it.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
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
