using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Core;
using SpaceGame.Presentation;

namespace SpaceGame.Characters
{
    [RequireComponent(typeof(Light))]
    public class Flashlight : MonoBehaviour
    {
        [SerializeField] private Key toggleKey = Key.L;

        [Header("Long-Throw (custom shader contribution)")]
        [Tooltip("How far the flashlight actually reaches on shaders that include Flashlight.hlsl, and how far the visible beam can extend. Decoupled from Light.range so URP can keep a tight near-field while the long-throw layer goes further.")]
        [SerializeField] private float flashlightReach = 120f;
        [Tooltip("Extra intensity added on top of URP's spot light, with a flatter falloff so distant surfaces still read.")]
        [SerializeField] private float longThrowIntensity = 6f;
        [Tooltip("Lower = slower falloff (carries further). Inverse-linear factor: attenuation = 1 / (1 + k*d).")]
        [SerializeField] private float longThrowFalloff = 0.012f;
        [Tooltip("Where the soft range cutoff begins as a fraction of flashlightReach (0..1).")]
        [SerializeField, Range(0.5f, 1.0f)] private float longThrowRangeFadeStart = 0.85f;

        [Header("Volumetric Beam")]
        [SerializeField] private Material beamMaterial;
        [SerializeField] private int beamSegments = 48;
        [Tooltip("Multiplier on the bounding cone mesh radius. Should stay generous (>= 2) so the mesh always over-covers the visible beam volume at oblique angles. The actual visible cone shape comes from the shader, not this mesh.")]
        [SerializeField, Range(0.5f, 5.0f)] private float beamWidthScale = 2.5f;
        [Tooltip("Hard cap on how long the beam mesh can extend, even if nothing is hit. Clamped to flashlightReach.")]
        [SerializeField] private float beamMaxLength = 120f;
        [Tooltip("Layers the beam raycast considers when finding where the beam ends. EXCLUDE the Player layer or the rays will self-hit and the beam will collapse to zero length.")]
        [SerializeField] private LayerMask beamHitMask = ~(1 << 6); // exclude layer 6 (Player) by default
        [Tooltip("Number of ground-probing rays around the center to find the *shortest* hit (so the beam wraps tight terrain).")]
        [SerializeField, Range(0, 16)] private int beamProbeRays = 6;
        [Tooltip("How quickly the beam length adapts to scene changes. 1 = instant, lower = smoother.")]
        [SerializeField, Range(0.05f, 1f)] private float beamLengthSmoothing = 1f;
        [Tooltip("Draw the beam raycasts as debug lines in the Scene view. Center ray = yellow if no hit, green if hit. Probes = magenta if hit.")]
        [SerializeField] private bool debugDrawRays = false;

        private Light flashlight;
        private GameObject beamGO;
        private MeshRenderer beamRenderer;
        private MeshFilter beamMF;
        private Mesh beamMesh;
        private Vector3[] beamVerts;
        private Vector2[] beamUVs;
        private int[] beamTris;
        private float currentBeamLength;

        // Public so they show up in the inspector during play and you can confirm the
        // raycast is doing what you think it is. Not authoritative state — purely diag.
        [Header("Debug (read-only at runtime)")]
        [SerializeField] private float dbgLastShortestHit;
        [SerializeField] private bool  dbgCenterRayHit;

        private static readonly int IdPos       = Shader.PropertyToID("_FlashlightPos");
        private static readonly int IdDir       = Shader.PropertyToID("_FlashlightDir");
        private static readonly int IdColor     = Shader.PropertyToID("_FlashlightColor");
        private static readonly int IdParams    = Shader.PropertyToID("_FlashlightParams");   // x=cosOuter, y=cosInner, z=range, w=enabled
        private static readonly int IdFalloff   = Shader.PropertyToID("_FlashlightFalloff");  // x=k, y=rangeFadeStart
        private static readonly int IdBeamEnd   = Shader.PropertyToID("_FlashlightBeamEnd");  // x=beamLength (world units)

        private void Awake()
        {
            flashlight = GetComponent<Light>();
            flashlight.type = LightType.Spot;
            BuildBeam();
            currentBeamLength = Mathf.Min(beamMaxLength, flashlightReach);
            SetEnabled(false);
        }

