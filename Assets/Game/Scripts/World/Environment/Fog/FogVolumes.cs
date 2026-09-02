// Which fog the renderer should draw, and what it should look like.
//
// Everything talks to this and nothing talks to a FogVolume directly. A volume registers itself
// when it wakes and forgets about the problem; the renderer asks this class one question per camera
// and gets an answer that is correct whether the scene holds nought volumes or ninety.
//
// The eight-slot limit is a deliberate budget rather than a technical ceiling. Cost in the shader is
// linear in the number of volumes a ray could be inside, so an unbounded count is an unbounded frame
// time on a scene an artist can create by accident. Eight is enough that no view has ever wanted a
// ninth, and picking the eight NEAREST means the ones that get dropped are the ones already too far
// away to read as anything but a tint.
using System;
using System.Collections.Generic;
using SpaceGame.World.Weather;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    public static class FogVolumes
    {
        /// <summary>
        /// Must match FOG_MAX_VOLUMES in VolumetricFog.hlsl. The arrays uploaded to the shader are
        /// this long every frame regardless of how many are in use — Unity fixes a global array's
        /// size at the first upload, and a later, longer one is silently truncated.
        /// </summary>
        public const int MaxVolumes = 8;

        /// <summary>Must match FOG_MAX_LIGHTS in VolumetricFog.hlsl.</summary>
        public const int MaxLights = 8;

        private static readonly List<FogVolume> Registered = new List<FogVolume>();
        private static readonly List<FogLight> Lights = new List<FogLight>();

        private static readonly List<FogVolume> Selected = new List<FogVolume>(MaxVolumes);
        private static readonly List<FogLight> SelectedLights = new List<FogLight>(MaxLights);

        private static readonly Matrix4x4[] WorldToLocal = new Matrix4x4[MaxVolumes];
        private static readonly Vector4[] ColorDensity = new Vector4[MaxVolumes];
        private static readonly Vector4[] Emission = new Vector4[MaxVolumes];
        private static readonly Vector4[] Shape = new Vector4[MaxVolumes];
        private static readonly Vector4[] Noise = new Vector4[MaxVolumes];
        private static readonly Vector4[] Motion = new Vector4[MaxVolumes];
        private static readonly Vector4[] Breathe = new Vector4[MaxVolumes];

        private static readonly Vector4[] LightPosition = new Vector4[MaxLights];
        private static readonly Vector4[] LightColor = new Vector4[MaxLights];

        private static readonly Vector3[] SkyDirections = { Vector3.up };
        private static readonly Color[] SkySamples = new Color[1];

        private static readonly int WorldToLocalId = Shader.PropertyToID("_FogWorldToLocal");
        private static readonly int ColorId = Shader.PropertyToID("_FogColor");
        private static readonly int EmissionId = Shader.PropertyToID("_FogEmission");
        private static readonly int ShapeId = Shader.PropertyToID("_FogShape");
        private static readonly int NoiseId = Shader.PropertyToID("_FogNoise");
        private static readonly int MotionId = Shader.PropertyToID("_FogMotion");
        private static readonly int BreatheId = Shader.PropertyToID("_FogBreathe");
        private static readonly int CountId = Shader.PropertyToID("_FogVolumeCount");
        private static readonly int LightPositionId = Shader.PropertyToID("_FogLightPosition");
        private static readonly int LightColorId = Shader.PropertyToID("_FogLightColor");
        private static readonly int LightCountId = Shader.PropertyToID("_FogLightCount");
        private static readonly int SkyLightId = Shader.PropertyToID("_FogSkyLight");
        private static readonly int TimeId = Shader.PropertyToID("_FogTime");

        /// <summary>Camera position the current sort is ordering against.</summary>
        private static Vector3 sortOrigin;

        private static readonly Comparison<FogVolume> ByDistance = (a, b) =>
            SortKey(a).CompareTo(SortKey(b));

        private static readonly Comparison<FogLight> LightsByDistance = (a, b) =>
            (a.transform.position - sortOrigin).sqrMagnitude
            .CompareTo((b.transform.position - sortOrigin).sqrMagnitude);

        /// <summary>
        /// How many volumes were uploaded by the last <see cref="Push"/>. Zero means the renderer
        /// has nothing to do, which is the check that keeps fog off the frame budget of every scene
        /// that does not have any.
        /// </summary>
        public static int ActiveCount { get; private set; }

        // Statics survive a domain reload when "Enter Play Mode Options" has it switched off, and a
        // list still holding last session's destroyed volumes renders as a screen of null-reference
        // spam on the first frame of the next one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Registered.Clear();
            Lights.Clear();
            Selected.Clear();
            SelectedLights.Clear();
            ActiveCount = 0;
        }

        public static void Register(FogVolume volume)
        {
            if (volume != null && !Registered.Contains(volume))
                Registered.Add(volume);
        }

        public static void Unregister(FogVolume volume) => Registered.Remove(volume);

        public static void Register(FogLight light)
        {
            if (light != null && !Lights.Contains(light))
                Lights.Add(light);
        }

        public static void Unregister(FogLight light) => Lights.Remove(light);

        /// <summary>
        /// Distance from the sort origin to the volume's surface, negative inside it. Sorting on
        /// this rather than on the distance to the centre is what stops a large volume the camera is
        /// standing in from losing its slot to a small one parked slightly nearer.
        /// </summary>
        private static float SortKey(FogVolume volume) =>
            Vector3.Distance(volume.transform.position, sortOrigin) - volume.BoundingRadius;

        /// <summary>
        /// Uploads the nearest volumes and lights, and returns how many volumes are in play.
        ///
        /// <para>
        /// Called once per rendering camera. Everything it writes is a global, because the fog is a
        /// fullscreen pass with no renderer of its own to carry a property block.
        /// </para>
        /// </summary>
        public static int Push(Vector3 cameraPosition, float maxDistance)
        {
            sortOrigin = cameraPosition;

            // How much cover the viewer is standing in, from the one shelter volume the weather
            // already uses (SandstormShelter — the ship's interior, a cave mouth). Weather fog does
            // not follow you indoors, so a volume that fades to nothing under cover is dropped here
            // rather than uploaded at zero: with every volume gone the whole pass is skipped, which
            // is the difference between a cheap interior and a full march that draws nothing.
            float shelter = Sandstorms.ShelterAt(cameraPosition);

            Selected.Clear();
            for (int i = 0; i < Registered.Count; i++)
            {
                FogVolume volume = Registered[i];

                // A volume can be destroyed without OnDisable running — a scene unload does exactly
                // that — so the list is swept here rather than trusted.
                if (volume == null)
                {
                    Registered.RemoveAt(i--);
                    continue;
                }

                if (!volume.isActiveAndEnabled || volume.DensityFor(shelter) <= 0f)
                    continue;

                if (SortKey(volume) > maxDistance)
                    continue;

                Selected.Add(volume);
            }

            Selected.Sort(ByDistance);
            if (Selected.Count > MaxVolumes)
                Selected.RemoveRange(MaxVolumes, Selected.Count - MaxVolumes);

            ActiveCount = Selected.Count;
            if (ActiveCount == 0)
            {
                Shader.SetGlobalInt(CountId, 0);
                Shader.SetGlobalInt(LightCountId, 0);
                return 0;
            }

            // The shared weather clock, not Time.time: two machines in one session must see the
            // same fog in the same place, and this is the only number both of them agree on.
            double clock = Sandstorms.Now;

            for (int i = 0; i < ActiveCount; i++)
                Pack(i, Selected[i], clock, shelter);

            // The unused tail is left as whatever it was last frame; the shader never reads past
            // _FogVolumeCount, and clearing it would be eight matrix writes a frame for nothing.
            Shader.SetGlobalMatrixArray(WorldToLocalId, WorldToLocal);
            Shader.SetGlobalVectorArray(ColorId, ColorDensity);
            Shader.SetGlobalVectorArray(EmissionId, Emission);
            Shader.SetGlobalVectorArray(ShapeId, Shape);
            Shader.SetGlobalVectorArray(NoiseId, Noise);
            Shader.SetGlobalVectorArray(MotionId, Motion);
            Shader.SetGlobalVectorArray(BreatheId, Breathe);
            Shader.SetGlobalInt(CountId, ActiveCount);
            Shader.SetGlobalFloat(TimeId, (float)clock);

            PushLights(cameraPosition);
            PushSkyLight();

            return ActiveCount;
        }

        private static void Pack(int slot, FogVolume volume, double clock, float shelter)
        {
            WorldToLocal[slot] = volume.WorldToVolume;

            Color color = volume.color.linear;
            ColorDensity[slot] = new Vector4(color.r, color.g, color.b, volume.DensityFor(shelter));

            Color emissive = volume.emission.linear;
            Emission[slot] = new Vector4(emissive.r, emissive.g, emissive.b, volume.ambient);

            Shape[slot] = new Vector4((float)volume.shape,
                                      volume.edgeFeather,
                                      volume.verticalFalloff,
                                      volume.extinction);

            Noise[slot] = new Vector4(volume.noiseScale,
                                      volume.erosion,
                                      volume.verticalSquash,
                                      volume.detailScale);

            Vector3 drift = volume.DriftAt(clock);
            Motion[slot] = new Vector4(drift.x, drift.y, drift.z, volume.forwardScatter);

            Breathe[slot] = new Vector4(volume.churn, volume.churnScale, volume.churnSpeed, 0f);
        }

        private static void PushLights(Vector3 cameraPosition)
        {
            SelectedLights.Clear();
            for (int i = 0; i < Lights.Count; i++)
            {
                FogLight light = Lights[i];
                if (light == null)
                {
                    Lights.RemoveAt(i--);
                    continue;
                }

                if (light.Contributes)
                    SelectedLights.Add(light);
            }

            SelectedLights.Sort(LightsByDistance);
            int count = Mathf.Min(SelectedLights.Count, MaxLights);

            for (int i = 0; i < count; i++)
            {
                FogLight light = SelectedLights[i];
                Vector3 position = light.transform.position;
                float range = Mathf.Max(0.01f, light.Source.range);

                // The shader's smooth cutoff wants the inverse squared range, matching URP's own
                // distance attenuation so a lamp fades out of the fog exactly where it fades off
                // the walls.
                LightPosition[i] = new Vector4(position.x, position.y, position.z,
                                               1f / (range * range));

                Color color = light.FogColor;
                LightColor[i] = new Vector4(color.r, color.g, color.b, 0f);
            }

            Shader.SetGlobalVectorArray(LightPositionId, LightPosition);
            Shader.SetGlobalVectorArray(LightColorId, LightColor);
            Shader.SetGlobalInt(LightCountId, count);
        }

        /// <summary>
        /// The sky's own radiance, which the shader cannot read for itself.
        ///
        /// <para>
        /// A fullscreen blit is a procedural draw with no per-object SH constants, so
        /// <c>SampleSH</c> inside it returns zero. Deep in a thick volume, where the sun march is
        /// occluded in every direction, this is the only light there is — the sandstorm learned that
        /// the hard way and its interior rendered black until the value was handed in.
        /// </para>
        /// </summary>
        private static void PushSkyLight()
        {
            RenderSettings.ambientProbe.Evaluate(SkyDirections, SkySamples);

            // Evaluate returns linear radiance, which is what a linear project's shaders want.
            Color sky = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? SkySamples[0]
                : SkySamples[0].gamma;

            Shader.SetGlobalVector(SkyLightId, new Vector4(Mathf.Max(0f, sky.r),
                                                           Mathf.Max(0f, sky.g),
                                                           Mathf.Max(0f, sky.b), 0f));
        }
    }
}
