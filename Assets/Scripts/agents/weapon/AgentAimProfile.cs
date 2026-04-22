using UnityEngine;

[CreateAssetMenu(menuName = "Agents/Aim Profile", fileName = "NewAgentAimProfile")]
public class AgentAimProfile : ScriptableObject
{
    [Tooltip("Base inaccuracy cone half-angle in degrees. 0 = perfect aim.")]
    [Range(0f, 45f)]
    public float baseSpreadAngle = 5f;

    [Tooltip("Additional spread added per shot within a burst. Resets between bursts.")]
    [Range(0f, 20f)]
    public float spreadGrowthPerBurstShot = 2f;

    [Tooltip("0 = no lead prediction, 1 = perfect lead. Values in between simulate reaction delay.")]
    [Range(0f, 1f)]
    public float aimLeadFactor = 0.5f;

    [Tooltip("If true, the agent will not fire unless it has line-of-sight to the target. " +
             "The LoS raycast is performed by the agent's PerceptionModule; if none is present, the check is skipped with a warning.")]
    public bool requireLineOfSight = true;
}
