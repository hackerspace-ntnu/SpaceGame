using UnityEngine;

/// <summary>
/// BallLightning projectile implementation.
/// Extends abstract Projectile class with wandering AI movement,
/// hover behavior, and lightning effects.
/// </summary>
public class BallLightningProjectile : Projectile
{
    [Header("Movement")]
    [SerializeField] private float speed = 45f;
    [SerializeField] private float wanderStrength = 4.5f;
    [SerializeField] private float wanderFrequency = 1.8f;
    [SerializeField] private float bobAmplitude = 0.45f;
    [SerializeField] private float bobFrequency = 4.0f;

    [Header("Hover")]
    [SerializeField] private bool hoverAboveGround = true;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float desiredHoverHeight = 1.2f;
    [SerializeField] private float hoverRayStartOffset = 1.5f;
    [SerializeField] private float hoverRayDistance = 8f;
    [SerializeField] private float hoverCorrectionStrength = 5f;
    [SerializeField] private float maxVerticalCorrection = 3f;

    [Header("Projectile Light")]
    [SerializeField] private Light projectileLight;
    [SerializeField] private BallLightningController lightningController;
    [SerializeField] private Renderer colorSourceRenderer;
    [SerializeField] private bool syncLightColorFromShader = true;
    [SerializeField] private Color baseLightColor = new Color(0.35f, 0.75f, 1.25f);
    [SerializeField] private Color boltLightColor = new Color(1.0f, 0.35f, 0.85f);
    [SerializeField] private float baseLightIntensity = 8f;
    [SerializeField] private float boltLightIntensity = 24f;
    [SerializeField] private float flickerAmplitude = 0.25f;
    [SerializeField] private float flickerFrequency = 24f;
    [SerializeField] private float lightResponseSpeed = 10f;

    [Header("Impact Spotlight")]
    [SerializeField] private bool spawnImpactSpotlight = true;
    [SerializeField] private float impactSpotAngle = 70f;
    [SerializeField] private float impactSpotRange = 12f;
    [SerializeField] private float impactSpotIntensity = 35f;
    [SerializeField] private float impactSpotLifetime = 0.25f;

    private float noiseSeed;
    private float bobPhase;
    private float lightNoiseSeed;

    private void OnEnable()
    {
        if (projectileLight == null)
        {
            projectileLight = GetComponentInChildren<Light>();
        }

        if (lightningController == null)
        {
            lightningController = GetComponentInChildren<BallLightningController>();
        }

        if (colorSourceRenderer == null)
        {
            colorSourceRenderer = GetComponentInChildren<Renderer>();
        }

        if (syncLightColorFromShader)
        {
            TrySyncColorsFromShader();
        }
    }

    public override void Initialize(Vector3 forwardDirection, Transform owner, Vector3 startPosition)
    {
        base.Initialize(forwardDirection, owner, startPosition);

        noiseSeed = Random.Range(0f, 1000f);
        bobPhase = Random.Range(0f, Mathf.PI * 2f);
        lightNoiseSeed = Random.Range(0f, 1000f);
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        UpdateMovement();
    }

