using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;

namespace SpaceGame.World
{
    /// <summary>
    /// Scene-level driver for the cave system.
    ///
    /// Two source modes:
    ///   • <see cref="profile"/> assigned  — full profile-driven pipeline (shape + decoration + liquid +
    ///     material). Recommended. Different profile = totally different-feeling cave.
    ///   • <see cref="profile"/> null      — legacy inline-<see cref="legacyShapeSettings"/> path. Generates
    ///     a shape-only cave with the previously-existing cluster lights. Keeps scenes baked under the
    ///     old workflow working.
    ///
    /// Two runtime modes (orthogonal to the source):
    ///   • Live generation (designer iteration) — calls <see cref="CaveGenerator"/> at runtime,
    ///     instantiates the mesh as a child, runs decoration + liquid passes, optionally bakes a NavMesh
    ///     on the spot. Slow on scene transition.
    ///   • Baked assets (shipping) — assign <see cref="bakedMesh"/> + <see cref="bakedNavMeshData"/> in
    ///     the inspector. At runtime we just spawn the mesh and attach the prebuilt navmesh. Use the
    ///     editor "Bake &amp; Save" button to produce these. Decoration + liquid still run live (they
    ///     depend on the profile and are cheap).
    /// </summary>
    [DisallowMultipleComponent]
    public class CaveSpawner : MonoBehaviour
    {
        [Header("Profile (recommended)")]
        [Tooltip("CaveProfile ScriptableObject. Defines shape, decoration rules, liquid, material/shader.")]
        [SerializeField] private CaveProfile profile;

        [Header("Legacy inline shape (used when no profile is assigned)")]
        [Tooltip("Only consulted when 'profile' above is null. Existing inline-config workflow.")]
        [SerializeField] private CaveGenerationSettings legacyShapeSettings = new CaveGenerationSettings();

        [Header("Baked assets (preferred at runtime)")]
        [Tooltip("Pre-baked cave mesh. If set, runtime uses this instead of generating a new cave. Produce via the inspector 'Bake & Save' button.")]
        [SerializeField] private Mesh bakedMesh;

        [Tooltip("Pre-baked NavMeshData matching bakedMesh. If set, attached instead of running a fresh bake.")]
        [SerializeField] private NavMeshData bakedNavMeshData;

        [Tooltip("Entrance mouth-node centre captured at bake time. Used by the baked path (which has no live graph) to place the entrance anchor / spawn / exit cut.")]
        [SerializeField] private Vector3 bakedEntranceCenter;

        [Tooltip("Entrance tube radius captured at bake time.")]
        [SerializeField] private float bakedEntranceRadius;

        [Tooltip("Entrance tube axis at the mouth (cave-local), captured at bake time. Orients the exit cut slab.")]
        [SerializeField] private Vector3 bakedEntranceTubeAxis = Vector3.up;

        [Tooltip("True once a bake has captured a valid entrance.")]
        [SerializeField] private bool bakedEntranceValid;

        [Header("Entrance")]
        [Tooltip("Anchor id assigned to the InteriorAnchor spawned at the cave entrance. Must match the InteriorScene's spawnAnchorId (default 'entrance').")]
        [SerializeField] private string entranceAnchorId = "entrance";

        [Tooltip("Optional dark prefab placed over the entrance mouth to cap the cave. If null, a procedural black box (wider than the cave) is generated instead.")]
        [SerializeField] private GameObject exitCoverPrefab;

        [Tooltip("Extra width/height (metres) the procedural dark exit cover extends past the entrance corridor radius, so it fully cuts through the cave wall.")]
        [SerializeField] private float exitCoverOversize = 6f;

        [Header("Lifecycle")]
        [SerializeField] private bool generateOnStart = true;
        [Tooltip("Randomise the seed on every Start. Ignored when baked assets are assigned.")]
        [SerializeField] private bool randomSeedOnStart = false;

        [Header("Debug")]
        [SerializeField] private bool drawGraphGizmos = true;
        [SerializeField] private bool drawBoundsGizmo = true;

