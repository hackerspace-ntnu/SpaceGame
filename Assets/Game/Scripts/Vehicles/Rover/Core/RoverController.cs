using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// Rover movement and exploration controller.
    /// Uses NavMesh for pathfinding waypoints but applies movement through RoverMovementController.
    /// Uses forward/downward raycasts from ThingMount to explore terrain.
    ///
    /// <para>
    /// Nobody drives this. It is not a crewed vehicle like the dune foiler — there is no station to
    /// claim, no seat, no input, and nothing on it a second player could race a first one for. What
    /// it does share with the foiler is that it decides where it goes and then writes its own
    /// transform, and a decision made independently on five machines is five different rovers. Here
    /// the decision is literally random (see <see cref="GenerateRandomWaypoint"/>), so the copies do
    /// not merely drift, they set off in different directions on the first frame.
    /// </para>
    /// <para>
    /// So the exploration is gated on <see cref="Network.Simulates"/> — the one machine that owns
    /// the entity decides, and the transform sync carries the result. That answers true offline,
    /// true on the server, and true for an entity with no NetworkObject, which is what the rover is
    /// today: it is authored only into <c>Assets/Game/Scenes/Tests/Aleksander test scene.unity</c>,
    /// its prefabs carry no NetworkObject and are in no network prefab list, so today this changes
    /// nothing at all and every machine keeps running its own copy exactly as before. It is here so
    /// that the day somebody puts a NetworkObject on the prefab and drops it into the world, the
    /// rover is server-simulated rather than quietly five rovers — which is the failure that costs
    /// an afternoon to find, because the host's screen is always right.
    /// </para>
    /// <para>
    /// Deliberately NOT a NetAuthority job. NetAuthority switches whole driver components off on
    /// remote machines, which is right for a legged creature whose animation is a separate
    /// component; here the exploration and the wheel/bogie presentation would go with it, and a
    /// remote rover would slide across the sand with dead wheels. Gating the decision and leaving
    /// the presentation running is the same split the dune foiler uses.
    /// </para>
    /// </summary>
    public class RoverController : MonoBehaviour
    {
        [Header("Exploration")]
        [SerializeField] private Transform thingMount; // End of appendage where raycasts originate
        [SerializeField] private float raycastLength = 5f;
        [SerializeField] private LayerMask raycastMask = ~0;

        [Header("Movement")]
        [SerializeField] private RoverMovementController movementController;
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
        private Vector3 currentTargetDirection = Vector3.zero; // Track the current target direction being sent to movement controller

        private void Awake()
        {
            if (movementController == null)
            {
                movementController = GetComponent<RoverMovementController>();
            }

            if (movementController == null)
            {
                movementController = gameObject.AddComponent<RoverMovementController>();
            }

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
                }
            }

            nextDirectionChangeTime = Time.time + directionChangeInterval;
            directionCommitUntilTime = Time.time + directionCommitTime;
            backupUntilTime = 0;
        
            // Pick initial waypoint
            GenerateRandomWaypoint();
        }

        private void Start()
        {
            // Nothing special needed - movement controller handles physics
        }

        private void Update()
        {
            UpdateExploration();

            if (drawDebug)
            {
                DrawDebugInfo();
            }
        }

        private void UpdateExploration()
        {
            if (thingMount == null || movementController == null)
            {
                return;
            }

            // Not ours to steer. Simulates, not IsServer: a rover with no NetworkObject — which is
            // every rover in the project today — has no remote truth to defer to, so every machine
            // running its own is the best available answer and refusing would freeze it solid.
            if (!Network.Simulates(this))
            {
                return;
            }

            // Always check terrain
            CheckTerrainAhead();

            // Handle backup phase after hitting obstacle
            if (Time.time < backupUntilTime)
            {
                // Backup in opposite direction
                currentTargetDirection = -currentMoveDirection * moveSpeed;
                movementController.SetTargetDirection(currentTargetDirection);
                movementController.UpdateMovement(transform);
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
                    currentTargetDirection = directionToWaypoint * moveSpeed;
                    movementController.SetTargetDirection(currentTargetDirection);
                }
                else
                {
                    // Hit obstacle or lost ground during commitment - initiate backup immediately
                    backupUntilTime = Time.time + backupTime;
                    GenerateRandomWaypoint();
                    directionCommitUntilTime = Time.time + directionCommitTime + backupTime;
                    nextDirectionChangeTime = Time.time + directionChangeInterval;
                    currentTargetDirection = Vector3.zero;
                    movementController.SetTargetDirection(currentTargetDirection);
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
                    currentTargetDirection = Vector3.zero;
                    movementController.SetTargetDirection(currentTargetDirection);
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
                    currentTargetDirection = directionToWaypoint * moveSpeed;
                    movementController.SetTargetDirection(currentTargetDirection);
                }

                // Also allow timed direction changes
                if (Time.time >= nextDirectionChangeTime)
                {
                    GenerateRandomWaypoint();
                    directionCommitUntilTime = Time.time + directionCommitTime;
                    nextDirectionChangeTime = Time.time + directionChangeInterval;
                }
            }

            movementController.UpdateMovement(transform);
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

        private void ChangeDirection()
        {
            // Deprecated - use GenerateRandomWaypoint instead
            GenerateRandomWaypoint();
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
        /// Get the current target movement direction and speed being applied by the rover.
        /// </summary>
        public Vector3 GetCurrentMovementVelocity()
        {
            return currentTargetDirection;
        }
    }
}
