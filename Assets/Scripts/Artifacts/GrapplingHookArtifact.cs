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
    [SerializeField] private float reelSpeed = 20f;          // rope shortens this many units/sec
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
    private float _ropeLength;
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
        _ropeLength = Vector3.Distance(owner.transform.position, _hookPoint);

        owner.GetComponent<PlayerMovement>()?.DisableGroundSnap(999f);

        EnableRope();

        _pullCoroutine = StartCoroutine(PullRoutine());
    }

    // ── Pull coroutine ─────────────────────────────────────────────────────
    //
    // Pendulum constraint: shorten the rope each frame, then enforce it as a
    // hard inextensible constraint. Gravity acts freely — the player swings in
    // an arc rather than flying straight at the hook point.

    private IEnumerator PullRoutine()
    {
        var rb = owner.GetComponent<Rigidbody>();

        while (_isGrappling && rb != null)
        {
            // Shorten rope over time
            _ropeLength = Mathf.Max(arrivalDistance, _ropeLength - reelSpeed * Time.deltaTime);

            Vector3 toHook = _hookPoint - rb.position;
            float dist = toHook.magnitude;
            Vector3 radial = dist > 0.001f ? toHook / dist : Vector3.up;

            // Hard constraint: if player is beyond rope length, cancel outward velocity
            // and snap back to the rope surface so they swing rather than drift away
            if (dist > _ropeLength)
            {
                float radialVel = Vector3.Dot(rb.linearVelocity, -radial); // velocity away from hook
                if (radialVel > 0f)
                    rb.linearVelocity += radial * radialVel;               // cancel the outward component

                rb.position = _hookPoint - radial * _ropeLength;
            }

            // Arrival — release and apply momentum boost
            if (_ropeLength <= arrivalDistance)
            {
                rb.linearVelocity += radial * arrivalBoost;
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
