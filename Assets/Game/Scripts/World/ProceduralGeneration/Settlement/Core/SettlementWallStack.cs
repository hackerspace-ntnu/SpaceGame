// How a terrace wall made of stacked pieces is put together: how many courses go under each top
// piece to reach the ground in front of it, and which joints along a run of blocks get a pillar.
// Pure rules; SettlementStreetPaver places the pieces.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public static class SettlementWallStack
    {
        // A drop this close above a whole number of courses does not need another one.
        private const float CourseTolerance = 0.05f;

        /// <summary>
        /// Courses under a top piece so the stack reaches at least <paramref name="drop"/> metres down,
        /// rounded up so the bottom course's footing is buried. <paramref name="tooTall"/> when the drop
        /// needs more than <paramref name="maxCourses"/>; the result is then capped.
        /// </summary>
        public static int CourseCount(float drop, float stepHeight, int maxCourses, out bool tooTall)
        {
            int needed = Mathf.Max(0, Mathf.CeilToInt((drop - CourseTolerance) / stepHeight) - 1);
            tooTall = needed > maxCourses;
            return Mathf.Min(needed, maxCourses);
        }

        /// <summary>
        /// Joints of a run of <c>blockYaws.Count</c> blocks that get a pillar: joint i is the start of
        /// block i, joint Count the end of the last. Both ends always do; a joint between does once
        /// <paramref name="every"/> blocks have passed since the last pillar, or once the wall has turned
        /// more than <paramref name="turn"/> degrees since it.
        /// </summary>
        public static List<int> PillarJoints(IReadOnlyList<float> blockYaws, int every, float turn)
        {
            var joints = new List<int>();
            if (blockYaws.Count == 0) return joints;
            joints.Add(0);
            float turned = 0f;
            int since = 0;
            for (int j = 1; j < blockYaws.Count; j++)
            {
                turned += Mathf.Abs(Mathf.DeltaAngle(blockYaws[j - 1], blockYaws[j]));
                since++;
                if (since < every && turned <= turn) continue;
                joints.Add(j);
                turned = 0f;
                since = 0;
            }
            joints.Add(blockYaws.Count);
            return joints;
        }
    }
}