        GameObject _caveRoot;
        GameObject _entranceRoot;
        CaveGenerationResult _last;
        NavMeshDataInstance _navMeshInstance;
        bool _fogOverrideActive;
        bool _fogPrevEnabled;
        Color _fogPrevColor;
        float _fogPrevDensity;

        public CaveGenerationResult LastResult => _last;
        public CaveProfile Profile => profile;
        public CaveGenerationSettings ActiveShapeSettings => profile != null ? profile.shape : legacyShapeSettings;
        public bool HasBakedAssets => bakedMesh != null && bakedNavMeshData != null;

        // Generate in Awake (not Start) so the cave — and crucially its EntranceAnchor — exists
        // before InteriorManager's scene-load-completed callback runs PlacePlayerAtAnchor. If the
        // anchor were spawned in Start(), an additive interior load that completes the same frame
        // could place the player before the anchor registered, dropping them at the origin.
        void Awake()
        {
            if (!generateOnStart) return;
            if (HasBakedAssets) SpawnBaked();
            else GenerateNow();
        }

        void OnDestroy()
        {
            if (_navMeshInstance.valid) _navMeshInstance.Remove();
            RestoreFog();
        }

        /// <summary>
        /// Runtime path for shipping: instantiate the prebaked mesh, attach the prebaked navmesh, and
        /// still run the cheap decoration / liquid / material passes from the profile.
        /// O(milliseconds) for mesh+navmesh; decoration cost scales with mesh size and rule density.
        /// </summary>
        public void SpawnBaked()
        {
            ClearPrevious();
            ApplyFogOverride();

            _caveRoot = new GameObject("CaveMesh_baked");
            _caveRoot.transform.SetParent(transform, worldPositionStays: false);
            int layer = ResolveCaveLayer();
            if (layer >= 0) _caveRoot.layer = layer;

            var mf = _caveRoot.AddComponent<MeshFilter>();
            mf.sharedMesh = bakedMesh;

            var mr = _caveRoot.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ResolvePrimaryMaterial();

            var mc = _caveRoot.AddComponent<MeshCollider>();
            mc.sharedMesh = bakedMesh;

            _navMeshInstance = NavMesh.AddNavMeshData(bakedNavMeshData);

            // Baked-mode pipeline still benefits from the profile's runtime passes — they're cheap.
            // We can't run liquid pool detection without a density field; baked path skips it.
            SpawnClusterLights(mc);
            RunDecorationPass(bakedMesh, _caveRoot.transform, null, layer);
            SpawnEntranceFromBake(layer);
        }

        [ContextMenu("Generate Now")]
        public void GenerateNow()
        {
            var shape = ActiveShapeSettings;
            if (randomSeedOnStart) shape.seed = Random.Range(int.MinValue, int.MaxValue);

            ClearPrevious();
            ApplyFogOverride();

            float t0 = Time.realtimeSinceStartup;
            _last = profile != null ? CaveGenerator.Generate(profile) : CaveGenerator.Generate(shape);
            float genMs = (Time.realtimeSinceStartup - t0) * 1000f;

            _caveRoot = new GameObject($"CaveMesh_seed{_last.Seed}");
            _caveRoot.transform.SetParent(transform, worldPositionStays: false);
            int layer = ResolveCaveLayer();
            if (layer >= 0) _caveRoot.layer = layer;

            var mf = _caveRoot.AddComponent<MeshFilter>();
            mf.sharedMesh = _last.Mesh;

            var mr = _caveRoot.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ResolvePrimaryMaterial();

            var mc = _caveRoot.AddComponent<MeshCollider>();
            mc.sharedMesh = _last.Mesh;

            if (shape.bakeNavMeshOnGenerate) BakeNavMeshLive();

            // Order: liquid first (so vegetation rules can sense waterlines), then decoration, then lights.
            SpawnLiquidPools(layer);
            int decorPlaced = RunDecorationPass(_last.Mesh, _caveRoot.transform, _last.LiquidPools, layer);
            SpawnClusterLights(mc);
            SpawnEntranceFromGraph(layer);

            Debug.Log($"[CaveSpawner] generated cave: {_last.Graph.Rooms.Count} rooms, " +
                      $"{_last.Graph.Corridors.Count} corridors, " +
                      $"{_last.Mesh.vertexCount} verts, {_last.Mesh.triangles.Length / 3} tris, " +
                      $"{_last.LiquidPools.Count} pools, {decorPlaced} decor instances " +
                      $"in {genMs:F0} ms (seed {_last.Seed}).");
        }

