using UnityEngine;

/// <summary>
/// Main controller for the rover.
/// Coordinates all bogie IK components and handles overall movement.
/// </summary>
public class RoverController : MonoBehaviour
{
    [Header("Bogie References")]
    [SerializeField] private RoverBogieIK[] bogies;
    [SerializeField] private bool autoCollectBogiesFromChildren = false;

    [Header("Movement")]
    [SerializeField] private RoverMovementController movementController;
    [SerializeField] private bool useWaypoints = true;
    [SerializeField] private Transform[] waypoints;
    [SerializeField] private bool autoCollectWaypointsFromChildren = false;
    [SerializeField] private bool loopWaypoints = true;
    [SerializeField] private float waypointReachDistance = 2f;
    [SerializeField] private float waypointWaitTime = 0f;

    [Header("Ground Follow")]
    [SerializeField] private bool followGroundHeight = true;
    [SerializeField] private float bodyHeightOffset = 0.35f;
    [SerializeField] private float bodyHeightLerpSpeed = 8f;
    [SerializeField] private float minGroundContactCount = 1f;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = true;

    private float nextWaypointTime;
    private Vector3 currentWaypoint;
    private int currentWaypointIndex;
    private float currentHeightVelocity;

    private void Awake()
    {
        if (autoCollectBogiesFromChildren)
        {
            CollectBogiesFromChildren();
        }

        if (autoCollectWaypointsFromChildren)
        {
            CollectWaypointsFromChildren();
        }

        if (movementController == null)
        {
            movementController = GetComponent<RoverMovementController>();
        }

        if (movementController == null)
        {
            movementController = gameObject.AddComponent<RoverMovementController>();
        }

        nextWaypointTime = Time.time;
    }

    private void OnValidate()
    {
        if (autoCollectBogiesFromChildren)
        {
            CollectBogiesFromChildren();
        }

        if (autoCollectWaypointsFromChildren)
        {
            CollectWaypointsFromChildren();
        }
    }

    private void Update()
    {
        UpdateBogieIK();
        UpdateMovement();
        UpdateGroundHeight();

        if (drawDebug)
        {
            DrawDebugInfo();
        }
    }

    private void UpdateBogieIK()
    {
        if (bogies == null)
        {
            return;
        }

        for (int i = 0; i < bogies.Length; i++)
        {
            bogies[i]?.UpdateBogieIK();
        }
    }

    private void CollectBogiesFromChildren()
    {
        bogies = GetComponentsInChildren<RoverBogieIK>(true);
    }

    private void UpdateMovement()
    {
        if (!useWaypoints || movementController == null)
        {
            return;
        }

        if (!HasWaypoints())
        {
            movementController.SetTargetDirection(Vector3.zero);
            movementController.UpdateMovement(transform);
            return;
        }

        if (currentWaypoint == Vector3.zero)
        {
            AdvanceWaypoint(true);
        }

        if (Time.time >= nextWaypointTime)
        {
            float distanceToWaypoint = Vector3.Distance(transform.position, currentWaypoint);
            if (distanceToWaypoint <= waypointReachDistance)
            {
                AdvanceWaypoint(false);
            }
        }

        Vector3 directionToWaypoint = currentWaypoint - transform.position;
        directionToWaypoint.y = 0f;

        if (directionToWaypoint.sqrMagnitude > 0.01f)
        {
            movementController.SetTargetDirection(directionToWaypoint);
        }
        else
        {
            movementController.SetTargetDirection(Vector3.zero);
        }

        movementController.UpdateMovement(transform);
    }

    private void UpdateGroundHeight()
    {
        if (!followGroundHeight || bogies == null || bogies.Length == 0)
        {
            return;
        }

        Vector3 sum = Vector3.zero;
        int contactCount = 0;

        for (int i = 0; i < bogies.Length; i++)
        {
            RoverBogieIK bogie = bogies[i];
            if (bogie == null || !bogie.HasGroundContact)
            {
                continue;
            }

            sum += bogie.LastGroundPoint;
            contactCount++;
        }

        if (contactCount < minGroundContactCount)
        {
            return;
        }

        Vector3 averageGroundPoint = sum / contactCount;
        Vector3 targetPosition = transform.position;
        targetPosition.y = averageGroundPoint.y + bodyHeightOffset;

        float smoothedY = Mathf.Lerp(transform.position.y, targetPosition.y, bodyHeightLerpSpeed * Time.deltaTime);
        transform.position = new Vector3(transform.position.x, smoothedY, transform.position.z);
    }

    private void CollectWaypointsFromChildren()
    {
        Transform[] allTransforms = GetComponentsInChildren<Transform>(true);
        waypoints = new Transform[allTransforms.Length];
        int count = 0;

        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform waypoint = allTransforms[i];
            if (waypoint == null || waypoint == transform)
            {
                continue;
            }

            if (waypoint.name.ToLowerInvariant().Contains("waypoint"))
            {
                waypoints[count++] = waypoint;
            }
        }

        if (count < waypoints.Length)
        {
            System.Array.Resize(ref waypoints, count);
        }

        currentWaypointIndex = 0;
    }

    private bool HasWaypoints()
    {
        return waypoints != null && waypoints.Length > 0;
    }

    private void AdvanceWaypoint(bool forceFirst)
    {
        if (!HasWaypoints())
        {
            currentWaypoint = transform.position;
            return;
        }

        if (forceFirst)
        {
            currentWaypointIndex = Mathf.Clamp(currentWaypointIndex, 0, waypoints.Length - 1);
        }
        else
        {
            currentWaypointIndex++;
            if (currentWaypointIndex >= waypoints.Length)
            {
                if (loopWaypoints)
                {
                    currentWaypointIndex = 0;
                }
                else
                {
                    currentWaypointIndex = waypoints.Length - 1;
                }
            }
        }

        currentWaypoint = waypoints[currentWaypointIndex] != null ? waypoints[currentWaypointIndex].position : transform.position;
        nextWaypointTime = Time.time + waypointWaitTime;
    }

    private void DrawDebugInfo()
    {
        if (useWaypoints)
        {
            Debug.DrawLine(transform.position + Vector3.up * 1.5f, currentWaypoint + Vector3.up * 1.5f, Color.blue);
        }

        if (bogies == null)
        {
            return;
        }

        for (int i = 0; i < bogies.Length; i++)
        {
            RoverBogieIK bogie = bogies[i];
            if (bogie == null || bogie.Wheel == null)
            {
                continue;
            }

            Color color = bogie.HasGroundContact ? Color.green : Color.red;
            Debug.DrawLine(bogie.Wheel.position, bogie.LastGroundPoint, color);
        }

        if (followGroundHeight && bogies.Length > 0)
        {
            Vector3 sum = Vector3.zero;
            int contactCount = 0;

            for (int i = 0; i < bogies.Length; i++)
            {
                RoverBogieIK bogie = bogies[i];
                if (bogie == null || !bogie.HasGroundContact)
                {
                    continue;
                }

                sum += bogie.LastGroundPoint;
                contactCount++;
            }

            if (contactCount > 0)
            {
                Vector3 averageGroundPoint = sum / contactCount;
                Vector3 bodyPoint = new Vector3(transform.position.x, averageGroundPoint.y + bodyHeightOffset, transform.position.z);
                Debug.DrawLine(transform.position, bodyPoint, Color.white);
            }
        }
    }

    public void SetWaypoint(Vector3 waypoint)
    {
        currentWaypoint = waypoint;
        nextWaypointTime = Time.time + waypointWaitTime;
    }
}
