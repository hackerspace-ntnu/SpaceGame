using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Parts the grass around whatever it is on — a player, a creature, a cart wheel. Purely a
    /// picture: each machine bends its own grass around the bodies it can see, so nothing here
    /// is networked, and trampled grass stands back up rather than being saved.
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

        private static readonly List<GrassBender> Active = new List<GrassBender>();
        private static Vector3 sortOrigin;
        private static readonly System.Comparison<GrassBender> ByDistance = CompareDistance;

        private void OnEnable() => Active.Add(this);

        private void OnDisable() => Active.Remove(this);

        /// <summary>Writes the benders nearest <paramref name="eye"/> into the shader array.</summary>
        public static int Gather(Vector3 eye, Vector4[] into)
        {
            if (Active.Count > MaxActive)
            {
                sortOrigin = eye;
                Active.Sort(ByDistance);
            }

            int count = Mathf.Min(Active.Count, MaxActive);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = Active[i].transform.position;
                into[i] = new Vector4(p.x, p.y, p.z, Active[i].radius);
            }
            return count;
        }

        private static int CompareDistance(GrassBender a, GrassBender b)
        {
            float da = (a.transform.position - sortOrigin).sqrMagnitude;
            float db = (b.transform.position - sortOrigin).sqrMagnitude;
            return da.CompareTo(db);
        }
    }
}
