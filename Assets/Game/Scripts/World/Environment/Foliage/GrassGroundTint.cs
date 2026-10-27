using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace SpaceGame.World.Environment
{
    /// <summary>
    /// Colours painted ground with the grass that grows on it, so a field still reads from afar
    /// after its tufts have sunk away — and on terrain shaders that ignore the paint entirely.
    ///
    /// For every loaded terrain whose material reads <c>_GrassColorMap</c> (StylizedTerrain does),
    /// the paint is turned into a texture the size of the paint map: each texel is the grass colour
    /// of the biomes painted there, its alpha how much of the ground they cover. The texture goes to
    /// the terrain through its splat property block, so no material or scene asset is changed.
    /// Terrains drawn with URP's Terrain/Lit already show the painted ground layers and are skipped.
    /// </summary>
    public sealed class GrassGroundTint : IDisposable
    {
        private static readonly int MapId = Shader.PropertyToID("_GrassColorMap");
        private static readonly int RectId = Shader.PropertyToID("_GrassMapRect");
        private static readonly int MinHeightId = Shader.PropertyToID("_GrassMinHeight");
        private static readonly int WarpId = Shader.PropertyToID("_GrassMapWarp");
        private static readonly int RootId = Shader.PropertyToID("_RootColor");
        private static readonly int TipId = Shader.PropertyToID("_TipColor");
        private static readonly int VariationAId = Shader.PropertyToID("_VariationColorA");
        private static readonly int VariationBId = Shader.PropertyToID("_VariationColorB");
        private static readonly int VariationStrengthId = Shader.PropertyToID("_VariationStrength");
        private static readonly int PatchColorId = Shader.PropertyToID("_PatchColor");
        private static readonly int PatchStrengthId = Shader.PropertyToID("_PatchStrength");
        private static readonly int BladeTintId = Shader.PropertyToID("_BladeTintColor");
        private static readonly int BladeTintAmountId = Shader.PropertyToID("_BladeTintAmount");

        // From afar a field is mostly its tips, so its colour sits this far up the root-to-tip
        // gradient; the patch colour shows over about half its strength. Each blade's warm cast
        // is weighted by its seed squared, which averages a third. Checked by comparing rendered
        // pixels of the tufts with the tinted ground beyond them.
        private const float TipShare = 0.8f;
        private const float PatchShare = 0.5f;
        private const float BladeTintShare = 1f / 3f;

        private sealed class Tinted
        {
            public Texture2D Map;
            public Task<Color32[]> Composing;
            public int Resolution;
            public float EdgeWarp;
        }

        private readonly IReadOnlyList<GrassFieldProfile> profiles;
        private readonly float minimumHeight;
        private readonly Dictionary<Terrain, Tinted> tinted = new Dictionary<Terrain, Tinted>();
        private readonly List<Terrain> scratch = new List<Terrain>();
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        /// <param name="profiles">Each biome's profile, by slot.</param>
        public GrassGroundTint(IReadOnlyList<GrassFieldProfile> profiles, float minimumHeight)
        {
            this.profiles = profiles;
            this.minimumHeight = minimumHeight;
        }

        /// <summary>Starts a map for every loaded or repainted terrain, and applies the finished ones.</summary>
        public void Update(PaintedGround ground)
        {
            scratch.Clear();
            ground.TakeRepainted(scratch);
            foreach (Terrain terrain in scratch)
                if (terrain != null)
                    Compose(terrain, ground);

            scratch.Clear();
            foreach (KeyValuePair<Terrain, Tinted> pair in tinted)
                if (pair.Key == null || !ground.Contains(pair.Key))
                    scratch.Add(pair.Key);
            foreach (Terrain terrain in scratch)
                Forget(terrain);

            foreach (KeyValuePair<Terrain, Tinted> pair in tinted)
                if (pair.Value.Composing != null && pair.Value.Composing.IsCompleted)
                    Apply(pair.Key, pair.Value);
        }

        /// <summary>Takes the colour off every terrain again.</summary>
        public void Dispose()
        {
            scratch.Clear();
            scratch.AddRange(tinted.Keys);
            foreach (Terrain terrain in scratch)
                Forget(terrain);
        }

        private void Compose(Terrain terrain, PaintedGround ground)
        {
            Material material = terrain.materialTemplate;
            if (material == null || !material.HasProperty(MapId))
                return;

            TerrainData data = terrain.terrainData;
            int layerCount = data.alphamapLayers;
            var colours = new Color[layerCount];
            var strengths = new float[layerCount];
            var fullWeights = new float[layerCount];
            float edgeWarp = 0f;
            bool any = false;
            IReadOnlyList<int> indices = ground.PaintIndices(terrain);
            for (int slot = 0; slot < profiles.Count; slot++)
            {
                int layer = indices[slot];
                if (layer < 0 || profiles[slot].GroundTint <= 0f)
                    continue;
                colours[layer] = GroundColour(profiles[slot].Material);
                strengths[layer] = profiles[slot].GroundTint;
                fullWeights[layer] = profiles[slot].MinimumLayerWeight;
                edgeWarp = Mathf.Max(edgeWarp, profiles[slot].EdgeWarp);
                any = true;
            }
            if (!any)
            {
                Forget(terrain);
                return;
            }

            int resolution = data.alphamapResolution;
            float[,,] paint = data.GetAlphamaps(0, 0, resolution, resolution);
            if (!tinted.TryGetValue(terrain, out Tinted entry))
                tinted.Add(terrain, entry = new Tinted());
            entry.Resolution = resolution;
            entry.EdgeWarp = edgeWarp;
            entry.Composing = Task.Run(() => ComposeSafely(paint, colours, strengths, fullWeights, resolution));
            // A terrain's first map is waited for: until it lands the painted ground shows the
            // bare terrain colour, which reads as a different world under the grass that is still
            // streaming in. A repaint keeps showing the old map meanwhile, so it stays on the worker.
            if (entry.Map == null)
            {
                entry.Composing.Wait();
                Apply(terrain, entry);
            }
        }

        /// <summary>
        /// The coverage-weighted grass colour per paint texel, thinned toward a painted edge the
        /// way the scatter thins the tufts. Runs on a worker thread.
        /// </summary>
        private static Color32[] ComposeSafely(float[,,] paint, Color[] colours, float[] strengths,
                                               float[] fullWeights, int resolution)
        {
            try
            {
                // Only the grass layers, with each layer's ramp precomputed: a terrain's first map
                // is built on the main thread, and every layer per texel was 170 ms there.
                var active = new List<int>();
                for (int layer = 0; layer < colours.Length; layer++)
                    if (strengths[layer] > 0f)
                        active.Add(layer);
                int count = active.Count;
                var start = new float[count];
                var scale = new float[count];
                var red = new float[count];
                var green = new float[count];
                var blue = new float[count];
                for (int i = 0; i < count; i++)
                {
                    int layer = active[i];
                    start[i] = fullWeights[layer] * 0.5f;
                    scale[i] = strengths[layer] / Mathf.Max(1e-4f, fullWeights[layer] * 0.5f);
                    red[i] = colours[layer].r;
                    green[i] = colours[layer].g;
                    blue[i] = colours[layer].b;
                }

                var pixels = new Color32[resolution * resolution];
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        float coverage = 0f, r = 0f, g = 0f, b = 0f;
                        for (int i = 0; i < count; i++)
                        {
                            float weight = paint[y, x, active[i]] - start[i];
                            if (weight <= 0f)
                                continue;
                            // The scatter's ramp: nothing at half the full weight, all of it at full.
                            float share = Mathf.Min(weight * scale[i], scale[i] * start[i]);
                            coverage += share;
                            r += red[i] * share;
                            g += green[i] * share;
                            b += blue[i] * share;
                        }
                        if (coverage <= 0f)
                            continue;
                        // Premultiplied by coverage: filtering and mips then fade an edge toward
                        // "no grass" instead of toward the black of the empty texels beyond it,
                        // which drew a dark ring round every field that widened with distance.
                        float alpha = Mathf.Min(1f, coverage);
                        float premultiplied = 255f * alpha / coverage;
                        pixels[y * resolution + x] = new Color32(ToByte(r * premultiplied), ToByte(g * premultiplied),
                                                                 ToByte(b * premultiplied), ToByte(alpha * 255f));
                    }
                }
                return pixels;
            }
            catch (Exception e)
            {
                // A worker's exception would otherwise vanish with its task; the ground stays bare.
                Debug.LogException(e);
                return null;
            }
        }

        private static byte ToByte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value), 0, 255);

        private void Apply(Terrain terrain, Tinted entry)
        {
            Color32[] pixels = entry.Composing.Result;
            entry.Composing = null;
            if (pixels == null)
                return;

            int resolution = entry.Resolution;
            if (entry.Map == null || entry.Map.width != resolution)
            {
                DestroyMap(entry);
                // Linear storage of the materials' sRGB colours, premultiplied: StylizedTerrain
                // divides by coverage and only then converts, which an sRGB texture would do
                // before it could. Mips keep the far field calm.
                entry.Map = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true, true)
                {
                    name = "GrassColorMap_" + terrain.name,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            entry.Map.SetPixels32(pixels);
            entry.Map.Apply(true);

            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            terrain.GetSplatMaterialPropertyBlock(block);
            block.SetTexture(MapId, entry.Map);
            block.SetVector(RectId, new Vector4(origin.x, origin.z, 1f / size.x, 1f / size.z));
            block.SetFloat(MinHeightId, minimumHeight);
            // The same wandering outline as the tufts: x how far in metres, y the noise period.
            float texel = size.x / resolution;
            block.SetVector(WarpId, new Vector4(entry.EdgeWarp * texel, texel / PaintedGroundPatch.WarpFrequency, 0f, 0f));
            terrain.SetSplatMaterialPropertyBlock(block);
        }

        private void Forget(Terrain terrain)
        {
            if (!tinted.TryGetValue(terrain, out Tinted entry))
                return;
            tinted.Remove(terrain);
            DestroyMap(entry);
            if (terrain == null)
                return;
            // A zero rect is the shader's "no grass colour" — the map is gone with the entry.
            terrain.GetSplatMaterialPropertyBlock(block);
            block.SetVector(RectId, Vector4.zero);
            terrain.SetSplatMaterialPropertyBlock(block);
        }

        private static void DestroyMap(Tinted entry)
        {
            if (entry.Map == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(entry.Map);
            else
                UnityEngine.Object.DestroyImmediate(entry.Map);
            entry.Map = null;
        }

        /// <summary>The colour a field of this material reads as from afar.</summary>
        private static Color GroundColour(Material material)
        {
            Color variation = (material.GetColor(VariationAId) + material.GetColor(VariationBId)) * 0.5f;
            Color tips = Color.Lerp(material.GetColor(TipId), variation, material.GetFloat(VariationStrengthId));
            tips = Color.Lerp(tips, material.GetColor(PatchColorId), material.GetFloat(PatchStrengthId) * PatchShare);
            Color field = Color.Lerp(material.GetColor(RootId), tips, TipShare);
            return Color.Lerp(field, material.GetColor(BladeTintId), material.GetFloat(BladeTintAmountId) * BladeTintShare);
        }
    }
}
