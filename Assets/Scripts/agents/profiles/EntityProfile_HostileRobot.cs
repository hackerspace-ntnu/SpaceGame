// Data-only profile for a hostile melee robot.
// Add to a prefab, configure values in the Inspector, then click Generate in the Inspector header.
// After generating, this component can be removed — all modules are fully configured.
using UnityEngine;

public class EntityProfile_HostileRobot : MonoBehaviour
{
    [Header("Chase")]
    public float detectRange = 12f;
    public float loseTargetRange = 18f;
    public float attackRange = 1.8f;
    public float chaseSpeed = 1.4f;

    [Header("Strafe")]
    public float strafeEngageRange = 10f;
    public float strafeRadius = 4.5f;

    [Header("Combat")]
    public float meleeDamageRange = 2f;
    public float meleeCooldown = 1.2f;
    public int meleeDamage = 12;

    [Header("Flocking")]
    public float separationRadius = 2.5f;
    public float perceptionRadius = 10f;

    [Header("Wander")]
    public float wanderRadius = 40f;
    public float wanderMinWait = 1f;
    public float wanderMaxWait = 4f;

    [Header("Health")]
    public float despawnDelay = 10f;
}
