// Data-only profile for a ranged mercenary / bounty hunter.
// More tactical than a basic robot: keeps distance, kites, bursts fire, seeks cover.
using UnityEngine;

public class EntityProfile_BountyHunter : MonoBehaviour
{
    [Header("Chase")]
    public float detectRange = 16f;
    public float loseTargetRange = 22f;
    public float chaseStopRange = 8f;
    public float chaseSpeed = 1.2f;

    [Header("Ranged Attack")]
    public float minFireRange = 4f;
    public float maxFireRange = 14f;
    public float fireCooldown = 1.4f;
    public int burstCount = 2;
    public float spreadAngle = 3f;

    [Header("Strafe")]
    public float strafeEngageRange = 12f;
    public float strafeRadius = 6f;

    [Header("Keep Distance")]
    public float keepDistanceDetect = 8f;
    public float keepDistancePreferred = 5f;

    [Header("Flocking (hunting party)")]
    public bool enableFlocking = true;
    public float separationRadius = 3f;
    public float flockPerceptionRadius = 10f;

    [Header("Wander")]
    public float wanderRadius = 50f;
    public float wanderMinWait = 1f;
    public float wanderMaxWait = 5f;

    [Header("Health")]
    public float despawnDelay = 12f;
}
