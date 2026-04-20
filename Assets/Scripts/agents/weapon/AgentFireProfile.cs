using UnityEngine;

[CreateAssetMenu(menuName = "Agents/Fire Profile", fileName = "NewAgentFireProfile")]
public class AgentFireProfile : ScriptableObject
{
    [Header("Range")]
    public float minRange = 4f;
    public float maxRange = 15f;

    [Header("Cadence")]
    [Tooltip("Seconds between trigger pulls (or burst starts).")]
    public float fireCooldown = 1.2f;

    [Header("Burst")]
    [Tooltip("Projectiles fired per trigger pull.")]
    [Min(1)]
    public int burstCount = 1;
    [Tooltip("Seconds between shots within a burst.")]
    public float burstInterval = 0.12f;

    private void OnValidate()
    {
        minRange = Mathf.Max(0f, minRange);
        maxRange = Mathf.Max(minRange + 0.1f, maxRange);
        fireCooldown = Mathf.Max(0.05f, fireCooldown);
        burstCount = Mathf.Max(1, burstCount);
        burstInterval = Mathf.Max(0.01f, burstInterval);
    }
}
