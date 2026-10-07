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

        private WornFit Fit(Vector3 position, Vector3 euler, float size, bool anchorToBone = false,
                            float inspectSize = 0f)
        {
            var fit = instance.AddComponent<WornFit>();
            var so = new SerializedObject(fit);
            so.FindProperty("localPosition").vector3Value = position;
            so.FindProperty("localEuler").vector3Value = euler;
            so.FindProperty("size").floatValue = size;
            so.FindProperty("inspectSize").floatValue = inspectSize;
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

        /// <summary>The gear screen's model, four cubes across — a different object with a
        /// different span, the way the pack's spread wings are to its stowed ones.</summary>
        private GameObject AddInspectModel()
        {
            var inspect = new GameObject(WornVisual.InspectChildName);
            inspect.transform.SetParent(instance.transform, false);

            var mesh = new GameObject("HugeCube");
            mesh.transform.SetParent(inspect.transform, false);
            mesh.transform.localScale = new Vector3(4f, 4f, 4f);
            mesh.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            mesh.AddComponent<MeshRenderer>();

            inspect.SetActive(false);
            return inspect;
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
            // pack's worn wings would be scaled to the size of the bundle it is carried as.
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

        // ── The gear screen's model ───────────────────────────────────────────
        //
        // The wing pack wears two different worn models: stowed out in the world, spread on the
        // gear screen. They are two objects with two spans, so the form has to reach both the
        // swap and the size — sizing the spread wings by the stowed bundle's number is the same
        // failure as scaling the worn wings by hand, and it looks deliberate.

        [Test]
        public void InspectedShowsTheGearScreenModel()
        {
            GameObject worn = AddWornModel();
            GameObject inspect = AddInspectModel();

            WornSeat.Apply(instance, bone.transform, Fit(Vector3.zero, Vector3.zero, 0f),
                           null, WornVisual.Form.Inspected);

            Assert.IsTrue(inspect.activeSelf, "the gear screen's model is shown");
            Assert.IsFalse(worn.activeSelf, "the world's worn model is hidden");
            Assert.IsFalse(instance.transform.Find("Body").gameObject.activeSelf,
                           "and so is the carried one");
        }

        [Test]
        public void InspectedFallsBackToTheWornModel()
        {
            // Every item but the wing pack. Nothing else had to gain a second model for the gear
            // screen to keep working.
            GameObject worn = AddWornModel();

            WornSeat.Apply(instance, bone.transform, Fit(Vector3.zero, Vector3.zero, 0f),
                           null, WornVisual.Form.Inspected);

            Assert.IsTrue(worn.activeSelf, "with no gear-screen model, the worn one stands in");
        }

        [Test]
        public void InspectedIsDrawnAtItsOwnSizeNotTheWornOnes()
        {
            AddWornModel();
            AddInspectModel();

            WornSeat.Apply(instance, bone.transform,
                           Fit(Vector3.zero, Vector3.zero, 4f, inspectSize: 8f),
                           null, WornVisual.Form.Inspected);

            Assert.AreEqual(2f, instance.transform.localScale.x, 1e-4f,
                            "a 4 m gear-screen model drawn at 8 m — not squeezed into the worn size");
        }

        [Test]
        public void InspectedFallsBackToTheWornSizeWhenItHasNoneOfItsOwn()
        {
            AddWornModel();

            WornSeat.Apply(instance, bone.transform, Fit(Vector3.zero, Vector3.zero, 4f),
                           null, WornVisual.Form.Inspected);

            Assert.AreEqual(2f, instance.transform.localScale.x, 1e-4f,
                            "one size still serves an item with one worn shape");
        }

        [Test]
        public void CarriedHidesBothWornModels()
        {
            // The gear screen's model is new, and the swap back to the hand has to know about it:
            // left on, it would hang off the item in the player's fist and be measured with it.
            GameObject worn = AddWornModel();
            GameObject inspect = AddInspectModel();

            WornVisual.SetForm(instance, WornVisual.Form.Carried);

            Assert.IsFalse(worn.activeSelf);
            Assert.IsFalse(inspect.activeSelf);
            Assert.IsTrue(instance.transform.Find("Body").gameObject.activeSelf,
                          "and the carried model comes back");
        }

        [Test]
        public void ReSeatingSwapsBetweenTheTwoWornModelsBothWays()
        {
            // What opening and closing the gear screen does to gear already on the body. It must
            // survive the round trip: a pack left spread walks a five-metre wingspan into the
            // world, and one that never spreads makes the screen pointless.
            GameObject worn = AddWornModel();
            GameObject inspect = AddInspectModel();
            WornFit fit = Fit(Vector3.zero, Vector3.zero, 4f, inspectSize: 8f);

            WornSeat.Apply(instance, bone.transform, fit, null, WornVisual.Form.Worn);
            WornSeat.Apply(instance, bone.transform, fit, null, WornVisual.Form.Inspected);

            Assert.IsTrue(inspect.activeSelf);
            Assert.AreEqual(2f, instance.transform.localScale.x, 1e-4f);

            WornSeat.Apply(instance, bone.transform, fit, null, WornVisual.Form.Worn);

            Assert.IsTrue(worn.activeSelf, "back to the stowed model");
            Assert.IsFalse(inspect.activeSelf);
            Assert.AreEqual(2f, instance.transform.localScale.x, 1e-4f,
                            "and back to the stowed model's own scale");
        }
    }
}
