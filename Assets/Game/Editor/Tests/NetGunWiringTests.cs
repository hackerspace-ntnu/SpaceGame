// The net gun's authored values, read back off the prefab on disk.
//
// Every failure this guards is SILENT. None of them throws, logs, or shows up in an inspector as
// anything other than a plausible number:
//
//   maxUses left at its -1 default is not "no limit set yet", it is infinite nets AND a dead
//   recharge — NetGunArtifact.TickRecharge returns immediately while ChargesLeft is -1.
//   holdSize is the size AFTER EquipItemSocket rescales the mesh, so copying the model's own
//   0.629 m from Blender puts a gun in the hand at half the size of every other gun.
//   packSize left at 0 is not "no second size needed", it is the hand's bracket reaching the mat,
//   the gear wall and the sand — the 0.629 m gun drawn 1.31 m long in all three.
//   A muzzle transform carrying the marker's imported rotation aims out of the gun's own top,
//   because the FBX axis conversion turns Blender's up into the marker's forward.
//   A null netMaterial or loadedBundle is indistinguishable from "the feature is broken".
//
// Same reasoning as PortalGunWiringTests, which is worth reading first: a prefab that holds
// references it cannot work without needs the suite to say so, not a playtest.
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class NetGunWiringTests
    {
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/NetGun.prefab";
        private const string CordMaterialPath = "Assets/Game/Art/Materials/Items/Net_Cord.mat";

        /// <summary>The player prefab that is actually spawned into a session — the one with an identity.</summary>
        private const string NetworkedPlayerPrefabPath =
            "Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab";

        /// <summary>The Gun size bracket, shared with Gun.prefab, PortalGun and GravelBlaster.</summary>
        private const float GunBracket = 1.25f;

        /// <summary>
        /// What the gun measures everywhere that is not the hand — the mat, the ship's gear wall
        /// and the sand. The true 0.629 m model rounded up to the next 0.09 m webbing pitch; on
        /// the mat that is 4 x 7 = 28 cells. See the prefab's packSize for why the roster's extra
        /// cell of margin is left off.
        /// </summary>
        private const float StowedSize = 0.63f;

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
        public void TheGunIsHeldAtTheBracketAndStowedAtItsTrueSize()
        {
            var grip = LoadPrefab().GetComponent<ItemGrip>();
            Assert.IsNotNull(grip, $"{PrefabPath} has no ItemGrip on its root");

            Assert.AreEqual(GunBracket, grip.HoldSize, 1e-3f,
                "holdSize is the longest axis IN THE HAND, after EquipItemSocket rescales the mesh — " +
                "not the size the model was built at. See the size bracket table.");
            Assert.AreEqual(StowedSize, grip.PackSize, 1e-3f,
                "packSize is back at the hand's bracket, so the gun is stowed and dropped at " +
                "1.25 m — 7 x 14 = 98 of the rig's 255 cells for one pistol, and a 2.39 m gun " +
                "lying in the sand. It carries its own true-metre size; see " +
                "the prefab's packSize and the row in PackSizeTests.");
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

        /// <summary>
        /// The tag, the Rigidbody and the NetworkObject are on ONE GameObject — and the whole
        /// capture family breaks on clients if anybody separates them.
        ///
        /// <para>
        /// Three pieces of code pick a player out of the world by three different routes, and every
        /// one of them assumes those routes land on the same object.
        /// <c>SnareReceiver.ResolveLandedNets</c> takes <c>hit.attachedRigidbody.gameObject</c> —
        /// the RIGIDBODY's object. <c>SnareCatch.Capture</c> then asks that object for the PLAYER
        /// tag and records it as the captive. <c>NetArg.Resolve</c> on every other machine hands
        /// back the NETWORKOBJECT's own <c>gameObject</c>, because that is what a NetworkObjectId
        /// resolves to.
        /// </para>
        /// <para>
        /// Move the collider, the body or the tag onto a child and each of those still returns
        /// something perfectly reasonable — just not the same something. The capture is recorded
        /// against one object and announced as another, so <c>NetMsg.Snared</c> holds nobody on a
        /// peer and <c>NetMsg.SnareStruggled</c> finds no net for a captive who is visibly
        /// struggling. Both fail on CLIENTS ONLY and in silence: the host records and resolves
        /// against its own local reference (<c>NetArg.localTarget</c> never leaves the machine), so
        /// everything works perfectly for whoever is hosting and for nobody else.
        /// </para>
        /// <para>
        /// Asserted as identities rather than as three presence checks, because three components
        /// each existing somewhere in the hierarchy is exactly the state this is meant to reject.
        /// On the networked prefab and not the base one: <c>PlayerCharacter</c> deliberately has no
        /// NetworkObject at all, and it is the wrapper that gets spawned into a session.
        /// </para>
        /// </summary>
        [Test]
        public void ThePlayerTagRigidbodyAndNetworkObjectShareOneGameObject()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkedPlayerPrefabPath);
            Assert.IsNotNull(player, $"prefab missing from the project: {NetworkedPlayerPrefabPath}");

            var identity = player.GetComponentInChildren<NetworkObject>(true);
            Assert.IsNotNull(identity,
                $"{NetworkedPlayerPrefabPath} has no NetworkObject anywhere. It is the prefab that " +
                "gets spawned into a session, so without one no message can name a player at all.");

            var body = player.GetComponentInChildren<Rigidbody>(true);
            Assert.IsNotNull(body,
                $"{NetworkedPlayerPrefabPath} has no Rigidbody, so a net’s capture query has " +
                "nothing to attach its collider hits to.");

            Assert.AreSame(identity.gameObject, body.gameObject,
                "The Rigidbody and the NetworkObject are on different objects. A net records the " +
                "Rigidbody’s object as its captive and announces the NetworkObject’s id, " +
                "so every peer resolves a body the shooter never caught — and the host, which " +
                "keeps its own local reference, sees nothing wrong.");

            Assert.IsTrue(identity.gameObject.CompareTag("Player"),
                $"The NetworkObject sits on '{identity.gameObject.name}', which is not tagged " +
                "Player. SnareCatch.Capture refuses anything that is neither tagged nor an " +
                "AgentController, so a peer resolving this body would put a SnareTether on a " +
                "player, or refuse the capture outright.");
        }
    }
}
