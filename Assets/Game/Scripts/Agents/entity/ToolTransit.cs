// The picture of a tool travelling between a hand and its place on the belt.
//
// Drawing or stowing is instant as state: the hand holds the item or it does not, from the frame the slot changes, so nothing that
// reads the hand (the gesture gate, the hold pose) ever waits on a tween. What travels is only the visual: the item's new
// instance (the one on the belt after a stow, the one in the hand after a draw) starts where the old one was and is carried to
// where it belongs over a short time, so it is seen to go instead of vanishing from one place and appearing in another.
//
// Both ends are bone-parented and move with the body, so each end is held as a pose in its own bone's space and re-read every
// frame; the tool lands exactly where the socket seated it, and its own local pose is untouched at the end.
using UnityEngine;

namespace SpaceGame.Agents
{
    public sealed class ToolTransit
    {
        /// <summary>A pose in the local space of a bone, so it follows the bone as the body moves.</summary>
        public readonly struct Anchored
        {
            public readonly Transform Space;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;

            public Anchored(Transform space, Vector3 position, Quaternion rotation)
            {
                Space = space;
                Position = position;
                Rotation = rotation;
            }

            /// <summary>Where <paramref name="item"/> is, as seen from <paramref name="space"/>.</summary>
            public static Anchored Of(Transform space, Transform item) =>
                new(space, space.InverseTransformPoint(item.position), Quaternion.Inverse(space.rotation) * item.rotation);

            public bool IsLive => Space != null;

            public Vector3 WorldPosition => Space.TransformPoint(Position);

            public Quaternion WorldRotation => Space.rotation * Rotation;
        }

        private readonly Transform visual;
        private readonly Anchored from;
        private readonly Vector3 restLocalPosition;
        private readonly Quaternion restLocalRotation;
        private readonly float seconds;
        private float elapsed;

        /// <summary>Starts <paramref name="visual"/> at <paramref name="from"/>; it ends where it is now, in its parent's space.</summary>
        public ToolTransit(Transform visual, Anchored from, float seconds)
        {
            this.visual = visual;
            this.from = from;
            this.seconds = seconds;
            restLocalPosition = visual.localPosition;
            restLocalRotation = visual.localRotation;
        }

        /// <summary>How far along a transit of <paramref name="seconds"/> is after <paramref name="elapsed"/>: eased, 0 to 1. A transit of no time is over.</summary>
        public static float Progress(float elapsed, float seconds) =>
            seconds <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / seconds));

        /// <summary>
        /// Moves the visual a step along; call after the animator has posed the body. False once it has arrived, or when its tool or
        /// either bone is gone, and then the visual stands exactly where its socket seated it.
        /// </summary>
        public bool Tick(float deltaTime)
        {
            if (visual == null) return false;

            elapsed += deltaTime;
            Transform parent = visual.parent;
            if (elapsed >= seconds || !from.IsLive || parent == null)
            {
                visual.SetLocalPositionAndRotation(restLocalPosition, restLocalRotation);
                return false;
            }

            float t = Progress(elapsed, seconds);
            Vector3 restWorld = parent.TransformPoint(restLocalPosition);
            Quaternion restWorldRotation = parent.rotation * restLocalRotation;
            visual.SetPositionAndRotation(Vector3.Lerp(from.WorldPosition, restWorld, t),
                                          Quaternion.Slerp(from.WorldRotation, restWorldRotation, t));
            return true;
        }
    }
}
