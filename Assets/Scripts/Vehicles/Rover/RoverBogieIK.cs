using UnityEngine;

/// <summary>
/// Bogie suspension using physics joints.
/// Wheels are connected to the bogie via ConfigurableJoints that:
/// - Lock XYZ position (wheel stays attached)
/// - Allow X-axis rotation (wheel can tilt with terrain)
/// - Apply spring forces to keep wheels on ground
/// </summary>
public class RoverBogieIK : MonoBehaviour
{
    [Header("Auto Setup")]
    [SerializeField] private Transform searchRoot;
    [SerializeField] private bool autoFindOnValidate = false;

    [Header("Bogie Structure")]
    [SerializeField] private Transform mainJoint;
    [SerializeField] private Transform secondaryJoint;

    [Header("Wheel Mounts")]
    [SerializeField] private Transform mainWheelMount;
    [SerializeField] private Transform secondaryWheelMountA;
    [SerializeField] private Transform secondaryWheelMountB;

    [Header("Joint Configuration")]
    [SerializeField] private Vector3 jointRotationAxis = Vector3.right; // X-axis rotation
    [SerializeField] private float jointSpring = 1000f; // Spring force to return to neutral
    [SerializeField] private float jointDamper = 100f; // Damping to prevent oscillation
    [SerializeField] private float jointMaxForce = 1000f; // Max force the joint can apply

    [Header("Joint Angle Limits")]
    [SerializeField] private float jointMinAngle = -60f;
    [SerializeField] private float jointMaxAngle = 60f;

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

