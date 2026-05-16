using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Light))]
public class Flashlight : MonoBehaviour
{
    [SerializeField] private Key toggleKey = Key.L;

    [Header("Volumetric Beam")]
    [SerializeField] private Material beamMaterial;
    [SerializeField] private int beamSegments = 32;
    [SerializeField] private float beamLengthScale = 0.9f;
    [SerializeField] private float beamWidthScale = 1.0f;

    private Light flashlight;
    private GameObject beamGO;
    private MeshRenderer beamRenderer;

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

        // tip at origin
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
            // approximate side normal: slope normal of cone wall
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
    }
}
