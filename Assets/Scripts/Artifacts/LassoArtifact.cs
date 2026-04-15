using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Lasso artifact — extends ToolItem.
///
/// First Use()  → plays a throw animation, then visibly throws the lasso forward.
///                If the traveling lasso head hits a valid rigidbody target, it
///                attaches a rope to the NPC's upper body.
///                Rope is tension-only: free when slack, pulls only when taut.
/// Second Use() → releases the lasso, restores NavMesh on the target.
/// </summary>
public class LassoArtifact : ToolItem
{
    [Header("Firing")]
    [SerializeField] private float maxRange = 40f;
    [SerializeField] private LayerMask hookableLayers = ~0;
    [SerializeField] private float throwDelay = 0.3f;
    [SerializeField] private float throwSpeed = 30f;
    [SerializeField] private float throwRadius = 1.2f;      // generous — easy to snatch NPCs
    [SerializeField] private float missHoldTime = 1.5f;     // rope stays visible this long on miss

    [Header("Rope / Joint")]
    [SerializeField] private float ropeSlack = 2f;
    [SerializeField] private float ropeTension = 600f;      // force applied when rope is taut
    [SerializeField] private float npcAttachHeightOffset = 1.2f; // world-units above NPC root to attach

    [Header("Animation")]
    [SerializeField] private string throwTrigger = "Throw";

    [Header("Rope Visual")]
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private Transform muzzle;
    [SerializeField] private int ropeSegments = 20;
    [SerializeField] private float ropeGravity = 3f;
    [SerializeField] private float ropeWidth = 0.04f;

    [Header("Lasso Loop")]
    [SerializeField] private LineRenderer loopRenderer;
    [SerializeField] private int loopSegments = 24;
    [SerializeField] private float loopRadius = 0.35f;
    [SerializeField] private float loopSpinSpeed = 360f;
    [SerializeField] private float loopTiltAngle = 60f;

    [Header("Throw Arc")]
    [SerializeField] private float throwArcHeight = 4f;
    [SerializeField] private float throwGravity = 18f;

    [Header("Rope Wobble")]
    [SerializeField] private float wobbleAmplitude = 0.3f;
    [SerializeField] private float wobbleFrequency = 2.5f;
    [SerializeField] private float wobbleDecay = 2f;

    // ── Runtime state ──────────────────────────────────────────────────────
    private bool _isLassoed;
    private bool _isThrowing;
    private Rigidbody _targetRb;
    private NavMeshAgent _targetAgent;
    private float _currentRopeLength;
    private Coroutine _routine;
    private Vector3 _ropeEndPoint;   // world-space point drawn as rope tip (chest height)
    private Vector3 _attachOffset;   // local offset on NPC body where rope attaches

    // Wobble state
    private float _wobbleTime;
    private float _wobbleStrength;
    private Vector3 _wobbleAxis;

    // Loop spin state
    private float _loopAngle;

    // ── ToolItem override ──────────────────────────────────────────────────

    protected override void Use()
    {
        base.Use();

        if (_isLassoed)
        {
            Release();
            return;
        }

        if (_isThrowing) return;

        _routine = StartCoroutine(ThrowRoutine());
    }

    protected override bool CanUse()
    {
        return base.CanUse() || _isLassoed;
    }

    // ── Throw sequence ─────────────────────────────────────────────────────

