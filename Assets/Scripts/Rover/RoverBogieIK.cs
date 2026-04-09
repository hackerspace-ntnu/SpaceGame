using UnityEngine;

/// <summary>
/// Bogie suspension IK for a two-joint rover arm:
/// - Main joint drives one wheel mount.
/// - Secondary joint drives a smaller rig with two wheel mounts.
///
/// The solver raycasts down from each wheel mount and rotates joints so wheels try to stay on ground.
/// </summary>
public class RoverBogieIK : MonoBehaviour
{
    [Header("Auto Setup")]
    [SerializeField] private Transform searchRoot;
    [SerializeField] private bool autoFindOnValidate = false;

    [Header("Bogie Joints")]
    [SerializeField] private Transform mainJoint;
    [SerializeField] private Transform secondaryJoint;

    [Header("Wheel Mounts")]
    [SerializeField] private Transform mainWheelMount;
    [SerializeField] private Transform secondaryWheelMountA;
    [SerializeField] private Transform secondaryWheelMountB;

    [Header("Joint Axis (Local)")]
    [SerializeField] private Vector3 mainJointAxisLocal = Vector3.right;
    [SerializeField] private Vector3 secondaryJointAxisLocal = Vector3.right;

    [Header("Joint Limits")]
    [SerializeField] private float mainMinAngle = -55f;
    [SerializeField] private float mainMaxAngle = 55f;
    [SerializeField] private float secondaryMinAngle = -65f;
    [SerializeField] private float secondaryMaxAngle = 65f;

    [Header("Joint Motion")]
    [SerializeField] private float mainJointDegreesPerSecond = 140f;
    [SerializeField] private float secondaryJointDegreesPerSecond = 160f;

    [Header("Raycast Grounding")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float rayStartHeight = 0.2f;
    [SerializeField] private float raycastDistance = 3.5f;
    [SerializeField] private float wheelGroundOffset = 0.03f;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = true;

    private Quaternion mainBaseLocalRotation;
    private Quaternion secondaryBaseLocalRotation;

    private float currentMainAngle;
    private float currentSecondaryAngle;

    private bool hasGroundContact;
    private Vector3 lastGroundPoint;
    private Vector3 lastGroundNormal = Vector3.up;
    private bool warnedMissingReferences;

    public bool HasGroundContact => hasGroundContact;
    public Vector3 LastGroundPoint => lastGroundPoint;
    public Vector3 LastGroundNormal => lastGroundNormal;
    public Transform Wheel => mainWheelMount;

    private void Awake()
    {
        if (searchRoot == null)
        {
            searchRoot = transform;
        }

        CacheBindPose();
    }

    private void OnEnable()
    {
        CacheBindPose();
    }

    private void OnValidate()
    {
        if (searchRoot == null)
        {
            searchRoot = transform;
        }

        if (autoFindOnValidate)
        {
            AutoSetupFromChildren();
        }

        CacheBindPose();
    }

    [ContextMenu("Auto Setup From Children")]
    public void AutoSetupFromChildren()
    {
        Transform root = searchRoot != null ? searchRoot : transform;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);

        AutoAssignJoints(all);
        AutoAssignWheelMounts(all);

        CacheBindPose();
    }

    public void UpdateBogieIK()
    {
        if (!HasRequiredReferences())
        {
            return;
        }

        WheelContact mainContact = SampleWheelGround(mainWheelMount);
        WheelContact secondaryAContact = SampleWheelGround(secondaryWheelMountA);
        WheelContact secondaryBContact = SampleWheelGround(secondaryWheelMountB);

        hasGroundContact = mainContact.HasHit || secondaryAContact.HasHit || secondaryBContact.HasHit;
        UpdateDebugGroundState(mainContact, secondaryAContact, secondaryBContact);

        float desiredMainAngle = ComputeDesiredMainAngle(mainContact);
        float desiredSecondaryAngle = ComputeDesiredSecondaryAngle(secondaryAContact, secondaryBContact);

        desiredMainAngle = Mathf.Clamp(desiredMainAngle, mainMinAngle, mainMaxAngle);
        desiredSecondaryAngle = Mathf.Clamp(desiredSecondaryAngle, secondaryMinAngle, secondaryMaxAngle);

        currentMainAngle = Mathf.MoveTowards(currentMainAngle, desiredMainAngle, mainJointDegreesPerSecond * Time.deltaTime);
        currentSecondaryAngle = Mathf.MoveTowards(currentSecondaryAngle, desiredSecondaryAngle, secondaryJointDegreesPerSecond * Time.deltaTime);

        mainJoint.localRotation = mainBaseLocalRotation * Quaternion.AngleAxis(currentMainAngle, mainJointAxisLocal.normalized);
        secondaryJoint.localRotation = secondaryBaseLocalRotation * Quaternion.AngleAxis(currentSecondaryAngle, secondaryJointAxisLocal.normalized);

        if (drawDebug)
        {
            DrawDebug(mainContact, secondaryAContact, secondaryBContact);
        }
    }

