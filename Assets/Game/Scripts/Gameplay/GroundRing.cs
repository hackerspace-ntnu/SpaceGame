// A circle drawn ON the ground, following whatever the ground does underneath it.
//
// Built for the conjurer staff's aim marker, but deliberately not staff-shaped: anything that has
// to say "this patch of ground, this radius" -- a blast warning, a placement footprint, a scan
// area -- wants the same thing, and StrikeTelegraph could adopt it the day its ring is turned
// back on.
//
// ---- why this is not the telegraph's ring -------------------------------------
//
// StrikeTelegraph draws a RIGID annulus and drops it with one downward raycast at its centre
// (see its Place). That is honest on a flat plain and a lie everywhere else: on a dune the far
// side of the ring is buried a metre under the sand and the near side floats a metre over it, and
// a warning that floats is a warning nobody believes. It also never reads the surface normal, so
// tilting it would only trade one wrong answer for another -- a plane fitted to a slope still
// cuts into anything curved, and terrain here is curved everywhere.
//
// So the ring is not placed, it is RESAMPLED. Every vertex asks the ground its own question at
// its own X/Z, and the band bends because its vertices do. That is the only version of this that
// survives a hill, and it is why the mesh is rewritten rather than transformed.
//
// ---- where the heights come from ----------------------------------------------
//
// TerrainProbe first, which reads the heightmap directly and cannot be shadowed by a roof, a
// vehicle or the player's own body -- and, being no raycast at all, is cheap enough to run once
// per vertex per frame without thinking about it.
//
// A raycast is the fallback rather than the rule, and only for the case the heightmap genuinely
// cannot answer: an interior, a cave, or standing on top of a rock, where the terrain surface is
// somewhere below the thing actually being aimed at. That case is DETECTED rather than guessed --
// the caller hands us the point it aimed at, and if the heightmap disagrees with that point's own
// height by more than a tolerance, the ground here is not terrain and every vertex is raycast
// instead. Doing it that way keeps the common case at zero raycasts and the rare case correct,
// instead of paying 72 raycasts a frame everywhere to fix the dune nobody is standing on.
//
// What it still does not do: wrap around a small prop standing inside the circle. The ring is a
// closed loop of samples, so a boulder between two of them is stepped over, not hugged. For a
// blast radius that is the right trade -- the damage is a sphere and does not care either.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.World.Safety;

