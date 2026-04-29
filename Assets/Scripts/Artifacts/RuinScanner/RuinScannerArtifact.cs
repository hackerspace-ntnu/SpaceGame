using UnityEngine;
using FMODUnity;

/// <summary>
/// Ruin Scanner — emits a pulse from the player that exposes traces of old
/// alien tech. Two effects fire on Use():
///
///   1. Every IRuinSecret inside `secretRevealRadius` is told to Reveal()
///      itself for `revealDuration` seconds (hidden doors fade in, dormant
///      glyphs glow, etc).
///   2. Every hidden MapPOI inside `mapDiscoverRadius` is marked discovered
///      on the player's map, so the fog cloud over a nearby ruin lifts.
///
/// A visual pulse sphere is spawned at the player and an optional ping sound
/// plays. Designed to feel like a tool for interpretation rather than a
/// universal key — partial information that the player still has to act on.
/// </summary>
public class RuinScannerArtifact : ToolItem
{
    [Header("Pulse")]
    [Tooltip("World-space radius of the reveal pulse for in-world secrets.")]
    [SerializeField] private float secretRevealRadius = 25f;
    [Tooltip("World-space radius for force-discovering hidden map POIs.")]
    [SerializeField] private float mapDiscoverRadius = 60f;
    [Tooltip("How long revealed secrets stay visible. Seconds.")]
    [SerializeField] private float revealDuration = 6f;
    [Tooltip("Layers searched for IRuinSecret components. Default = everything.")]
    [SerializeField] private LayerMask secretMask = ~0;

    [Header("VFX")]
    [Tooltip("Material used by the expanding pulse sphere (RuinScannerPulse shader).")]
    [SerializeField] private Material pulseMaterial;
    [Tooltip("Visual pulse expansion duration. Seconds.")]
    [SerializeField] private float pulseDuration = 1.2f;
    [Tooltip("Visual pulse max radius (independent of detection radius for art reasons).")]
    [SerializeField] private float pulseVisualRadius = 30f;

    [Header("Audio")]
    [Tooltip("Sound played when a hidden POI is newly discovered (in addition to the base useSound).")]
    [SerializeField] private EventReference discoverySound;

    protected override void Use()
    {
        base.Use();

        Vector3 origin = owner != null ? owner.transform.position : transform.position;

        // 1. In-world secrets: reveal whatever is in pulse range.
        int secretsRevealed = 0;
        Collider[] hits = Physics.OverlapSphere(origin, secretRevealRadius, secretMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;
            // Search up the hierarchy so a child collider on a hidden door still hits its RuinSecret root.
            var secret = col.GetComponentInParent<IRuinSecret>();
            if (secret == null) continue;
            secret.Reveal(revealDuration);
            secretsRevealed++;
        }

        // 2. Map POIs: lift the fog cloud over nearby hidden ruins.
        int poisDiscovered = 0;
        if (MapService.Instance != null)
            poisDiscovered = MapService.Instance.DiscoverPOIsInRadius(origin, mapDiscoverRadius);

        // 3. Visual pulse — spawned at the scanner itself (hand-socket height) so
        // the cone has room to reach the ground rather than being buried in it.
        if (pulseMaterial != null)
        {
            Vector3 vfxOrigin = transform.position;
            float coneLength = pulseVisualRadius;
            // Raycast down so the cone stops at the real ground surface.
            if (Physics.Raycast(vfxOrigin, Vector3.down, out RaycastHit groundHit,
                    pulseVisualRadius * 4f, ~0, QueryTriggerInteraction.Ignore))
            {
                coneLength = Mathf.Max(0.1f, groundHit.distance);
            }
            RuinScannerPulse.Spawn(vfxOrigin, pulseVisualRadius, coneLength, pulseDuration, pulseMaterial);
        }

        // 4. Discovery audio cue (only if something was uncovered).
        if (poisDiscovered > 0 && !discoverySound.IsNull)
            AudioManager.Instance.PlayEvent(discoverySound, origin);
    }
}
