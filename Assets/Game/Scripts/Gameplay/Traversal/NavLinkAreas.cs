// The NavMesh areas that off-mesh links are tagged with, by name.
//
// Area IDs are slots in ProjectSettings/NavMeshAreas.asset, so a link author asks for the ID by
// name instead of hardcoding a slot. "Jump" is Unity's built-in slot 2: a link tagged with it is
// crossed as a leap by NavMeshAgentMotor (a gap, a drop). "Ladder" is the project's own slot 3: a
// Ladder registers its link under it, and the motor climbs rather than walks it.
//
// Anything that can only walk the ground -- a legged rig that follows path corners itself -- plans
// with GroundMask, or its path would lead it onto a link it cannot cross.
using UnityEngine.AI;

namespace SpaceGame.Gameplay
{
    public static class NavLinkAreas
    {
        public const string JumpName = "Jump";
        public const string LadderName = "Ladder";

        /// <summary>The Jump area's ID, or -1 if the project has no area of that name.</summary>
        public static int Jump => NavMesh.GetAreaFromName(JumpName);

        /// <summary>The Ladder area's ID, or -1 if the project has no area of that name.</summary>
        public static int Ladder => NavMesh.GetAreaFromName(LadderName);

        /// <summary>Every area except the link areas: what a walker that cannot climb or jump may plan over.</summary>
        public static int GroundMask => Without(Without(NavMesh.AllAreas, Jump), Ladder);

        /// <summary><paramref name="mask"/> with <paramref name="area"/> removed. An unknown area (-1) removes nothing.</summary>
        public static int Without(int mask, int area) => area < 0 ? mask : mask & ~(1 << area);
    }
}
