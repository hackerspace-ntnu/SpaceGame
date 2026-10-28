using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Parts the grass around whatever it is on — a player, a creature, a cart wheel — leaves a
    /// trail of flattened grass behind it (<see cref="GrassTrample"/>) and rustles the bushes it
    /// pushes through. Purely a picture: each machine bends its own grass around the bodies it can
    /// see, so nothing here is networked, and trampled grass stands back up rather than being saved.
    ///
    /// Only the <see cref="MaxActive"/> benders nearest the camera are used; the shader has a
    /// fixed-size array.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class GrassBender : MonoBehaviour
    {
        public const int MaxActive = 8;

        [Tooltip("How far around this object the grass leans away, in metres.")]
        [SerializeField, Min(0.1f)] private float radius = 0.9f;

        [Tooltip("How quickly the measured speed follows the body, per second. Lower smooths " +
                 "out jitter from a network-interpolated position.")]
        [SerializeField, Min(0.1f)] private float speedResponse = 8f;

        private static readonly List<GrassBender> Active = new List<GrassBender>();
        private static Vector3 sortOrigin;
        private static readonly System.Comparison<GrassBender> ByDistance = CompareDistance;

        private Vector3 lastPosition;
        private float lastTime = -1f;
        private Vector2 heading;
        private float speed;

        private void OnEnable() => Active.Add(this);

        private void OnDisable() => Active.Remove(this);

        /// <summary>
        /// Writes the benders nearest <paramref name="eye"/> into the shader arrays: their position
        /// and radius, and their motion (xy heading on the ground, z speed in m/s).
        /// </summary>
        /// <param name="time">The grass clock; motion is measured between calls with different times.</param>
        public static int Gather(Vector3 eye, float time, Vector4[] positions, Vector4[] motion)
        {
            if (Active.Count > MaxActive)
            {
                sortOrigin = eye;
                Active.Sort(ByDistance);
            }

            int count = Mathf.Min(Active.Count, MaxActive);
            for (int i = 0; i < count; i++)
            {
                GrassBender bender = Active[i];
                Vector3 p = bender.transform.position;
                bender.Measure(p, time);
                positions[i] = new Vector4(p.x, p.y, p.z, bender.radius);
                motion[i] = new Vector4(bender.heading.x, bender.heading.y, bender.speed, 0f);
            }
            return count;
        }

        /// <summary>Follows the body's ground speed and keeps the last direction it moved in.</summary>
        private void Measure(Vector3 position, float time)
        {
            if (lastTime < 0f)
            {
                lastPosition = position;
                lastTime = time;
                return;
            }
            float elapsed = time - lastTime;
            if (elapsed <= 0f)
                return;
            var moved = new Vector2(position.x - lastPosition.x, position.z - lastPosition.z);
            float distance = moved.magnitude;
            if (distance > 0f)
                heading = moved / distance;
            speed = Mathf.Lerp(speed, distance / elapsed, 1f - Mathf.Exp(-speedResponse * elapsed));
            lastPosition = position;
            lastTime = time;
        }

        private static int CompareDistance(GrassBender a, GrassBender b)
        {
            float da = (a.transform.position - sortOrigin).sqrMagnitude;
            float db = (b.transform.position - sortOrigin).sqrMagnitude;
            return da.CompareTo(db);
        }
    }
}
