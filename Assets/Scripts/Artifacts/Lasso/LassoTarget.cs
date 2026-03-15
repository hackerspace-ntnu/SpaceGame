using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class LassoTarget : NetworkBehaviour
{
    [Header("Attachment")]
    [SerializeField] private Transform attachmentPoint;

    [Header("Physics Pull")]
    [SerializeField] private Rigidbody targetRigidbody;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private float pullAcceleration = 12f;
    [SerializeField] private float reelBonusAcceleration = 18f;
    [SerializeField] private float verticalAssist = 1.5f;
    [SerializeField] private float maxAcceleration = 36f;

    [Header("AI Lead")]
    [SerializeField] private float aiLeadSlackMultiplier = 0.9f;
    [SerializeField] private float aiLeadSpeedMultiplier = 1.2f;

    private LassoItem activeSource;
    private Transform activeAnchor;
    private float desiredDistance;

    public bool IsAttached => activeSource != null && activeAnchor != null;

    public Vector3 AttachmentPosition =>
        attachmentPoint != null ? attachmentPoint.position : transform.position + Vector3.up;

    private void Awake()
    {
        if (attachmentPoint == null)
        {
            attachmentPoint = transform;
        }

        if (targetRigidbody == null)
        {
            targetRigidbody = GetComponent<Rigidbody>();
        }

        if (playerMovement == null)
        {
            playerMovement = GetComponent<PlayerMovement>();
        }
    }

    private void FixedUpdate()
    {
        if (!ShouldApplyPhysicsPull())
        {
            return;
        }

        Vector3 delta = activeAnchor.position - AttachmentPosition;
        float distance = delta.magnitude;
        if (distance <= desiredDistance)
        {
            return;
        }

        float stretch = distance - desiredDistance;
        float acceleration = pullAcceleration * stretch;
        acceleration += reelBonusAcceleration * Mathf.Clamp01(stretch);
        acceleration = Mathf.Min(acceleration, maxAcceleration);

        Vector3 force = delta.normalized * acceleration;
        force += Vector3.up * verticalAssist * Mathf.Clamp01(stretch);

        targetRigidbody.AddForce(force, ForceMode.Acceleration);
        playerMovement?.DisableGroundSnap(0.1f);
    }

    public bool CanBeLassoed()
    {
        return isActiveAndEnabled && gameObject.activeInHierarchy;
    }

    public void Attach(LassoItem source, float ropeLength)
    {
        if (source == null)
        {
            return;
        }

        activeSource = source;
        activeAnchor = source.RopeOrigin;
        desiredDistance = Mathf.Max(0.5f, ropeLength);
    }

    public void Detach(LassoItem source)
    {
        if (source == null || activeSource != source)
        {
            return;
        }

        activeSource = null;
        activeAnchor = null;
    }

    public void SetDesiredDistance(LassoItem source, float ropeLength)
    {
        if (source == null || activeSource != source)
        {
            return;
        }

        desiredDistance = Mathf.Max(0.5f, ropeLength);
    }

    public bool TryGetLeadIntent(Vector3 currentPosition, out MoveIntent intent)
    {
        if (!IsAttached || !ShouldDriveAi())
        {
            intent = default;
            return false;
        }

        Vector3 anchorPosition = activeAnchor.position;
        float stopDistance = Mathf.Max(0.25f, desiredDistance * aiLeadSlackMultiplier);
        float distance = Vector3.Distance(currentPosition, anchorPosition);

        if (distance <= stopDistance)
        {
            intent = MoveIntent.StopAndFace(anchorPosition);
            return true;
        }

        intent = MoveIntent.MoveTo(anchorPosition, stopDistance, aiLeadSpeedMultiplier);
        return true;
    }

    private bool ShouldApplyPhysicsPull()
    {
        if (!IsAttached || targetRigidbody == null || targetRigidbody.isKinematic)
        {
            return false;
        }

        if (!IsSpawned)
        {
            return true;
        }

        return IsOwner || IsServer;
    }

    private bool ShouldDriveAi()
    {
        if (!IsAttached)
        {
            return false;
        }

        if (!IsSpawned)
        {
            return true;
        }

        return IsServer;
    }

    private void OnDisable()
    {
        activeSource = null;
        activeAnchor = null;
    }

    private void OnValidate()
    {
        pullAcceleration = Mathf.Max(0.1f, pullAcceleration);
        reelBonusAcceleration = Mathf.Max(0f, reelBonusAcceleration);
        verticalAssist = Mathf.Max(0f, verticalAssist);
        maxAcceleration = Mathf.Max(0.1f, maxAcceleration);
        aiLeadSlackMultiplier = Mathf.Clamp(aiLeadSlackMultiplier, 0.1f, 1f);
        aiLeadSpeedMultiplier = Mathf.Max(0.1f, aiLeadSpeedMultiplier);
    }
}
