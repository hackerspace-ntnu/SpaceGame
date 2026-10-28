// A story character sent to a spot and held there, facing the way the spot faces: a Shellback at the workshop gate, the
// ranch's wrangler at the corral. Only the travel (through the character's AgentGoal) and quieting the stock modules that
// would fight it are written here; who decides where it stands (a mission's phase, a quest's step) stays with the caller.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Agents
{
    public static class ScriptedStand
    {
        /// <summary>How far ahead of a spot its facing point lies, metres.</summary>
        private const float FaceDistance = 5f;

        /// <summary>
        /// SERVER: walk (or run) to <paramref name="spot"/> and hold there, facing along it. False when the spot has no
        /// NavMesh near it, and the goal is left as it was.
        /// </summary>
        public static bool Stand(AgentGoal goal, Pose spot, float arriveRadius, string reason, bool run = false) =>
            goal != null && goal.TrySetSampled(spot.position, arriveRadius, reason, hold: true, facePoint: FacePoint(spot),
                                               run: run);

        /// <summary>SERVER: straight to <paramref name="spot"/>, where no walk leads or the story moved on in a reload.</summary>
        public static void PutAt(GameObject body, AgentGoal goal, Pose spot, float arriveRadius, string reason)
        {
            NetworkedTeleport.Move(body, spot.position, spot.rotation);
            Stand(goal, spot, arriveRadius, reason);
        }

        /// <summary>
        /// Switch off the stock modules a story character's own walks and lines would fight: the watcher freezes a walk
        /// whenever a player is near, and idle glances and small talk talk over the story.
        /// </summary>
        public static void QuietStockModules(GameObject body)
        {
            foreach (WatchModule watch in body.GetComponentsInChildren<WatchModule>(true)) watch.enabled = false;
            foreach (IdleLookAroundModule look in body.GetComponentsInChildren<IdleLookAroundModule>(true)) look.enabled = false;
            foreach (ChatterModule chatter in body.GetComponentsInChildren<ChatterModule>(true)) chatter.enabled = false;
        }

        private static Vector3 FacePoint(Pose spot) => spot.position + spot.forward * FaceDistance;
    }
}
