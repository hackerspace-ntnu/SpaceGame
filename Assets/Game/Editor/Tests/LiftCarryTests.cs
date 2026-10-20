// A lifted load is in the carrier's hands every frame, whatever the carrier does, with its far end on the ground and never under
// it; it slows the walk to the carry pace; Esc and interact put it down; and a save taken mid-carry records it lying where it was
// carried. Measured on the real player rig and the real loose oxygen plant — and somebody tied up goes over the shoulder with
// the feet in the carrier's view, measured on real Raxy, Nomad and DuneRat rigs.
//
// In Editor/ rather than beside the asmdef'd EditMode tests because these touch Assembly-CSharp types. Nothing runs Awake or
// OnEnable in EditMode, so the rig is posed only by the carrier's own arm reach — which is exactly the part under test.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceGame.Characters;
using SpaceGame.Persistence;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class LiftCarryTests
    {
        private const string PlayerPath = "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";
        private const float Stage = 900f;

        // A fist closes within this of its grip, every frame.
        private const float HandsOnTheGrip = 0.03f;

        private readonly List<GameObject> made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in made)
            {
                if (go == null) continue;
                go.GetComponent<LiftCarrier>()?.Drop();
                Object.DestroyImmediate(go);
            }
            made.Clear();
        }

        // ─────────────────────────── The solver ───────────────────────────

        private static readonly LiftShape Plank = new(new Vector3(0f, 0.6f, -1.9f), new Vector3(0f, 0f, -1.9f), new Vector3(0f, 0f, 1.9f), Vector3.up);

        [Test]
        public void TheGripIsExactlyWhereTheHandsAreAndTheFarEndRestsOnFlatGround()
        {
            var hands = new Vector3(3f, 1.5f, -2f);
            foreach (float yaw in new[] { 0f, 37f, 90f, 181f, 300f })
            {
                Vector3 heading = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Pose pose = LiftPoseSolver.Solve(Plank, hands, heading, (x, z) => 0f);

                Assert.Less(Vector3.Distance(pose.position + pose.rotation * Plank.Grip, hands), 1e-4f,
                            $"at heading {yaw} the grip is not in the hands.");
                Assert.AreEqual(0f, (pose.position + pose.rotation * Plank.Foot).y, 0.01f,
                                $"at heading {yaw} the far end is not on the ground.");
                Assert.Greater(Vector3.Dot(LiftPoseSolver.Flat(pose.rotation * Plank.Axis), heading), 0.999f,
                               $"at heading {yaw} the load does not point the way it was swung.");
            }
        }

        [Test]
        public void TheFarEndFollowsASlopeAndNeverSinksIntoACrest()
        {
            var hands = new Vector3(0f, 1.5f, 0f);

            // Uphill ahead: the ground rises 0.4 m per metre.
            Pose up = LiftPoseSolver.Solve(Plank, hands, Vector3.forward, (x, z) => 0.4f * z);
            Vector3 foot = up.position + up.rotation * Plank.Foot;
            Assert.AreEqual(0.4f * foot.z, foot.y, 0.02f, "on a slope the far end floats or digs in.");

            // A ridge 0.9 m high a metre and a half ahead: the underside must clear it rather than pass through it.
            float Ridge(float x, float z) => Mathf.Abs(z - 1.5f) < 0.3f ? 0.9f : 0f;
            Pose over = LiftPoseSolver.Solve(Plank, hands, Vector3.forward, Ridge);
            for (int i = 0; i <= 20; i++)
            {
                Vector3 p = over.position + over.rotation * Vector3.Lerp(Plank.Heel, Plank.Foot, i / 20f);
                Assert.GreaterOrEqual(p.y, Ridge(p.x, p.z) - 0.05f, $"the underside passes {Ridge(p.x, p.z) - p.y:0.00} m into the ridge.");
            }
        }

        [Test]
        public void PutDownTheLoadLiesOnItsUndersideWhereItWasCarried()
        {
            Pose carried = LiftPoseSolver.Solve(Plank, new Vector3(5f, 1.5f, 5f), Vector3.right, (x, z) => 0f);
            Pose rest = LiftPoseSolver.Rest(Plank, carried, (x, z) => 0f);

            Assert.AreEqual(0f, (rest.position + rest.rotation * Plank.Heel).y, 0.01f, "the near end is not on the ground.");
            Assert.AreEqual(0f, (rest.position + rest.rotation * Plank.Foot).y, 0.01f, "the far end is not on the ground.");
            Assert.Less(Vector3.Distance(rest.position + rest.rotation * Plank.Foot, carried.position + carried.rotation * Plank.Foot), 0.25f,
                        "putting it down moved the far end across the ground.");
        }

        [Test]
        public void ATurnSwingsTheFarEndRoundSmoothlyAndCatchesUp()
        {
            Vector3 heading = Vector3.forward;
            float largestStep = 0f;
            for (int i = 0; i < 120; i++)
            {
                Vector3 next = LiftPoseSolver.Swing(heading, Vector3.right, 5f, 1f / 60f);
                largestStep = Mathf.Max(largestStep, Vector3.Angle(heading, next));
                heading = next;
            }

            // A body that snaps 90 degrees in one frame moves the load at most a twelfth of the way at 60 fps.
            Assert.Less(largestStep, 10f, "a 90 degree turn snapped the load round instead of swinging it.");
            Assert.Less(Vector3.Angle(heading, Vector3.right), 1f, "two seconds after a turn the load has still not followed.");
        }

        // ─────────────────────────── The real rig and the real plant ───────────────────────────

        [Test]
        public void TheHandsStayOnTheGripsAtEverySpeedAndTurn()
        {
            MakeGround();
            (GameObject body, Liftable plant, LiftCarrier carrier) = Lifted();

            foreach ((float speed, float turn) in new[] { (0f, 0f), (1.2f, 0f), (4.5f, 0f), (4.5f, 120f), (9f, 360f), (0f, -400f) })
            {
                for (int frame = 0; frame < 90; frame++)
                {
                    const float dt = 1f / 60f;
                    body.transform.rotation *= Quaternion.Euler(0f, turn * dt, 0f);
                    body.transform.position += body.transform.forward * (speed * dt);
                    Physics.SyncTransforms();
                    carrier.Follow(dt);

                    string at = $"at {speed} m/s turning {turn} deg/s, frame {frame}";
                    Assert.Less(Vector3.Distance(plant.GripPoint, carrier.CarryPoint()), 1e-3f, $"the grips left the carry point {at}.");
                    Assert.Less(Vector3.Distance(Palm(body, HumanBodyBones.LeftHand), Nearest(body, plant, false)), HandsOnTheGrip,
                                $"the left fist is off its grip {at}.");
                    Assert.Less(Vector3.Distance(Palm(body, HumanBodyBones.RightHand), Nearest(body, plant, true)), HandsOnTheGrip,
                                $"the right fist is off its grip {at}.");

                    Vector3 foot = plant.transform.TransformPoint(plant.transform.Find("Lift_Foot").localPosition);
                    Assert.GreaterOrEqual(foot.y, Stage - 0.02f, $"the far end is below the ground {at}.");
                    Assert.Less(foot.y, Stage + 0.05f, $"the far end lifted off the ground {at}.");
                }
            }
        }

        [Test]
        public void TheCarriedEndIsAtTheWaistAndAheadOfTheBody()
        {
            MakeGround();
            (GameObject body, Liftable plant, _) = Lifted();

            float height = plant.GripPoint.y - Stage;
            float hips = body.GetComponentInChildren<Animator>(true).GetBoneTransform(HumanBodyBones.Hips).position.y - Stage;
            float ahead = Vector3.Dot(plant.GripPoint - body.transform.position, body.transform.forward);
            Assert.That(height, Is.InRange(hips - 0.2f, hips + 0.5f), $"the lifted end ({height:0.00} m) is not at the waist (hips {hips:0.00} m).");
            Assert.That(ahead, Is.InRange(0.35f, 0.9f), "the lifted end is inside the body or out at arm's length.");
        }

        [Test]
        public void ASaveTakenMidCarryRecordsTheLoadLyingWhereItWasCarried()
        {
            MakeGround();
            (GameObject body, Liftable plant, LiftCarrier carrier) = Lifted();

            ISavedPose saved = plant;
            var rest = new Pose(saved.PositionToSave, saved.RotationToSave);
            Transform root = plant.transform;
            Vector3 Lying(string marker) => rest.position + rest.rotation * root.Find(marker).localPosition;

            Assert.AreEqual(Stage, Lying("Lift_Heel").y, 0.02f, "the save has the near end hanging in the air.");
            Assert.AreEqual(Stage, Lying("Lift_Foot").y, 0.02f, "the save has the far end off the ground.");
            Assert.Less(Vector3.Distance(Lying("Lift_Foot"), root.TransformPoint(root.Find("Lift_Foot").localPosition)), 0.3f,
                        "the save puts the load somewhere other than where it was carried.");

            carrier.Drop();
            Assert.AreEqual(root.position, saved.PositionToSave, "a load nobody carries saves somewhere other than where it is.");
        }

        // ─────────────────────────── Pace and exit ───────────────────────────

        [Test]
        public void TheCarryIsALittleSlowerThanAWalkWithNoSprint()
        {
            var plant = AssetDatabase.LoadAssetAtPath<GameObject>(OxygenPlantRecoveryAuthoring.LoosePlantPath).GetComponent<Liftable>();
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath).GetComponent<PlayerMovement>();
            float fraction = new SerializedObject(plant).FindProperty("carrySpeedFraction").floatValue;

            Assert.That(fraction, Is.InRange(0.7f, 0.8f), "the carry is not 'a little slower than walking'.");
            Assert.Less(player.WalkSpeed * fraction, player.WalkSpeed, "carrying does not slow the walk.");
            Assert.Less(player.WalkSpeed * fraction, player.SprintSpeed, "the carry pace allows a sprint.");
        }

        [Test]
        public void EscOrInteractPutsTheLoadDownButNotThroughAMenuOrOnTheLiftingPress()
        {
            Assert.IsTrue(LiftHands.PutsDown(true, false, true, false), "Esc did not put the load down.");
            Assert.IsFalse(LiftHands.PutsDown(true, false, false, false), "Esc closing the chat box also dropped the load.");
            Assert.IsTrue(LiftHands.PutsDown(false, true, true, false), "interact did not put the load down.");
            Assert.IsFalse(LiftHands.PutsDown(false, true, true, true), "the press that lifted it put it straight back down.");
            Assert.IsFalse(LiftHands.PutsDown(false, false, true, false), "no input put the load down.");
        }

        [Test]
        public void TheLoosePlantIsLiftableAndPosedOnlyByItsCarrier()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OxygenPlantRecoveryAuthoring.LoosePlantPath);
            Assert.IsNotNull(prefab.GetComponent<Liftable>(), "the loose plant is not liftable.");
            Assert.IsTrue(prefab.GetComponent<Liftable>().Shape.IsValid, "the loose plant has no grips or no foot.");
            Assert.IsNull(prefab.GetComponent<Unity.Netcode.Components.NetworkTransform>(),
                          "a NetworkTransform would fight the pose every machine derives from the carrier.");
            Assert.AreEqual(RigidbodyInterpolation.None, prefab.GetComponent<Rigidbody>().interpolation,
                            "an interpolating body puts the load back where it was each frame.");
            Assert.IsFalse(prefab.GetComponents<Component>().Any(c => c == null), "the loose plant carries a missing script.");

            var lift = new SerializedObject(prefab.GetComponent<Liftable>());
            Assert.IsNotNull(lift.FindProperty("liftAction").objectReferenceValue, "the lift plays no clip.");
            Assert.IsNotNull(lift.FindProperty("setDownAction").objectReferenceValue, "the put-down plays no clip.");
        }

        // ─────────────────────────── A captive ───────────────────────────

        private const string RaxyPath = "Assets/Game/Prefabs/agents/Characters/Raxy/Raxy_Job_Farmer.prefab";
        private const string NomadPath = "Assets/Game/Prefabs/agents/Characters/Nomad.prefab";
        private const string DuneRatPath = "Assets/Game/Prefabs/agents/creatures/DuneRat.prefab";

        /// <summary>Degrees below level the carrier may look and still see the bound feet: straight ahead, and walking with the eyes down.</summary>
        private static readonly float[] LooksAtTheFeet = { 0f, 20f };

        /// <summary>Degrees below level the carrier looks as they take hold of a body lying at their feet.</summary>
        private const float PickedUpLookingDown = 45f;

        [TestCase(RaxyPath)]
        [TestCase(NomadPath)]
        public void ATiedPersonIsFoldedOverTheShoulderWithTheFeetInView(string captivePath)
        {
            // The carry is any ILiftLoad: somebody tied up is heaved onto the RIGHT shoulder and folded over it — hips on
            // top of it, trunk hanging down the back, the bound legs bent out in front where the carrier sees the feet
            // without looking for them. Measured on real rigs, because every number in the pose is a humanoid muscle whose
            // sign was found by trying it — and the legs are fitted per body, so a Nomad's longer ones must land the same.
            (GameObject body, GameObject person, SpaceGame.Items.BoundBody bound, LiftCarrier carrier) = Shouldered(captivePath);
            Animator rig = person.GetComponentInChildren<Animator>(true);
            Camera eye = body.GetComponentInChildren<Camera>(true);
            Vector3 forward = body.transform.forward;

            Vector3 hips = rig.GetBoneTransform(HumanBodyBones.Hips).position;
            Vector3 head = rig.GetBoneTransform(HumanBodyBones.Head).position;
            Vector3 shoulder = carrier.Shoulder(right: true);
            Assert.Less(Vector3.ProjectOnPlane(hips - shoulder, Vector3.up).magnitude, 0.4f, "the hips are not over the carrier's shoulder.");
            Assert.Greater(hips.y, shoulder.y + 0.2f, "the hips sit in the carrier's shoulder rather than on top of it.");
            Assert.Less(head.y, shoulder.y - 0.15f, "the trunk is not hanging down the carrier's back.");
            Assert.Less(Vector3.Dot(head - body.transform.position, forward), -0.3f, "the head is not down the carrier's back.");

            foreach (HumanBodyBones hand in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                Assert.Less(rig.GetBoneTransform(hand).position.y, hips.y, $"the tied {hand} stands up past the hips.");

            // A hogtie's legs: the ankles bound together and the knees bent about a right angle.
            Vector3 leftFoot = rig.GetBoneTransform(HumanBodyBones.LeftFoot).position;
            Vector3 rightFoot = rig.GetBoneTransform(HumanBodyBones.RightFoot).position;
            Assert.Less(Vector3.Distance(leftFoot, rightFoot), 0.2f, "the bound ankles are splayed apart.");
            float knee = Vector3.Angle(rig.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position - rig.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position,
                                       leftFoot - rig.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position);
            Assert.That(knee > 60f && knee < 120f, $"the knees are not bent in a hogtie ({knee:0}°).");

            foreach (Vector3 at in new[] { leftFoot, rightFoot })
            {
                Assert.Less(at.y, eye.transform.position.y, "a foot stands up past the carrier's eye.");
                Assert.Greater(Vector3.Distance(at, eye.transform.position), 0.45f, "a foot is in the carrier's face.");
                Assert.Greater(Vector3.Dot(at - eye.transform.position, forward), 0.3f, "a foot is not out in front of the carrier.");

                foreach (float pitch in LooksAtTheFeet)
                {
                    eye.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                    Vector3 seen = eye.WorldToViewportPoint(at);
                    Assert.That(seen.z > eye.nearClipPlane && seen.x > 0f && seen.x < 1f && seen.y > 0f && seen.y < 1f,
                                $"the carrier cannot see the captive's feet looking {pitch}° down (viewport {seen}).");
                }
            }

            Assert.IsNotNull(bound.CarriedBy, "the body does not know it is carried.");
            Assert.IsFalse(bound.LyingCollider != null && bound.LyingCollider.gameObject.activeInHierarchy,
                           "the lying collider is still there to trip the carrier up.");
        }

        [TestCase(RaxyPath)]
        [TestCase(NomadPath)]
        public void NothingOfACarriedBodyStandsInTheCarriersEyeLineAndTheLegsAreNeverHidden(string captivePath)
        {
            // A Nomad's cape follows its spine, so with the trunk hanging down the back it stands up past the hips. Whatever
            // of a carried body stands above the eye line in the carrier's own view is hidden from that one camera; what is
            // left there must be nothing, and the mesh the feet are in, which is the point, must never be among the hidden.
            (GameObject body, GameObject person, _, _) = Shouldered(captivePath);
            var held = person.GetComponent<SpaceGame.Items.CaptiveHaul>();
            Camera eye = body.GetComponentInChildren<Camera>(true);
            var settings = new SpaceGame.Items.CaptiveHaulSettings();

            var baked = new Mesh();
            var vertices = new List<Vector3>();
            foreach (SkinnedMeshRenderer skin in person.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!skin.enabled || held.HiddenFromCarrierEye.Contains(skin)) continue;

                skin.BakeMesh(baked, true);
                baked.GetVertices(vertices);
                Transform at = skin.transform;
                foreach (Vector3 v in vertices)
                {
                    Vector3 seen = eye.WorldToViewportPoint(at.position + at.rotation * Vector3.Scale(v, at.lossyScale));
                    Assert.IsFalse(seen.z > eye.nearClipPlane && seen.x >= 0f && seen.x <= 1f && seen.y >= settings.EyeLine && seen.y <= 1f,
                                   $"'{skin.name}' stands in the carrier's eye line and is not hidden from it.");
                }
            }
            Object.DestroyImmediate(baked);

            Transform foot = person.GetComponentInChildren<Animator>(true).GetBoneTransform(HumanBodyBones.LeftFoot);
            Assert.IsFalse(person.GetComponentsInChildren<SkinnedMeshRenderer>()
                                 .Where(skin => skin.bones.Contains(foot))
                                 .All(skin => held.HiddenFromCarrierEye.Contains(skin)),
                           "every mesh with a foot in it was hidden, so the carrier cannot see the feet.");
        }

        [Test]
        public void ATiedCreatureIsCarriedAcrossTheShouldersBehindTheHead()
        {
            // No humanoid avatar: across both shoulders the way a sheep is carried, its long axis across them and its middle
            // behind the eye, rather than draped down one side of the face.
            (GameObject body, GameObject creature, SpaceGame.Items.BoundBody bound, _) = Shouldered(DuneRatPath);
            Bounds extent = SpaceGame.Items.TiedPose.BodyInLocalSpace(creature.transform);
            Vector3 longAxis = creature.transform.rotation * (extent.size.x >= extent.size.z ? Vector3.right : Vector3.forward);
            Camera eye = body.GetComponentInChildren<Camera>(true);

            Assert.IsTrue(bound.Pose.CarriedAcross);
            Assert.Less(Mathf.Abs(Vector3.Dot(longAxis, body.transform.forward)), 0.1f, "the creature lies fore and aft, along the carrier's view.");
            Assert.Less(Vector3.Dot(creature.transform.TransformPoint(bound.CarryAnchor) - eye.transform.position, body.transform.forward), 0f,
                        "the creature's middle is in front of the carrier's eye.");
        }

        [Test]
        public void ACaptiveThrownOffTheShoulderArcsOntoTheGroundAheadAndTouchesTheCarrierAgain()
        {
            (GameObject body, GameObject person, SpaceGame.Items.BoundBody bound, LiftCarrier carrier) = Shouldered(RaxyPath);
            Collider theirs = body.GetComponentsInChildren<Collider>().First(c => !c.isTrigger && c.enabled);
            Assert.IsFalse(bound.LyingCollider.gameObject.activeInHierarchy, "a carried body still has its lying collider.");

            var held = person.GetComponent<SpaceGame.Items.CaptiveHaul>();
            var settings = new SpaceGame.Items.CaptiveHaulSettings();
            Pose rest = carrier.RestFromHere();
            Vector3 from = person.transform.position;
            held.ThrowOff(carrier);

            // Tossed, not lowered: somewhere in the flight the root rides well above the straight line to where it lands.
            float above = 0f;
            const float step = 0.02f;
            int steps = Mathf.CeilToInt(settings.ThrowSeconds / step) + 2;
            for (int i = 0; i < steps; i++)
            {
                carrier.Follow(step);
                bound.Pose.Apply(0f);
                Vector3 at = person.transform.position;
                float along = Mathf.InverseLerp(0f, LiftPoseSolver.Flat(rest.position - from).magnitude, LiftPoseSolver.Flat(at - from).magnitude);
                above = Mathf.Max(above, at.y - Mathf.Lerp(from.y, rest.position.y, along));
            }

            Assert.Greater(above, settings.ThrowArc * 0.5f, "the captive is lowered in a straight line, not thrown off the shoulder.");
            Assert.IsNull(carrier.Load, "the carrier is still holding the captive.");
            Assert.IsNull(bound.CarriedBy, "the body still thinks it is carried.");
            Assert.Less(Vector3.Distance(person.transform.position, rest.position), 0.01f, "the captive did not land where it was thrown.");
            Assert.Greater(Vector3.Dot(rest.position - body.transform.position, body.transform.right), 0.5f, "the captive was thrown onto the carrier's feet.");
            Assert.Greater(Vector3.Dot(rest.position - body.transform.position, body.transform.forward), 0.5f, "the captive was not thrown ahead.");
            Assert.AreEqual(Stage, rest.position.y + bound.Pose.GroundBelowRoot, 0.05f, "the captive was put down off the ground.");
            Assert.IsTrue(bound.LyingCollider.gameObject.activeInHierarchy, "the lying collider did not come back.");
            Assert.IsFalse(Physics.GetIgnoreCollision(bound.LyingCollider, theirs), "the put-down body passes through its carrier.");

            person.GetComponent<SpaceGame.Gameplay.Status.StatusReceiver>().Clear(SpaceGame.Gameplay.Status.StatusKind.Bound);
            Assert.IsFalse(((ILiftLoad)person.GetComponent<SpaceGame.Items.CaptiveHaul>()).CanBeHeld, "an untied person can still be carried.");
        }

        /// <summary>The player standing on the stage with <paramref name="captivePath"/>, tied, already up on their shoulder.</summary>
        private (GameObject body, GameObject person, SpaceGame.Items.BoundBody bound, LiftCarrier carrier) Shouldered(string captivePath)
        {
            MakeGround();
            var body = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));
            body.hideFlags = HideFlags.DontSave;
            CapsuleCollider capsule = body.GetComponentsInChildren<CapsuleCollider>(true).First(c => !c.isTrigger);
            float rootAboveFeet = -capsule.transform.TransformPoint(capsule.center - Vector3.up * capsule.height * 0.5f).y;
            body.transform.SetPositionAndRotation(new Vector3(0f, Stage + rootAboveFeet, 0f), Quaternion.identity);
            made.Add(body);
            Animator animator = body.GetComponentInChildren<Animator>(true);
            animator.Rebind();
            animator.Update(0f);

            var person = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(captivePath));
            person.hideFlags = HideFlags.DontSave;
            person.transform.SetPositionAndRotation(new Vector3(0.5f, Stage, 1.5f), Quaternion.identity);
            made.Add(person);
            Animator rig = person.GetComponentInChildren<Animator>(true);
            rig.Rebind();
            rig.Update(0f);
            Physics.SyncTransforms();

            var status = person.GetComponent<SpaceGame.Gameplay.Status.StatusReceiver>();
            if (status == null) status = person.AddComponent<SpaceGame.Gameplay.Status.StatusReceiver>();
            SpaceGame.Items.BoundBody.EnsureOn(status, new SpaceGame.Items.HogtieWrapLook(), new SpaceGame.Items.TiedPoseLook(),
                                               haul: new SpaceGame.Items.CaptiveHaulSettings());
            ILiftLoad captive = person.GetComponent<SpaceGame.Items.CaptiveHaul>();
            Assert.IsNotNull(captive, "a tied body has nothing to carry it by.");
            Assert.IsFalse(captive.CanBeHeld, "somebody nobody has tied can be carried.");

            status.Apply(SpaceGame.Gameplay.Status.StatusKind.Bound, 120f);
            Assert.IsTrue(captive.CanBeHeld, "a tied person cannot be carried.");
            Assert.IsTrue(person.GetComponent<SpaceGame.Items.CaptiveHaul>().FitsOnShoulderOf(body), "the captive is too big for the player.");

            var bound = person.GetComponent<SpaceGame.Items.BoundBody>();
            LiftCarrier carrier = LiftCarrier.On(body);

            // Picked up looking down at the body on the ground, the way it is in play: what is hidden from the eye must be
            // measured looking ahead, or the feet stand above the eye line and the whole body goes with them.
            Transform eye = body.GetComponentInChildren<Camera>(true).transform;
            eye.localRotation = Quaternion.Euler(PickedUpLookingDown, 0f, 0f);
            Assert.IsTrue(carrier.Lift(captive, 1f, alreadyDone: 1f), "the player rig cannot take hold of a captive.");

            // The carrier places the root (LateUpdate 200), then BoundBody poses the body on it (1000): the same order by hand.
            for (int i = 0; i < 4; i++)
            {
                carrier.Follow(0.02f);
                bound.Pose.Apply(0f);
            }

            eye.localRotation = Quaternion.identity;
            Assert.IsTrue(carrier.IsCarrying);
            return (body, person, bound, carrier);
        }

        // ─────────────────────────── Fixture ───────────────────────────

        private void MakeGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.hideFlags = HideFlags.DontSave;
            ground.transform.position = new Vector3(0f, Stage - 0.5f, 0f);
            ground.transform.localScale = new Vector3(200f, 1f, 200f);
            made.Add(ground);
        }

        /// <summary>The player standing at the plant's handle end, facing along it, with the plant already lifted.</summary>
        private (GameObject body, Liftable plant, LiftCarrier carrier) Lifted()
        {
            var plantObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OxygenPlantRecoveryAuthoring.LoosePlantPath));
            plantObject.hideFlags = HideFlags.DontSave;
            plantObject.transform.position = new Vector3(0f, Stage, 0f);
            made.Add(plantObject);
            Liftable plant = plantObject.GetComponent<Liftable>();

            var body = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));
            body.hideFlags = HideFlags.DontSave;
            Vector3 grips = plant.GripPoint;

            // The player's root is not at its feet: its 3 m capsule reaches a metre below it, which is where the ground is.
            CapsuleCollider capsule = body.GetComponentsInChildren<CapsuleCollider>(true).First(c => !c.isTrigger);
            float rootAboveFeet = -capsule.transform.TransformPoint(capsule.center - Vector3.up * capsule.height * 0.5f).y;
            body.transform.SetPositionAndRotation(new Vector3(grips.x, Stage + rootAboveFeet, grips.z - 0.6f), Quaternion.identity);
            made.Add(body);

            // The idle pose, so the shoulders are where they stand in play rather than in the asset's bind pose.
            Animator animator = body.GetComponentInChildren<Animator>(true);
            animator.Rebind();
            animator.Update(0f);
            Physics.SyncTransforms();

            LiftCarrier carrier = LiftCarrier.On(body);
            Assert.IsTrue(carrier.Lift(plant, 1f, alreadyDone: 1f), "the player rig cannot lift the plant.");
            for (int i = 0; i < 4; i++) carrier.Follow(0.02f);
            return (body, plant, carrier);
        }

        private static Vector3 Palm(GameObject body, HumanBodyBones bone)
        {
            Animator animator = body.GetComponentInChildren<Animator>(true);
            Transform hand = animator.GetBoneTransform(bone);
            return hand.TransformPoint(SpaceGame.Items.HandGripFrame.Derive(animator, hand, bone == HumanBodyBones.RightHand).LocalPosition);
        }

        // The grip on the body's left or right, as the carrier pairs them.
        private static Vector3 Nearest(GameObject body, Liftable plant, bool right)
        {
            Vector3 a = plant.GripLeft.position, b = plant.GripRight.position;
            bool aIsRight = Vector3.Dot(a - b, body.transform.right) > 0f;
            return right == aIsRight ? a : b;
        }
    }
}
