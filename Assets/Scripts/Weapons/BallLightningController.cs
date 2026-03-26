using UnityEngine;

[ExecuteAlways]
public class BallLightningController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Renderer targetRenderer;

    [Header("Shadertoy Inputs")]
    [SerializeField] private bool useScreenResolution = true;
    [SerializeField] private Vector2 manualResolution = new Vector2(512f, 512f);
    [SerializeField] private Vector2 directBoltUv = new Vector2(0.80f, 0.22f);

    [Header("Direct Bolt Random Activation")]
    [SerializeField] private bool randomizeDirectBoltActivation = true;
    [SerializeField] private bool randomizeDirectBoltPosition = true;
    [SerializeField] private Vector2 directBoltUvMin = new Vector2(0.2f, 0.2f);
    [SerializeField] private Vector2 directBoltUvMax = new Vector2(0.85f, 0.85f);
    [SerializeField] private Vector2 activeDurationRange = new Vector2(0.06f, 0.22f);
    [SerializeField] private Vector2 inactiveDurationRange = new Vector2(0.18f, 0.90f);
    [SerializeField] private bool forceDirectBoltOn;

    private static readonly int ITimeId = Shader.PropertyToID("iTime");
    private static readonly int IResolutionId = Shader.PropertyToID("iResolution");
    private static readonly int IMouseId = Shader.PropertyToID("iMouse");

    private MaterialPropertyBlock propertyBlock;
    private bool directBoltActive;
    private float nextToggleTime;
    private bool useExternalDirectBolt;
    private bool externalDirectBoltActive;
    private Vector2 externalDirectBoltUv = new Vector2(0.80f, 0.22f);
    private bool currentDirectBoltActive;

    public bool IsDirectBoltActive => currentDirectBoltActive;

    public void SetExternalDirectBolt(Vector2 uv01, bool active)
    {
        useExternalDirectBolt = true;
        externalDirectBoltUv = new Vector2(Mathf.Clamp01(uv01.x), Mathf.Clamp01(uv01.y));
        externalDirectBoltActive = active;
    }

    public void ClearExternalDirectBolt()
    {
        useExternalDirectBolt = false;
    }

    private void Reset()
    {
        targetRenderer = GetComponent<Renderer>();
    }

    private void OnEnable()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
        }

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        directBoltActive = forceDirectBoltOn;
        nextToggleTime = GetNow() + GetRandomDuration(directBoltActive);
    }

    private void LateUpdate()
    {
        if (targetRenderer == null)
        {
            return;
        }

        float now = GetNow();
        if (randomizeDirectBoltActivation)
        {
            if (now >= nextToggleTime)
            {
                directBoltActive = !directBoltActive;
                if (directBoltActive && randomizeDirectBoltPosition)
                {
                    directBoltUv = new Vector2(
                        Random.Range(directBoltUvMin.x, directBoltUvMax.x),
                        Random.Range(directBoltUvMin.y, directBoltUvMax.y)
                    );
                }

                nextToggleTime = now + GetRandomDuration(directBoltActive);
            }
        }
        else
        {
            directBoltActive = forceDirectBoltOn;
        }

        Vector2 resolution = useScreenResolution
            ? new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height))
            : new Vector2(Mathf.Max(1f, manualResolution.x), Mathf.Max(1f, manualResolution.y));

        Vector2 chosenUv = useExternalDirectBolt
            ? externalDirectBoltUv
            : new Vector2(Mathf.Clamp01(directBoltUv.x), Mathf.Clamp01(directBoltUv.y));

        currentDirectBoltActive = useExternalDirectBolt
            ? externalDirectBoltActive
            : (directBoltActive || forceDirectBoltOn);

        Vector2 mousePixel = new Vector2(
            chosenUv.x * resolution.x,
            chosenUv.y * resolution.y
        );

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        targetRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(ITimeId, now);
        propertyBlock.SetVector(IResolutionId, new Vector4(resolution.x, resolution.y, 1f, 1f));
        propertyBlock.SetVector(IMouseId, new Vector4(mousePixel.x, mousePixel.y, currentDirectBoltActive ? 1f : 0f, 0f));
        targetRenderer.SetPropertyBlock(propertyBlock);
    }

    private float GetNow()
    {
        return Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
    }

    private float GetRandomDuration(bool currentlyActive)
    {
        Vector2 range = currentlyActive ? activeDurationRange : inactiveDurationRange;
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        return Random.Range(min, max);
    }
}