        /// <summary>
        /// Editor-only entry point. Runs the full generate + bake pipeline and exposes the produced
        /// mesh + NavMeshData so the editor script can save them as assets. Returns true on success.
        /// </summary>
        public bool EditorBuildForBaking(out Mesh mesh, out NavMeshData navMeshData)
        {
            mesh = null;
            navMeshData = null;

            var shape = ActiveShapeSettings;

            ClearPrevious();

            _last = profile != null ? CaveGenerator.Generate(profile) : CaveGenerator.Generate(shape);
            if (_last == null || _last.Mesh == null) return false;

            _caveRoot = new GameObject($"CaveMesh_seed{_last.Seed}");
            _caveRoot.transform.SetParent(transform, worldPositionStays: false);
            _caveRoot.AddComponent<MeshFilter>().sharedMesh = _last.Mesh;
            var mr = _caveRoot.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ResolvePrimaryMaterial();
            _caveRoot.AddComponent<MeshCollider>().sharedMesh = _last.Mesh;

            var surface = GetComponent<NavMeshSurface>();
            if (surface == null) surface = gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            // Capture the entrance so the baked runtime path (which has no live graph) can still
            // place the anchor / spawn / exit cover.
            CaptureBakedEntrance();

            mesh = _last.Mesh;
            navMeshData = surface.navMeshData;
            return navMeshData != null;
        }

        void CaptureBakedEntrance()
        {
            bakedEntranceValid = false;
            if (_last == null || _last.Graph == null) return;
            var entrance = _last.Graph.EntranceRoom;
            if (entrance == null) return;
            bakedEntranceCenter = entrance.Value.Center;
            bakedEntranceRadius = entrance.Value.Radius;
            bakedEntranceTubeAxis = EntranceTubeAxisAtMouth(_last.Graph);
            bakedEntranceValid = true;
        }

        public void AssignBakedAssets(Mesh mesh, NavMeshData data)
        {
            bakedMesh = mesh;
            bakedNavMeshData = data;
        }

        // -------------------------------------------------------------------------
        // Sub-passes
        // -------------------------------------------------------------------------

        int RunDecorationPass(Mesh mesh, Transform root, System.Collections.Generic.List<LiquidPool> pools, int layer)
        {
            if (profile == null || profile.decoration == null || !profile.decoration.enabled) return 0;
            int seed = _last != null ? _last.Seed : ActiveShapeSettings.seed;
            return DecorationScatterer.Scatter(profile.decoration, new DecorationScatterer.ScatterContext
            {
                Mesh = mesh,
                Parent = root,
                Graph = _last?.Graph,
                LiquidPools = pools,
                Seed = seed,
                CaveLayer = layer,
            });
        }

        void SpawnLiquidPools(int layer)
        {
            if (profile == null || profile.liquid == null || !profile.liquid.enabled) return;
            if (_last == null || _last.LiquidPools == null || _last.LiquidPools.Count == 0) return;
            LiquidPoolSpawner.Spawn(_last.LiquidPools, profile.liquid, _caveRoot.transform, layer);
        }

