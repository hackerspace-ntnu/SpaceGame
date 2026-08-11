using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Attached to each combined visual mesh produced by SettlementBuilder.
/// Disables shadow casting when the camera is beyond shadowCullDistance,
/// re-enables it when the camera comes back within range.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class SettlementShadowCuller : MonoBehaviour
{
    public float shadowCullDistance = 60f;

    private MeshRenderer meshRenderer;
    private ShadowCastingMode activeShadowMode;
    private bool shadowsCurrentlyOn = true;
    private float sqrCullDistance;

    void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        activeShadowMode = meshRenderer.shadowCastingMode;
        sqrCullDistance = shadowCullDistance * shadowCullDistance;
    }

    void Update()
    {
        var cam = Camera.main;
        if (cam == null || activeShadowMode == ShadowCastingMode.Off) return;

        float sqrDist = (transform.position - cam.transform.position).sqrMagnitude;
        bool shouldCast = sqrDist <= sqrCullDistance;

        if (shouldCast == shadowsCurrentlyOn) return;

        shadowsCurrentlyOn = shouldCast;
        meshRenderer.shadowCastingMode = shouldCast ? activeShadowMode : ShadowCastingMode.Off;
    }
}
