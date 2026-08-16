using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Builds the runtime GameObjects for a list of <see cref="LiquidPool"/>s.
    ///
    /// Per pool:
    ///   • One MeshRenderer + MeshFilter holding a flat quad at the waterline, sized to the pool's
    ///     horizontal bounds. The quad is intentionally a *rectangle* not the pool's exact silhouette
    ///     — we trade visual precision for cheap meshes. The water material can hide the rectangle's
    ///     extent with depth fade / opacity around edges.
    ///   • Optionally a BoxCollider trigger spanning the rectangle from the waterline down by
    ///     <see cref="CaveLiquidSettings.triggerDepth"/> metres, so gameplay code can detect entering.
    ///
    /// All objects parented under one "Liquid" root for tidy hierarchy.
    /// </summary>
    public static class LiquidPoolSpawner
    {
        public static GameObject Spawn(List<LiquidPool> pools, CaveLiquidSettings settings, Transform parent, int layer)
        {
            if (pools == null || pools.Count == 0) return null;
            if (!settings.enabled) return null;

            var root = new GameObject("Liquid");
            root.transform.SetParent(parent, worldPositionStays: false);
            if (layer >= 0) root.layer = layer;

            for (int i = 0; i < pools.Count; i++)
            {
                var pool = pools[i];
                BuildPoolGo(pool, settings, root.transform, layer);
            }

            return root;
        }

        static void BuildPoolGo(LiquidPool pool, CaveLiquidSettings settings, Transform parent, int layer)
        {
            var go = new GameObject($"Pool_{pool.Id}");
            go.transform.SetParent(parent, worldPositionStays: false);
            if (!string.IsNullOrEmpty(settings.liquidTag) && settings.liquidTag != "Untagged")
            {
                try { go.tag = settings.liquidTag; }
                catch (UnityException) { /* tag not registered — leave Untagged rather than crashing */ }
            }
            if (layer >= 0) go.layer = layer;

            // Centre the GameObject on the pool's horizontal centre at the waterline (in parent space).
            Vector3 worldCenter = new Vector3(pool.HorizontalBounds.center.x, pool.WaterlineY, pool.HorizontalBounds.center.z);
            go.transform.position = parent.TransformPoint(worldCenter);

            // Build a quad sized to the bounds. Local space, centred at origin, lying in XZ plane.
            float halfX = pool.HorizontalBounds.size.x * 0.5f;
            float halfZ = pool.HorizontalBounds.size.z * 0.5f;
            var mesh = new Mesh { name = $"Pool_{pool.Id}_Mesh" };
            mesh.vertices = new[]
            {
                new Vector3(-halfX, 0f, -halfZ),
                new Vector3( halfX, 0f, -halfZ),
                new Vector3( halfX, 0f,  halfZ),
                new Vector3(-halfX, 0f,  halfZ),
            };
            mesh.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = settings.liquidMaterial != null ? settings.liquidMaterial : DefaultLiquidMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (settings.spawnTriggerVolume && settings.triggerDepth > 0f)
            {
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                float h = settings.triggerDepth;
                box.center = new Vector3(0f, -h * 0.5f, 0f);
                box.size = new Vector3(halfX * 2f, h, halfZ * 2f);
            }
        }

        static Material DefaultLiquidMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = "DefaultLiquid" };
            // Translucent blue-ish baseline so something is visible before a custom water material is assigned.
            m.SetColor("_BaseColor", new Color(0.2f, 0.4f, 0.6f, 0.65f));
            m.SetFloat("_Surface", 1f);    // URP transparent
            m.SetFloat("_Smoothness", 0.85f);
            m.renderQueue = 3000;
            return m;
        }
    }
}
