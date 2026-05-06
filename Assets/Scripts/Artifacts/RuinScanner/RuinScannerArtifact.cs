using UnityEngine;
using FMODUnity;

/// <summary>
/// Ruin Scanner — emits a raised, top-down cone of light guided by the
/// player's horizontal aim. Every IRuinSecret the cone's rays hit is told to
/// Reveal() itself for `revealDuration` seconds. The detection cone matches
/// the visual cone exactly — what the beam touches is what gets exposed.
///
/// Designed to feel like a tool for interpretation rather than a universal
/// key — partial information that the player still has to act on.
/// </summary>
public class RuinScannerArtifact : ToolItem
{
    [Header("Beam (visual + secret detection — kept in sync)")]
    [Tooltip("The point the beam fires from. Should be the muzzle/tip of the scanner mesh. Falls back to this transform if unset.")]
    [SerializeField] private Transform muzzle;
    [Tooltip("Cone half-angle in degrees. The cone stays the same shape regardless of distance — wider angle = wider beam.")]
    [Range(2f, 45f)]
    [SerializeField] private float coneHalfAngleDegrees = 12f;
    [Tooltip("Maximum beam length (m). Each direction inside the cone reaches as far as its own raycast travels, capped at this distance — open directions stay long even if other directions hit a wall.")]
    [SerializeField] private float maxBeamDistance = 25f;
    [Tooltip("Minimum cone length so the beam is always visible even when aiming at point-blank geometry.")]
    [SerializeField] private float minBeamDistance = 2f;
    [Tooltip("How long revealed secrets stay visible. Seconds.")]
    [SerializeField] private float revealDuration = 6f;
    [Tooltip("Layers the detection rays can hit. Default = everything.")]
    [SerializeField] private LayerMask secretMask = ~0;
    [Tooltip("How many rays are cast inside the cone (1 axis + N rim rings × radial segments). Higher = denser detection.")]
    [Range(0, 6)]
    [SerializeField] private int detectionRings = 3;
    [Tooltip("Radial samples per ring.")]
    [Range(4, 32)]
    [SerializeField] private int detectionRadialSegments = 12;

    [Header("Top-Down Scan Pose")]
    [Tooltip("Raises the scan origin above the held scanner so the sweep reads the ruin from overhead.")]
    [SerializeField] private float topDownOriginHeight = 10f;
    [Tooltip("Moves the raised scan origin forward along the player's horizontal aim.")]
    [SerializeField] private float topDownForwardOffset = 6f;
    [Tooltip("Downward pitch of the scan cone relative to the horizon. 90 = straight down.")]
    [Range(45f, 89f)]
    [SerializeField] private float topDownPitchDegrees = 82f;

    [Header("Cooldown")]
    [Tooltip("Minimum time between pulses, in seconds.")]
    [SerializeField] private float cooldown = 2f;

    [Header("Debug")]
    [Tooltip("Log the chosen aim source + direction on every pulse. Use this to track down 'beam fires the wrong way' bugs.")]
    [SerializeField] private bool debugLogAim;

    private float nextUseTime;

    protected override bool CanUse()
    {
        if (Time.time < nextUseTime) return false;
        return base.CanUse();
    }

    [Header("VFX")]
    [Tooltip("Material used by the cone-of-light pulse (RuinScannerPulse shader).")]
    [SerializeField] private Material pulseMaterial;
    [Tooltip("Visual pulse fade duration. Seconds.")]
    [SerializeField] private float pulseDuration = 1.2f;

    [Header("Audio")]
    [Tooltip("Sound played when the pulse exposes at least one secret (in addition to the base useSound).")]
    [SerializeField] private EventReference discoverySound;

