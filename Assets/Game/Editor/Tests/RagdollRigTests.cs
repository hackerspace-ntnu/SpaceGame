using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpaceGame.Gameplay.Ragdoll;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The ragdoll's physical behaviour, measured on hard-surface rigs built in code: empty bones
    /// with cubes parented on as leaves, which is exactly the shape RagdollRig's structural rule
    /// selects. Physics runs in Script mode so each test decides how many steps happen.
    /// </summary>
    public class RagdollRigTests
    {
        private const float Step = 0.02f;

        private readonly List<GameObject> spawned = new List<GameObject>();
        private SimulationMode originalSimulationMode;
        private Vector3 originalGravity;

        [SetUp]
        public void SetUp()
        {
            originalSimulationMode = Physics.simulationMode;
            originalGravity = Physics.gravity;
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();

            Physics.simulationMode = originalSimulationMode;
            Physics.gravity = originalGravity;
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// Call <see cref="RagdollRig.Freeze"/> from an EditMode test.
        ///
        /// <para>
        /// Freeze tears the skeleton down with <c>Object.Destroy</c>, which is right at runtime and
        /// logs "Destroy may not be called from edit mode" once per joint, created collider and body
        /// here. How many of each the rig creates is its own business, so rather than expect a count
        /// that breaks whenever the build changes, the errors are tolerated for the rest of the test.
        /// TearDown switches that back off.
        /// </para>
        /// </summary>
        private static void FreezeInEditMode(RagdollRig rig)
        {
            LogAssert.ignoreFailingMessages = true;
            rig.Freeze();
        }

        private static void StepPhysics(int steps)
        {
            for (int i = 0; i < steps; i++) Physics.Simulate(Step);
        }

        /// <summary>
        /// Calls a method by name, searching base classes too: reflection never returns a base
        /// class's PRIVATE members from a derived type, and RagdollController's private handlers
        /// (Phase B) are reached through AgentRagdoll.
        /// </summary>
        private static object Invoke(object target, string method, params object[] args)
        {
            MethodInfo info = null;
            for (System.Type type = target.GetType(); type != null && info == null; type = type.BaseType)
                info = type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic |
                                              BindingFlags.Public | BindingFlags.DeclaredOnly);

            Assert.IsNotNull(info, $"{target.GetType().Name}.{method} no longer exists — the test " +
                                   "would silently measure nothing.");
            return info.Invoke(target, args);
        }

        /// <summary>An empty bone with one collider-less cube hanging under it.</summary>
        private static Transform Bone(Transform parent, string name, Vector3 localPosition,
                                      Vector3 partSize, Vector3 partOffset)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;

            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.name = name + "_Mesh";
            part.transform.SetParent(bone, false);
            part.transform.localPosition = partOffset;
            part.transform.localScale = partSize;

            return bone;
        }

        private RagdollRig Rig(GameObject root)
        {
            RagdollRig rig = root.AddComponent<RagdollRig>();
            Invoke(rig, "Awake");
            return rig;
        }

        /// <summary>Hips → spine; hips → thigh → shin, both sides. Everything under the hips.</summary>
        private GameObject NewHumanoidRig(out RagdollRig rig)
        {
            var root = new GameObject("Humanoid");
            spawned.Add(root);

            Transform hips = Bone(root.transform, "Hips", new Vector3(0f, 1f, 0f),
                                  new Vector3(0.4f, 0.3f, 0.3f), Vector3.zero);
            Bone(hips, "Spine", new Vector3(0f, 0.2f, 0f),
                 new Vector3(0.4f, 0.5f, 0.25f), new Vector3(0f, 0.25f, 0f));

            foreach (float side in new[] { -1f, 1f })
            {
                Transform thigh = Bone(hips, side < 0 ? "ThighL" : "ThighR",
                                       new Vector3(0.15f * side, -0.1f, 0f),
                                       new Vector3(0.15f, 0.4f, 0.15f), new Vector3(0f, -0.2f, 0f));
                Bone(thigh, side < 0 ? "ShinL" : "ShinR", new Vector3(0f, -0.4f, 0f),
                     new Vector3(0.12f, 0.4f, 0.12f), new Vector3(0f, -0.2f, 0f));
            }

            rig = Rig(root);
            return root;
        }

        /// <summary>
        /// A body and a leg that are SIBLINGS under an empty pelvis — the ostrich's shape. The pelvis
        /// carries nothing, so the body becomes the ragdoll's root bone and the leg is jointed to it
        /// without being its child in the hierarchy.
        /// </summary>
        private GameObject NewSplitRig(out RagdollRig rig, out Transform body, out Transform leg)
        {
            var root = new GameObject("Split");
            spawned.Add(root);

            var pelvis = new GameObject("Pelvis").transform;
            pelvis.SetParent(root.transform, false);
            pelvis.localPosition = new Vector3(0f, 1f, 0f);

            body = Bone(pelvis, "Body", Vector3.zero, new Vector3(0.6f, 0.4f, 0.8f), Vector3.zero);
            leg = Bone(pelvis, "Leg", new Vector3(0f, -0.2f, 0f),
                       new Vector3(0.12f, 0.6f, 0.12f), new Vector3(0f, -0.3f, 0f));

            rig = Rig(root);
            return root;
        }

        private static Transform Find(GameObject root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;

            Assert.Fail($"No '{name}' under {root.name}.");
            return null;
        }

        [Test]
        public void Fixture_BuildsAJointedSkeleton()
        {
            NewHumanoidRig(out RagdollRig rig);

            rig.GoLimp(Vector3.zero);

            Assert.AreEqual(6, rig.BoneCount, "hips, spine, two thighs, two shins");
            Assert.AreEqual(5, rig.JointCount, "every bone but the hips is jointed to its parent");
        }

        [Test]
        public void Judge_NeverEvictsALivingBody()
        {
            Assert.AreEqual(RagdollBudget.Verdict.Skip,
                RagdollBudget.Judge(excluded: false, exempt: false, corpse: false, settled: true),
                "a knocked-down body that is still alive stands up within seconds — freezing it " +
                "leaves it standing in its ragdoll pose with its brain switched back on");
            Assert.AreEqual(RagdollBudget.Verdict.Take,
                RagdollBudget.Judge(excluded: false, exempt: false, corpse: true, settled: true));
            Assert.AreEqual(RagdollBudget.Verdict.Consider,
                RagdollBudget.Judge(excluded: false, exempt: false, corpse: true, settled: false));
        }

        [Test]
        public void Recover_AfterFreeze_GivesTheAnimatorBack()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            Animator animator = root.AddComponent<Animator>();
            Invoke(rig, "Awake");   // re-resolve the animator now that it exists

            rig.GoLimp(Vector3.zero);
            Assert.IsFalse(animator.enabled);

            FreezeInEditMode(rig);
            rig.Recover();

            Assert.IsTrue(animator.enabled,
                "Freeze took the body off physics; nothing else will ever switch the animator on");
        }

        [Test]
        public void AgentRagdoll_ReviveAfterTheBudgetFrozeTheCorpse_HandsTheBodyBack()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            Animator animator = root.AddComponent<Animator>();
            Invoke(rig, "Awake");
            var ragdoll = root.AddComponent<AgentRagdoll>();
            Invoke(ragdoll, "Awake");

            Invoke(ragdoll, "OnDeath");
            Assert.IsTrue(rig.IsCorpse, "death has to mark the body a corpse, or the budget never takes it");

            FreezeInEditMode(rig);
            Assert.IsFalse(rig.IsLimp, "the fixture has to reproduce a frozen corpse: suspended but not limp");

            Invoke(ragdoll, "OnRevive");

            Assert.IsFalse(rig.IsCorpse);
            Assert.IsTrue(animator.enabled,
                "a corpse the budget froze is not limp but is still suspended — revive has to hand it " +
                "back, or the creature comes back to life with its animation switched off");
        }

        [Test]
        public void Joints_TwistAboutTheirOwnBone()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            rig.GoLimp(Vector3.zero);

            var thigh = Find(root, "ThighL").GetComponent<CharacterJoint>();
            Vector3 towardShin = thigh.transform.InverseTransformDirection(
                Find(root, "ShinL").position - thigh.transform.position);

            Assert.Less(Vector3.Angle(thigh.axis, towardShin), 1f,
                "the twist axis lies across the thigh, so its ±45° swing limit is a twist limit");
        }

        [Test]
        public void Joints_BoneWithItsMeshAtItsOrigin_TwistsAlongTheParentToBoneLine()
        {
            // The hard-surface shape: the leg's only child is its mesh, sitting exactly on the bone.
            var root = new GameObject("HardSurface");
            spawned.Add(root);

            Transform hips = Bone(root.transform, "Hips", new Vector3(0f, 1f, 0f),
                                  new Vector3(0.4f, 0.3f, 0.3f), Vector3.zero);
            Transform leg = Bone(hips, "Leg", new Vector3(0.2f, -0.4f, 0f),
                                 new Vector3(0.15f, 0.6f, 0.15f), Vector3.zero);
            leg.localRotation = Quaternion.Euler(0f, 0f, 30f);

            RagdollRig rig = Rig(root);
            rig.GoLimp(Vector3.zero);

            var joint = leg.GetComponent<CharacterJoint>();
            Assert.IsNotNull(joint, "the fixture has to joint the leg to the hips");

            Vector3 alongLeg = leg.InverseTransformDirection(leg.position - hips.position);
            Assert.Less(Vector3.Angle(joint.axis, alongLeg), 1f,
                "a zero-offset mesh child was read as the bone's direction and the twist fell back to X");
        }

        [Test]
        public void SecondKnockdown_LimitsAreRelativeToTheNewPose()
        {
            Physics.gravity = Vector3.zero;
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            Transform thigh = Find(root, "ThighL");

            rig.GoLimp(Vector3.zero, settled: true);
            rig.Recover();

            // Well past the 45° swing limit measured from the first pose.
            Quaternion posed = Quaternion.Euler(80f, 0f, 0f);
            thigh.localRotation = posed;

            rig.GoLimp(Vector3.zero);
            StepPhysics(20);

            Assert.Less(Quaternion.Angle(thigh.localRotation, posed), 3f,
                "the joint still measures its limits from the first knockdown's pose and snapped the leg");
        }

        [Test]
        public void GoLimp_AfterAJointedBoneIsDestroyed_DoesNotThrow()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            rig.GoLimp(Vector3.zero, settled: true);
            rig.Recover();

            Object.DestroyImmediate(Find(root, "ThighL").gameObject);

            Assert.DoesNotThrow(() => rig.GoLimp(Vector3.zero));
            Assert.AreEqual(3, rig.JointCount, "spine, and the right thigh and shin");
        }
    }
}
