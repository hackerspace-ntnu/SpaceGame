using System.Runtime.InteropServices;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// One stored tuft, exactly as GrassCull.compute reads it from <c>_Instances</c>. The layout
    /// is the contract with the compute shader: change one and the other must change with it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GrassInstance
    {
        public const int Stride = sizeof(float) * 8;

        /// <summary>xyz root in world space, w tuft height in metres.</summary>
        public Vector4 PositionScale;

        /// <summary>
        /// x yaw in radians, y tint 0..1, z flower hue (0 = no flower), w thinning rank 0..1:
        /// with distance, tufts with the highest ranks are dropped first.
        /// </summary>
        public Vector4 YawTintAccent;

        public GrassInstance(Vector3 root, float height, float yaw, float tint, float accent, float rank)
        {
            PositionScale = new Vector4(root.x, root.y, root.z, height);
            YawTintAccent = new Vector4(yaw, tint, accent, rank);
        }
    }
}