    protected override void Use()
    {
        base.Use();
        nextUseTime = Time.time + cooldown;

        // ---- Aim direction ----
        // Prefer the *currently active* main camera (handles mount/unmount and
        // third-person swaps where the AimProvider's serialized camera ref can
        // be stale or disabled). Fall back to AimProvider, then player forward.
        Vector3 rawAimDir = Vector3.zero;
        string aimSource = "none";
        var activeCam = Camera.main;
        if (activeCam != null && activeCam.isActiveAndEnabled)
        {
            rawAimDir = activeCam.transform.forward;
            aimSource = "Camera.main:" + activeCam.name;
        }
        else if (aimProvider != null)
        {
            rawAimDir = aimProvider.GetAimRay().direction;
            aimSource = "AimProvider";
        }
        else if (owner != null)
        {
            rawAimDir = owner.transform.forward;
            aimSource = "owner.forward";
        }
        else
        {
            rawAimDir = transform.forward;
            aimSource = "self.forward";
        }
        if (rawAimDir.sqrMagnitude < 0.0001f) rawAimDir = Vector3.forward;
        rawAimDir.Normalize();

        // ---- Beam origin = raised top-down scan pose ----
        Transform muzzleT = muzzle != null ? muzzle : transform;
        Vector3 horizontalAim = ResolveHorizontalAim(rawAimDir);
        Vector3 beamOrigin = muzzleT.position
            + Vector3.up * Mathf.Max(0f, topDownOriginHeight)
            + horizontalAim * topDownForwardOffset;
        Vector3 aimDir = ResolveTopDownAim(horizontalAim);

        if (debugLogAim)
        {
            Debug.Log($"[RuinScanner] source={aimSource} rawAim={rawAimDir} scanAim={aimDir} scanOrigin={beamOrigin} muzzle={(muzzle != null ? muzzle.name : "<self>")}");
        }

        // ---- Per-direction ray expansion ----
        // Each direction in the cone reaches as far as *its own* raycast
        // travels — so if the center ray hits a wall but the rim above it has
        // open sky, that side of the cone keeps extending. The visual mesh and
        // the detection rays both use these per-direction lengths, so the
        // shape of the beam still equals the shape of what it scanned.
        float baseRadius = maxBeamDistance * Mathf.Tan(coneHalfAngleDegrees * Mathf.Deg2Rad);

        // Build an orthonormal basis perpendicular to aimDir for the rim rings.
        Vector3 right = Vector3.Cross(aimDir, Vector3.up);
        if (right.sqrMagnitude < 0.0001f) right = Vector3.Cross(aimDir, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, aimDir).normalized;

        var revealed = new System.Collections.Generic.HashSet<IRuinSecret>();
        bool anyHit = false;

        // Center ray.
        float centerSlant = CastSlant(beamOrigin, aimDir, maxBeamDistance, revealed, ref anyHit);

        // Rim rings — each (ring, segment) has its own slant length.
        // Outer rim ring (ring == detectionRings) drives the visual mesh's
        // base ring per radial segment.
        int rings = Mathf.Max(1, detectionRings);
        int segs = Mathf.Max(4, detectionRadialSegments);
        var outerRimSlants = new float[segs];
        Vector3 axisEnd = beamOrigin + aimDir * maxBeamDistance;
        for (int ring = 1; ring <= rings; ring++)
        {
            float t = (float)ring / rings;
            float ringRadius = baseRadius * t;
            for (int s = 0; s < segs; s++)
            {
                float a = (s / (float)segs) * Mathf.PI * 2f;
                Vector3 rimOffset = (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * ringRadius;
                Vector3 target = axisEnd + rimOffset;
                Vector3 dir = (target - beamOrigin).normalized;
                float maxSlant = Vector3.Distance(beamOrigin, target);
                float slant = CastSlant(beamOrigin, dir, maxSlant, revealed, ref anyHit);
                if (ring == rings) outerRimSlants[s] = slant;
            }
        }

        foreach (var s in revealed) s.Reveal(revealDuration);

        // ---- Visual pulse ----
        // Only show the pulse when the beam actually landed on something —
        // firing into open sky shouldn't produce a ground scan.
        // Mesh base ring follows the outer rim slants so the cone bulges out
        // wherever rays travelled further. minBeamDistance keeps the beam
        // visible at point-blank.
        if (pulseMaterial != null && anyHit)
        {
            float visibleCenter = Mathf.Max(minBeamDistance, centerSlant);
            float[] visibleRim = new float[segs];
            for (int s = 0; s < segs; s++)
                visibleRim[s] = Mathf.Max(minBeamDistance, outerRimSlants[s]);
            RuinScannerPulse.Spawn(beamOrigin, aimDir, right, up, baseRadius, visibleCenter, visibleRim, pulseDuration, pulseMaterial);
        }

        // ---- Discovery audio cue ----
        if (revealed.Count > 0 && !discoverySound.IsNull)
            AudioManager.Instance.PlayEvent(discoverySound, muzzleT.position);
    }

    private Vector3 ResolveHorizontalAim(Vector3 aimDir)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(aimDir, Vector3.up);
        if (horizontal.sqrMagnitude >= 0.0001f) return horizontal.normalized;

        if (owner != null)
        {
            horizontal = Vector3.ProjectOnPlane(owner.transform.forward, Vector3.up);
            if (horizontal.sqrMagnitude >= 0.0001f) return horizontal.normalized;
        }

        horizontal = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (horizontal.sqrMagnitude >= 0.0001f) return horizontal.normalized;

        return Vector3.forward;
    }

    private Vector3 ResolveTopDownAim(Vector3 horizontalAim)
    {
        float pitch = Mathf.Clamp(topDownPitchDegrees, 45f, 89f) * Mathf.Deg2Rad;
        return (horizontalAim * Mathf.Cos(pitch) + Vector3.down * Mathf.Sin(pitch)).normalized;
    }

    /// <summary>
    /// Casts one ray inside the cone, reveals any IRuinSecret it hits, and
    /// returns the slant length the cone should occupy in this direction —
    /// either the full requested distance (open sky) or the distance to the
    /// blocker. Detection and visual length stay in sync because they share
    /// this number.
    /// </summary>
    private float CastSlant(Vector3 origin, Vector3 dir, float distance,
        System.Collections.Generic.HashSet<IRuinSecret> revealed, ref bool anyHit)
    {
        // Use a single all-layers cast so opaque world geometry blocks the
        // beam, but still surface IRuinSecret hits from the secret layers.
        // The overhead cone can pass through the player, so ignore owner hits.
        RaycastHit[] hits = Physics.RaycastAll(origin, dir, distance, ~0, QueryTriggerInteraction.Collide);
        if (hits.Length == 0)
            return distance;

        int closestIndex = -1;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null) continue;
            if (owner != null && hitCollider.transform.IsChildOf(owner.transform)) continue;
            if (hits[i].distance >= closestDistance) continue;

            closestIndex = i;
            closestDistance = hits[i].distance;
        }

        if (closestIndex < 0)
            return distance;

        RaycastHit hit = hits[closestIndex];
        anyHit = true;
        if (((1 << hit.collider.gameObject.layer) & secretMask) != 0)
        {
            var secret = hit.collider.GetComponentInParent<IRuinSecret>();
            if (secret != null) revealed.Add(secret);
        }
        return hit.distance;
    }
}
