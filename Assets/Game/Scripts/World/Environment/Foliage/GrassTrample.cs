using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Where bodies have walked through the grass lately. A square of ground around the viewer
    /// is kept in a small texture that the <see cref="GrassBender"/>s stamp every frame and that
    /// slowly recovers, so a path through a meadow stays flattened behind whoever made it — laid
    /// over in the direction they walked — and stands back up over <c>recoverSeconds</c>.
    ///
    /// The texture is a wrap-around window: texel (i, j) holds the ground cell congruent to it,
    /// so following the viewer only clears the strip of cells that just came into view; nothing
    /// is copied. GrassCull.compute's <c>Trample</c> kernel updates it and its <c>Cull</c> kernel
    /// reads it per tuft. Texel: x how flat 0..1, y foot height × x (bilinear-safe), zw heading.
    ///
    /// Purely a picture, like the benders it records: every machine draws its own trails from
    /// the bodies it sees, nothing is networked, and a reload starts with the grass upright.
    /// </summary>
    public sealed class GrassTrample : IDisposable
    {
        private static readonly int MapId = Shader.PropertyToID("_TrampleMap");
        private static readonly int ReadId = Shader.PropertyToID("_Trample");
        private static readonly int WindowId = Shader.PropertyToID("_TrampleWindow");
        private static readonly int ParamsId = Shader.PropertyToID("_TrampleParams");

        // Trample kernel thread group side (GrassCull.compute: [numthreads(8, 8, 1)]).
        private const int GroupSize = 8;

        // A stall (a breakpoint, a loading hitch) must not recover a whole trail in one step.
        private const float MaxStepSeconds = 0.25f;

        private readonly int resolution;
        private readonly float texel;
        private readonly float recoverSeconds;
        private readonly float heightTolerance;
        private readonly RenderTexture map;

        private Vector2Int origin;
        private Vector2Int previous;
        private float recovered;
        private bool hasOrigin;
        private float lastTime;

        /// <param name="resolution">Texels along one side; rounded up to a power of two (the
        /// trample kernel wraps cells with a mask) of at least one thread group.</param>
        /// <param name="windowSize">The side of the remembered square of ground, in metres.</param>
        /// <param name="recoverSeconds">How long flattened grass takes to stand fully back up.</param>
        /// <param name="heightTolerance">How far above or below a body's feet, in metres, grass is still trampled.</param>
        public GrassTrample(int resolution, float windowSize, float recoverSeconds, float heightTolerance)
        {
            this.resolution = Mathf.NextPowerOfTwo(Mathf.Max(resolution, GroupSize));
            texel = windowSize / this.resolution;
            this.recoverSeconds = recoverSeconds;
            this.heightTolerance = heightTolerance;
            map = new RenderTexture(this.resolution, this.resolution, 0, RenderTextureFormat.ARGBHalf,
                                    RenderTextureReadWrite.Linear)
            {
                name = "Grass Trample",
                enableRandomWrite = true,
                hideFlags = HideFlags.HideAndDontSave,
            };
            map.Create();
        }

        /// <summary>
        /// Moves the window to <paramref name="eye"/>, recovers the grass and stamps the benders
        /// already set on the shader (<c>_GrassBenders</c>, <c>_GrassBenderMotion</c>). Called for
        /// one camera only (GrassField decides which); a second call at the same time does nothing.
        /// </summary>
        public void Update(CommandBuffer commands, ComputeShader shader, int kernel, Vector3 eye, float time)
        {
            if (hasOrigin && time <= lastTime)
                return;
            float elapsed = hasOrigin ? Mathf.Clamp(time - lastTime, 0f, MaxStepSeconds) : 0f;
            lastTime = time;
            recovered = elapsed / recoverSeconds;

            previous = origin;
            origin = new Vector2Int(Mathf.FloorToInt(eye.x / texel) - resolution / 2,
                                    Mathf.FloorToInt(eye.z / texel) - resolution / 2);
            // Nothing written yet: a previous window that shares no cell clears every texel.
            if (!hasOrigin)
                previous = origin + new Vector2Int(resolution * 2, 0);
            hasOrigin = true;

            commands.SetComputeTextureParam(shader, kernel, MapId, map);
            SetWindow(commands, shader);
            int groups = resolution / GroupSize;
            commands.DispatchCompute(shader, kernel, groups, groups, 1);
        }

        /// <summary>Lets the cull kernel read the trails; every camera, after <see cref="Update"/>.</summary>
        public void Bind(CommandBuffer commands, ComputeShader shader, int cullKernel)
        {
            commands.SetComputeTextureParam(shader, cullKernel, ReadId, map);
            SetWindow(commands, shader);
        }

        private void SetWindow(CommandBuffer commands, ComputeShader shader)
        {
            commands.SetComputeIntParams(shader, WindowId, origin.x, origin.y, previous.x, previous.y);
            commands.SetComputeVectorParam(shader, ParamsId, new Vector4(texel, resolution, recovered, heightTolerance));
        }

        public void Dispose()
        {
            map.Release();
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(map);
            else
                UnityEngine.Object.DestroyImmediate(map);
        }
    }
}
