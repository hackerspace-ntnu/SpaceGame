using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// A pond or lake: the root of the Water prefab. Set <see cref="size"/> and the surface,
    /// its planar reflection volume and its underwater volume all follow.
    ///
    /// The surface is a flat grid built here, never saved: the shader's swells displace
    /// vertices, so they need a vertex every <see cref="cellSize"/> metres, and a mesh per size
    /// as an asset would bloat every scene and prefab that holds water. The mesh is rebuilt
    /// whenever the component is enabled or its size changes.
    ///
    /// Purely a picture, the same on every machine: nothing here is networked or saved.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WaterBody : MonoBehaviour
    {
        [Tooltip("Width (X) and length (Z) of the water surface in metres, centred on this transform. " +
                 "Let it run under the shore: the hidden part costs no pixels.")]
        [SerializeField] private Vector2 size = new Vector2(30f, 30f);

        [Tooltip("Spacing of the surface grid in metres. The swells need a vertex at least every " +
                 "half metre; larger saves vertices on big, calm water.")]
        [SerializeField, Range(0.25f, 4f)] private float cellSize = 0.5f;

        [Tooltip("How far beyond the water's edge, and how high above it, the camera still gets " +
                 "the planar reflection. Outside that the water reflects the reflection probe.")]
        [SerializeField, Min(0f)] private float reflectionReach = 40f;

        [Tooltip("How far the swells can lift the surface, in metres; widens the culling bounds.")]
        [SerializeField, Min(0f)] private float swellAllowance = 0.5f;

        [SerializeField] private PlanarReflectionVolume reflectionVolume;
        [SerializeField] private UnderwaterVolume underwaterVolume;

        private Mesh surface;
        private Vector2 builtSize;
        private float builtCellSize;

        private void OnEnable() => Rebuild();

        private void OnDisable() => ReleaseSurface();

        // Inspector edits rebuild on the next update: a mesh cannot be swapped inside OnValidate.
        private void Update()
        {
            if (size != builtSize || !Mathf.Approximately(cellSize, builtCellSize))
                Rebuild();
        }

        private void Rebuild()
        {
            ReleaseSurface();
            surface = BuildSurface(size, cellSize, swellAllowance);
            GetComponent<MeshFilter>().sharedMesh = surface;
            builtSize = size;
            builtCellSize = cellSize;
            FitVolumes();
        }

        private void ReleaseSurface()
        {
            if (surface == null)
                return;
            if (Application.isPlaying)
                Destroy(surface);
            else
                DestroyImmediate(surface);
            surface = null;
        }

        private void FitVolumes()
        {
            if (reflectionVolume != null)
            {
                reflectionVolume.volumeSize = new Vector3(size.x + reflectionReach * 2f, reflectionReach * 2f,
                                                          size.y + reflectionReach * 2f);
            }
            if (underwaterVolume != null)
                underwaterVolume.Size = size;
        }

        /// <summary>A flat, evenly subdivided rectangle centred on the origin.</summary>
        private static Mesh BuildSurface(Vector2 size, float cellSize, float swellAllowance)
        {
            int cellsX = Mathf.Max(1, Mathf.CeilToInt(size.x / cellSize));
            int cellsZ = Mathf.Max(1, Mathf.CeilToInt(size.y / cellSize));
            int sideX = cellsX + 1;
            int sideZ = cellsZ + 1;

            var vertices = new Vector3[sideX * sideZ];
            var normals = new Vector3[vertices.Length];
            for (int z = 0; z < sideZ; z++)
            {
                for (int x = 0; x < sideX; x++)
                {
                    int i = z * sideX + x;
                    vertices[i] = new Vector3((x / (float)cellsX - 0.5f) * size.x, 0f, (z / (float)cellsZ - 0.5f) * size.y);
                    normals[i] = Vector3.up;
                }
            }

            var indices = new int[cellsX * cellsZ * 6];
            for (int z = 0, t = 0; z < cellsZ; z++)
            {
                for (int x = 0; x < cellsX; x++, t += 6)
                {
                    int i = z * sideX + x;
                    indices[t] = i;
                    indices[t + 1] = i + sideX;
                    indices[t + 2] = i + 1;
                    indices[t + 3] = i + 1;
                    indices[t + 4] = i + sideX;
                    indices[t + 5] = i + sideX + 1;
                }
            }

            var mesh = new Mesh
            {
                name = "Water Surface",
                hideFlags = HideFlags.DontSave,
                indexFormat = vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(indices, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size.x, swellAllowance * 2f, size.y));
            return mesh;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.75f, 0.95f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, 0f, size.y));
        }
    }
}
