using System.Linq;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// How much a clip turns a skeleton, measured by sampling it onto an instance of the model.
    /// Shared by the creature builder (is this candidate a held pose?) and the animation review
    /// window (does this loop pop at its seam?).
    /// </summary>
    public static class ClipMotion
    {
        /// <summary>Below this many degrees between two samples, no bone has visibly moved.</summary>
        public const float StillDegrees = 0.5f;

        /// <summary>Every bone's local rotation after sampling <paramref name="clip"/> at <paramref name="time"/>.</summary>
        public static Quaternion[] Pose(AnimationClip clip, GameObject model, Transform[] bones, float time)
        {
            clip.SampleAnimation(model, time);
            return bones.Select(t => t.localRotation).ToArray();
        }

        /// <summary>The largest angle, in degrees, any one bone turns between two poses.</summary>
        public static float MaxTurn(Quaternion[] a, Quaternion[] b) =>
            a.Select((rotation, i) => Quaternion.Angle(rotation, b[i])).DefaultIfEmpty(0f).Max();

        /// <summary>The largest angle any bone of <paramref name="model"/> turns between two times of the clip.</summary>
        public static float MaxBoneTurn(AnimationClip clip, GameObject model, float from, float to)
        {
            var bones = model.GetComponentsInChildren<Transform>();
            return MaxTurn(Pose(clip, model, bones, from), Pose(clip, model, bones, to));
        }
    }
}
