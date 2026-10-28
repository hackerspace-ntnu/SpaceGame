using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The creatures standing in the review scene, one lane each, side by side along +X and all
    /// facing +X, so a camera looking along +Z sees every one in profile walking to the right.
    ///
    /// <para>
    /// Each lane has its own floor whose grid scrolls backward at the lane's speed. The clips are
    /// in place, so a creature on a floor moving at the speed the game moves its body should keep
    /// its planted feet still on the grid: any slide is the foot skating players will see.
    /// </para>
    /// <para>
    /// Everything here is <see cref="HideFlags.DontSave"/> and edit-mode only: bodies are instances
    /// of the model file, not the agent prefab, so no AI, netcode or saver ever wakes up. The
    /// prefab only lends its materials, so eyes and coats look as in game.
    /// </para>
    /// </summary>
    public sealed class CreatureAnimationStage : IDisposable
    {
        public const string RootName = "[Creature Animation Review]";

        private const float LaneGap = 1f;
        private const float FloorMargin = 1f;
        private const float TileMetres = 0.5f;
        private const int TilePixels = 64;
        private const int LinePixels = 3;
        // A clip that does not loop holds its last pose this long before it starts again.
        private const float EndHoldSeconds = 0.6f;
        private static readonly Color FloorColour = new Color(0.46f, 0.45f, 0.43f);
        private static readonly Color LineColour = new Color(0.24f, 0.24f, 0.23f);
        private static readonly Quaternion FacingPlusX = Quaternion.Euler(0f, 90f, 0f);

        public sealed class Lane
        {
            public ReviewCreature Creature;
            public ReviewClip Clip;
            /// <summary>The floor's speed in m/s.</summary>
            public float Speed;
            public GameObject Body;
            public Bounds Bounds;
            internal Material Floor;
            internal Vector3 BodyPosition;
        }

        private readonly List<Lane> lanes = new List<Lane>();
        private readonly List<Object> owned = new List<Object>();
        private GameObject root;
        private Texture2D grid;

        public IReadOnlyList<Lane> Lanes => lanes;

        /// <summary>True while the stage stands in the scene with exactly these creatures, in this order.</summary>
        public bool Shows(IReadOnlyList<ReviewCreature> creatures) =>
            root != null && lanes.Select(l => l.Creature).SequenceEqual(creatures);

        public Bounds AllBounds
        {
            get
            {
                Bounds all = lanes[0].Bounds;
                foreach (Lane lane in lanes) all.Encapsulate(lane.Bounds);
                return all;
            }
        }

        public void Build(IReadOnlyList<ReviewCreature> creatures)
        {
            Clear();
            root = new GameObject(RootName) { hideFlags = HideFlags.DontSave };
            if (grid == null) grid = GridTexture();
            float x = 0f;
            foreach (ReviewCreature creature in creatures)
            {
                GameObject body = CreateBody(creature, root.transform);
                Bounds local = RendererBounds(body);
                float laneLength = local.size.x + 2f * FloorMargin;
                float halfWidth = local.extents.z + FloorMargin;
                var offset = new Vector3(x + laneLength * 0.5f - local.center.x, 0f, -local.center.z);
                body.transform.position = offset;
                local.center += offset;

                lanes.Add(new Lane
                {
                    Creature = creature,
                    Body = body,
                    Bounds = local,
                    BodyPosition = offset,
                    Floor = CreateFloor(x, x + laneLength, halfWidth),
                });
                x += laneLength + LaneGap;
            }
        }

        /// <summary>Where in its clip <paramref name="lane"/> is at review time <paramref name="time"/>, in seconds.</summary>
        public static float LocalTime(Lane lane, double time)
        {
            float length = lane.Clip.Clip.length;
            if (length <= 0f) return 0f;
            if (lane.Clip.Clip.isLooping) return (float)(time % length);
            return Mathf.Min((float)(time % (length + EndHoldSeconds)), length);
        }

        public void Sample(double time, bool treadmill)
        {
            foreach (Lane lane in lanes)
            {
                if (lane.Clip == null) continue;
                lane.Clip.Clip.SampleAnimation(lane.Body, LocalTime(lane, time));
                // A clip with curves on the model root would carry the body out of its lane.
                lane.Body.transform.SetPositionAndRotation(lane.BodyPosition, FacingPlusX);
                float scroll = treadmill ? (float)(lane.Speed * time / TileMetres % 1.0) : 0f;
                lane.Floor.mainTextureOffset = new Vector2(scroll, 0f);
            }
        }

        public void Clear()
        {
            if (root != null) Object.DestroyImmediate(root);
            foreach (Object o in owned) Object.DestroyImmediate(o);
            owned.Clear();
            lanes.Clear();
        }

        public void Dispose()
        {
            Clear();
            if (grid != null) Object.DestroyImmediate(grid);
        }

        private static GameObject CreateBody(ReviewCreature creature, Transform parent)
        {
            var body = (GameObject)Object.Instantiate(creature.Model, parent);
            body.name = creature.Name;
            foreach (Transform t in body.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave;

            // Wear the prefab's look: the builders replace eye meshes and materials on the prefab,
            // not in the model file.
            Transform source = creature.Prefab.GetComponentInChildren<Animator>(true).transform;
            body.transform.localScale = source.lossyScale;
            body.transform.rotation = FacingPlusX;
            foreach (Renderer from in source.GetComponentsInChildren<Renderer>(true))
            {
                string path = AnimationUtility.CalculateTransformPath(from.transform, source);
                Transform target = path.Length == 0 ? body.transform : body.transform.Find(path);
                if (target == null || !(target.GetComponent(from.GetType()) is Renderer to)) continue;
                to.sharedMaterials = from.sharedMaterials;
                if (from is SkinnedMeshRenderer skinnedFrom && to is SkinnedMeshRenderer skinnedTo)
                    skinnedTo.sharedMesh = skinnedFrom.sharedMesh;
                else if (from.TryGetComponent(out MeshFilter meshFrom) && to.TryGetComponent(out MeshFilter meshTo))
                    meshTo.sharedMesh = meshFrom.sharedMesh;
            }
            return body;
        }

        private static Bounds RendererBounds(GameObject body)
        {
            var renderers = body.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(body.transform.position, Vector3.one);
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // A flat strip from x0 to x1 whose UVs count grid tiles, so the material's offset moves
        // the grid by whole metres / TileMetres.
        private Material CreateFloor(float x0, float x1, float halfWidth)
        {
            var mesh = new Mesh { name = "ReviewFloor", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[]
            {
                new Vector3(x0, 0f, -halfWidth), new Vector3(x1, 0f, -halfWidth),
                new Vector3(x0, 0f, halfWidth), new Vector3(x1, 0f, halfWidth),
            };
            mesh.uv = mesh.vertices.Select(v => new Vector2((v.x - x0) / TileMetres, v.z / TileMetres)).ToArray();
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.DontSave };
            material.mainTexture = grid;
            owned.Add(mesh);
            owned.Add(material);

            var floor = new GameObject("Floor", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
            floor.transform.SetParent(root.transform, false);
            floor.GetComponent<MeshFilter>().sharedMesh = mesh;
            floor.GetComponent<MeshRenderer>().sharedMaterial = material;
            return material;
        }

        private static Texture2D GridTexture()
        {
            var texture = new Texture2D(TilePixels, TilePixels, TextureFormat.RGBA32, true)
            {
                name = "ReviewGrid",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            var pixels = new Color[TilePixels * TilePixels];
            for (int y = 0; y < TilePixels; y++)
            for (int x = 0; x < TilePixels; x++)
                pixels[y * TilePixels + x] = x < LinePixels || y < LinePixels ? LineColour : FloorColour;
            texture.SetPixels(pixels);
            texture.Apply(true);
            return texture;
        }
    }
}
