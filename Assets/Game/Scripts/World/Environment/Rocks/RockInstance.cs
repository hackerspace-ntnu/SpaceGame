using System.Runtime.InteropServices;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// One scattered rock, exactly as RockCull.compute reads it from <c>_Instances</c>. The layout
    /// is the contract with the compute shader: change one and the other must change with it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RockInstance
    {
        // A tint of exactly 1 would carry into the next variant index.
        private const float MaxTint = 0.999f;

        /// <summary>xyz the mesh origin in world space, w uniform scale.</summary>
        public Vector4 PositionScale;

        /// <summary>The rotation as a quaternion (x, y, z, w).</summary>
        public Vector4 Rotation;

        /// <summary>xyz per-axis stretch on top of the scale; w the variant index plus a 0..1 tint as its fraction.</summary>
        public Vector4 StretchVariant;

        /// <summary>xyz world centre of the rock's bounding sphere, w its radius.</summary>
        public Vector4 Bounds;

        public RockInstance(Vector3 pivot, float scale, Quaternion rotation, Vector3 stretch, int variant, float tint,
                            Vector3 centre, float radius)
        {
            PositionScale = new Vector4(pivot.x, pivot.y, pivot.z, scale);
            Rotation = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
            StretchVariant = new Vector4(stretch.x, stretch.y, stretch.z, variant + Mathf.Min(tint, MaxTint));
            Bounds = new Vector4(centre.x, centre.y, centre.z, radius);
        }

        public int Variant => (int)StretchVariant.w;
        public Vector3 Pivot => PositionScale;
        public Quaternion Orientation => new Quaternion(Rotation.x, Rotation.y, Rotation.z, Rotation.w);
        public Vector3 Scale => (Vector3)StretchVariant * PositionScale.w;
    }
}
