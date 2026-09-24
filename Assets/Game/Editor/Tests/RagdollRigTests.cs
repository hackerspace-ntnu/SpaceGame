using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
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

        /// <summary>A <c>NetMsg.Knockdown</c> as every machine receives it: <c>A</c> = ms down, <c>B</c> = cause.</summary>
        private static void Knock(Component body, float seconds, RagdollCause cause = RagdollCause.Blast)
        {
            var arg = new SpaceGame.Core.NetArg { P = Vector3.zero, A = Mathf.RoundToInt(seconds * 1000f), B = (int)cause };
            Invoke(body, "OnKnockdown", arg, 0UL);
        }

        [Test]
        public void Controller_StandsUpOnlyAfterTheEventsDuration()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var ragdoll = root.AddComponent<AgentRagdoll>();
            Invoke(ragdoll, "Awake");

            Knock(ragdoll, 2f);
            Assert.IsTrue(rig.IsLimp);

            Invoke(ragdoll, "TickStandUp", Time.time + 1.9f);
            Assert.IsTrue(rig.IsLimp, "stood up before the 2 s the event asked for");

            Invoke(ragdoll, "TickStandUp", Time.time + 2f + ragdoll.Tuning.settleGraceSeconds + 0.01f);
            Assert.IsFalse(rig.IsLimp, "never stood up");
        }

        /// <summary>
        /// A client learns of every death through <c>RestoreHealth</c>, so <c>IsRestoring</c> is true
        /// for a fresh death there as much as for a save. Only a load may lay a corpse down unthrown.
        /// </summary>
        [Test]
        public void ReplicatedDeath_FallsLikeAnyDeath_ButALoadedCorpseIsNotThrown()
        {
            Physics.gravity = Vector3.zero;

            GameObject replicated = NewHumanoidRig(out RagdollRig replicatedRig);
            HealthComponent replicatedHealth = DyingBody(replicated);
            replicatedHealth.RestoreHealth(0);

            Assert.IsTrue(replicatedRig.IsLimp);
            Assert.Greater(FastestBone(replicated), 0.1f,
                "a death arriving over the wire was treated as a save load and dropped without its impulse");

            GameObject loaded = NewHumanoidRig(out RagdollRig loadedRig);
            HealthComponent loadedHealth = DyingBody(loaded);
            loadedHealth.LoadHealth(0);

            Assert.IsTrue(loadedRig.IsLimp);
            Assert.Less(FastestBone(loaded), 1e-4f, "a corpse loaded from a save was thrown again");
        }

        private static HealthComponent DyingBody(GameObject root)
        {
            var health = root.AddComponent<HealthComponent>();
            var ragdoll = root.AddComponent<AgentRagdoll>();
            Invoke(ragdoll, "Awake");
            Invoke(ragdoll, "OnEnable");
            return health;
        }

        private static float FastestBone(GameObject root)
        {
            float fastest = 0f;
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>())
                if (!body.isKinematic) fastest = Mathf.Max(fastest, body.linearVelocity.magnitude);
            return fastest;
        }

        /// <summary>
        /// The whole server path, offline: <c>Network.Decides</c> is true and with no relay on the
        /// body <c>NetSendTo(…, NetTo.All)</c> dispatches straight to the channel OnEnable
        /// registered, so damage → OnDamaged → Knock → NetMsg.Knockdown → OnKnockdown all run.
        /// </summary>
        [Test]
        public void SmallHit_DoesNotKnockDown_BigHitDoes()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var health = root.AddComponent<HealthComponent>();
            var ragdoll = root.AddComponent<AgentRagdoll>();
            Invoke(ragdoll, "Awake");
            Invoke(ragdoll, "OnEnable");

            health.Damage(Mathf.RoundToInt(health.GetMaxHealth * 0.05f), null);
            Assert.IsFalse(rig.IsLimp, "a 5% hit knocked the body down — automatic fire will stun-lock");

            health.Damage(Mathf.RoundToInt(health.GetMaxHealth * 0.4f), null);
            Assert.IsTrue(rig.IsLimp, "a 40% hit on a wounded body did not knock it down");

            // Lethal and harder still, so a Hit knockdown would push the stand-up time later.
            float standAt = ControllerField<float>(ragdoll, "standAt");
            health.Damage(health.GetHealth, null);
            Assert.AreEqual(standAt, ControllerField<float>(ragdoll, "standAt"),
                "a killing blow was priced as a hit knockdown — death's business, not the policy's");
        }

        /// <summary>
        /// Offline the request dispatches straight to the channel OnEnable registered, so
        /// RequestFallKnockdown → OnKnockdownRequest → Knock(Fall) → OnKnockdown all run here.
        /// </summary>
        [Test]
        public void FallRequest_KnocksDownForTheFallTime()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var ragdoll = root.AddComponent<AgentRagdoll>();
            Invoke(ragdoll, "Awake");
            Invoke(ragdoll, "OnEnable");

            RagdollController.RequestFallKnockdown(ragdoll);

            Assert.IsTrue(rig.IsLimp);
            Invoke(ragdoll, "TickStandUp", Time.time + ragdoll.Tuning.fallSeconds - 0.05f);
            Assert.IsTrue(rig.IsLimp, "stood up before the fall's second was up");
        }

        [Test]
        public void FallDamage_IsNotAlsoPricedAsAHit()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var health = root.AddComponent<HealthComponent>();
            var ragdoll = root.AddComponent<AgentRagdoll>();
            Invoke(ragdoll, "Awake");
            Invoke(ragdoll, "OnEnable");

            // Wounded first, so the fall's damage on its own would be a hit knockdown of about
            // 1.65 s — long enough to show whether it was priced.
            health.Damage(Mathf.RoundToInt(health.GetMaxHealth * 0.05f), null);
            Assert.IsFalse(rig.IsLimp, "the fixture's first hit has to leave the body standing");

            RagdollController.RequestFallKnockdown(ragdoll);
            float standAt = ControllerField<float>(ragdoll, "standAt");
            health.Damage(Mathf.RoundToInt(health.GetMaxHealth * 0.4f), null);

            Assert.AreEqual(standAt, ControllerField<float>(ragdoll, "standAt"),
                "the fall's damage was priced as a hit as well — a hard landing outlasted fallSeconds");
            Assert.IsFalse(ControllerField<bool>(ragdoll, "fallDamagePending"),
                "the fall's damage did not use up the mark, so the next sourceless hit will be swallowed");

            Invoke(ragdoll, "TickStandUp",
                   Time.time + ragdoll.Tuning.fallSeconds + ragdoll.Tuning.settleGraceSeconds + 0.01f);
            Assert.IsFalse(rig.IsLimp, "still down after the fall's time and the settle grace");
        }

        [Test]
        public void RefusedHold_GrantsNoHitImmunity()
        {
            // No bones at all, so GoLimp declines and HoldDown rolls itself back through Restore.
            var root = new GameObject("Boneless");
            spawned.Add(root);
            Rig(root);
            var ragdoll = root.AddComponent<AgentRagdoll>();
            Invoke(ragdoll, "Awake");

            Assert.IsFalse(ragdoll.HoldDown(new object()), "the fixture has to reproduce a refused hold");

            Assert.AreEqual(float.NegativeInfinity, ControllerField<float>(ragdoll, "stoodUpAt"),
                "a body that never went down was made immune to hits as though it had stood up");
        }

        /// <summary>
        /// A freeze holds a player ON THEIR FEET; a blast or a hit must not lay the statue down —
        /// neither through the deciding machine's door (<c>Knock</c>) nor on a machine that is
        /// only told about it (<c>OnKnockdown</c>), or the two disagree.
        /// </summary>
        [Test]
        public void StandingHold_RefusesKnockdowns()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var ragdoll = root.AddComponent<StandingHoldTestRagdoll>();
            Invoke(ragdoll, "Awake");
            Invoke(ragdoll, "OnEnable");

            Assert.IsTrue(ragdoll.HoldStanding(new object()), "the fixture has to hold the body standing");

            RagdollController.Knock(root, RagdollCause.Blast, new Vector3(20f, 5f, 0f));
            Assert.IsFalse(rig.IsLimp, "a blast laid a body held standing on the ground");

            Knock(ragdoll, 2f);
            Assert.IsFalse(rig.IsLimp, "a machine told of a knockdown laid the statue down anyway");
        }

        /// <summary>Exposes the standing claim <c>PlayerRagdoll.HoldStanding</c> takes.</summary>
        private sealed class StandingHoldTestRagdoll : AgentRagdoll
        {
            public bool HoldStanding(object holder) => HoldStandingClaim(holder);
        }

        /// <summary>Reads one of <see cref="RagdollController"/>'s private fields.</summary>
        private static T ControllerField<T>(RagdollController ragdoll, string name)
        {
            FieldInfo field = typeof(RagdollController).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"RagdollController.{name} no longer exists");
            return (T)field.GetValue(ragdoll);
        }

        [Test]
        public void Knockdown_RefusedWhileRiding()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var ragdoll = root.AddComponent<RidingTestRagdoll>();
            Invoke(ragdoll, "Awake");

            Knock(ragdoll, 1f);

            Assert.IsFalse(rig.IsLimp, "a rider went limp in the saddle and will be dragged through the ground");
        }

        /// <summary>
        /// Seated as a watcher sees it: parented under a carrier. The authority's other half, the
        /// CarriedBody claim, is the same one-line query PlayerRagdoll already makes, and staging
        /// it here would mean a Rigidbody and a static record to clean up after.
        /// </summary>
        [TestCase(typeof(SpaceGame.Agents.NpcPassenger))]
        [TestCase(typeof(SpaceGame.Vehicles.VesselSeats))]
        public void Knockdown_RefusedWhileSeatedAsPassenger(System.Type carrierKind)
        {
            var carrier = new GameObject("Carrier", carrierKind);
            spawned.Add(carrier);
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            root.transform.SetParent(carrier.transform, false);
            var ragdoll = root.AddComponent<AgentRagdoll>();
            Invoke(ragdoll, "Awake");

            Knock(ragdoll, 1f);

            Assert.IsFalse(rig.IsLimp, "a seated passenger went limp and will be dragged along by its carrier");
        }

        /// <summary>A controller that answers "someone is carrying me" — the saddle/seat case.</summary>
        private sealed class RidingTestRagdoll : AgentRagdoll
        {
            protected override bool RefusesToGoDown => true;
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

        [Test]
        public void GoLimp_HandsTheImpulseToEveryBone()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var impulse = new Vector3(12f, 3f, 0f);

            rig.GoLimp(impulse);

            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>())
                Assert.Less(Vector3.Distance(body.linearVelocity, impulse), 1e-3f,
                    $"{body.name} started at rest while the hips flew — the joints tear");
        }

        [Test]
        public void HardBlast_JointsStayConnected()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            rig.GoLimp(new Vector3(48f, 10f, 0f));
            StepPhysics(30);

            foreach (CharacterJoint joint in root.GetComponentsInChildren<CharacterJoint>())
            {
                Vector3 mine = joint.transform.TransformPoint(joint.anchor);
                Vector3 theirs = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
                Assert.Less(Vector3.Distance(mine, theirs), 0.05f, $"{joint.name} came apart");
                Assert.IsTrue(joint.enableProjection);
            }
        }

        [Test]
        public void FollowHips_MovesTheRoot_WithoutMovingAnyBone()
        {
            Physics.gravity = new Vector3(0f, -18f, 0f);
            GameObject root = NewSplitRig(out RagdollRig rig, out Transform body, out Transform leg);

            rig.GoLimp(new Vector3(5f, 0f, 0f));
            StepPhysics(10);

            Rigidbody legBody = leg.GetComponent<Rigidbody>();
            Vector3 legBefore = legBody.position;

            Invoke(rig, "FollowHips");
            Physics.SyncTransforms();

            Assert.Less(Vector3.Distance(root.transform.position, body.GetComponent<Rigidbody>().position),
                        1e-3f, "the root is not where the body is");
            Assert.Less(Vector3.Distance(legBody.position, legBefore), 1e-3f,
                "moving the root teleported a bone that is not a child of the hips");
        }

        [Test]
        public void Watcher_PinsHipsRotationToo()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            Transform hips = Find(root, "Hips");

            rig.GoLimp(Vector3.zero, drives: false);
            root.transform.rotation = Quaternion.Euler(0f, 0f, 90f);   // the wire says: lying on its side
            hips.rotation = Quaternion.identity;   // the root's turn dragged the hips along; undo it,
                                                   // so only the pin can bring them to the root

            Invoke(rig, "PinHipsToRoot");
            StepPhysics(1);

            Assert.Less(Quaternion.Angle(hips.rotation, root.transform.rotation), 1f,
                "a watcher's pelvis stays upright while the owner's body lies down");
        }

        [Test]
        public void SaveTakenMidKnockdown_RecordsTheRootUpright()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var saver = root.AddComponent<TransformSaveable>();

            rig.GoLimp(Vector3.zero);
            root.transform.rotation = Quaternion.Euler(0f, 40f, 90f);   // lying on its side, as FollowHips leaves it

            var saved = (TransformSaveable.State)saver.CaptureState();

            Assert.Less(Quaternion.Angle(saved.rotation, Quaternion.Euler(0f, 40f, 0f)), 1f,
                "a body saved while knocked down reloads alive and not limp, and stays tilted forever");
        }

        [Test]
        public void SaveTakenOfACorpse_KeepsTheTilt()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            var saver = root.AddComponent<TransformSaveable>();

            rig.IsCorpse = true;
            rig.GoLimp(Vector3.zero);
            Quaternion lying = Quaternion.Euler(0f, 40f, 90f);
            root.transform.rotation = lying;

            var saved = (TransformSaveable.State)saver.CaptureState();

            Assert.Less(Quaternion.Angle(saved.rotation, lying), 1f,
                "a corpse goes limp again on load and should start lying the way it lay");
        }

        /// <summary>
        /// A load rebuilds the model on the saved root in its STANDING pose, so the saved root has
        /// to be the one that pose hangs the pelvis where the corpse lay — not the pelvis itself,
        /// where the root sits while limp. Frozen by the budget, the answer must not change.
        /// </summary>
        [Test]
        public void SaveTakenOfACorpse_PutsTheRootWhereTheStandingPoseReturnsThePelvis()
        {
            Physics.gravity = Vector3.zero;
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            Transform hips = Find(root, "Hips");
            var saver = root.AddComponent<TransformSaveable>();
            Vector3 standingOffset = hips.position - root.transform.position;

            rig.IsCorpse = true;
            rig.GoLimp(Vector3.zero);
            Quaternion lying = Quaternion.Euler(0f, 40f, 90f);
            root.transform.SetPositionAndRotation(new Vector3(3f, 0f, 2f), lying);
            Physics.SyncTransforms();
            Invoke(rig, "FollowHips");
            Assert.Less(Vector3.Distance(root.transform.position, hips.position), 1e-3f,
                "the fixture has to leave the root at the pelvis, as a limp body's root is");

            var saved = (TransformSaveable.State)saver.CaptureState();

            Assert.Less(Quaternion.Angle(saved.rotation, lying), 1f);
            Assert.Less(Vector3.Distance(saved.position + saved.rotation * standingOffset, hips.position), 1e-3f,
                "a reloaded corpse's pelvis lands a hip height along its tilted up axis from where it lay");

            FreezeInEditMode(rig);
            var frozen = (TransformSaveable.State)saver.CaptureState();

            Assert.Less(Vector3.Distance(frozen.position, saved.position), 1e-3f,
                "a corpse the budget froze is saved somewhere else than the same corpse still limp");
            Assert.Less(Quaternion.Angle(frozen.rotation, saved.rotation), 0.1f);
        }

        [Test]
        public void LoadedCorpse_IsNotThrown_ButIsLeftToSettle()
        {
            GameObject root = NewHumanoidRig(out RagdollRig rig);

            rig.GoLimp(new Vector3(10f, 2f, 0f), settled: true);

            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>())
                Assert.Less(body.linearVelocity.magnitude, 1e-4f, $"{body.name} was thrown on load");
            Assert.IsFalse(rig.IsSettled,
                "a loaded corpse was put to sleep at once — rebuilt in its standing pose, it lies as a rigid plank");
        }

        [Test]
        public void DroppedBody_ComesToRest_AndSleeps()
        {
            Physics.gravity = new Vector3(0f, -18f, 0f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawned.Add(ground);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);

            GameObject root = NewHumanoidRig(out RagdollRig rig);
            rig.GoLimp(new Vector3(2f, 1f, 0f));

            StepPhysics(100);   // two seconds to land and stop

            float fastest = 0f;
            for (int i = 0; i < 50; i++)   // the next second must be still
            {
                StepPhysics(1);
                foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>())
                    if (!body.isKinematic) fastest = Mathf.Max(fastest, body.linearVelocity.magnitude);
            }

            Assert.Less(fastest, 0.05f, "a body on flat ground is still jittering after two seconds");

            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>())
                if (!body.isKinematic) Assert.IsTrue(body.IsSleeping(), $"{body.name} never went to sleep");
        }

        [Test]
        public void Recovery_ReturnsEveryBoneToItsPose_EvenWithNoAnimator()
        {
            Physics.gravity = new Vector3(0f, -18f, 0f);
            GameObject root = NewHumanoidRig(out RagdollRig rig);

            var before = new Dictionary<Transform, (Vector3, Quaternion)>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>())
                before[t] = (t.localPosition, t.localRotation);

            rig.GoLimp(new Vector3(8f, 4f, 0f));
            StepPhysics(40);
            rig.Recover();

            for (int frame = 0; frame < 20; frame++)
            {
                Invoke(rig, "WriteRecoveryTarget");
                Invoke(rig, "BlendRecovery", 0.05f);
            }

            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>())
            {
                Transform t = body.transform;
                if (t == root.transform) continue;

                Assert.Less(Vector3.Distance(t.localPosition, before[t].Item1), 1e-3f, $"{t.name} stayed stretched");
                Assert.Less(Quaternion.Angle(t.localRotation, before[t].Item2), 0.5f, $"{t.name} stayed bent");
                Assert.AreEqual(RigidbodyInterpolation.None, body.interpolation,
                    $"{t.name} keeps an interpolated body that fights the animator");
            }
        }

        [Test]
        public void Recovery_StartsTheBlendFromWhereTheBodyLay()
        {
            Physics.gravity = Vector3.zero;
            GameObject root = NewHumanoidRig(out RagdollRig rig);
            Transform hips = Find(root, "Hips");

            // Lying on its side, the way a fall leaves it: the bodies turned over, then the root
            // dragged after them — so the tilt sits in the root and the hips' local pose is rest.
            rig.GoLimp(Vector3.zero);
            root.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            Physics.SyncTransforms();
            StepPhysics(1);
            Invoke(rig, "FollowHips");
            Quaternion lying = hips.rotation;
            Assert.Greater(Quaternion.Angle(lying, Quaternion.identity), 80f,
                "the fixture has to lie the body down, or an upright blend start is not a snap");

            rig.Recover();
            Invoke(rig, "WriteRecoveryTarget");
            Invoke(rig, "BlendRecovery", 0.01f);

            Assert.Less(Quaternion.Angle(hips.rotation, lying), 5f,
                "one hundredth of a second into getting up, the lying body already stands vertical");

            for (int frame = 0; frame < 10; frame++)
            {
                Invoke(rig, "WriteRecoveryTarget");
                Invoke(rig, "BlendRecovery", 0.05f);
            }
            Assert.AreEqual(0f, RigField<float>(rig, "blendRemaining"), "the blend never finished");

            Quaternion animated = Quaternion.Euler(30f, 0f, 0f);
            hips.localRotation = animated;
            Invoke(rig, "Update");
            Assert.Less(Quaternion.Angle(hips.localRotation, animated), 0.01f,
                "the recovery target is still written over the animator after the blend has ended");
        }

        /// <summary>Reads one of <see cref="RagdollRig"/>'s private fields.</summary>
        private static T RigField<T>(RagdollRig rig, string name)
        {
            FieldInfo field = typeof(RagdollRig).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"RagdollRig.{name} no longer exists");
            return (T)field.GetValue(rig);
        }
    }
}
