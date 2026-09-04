// The one copy of the WornFit seating arithmetic, shared by the real worn item and the body
// screen's ghost of it — so both land in the same place at the same size.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public class WornSeatTests
    {
        private GameObject bone;
        private GameObject instance;

        [SetUp]
        public void SetUp()
        {
            bone = new GameObject("Spine");
            bone.transform.position = new Vector3(1f, 1.2f, 0f);

            instance = new GameObject("Pack");
            var body = new GameObject("Body");
            body.transform.SetParent(instance.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            body.AddComponent<MeshRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(bone);
        }

        private WornFit Fit(Vector3 position, Vector3 euler, float size, bool anchorToBone = false)
        {
            var fit = instance.AddComponent<WornFit>();
            var so = new SerializedObject(fit);
            so.FindProperty("localPosition").vector3Value = position;
            so.FindProperty("localEuler").vector3Value = euler;
            so.FindProperty("size").floatValue = size;
            so.FindProperty("anchorToBone").boolValue = anchorToBone;
            so.ApplyModifiedPropertiesWithoutUndo();
            return fit;
        }

        /// <summary>A second model on the item, the size of two cubes, under the name the swap
        /// looks for. Switched off, as it ships.</summary>
        private GameObject AddWornModel()
        {
            var worn = new GameObject(WornVisual.ChildName);
            worn.transform.SetParent(instance.transform, false);

            var mesh = new GameObject("BigCube");
            mesh.transform.SetParent(worn.transform, false);
            mesh.transform.localScale = new Vector3(2f, 2f, 2f);
            mesh.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            mesh.AddComponent<MeshRenderer>();

            worn.SetActive(false);
            return worn;
        }

        [Test]
        public void SeatsAtTheFitsPoseAndSize()
        {
            WornFit fit = Fit(new Vector3(0f, 0.05f, -0.22f), new Vector3(0f, 90f, 0f), 0.5f);

            WornSeat.Apply(instance, bone.transform, fit);

            Assert.AreEqual(bone.transform, instance.transform.parent);
            Assert.AreEqual(0.5f, instance.transform.localScale.x, 1e-4f, "a 1 m cube drawn at 0.5 m");
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(0f, 0.05f, -0.22f), instance.transform.localPosition), 1e-5f);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.Euler(0f, 90f, 0f), instance.transform.localRotation), 1e-3f);
        }

        [Test]
        public void ZeroSizeKeepsTheAuthoredScale()
        {
            instance.transform.localScale = new Vector3(2f, 2f, 2f);
            WornFit fit = Fit(Vector3.zero, Vector3.zero, 0f);

            WornSeat.Apply(instance, bone.transform, fit);

            Assert.AreEqual(2f, instance.transform.localScale.x, 1e-5f);
        }

        [Test]
        public void NoFitIsTheBoneItself()
        {
            WornSeat.Apply(instance, bone.transform, null);

            Assert.AreEqual(bone.transform, instance.transform.parent);
            Assert.AreEqual(Vector3.zero, instance.transform.localPosition);
            Assert.AreEqual(Quaternion.identity, instance.transform.localRotation);
        }

        // ── The two places one torso slot has ─────────────────────────────────

        [Test]
        public void ChestKindTakesTheChestBoneAndEveryOtherKindTheSpine()
        {
            var chest = new GameObject("Chest").transform;

            Assert.AreEqual(chest, WornSeat.BoneFor(EquipKind.Chest, bone.transform, chest));
            Assert.AreEqual(bone.transform, WornSeat.BoneFor(EquipKind.Back, bone.transform, chest));

            Object.DestroyImmediate(chest.gameObject);
        }

        [Test]
        public void ChestKindFallsBackToTheSpineOnARigWithNoChestBone()
        {
            // A rig that cannot tell its chest from its spine wears the thing anyway. The
            // alternative is a chest item that silently does not appear.
            Assert.AreEqual(bone.transform, WornSeat.BoneFor(EquipKind.Chest, bone.transform, null));
        }

        // ── The mount ─────────────────────────────────────────────────────────

        [Test]
        public void AMountPlacesTheItemOnItselfRatherThanAtTheFitsOffset()
        {
            var mount = new GameObject("Mesh_Rig_LashRail").transform;
            mount.position = new Vector3(1f, 1.45f, -0.3f);
            WornFit fit = Fit(new Vector3(0f, 0.05f, -0.22f), new Vector3(0f, 90f, 0f), 0.5f);

            WornSeat.Apply(instance, bone.transform, fit, mount);

            Assert.AreEqual(bone.transform, instance.transform.parent, "parented to the BONE, so a deployed pack cannot take it");
            Assert.AreEqual(0f, Vector3.Distance(mount.position, instance.transform.position), 1e-4f);

            // The rail's own rotation is the pack's leaf angle and says nothing about which way up
            // a wing pack goes, so the orientation is still the fit's.
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.Euler(0f, 90f, 0f), instance.transform.localRotation), 1e-3f);
            Assert.AreEqual(0.5f, instance.transform.localScale.x, 1e-4f);

            Object.DestroyImmediate(mount.gameObject);
        }

        [Test]
        public void NoMountFallsBackToTheAuthoredOffset()
        {
            WornFit fit = Fit(new Vector3(0f, 0.05f, -0.22f), Vector3.zero, 0.5f);

            WornSeat.Apply(instance, bone.transform, fit, null);

            Assert.AreEqual(0f, Vector3.Distance(new Vector3(0f, 0.05f, -0.22f), instance.transform.localPosition), 1e-5f);
        }

        [Test]
        public void AnchorToBoneIgnoresTheMount()
        {
            // Gear shaped around the WEARER rather than clipped to the pack: the worn wingsuit's
            // wing roots are the wearer's own shoulders, and the rail is half a metre behind them.
            var mount = new GameObject("Mesh_Rig_LashRail").transform;
            mount.position = new Vector3(1f, 1.45f, -0.3f);
            WornFit fit = Fit(Vector3.zero, Vector3.zero, 0.5f, anchorToBone: true);

            WornSeat.Apply(instance, bone.transform, fit, mount);

            Assert.AreEqual(0f, Vector3.Distance(Vector3.zero, instance.transform.localPosition), 1e-5f,
                            "seated on the bone, not on the rail it was handed");

            Object.DestroyImmediate(mount.gameObject);
        }

        // ── The worn model ────────────────────────────────────────────────────

        [Test]
        public void SeatingSwapsInTheWornModel()
        {
            GameObject worn = AddWornModel();

            WornSeat.Apply(instance, bone.transform, Fit(Vector3.zero, Vector3.zero, 0f));

            Assert.IsTrue(worn.activeSelf, "the worn model is shown");
            Assert.IsFalse(instance.transform.Find("Body").gameObject.activeSelf, "the carried model is hidden");
        }

        [Test]
        public void TheSizeMeasuresTheWORNModelNotTheCarriedOne()
        {
            // The order inside Apply is what this pins. Swap after the measurement and the wing
            // pack's 3.5 m of wings would be scaled to the size of the bundle it is carried as.
            AddWornModel();

            WornSeat.Apply(instance, bone.transform, Fit(Vector3.zero, Vector3.zero, 4f));

            Assert.AreEqual(2f, instance.transform.localScale.x, 1e-4f,
                            "a 2 m worn model drawn at 4 m, not a 1 m carried one drawn at 4 m");
        }

        [Test]
        public void AnItemWithNoWornModelIsUntouched()
        {
            WornSeat.Apply(instance, bone.transform, Fit(Vector3.zero, Vector3.zero, 0.5f));

            Assert.IsTrue(instance.transform.Find("Body").gameObject.activeSelf,
                          "the swap is opt-in: every item but two has one look");
        }

        [Test]
        public void TheSwapLeavesChildrenThatDrawNothingAlone()
        {
            // Hiding a model is not the same as taking an item apart: a grip point, a muzzle
            // marker or a collider-only child has to survive being worn.
            AddWornModel();
            var marker = new GameObject("GripPoint");
            marker.transform.SetParent(instance.transform, false);

            WornVisual.SetWorn(instance, true);

            Assert.IsTrue(marker.activeSelf);
        }
    }
}