namespace SpaceGame.Gameplay
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class GroundRing : MonoBehaviour
    {
        [Header("Shape")]
        [Tooltip("Samples around the ring. Each one is a ground query and a pair of vertices, so " +
                 "this is both how round the circle looks and how closely it follows a slope.")]
        [SerializeField] private int segments = 72;

        [Tooltip("Width of the band as a fraction of the radius. The OUTER edge is the radius " +
                 "being described, so the band grows inwards -- a ring that straddled the radius " +
                 "would cover ground it does not mean.")]
        [SerializeField] [Range(0.02f, 0.5f)] private float thickness = 0.14f;

        [Tooltip("Extra width once locked, as a multiple of thickness. The ring snapping wider is " +
                 "half of what makes a commit readable at a glance.")]
        [SerializeField] private float lockedThickness = 1.8f;

        [Header("Ground")]
        [Tooltip("Clearance above the surface, in metres. The material does not write depth, so a " +
                 "ring sitting exactly on the ground is depth-fought out of existence.")]
        [SerializeField] private float groundOffset = 0.08f;

        [Tooltip("How far above and below a sample to look for ground on the raycast path.")]
        [SerializeField] private float groundProbe = 40f;

        [Tooltip("What counts as ground for the raycast fallback. Triggers are always ignored.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("How far the heightmap may disagree with the aimed-at point before this decides " +
                 "the surface is not terrain -- a roof, a rock, a vehicle -- and switches the " +
                 "whole ring to raycasts.")]
        [SerializeField] private float terrainTolerance = 1.2f;

        [Header("Refresh")]
        [Tooltip("How far the centre must move before the ring is resampled, in metres. Aiming at " +
                 "one spot costs nothing.")]
        [SerializeField] private float moveEpsilon = 0.05f;

        [Tooltip("Resample anyway this often, in seconds. Terrain streams in after the ring may " +
                 "already be sitting on it, and a chunk arriving must not leave a flat ring " +
                 "hanging over the dune it was drawn before.")]
        [SerializeField] private float resampleInterval = 0.25f;

        [Header("Look")]
        [SerializeField] private Color trackingColour = new Color(0.24f, 0.55f, 1f);
        [SerializeField] private Color lockedColour = new Color(0.85f, 0.95f, 1f);

        [Tooltip("Drives the material's _Ignite, which the shader squares. 0 is invisible.")]
        [SerializeField] private float startIgnite = 0.55f;
        [SerializeField] private float endIgnite = 1f;

        [Tooltip("Optional. Lit to match, so the ring throws light on the ground it marks.")]
        [SerializeField] private Light glow;

        [SerializeField] private float startLightIntensity = 1.2f;
        [SerializeField] private float endLightIntensity = 7f;

        private static readonly int IgniteId = Shader.PropertyToID("_Ignite");
        private static readonly int BeamLengthId = Shader.PropertyToID("_BeamLength");
        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        private static readonly int BoltColorId = Shader.PropertyToID("_BoltColor");

        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _props;
        private Mesh _mesh;

        private readonly List<Vector3> _verts = new List<Vector3>();
        private int _builtSegments = -1;

        private Vector3 _centre;
        private float _radius = 1f;
        private bool _visible;
        private bool _locked;
        private float _progress;
        private bool _dirty = true;
        private float _sinceResample;

        /// <summary>Where the ring is centred. The height is the caller's aim point, unchanged.</summary>
        public Vector3 Point => _centre;

        /// <summary>Radius of the ring's outer edge, which is the radius it is describing.</summary>
        public float Radius => _radius;

        /// <summary>Whether the ring is currently drawn.</summary>
        public bool Visible => _visible;

        /// <summary>
        /// Put the ring here, at this size, and draw it.
        ///
        /// <paramref name="centre"/> is the point that was aimed at, height included — that height
        /// is what tells this whether it is looking at terrain or at something standing on it.
        /// </summary>
        public void Show(Vector3 centre, float radius)
        {
            radius = Mathf.Max(0.05f, radius);

            if (!_visible || (centre - _centre).sqrMagnitude > moveEpsilon * moveEpsilon ||
                !Mathf.Approximately(radius, _radius))
                _dirty = true;

            _centre = centre;
            _radius = radius;
            _visible = true;

            EnsureParts();
            _renderer.enabled = true;
            if (glow != null) glow.enabled = true;
        }

        /// <summary>Stop drawing. Cheap and idempotent — the mesh is kept for the next Show.</summary>
        public void Hide()
        {
            _visible = false;

            EnsureParts();
            _renderer.enabled = false;
            if (glow != null) glow.enabled = false;
        }

        /// <summary>
        /// Commit. The ring stops being a suggestion and becomes a promise, and it says so loudly:
        /// wider, whiter, brighter. A lock the player cannot see is a lock they cannot play against.
        /// </summary>
        public void SetLocked(bool locked)
        {
            if (_locked == locked) return;
            _locked = locked;
            _dirty = true;   // the band changes width, so the vertices change
            Apply();
        }

        /// <summary>How far through the wind-up, 0 to 1. Drives the escalation, nothing else.</summary>
        public void SetProgress(float t01)
        {
            _progress = Mathf.Clamp01(t01);
            Apply();
        }

        /// <summary>
        /// Resample the ground and rewrite the mesh now, instead of on the next frame that notices.
        ///
        /// Ordinarily nothing needs this — <see cref="Show"/> marks the ring dirty and LateUpdate
        /// does the work. It is here for the two callers that cannot wait for a frame: a test, and
        /// anything that has just moved the ground itself.
        /// </summary>
        public void Rebuild()
        {
            EnsureParts();
            Resample();
        }

        private void Awake()
        {
            EnsureParts();
            Apply();
        }

        private void LateUpdate()
        {
            if (!_visible) return;

            _sinceResample += Time.deltaTime;
            if (_sinceResample >= resampleInterval)
            {
                _sinceResample = 0f;
                _dirty = true;
            }

            if (_dirty) Resample();
        }

        private void EnsureParts()
        {
            if (_filter == null) _filter = GetComponent<MeshFilter>();
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            if (_props == null) _props = new MaterialPropertyBlock();

            if (_mesh == null)
            {
                // Not shared and not an asset: every ring rewrites its own vertices every time the
                // aim moves, so two rings on one mesh would fight over the same buffer.
                _mesh = new Mesh { name = "GroundRing" };
                _mesh.MarkDynamic();
                _filter.sharedMesh = _mesh;
            }

            if (_builtSegments != Mathf.Max(8, segments)) BuildTopology();
        }

        /// Triangles, UVs and vertex colours — everything that does NOT change when the ground does.
        ///
        /// Two rings of vertices, outer then inner, and the band between them is one quad per
        /// segment emitted twice with opposite winding. Double-sided because the ring lies within a
        /// few centimetres of the ground and the camera ends up underneath it on any rise; a
        /// single-sided ring simply vanishes from those angles, which is the one thing a marker
        /// must never do.
        private void BuildTopology()
        {
            int n = Mathf.Max(8, segments);
            _builtSegments = n;

            var uvs = new Vector2[n * 2];
            var colours = new Color[n * 2];
            var tris = new int[n * 12];

            for (int i = 0; i < n; i++)
            {
                // Deliberately squeezed into 0.02..0.98 rather than run 0..1. SpaceGame/LightningBeam
                // tapers to nothing at u = 0 and flares at u = 1, which are its muzzle and its
                // impact — meaningful on a bolt with two ends, and on a closed loop they would put
                // a dark notch and a hot spot at the same arbitrary azimuth forever.
                float u = 0.02f + 0.96f * (i / (float)n);

                // v spans the band, and the shader reads |v*2-1| as distance from the centreline —
                // so 1 and 0 are the two EDGES and the bright filament interpolates down the middle.
                uvs[i] = new Vector2(u, 1f);
                uvs[n + i] = new Vector2(u, 0f);

                colours[i] = Color.white;
                colours[n + i] = Color.white;
            }

            int t = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int o0 = i, o1 = j, i0 = n + i, i1 = n + j;

                tris[t++] = o0; tris[t++] = i0; tris[t++] = o1;
                tris[t++] = o1; tris[t++] = i0; tris[t++] = i1;

                tris[t++] = o1; tris[t++] = i0; tris[t++] = o0;
                tris[t++] = i1; tris[t++] = i0; tris[t++] = o1;
            }

            // Vertices first and at the right count, because SetTriangles validates indices against
            // whatever the mesh currently holds.
            _verts.Clear();
            for (int i = 0; i < n * 2; i++) _verts.Add(Vector3.zero);

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, uvs);
            _mesh.SetColors(colours);
            _mesh.SetTriangles(tris, 0);

            _dirty = true;
        }

        /// Ask the ground where it is, once per segment, and rewrite the vertices.
        private void Resample()
        {
            _dirty = false;

            EnsureParts();

            transform.position = _centre;
            transform.rotation = Quaternion.identity;

            int n = _builtSegments;
            float outer = _radius;
            float inner = _radius * (1f - Mathf.Clamp(
                thickness * (_locked ? Mathf.Max(1f, lockedThickness) : 1f), 0.02f, 0.9f));

            // The one decision that costs nothing and saves 72 raycasts: does the heightmap agree
            // with the point the caller actually aimed at? If it does, this is open terrain and the
            // heightmap can answer for the whole ring. If it does not, we are on a roof, a rock, a
            // vehicle or inside something, and the heightmap is answering about ground that is not
            // the surface being marked.
            bool onTerrain = TerrainProbe.TryGetTerrainHeight(_centre, out float centreTerrainY) &&
                             Mathf.Abs(centreTerrainY - _centre.y) <= terrainTolerance;

            _verts.Clear();

            // Outer and inner rings are filled in two passes over the same heights, so the two
            // vertices at one azimuth always sit at exactly the same height. Sampling them
            // separately would shear the band on a slope — it would be two circles at different
            // heights rather than one band lying on the hill.
            for (int pass = 0; pass < 2; pass++)
            {
                float r = pass == 0 ? outer : inner;

                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    Vector3 offset = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    Vector3 world = _centre + offset;

                    float y = SampleGround(world, onTerrain);

                    // Local space: the object sits at the centre with no rotation, so this is the
                    // offset plus however far the ground moved the height.
                    _verts.Add(new Vector3(offset.x, y + groundOffset - _centre.y, offset.z));
                }
            }

            _mesh.SetVertices(_verts);
            _mesh.RecalculateBounds();

            Apply();
        }

        /// <summary>
        /// Ground height at one sample, or the centre's own height when nothing answers — a flat
        /// ring is a worse picture than a curved one, and a ring collapsed to y = 0 is not a picture
        /// at all.
        /// </summary>
        private float SampleGround(Vector3 world, bool onTerrain)
        {
            if (onTerrain && TerrainProbe.TryGetTerrainHeight(world, out float terrainY))
                return terrainY;

            if (Physics.Raycast(world + Vector3.up * groundProbe, Vector3.down,
                                out RaycastHit hit, groundProbe * 2f, groundMask,
                                QueryTriggerInteraction.Ignore))
                return hit.point.y;

            return _centre.y;
        }

        /// Colour and brightness. Squared escalation, because the last moments are what the player
        /// reads as "now" and a linear ramp reads as a light being turned up.
        private void Apply()
        {
            if (_renderer == null || _props == null) return;

            float e = _progress * _progress;
            Color colour = _locked ? lockedColour : trackingColour;

            _renderer.GetPropertyBlock(_props);
            _props.SetFloat(IgniteId, Mathf.Lerp(startIgnite, endIgnite, e) * (_locked ? 1f : 0.85f));

            // The crackle scrolls in METRES, so the ring has to say how many it is. Without this a
            // small ring seethes and a large one crawls, and they would not look like one effect.
            _props.SetFloat(BeamLengthId, Mathf.Max(0.1f, 2f * Mathf.PI * _radius / 0.96f));
            _props.SetColor(CoreColorId, Color.Lerp(Color.white, colour, 0.35f));
            _props.SetColor(BoltColorId, colour);
            _renderer.SetPropertyBlock(_props);

            if (glow != null)
            {
                glow.color = colour;
                glow.intensity = Mathf.Lerp(startLightIntensity, endLightIntensity, e) *
                                 (_locked ? 1.4f : 1f);
                glow.range = Mathf.Max(1f, _radius * 3f);
            }
        }

        private void OnDestroy()
        {
            // Built with `new Mesh()` per instance, so nothing else will collect it.
            if (_mesh != null) Destroy(_mesh);
        }

        private void OnValidate()
        {
            segments = Mathf.Clamp(segments, 8, 256);
            groundProbe = Mathf.Max(1f, groundProbe);
            groundOffset = Mathf.Max(0f, groundOffset);
            resampleInterval = Mathf.Max(0.02f, resampleInterval);
            moveEpsilon = Mathf.Max(0.001f, moveEpsilon);
            terrainTolerance = Mathf.Max(0.05f, terrainTolerance);
            lockedThickness = Mathf.Max(1f, lockedThickness);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = _locked ? Color.white : new Color(0.24f, 0.55f, 1f);
            Gizmos.DrawWireSphere(_centre, _radius);
        }
    }
}
