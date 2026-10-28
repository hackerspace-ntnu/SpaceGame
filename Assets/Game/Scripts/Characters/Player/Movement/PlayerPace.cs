// How fast a player walks, for everything that has to keep pace with one.
//
// An NPC leading the crew somewhere, or just going about its business, used to walk at its own
// prefab pace (2.4 m/s for a Raxy) while a player walks at 6, so following anybody meant stopping
// every few steps. Goal travel now asks for this pace instead (GoalTravelModule → MoveIntent.WalkSpeed).
//
// Reported by every PlayerMovement as it wakes, so the number is the player prefab's own walk and
// there is no second copy of it to drift. Zero until a player exists: callers then keep their own pace.
using UnityEngine;

namespace SpaceGame.Characters
{
    public static class PlayerPace
    {
        /// <summary>A player's ordinary walk, in m/s; 0 while no player has woken yet.</summary>
        public static float WalkSpeed { get; private set; }

        internal static void Report(float walkSpeed) => WalkSpeed = Mathf.Max(WalkSpeed, walkSpeed);

        // Enter Play Mode without a domain reload keeps statics; start every session from nothing.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad() => WalkSpeed = 0f;
    }
}
