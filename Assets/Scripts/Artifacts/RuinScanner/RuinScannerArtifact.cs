using UnityEngine;
using FMODUnity;

/// <summary>
/// Ruin Scanner — emits a forward cone of light along the player's aim that
/// exposes traces of old alien tech. Every IRuinSecret the cone's rays hit
/// is told to Reveal() itself for `revealDuration` seconds. The detection
/// cone matches the visual cone exactly — what the beam touches is what
/// gets exposed.
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
    [Tooltip("Maximum beam length (m). The cone stops earlier if the center ray hits geometry.")]
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
        Vector3 aimDir = Vector3.zero;
        string aimSource = "none";
        var activeCam = Camera.main;
        if (activeCam != null && activeCam.isActiveAndEnabled)
        {
            aimDir = activeCam.transform.forward;
            aimSource = "Camera.main:" + activeCam.name;
        }
        else if (aimProvider != null)
        {
            aimDir = aimProvider.GetAimRay().direction;
            aimSource = "AimProvider";
        }
        else if (owner != null)
        {
            aimDir = owner.transform.forward;
            aimSource = "owner.forward";
        }
        else
        {
            aimDir = transform.forward;
            aimSource = "self.forward";
        }
        if (aimDir.sqrMagnitude < 0.0001f) aimDir = Vector3.forward;
        aimDir.Normalize();

        if (debugLogAim)
            Debug.Log($"[RuinScanner] aim={aimDir} source={aimSource} muzzle={(muzzle != null ? muzzle.name : "<self>")}");

        // ---- Beam origin = muzzle ----
        Transform muzzleT = muzzle != null ? muzzle : transform;
        Vector3 beamOrigin = muzzleT.position;

        // ---- Center ray to size the cone ----
        // The cone reaches as far as the center ray travels (capped at
        // maxBeamDistance), but never shorter than minBeamDistance — keeps the
        // beam visible when you aim at point-blank geometry like the floor
        // beneath your feet.
        float coneLength = maxBeamDistance;
        if (Physics.Raycast(beamOrigin, aimDir, out RaycastHit centerHit,
                maxBeamDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            coneLength = centerHit.distance;
        }
        coneLength = Mathf.Max(minBeamDistance, coneLength);

        // Cone base radius is derived from a fixed half-angle, so the cone
        // always *looks* like a beam regardless of distance — short shots stay
        // narrow rather than ballooning into a flat disk.
        float baseRadius = coneLength * Mathf.Tan(coneHalfAngleDegrees * Mathf.Deg2Rad);

        // ---- Detection: cast a fan of rays inside the cone ----
        // Whatever any ray hits gets revealed — visual and detection use the
        // same geometry, so "if the rays hit it, it's discovered" is literally
        // what happens. Occlusion is respected: a wall blocks rays behind it.
        var revealed = new System.Collections.Generic.HashSet<IRuinSecret>();
        TryHitWithRay(beamOrigin, aimDir, coneLength, revealed);

        // Build an orthonormal basis perpendicular to aimDir for the rim rings.
        Vector3 right = Vector3.Cross(aimDir, Vector3.up);
        if (right.sqrMagnitude < 0.0001f) right = Vector3.Cross(aimDir, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, aimDir).normalized;

        Vector3 endPoint = beamOrigin + aimDir * coneLength;
        for (int ring = 1; ring <= detectionRings; ring++)
        {
            float t = (float)ring / detectionRings;          // 0..1 of the cone radius
            float ringRadius = baseRadius * t;
            for (int s = 0; s < detectionRadialSegments; s++)
            {
                float a = (s / (float)detectionRadialSegments) * Mathf.PI * 2f;
                Vector3 rimOffset = (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * ringRadius;
                Vector3 target = endPoint + rimOffset;
                Vector3 dir = (target - beamOrigin).normalized;
                // Use the cone's slant length so corner rays reach the rim.
                float slant = Vector3.Distance(beamOrigin, target);
                TryHitWithRay(beamOrigin, dir, slant, revealed);
            }
        }

        foreach (var s in revealed) s.Reveal(revealDuration);

        // ---- Visual pulse ----
        if (pulseMaterial != null)
            RuinScannerPulse.Spawn(beamOrigin, aimDir, baseRadius, coneLength, pulseDuration, pulseMaterial);

        // ---- Discovery audio cue ----
        if (revealed.Count > 0 && !discoverySound.IsNull)
            AudioManager.Instance.PlayEvent(discoverySound, beamOrigin);
    }

    private void TryHitWithRay(Vector3 origin, Vector3 dir, float distance,
        System.Collections.Generic.HashSet<IRuinSecret> revealed)
    {
        if (!Physics.Raycast(origin, dir, out RaycastHit hit, distance, secretMask, QueryTriggerInteraction.Collide))
            return;
        var secret = hit.collider.GetComponentInParent<IRuinSecret>();
        if (secret != null) revealed.Add(secret);
    }
}
