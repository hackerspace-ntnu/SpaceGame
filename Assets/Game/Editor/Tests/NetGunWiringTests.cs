// The net gun's authored values, read back off the prefab on disk.
//
// Every failure this guards is SILENT. None of them throws, logs, or shows up in an inspector as
// anything other than a plausible number:
//
//   maxUses left at its -1 default is not "no limit set yet", it is infinite nets AND a dead
//   recharge — NetGunArtifact.TickRecharge returns immediately while ChargesLeft is -1.
//   holdSize is the size AFTER EquipItemSocket rescales the mesh, so copying the model's own
//   0.629 m from Blender puts a gun in the hand at half the size of every other gun.
//   A muzzle transform carrying the marker's imported rotation aims out of the gun's own top,
//   because the FBX axis conversion turns Blender's up into the marker's forward.
//   A null netMaterial or loadedBundle is indistinguishable from "the feature is broken".
//
// Same reasoning as PortalGunWiringTests, which is worth reading first: a prefab that holds
// references it cannot work without needs the suite to say so, not a playtest.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class NetGunWiringTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/NetGun.prefab";
        private const string CordMaterialPath = "Assets/Game/Art/Materials/Items/Net_Cord.mat";

        /// <summary>The Gun bracket of ItemScaleLadder, shared with Gun.prefab, PortalGun and GravelBlaster.</summary>
        private const float GunBracket = 1.25f;

        private static GameObject LoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"prefab missing from the project: {PrefabPath}. " +
                                     "Run Tools/Items/Build Net Gun.");
            return prefab;
        }

        private static SerializedObject LoadArtifact()
        {
            var artifact = LoadPrefab().GetComponent<NetGunArtifact>();
            Assert.IsNotNull(artifact, $"{PrefabPath} has no NetGunArtifact on its root");
            return new SerializedObject(artifact);
        }

        [Test]
        public void TheGunCarriesThreeNets()
        {
            int maxUses = LoadArtifact().FindProperty("maxUses").intValue;

            Assert.AreEqual(3, maxUses,
                "the net gun's maxUses is " + maxUses + ". UsableItem defaults it to -1, which means " +
                "UNLIMITED — and an unlimited gun reports ChargesLeft == -1, which is the first thing " +
                "TickRecharge returns on. The default is therefore infinite nets with a dead recharge " +
                "clock, not three nets.");
        }

        [Test]
        public void TheGunsOwnPartsAreWired()
        {
            SerializedObject so = LoadArtifact();

            Assert.IsNotNull(so.FindProperty("muzzle").objectReferenceValue,
                "muzzle is unset, so every shot leaves from the prefab root instead of the bore.");
            Assert.IsNotNull(so.FindProperty("loadedBundle").objectReferenceValue,
                "loadedBundle is unset, so a spent gun still shows a net crammed in its canister.");
            Assert.IsNotNull(so.FindProperty("netMaterial").objectReferenceValue,
                "netMaterial is unset, so every net in the air draws with the default magenta.");
        }

        /// <summary>
        /// NetGunArtifact.OnRequestUse sends <c>muzzle.rotation</c> as the aim, so the muzzle's
        /// forward IS the shot. The imported marker's own rotation is not it.
        /// </summary>
        [Test]
        public void TheMuzzleAimsDownTheBore()
        {
            var muzzle = LoadArtifact().FindProperty("muzzle").objectReferenceValue as Transform;
            Assert.IsNotNull(muzzle, "muzzle is unset");

            Vector3 aim = muzzle.localRotation * Vector3.forward;

            Assert.Greater(Vector3.Dot(aim, Vector3.forward), 0.99f,
                $"the muzzle aims {aim} in prefab space rather than down the gun's +Z. A net fired " +
                "from it leaves sideways or straight up.");
        }

        [Test]
        public void TheGunIsHeldAtTheGunBracket()
        {
            var grip = LoadPrefab().GetComponent<ItemGrip>();
            Assert.IsNotNull(grip, $"{PrefabPath} has no ItemGrip on its root");

            Assert.AreEqual(GunBracket, grip.HoldSize, 1e-3f,
                "holdSize is the longest axis IN THE HAND, after EquipItemSocket rescales the mesh — " +
                "not the size the model was built at. See ItemScaleLadder for the bracket table.");
            Assert.AreEqual(grip.HoldSize, grip.PackSize, 1e-3f,
                "packSize is authored, so the gun lies on the pack mat at a size nobody asked for. " +
                "It should stay 0 and fall back to holdSize: guns sit at the anchor on the mat too, " +
                "because big gear goes on the rack with overhang.");
        }

        /// <summary>
        /// The player sees the underside of a net every time one drapes over something, so the cord
        /// cannot be back-face culled.
        /// </summary>
        [Test]
        public void TheCordMaterialIsTwoSided()
        {
            var cord = AssetDatabase.LoadAssetAtPath<Material>(CordMaterialPath);
            Assert.IsNotNull(cord, $"material missing from the project: {CordMaterialPath}");

            Assert.AreEqual((float)UnityEngine.Rendering.CullMode.Off, cord.GetFloat("_Cull"), 1e-3f,
                "Net_Cord culls back faces, so a draped net vanishes when seen from below.");
            Assert.IsNotNull(cord.GetTexture("_BaseMap"), "Net_Cord has no base map");
            Assert.IsNotNull(cord.GetTexture("_BumpMap"), "Net_Cord has no normal map");
        }
    }
}