    private IEnumerator ThrowRoutine()
    {
        _isThrowing = true;

        var animator = owner.GetComponentInChildren<Animator>();
        if (animator != null)
            animator.SetTrigger(throwTrigger);

        yield return new WaitForSeconds(throwDelay);

        Ray aimRay = aimProvider.GetAimRay();
        Vector3 start = GetRopeStart();
        Vector3 targetPoint = aimRay.origin + aimRay.direction * maxRange;

        if (Physics.Raycast(aimRay, out RaycastHit aimHit, maxRange, ~0, QueryTriggerInteraction.Ignore))
            targetPoint = aimHit.point;

        _wobbleStrength = 1f;
        _wobbleTime = 0f;
        _wobbleAxis = Vector3.Cross(aimRay.direction, Vector3.up).normalized;
        if (_wobbleAxis.sqrMagnitude < 0.01f)
            _wobbleAxis = Vector3.right;

        EnableRope();
        EnableLoop();

        Vector3 delta = targetPoint - start;
        Vector3 flatDelta = new Vector3(delta.x, 0f, delta.z);
        float flatDist = Mathf.Max(flatDelta.magnitude, 0.01f);
        float timeToTarget = flatDist / throwSpeed;

        float vy = (delta.y / timeToTarget) + 0.5f * throwGravity * timeToTarget + throwArcHeight;
        Vector3 velocity = flatDelta.normalized * throwSpeed + Vector3.up * vy;

        Vector3 headPos = start;
        Vector3 prevHeadPos = start;
        _ropeEndPoint = start;

        float estimatedFlightTime = timeToTarget * 1.5f;
        float elapsed = 0f;

        while (true)
        {
            elapsed += Time.deltaTime;
            velocity += Vector3.down * throwGravity * Time.deltaTime;

            prevHeadPos = headPos;
            headPos += velocity * Time.deltaTime;

            Vector3 stepDir = headPos - prevHeadPos;
            float stepDist = stepDir.magnitude;
            Vector3 stepDirNorm = stepDist > 0.001f ? stepDir / stepDist : velocity.normalized;

            _loopAngle += loopSpinSpeed * Time.deltaTime;
            _wobbleTime += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsed / estimatedFlightTime);

            if (TryGetLatchTarget(headPos, out Rigidbody latchedRb, out Vector3 latchPoint))
            {
                _ropeEndPoint = latchPoint;
                UpdateRope(progress);
                UpdateLoop(_ropeEndPoint, stepDirNorm);

                _isThrowing = false;
                Attach(latchedRb);
                yield break;
            }

            bool pastTarget = Vector3.Dot(headPos - targetPoint, velocity) > 0f && elapsed > timeToTarget;
            bool tooFar = (headPos - start).magnitude > maxRange * 1.5f;

            _ropeEndPoint = headPos;
            UpdateRope(progress);
            UpdateLoop(headPos, stepDirNorm);

            if (pastTarget || tooFar)
            {
                yield return new WaitForSeconds(missHoldTime);
                DisableRope();
                DisableLoop();
                _isThrowing = false;
                yield break;
            }

            yield return null;
        }
    }

    // ── Attach / Release ───────────────────────────────────────────────────

    private void Attach(Rigidbody targetRb)
    {
        _targetRb = targetRb;
        _isLassoed = true;

        _targetAgent = targetRb.GetComponent<NavMeshAgent>();
        if (_targetAgent != null)
            _targetAgent.enabled = false;

        _targetRb.isKinematic = false;

        // Attach point is offset upward from the NPC root (chest area)
        _attachOffset = Vector3.up * npcAttachHeightOffset;

        Vector3 attachWorldPos = _targetRb.position + _attachOffset;
        _currentRopeLength = Vector3.Distance(GetRopeStart(), attachWorldPos) + ropeSlack;
        _ropeEndPoint = attachWorldPos;

        _wobbleStrength = 1f;
        _wobbleTime = 0f;
        _wobbleAxis = Vector3.Cross((_targetRb.position - owner.transform.position).normalized, Vector3.up).normalized;
        if (_wobbleAxis.sqrMagnitude < 0.01f)
            _wobbleAxis = Vector3.right;

        EnableRope();
        EnableLoop();
        _routine = StartCoroutine(ReelRoutine());
    }

    private void Release()
    {
        _isLassoed = false;

        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }

        if (_targetAgent != null)
        {
            _targetAgent.enabled = true;
            _targetAgent = null;
        }

        _targetRb = null;
        DisableRope();
        DisableLoop();
    }

    // ── Reel-in loop ───────────────────────────────────────────────────────

    private IEnumerator ReelRoutine()
    {
        while (_isLassoed && _targetRb != null)
        {
            _wobbleTime += Time.deltaTime;
            _wobbleStrength = Mathf.Max(0f, _wobbleStrength - wobbleDecay * Time.deltaTime);
            _loopAngle += loopSpinSpeed * Time.deltaTime;

            Vector3 attachWorldPos = _targetRb.position + _attachOffset;
            _ropeEndPoint = attachWorldPos;

            UpdateRope(1f);
            UpdateLoop(attachWorldPos, (attachWorldPos - GetRopeStart()).normalized);

            yield return null;
        }

        if (_isLassoed) Release();
    }

    // ── Rope tension (FixedUpdate) ─────────────────────────────────────────

    private void FixedUpdate()
    {
        if (!_isLassoed || _targetRb == null) return;

        Vector3 ropeStart = GetRopeStart();
        Vector3 attachWorld = _targetRb.position + _attachOffset;
        Vector3 toTarget = attachWorld - ropeStart;
        float dist = toTarget.magnitude;

        // Only pull when rope is taut — no push, no spring inside the limit
        if (dist <= _currentRopeLength) return;

        Vector3 pullDir = toTarget.normalized;
        float excess = dist - _currentRopeLength;
        float force = ropeTension * excess;

        // Pull the NPC toward the player
        _targetRb.AddForceAtPosition(-pullDir * force, attachWorld, ForceMode.Force);

        // Optionally pull the player too (comment out if you want to be an anchor)
        Rigidbody ownerRb = owner.GetComponent<Rigidbody>();
        if (ownerRb != null)
            ownerRb.AddForce(pullDir * force * 0.3f, ForceMode.Force);
    }

    // ── Rope visual ────────────────────────────────────────────────────────

    private void EnableRope()
    {
        if (lineRenderer == null) return;
        lineRenderer.positionCount = ropeSegments;
        lineRenderer.enabled = true;

        var widthCurve = new AnimationCurve();
        widthCurve.AddKey(0f, ropeWidth);
        widthCurve.AddKey(1f, ropeWidth * 0.35f);
        lineRenderer.widthCurve = widthCurve;
    }

    private void DisableRope()
    {
        if (lineRenderer == null) return;
        lineRenderer.enabled = false;
    }

    private void UpdateRope(float headProgress)
    {
        if (lineRenderer == null || !lineRenderer.enabled) return;

        Vector3 start = GetRopeStart();
        Vector3 end = _ropeEndPoint;
        float span = (end - start).magnitude;
        float sagFactor = Mathf.Clamp01(span / maxRange);

        Vector3 wobbleDir = _wobbleAxis;
        if (wobbleDir.sqrMagnitude < 0.01f)
            wobbleDir = Vector3.right;

        int activeSegments = Mathf.Max(2, Mathf.RoundToInt(headProgress * ropeSegments));
        lineRenderer.positionCount = activeSegments;

        for (int i = 0; i < activeSegments; i++)
        {
            float t = i / (float)(activeSegments - 1);
            Vector3 pos = Vector3.Lerp(start, end, t);

            pos.y -= Mathf.Sin(t * Mathf.PI) * ropeGravity * sagFactor;

            float wobble = Mathf.Sin(t * Mathf.PI * wobbleFrequency + _wobbleTime * 6f)
                           * wobbleAmplitude * _wobbleStrength
                           * Mathf.Sin(t * Mathf.PI);
            pos += wobbleDir * wobble;

            lineRenderer.SetPosition(i, pos);
        }
    }

    // ── Loop visual ────────────────────────────────────────────────────────

    private void EnableLoop()
    {
        if (loopRenderer == null) return;
        loopRenderer.positionCount = loopSegments + 1;
        loopRenderer.enabled = true;
    }

    private void DisableLoop()
    {
        if (loopRenderer == null) return;
        loopRenderer.enabled = false;
    }

    private void UpdateLoop(Vector3 center, Vector3 forward)
    {
        if (loopRenderer == null || !loopRenderer.enabled) return;
        if (forward.sqrMagnitude < 0.001f) return;

        Quaternion baseRot = Quaternion.LookRotation(forward);
        Quaternion tilt = Quaternion.AngleAxis(loopTiltAngle, baseRot * Vector3.right);
        Quaternion spin = Quaternion.AngleAxis(_loopAngle, forward);
        Quaternion loopRot = spin * tilt * baseRot;

        Vector3 right = loopRot * Vector3.right;
        Vector3 up    = loopRot * Vector3.up;

        for (int i = 0; i <= loopSegments; i++)
        {
            float angle = i / (float)loopSegments * Mathf.PI * 2f;
            Vector3 pt = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * loopRadius;
            loopRenderer.SetPosition(i, pt);
        }
    }

    private Vector3 GetRopeStart()
    {
        return muzzle != null ? muzzle.position : owner.transform.position;
    }

    // Returns the first hookable Rigidbody within throwRadius of headPos (walks up the hierarchy).
    private bool TryGetLatchTarget(Vector3 headPos, out Rigidbody rb, out Vector3 latchPoint)
    {
        rb = null;
        latchPoint = headPos;

        Collider[] nearby = Physics.OverlapSphere(headPos, throwRadius, ~0, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;

        foreach (Collider col in nearby)
        {
            if (col == null) continue;
            if (col.transform.IsChildOf(owner.transform)) continue;

            // Walk up to find a Rigidbody on this collider or any parent
            Rigidbody candidate = col.GetComponentInParent<Rigidbody>();
            if (candidate == null) continue;

            bool layerOk = (hookableLayers.value & (1 << col.gameObject.layer)) != 0;
            if (!layerOk) continue;

            float d = Vector3.Distance(headPos, col.ClosestPoint(headPos));
            if (d < bestDist)
            {
                bestDist = d;
                rb = candidate;
                latchPoint = col.ClosestPoint(headPos);
            }
        }

        return rb != null;
    }
}