    private void CacheBindPose()
    {
        if (mainJoint != null)
        {
            mainBaseLocalRotation = mainJoint.localRotation;
        }

        if (secondaryJoint != null)
        {
            secondaryBaseLocalRotation = secondaryJoint.localRotation;
        }

        currentMainAngle = 0f;
        currentSecondaryAngle = 0f;
    }

    private bool HasRequiredReferences()
    {
        bool valid = mainJoint != null &&
                     secondaryJoint != null &&
                     mainWheelMount != null &&
                     secondaryWheelMountA != null &&
                     secondaryWheelMountB != null;

        if (!valid && !warnedMissingReferences)
        {
            Debug.LogWarning($"{name}: Missing RoverBogieIK references. Assign joints and all three wheel mounts.", this);
            warnedMissingReferences = true;
        }

        if (valid)
        {
            warnedMissingReferences = false;
        }

        return valid;
    }

    private void AutoAssignJoints(Transform[] all)
    {
        Transform firstJoint = null;
        Transform secondJoint = null;
        int firstDepth = int.MaxValue;
        int secondDepth = int.MaxValue;

        for (int i = 0; i < all.Length; i++)
        {
            Transform candidate = all[i];
            if (candidate == null)
            {
                continue;
            }

            string lowerName = candidate.name.ToLowerInvariant();
            if (!lowerName.Contains("joint"))
            {
                continue;
            }

            int depth = GetHierarchyDepth(candidate, searchRoot != null ? searchRoot : transform);

            if (depth < firstDepth)
            {
                secondJoint = firstJoint;
                secondDepth = firstDepth;
                firstJoint = candidate;
                firstDepth = depth;
            }
            else if (depth < secondDepth)
            {
                secondJoint = candidate;
                secondDepth = depth;
            }
        }

        if (firstJoint != null)
        {
            mainJoint = firstJoint;
        }

        if (secondJoint != null)
        {
            secondaryJoint = secondJoint;
        }
    }

    private void AutoAssignWheelMounts(Transform[] all)
    {
        Transform[] wheelCandidates = new Transform[all.Length];
        int candidateCount = 0;

        for (int i = 0; i < all.Length; i++)
        {
            Transform candidate = all[i];
            if (candidate == null || candidate == mainJoint || candidate == secondaryJoint)
            {
                continue;
            }

            if (!LooksLikeWheelMount(candidate))
            {
                continue;
            }

            wheelCandidates[candidateCount++] = candidate;
        }

        if (candidateCount < 3)
        {
            return;
        }

        Transform backCandidate = null;
        Transform frontCandidate = null;
        Transform otherCandidate = null;

        for (int i = 0; i < candidateCount; i++)
        {
            Transform candidate = wheelCandidates[i];
            string lower = candidate.name.ToLowerInvariant();

            if (frontCandidate == null && lower.Contains("front"))
            {
                frontCandidate = candidate;
                continue;
            }

            if (backCandidate == null && lower.Contains("back"))
            {
                backCandidate = candidate;
                continue;
            }

            if (otherCandidate == null)
            {
                otherCandidate = candidate;
            }
        }

        if (frontCandidate != null && backCandidate != null && otherCandidate != null)
        {
            mainWheelMount = otherCandidate;
            secondaryWheelMountA = frontCandidate;
            secondaryWheelMountB = backCandidate;
            return;
        }

        mainWheelMount = wheelCandidates[0];
        secondaryWheelMountA = wheelCandidates[1];
        secondaryWheelMountB = wheelCandidates[2];
    }

    private static bool LooksLikeWheelMount(Transform candidate)
    {
        string lower = candidate.name.ToLowerInvariant();
        if (lower.Contains("wheel") || lower.Contains("bogie") || lower.Contains("mount"))
        {
            return HasWheelChild(candidate);
        }

        return HasWheelChild(candidate);
    }

