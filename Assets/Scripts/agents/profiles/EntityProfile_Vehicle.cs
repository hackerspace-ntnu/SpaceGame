// Profile for player-mountable, NavMesh-driven robotic vehicles
// (Drone Drill, Drone Saw, Drone Truck, etc.).
//
// Generates: Rigidbody (kinematic) + CapsuleCollider + NavMeshAgent + NavMeshAgentMotor
// + AgentController + AgentAnimatorDriver + HealthComponent + HealthReactionModule
// + EntityFaction + WanderModule + MountModule + SteerModule.
//
// Resulting entity wanders the NavMesh on its own and can be mounted by the player
// (interact to mount, WASD to steer, jump = leap).
using UnityEngine;

public class EntityProfile_Vehicle : MonoBehaviour
{
    [TextArea(2, 4)]
    public string description = "Player-mountable NavMesh-driven vehicle drone.";

    [Header("Health")]
    public int maxHealth = 200;
    public float despawnDelay = 30f;

    [Header("NavMeshAgent")]
    public float agentSpeed = 6f;
    public float agentAngularSpeed = 180f;
    public float agentAcceleration = 12f;
    public float agentRadius = 1.2f;
    public float agentHeight = 2.5f;
    public float stoppingDistance = 0.3f;

    [Header("Collider (capsule)")]
    public float colliderRadius = 1.2f;
    public float colliderHeight = 2.5f;
    public Vector3 colliderCenter = new Vector3(0f, 1.25f, 0f);

    [Header("Wander AI")]
    public bool wanderEnabled = true;
    public float wanderRadius = 25f;
    public float wanderMinWait = 1.5f;
    public float wanderMaxWait = 4f;
    public float wanderSpeedMultiplier = 1f;

    [Header("Mount")]
    [Tooltip("Optional seat point. If null the prefab root is used.")]
    public Transform seatPoint;
    [Tooltip("If true the vehicle keeps wandering between rider inputs. If false it stands still while ridden.")]
    public bool allowAISelfMovementWhenMounted = false;

    [Header("Steering")]
    public bool jumpEnabled = false;
    public bool leapEnabled = false;
    public bool riderCanRun = true;
}
