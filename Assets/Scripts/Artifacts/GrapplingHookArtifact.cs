using System.Collections;
using UnityEngine;

/// <summary>
/// Grappling hook artifact — extends ToolItem.
///
/// First Use()  → fires hook at the aimed surface, starts pulling the player.
/// Second Use() → releases the hook (or it auto-releases on arrival).
///
/// The per-frame pull runs as a coroutine on this MonoBehaviour so ToolItem's
/// fire-and-forget Use() stays clean.
/// </summary>
public class GrapplingHookArtifact : ToolItem
{
    [Header("Firing")]
    [SerializeField] private float maxRange = 60f;
    [SerializeField] private LayerMask hookableLayers = ~0;

    [Header("Pull")]
    [SerializeField] private float pullSpeed = 18f;
    [SerializeField] private float arrivalDistance = 2.5f;
    [SerializeField] private float arrivalBoost = 14f;

    [Header("Rope Visual")]
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private Transform muzzle;      // optional gun-tip transform
    [SerializeField] private int ropeSegments = 12;
    [SerializeField] private float ropeGravity = 4f;

    // ── Runtime state ──────────────────────────────────────────────────────
    private bool _isGrappling;
    private Vector3 _hookPoint;
    private Coroutine _pullCoroutine;

    // ── ToolItem override ──────────────────────────────────────────────────

    protected override void Use()
    {
        base.Use(); // sets aimProvider

        if (_isGrappling)
        {
            StopGrapple();
            return;
        }

        RaycastHit? hit = aimProvider.GetRayCast(maxRange);
        if (hit == null) return;

        // Respect hookable layer mask
        if ((hookableLayers.value & (1 << hit.Value.collider.gameObject.layer)) == 0)
            return;

        _hookPoint = hit.Value.point;
        _isGrappling = true;

        owner.GetComponent<PlayerMovement>()?.DisableGroundSnap(999f);

        EnableRope();

        _pullCoroutine = StartCoroutine(PullRoutine());
    }

    // ── Pull coroutine ─────────────────────────────────────────────────────

    private IEnumerator PullRoutine()
    {
        var rb = owner.GetComponent<Rigidbody>();
        bool boostApplied = false;

        while (_isGrappling && rb != null)
        {
            Vector3 toHook = _hookPoint - rb.position;
            float dist = toHook.magnitude;
            Vector3 dir = toHook.normalized;

            // Smooth pull — lerp velocity toward the hook direction
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, dir * pullSpeed, Time.deltaTime * 8f);

            // Arrival boost fires once when close enough
            if (!boostApplied && dist <= arrivalDistance)
            {
                boostApplied = true;
                rb.linearVelocity = dir * (rb.linearVelocity.magnitude + arrivalBoost);
                StopGrapple();
                yield break;
            }

            UpdateRope(rb.position);

            yield return null;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void StopGrapple()
    {
        _isGrappling = false;

        if (_pullCoroutine != null)
        {
            StopCoroutine(_pullCoroutine);
            _pullCoroutine = null;
        }

        DisableRope();
        owner.GetComponent<PlayerMovement>()?.DisableGroundSnap(0.15f);
    }

    private void EnableRope()
    {
        if (lineRenderer == null) return;
        lineRenderer.positionCount = ropeSegments;
        lineRenderer.enabled = true;
    }

    private void DisableRope()
    {
        if (lineRenderer == null) return;
        lineRenderer.enabled = false;
    }

    private void UpdateRope(Vector3 playerPos)
    {
        if (lineRenderer == null || !lineRenderer.enabled) return;

        Vector3 start = muzzle != null ? muzzle.position : playerPos;
        Vector3 end = _hookPoint;
        float span = (end - start).magnitude;

        for (int i = 0; i < ropeSegments; i++)
        {
            float t = i / (float)(ropeSegments - 1);
            Vector3 pos = Vector3.Lerp(start, end, t);
            pos.y -= Mathf.Sin(t * Mathf.PI) * ropeGravity * (span / maxRange);
            lineRenderer.SetPosition(i, pos);
        }
    }
}