    protected override void UpdateMovement()
    {
        float elapsed = GetElapsedTime();
        Vector3 frameVelocity = direction * speed;

        // Calculate right axis for wandering
        Vector3 rightAxis = Vector3.Cross(Vector3.up, direction).normalized;
        if (rightAxis.sqrMagnitude < 0.0001f)
        {
            rightAxis = transform.right;
        }

        // Wander behavior
        float wanderSampleA = Mathf.PerlinNoise(noiseSeed, elapsed * wanderFrequency) * 2f - 1f;
        float wanderSampleB = Mathf.PerlinNoise(noiseSeed + 17.31f, elapsed * wanderFrequency * 0.73f) * 2f - 1f;
        Vector3 wanderVelocity = (rightAxis * wanderSampleA + Vector3.up * wanderSampleB * 0.45f) * wanderStrength;

        // Bob behavior
        float bob = Mathf.Sin(elapsed * bobFrequency + bobPhase) * bobAmplitude;
        Vector3 bobVelocity = Vector3.up * bob;

        // Hover correction
        Vector3 hoverVelocity = Vector3.zero;
        if (hoverAboveGround)
        {
            Vector3 rayOrigin = transform.position + Vector3.up * hoverRayStartOffset;
            float rayLength = hoverRayDistance + hoverRayStartOffset;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit groundHit, rayLength, groundMask, QueryTriggerInteraction.Ignore))
            {
                float currentHeight = groundHit.distance - hoverRayStartOffset;
                float heightError = desiredHoverHeight - currentHeight;
                float correction = Mathf.Clamp(heightError * hoverCorrectionStrength, -maxVerticalCorrection, maxVerticalCorrection);
                hoverVelocity = Vector3.up * correction;
            }
        }

        frameVelocity += wanderVelocity + bobVelocity + hoverVelocity;

        Vector3 moveDelta = frameVelocity * Time.deltaTime;
        float travelDistance = moveDelta.magnitude;

        if (travelDistance <= 0.0001f)
        {
            return;
        }

        Vector3 moveDir = moveDelta / travelDistance;
        Vector3 start = transform.position;
        Vector3 end = start + moveDelta;

        // Collision check with sphere cast
        if (Physics.SphereCast(start, collisionRadius, moveDir, out RaycastHit hit, travelDistance, hitMask, QueryTriggerInteraction.Ignore))
        {
            if (!IsOwnerHit(hit.collider.transform))
            {
                transform.position = hit.point;
                HandleHit(hit);
                return;
            }
        }

        transform.position = end;

        // Update rotation to face direction of movement
        Vector3 planarForward = new Vector3(moveDir.x, 0f, moveDir.z);
        if (planarForward.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(planarForward.normalized, Vector3.up);
        }

        UpdateProjectileLight(elapsed);
    }

    protected override void OnImpact(Vector3 position, Vector3 normal, Collider hitCollider)
    {
        if (spawnImpactSpotlight)
        {
            SpawnImpactSpotlight(position, normal);
        }
    }

    private void UpdateProjectileLight(float elapsed)
    {
        if (projectileLight == null)
        {
            return;
        }

        bool boltActive = lightningController != null && lightningController.IsDirectBoltActive;

        float flickerNoise = Mathf.PerlinNoise(lightNoiseSeed, elapsed * flickerFrequency) * 2f - 1f;
        float flickerFactor = 1f + flickerNoise * Mathf.Max(0f, flickerAmplitude);

        float targetIntensity = (boltActive ? boltLightIntensity : baseLightIntensity) * flickerFactor;
        Color targetColor = boltActive ? boltLightColor : baseLightColor;

        float t = 1f - Mathf.Exp(-lightResponseSpeed * Time.deltaTime);
        projectileLight.intensity = Mathf.Lerp(projectileLight.intensity, targetIntensity, t);
        projectileLight.color = Color.Lerp(projectileLight.color, targetColor, t);
    }

    private void SpawnImpactSpotlight(Vector3 position, Vector3 normal)
    {
        GameObject lightObject = new GameObject("BallLightningImpactSpotlight");
        lightObject.transform.position = position + normal * 0.03f;
        lightObject.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);

        Light impactLight = lightObject.AddComponent<Light>();
        impactLight.type = LightType.Spot;
        impactLight.color = boltLightColor;
        impactLight.intensity = impactSpotIntensity;
        impactLight.range = impactSpotRange;
        impactLight.spotAngle = impactSpotAngle;
        impactLight.shadows = LightShadows.None;

        Destroy(lightObject, impactSpotLifetime);
    }

    private void TrySyncColorsFromShader()
    {
        if (colorSourceRenderer == null)
        {
            return;
        }

        Material material = colorSourceRenderer.sharedMaterial;
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_StreamColor"))
        {
            baseLightColor = material.GetColor("_StreamColor");
        }
        else if (material.HasProperty("_BlowoutColor"))
        {
            baseLightColor = material.GetColor("_BlowoutColor");
        }

        if (material.HasProperty("_CorePunchColor"))
        {
            Color candidate = material.GetColor("_CorePunchColor");
            boltLightColor = Color.Lerp(candidate, new Color(1.0f, 0.35f, 0.85f), 0.55f);
        }
    }
}