        private void Update()
        {
            // Asked once and reused, because it walks the hierarchy looking for a NetworkObject and
            // both gates below want the same answer in the same frame.
            bool mine = OwnsSingleSlotEffects;

            // Gated twice, because this component reads the keyboard directly rather than through
            // an action map that gets switched off with the player.
            //
            // By SCOPE: the same Main Camera prefab that carries this light is dropped into
            // MainMenu.unity for the backdrop, so an ungated read had L switching a flashlight on
            // behind the main menu — and behind the pause menu, and on a corpse.
            //
            // By OWNERSHIP: every machine in a session holds a copy of every other player's lamp,
            // and a keyboard is not per-player. Without this, one press of L toggled the torch on
            // every player in the game at once — on this machine's copy of them. The owner's press
            // is the only one that counts, and PlayerViewNetwork carries the result to everyone
            // else. Network.Owns walks up to the player above this light, and answers true offline
            // and for anything unnetworked, so a torch on a prop or in a test scene still works.
            var kb = Keyboard.current;
            if (kb != null && kb[toggleKey].wasPressedThisFrame
                && GameplayMenuScope.AcceptsGameplayInput
                && mine)
            {
                SetEnabled(!flashlight.enabled);
            }

            // Both of these drive SINGLE-SLOT effects, so only one lamp in the session may run
            // them — see OwnsSingleSlotEffects.
            if (!mine) return;

            if (flashlight.enabled)
            {
                UpdateBeamGeometry();
            }
            PushShaderGlobals();
        }

        /// <summary>
        /// May this lamp drive the effects there is only one of?
        ///
        /// <para>
        /// The long-throw layer is a set of GLOBAL shader uniforms — one position, one direction,
        /// one colour for the whole frame — and the volumetric beam mesh is shaped by another
        /// (<c>_FlashlightBeamEnd</c>). One writer was the whole design, and it was true for as
        /// long as a remote player's lamp was switched off with the camera it hangs under.
        /// </para>
        /// <para>
        /// Now that every player's lamp is live on every machine, an ungated push means four
        /// flashlights racing to write the same slot every frame: the layer would flicker between
        /// whoever wrote last, and any torch that happens to be OFF writes <c>enabled = 0</c> and
        /// blacks out the layer for the player whose torch is on.
        /// </para>
        /// <para>
        /// So the slot belongs to the lamp of whoever is looking — the local player. What another
        /// player's torch contributes is its URP spot light, which is a real light and lights the
        /// world for everyone correctly; what it does not contribute is the extra reach and the
        /// visible shaft. Giving every torch those too means an array of flashlights in
        /// Flashlight.hlsl rather than one, which is a rendering change and not a netcode one.
        /// </para>
        /// </summary>
        private bool OwnsSingleSlotEffects => Network.Owns(this);

        private void PushShaderGlobals()
        {
            bool on = flashlight.enabled;
            float cosOuter = Mathf.Cos(flashlight.spotAngle * 0.5f * Mathf.Deg2Rad);
            float cosInner = Mathf.Cos(flashlight.innerSpotAngle * 0.5f * Mathf.Deg2Rad);

            Shader.SetGlobalVector(IdPos, transform.position);
            Shader.SetGlobalVector(IdDir, transform.forward);
            Color c = flashlight.color * (on ? longThrowIntensity : 0f);
            Shader.SetGlobalVector(IdColor, new Vector4(c.r, c.g, c.b, 1f));
            Shader.SetGlobalVector(IdParams, new Vector4(cosOuter, cosInner, flashlightReach, on ? 1f : 0f));
            Shader.SetGlobalVector(IdFalloff, new Vector4(longThrowFalloff, longThrowRangeFadeStart, 0f, 0f));
            Shader.SetGlobalVector(IdBeamEnd, new Vector4(currentBeamLength, 0f, 0f, 0f));
        }

        /// <summary>
        /// Is the torch on? The whole of this component's state, and none of it was in any record.
        ///
        /// <para>
        /// Reads the Light rather than a flag of its own, so it cannot disagree with what is
        /// actually lit — <see cref="Awake"/> switches the light off directly.
        /// </para>
        /// </summary>
        public bool IsOn => flashlight != null && flashlight.enabled;