    private static bool HasWheelChild(Transform candidate)
    {
        for (int i = 0; i < candidate.childCount; i++)
        {
            Transform child = candidate.GetChild(i);
            if (child.name.ToLowerInvariant().Contains("wheel"))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetHierarchyDepth(Transform target, Transform root)
    {
        int depth = 0;
        Transform current = target;

        while (current != null && current != root)
        {
            current = current.parent;
            depth++;
        }

        return depth;
    }

    private WheelContact SampleWheelGround(Transform wheelMount)
    {
        Vector3 origin = wheelMount.position + Vector3.up * rayStartHeight;
        bool hitFound = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastDistance, groundMask, QueryTriggerInteraction.Ignore);

        if (hitFound)
        {
            return new WheelContact(
                true,
                wheelMount,
                origin,
                hit.point,
                hit.normal,
                hit.point + hit.normal * wheelGroundOffset
            );
        }

        Vector3 fallbackPoint = origin + Vector3.down * raycastDistance;
        return new WheelContact(false, wheelMount, origin, fallbackPoint, Vector3.up, fallbackPoint + Vector3.up * wheelGroundOffset);
    }

    private float ComputeDesiredMainAngle(WheelContact mainContact)
    {
        float delta = ComputeWheelDeltaAngle(mainJoint, mainJointAxisLocal, mainWheelMount, mainContact.TargetPoint);
        return currentMainAngle + delta;
    }

    private float ComputeDesiredSecondaryAngle(WheelContact contactA, WheelContact contactB)
    {
        float deltaA = ComputeWheelDeltaAngle(secondaryJoint, secondaryJointAxisLocal, secondaryWheelMountA, contactA.TargetPoint);
        float deltaB = ComputeWheelDeltaAngle(secondaryJoint, secondaryJointAxisLocal, secondaryWheelMountB, contactB.TargetPoint);

        float averageDelta = (deltaA + deltaB) * 0.5f;
        return currentSecondaryAngle + averageDelta;
    }

    private float ComputeWheelDeltaAngle(Transform joint, Vector3 localAxis, Transform wheelMount, Vector3 targetPoint)
    {
        Vector3 worldAxis = joint.TransformDirection(localAxis.normalized);
        Vector3 currentVector = wheelMount.position - joint.position;
        Vector3 targetVector = targetPoint - joint.position;

        Vector3 currentProjected = Vector3.ProjectOnPlane(currentVector, worldAxis);
        Vector3 targetProjected = Vector3.ProjectOnPlane(targetVector, worldAxis);

        if (currentProjected.sqrMagnitude < 0.000001f || targetProjected.sqrMagnitude < 0.000001f)
        {
            return 0f;
        }

        return Vector3.SignedAngle(currentProjected, targetProjected, worldAxis);
    }

    private void UpdateDebugGroundState(WheelContact mainContact, WheelContact contactA, WheelContact contactB)
    {
        int hitCount = 0;
        Vector3 pointSum = Vector3.zero;
        Vector3 normalSum = Vector3.zero;

        if (mainContact.HasHit)
        {
            hitCount++;
            pointSum += mainContact.HitPoint;
            normalSum += mainContact.HitNormal;
        }

        if (contactA.HasHit)
        {
            hitCount++;
            pointSum += contactA.HitPoint;
            normalSum += contactA.HitNormal;
        }

        if (contactB.HasHit)
        {
            hitCount++;
            pointSum += contactB.HitPoint;
            normalSum += contactB.HitNormal;
        }

        if (hitCount > 0)
        {
            lastGroundPoint = pointSum / hitCount;
            lastGroundNormal = normalSum.normalized;
            return;
        }

        lastGroundPoint = (mainContact.TargetPoint + contactA.TargetPoint + contactB.TargetPoint) / 3f;
        lastGroundNormal = Vector3.up;
    }

    private void DrawDebug(WheelContact mainContact, WheelContact contactA, WheelContact contactB)
    {
        DrawContact(mainContact, Color.cyan);
        DrawContact(contactA, new Color(1f, 0.6f, 0.2f));
        DrawContact(contactB, new Color(1f, 0.6f, 0.2f));

        if (mainJoint != null && mainWheelMount != null)
        {
            Debug.DrawLine(mainJoint.position, mainWheelMount.position, Color.yellow);
        }

        if (secondaryJoint != null && secondaryWheelMountA != null)
        {
            Debug.DrawLine(secondaryJoint.position, secondaryWheelMountA.position, Color.magenta);
        }

        if (secondaryJoint != null && secondaryWheelMountB != null)
        {
            Debug.DrawLine(secondaryJoint.position, secondaryWheelMountB.position, Color.magenta);
        }
    }

    private void DrawContact(WheelContact contact, Color armColor)
    {
        Color rayColor = contact.HasHit ? Color.green : Color.red;
        Debug.DrawLine(contact.RaycastOrigin, contact.RaycastOrigin + Vector3.down * raycastDistance, rayColor);
        Debug.DrawLine(contact.WheelMount.position, contact.TargetPoint, armColor);
    }

    private readonly struct WheelContact
    {
        public readonly bool HasHit;
        public readonly Transform WheelMount;
        public readonly Vector3 RaycastOrigin;
        public readonly Vector3 HitPoint;
        public readonly Vector3 HitNormal;
        public readonly Vector3 TargetPoint;

        public WheelContact(bool hasHit, Transform wheelMount, Vector3 raycastOrigin, Vector3 hitPoint, Vector3 hitNormal, Vector3 targetPoint)
        {
            HasHit = hasHit;
            WheelMount = wheelMount;
            RaycastOrigin = raycastOrigin;
            HitPoint = hitPoint;
            HitNormal = hitNormal;
            TargetPoint = targetPoint;
        }
    }
}
