// Data-only profile for Cath — the band's cover-seeking ranged shooter.
// Finds cover, peeks and fires in 3-round bursts.
using UnityEngine;

public class EntityProfile_RobotCath : MonoBehaviour
{
    [Header("Chase")]
    public float detectRange = 14f;
    public float loseTargetRange = 20f;
    public float attackRange = 10f;
    public float chaseSpeed = 1.1f;

    [Header("Ranged Attack")]
    public float minFireRange = 5f;
    public float maxFireRange = 12f;
    public float fireCooldown = 2f;
    public int burstCount = 3;
    public float spreadAngle = 4f;

    [Header("Strafe")]
    public float strafeEngageRange = 12f;
    public float strafeRadius = 5f;

    [Header("Flocking")]
    public float separationRadius = 2.5f;
    public float perceptionRadius = 10f;

    [Header("Wander")]
    public float wanderRadius = 35f;
    public float wanderMinWait = 1.5f;
    public float wanderMaxWait = 6f;

    [Header("Health")]
    public float despawnDelay = 10f;
}
