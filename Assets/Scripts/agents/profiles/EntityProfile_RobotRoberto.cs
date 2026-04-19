// Data-only profile for Roberto — the band's scout and spotter.
// Wide detection, keeps distance, alerts the whole group on spot.
using UnityEngine;

public class EntityProfile_RobotRoberto : MonoBehaviour
{
    [Header("Chase")]
    public float detectRange = 18f;
    public float loseTargetRange = 25f;
    public float attackRange = 12f;
    public float chaseSpeed = 1.1f;

    [Header("Keep Distance")]
    public float keepDistanceDetect = 14f;
    public float keepDistancePreferred = 8f;
    public float keepDistanceSpeed = 1.3f;

    [Header("Strafe")]
    public float strafeEngageRange = 15f;
    public float strafeRadius = 8f;

    [Header("Flocking")]
    public float separationRadius = 3f;
    public float perceptionRadius = 12f;

    [Header("Wander")]
    public float wanderRadius = 50f;
    public float wanderMinWait = 1f;
    public float wanderMaxWait = 5f;

    [Header("Health")]
    public float despawnDelay = 10f;
}