        InitializePhysics();
    }

    private void OnEnable()
    {
        InitializePhysics();
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
    }

    [ContextMenu("Auto Setup From Children")]
    public void AutoSetupFromChildren()
    {
        Transform root = searchRoot != null ? searchRoot : transform;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);

        AutoAssignJoints(all);
        AutoAssignWheelMounts(all);
    }

    [ContextMenu("Initialize Physics Joints")]
    public void InitializePhysics()
    {
        if (!HasRequiredReferences())
        {
            return;
        }

        // Get or create bogie rigidbody
        Rigidbody bogieRigidbody = GetComponent<Rigidbody>();
        if (bogieRigidbody == null)
        {
            bogieRigidbody = gameObject.AddComponent<Rigidbody>();
            bogieRigidbody.isKinematic = false;
            bogieRigidbody.useGravity = true;
        }

        // Create/update wheel rigidbodies and joints
        // Wheels connect to their parent joint, not the bogie
        SetupWheelPhysics(mainWheelMount);
        SetupWheelPhysics(secondaryWheelMountA);
        SetupWheelPhysics(secondaryWheelMountB);
    }

    private void SetupWheelPhysics(Transform wheelMount)
    {
        if (wheelMount == null)
        {
            return;
        }

        // Get the wheel's parent (should be the joint)
        Transform parentJoint = wheelMount.parent;
        if (parentJoint == null)
        {
            Debug.LogError($"{wheelMount.name} has no parent! Wheel must be child of a joint.", this);
            return;
        }

        Rigidbody parentRb = parentJoint.GetComponent<Rigidbody>();
        if (parentRb == null)
        {
            Debug.LogError($"{parentJoint.name} (parent of {wheelMount.name}) has no Rigidbody!", this);
            return;
        }

        // Ensure wheel has rigidbody
        Rigidbody wheelRb = wheelMount.GetComponent<Rigidbody>();
        if (wheelRb == null)
        {
            wheelRb = wheelMount.gameObject.AddComponent<Rigidbody>();
            wheelRb.isKinematic = false;
            wheelRb.useGravity = true;
            wheelRb.mass = 0.5f; // Light wheel
            Debug.Log($"Created Rigidbody on {wheelMount.name}");
        }

        // Make sure wheel rigidbody is NOT kinematic
        if (wheelRb.isKinematic)
        {
            wheelRb.isKinematic = false;
            Debug.LogWarning($"{wheelMount.name} was kinematic, setting to dynamic");
        }

        // Remove old joint if exists
        ConfigurableJoint existingJoint = wheelMount.GetComponent<ConfigurableJoint>();
        if (existingJoint != null)
        {
            DestroyImmediate(existingJoint);
        }

        // Create new joint: wheel connected to its parent joint
        ConfigurableJoint joint = wheelMount.gameObject.AddComponent<ConfigurableJoint>();
        joint.connectedBody = parentRb;

        // Set anchor to local position (relative to this wheel)
        joint.anchor = Vector3.zero;
        // Connected anchor on the parent joint
        joint.connectedAnchor = parentRb.transform.InverseTransformPoint(wheelMount.position);

        // Lock XYZ position (wheel stays attached to joint)
        joint.xMotion = ConfigurableJointMotion.Locked;
        joint.yMotion = ConfigurableJointMotion.Locked;
        joint.zMotion = ConfigurableJointMotion.Locked;

        // Allow rotation on X axis only (tilt with terrain)
        joint.angularXMotion = ConfigurableJointMotion.Limited;
        joint.angularYMotion = ConfigurableJointMotion.Locked;
        joint.angularZMotion = ConfigurableJointMotion.Locked;

        // Set angle limits on X axis
        SoftJointLimit limitMax = new SoftJointLimit();
        limitMax.limit = jointMaxAngle;
        limitMax.bounciness = 0f;
        joint.highAngularXLimit = limitMax;

        SoftJointLimit limitMin = new SoftJointLimit();
        limitMin.limit = jointMinAngle;
        limitMin.bounciness = 0f;
        joint.lowAngularXLimit = limitMin;

        // Spring and damper for smooth motion
        JointDrive drive = new JointDrive();
        drive.positionSpring = 0;
        drive.positionDamper = 0;
        drive.maximumForce = jointMaxForce;
        joint.xDrive = drive;
        joint.yDrive = drive;
        joint.zDrive = drive;

        // Rotation drive for X axis
        drive.positionSpring = jointSpring;
        drive.positionDamper = jointDamper;
        drive.maximumForce = jointMaxForce;
        joint.angularXDrive = drive;

        Debug.Log($"Created ConfigurableJoint on {wheelMount.name} connected to parent {parentJoint.name}");
    }

    public void UpdateBogieIK()
    {
        if (!HasRequiredReferences())
        {
            return;
        }

        // Sample ground contact for all wheels
        WheelContact mainContact = SampleWheelGround(mainWheelMount);
        WheelContact secondaryAContact = SampleWheelGround(secondaryWheelMountA);
        WheelContact secondaryBContact = SampleWheelGround(secondaryWheelMountB);

        hasGroundContact = mainContact.HasHit || secondaryAContact.HasHit || secondaryBContact.HasHit;
        UpdateDebugGroundState(mainContact, secondaryAContact, secondaryBContact);

        if (drawDebug)
        {
            DrawDebug(mainContact, secondaryAContact, secondaryBContact);
        }
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
        // Joints are no longer used in physics-based approach
        // Kept for compatibility
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
            return true;
        }

        return false;
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
                hit.point + hit.normal * wheelGroundOffset,
                hit.normal
            );
        }

        Vector3 fallbackPoint = origin + Vector3.down * raycastDistance;
        return new WheelContact(false, wheelMount, origin, fallbackPoint, Vector3.up);
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

        lastGroundPoint = (mainContact.RaycastOrigin + contactA.RaycastOrigin + contactB.RaycastOrigin) / 3f;
        lastGroundNormal = Vector3.up;
    }

    private void DrawDebug(WheelContact mainContact, WheelContact contactA, WheelContact contactB)
    {
        DrawContact(mainContact, Color.cyan);
        DrawContact(contactA, new Color(1f, 0.6f, 0.2f));
        DrawContact(contactB, new Color(1f, 0.6f, 0.2f));

        // Draw connections to wheel mounts
        if (mainWheelMount != null)
        {
            Debug.DrawLine(transform.position, mainWheelMount.position, Color.yellow);
        }

        if (secondaryWheelMountA != null)
        {
            Debug.DrawLine(transform.position, secondaryWheelMountA.position, Color.magenta);
        }

        if (secondaryWheelMountB != null)
        {
            Debug.DrawLine(transform.position, secondaryWheelMountB.position, Color.magenta);
        }
    }

    private void DrawContact(WheelContact contact, Color armColor)
    {
        Color rayColor = contact.HasHit ? Color.green : Color.red;
        Debug.DrawLine(contact.RaycastOrigin, contact.RaycastOrigin + Vector3.down * raycastDistance, rayColor);
        Debug.DrawLine(contact.WheelMount.position, contact.HitPoint, armColor);
    }

    private readonly struct WheelContact
    {
        public readonly bool HasHit;
        public readonly Transform WheelMount;
        public readonly Vector3 RaycastOrigin;
        public readonly Vector3 HitPoint;
        public readonly Vector3 HitNormal;

        public WheelContact(bool hasHit, Transform wheelMount, Vector3 raycastOrigin, Vector3 hitPoint, Vector3 hitNormal)
        {
            HasHit = hasHit;
            WheelMount = wheelMount;
            RaycastOrigin = raycastOrigin;
            HitPoint = hitPoint;
            HitNormal = hitNormal;
        }
    }
}
