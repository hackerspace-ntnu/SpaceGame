using UnityEngine;

[DisallowMultipleComponent]
public class BallLightningBoltTargeting : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BallLightningController lightningController;
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private Transform uvBasis;
    [SerializeField] private Light strikePointLight;

    [Header("Raycast Scan")]
    [SerializeField] private LayerMask strikeMask = ~0;
    [SerializeField] private float scanDistance = 18f;
    [SerializeField] private float scanInterval = 0.02f;
    [SerializeField] private int raysPerScan = 7;
    [SerializeField] private float coneAngle = 38f;

    [Header("Strike Timing")]
    [SerializeField] private bool usePulseStrikes = true;
    [SerializeField] private Vector2 pulseDurationRange = new Vector2(0.035f, 0.090f);
    [SerializeField] private Vector2 pulseCooldownRange = new Vector2(0.055f, 0.160f);

    [Header("UV Motion")]
    [SerializeField] private float uvRetargetSpeed = 18f;
    [SerializeField] private float uvJitterAmplitude = 0.035f;
    [SerializeField] private float uvJitterFrequency = 16f;

    [Header("UV Mapping")]
    [SerializeField] private Vector2 uvAxisScale = new Vector2(1f, 0.5f);

    [Header("Fallback")]
    [SerializeField] private Vector2 fallbackUv = new Vector2(0.80f, 0.22f);
    [SerializeField] private bool keepPreviousTargetIfNoHit = true;

    [Header("Strike Hit Light")]
    [SerializeField] private float hitLightIntensity = 30f;
    [SerializeField] private float hitLightHoverOffset = 0.12f;

    private float nextScanTime;
    private bool hasLastUv;
    private Vector2 lastUv;
    private Vector2 currentUv;
    private float pulseEndTime;
    private float cooldownEndTime;
    private float jitterSeed;
    private Vector3 lastHitPoint;
    private Vector3 lastHitNormal = Vector3.up;
    private bool hasLastHit;
    private Light runtimeStrikeLight;

    private void Reset()
    {
        if (lightningController == null)
        {
            lightningController = GetComponentInChildren<BallLightningController>();
        }

        if (rayOrigin == null)
        {
            rayOrigin = transform;
        }

        if (uvBasis == null)
        {
            uvBasis = lightningController != null ? lightningController.transform : transform;
        }

        if (strikePointLight == null)
        {
            strikePointLight = GetComponentInChildren<Light>();
        }
    }

    private void OnEnable()
    {
        nextScanTime = 0f;
        currentUv = fallbackUv;
        pulseEndTime = 0f;
        cooldownEndTime = 0f;
        jitterSeed = Random.Range(0f, 1000f);
        hasLastHit = false;
        SetupRuntimeStrikeLight();
    }

    private void OnDisable()
    {
        if (lightningController != null)
        {
            lightningController.ClearExternalDirectBolt();
        }

        if (runtimeStrikeLight != null)
        {
            Destroy(runtimeStrikeLight.gameObject);
            runtimeStrikeLight = null;
        }

        if (strikePointLight != null)
        {
            strikePointLight.intensity = 0f;
            strikePointLight.enabled = true;
        }
    }

    private void Update()
    {
        if (lightningController == null)
        {
            return;
        }

        bool hitFound = false;
        Vector2 desiredUv = keepPreviousTargetIfNoHit && hasLastUv ? lastUv : fallbackUv;

        if (Time.time >= nextScanTime)
        {
            nextScanTime = Time.time + Mathf.Max(0.005f, scanInterval);

            if (TryFindStrikeHit(out RaycastHit hit))
            {
                hitFound = true;
                desiredUv = WorldPointToUv(hit.point);
                lastUv = desiredUv;
                hasLastUv = true;
                lastHitPoint = hit.point;
                lastHitNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
                hasLastHit = true;

                if (CanStartPulse())
                {
                    StartPulse();
                }
            }
            else if (!keepPreviousTargetIfNoHit)
            {
                desiredUv = fallbackUv;
            }
        }

        float lerpFactor = 1f - Mathf.Exp(-Mathf.Max(0.01f, uvRetargetSpeed) * Time.deltaTime);
        currentUv = Vector2.Lerp(currentUv, desiredUv, lerpFactor);

        bool pulseActive = IsPulseActive();
        Vector2 finalUv = currentUv;
        if (pulseActive)
        {
            finalUv += GetUvJitter(Time.time);
            finalUv.x = Mathf.Clamp01(finalUv.x);
            finalUv.y = Mathf.Clamp01(finalUv.y);
        }

        bool active = usePulseStrikes ? pulseActive : hitFound;
        lightningController.SetExternalDirectBolt(finalUv, active);
        UpdateStrikePointLight(active);
    }

    private void UpdateStrikePointLight(bool active)
    {
        Light hitLight = runtimeStrikeLight != null ? runtimeStrikeLight : strikePointLight;
        if (hitLight == null)
        {
            return;
        }

        if (active && hasLastHit)
        {
            hitLight.transform.position = lastHitPoint + lastHitNormal * hitLightHoverOffset;
            hitLight.intensity = hitLightIntensity;
            return;
        }

        hitLight.intensity = 0f;
    }

    private void SetupRuntimeStrikeLight()
    {
        if (strikePointLight == null)
        {
            return;
        }

        GameObject lightObject = new GameObject("BallLightningStrikePointLight");
        runtimeStrikeLight = lightObject.AddComponent<Light>();
        CopyLightSettings(strikePointLight, runtimeStrikeLight);
        runtimeStrikeLight.intensity = 0f;
        runtimeStrikeLight.enabled = true;

        strikePointLight.intensity = 0f;
        strikePointLight.enabled = false;
    }

    private static void CopyLightSettings(Light source, Light destination)
    {
        destination.type = source.type;
        destination.color = source.color;
        destination.range = source.range;
        destination.spotAngle = source.spotAngle;
        destination.innerSpotAngle = source.innerSpotAngle;
        destination.shadows = source.shadows;
        destination.cullingMask = source.cullingMask;
        destination.renderMode = source.renderMode;
        destination.intensity = source.intensity;
    }

    private bool IsPulseActive()
    {
        if (!usePulseStrikes)
        {
            return false;
        }

        return Time.time < pulseEndTime;
    }

    private bool CanStartPulse()
    {
        if (!usePulseStrikes)
        {
            return false;
        }

        return Time.time >= cooldownEndTime;
    }

    private void StartPulse()
    {
        float pulseDuration = Random.Range(
            Mathf.Min(pulseDurationRange.x, pulseDurationRange.y),
            Mathf.Max(pulseDurationRange.x, pulseDurationRange.y)
        );

        float cooldownDuration = Random.Range(
            Mathf.Min(pulseCooldownRange.x, pulseCooldownRange.y),
            Mathf.Max(pulseCooldownRange.x, pulseCooldownRange.y)
        );

        pulseEndTime = Time.time + pulseDuration;
        cooldownEndTime = pulseEndTime + cooldownDuration;
    }

    private Vector2 GetUvJitter(float now)
    {
        float freq = Mathf.Max(0.01f, uvJitterFrequency);
        float amp = Mathf.Max(0f, uvJitterAmplitude);
        float jitterX = Mathf.PerlinNoise(jitterSeed, now * freq) * 2f - 1f;
        float jitterY = Mathf.PerlinNoise(jitterSeed + 33.17f, now * (freq * 1.21f)) * 2f - 1f;
        return new Vector2(jitterX, jitterY) * amp;
    }

    private bool TryFindStrikeHit(out RaycastHit bestHit)
    {
        Transform originTransform = rayOrigin != null ? rayOrigin : transform;
        Vector3 origin = originTransform.position;
        Vector3 forward = originTransform.forward;

        bool foundAny = false;
        float bestDistance = float.MaxValue;
        bestHit = default;

        if (Physics.Raycast(origin, forward, out RaycastHit centerHit, scanDistance, strikeMask, QueryTriggerInteraction.Ignore))
        {
            foundAny = true;
            bestDistance = centerHit.distance;
            bestHit = centerHit;
        }

        int rayCount = Mathf.Max(1, raysPerScan);
        for (int i = 0; i < rayCount; i++)
        {
            float t = (i + 0.5f) / rayCount;
            float yaw = Mathf.Lerp(-coneAngle, coneAngle, t);
            float pitch = Mathf.Sin((t + Time.time * 0.17f) * Mathf.PI * 2f) * coneAngle * 0.45f;

            Quaternion spread = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 dir = spread * forward;

            if (!Physics.Raycast(origin, dir, out RaycastHit hit, scanDistance, strikeMask, QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            if (hit.distance < bestDistance)
            {
                foundAny = true;
                bestDistance = hit.distance;
                bestHit = hit;
            }
        }

        return foundAny;
    }

    private Vector2 WorldPointToUv(Vector3 worldPoint)
    {
        Transform basis = uvBasis != null ? uvBasis : transform;
        Vector3 toHit = worldPoint - basis.position;
        if (toHit.sqrMagnitude < 0.0001f)
        {
            return fallbackUv;
        }

        Vector3 localDir = basis.InverseTransformDirection(toHit.normalized);
        float scaleX = Mathf.Max(0.0001f, uvAxisScale.x);
        float scaleY = Mathf.Max(0.0001f, uvAxisScale.y);

        float mappedX = localDir.x / scaleX;
        float mappedY = localDir.y / scaleY;

        Vector2 uv = new Vector2(mappedX * 0.5f + 0.5f, mappedY * 0.5f + 0.5f);
        uv.x = Mathf.Clamp01(uv.x);
        uv.y = Mathf.Clamp01(uv.y);
        return uv;
    }
}
