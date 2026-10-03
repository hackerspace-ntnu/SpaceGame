// NPCs on ladders and jump links: a Ladder registers an off-mesh link and NavMeshAgentMotor climbs it;
// a link in the Jump area is crossed as a leap.
//
// A NavMeshAgent does not move in edit mode, so the walk itself is run in play mode: the test enters
// it, builds two decks and a ladder out of boxes, and ticks a motor at a fixed 60 Hz (an unfocused
// editor otherwise plays at 3-5 fps, and agents overshoot at that rate). Everything is built AFTER
// entering play mode, so a domain reload on the way in has nothing to lose.
//
// In Editor/ rather than beside the other EditMode tests because it touches Assembly-CSharp types.
using System.Collections;
using NUnit.Framework;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.World;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace SpaceGame.EditorTools
{
    public class LadderTraversalTests
    {
        private const float Step = 1f / 60f;
        private const int MaxFrames = 60 * 60;
        private const float ArriveWithin = 0.6f;
        private const float DeckHeight = 4f;
        private const float OffsetFarAway = 5000f;

        private GameObject world;
        private NavMeshDataInstance meshInstance;
        private NavMeshLinkInstance jumpLink;
        private bool sawLeap;

        // A 30 m floor with its top at y = 0, and a deck 4 m up over its far half (z 6 to 14). The ladder stands at
        // z = 5, a metre short of the deck's edge; a climber stands on the -Z side of it.
        private static readonly Vector3 Origin = new Vector3(OffsetFarAway, 0f, OffsetFarAway);
        private static readonly Vector3 Spawn = Origin + new Vector3(0f, 0f, -3f);
        private static readonly Vector3 DeckGoal = Origin + new Vector3(0f, DeckHeight, 11f);

        [UnityTest]
        public IEnumerator AnAgentClimbsToTheUpperDeckAndBackDown()
        {
            yield return new EnterPlayMode();

            Time.captureDeltaTime = Step;
            BuildWorld();
            yield return null;

            GameObject npc = SpawnAgent(out NavMeshAgentMotor motor);
            yield return null;

            Assert.IsTrue(NavMeshReach.CanWalk(Spawn, DeckGoal), "the ladder's link joins the two decks");

            yield return Walk(npc, motor, DeckGoal);
            Assert.AreEqual(DeckHeight, npc.transform.position.y, ArriveWithin, "reached the upper deck");

            yield return Walk(npc, motor, Spawn);
            Assert.AreEqual(0f, npc.transform.position.y, ArriveWithin, "came back down to the floor");

            Cleanup(npc);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator AnAgentLeapsAcrossAJumpLinkAndBack()
        {
            yield return new EnterPlayMode();

            Time.captureDeltaTime = Step;
            BuildWorld(withLadder: false);
            // Registered the way a baker would: by area, with no component and no owner, and with
            // both ends snapped onto the mesh.
            NavMesh.SamplePosition(Origin + new Vector3(0f, 0f, 4f), out NavMeshHit lower, 1f, NavMesh.AllAreas);
            NavMesh.SamplePosition(Origin + new Vector3(0f, DeckHeight, 7f), out NavMeshHit upper, 1f, NavMesh.AllAreas);
            jumpLink = NavMesh.AddLink(new NavMeshLinkData
            {
                startPosition = lower.position,
                endPosition = upper.position,
                bidirectional = true,
                area = NavLinkAreas.Jump,
                costModifier = -1f,
            });
            yield return null;

            GameObject npc = SpawnAgent(out NavMeshAgentMotor motor);
            yield return null;

            yield return Walk(npc, motor, DeckGoal);
            Assert.AreEqual(DeckHeight, npc.transform.position.y, ArriveWithin, "reached the upper deck");
            Assert.IsTrue(sawLeap, "by leaping across the link");

            yield return Walk(npc, motor, Spawn);
            Assert.AreEqual(0f, npc.transform.position.y, ArriveWithin, "jumped back down to the floor");

            NavMesh.RemoveLink(jumpLink);
            Cleanup(npc);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator AWalkerThatCannotClimbDoesNotPlanOverTheLadder()
        {
            yield return new EnterPlayMode();

            Time.captureDeltaTime = Step;
            BuildWorld();
            yield return null;

            var path = new NavMeshPath();
            NavMesh.CalculatePath(Spawn, DeckGoal, NavLinkAreas.GroundMask, path);

            Assert.AreNotEqual(NavMeshPathStatus.PathComplete, path.status,
                "a ground-only query cannot reach the deck");

            Cleanup(null);
            yield return new ExitPlayMode();
        }

        private void BuildWorld(bool withLadder = true)
        {
            world = new GameObject("LadderTraversalWorld");

            var sources = new System.Collections.Generic.List<NavMeshBuildSource>
            {
                BoxSource(Origin + new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f)),
                BoxSource(Origin + new Vector3(0f, DeckHeight - 0.25f, 10f), new Vector3(8f, 0.5f, 8f)),
            };
            foreach (NavMeshBuildSource source in sources)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.transform.SetParent(world.transform, true);
                box.transform.position = source.transform.GetPosition();
                box.transform.localScale = source.size;
            }

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            // Relative to the data's position, which is where it is placed in the world.
            var bounds = new Bounds(Vector3.zero, new Vector3(60f, 30f, 60f));
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Origin, Quaternion.identity);
            meshInstance = NavMesh.AddNavMeshData(data);

            if (!withLadder) return;

            var ladderObject = new GameObject("Ladder");
            ladderObject.transform.SetParent(world.transform, false);
            ladderObject.transform.position = Origin + new Vector3(0f, 0f, 5f);
            ladderObject.SetActive(false);
            Transform top = new GameObject("Top").transform;
            top.SetParent(ladderObject.transform, false);
            top.position = Origin + new Vector3(0f, DeckHeight, 5f);
            Transform exit = new GameObject("Exit").transform;
            exit.SetParent(ladderObject.transform, false);
            exit.position = Origin + new Vector3(0f, DeckHeight, 6.6f);
            ladderObject.AddComponent<Ladder>().Configure(top, exit);
            ladderObject.SetActive(true);
        }

        private static NavMeshBuildSource BoxSource(Vector3 centre, Vector3 size) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = size,
            transform = Matrix4x4.TRS(centre, Quaternion.identity, Vector3.one),
            area = 0,
        };

        // Inactive while it is assembled, so the motor's Awake sees a NavMesh underneath it.
        private GameObject SpawnAgent(out NavMeshAgentMotor motor)
        {
            var npc = new GameObject("Climber");
            npc.SetActive(false);
            npc.transform.SetParent(world.transform, false);
            npc.transform.position = Spawn;
            var nav = npc.AddComponent<NavMeshAgent>();
            nav.radius = 0.5f;
            nav.height = 2f;
            motor = npc.AddComponent<NavMeshAgentMotor>();
            npc.SetActive(true);
            return npc;
        }

        private IEnumerator Walk(GameObject npc, NavMeshAgentMotor motor, Vector3 goal)
        {
            for (int frame = 0; frame < MaxFrames; frame++)
            {
                motor.Tick(MoveIntent.MoveTo(goal, 0.3f), Step);
                yield return null;
                sawLeap |= motor.IsLeaping;
                if (Vector3.Distance(npc.transform.position, goal) < ArriveWithin) yield break;
            }
        }

        private void Cleanup(GameObject npc)
        {
            Time.captureDeltaTime = 0f;
            if (npc != null) Object.Destroy(npc);
            meshInstance.Remove();
            Object.Destroy(world);
        }
    }
}
