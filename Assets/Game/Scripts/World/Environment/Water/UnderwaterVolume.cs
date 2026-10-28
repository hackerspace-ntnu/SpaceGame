using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Marks a body of water a camera can go under. The surface is at this transform's height;
    /// the volume is <see cref="size"/> across in this transform's local space — so it stretches,
    /// turns and mirrors with the surface mesh — and reaches <see cref="depth"/> metres below it.
    /// While a camera is inside one, <see cref="UnderwaterRenderFeature"/> draws that camera's view
    /// as seen through the water.
    ///
    /// Purely a picture, decided per camera on each machine: nothing here is networked or saved.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class UnderwaterVolume : MonoBehaviour
    {
        [Tooltip("Width (X) and length (Z) of the water body, centred on this transform and scaled " +
                 "with it like the surface mesh (metres at scale 1).")]
        [SerializeField] private Vector2 size = new Vector2(40f, 40f);

        [Tooltip("How far below the surface the volume reaches, in metres.")]
        [SerializeField, Min(0.1f)] private float depth = 10f;

        private static readonly List<UnderwaterVolume> Active = new List<UnderwaterVolume>();

        private Renderer surfaceRenderer;

        /// <summary>Set by <see cref="WaterBody"/>, which keeps the volume the size of its surface.</summary>
        public Vector2 Size
        {
            get => size;
            set => size = value;
        }

        public float SurfaceHeight => transform.position.y;

        /// <summary>
        /// The material of the water surface on this GameObject, whose colours the underwater view
        /// takes on; null for a volume with no surface of its own.
        /// </summary>
        public Material SurfaceMaterial => surfaceRenderer != null ? surfaceRenderer.sharedMaterial : null;

        private void OnEnable()
        {
            surfaceRenderer = GetComponent<Renderer>();
            Active.Add(this);
        }

        private void OnDisable() => Active.Remove(this);

        /// <summary>The water <paramref name="eye"/> is under, or null.</summary>
        public static UnderwaterVolume Find(Vector3 eye)
        {
            foreach (UnderwaterVolume volume in Active)
            {
                // Across in local space, like the surface mesh; down in world metres.
                Vector3 local = volume.transform.InverseTransformPoint(eye);
                float below = volume.SurfaceHeight - eye.y;
                bool inside = Mathf.Abs(local.x) <= volume.size.x * 0.5f
                           && Mathf.Abs(local.z) <= volume.size.y * 0.5f
                           && below > 0f && below < volume.depth;
                if (inside)
                    return volume;
            }
            return null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.55f, 0.9f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            float localDepth = depth / Mathf.Max(Mathf.Abs(transform.lossyScale.y), 0.0001f);
            Gizmos.DrawWireCube(Vector3.down * (localDepth * 0.5f), new Vector3(size.x, localDepth, size.y));
        }
    }
}
