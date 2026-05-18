using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Light))]
public class Flashlight : MonoBehaviour
{
    [SerializeField] private Key toggleKey = Key.L;

    [Header("Long-Throw (custom shader contribution)")]
    [Tooltip("Extra intensity added on top of URP's spot light, with a flatter falloff so distant surfaces still read.")]
    [SerializeField] private float longThrowIntensity = 6f;
    [Tooltip("Lower = slower falloff (carries further). Inverse-linear factor: attenuation = 1 / (1 + k*d).")]
    [SerializeField] private float longThrowFalloff = 0.012f;
    [Tooltip("Where the soft range cutoff begins as a fraction of the light range (0..1).")]
    [SerializeField, Range(0.5f, 1.0f)] private float longThrowRangeFadeStart = 0.85f;

    [Header("Volumetric Beam")]
    [SerializeField] private Material beamMaterial;
    [SerializeField] private int beamSegments = 48;
    [Tooltip("Visible beam length as a fraction of Light.range.")]
    [SerializeField, Range(0.01f, 1.0f)] private float beamLengthScale = 0.35f;
    [Tooltip("Multiplier on the cone's radius. >1 makes the visible cone wider than the light's spot angle.")]
    [SerializeField, Range(0.5f, 3.0f)] private float beamWidthScale = 1.3f;

    private Light flashlight;
    private GameObject beamGO;
    private MeshRenderer beamRenderer;

    private static readonly int IdPos       = Shader.PropertyToID("_FlashlightPos");
    private static readonly int IdDir       = Shader.PropertyToID("_FlashlightDir");
    private static readonly int IdColor     = Shader.PropertyToID("_FlashlightColor");
    private static readonly int IdParams    = Shader.PropertyToID("_FlashlightParams");   // x=cosOuter, y=cosInner, z=range, w=enabled
    private static readonly int IdFalloff   = Shader.PropertyToID("_FlashlightFalloff");  // x=k, y=rangeFadeStart

    private void Awake()
    {
        flashlight = GetComponent<Light>();
        flashlight.type = LightType.Spot;
        BuildBeam();
        SetEnabled(false);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[toggleKey].wasPressedThisFrame)
        {
            SetEnabled(!flashlight.enabled);
        }

        PushShaderGlobals();
    }

    private void PushShaderGlobals()
    {
        bool on = flashlight.enabled;
        // Outer is full spot angle; inner is Unity's InnerSpotAngle. Both in degrees, converted to cosines.
        float cosOuter = Mathf.Cos(flashlight.spotAngle * 0.5f * Mathf.Deg2Rad);
        float cosInner = Mathf.Cos(flashlight.innerSpotAngle * 0.5f * Mathf.Deg2Rad);

        Shader.SetGlobalVector(IdPos, transform.position);
        Shader.SetGlobalVector(IdDir, transform.forward);
        Color c = flashlight.color * (on ? longThrowIntensity : 0f);
        Shader.SetGlobalVector(IdColor, new Vector4(c.r, c.g, c.b, 1f));
        Shader.SetGlobalVector(IdParams, new Vector4(cosOuter, cosInner, flashlight.range, on ? 1f : 0f));
        Shader.SetGlobalVector(IdFalloff, new Vector4(longThrowFalloff, longThrowRangeFadeStart, 0f, 0f));
    }

    private void SetEnabled(bool on)
    {
        flashlight.enabled = on;
        if (beamRenderer != null) beamRenderer.enabled = on;
    }

    private void BuildBeam()
    {
        if (beamMaterial == null) return;

        beamGO = new GameObject("Beam");
        beamGO.transform.SetParent(transform, false);
        beamGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // mesh built along +Y, light shines along +Z

        var mf = beamGO.AddComponent<MeshFilter>();
        beamRenderer = beamGO.AddComponent<MeshRenderer>();
        beamRenderer.sharedMaterial = beamMaterial;
        beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beamRenderer.receiveShadows = false;
        beamRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        beamRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        beamRenderer.allowOcclusionWhenDynamic = false;

        float length = flashlight.range * beamLengthScale;
        float halfAngleRad = flashlight.spotAngle * 0.5f * Mathf.Deg2Rad;
        float radius = Mathf.Tan(halfAngleRad) * length * beamWidthScale;

        mf.sharedMesh = BuildConeMesh(beamSegments, radius, length);
    }

    private static Mesh BuildConeMesh(int segments, float radius, float length)
    {
        segments = Mathf.Max(8, segments);
        var verts = new Vector3[segments + 2];
        var uvs = new Vector2[segments + 2];
        var normals = new Vector3[segments + 2];

        verts[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0f);
        normals[0] = Vector3.up;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float ang = t * Mathf.PI * 2f;
            float c = Mathf.Cos(ang);
            float s = Mathf.Sin(ang);
            verts[i + 1] = new Vector3(c * radius, length, s * radius);
            uvs[i + 1] = new Vector2(t, 1f);
            Vector3 radialDir = new Vector3(c, 0f, s);
            Vector3 sideNormal = (radialDir * length + Vector3.up * radius).normalized;
            normals[i + 1] = sideNormal;
        }

        var tris = new int[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = i + 2;
        }

        var mesh = new Mesh
        {
            name = "FlashlightBeamCone",
            vertices = verts,
            uv = uvs,
            normals = normals,
            triangles = tris,
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy()
    {
        if (beamGO != null) Destroy(beamGO);
        // Make sure shaders don't keep lighting from a destroyed flashlight.
        Shader.SetGlobalVector(IdParams, new Vector4(1f, 1f, 1f, 0f));
    }
}