        /// <summary>
        /// Someone outside this lamp stating what it should be. Called by the save system and by
        /// <see cref="PlayerViewNetwork"/> mirroring the owner's torch onto a remote copy; do not
        /// call it from gameplay, which should go through the key.
        ///
        /// <para>
        /// Both callers are the same shape — an authority declaring the truth rather than asking
        /// for a toggle — which is why it takes the state and not a flip.
        /// </para>
        /// <para>
        /// Goes through the same <see cref="SetEnabled"/> the L key does, so the beam mesh is
        /// switched with the light instead of being left behind as a lit cone with no lamp.
        /// </para>
        /// </summary>
        public void RestoreOn(bool on)
        {
            if (flashlight == null) flashlight = GetComponent<Light>();
            SetEnabled(on);
        }

        private void SetEnabled(bool on)
        {
            flashlight.enabled = on;

            // The spot light is switched for everyone; the beam mesh only for the lamp that owns
            // the shader slot shaping it. A remote lamp with the mesh on would draw a shaft cut to
            // the LOCAL player's beam length — a cone of light stopping at nothing.
            if (beamRenderer != null) beamRenderer.enabled = on && OwnsSingleSlotEffects;
        }

        // Probe the scene for the shortest opaque hit inside the cone, then rebuild the
        // beam mesh so it ends at that surface instead of hovering past it.
        private void UpdateBeamGeometry()
        {
            if (beamMesh == null) return;

            float maxLen = Mathf.Min(beamMaxLength, flashlightReach);
            Vector3 origin = transform.position;
            Vector3 fwd = transform.forward;

            float shortest = maxLen;
            bool centerHit = Physics.Raycast(origin, fwd, out var centerInfo, maxLen, beamHitMask, QueryTriggerInteraction.Ignore);
            if (centerHit) shortest = centerInfo.distance;
            dbgCenterRayHit = centerHit;

            if (debugDrawRays)
            {
                Color cc = centerHit ? Color.green : Color.yellow;
                Debug.DrawRay(origin, fwd * (centerHit ? centerInfo.distance : maxLen), cc, 0f, false);
            }

            if (beamProbeRays > 0)
            {
                // Probe a ring slightly inside the outer cone angle so we catch ground/walls
                // the player is pointing at, not space just outside the cone.
                float probeHalfAngle = flashlight.spotAngle * 0.5f * 0.7f * Mathf.Deg2Rad;
                float sinA = Mathf.Sin(probeHalfAngle);
                float cosA = Mathf.Cos(probeHalfAngle);
                Vector3 up = transform.up;
                Vector3 right = transform.right;
                for (int i = 0; i < beamProbeRays; i++)
                {
                    float t = (float)i / beamProbeRays * Mathf.PI * 2f;
                    Vector3 offset = (right * Mathf.Cos(t) + up * Mathf.Sin(t)) * sinA;
                    Vector3 dir = (fwd * cosA + offset).normalized;
                    bool probeHit = Physics.Raycast(origin, dir, out var hit, maxLen, beamHitMask, QueryTriggerInteraction.Ignore);
                    if (probeHit)
                    {
                        // Project hit distance back onto the forward axis so the mesh
                        // length stays meaningful along the cone's central axis.
                        float axialDist = Vector3.Dot(hit.point - origin, fwd);
                        if (axialDist > 0.05f && axialDist < shortest) shortest = axialDist;
                    }
                    if (debugDrawRays)
                        Debug.DrawRay(origin, dir * (probeHit ? hit.distance : maxLen),
                                      probeHit ? Color.magenta : new Color(0.5f, 0.5f, 0.5f, 1f), 0f, false);
                }
            }

            dbgLastShortestHit = shortest;

            // currentBeamLength drives the *shader's* visible-beam cutoff via the
            // _FlashlightBeamEnd global. The mesh itself is built once at full
            // length — see BuildBeam — and never resized, so its edge can't show
            // up as the visible silhouette. The shader fades brightness inside
            // the mesh based on this value plus the depth buffer.
            currentBeamLength = Mathf.Lerp(currentBeamLength, shortest, beamLengthSmoothing);
        }

