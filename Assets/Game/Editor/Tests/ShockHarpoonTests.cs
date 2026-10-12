// The Shock Harpoon and the Shocked condition it leaves behind.
//
// Every failure guarded here is silent: an unwired loaded harpoon fires nothing with a clean
// console, an unregistered prefab fails on clients only, and a shock that could be refreshed or
// chained would let two harpoons keep somebody on the floor for good without any error at all.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay.Status;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class ShockHarpoonTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/ShockHarpoon.prefab";
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/ShockHarpoon.asset";
        private const string RaxyPath = "Assets/Game/Prefabs/agents/Characters/Raxy/Drifter_Raxy 2.prefab";

        /// <summary>WCAG 2.3.1: no more than three flashes in any one second.</summary>
        private const float MaxFlashesPerSecond = 3f;

        private static SerializedObject LoadArtifact()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"{PrefabPath} is missing. Run Tools/SpaceGame/Items/Build Shock Harpoon.");

            var artifact = prefab.GetComponent<ShockHarpoonArtifact>();
            Assert.IsNotNull(artifact, $"{PrefabPath} has no ShockHarpoonArtifact on its root");
            return new SerializedObject(artifact);
        }

        [Test]
        public void TheGunsOwnPartsAreWired()
        {
            SerializedObject so = LoadArtifact();

            foreach (string field in new[] { "loadedHarpoon", "muzzle", "wireLine", "arcPrefab" })
                Assert.IsNotNull(so.FindProperty(field).objectReferenceValue,
                                 $"{field} is unset, so the harpoon fires nothing and nothing says why.");

            // In world metres: the FBX node is scaled, so the ends are tiny in the part's own space.
            var harpoon = (Transform)so.FindProperty("loadedHarpoon").objectReferenceValue;
            Vector3 tip = harpoon.TransformPoint(so.FindProperty("harpoonTip").vector3Value);
            Vector3 tail = harpoon.TransformPoint(so.FindProperty("harpoonTail").vector3Value);
            Assert.Greater(Vector3.Distance(tip, tail), 0.3f,
                           "the harpoon measures under 30 cm tip to tail, so its ends were read off the wrong axis.");
        }

        [Test]
        public void TheWireLeavesTheGunAheadOfTheLoadedHarpoonsTail()
        {
            SerializedObject so = LoadArtifact();
            var harpoon = (Transform)so.FindProperty("loadedHarpoon").objectReferenceValue;
            var muzzle = (Transform)so.FindProperty("muzzle").objectReferenceValue;
            Vector3 tail = harpoon.TransformPoint(so.FindProperty("harpoonTail").vector3Value);
            Vector3 tip = harpoon.TransformPoint(so.FindProperty("harpoonTip").vector3Value);

            Assert.Greater(Vector3.Dot(tip - tail, Vector3.forward), 0f,
                           "the harpoon's tip points back into the gun: the dart would fly tail first.");
            Assert.Greater(muzzle.position.z, tail.z,
                           "the muzzle sits behind the harpoon's tail, so the wire runs out of the stock.");
        }

        [Test]
        public void TheItemIsRegisteredAndReachable()
        {
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            Assert.IsNotNull(item, $"{ItemPath} is missing: an item outside Resources/Items never registers.");
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), item.itemPrefab);
            Assert.IsTrue(NetworkPrefabRegistrar.IsRegistered(PrefabPath),
                          "not in the network prefab list: dropping it fails on clients only.");
        }

        [Test]
        public void AHitBodyStaysDownFiveSecondsAndTheHarpoonIsHomeBeforeTheNextShot()
        {
            SerializedObject so = LoadArtifact();
            float shock = so.FindProperty("shockSeconds").floatValue;
            float reload = so.FindProperty("reloadSeconds").floatValue;
            float range = so.FindProperty("range").floatValue;
            float worstTrip = range / so.FindProperty("flightSpeed").floatValue
                            + range / so.FindProperty("reelSpeed").floatValue;

            Assert.AreEqual(5f, shock, 0.001f,
                            "the harpoon must name its 5 s: the Shocked default is serialized per body and can be stale");
            Assert.Greater(reload, shock + worstTrip,
                           "the reload ends before a full-range shot, shock and reel are over");
        }

        [Test]
        public void TheWireIsDrawnAsACableAndCarriesCurrent()
        {
            SerializedObject so = LoadArtifact();
            var wire = (LineRenderer)so.FindProperty("wireLine").objectReferenceValue;

            Assert.IsNotNull(wire.GetComponent<WireTube>(), "the wire would draw as a flat ribbon");
            Assert.IsNotNull(wire.GetComponent<MeshRenderer>().sharedMaterial, "the cable has no material");

            var current = (WireCurrent)so.FindProperty("current").objectReferenceValue;
            Assert.IsNotNull(current, "nothing crackles down the wire while the body is shocked");
            Assert.AreEqual(wire, new SerializedObject(current).FindProperty("wire").objectReferenceValue,
                            "the current follows some other line than the cable");
        }

        [Test]
        public void TheShockLookIsShippedAndWired()
        {
            ShockLook look = Resources.Load<ShockLook>(ShockLook.ResourcePath);
            Assert.IsNotNull(look, $"no Resources/{ShockLook.ResourcePath}: a shocked body would show nothing");

            Assert.IsNotNull(look.xrayShell, "no X-ray shell material");
            Assert.AreEqual("SpaceGame/XRayShell", look.xrayShell.shader.name);
            Assert.IsNotNull(look.bone, "no bone material");
            Assert.IsNotNull(look.socket, "no eye-socket material");
            Assert.IsNotNull(look.arc, "no arc for the current to jump along the bones");
        }

        [Test]
        public void TheXRayStrobesUnderThePhotosensitivityLimit()
        {
            ShockLook look = Resources.Load<ShockLook>(ShockLook.ResourcePath);
            float fastestCycle = look.xraySeconds.x + look.normalSeconds.x;

            Assert.LessOrEqual(1f / fastestCycle, MaxFlashesPerSecond,
                               "the full-body X-ray flashes faster than three times a second, and the " +
                               "game has no reduce-flashing setting to turn it down");
        }

        [Test]
        public void ARaxyIsMeasuredWithAHeadAndAChest()
        {
            var raxy = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RaxyPath));
            try
            {
                BodyFrame frame = BodyFrame.Measure(raxy.transform);

                Assert.IsNotNull(frame.Head, "no head found: the skeleton would have no skull");
                Assert.IsNotNull(frame.Chest, "no chest found: the skeleton would have no ribs");
                Assert.Greater(frame.Height, 1f, "a Raxy measured under a metre: the skeleton would be a toy");
                Assert.Greater(BodyFrame.BonesOf(raxy.transform).Count, 10, "too few joints for a skeleton");
            }
            finally
            {
                Object.DestroyImmediate(raxy);
            }
        }

        [Test]
        public void AShockIsHelplessAndCannotBeRefreshedWhileItRuns()
        {
            var shock = new ShockedStatus();

            Assert.AreEqual(StatusKind.Shocked, shock.Kind);
            Assert.IsTrue(shock.Suppresses, "a shocked body must not act");
            Assert.IsTrue(shock.CanApply(null, running: false), "a body never shocked can be shocked");
            Assert.IsFalse(shock.CanApply(null, running: true),
                           "a refresh would let a held harpoon keep its victim down for ever");
        }

        [Test]
        public void ABodyThatJustGotUpCannotBeShockedStraightAgain()
        {
            var shock = new ShockedStatus();
            shock.OnCleared(null);

            Assert.IsFalse(shock.CanApply(null, running: false),
                           "two harpoons taking turns would chain the victim on the floor");
        }
    }
}
