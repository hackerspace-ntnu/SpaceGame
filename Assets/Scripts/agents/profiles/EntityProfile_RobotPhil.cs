// Data-only profile for Phil — the band's aggressive charger.
// High damage, fast chase, no strafe.
using UnityEngine;

public class EntityProfile_RobotPhil : MonoBehaviour
{
    [Header("Chase")]
    public float detectRange = 9f;
    public float loseTargetRange = 14f;
    public float attackRange = 2f;
    public float chaseSpeed = 1.7f;

    [Header("Combat")]
    public float meleeDamageRange = 2.2f;
    public float meleeCooldown = 0.85f;
    public int meleeDamage = 18;

    [Header("Flocking")]
    public float separationRadius = 2f;
    public float perceptionRadius = 10f;

    [Header("Wander")]
    public float wanderRadius = 25f;
    public float wanderMinWait = 0.5f;
    public float wanderMaxWait = 3f;

    [Header("Health")]
    public float despawnDelay = 10f;
}
