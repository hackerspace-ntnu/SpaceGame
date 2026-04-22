// Generic enemy profile mirroring the PatrolRobot setup.
// Add to a prefab, configure values in the Inspector, then click Generate.
// After generating, this component can be removed — all modules are fully configured.
using UnityEngine;

public enum RobotHerdAttackStyle
{
    Melee,
    Ranged,
    KitingRanged,
    Mixed
}

public class EntityProfile_GenericEnemy : MonoBehaviour
{
    [TextArea(2, 4)]
    public string description = "Generic enemy. Patrols a base area, moves as a herd, engages on detection.";

    [Header("Health")]
    public int maxHealth = 100;
    public float despawnDelay = 12f;

    [Header("Base Patrol")]
    public Transform baseTransform;
    public float patrolRadius = 80f;
    public float patrolMinWait = 0.4f;
    public float patrolMaxWait = 2.5f;

    [Header("Herd")]
    public string herdId = "default";
    public float herdSpeed = 1f;

    [Header("Detection")]
    public float detectRange = 18f;
    public float loseTargetRange = 28f;
    public float fieldOfViewAngle = 130f;
    public float memoryDuration = 6f;
    public LayerMask occlusionLayers;

    [Header("Attack")]
    public RobotHerdAttackStyle attackStyle = RobotHerdAttackStyle.Mixed;
    public float chaseStopRange = 2f;
    public float chaseSpeed = 1.7f;

    [Header("Melee")]
    public float meleeRange = 2.2f;
    public float meleeCooldown = 0.8f;
    public int meleeDamage = 18;

    [Header("Ranged — ScriptableObject refs for AgentRangedCombatModule")]
    public AgentWeaponDefinition weapon;
    public AgentFireProfile fireProfile;
    public AgentAimProfile aimProfile;
    [Tooltip("Empty child transform at the gun barrel tip. Projectiles spawn here.")]
    public Transform muzzleTransform;

    [Header("Ranged Positioning")]
    public float keepDistanceDetect = 10f;
    public float keepDistancePreferred = 6f;
    public float keepDistanceSpeed = 1.25f;

    [Header("Alerts")]
    public float alertRadius = 28f;
    public LayerMask alertReceiverLayers;

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        patrolRadius = Mathf.Max(1f, patrolRadius);
        patrolMinWait = Mathf.Max(0f, patrolMinWait);
        patrolMaxWait = Mathf.Max(patrolMinWait, patrolMaxWait);
        herdSpeed = Mathf.Max(0.01f, herdSpeed);
        detectRange = Mathf.Max(0.1f, detectRange);
        loseTargetRange = Mathf.Max(detectRange, loseTargetRange);
        fieldOfViewAngle = Mathf.Clamp(fieldOfViewAngle, 1f, 360f);
        memoryDuration = Mathf.Max(0f, memoryDuration);
        chaseStopRange = Mathf.Max(0.1f, chaseStopRange);
        chaseSpeed = Mathf.Max(0.01f, chaseSpeed);
        meleeRange = Mathf.Max(0.1f, meleeRange);
        meleeCooldown = Mathf.Max(0.05f, meleeCooldown);
        meleeDamage = Mathf.Max(0, meleeDamage);
        keepDistanceDetect = Mathf.Max(0.1f, keepDistanceDetect);
        keepDistancePreferred = Mathf.Max(0.1f, keepDistancePreferred);
        keepDistanceSpeed = Mathf.Max(0.01f, keepDistanceSpeed);
        alertRadius = Mathf.Max(0f, alertRadius);
        despawnDelay = Mathf.Max(0f, despawnDelay);
    }
}
