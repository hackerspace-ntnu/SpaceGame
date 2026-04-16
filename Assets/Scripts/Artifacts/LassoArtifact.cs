using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;


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
    [SerializeField] private float reelSpeed = 18f;          // units/sec the rope pulls back on miss

    [Header("Rope / Joint")]
    [SerializeField] private float ropeSlack = 2f;
    [SerializeField] private float ropeTension = 600f;
    [SerializeField] private float reelInForce = 18f;   // units/sec speed when pulling target in
    [SerializeField] private InputActionReference reelInAction;   // assign RightClick in Inspector      // force applied when rope is taut
    [SerializeField] private float npcAttachHeightOffset = 1.2f; // world-units above NPC root to attach

    [Header("Animation")]
    [SerializeField] private string throwTrigger = "Throw";
    [SerializeField] private GameObject lassoModel;   // the held dummy mesh — hidden while rope is out

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
    [SerializeField] private float loopDistortAmount = 0.12f;  // max radius deviation
    [SerializeField] private float loopDistortSpeed = 1.8f;    // how fast the distortion drifts

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
    private Transform _targetTransform;   // used when target has no Rigidbody
    private NavMeshAgent _targetAgent;
    private AgentController _targetAgentController;
    private float _currentRopeLength;
    private Coroutine _routine;
    private Vector3 _ropeEndPoint;   // world-space point drawn as rope tip (chest height)
    private Vector3 _attachOffset;   // local offset on NPC body where rope attaches
    private float _loopSpinCurrent;  // actual spin speed, wound down after attach

    // Wobble state
    private float _wobbleTime;
    private float _wobbleStrength;
    private Vector3 _wobbleAxis;

    // Loop spin state
    private float _loopAngle;
    private float _loopSpinDecay = 180f; // deg/sec² wind-down rate after attach

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

        if (lassoModel != null) lassoModel.SetActive(false);

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

            if (TryGetLatchTarget(headPos, out Rigidbody latchedRb, out Transform latchedTransform, out Vector3 latchPoint))
            {
                _ropeEndPoint = latchPoint;
                UpdateRope(progress);
                UpdateLoop(_ropeEndPoint, stepDirNorm);

                _isThrowing = false;
                Attach(latchedRb, latchedTransform);
                yield break;
            }

            bool pastTarget = Vector3.Dot(headPos - targetPoint, velocity) > 0f && elapsed > timeToTarget;
            bool tooFar = (headPos - start).magnitude > maxRange * 1.5f;

            _ropeEndPoint = headPos;
            UpdateRope(progress);
            UpdateLoop(headPos, stepDirNorm);

            if (pastTarget || tooFar)
            {
                // Let the head continue falling under gravity until it hits the ground
                while (true)
                {
                    velocity += Vector3.down * throwGravity * Time.deltaTime;
                    prevHeadPos = headPos;
                    headPos += velocity * Time.deltaTime;

                    _loopAngle += loopSpinSpeed * Time.deltaTime;
                    _wobbleTime += Time.deltaTime;

                    Vector3 stepDir2 = headPos - prevHeadPos;
                    Vector3 stepDirNorm2 = stepDir2.magnitude > 0.001f ? stepDir2.normalized : velocity.normalized;

                    bool landed = Physics.Linecast(prevHeadPos, headPos, out RaycastHit groundHit, ~0, QueryTriggerInteraction.Ignore)
                                  && !groundHit.collider.transform.IsChildOf(owner.transform);
                    if (landed)
                        headPos = groundHit.point;

                    _ropeEndPoint = headPos;
                    UpdateRope(1f);
                    UpdateLoop(headPos, stepDirNorm2);

                    if (landed) break;

                    if (headPos.y < start.y - maxRange)
                        break;

                    yield return null;
                }

                // Reel the rope end back toward the muzzle
                Vector3 reelStart = _ropeEndPoint;
                float reelDist = Vector3.Distance(reelStart, GetRopeStart());
                float reelElapsed = 0f;
                float reelDuration = reelDist / Mathf.Max(reelSpeed, 0.1f);

                while (reelElapsed < reelDuration)
                {
                    reelElapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(reelElapsed / reelDuration);
                    _ropeEndPoint = Vector3.Lerp(reelStart, GetRopeStart(), t);
                    _loopAngle += loopSpinSpeed * Time.deltaTime;
                    _wobbleTime += Time.deltaTime;
                    UpdateRope(1f - t);
                    UpdateLoop(_ropeEndPoint, (GetRopeStart() - _ropeEndPoint).normalized);
                    yield return null;
                }

                DisableRope();
                DisableLoop();
                if (lassoModel != null) lassoModel.SetActive(true);
                _isThrowing = false;
                yield break;
            }

            yield return null;
        }
    }

    // ── Attach / Release ───────────────────────────────────────────────────

    private void Attach(Rigidbody targetRb, Transform targetTransform)
    {
        _targetRb        = targetRb;
        _targetTransform = targetTransform;
        _isLassoed       = true;
        _loopSpinCurrent = loopSpinSpeed;

        Transform root = targetRb != null ? targetRb.transform : targetTransform;

        _targetAgent = root.GetComponentInParent<NavMeshAgent>();
        if (_targetAgent != null) _targetAgent.enabled = false;

        _targetAgentController = root.GetComponentInParent<AgentController>();

        if (targetRb != null) targetRb.isKinematic = false;

        _attachOffset = Vector3.up * npcAttachHeightOffset;

        Vector3 attachWorldPos = root.position + _attachOffset;
        _currentRopeLength = Vector3.Distance(GetRopeStart(), attachWorldPos) + ropeSlack;
        _ropeEndPoint = attachWorldPos;

        _wobbleStrength = 1f;
        _wobbleTime     = 0f;
        _wobbleAxis = Vector3.Cross((root.position - owner.transform.position).normalized, Vector3.up).normalized;
        if (_wobbleAxis.sqrMagnitude < 0.01f) _wobbleAxis = Vector3.right;

        EnableRope();
        EnableLoop();
        _routine = StartCoroutine(ReelRoutine());
    }

    private void Release()
    {
        _isLassoed = false;

        if (_routine != null) { StopCoroutine(_routine); _routine = null; }

        if (_targetAgent != null) { _targetAgent.enabled = true; _targetAgent = null; }
        _targetAgentController = null;

        _targetRb        = null;
        _targetTransform = null;
        DisableRope();
        DisableLoop();
        if (lassoModel != null) lassoModel.SetActive(true);
    }

    // ── Reel-in loop ───────────────────────────────────────────────────────

    private IEnumerator ReelRoutine()
    {
        Transform root = _targetRb != null ? _targetRb.transform : _targetTransform;

        while (_isLassoed && root != null)
        {
            _wobbleTime      += Time.deltaTime;
            _wobbleStrength   = Mathf.Max(0f, _wobbleStrength - wobbleDecay * Time.deltaTime);
            _loopSpinCurrent  = Mathf.Max(0f, _loopSpinCurrent - _loopSpinDecay * Time.deltaTime);
            _loopAngle       += _loopSpinCurrent * Time.deltaTime;

            Vector3 attachWorldPos = root.position + _attachOffset;
            _ropeEndPoint = attachWorldPos;

            UpdateRope(1f);
            UpdateLoop(attachWorldPos, (attachWorldPos - GetRopeStart()).normalized);

            yield return null;
        }

        if (_isLassoed) Release();
    }

    // ── Right-click reel-in (input read only) ─────────────────────────────

    private bool _reelHeld;

    private void Update()
    {
        _reelHeld = _isLassoed
                    && reelInAction != null
                    && reelInAction.action.ReadValue<float>() >= 0.5f;

        // Transform-only targets (e.g. ant) — move directly, no physics
        if (_reelHeld && _isLassoed && _targetRb == null && _targetTransform != null)
        {
            _targetTransform.position = Vector3.MoveTowards(
                _targetTransform.position,
                GetRopeStart() - _attachOffset,
                reelInForce * Time.deltaTime);
        }
    }

    // ── Rope physics (FixedUpdate) ─────────────────────────────────────────
    //
    // Pendulum-style swinging rope:
    //   1. Hard constraint — strip any velocity component that would lengthen the rope
    //      beyond _currentRopeLength (inextensible rope, no spring bounce).
    //   2. Reel-in — when right-click held, shorten _currentRopeLength and remove the
    //      radial velocity component so the target swings inward rather than flying straight.
    //   3. Gravity acts freely every frame — the target arcs downward as it swings.

    private void FixedUpdate()
    {
        if (!_isLassoed || _targetRb == null) return;

        Vector3 ropeStart   = GetRopeStart();
        Vector3 attachWorld = _targetRb.position + _attachOffset;
        Vector3 toTarget    = attachWorld - ropeStart;          // rope vector (anchor → target)
        float   dist        = toTarget.magnitude;
        Vector3 radial      = dist > 0.001f ? toTarget / dist : Vector3.up;  // unit rope direction

        // ── Shorten rope when reeling ──────────────────────────────────────
        if (_reelHeld)
            _currentRopeLength = Mathf.Max(ropeSlack, _currentRopeLength - reelInForce * Time.fixedDeltaTime);

        // ── Inextensible constraint ────────────────────────────────────────
        // If the target is beyond the rope length, cancel the outward radial velocity
        // and push the target back to the rope surface. No spring — hard constraint.
        if (dist > _currentRopeLength)
        {
            // Cancel the velocity component pulling away from anchor
            float radialVel = Vector3.Dot(_targetRb.linearVelocity, radial);
            if (radialVel > 0f)
                _targetRb.linearVelocity -= radial * radialVel;

            // Snap position to rope length
            _targetRb.position = ropeStart + radial * _currentRopeLength - _attachOffset;

            // Drag the player anchor slightly (feel of weight on the rope)
            Rigidbody ownerRb = owner.GetComponent<Rigidbody>();
            if (ownerRb != null)
            {
                float drag = Mathf.Abs(radialVel) * 0.15f;
                ownerRb.AddForce(radial * drag, ForceMode.VelocityChange);
            }
        }
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

            float distort = Mathf.Sin(angle * 2f + _loopAngle * 0.03f * loopDistortSpeed) * 0.6f
                          + Mathf.Sin(angle * 3f - _loopAngle * 0.05f * loopDistortSpeed) * 0.4f;
            float r = loopRadius + distort * loopDistortAmount;

            Vector3 pt = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * r;
            loopRenderer.SetPosition(i, pt);
        }
    }

    private Vector3 GetRopeStart()
    {
        return muzzle != null ? muzzle.position : owner.transform.position;
    }

    // Returns the best hookable target within throwRadius of headPos.
    // Prefers Rigidbody targets; falls back to any collider whose root has AgentController.
    private bool TryGetLatchTarget(Vector3 headPos, out Rigidbody rb, out Transform hitTransform, out Vector3 latchPoint)
    {
        rb = null;
        hitTransform = null;
        latchPoint = headPos;

        Collider[] nearby = Physics.OverlapSphere(headPos, throwRadius, ~0, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;

        foreach (Collider col in nearby)
        {
            if (col == null) continue;
            if (col.transform.IsChildOf(owner.transform)) continue;
            if ((hookableLayers.value & (1 << col.gameObject.layer)) == 0) continue;

            float d = Vector3.Distance(headPos, col.ClosestPoint(headPos));
            if (d >= bestDist) continue;

            Rigidbody candidateRb = col.GetComponentInParent<Rigidbody>();
            AgentController candidateAgent = col.GetComponentInParent<AgentController>();

            if (candidateRb == null && candidateAgent == null) continue;

            bestDist = d;
            rb = candidateRb;
            hitTransform = candidateAgent != null ? candidateAgent.transform : candidateRb.transform;
            latchPoint = col.ClosestPoint(headPos);
        }

        return hitTransform != null || rb != null;
    }
}