        private void BuildBeam()
        {
            if (beamMaterial == null) return;

            beamGO = new GameObject("Beam");
            beamGO.transform.SetParent(transform, false);
            beamGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // mesh built along +Y, light shines along +Z

            beamMF = beamGO.AddComponent<MeshFilter>();
            beamRenderer = beamGO.AddComponent<MeshRenderer>();
            beamRenderer.sharedMaterial = beamMaterial;
            beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beamRenderer.receiveShadows = false;
            beamRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            beamRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            beamRenderer.allowOcclusionWhenDynamic = false;

            AllocConeMeshBuffers(beamSegments);
            beamMesh = new Mesh { name = "FlashlightBeamCone" };
            beamMF.sharedMesh = beamMesh;

            // Mesh is a *bounding volume*, built once at the full possible length and
            // a generously wide radius so the actual beam (clipped per-frame inside
            // the shader via _FlashlightBeamEnd) is always fully contained. This is
            // what hides the cone silhouette: the mesh edge is far outside where the
            // shader writes any visible brightness, so you never see it.
            float meshLen = Mathf.Min(beamMaxLength, flashlightReach);
            float halfAngleRad = flashlight.spotAngle * 0.5f * Mathf.Deg2Rad;
            float meshRadius = Mathf.Tan(halfAngleRad) * meshLen * beamWidthScale;
            BuildConeMesh(beamSegments, meshRadius, meshLen);
        }

        // Vertex layout:
        //   0                       apex (local origin)
        //   1 .. segments+1         ring at the far end (closes the loop with overlap)
        //   segments+2              center of the far end (cap)
        //
        // Triangles: side-wall fan from apex (segments tris) +
        //            base cap fan from far-end center (segments tris).
        // The base cap is required because the bounding mesh is used to drive shader
        // execution — if the camera looks at the cone from outside through the open
        // far end, fragments only exist where the side wall covers screen pixels,
        // leaving a "hole" in the middle of the beam. Closing the base fills it.
        private void AllocConeMeshBuffers(int segments)
        {
            segments = Mathf.Max(8, segments);
            beamVerts = new Vector3[segments + 3];
            beamUVs = new Vector2[segments + 3];
            beamTris = new int[segments * 6];

            beamUVs[0] = new Vector2(0.5f, 0f);
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                beamUVs[i + 1] = new Vector2(t, 1f);
            }
            beamUVs[segments + 2] = new Vector2(0.5f, 1f);

            // Side-wall triangles.
            for (int i = 0; i < segments; i++)
            {
                beamTris[i * 3 + 0] = 0;
                beamTris[i * 3 + 1] = i + 1;
                beamTris[i * 3 + 2] = i + 2;
            }
            // Base cap triangles. Wind opposite to the side wall so both faces are
            // outward-facing; not strictly necessary since the material is Cull Off,
            // but keeps the mesh topologically consistent.
            int capCenter = segments + 2;
            int capBase = segments * 3;
            for (int i = 0; i < segments; i++)
            {
                beamTris[capBase + i * 3 + 0] = capCenter;
                beamTris[capBase + i * 3 + 1] = i + 2;
                beamTris[capBase + i * 3 + 2] = i + 1;
            }
        }

        private void BuildConeMesh(int segments, float radius, float length)
        {
            segments = Mathf.Max(8, segments);
            beamVerts[0] = Vector3.zero;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float ang = t * Mathf.PI * 2f;
                beamVerts[i + 1] = new Vector3(Mathf.Cos(ang) * radius, length, Mathf.Sin(ang) * radius);
            }
            beamVerts[segments + 2] = new Vector3(0f, length, 0f);

            beamMesh.Clear();
            beamMesh.vertices = beamVerts;
            beamMesh.uv = beamUVs;
            beamMesh.triangles = beamTris;
            // Generous bounds so frustum culling doesn't kill the beam at oblique angles.
            beamMesh.bounds = new Bounds(new Vector3(0f, length * 0.5f, 0f),
                                         new Vector3(radius * 2.2f, length + radius, radius * 2.2f));
        }

        private void OnDestroy()
        {
            if (beamGO != null) Destroy(beamGO);
            if (beamMesh != null) Destroy(beamMesh);

            // Make sure shaders don't keep lighting from a destroyed flashlight — but only if this
            // was the lamp writing them. A remote player disconnecting used to be enough to black
            // out the local player's long-throw layer, since every lamp cleared the shared slot on
            // the way out whether or not it had ever owned it.
            if (OwnsSingleSlotEffects)
                Shader.SetGlobalVector(IdParams, new Vector4(1f, 1f, 1f, 0f));
        }
    }
}