        void SpawnClusterLights(MeshCollider caveCollider)
        {
            var mat = profile != null ? profile.material : null;
            int count = mat != null ? mat.clusterLightCount : 0;
            if (count <= 0 || caveCollider == null) return;
            var mesh = caveCollider.sharedMesh;
            if (mesh == null) return;

            var verts = mesh.vertices;
            var tris  = mesh.triangles;
            var normals = mesh.normals;
            if (tris.Length == 0) return;

            int triCount = tris.Length / 3;
            var rng = new System.Random(_last != null ? _last.Seed : ActiveShapeSettings.seed);
            var caveTransform = _caveRoot.transform;
            Vector3 worldCenter = caveCollider.bounds.center;

            var wallTris = new System.Collections.Generic.List<(Vector3 center, Vector3 inwardNormal)>();
            for (int t = 0; t < triCount; t++)
            {
                int i0 = tris[t * 3 + 0], i1 = tris[t * 3 + 1], i2 = tris[t * 3 + 2];
                Vector3 localCenter = (verts[i0] + verts[i1] + verts[i2]) / 3f;
                Vector3 localNormal = normals != null && normals.Length == verts.Length
                    ? (normals[i0] + normals[i1] + normals[i2]).normalized
                    : Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]).normalized;
                Vector3 worldPos = caveTransform.TransformPoint(localCenter);
                Vector3 worldNormal = caveTransform.TransformDirection(localNormal).normalized;
                Vector3 toCenter = (worldCenter - worldPos).normalized;
                Vector3 inward = Vector3.Dot(worldNormal, toCenter) > 0f ? worldNormal : -worldNormal;
                if (Mathf.Abs(inward.y) <= mat.clusterLightMaxNormalY)
                    wallTris.Add((worldPos, inward));
            }
            if (wallTris.Count == 0) return;

            var lightsRoot = new GameObject("ClusterLights");
            lightsRoot.transform.SetParent(caveTransform, worldPositionStays: false);
            int interiorLayer = ResolveCaveLayer();

