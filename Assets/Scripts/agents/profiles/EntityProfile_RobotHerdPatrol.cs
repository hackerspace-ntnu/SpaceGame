// Data-only profile for robot groups that patrol around a base, move as a herd,
// detect hostile entities nearby, and switch into fast melee or ranged attacks.
using UnityEngine;

public enum RobotHerdAttackStyle
{
    Melee,
    Ranged,
    KitingRanged,
    Mixed
}

public class EntityProfile_RobotHerdPatrol : MonoBehaviour
{
    [Header("Base Patrol")]
    public Transform baseTransform;
    public float patrolRadius = 80f;
    public float patrolSampleDistance = 8f;
    public float patrolMinDestinationDistance = 8f;
    public float patrolMinWait = 0.4f;
    public float patrolMaxWait = 2.5f;

    [Header("Herd")]
    public string herdId = "default";
    public float herdSpeed = 1f;

    [Header("Detection")]
    public string targetTag = "Player";
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

    [Header("Ranged")]
    public GameObject projectilePrefab;
    public Transform muzzleTransform;
    public float minFireRange = 4f;
    public float maxFireRange = 16f;
    public float projectileSpeed = 24f;
    public float fireCooldown = 1.1f;
    public int burstCount = 2;
    public float burstInterval = 0.12f;
    public float spreadAngle = 3f;
    public bool leadTarget = true;

    [Header("Ranged Positioning")]
    public float keepDistanceDetect = 10f;
    public float keepDistancePreferred = 6f;
    public float keepDistanceSpeed = 1.25f;
    public float strafeEngageRange = 15f;
    public float strafeRadius = 7f;

    [Header("Alerts")]
    public float alertRadius = 28f;
    public LayerMask alertReceiverLayers;

    [Header("Health / Loot")]
    public float despawnDelay = 12f;

    private void OnValidate()
    {
        patrolRadius = Mathf.Max(1f, patrolRadius);
        patrolSampleDistance = Mathf.Max(0.5f, patrolSampleDistance);
        patrolMinDestinationDistance = Mathf.Max(0.1f, patrolMinDestinationDistance);
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

        minFireRange = Mathf.Max(0f, minFireRange);
        maxFireRange = Mathf.Max(minFireRange + 0.1f, maxFireRange);
        projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
        fireCooldown = Mathf.Max(0.05f, fireCooldown);
        burstCount = Mathf.Max(1, burstCount);
        burstInterval = Mathf.Max(0.01f, burstInterval);
        spreadAngle = Mathf.Clamp(spreadAngle, 0f, 45f);

        keepDistanceDetect = Mathf.Max(0.1f, keepDistanceDetect);
        keepDistancePreferred = Mathf.Max(0.1f, keepDistancePreferred);
        keepDistanceSpeed = Mathf.Max(0.01f, keepDistanceSpeed);
        strafeEngageRange = Mathf.Max(0.1f, strafeEngageRange);
        strafeRadius = Mathf.Max(0.1f, strafeRadius);
        alertRadius = Mathf.Max(0f, alertRadius);
        despawnDelay = Mathf.Max(0f, despawnDelay);
    }
}
