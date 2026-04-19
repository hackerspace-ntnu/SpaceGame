using UnityEngine;

/// <summary>
/// Autonomous rover exploration behavior using roomba-style random wandering.
/// Handles raycasting, waypoint generation, obstacle avoidance, and backup behavior.
/// </summary>
public class AutonomousExplorer : MonoBehaviour
{
    [Header("Exploration")]
    [SerializeField] private Transform thingMount; // End of appendage where raycasts originate
    [SerializeField] private float raycastLength = 5f;
    [SerializeField] private LayerMask raycastMask = ~0;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float directionChangeInterval = 5f; // Change direction every N seconds
    [SerializeField] private float directionCommitTime = 1f; // Stick to direction for N seconds
    [SerializeField] private float backupTime = 2f; // Backup for N seconds after hitting obstacle
    [SerializeField] private float explorationRadius = 20f; // Max distance for random waypoints
    [SerializeField] private float waypointStoppingDistance = 1f; // How close to waypoint before picking new one

    [Header("Debug")]
    [SerializeField] private bool drawDebug = true;

    private float nextDirectionChangeTime;
    private float directionCommitUntilTime;
    private float backupUntilTime;
    private Vector3 currentMoveDirection = Vector3.forward;
    private Vector3 currentWaypoint;
    private bool canMoveForward = true;
    private bool hasGroundBelow = true;
    private Vector3 currentMovementDirection = Vector3.zero;
    private bool isInitialized;

    private void Start()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (isInitialized)
            return;

        if (thingMount == null)
        {
            // Try to find ThingMount in children
            Transform found = transform.Find("**/ThingMount");
            if (found == null)
            {
                // Fallback: search all children
                foreach (Transform child in GetComponentsInChildren<Transform>())
                {
                    if (child.name.Contains("ThingMount"))
                    {
                        thingMount = child;
                        break;
                    }
                }
            }
            else
            {
                thingMount = found;
            }

            if (thingMount == null)
            {
                Debug.LogWarning($"{name}: ThingMount not found! Exploration disabled.", this);
                return;
            }
        }

        nextDirectionChangeTime = Time.time + directionChangeInterval;
        directionCommitUntilTime = Time.time + directionCommitTime;
        backupUntilTime = 0;

        // Pick initial waypoint
        GenerateRandomWaypoint();
        isInitialized = true;
    }

    private void Update()
    {
        if (!isInitialized)
            return;

        UpdateExploration();

        if (drawDebug)
        {
            DrawDebugInfo();
        }
    }

    private void UpdateExploration()
    {
        if (thingMount == null)
        {
            return;
        }

        // Always check terrain
        CheckTerrainAhead();

        // Handle backup phase after hitting obstacle
        if (Time.time < backupUntilTime)
        {
            // Backup in opposite direction
            currentMovementDirection = -currentMoveDirection * moveSpeed;
            return;
        }

        // If we've committed to a direction, try to reach the waypoint
        if (Time.time < directionCommitUntilTime)
        {
            // Try to move forward only if both checks pass
            if (canMoveForward && hasGroundBelow)
            {
                // Calculate direction to waypoint
                Vector3 directionToWaypoint = (currentWaypoint - transform.position).normalized;
                currentMovementDirection = directionToWaypoint * moveSpeed;
            }
            else
            {
                // Hit obstacle or lost ground during commitment - initiate backup immediately
                backupUntilTime = Time.time + backupTime;
                GenerateRandomWaypoint();
                directionCommitUntilTime = Time.time + directionCommitTime + backupTime;
                nextDirectionChangeTime = Time.time + directionChangeInterval;
                currentMovementDirection = Vector3.zero;
            }
        }
        else
        {
            // Commitment period ended - check if we should change direction
            if (!canMoveForward || !hasGroundBelow)
            {
                // Obstacle detected - backup for 2 seconds then pick new direction
                backupUntilTime = Time.time + backupTime;
                GenerateRandomWaypoint();
                directionCommitUntilTime = Time.time + directionCommitTime + backupTime;
                nextDirectionChangeTime = Time.time + directionChangeInterval;
                currentMovementDirection = Vector3.zero;
            }
            else
            {
                // Check if we're close to waypoint
                float distanceToWaypoint = Vector3.Distance(transform.position, currentWaypoint);
                if (distanceToWaypoint < waypointStoppingDistance)
                {
                    // Reached waypoint - generate new one
                    GenerateRandomWaypoint();
                    directionCommitUntilTime = Time.time + directionCommitTime;
                }

                // Keep moving to waypoint
                Vector3 directionToWaypoint = (currentWaypoint - transform.position).normalized;
                currentMovementDirection = directionToWaypoint * moveSpeed;
            }

            // Also allow timed direction changes
            if (Time.time >= nextDirectionChangeTime)
            {
                GenerateRandomWaypoint();
                directionCommitUntilTime = Time.time + directionCommitTime;
                nextDirectionChangeTime = Time.time + directionChangeInterval;
            }
        }
    }

    private void CheckTerrainAhead()
    {
        // Forward raycast: check for obstacles
        Vector3 rayOrigin = thingMount.position;
        Vector3 rayDir = thingMount.forward;
        canMoveForward = !Physics.Raycast(rayOrigin, rayDir, raycastLength, raycastMask, QueryTriggerInteraction.Ignore);

        // Downward raycast: check for ground
        rayDir = Vector3.down;
        hasGroundBelow = Physics.Raycast(rayOrigin, rayDir, raycastLength, raycastMask, QueryTriggerInteraction.Ignore);

        if (drawDebug)
        {
            // Color for forward ray
            Color forwardColor = canMoveForward ? Color.green : Color.red;
            Debug.DrawRay(rayOrigin, thingMount.forward * raycastLength, forwardColor);

            // Color for downward ray
            Color downColor = hasGroundBelow ? Color.green : Color.red;
            Debug.DrawRay(rayOrigin, Vector3.down * raycastLength, downColor);
        }
    }

    private void GenerateRandomWaypoint()
    {
        // Pick a random point on the navmesh within exploration radius
        Vector3 randomDirection = Random.insideUnitSphere * explorationRadius;
        randomDirection += transform.position;

        if (UnityEngine.AI.NavMesh.SamplePosition(randomDirection, out UnityEngine.AI.NavMeshHit hit, explorationRadius, UnityEngine.AI.NavMesh.AllAreas))
        {
            currentWaypoint = hit.position;
            currentMoveDirection = (currentWaypoint - transform.position).normalized;
        }
        else
        {
            // Fallback: if no navmesh point found, pick random direction
            float randomAngle = Random.Range(0f, 360f);
            currentMoveDirection = Quaternion.Euler(0, randomAngle, 0) * Vector3.forward;
            currentWaypoint = transform.position + currentMoveDirection * explorationRadius;
        }
    }

    private void DrawDebugInfo()
    {
        if (thingMount != null)
        {
            Debug.DrawLine(transform.position, thingMount.position, Color.cyan);
            Debug.DrawLine(thingMount.position, thingMount.position + currentMoveDirection * moveSpeed, Color.yellow);
        }
    }

    /// <summary>
    /// Get the current movement direction for this frame.
    /// </summary>
    public Vector3 GetMovementDirection()
    {
        return currentMovementDirection;
    }
}
