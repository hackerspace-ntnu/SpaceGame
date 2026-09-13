// The assembled agent, on real colliders.
//
// Runs in EditMode rather than PlayMode because everything needed is available without a running
// player loop -- AgentGroundConform.Initialise and .Conform are public and dt-driven for exactly
// this reason, and Unity's raycasts work against colliders in an edit-mode scene once the
// transforms are synced. Same shape as SpiderWalkerGroundingTests.
//
// The loop in each test does the NavMeshAgent's job by hand: the agent is what turns baseOffset
// into a transform position, and without it the conform would measure its own output and run away.
// Closing the loop here is also the point -- it proves the correction SETTLES rather than
// oscillating.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class AgentGroundConformTests
    {
        private const string GolemPath = "Assets/Game/Prefabs/agents/creatures/Golem.prefab";
        private const string NomadPath = "Assets/Game/Prefabs/agents/Characters/Nomad.prefab";

        /// The measured median error: the NavMesh floats a quarter of a metre over the sand.
        private const float NavMeshError = 0.257f;

        private GameObject world;
        private GameObject agent;

        [SetUp]
        public void SetUp()
        {
            world = new GameObject("TestWorld");
            Slab(new Vector3(0f, -1f, 0f), new Vector3(400f, 2f, 400f), Quaternion.identity);
            Slab(new Vector3(60f, 3.5f, 0f), new Vector3(60f, 2f, 200f), Quaternion.Euler(0f, 0f, -15f));
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (agent != null) Object.DestroyImmediate(agent);
            if (world != null) Object.DestroyImmediate(world);
        }

        private void Slab(Vector3 centre, Vector3 size, Quaternion rotation)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(world.transform);
            go.transform.SetPositionAndRotation(centre, rotation);
            go.transform.localScale = size;
        }

        private AgentGroundConform Spawn(string prefabPath, Vector3 navMeshPosition)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, "prefab missing: " + prefabPath);

            agent = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            agent.transform.position = navMeshPosition;

            var conform = agent.GetComponent<AgentGroundConform>();
            Assert.IsNotNull(conform, prefabPath + " has no AgentGroundConform. " +
                                      "Run Tools > SpaceGame > Agents > Wire Ground Conform.");
            conform.Initialise();
            Physics.SyncTransforms();
            return conform;
        }

        /// <summary>Steps the conform and does the NavMeshAgent's job of applying baseOffset.</summary>
        private void Settle(AgentGroundConform conform, Vector3 navMeshPosition, int frames = 120)
        {
            var navAgent = agent.GetComponent<NavMeshAgent>();
            for (int i = 0; i < frames; i++)
            {
                conform.Conform(1f / 60f);
                agent.transform.position = navMeshPosition + Vector3.up * navAgent.baseOffset;
                Physics.SyncTransforms();
            }
        }

        [Test]
        public void TheBodyEndsUpOnTheSlabRatherThanAboveIt()
        {
            var navMeshPosition = new Vector3(0f, 0f + NavMeshError, 0f);   // slab top is y = 0
            var conform = Spawn(GolemPath, navMeshPosition);

            Settle(conform, navMeshPosition);

            Assert.AreEqual(0f, agent.transform.position.y, 0.02f,
                            "the Golem should be standing on the slab, not floating over it");
        }

        [Test]
        public void AHumanoidIsGroundedToo()
        {
            var navMeshPosition = new Vector3(0f, 0f + NavMeshError, 5f);
            var conform = Spawn(NomadPath, navMeshPosition);

            Settle(conform, navMeshPosition);

            Assert.AreEqual(0f, agent.transform.position.y, 0.02f);
        }

        /// <summary>
        /// The node the lean is written to.
        ///
        /// Asked of the component rather than re-derived here. An earlier version of this helper
        /// repeated the resolution and repeated it incompletely, so it reported "no visual root"
        /// on the Golem -- a rig with no SkinnedMeshRenderer at all -- while the component itself
        /// was resolving one perfectly well. A test that re-implements what it is testing fails
        /// for its own reasons.
        ///
        /// The authored rotation is NOT identity on every rig -- a Blender bone root usually
        /// carries a -90 degree X -- so every assertion here measures the lean as a change from
        /// the rest pose, never as an angle against world up.
        /// </summary>
        private static Transform BodyRoot(AgentGroundConform conform)
        {
            Assert.IsNotNull(conform.BodyRoot,
                             "no visual root resolved under " + conform.name +
                             ", so this agent would be grounded but never lean");
            return conform.BodyRoot;
        }

        /// <summary>
        /// A body that ignores the slope stands bolt upright on a hillside and reads as pasted on.
        ///
        /// The ramp is 15 degrees and the Golem is a biped (`Bone_Thigh/Shin/Foot_L/R`, two legs
        /// and no others), so it is wired at <c>BipedSlopeFollow</c> = 0.35 and should lean about
        /// 5. The bounds are wide enough to survive re-tuning that value and narrow enough that
        /// "not leaning at all" and "lying flat on the hillside" both still fail.
        /// </summary>
        [Test]
        public void TheBodyLeansIntoTheSlope()
        {
            // Top surface of the ramp slab at x = 60, allowing for its 15-degree tilt.
            var navMeshPosition = new Vector3(60f, 4.5f + NavMeshError, 0f);
            var conform = Spawn(GolemPath, navMeshPosition);

            Transform body = BodyRoot(conform);
            Quaternion rest = body.localRotation;

            Settle(conform, navMeshPosition);

            float lean = Quaternion.Angle(rest, body.localRotation);
            Assert.Greater(lean, 3f, "the body did not follow the slope at all");
            Assert.Less(lean, 9f, "the body over-leaned past the slope it is standing on");
        }

        [Test]
        public void OnFlatGroundTheBodyDoesNotLean()
        {
            var navMeshPosition = new Vector3(0f, NavMeshError, 10f);
            var conform = Spawn(GolemPath, navMeshPosition);

            Transform body = BodyRoot(conform);
            Quaternion rest = body.localRotation;

            Settle(conform, navMeshPosition);

            Assert.AreEqual(0f, Quaternion.Angle(rest, body.localRotation), 1f,
                            "a body on a flat slab must keep its authored pose");
        }

        /// <summary>
        /// The compounding failure, caught end to end. On the Nomad nothing animates the node the
        /// lean is written to, so a naive implementation reads its own output back and multiplies
        /// the lean in again every frame until the body is spinning. Ten seconds of frames is long
        /// enough that any accumulation is unmissable.
        /// </summary>
        [Test]
        public void TheTiltDoesNotAccumulateOnANodeNothingElseDrives()
        {
            var navMeshPosition = new Vector3(60f, 4.5f + NavMeshError, 6f);
            var conform = Spawn(NomadPath, navMeshPosition);

            Transform body = agent.transform.Find("Model");
            Assert.IsNotNull(body, "the Nomad's visual root is the Model child");
            Quaternion rest = body.localRotation;

            Settle(conform, navMeshPosition, frames: 600);

            Assert.Less(Quaternion.Angle(rest, body.localRotation), 18f,
                        "the lean accumulated instead of settling");
        }

        /// <summary>
        /// Over a hole the probe finds nothing, and holding the last correction would hang the body
        /// at the height of ground it is no longer above.
        /// </summary>
        [Test]
        public void WithNoGroundBelowItTheBodyReturnsToWhereNavigationPutIt()
        {
            var navMeshPosition = new Vector3(0f, NavMeshError, 0f);
            var conform = Spawn(GolemPath, navMeshPosition);
            Settle(conform, navMeshPosition);

            // Step off the world entirely.
            var overNothing = new Vector3(5000f, 50f, 5000f);
            Settle(conform, overNothing, frames: 240);

            Assert.AreEqual(overNothing.y, agent.transform.position.y, 0.02f);
        }
    }
}