            for (int i = 0; i < count; i++)
            {
                var pick = wallTris[rng.Next(wallTris.Count)];
                Vector3 lightPos = pick.center + pick.inwardNormal * 0.4f;

                var lightGo = new GameObject($"ClusterLight_{i}");
                lightGo.transform.SetParent(lightsRoot.transform, worldPositionStays: true);
                lightGo.transform.position = lightPos;
                if (interiorLayer >= 0) lightGo.layer = interiorLayer;

                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = mat.clusterLightColor;
                light.intensity = mat.clusterLightIntensity;
                light.range = mat.clusterLightRange;
                light.shadows = LightShadows.None;
            }
        }

        // -------------------------------------------------------------------------
        // Entrance — anchor (transition system) + safe player spawn + dark exit cover
        // -------------------------------------------------------------------------

        /// <summary>Live-generation path: read the entrance tube straight off the graph.</summary>
        void SpawnEntranceFromGraph(int layer)
        {
            if (_last == null || _last.Graph == null) return;
            var entrance = _last.Graph.EntranceRoom;
            if (entrance == null) return;
            BuildEntrance(entrance.Value.Center, entrance.Value.Radius,
                          EntranceTubeAxisAtMouth(_last.Graph), layer);
        }

        /// <summary>Baked path: no live graph, use the entrance captured at bake time.</summary>
        void SpawnEntranceFromBake(int layer)
        {
            if (!bakedEntranceValid)
            {
                Debug.LogWarning("[CaveSpawner] Baked cave has no captured entrance — re-bake to generate one. " +
                                 "Skipping entrance anchor / exit cover.");
                return;
            }
            BuildEntrance(bakedEntranceCenter, bakedEntranceRadius, bakedEntranceTubeAxis, layer);
        }

        /// <summary>
        /// Direction the entrance tube points at its mouth — from the second-to-last entrance node
        /// to the mouth node. The exit slab is laid perpendicular to this so it slices the tube
        /// straight across (a clean circular cross-section). Falls back to up if it can't be found.
        /// </summary>
        static Vector3 EntranceTubeAxisAtMouth(CaveGraph graph)
        {
            int mouthId = graph.EntranceRoomId;
            if (mouthId < 0) return Vector3.up;
            // The mouth's single corridor connects it to the previous tube node.
            foreach (var c in graph.Corridors)
            {
                if (c.Kind != CorridorKind.Entrance) continue;
                int other = c.FromRoomId == mouthId ? c.ToRoomId
                          : c.ToRoomId == mouthId ? c.FromRoomId : -1;
                if (other < 0) continue;
                Vector3 axis = graph.Rooms[mouthId].Center - graph.Rooms[other].Center;
                if (axis.sqrMagnitude > 1e-4f) return axis.normalized;
            }
            return Vector3.up;
        }

        /// <summary>
        /// Builds the entrance fixtures under a single child root:
        ///   • an <see cref="InteriorAnchor"/> on the tunnel floor just inside the mouth — the
        ///     spawn / transition target,
        ///   • a dark "exit cut" slab laid perpendicular to the tube axis, slicing the entrance
        ///     spaghetti straight across so its mouth reads as a clean circular cross-section.
        /// <paramref name="mouthCenter"/> is the graph-space mouth-node centre, <paramref name="tubeRadius"/>
        /// the uniform tube radius, and <paramref name="tubeAxisLocal"/> the (cave-local) direction
        /// the tube points at the mouth. The anchor floor Y is found by raycasting the cave collider
        /// so the player spawns standing on solid ground rather than falling into the void.
        /// </summary>
        void BuildEntrance(Vector3 mouthCenter, float tubeRadius, Vector3 tubeAxisLocal, int layer)
        {
            var caveTransform = _caveRoot.transform;

            _entranceRoot = new GameObject("CaveEntrance");
            _entranceRoot.transform.SetParent(caveTransform, worldPositionStays: false);
            if (layer >= 0) _entranceRoot.layer = layer;

            Vector3 worldMouth = caveTransform.TransformPoint(mouthCenter);

            // Tube axis in world space, pointing OUT of the mouth (up the tube, away from the cave).
            Vector3 outAxis = caveTransform.TransformDirection(tubeAxisLocal);
            if (outAxis.sqrMagnitude < 1e-4f) outAxis = Vector3.up;
            outAxis.Normalize();
            // "Down the tube" = into the cave = opposite the out-axis.
            Vector3 intoCave = -outAxis;

            // --- Safe spawn point -------------------------------------------------
            // Spawn the player a little way DOWN the tube from the mouth (so they're inside, past
            // the exit slab), then drop them onto the meshed floor with a downward raycast.
            Vector3 spawnProbe = worldMouth + intoCave * (tubeRadius * 1.5f);
            Vector3 floorPoint = spawnProbe + Vector3.down * (tubeRadius * 0.9f);
            Vector3 rayOrigin = spawnProbe + Vector3.up * (tubeRadius * 0.8f);
            var caveCollider = _caveRoot.GetComponent<MeshCollider>();
            if (caveCollider != null &&
                caveCollider.Raycast(new Ray(rayOrigin, Vector3.down), out var hit, tubeRadius * 5f))
            {
                floorPoint = hit.point;
            }
            // Lift slightly so a CharacterController capsule isn't spawned intersecting the ground.
            Vector3 spawnPoint = floorPoint + Vector3.up * 0.5f;

            var anchorGo = new GameObject("EntranceAnchor");
            anchorGo.transform.SetParent(_entranceRoot.transform, worldPositionStays: true);
            anchorGo.transform.position = spawnPoint;
            // Face the player down the tube into the cave (horizontal heading only).
            Vector3 facing = intoCave;
            facing.y = 0f;
            anchorGo.transform.rotation = facing.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(facing.normalized, Vector3.up)
                : Quaternion.identity;
            if (layer >= 0) anchorGo.layer = layer;

            var anchor = anchorGo.AddComponent<InteriorAnchor>();
            anchor.SetAnchorId(entranceAnchorId);

            // --- Dark exit cut ----------------------------------------------------
            // A dark slab laid PERPENDICULAR to the tube axis (slab normal = tube axis), so it
            // slices the entrance spaghetti straight across. The cross-section it exposes is a
            // clean circle of the tube. Sits at the mouth; the player walks/clicks it to exit.
            Vector3 coverCenter = worldMouth;
            // LookRotation's forward = slab local +Z (its thin depth axis). Aim it along the tube
            // axis so the slab's broad face is square to the tube — a clean 90° cross-cut.
            Quaternion coverRot = Quaternion.LookRotation(outAxis, Vector3.up);
            float coverSize = (tubeRadius + Mathf.Max(0f, exitCoverOversize)) * 2f;
            SpawnExitCover(coverCenter, coverRot, coverSize, layer);
        }

        /// <summary>
        /// Spawns the dark exit-cut slab. Oriented so its thin axis runs along the tube, the broad
        /// face (coverSize × coverSize) slices the entrance spaghetti perpendicular — cutting fully
        /// through it and exposing a circular cross-section. Carries a <see cref="CaveExitCover"/>
        /// so it works as the exit both by interaction click and by walking into the volume.
        /// </summary>
        void SpawnExitCover(Vector3 worldCenter, Quaternion rotation, float size, int layer)
        {
            GameObject cover;
            if (exitCoverPrefab != null)
            {
                cover = Instantiate(exitCoverPrefab, worldCenter, rotation, _entranceRoot.transform);
                cover.name = "ExitCover";
            }
            else
            {
                // Procedural fallback: a thin black slab cutting straight across the tube mouth,
                // wider/taller than the passage so it visually slices through and seals the cave.
                cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cover.name = "ExitCover (procedural)";
                cover.transform.SetParent(_entranceRoot.transform, worldPositionStays: true);
                cover.transform.SetPositionAndRotation(worldCenter, rotation);
                // Local axes after the LookRotation: X & Y = broad cross-cut face, Z = thin depth
                // running along the tube axis.
                cover.transform.localScale = new Vector3(size, size, 0.5f);

                var mr = cover.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = DarkExitMaterial();
            }

            // The collider must stay a trigger — CaveExitCover uses it for the walk-in volume and
            // for interaction raycasts. It must not physically block the player.
            var col = cover.GetComponent<Collider>();
            if (col == null) col = cover.AddComponent<BoxCollider>();
            col.isTrigger = true;

            if (cover.GetComponent<CaveExitCover>() == null)
                cover.AddComponent<CaveExitCover>();

            if (layer >= 0) SetLayerRecursive(cover, layer);
        }

        Material _darkExitMaterial;
        Material DarkExitMaterial()
        {
            if (_darkExitMaterial != null) return _darkExitMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _darkExitMaterial = new Material(shader) { name = "CaveExitCoverDark" };
            if (_darkExitMaterial.HasProperty("_BaseColor")) _darkExitMaterial.SetColor("_BaseColor", Color.black);
            if (_darkExitMaterial.HasProperty("_Color")) _darkExitMaterial.SetColor("_Color", Color.black);
            return _darkExitMaterial;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }

        void BakeNavMeshLive()
        {
            var surface = GetComponent<NavMeshSurface>();
            if (surface == null) surface = gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        /// <summary>Prefix shared by every generated cave-root GameObject ("CaveMesh_seed123", "CaveMesh_baked").</summary>
        const string CaveRootPrefix = "CaveMesh_";

        void ClearPrevious()
        {
            // Sweep EVERY cave root under this spawner, not just the tracked _caveRoot reference.
            // The reference is lost across domain reloads, scene re-opens, and play-mode toggles —
            // relying on it alone lets a stale "CaveMesh_*" survive and a fresh generate would then
            // append a duplicate cave. Destroying by name prefix guarantees one cave at most.
            // _entranceRoot lives under a cave root, so it is destroyed along with it.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                if (!child.name.StartsWith(CaveRootPrefix)) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
            _caveRoot = null;
            _entranceRoot = null;

            if (_navMeshInstance.valid)
            {
                _navMeshInstance.Remove();
                _navMeshInstance = default;
            }
            var surface = GetComponent<NavMeshSurface>();
            if (surface != null && surface.navMeshData != null) surface.RemoveData();
        }

        // -------------------------------------------------------------------------
        // Material / layer / fog helpers
        // -------------------------------------------------------------------------

        Material ResolvePrimaryMaterial()
        {
            var m = profile != null && profile.material != null ? profile.material.primaryMaterial : null;
            return m != null ? m : DefaultCaveMaterial();
        }

        Material DefaultCaveMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = "DefaultCaveMaterial" };
            m.SetColor("_BaseColor", new Color(0.32f, 0.30f, 0.27f));
            m.SetFloat("_Smoothness", 0.15f);
            return m;
        }

        int ResolveCaveLayer()
        {
            string layerName = profile != null && profile.material != null ? profile.material.caveLayer : "Interior";
            if (string.IsNullOrEmpty(layerName)) return -1;
            int idx = LayerMask.NameToLayer(layerName);
            return idx >= 0 ? idx : -1;
        }

        void ApplyFogOverride()
        {
            var mat = profile != null ? profile.material : null;
            if (mat == null || !mat.overrideFog) return;
            _fogPrevEnabled = RenderSettings.fog;
            _fogPrevColor   = RenderSettings.fogColor;
            _fogPrevDensity = RenderSettings.fogDensity;
            RenderSettings.fog = true;
            RenderSettings.fogColor = mat.fogColor;
            RenderSettings.fogDensity = mat.fogDensity;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            _fogOverrideActive = true;
        }

        void RestoreFog()
        {
            if (!_fogOverrideActive) return;
            RenderSettings.fog = _fogPrevEnabled;
            RenderSettings.fogColor = _fogPrevColor;
            RenderSettings.fogDensity = _fogPrevDensity;
            _fogOverrideActive = false;
        }

        // -------------------------------------------------------------------------
        // Gizmos
        // -------------------------------------------------------------------------

        void OnDrawGizmosSelected()
        {
            var shape = ActiveShapeSettings;
            if (drawBoundsGizmo)
            {
                Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.4f);
                Gizmos.DrawWireCube(Vector3.zero, shape.halfExtents * 2f);
            }

            if (drawGraphGizmos && _last != null)
            {
                foreach (var r in _last.Graph.Rooms)
                {
                    Gizmos.color = r.Kind switch
                    {
                        RoomKind.BigChamber => new Color(1f, 0.4f, 0.2f, 0.6f),
                        RoomKind.Junction   => new Color(0.4f, 1f, 0.5f, 0.6f),
                        RoomKind.DeadEnd    => new Color(0.6f, 0.6f, 0.6f, 0.5f),
                        RoomKind.Entrance   => new Color(1f, 1f, 1f, 0.9f),
                        _                   => new Color(0.9f, 0.9f, 0.2f, 0.5f),
                    };
                    Gizmos.DrawWireSphere(transform.TransformPoint(r.Center), r.Radius);
                    if (r.CeilingLift > 0.01f)
                    {
                        Gizmos.color = new Color(1f, 0.6f, 0.3f, 0.4f);
                        Gizmos.DrawLine(transform.TransformPoint(r.Center),
                                        transform.TransformPoint(r.Center + Vector3.up * (r.Radius + r.CeilingLift)));
                    }
                }

                foreach (var c in _last.Graph.Corridors)
                {
                    Gizmos.color = c.Kind switch
                    {
                        CorridorKind.Shaft    => new Color(1f, 0.2f, 0.2f, 0.8f),
                        CorridorKind.Slot     => new Color(0.9f, 0.7f, 0.2f, 0.8f),
                        CorridorKind.Wide     => new Color(0.4f, 0.8f, 1f,  0.8f),
                        CorridorKind.Tight    => new Color(0.6f, 0.6f, 0.9f, 0.6f),
                        CorridorKind.Entrance => new Color(1f, 1f, 1f, 0.9f),
                        _                     => new Color(0.2f, 0.6f, 1f,  0.7f),
                    };
                    var a = transform.TransformPoint(_last.Graph.Rooms[c.FromRoomId].Center);
                    var b = transform.TransformPoint(_last.Graph.Rooms[c.ToRoomId].Center);
                    Gizmos.DrawLine(a, b);
                }

                if (_last.LiquidPools != null)
                {
                    Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.5f);
                    foreach (var p in _last.LiquidPools)
                    {
                        Vector3 c = transform.TransformPoint(new Vector3(p.HorizontalBounds.center.x, p.WaterlineY, p.HorizontalBounds.center.z));
                        Vector3 s = new Vector3(p.HorizontalBounds.size.x, 0.1f, p.HorizontalBounds.size.z);
                        Gizmos.DrawWireCube(c, s);
                    }
                }
            }
        }
    }
}
