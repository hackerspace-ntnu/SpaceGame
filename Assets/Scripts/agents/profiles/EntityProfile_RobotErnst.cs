// Data-only profile for Ernst — the band's slow, tanky heavy.
// Relentless once he has a target. High damage, long despawn.
using UnityEngine;

public class EntityProfile_RobotErnst : MonoBehaviour
{
    [Header("Chase")]
    public float detectRange = 10f;
    public float loseTargetRange = 30f;
    public float attackRange = 2.5f;
    public float chaseSpeed = 0.85f;

    [Header("Combat")]
    public float meleeDamageRange = 2.8f;
    public float meleeCooldown = 1.8f;
    public int meleeDamage = 30;

    [Header("Flocking")]
    public float separationRadius = 3f;
    public float perceptionRadius = 10f;

    [Header("Wander")]
    public float wanderRadius = 20f;
    public float wanderMinWait = 2f;
    public float wanderMaxWait = 6f;

    [Header("Health")]
    public float despawnDelay = 20f;
}
