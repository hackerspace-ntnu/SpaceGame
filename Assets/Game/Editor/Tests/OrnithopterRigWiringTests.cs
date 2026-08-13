// The ornithopter's prefab wiring, asserted.
//
// The flight model has its own tests in SpaceGame.Tests.EditMode, where it belongs — it is pure
// C# and needs no scene. What those cannot see is everything the BUILDER is responsible for: that
// the rig resolves, that the six webbed panels are skinned, that the seat is pitched so the rider
// lies prone, that the wing pack points at the craft. All of that is silent when wrong. A wing
// that does not deform, or a rider standing bolt upright in a prone cradle, looks like an animation
// bug and is a build-time wiring bug.
//
// This lives in Assembly-CSharp-Editor rather than in the tests asmdef because MountModule,
// SteerModule, AgentController, OrnithopterFlightMotor and WingPackItem are all declared in
// Assembly-CSharp, and an asmdef may not reference a predefined assembly.
using NUnit.Framework;
using SpaceGame.Vehicles.Ornithopter;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class OrnithopterRigWiringTests
    {
        private const string CraftPath = "Assets/Game/Prefabs/agents/vehicle/DuneOrnithopter.prefab";
        private const string PackPath = "Assets/Game/Prefabs/items/WingPack.prefab";
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/WingPack.asset";

        private GameObject craft;

        /// Destroy what this made, on the failure path too. A leaked clone sits at the world origin in
        /// the editor's open scene and the next thing measured in that editor is measured against it.
        [TearDown]
        public void TearDown()
        {
            if (craft != null) Object.DestroyImmediate(craft);
            craft = null;

            foreach (GameObject go in Object.FindObjectsByType<GameObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (go != null && go.transform.parent == null && go.name.StartsWith("DuneOrnithopter"))
                    Object.DestroyImmediate(go);
            }
        }

        private static GameObject LoadCraftPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CraftPath);
            Assert.IsNotNull(prefab, $"Ornithopter prefab missing at {CraftPath} — run " +
                                     "Tools ▸ Vehicles ▸ Build Dune Ornithopter Prefab.");
            return prefab;
        }

        [Test]
        public void ThePrefabCarriesTheFlightAndMountRig()
        {
            GameObject prefab = LoadCraftPrefab();

            Assert.IsNotNull(prefab.GetComponent<OrnithopterFlightMotor>(), "No flight motor.");
            Assert.IsNotNull(prefab.GetComponent<MountModule>(), "No MountModule — nothing to ride.");
            Assert.IsNotNull(prefab.GetComponent<SteerModule>(), "No SteerModule — no rider input.");
            Assert.IsNotNull(prefab.GetComponent<AgentController>(), "No AgentController.");
            Assert.IsNotNull(prefab.GetComponent<OrnithopterWingAnimator>(), "No wing animator.");

            Rigidbody rb = prefab.GetComponent<Rigidbody>();
            Assert.IsNotNull(rb, "No Rigidbody.");
            Assert.IsFalse(rb.useGravity,
                "Gravity must be OFF: the flight model integrates weight itself, as one equation with " +
                "lift and drag. Leaving Unity's gravity on as well is a second, uncoordinated pull.");
        }

        [Test]
        public void EveryBoneTheAnimatorNeedsResolves()
        {
            craft = Object.Instantiate(LoadCraftPrefab());

            var rig = new OrnithopterWingRig();
            Assert.DoesNotThrow(() => rig.Build(craft.transform),
                "The rig failed to bind. The FBX must be exported WITH its armature — see " +
                "Assets/Game/Art/Models/_Source~/models/vehicles/dune_ornithopter_export.py.");

            Assert.IsTrue(rig.IsBuilt);
            Assert.IsNotNull(rig.Cradle, "No cradle bone — the rider has nowhere to lie.");
            Assert.IsNotNull(rig.Boom1);
            Assert.IsNotNull(rig.Boom2);
            Assert.IsNotNull(rig.TailHub);

            foreach (OrnithopterWingRig.Wing w in new[] { rig.Left, rig.Right })
            {
                Assert.IsNotNull(w.Shoulder, "Missing shoulder — no flap.");
                Assert.IsNotNull(w.Arm, "Missing arm — no sweep.");
                Assert.IsNotNull(w.Gear);
                Assert.IsNotNull(w.Crank);
                for (int i = 0; i < OrnithopterWingRig.DigitCount; i++)
                    Assert.IsNotNull(w.Digits[i], $"Missing digit {i + 1}.");
            }

            for (int i = 0; i < OrnithopterWingRig.DigitCount; i++)
                Assert.IsNotNull(rig.TailDigits[i], $"Missing tail digit {i + 1}.");
        }

        [Test]
        public void TheTwoWingsCarryOppositeSigns()
        {
            craft = Object.Instantiate(LoadCraftPrefab());
            var rig = new OrnithopterWingRig();
            rig.Build(craft.transform);

            Assert.AreEqual(-rig.Left.Sign, rig.Right.Sign,
                "The wings must carry opposite signs. Local Z is NOT mirrored between them, so a " +
                "shared sign opens one wing while closing the other.");
        }

        [Test]
        public void TheWebbedPanelsAreSkinned()
        {
            GameObject prefab = LoadCraftPrefab();

            string[] panels =
            {
                "Mesh_Wing_L_Frame", "Mesh_Wing_L_Web",
                "Mesh_Wing_R_Frame", "Mesh_Wing_R_Web",
                "Mesh_TailFan_Frame", "Mesh_TailFan_Web",
            };

            var skinned = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (string panel in panels)
            {
                SkinnedMeshRenderer found = System.Array.Find(skinned, s => s.name == panel);
                Assert.IsNotNull(found,
                    $"{panel} is not a SkinnedMeshRenderer. The wing will not deform: the shoulders " +
                    "will flap and the cloth will hang in space behind them.");
                Assert.Greater(found.bones.Length, 0, $"{panel} is skinned but bound to no bones.");
            }
        }

        [Test]
        public void TheSeatLaysTheRiderProne()
        {
            GameObject prefab = LoadCraftPrefab();
            Transform seat = prefab.transform.Find("SEAT_Cradle");
            Assert.IsNotNull(seat, "No SEAT_Cradle.");

            // MountModule parents the rider at identity local rotation, so the seat's orientation IS
            // the rider's. A player capsule stands along its own +Y; laying that down along the
            // craft's forward is a 90 degree pitch about X. Checked as a direction rather than as
            // Euler angles, which have more than one spelling for the same rotation.
            Vector3 riderUp = seat.localRotation * Vector3.up;
            Assert.That(Vector3.Dot(riderUp, Vector3.forward), Is.EqualTo(1f).Within(0.01f),
                $"The rider must lie face-down along the craft's forward axis, but their up axis " +
                $"points {riderUp}. Standing upright in a prone cradle is the failure this catches.");
        }

        [Test]
        public void TheWingPackPointsAtTheCraft()
        {
            var pack = AssetDatabase.LoadAssetAtPath<GameObject>(PackPath);
            Assert.IsNotNull(pack, $"Wing pack missing at {PackPath} — run " +
                                   "Tools ▸ Vehicles ▸ Build Wing Pack Item.");

            var item = pack.GetComponent<WingPackItem>();
            Assert.IsNotNull(item, "The held pack carries no WingPackItem.");

            var so = new SerializedObject(item);
            SerializedProperty craftRef = so.FindProperty("ornithopterPrefab");
            Assert.IsNotNull(craftRef, "WingPackItem has no ornithopterPrefab field.");
            Assert.IsNotNull(craftRef.objectReferenceValue,
                "The wing pack spawns nothing — its ornithopterPrefab is unset.");

            SerializedProperty maxUses = so.FindProperty("maxUses");
            Assert.AreEqual(-1, maxUses.intValue,
                "The pack is equipment, not a consumable: it must have unlimited uses, or bailing out " +
                "mid-air would leave the player with no way down.");
        }

        [Test]
        public void TheInventoryItemResolvesToTheHeldPack()
        {
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            Assert.IsNotNull(item, $"WingPack InventoryItem missing at {ItemPath}.");
            Assert.IsNotNull(item.itemPrefab, "The InventoryItem has no prefab to equip.");
            Assert.IsNotNull(item.itemPrefab.GetComponent<WingPackItem>(),
                "The InventoryItem's prefab is not a wing pack.");
        }

        [Test]
        public void TheCameraFollowsTheMountsPitch()
        {
            GameObject prefab = LoadCraftPrefab();
            var so = new SerializedObject(prefab.GetComponent<MountModule>());

            SerializedProperty follow = so.FindProperty("followMountPitch");
            Assert.IsNotNull(follow, "MountModule has no followMountPitch field.");
            Assert.IsTrue(follow.boolValue,
                "A flying machine must have followMountPitch on, or the camera holds a level horizon " +
                "through a dive and the manoeuvre reads as the ground rising.");

            SerializedProperty direct = so.FindProperty("mountableByDirectInteraction");
            Assert.IsFalse(direct.boolValue,
                "The craft is only ever boarded by using the wing pack, so direct interaction must be " +
                "off — otherwise every hull collider becomes a boarding point.");
        }

        [Test]
        public void TheCameraBoomClearsTheWingspan()
        {
            GameObject prefab = LoadCraftPrefab();
            var so = new SerializedObject(prefab.GetComponent<MountModule>());
            float distance = so.FindProperty("thirdPersonDistance").floatValue;

            // The machine is 10 m across. A boom shorter than the span puts the camera inside the
            // wings, which is the "good distance when in use" this was sized for.
            Assert.Greater(distance, 10f,
                $"The camera boom ({distance:F1} m) is shorter than the wingspan — the camera would " +
                "sit inside the wings.");
        }

        [Test]
        public void SteeringIsWiredForFlight()
        {
            GameObject prefab = LoadCraftPrefab();
            var so = new SerializedObject(prefab.GetComponent<SteerModule>());

            Assert.AreEqual("Vertical", so.FindProperty("verticalActionName").stringValue,
                "The flap axis comes in on the Vertical action; without it Space and Ctrl do nothing.");
            Assert.IsFalse(so.FindProperty("jumpEnabled").boolValue,
                "A flying machine has nothing to jump off.");
            Assert.IsFalse(so.FindProperty("leapEnabled").boolValue);
        }
    }
}
