using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class LassoItem : ToolItem
{
    [Header("References")]
    [SerializeField] private Transform ropeOrigin;
    [SerializeField] private LineRenderer lineRenderer;

    [Header("Throw")]
    [SerializeField] private float castDistance = 20f;
    [SerializeField] private float castRadius = 0.35f;
    [SerializeField] private LayerMask targetMask = ~0;

    [Header("Rope")]
    [SerializeField] private float initialRopeLength = 7f;
    [SerializeField] private float minRopeLength = 2f;
    [SerializeField] private float maxRopeLength = 18f;
    [SerializeField] private float scrollLengthFactor = 0.015f;
    [SerializeField] private float autoReleaseDistance = 28f;
    [SerializeField] private int ropeSegments = 10;
    [SerializeField] private float ropeSag = 0.6f;

    private LassoTarget currentTarget;
    private float currentRopeLength;
    private PlayerLook playerLook;
    private bool isSubscribedToScroll;

    public Transform RopeOrigin => ropeOrigin != null ? ropeOrigin : transform;

    protected override void Awake()
    {
        base.Awake();
        playerLook = GetComponentInParent<PlayerLook>();

        if (lineRenderer == null)
        {
            lineRenderer = GetComponentInChildren<LineRenderer>(true);
        }

        if (lineRenderer == null)
        {
            lineRenderer = gameObject.AddComponent<LineRenderer>();
            lineRenderer.widthMultiplier = 0.05f;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
        }

        lineRenderer.enabled = false;
        lineRenderer.positionCount = Mathf.Max(2, ropeSegments);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        TrySubscribeToScroll();
    }

    protected override void OnDisable()
    {
        UnsubscribeFromScroll();

        base.OnDisable();
        ClearAttachedState();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && currentTarget != null)
        {
            ReleaseInternal();
            ReleaseClientRpc();
        }

        base.OnNetworkDespawn();
    }

    private void LateUpdate()
    {
        TrySubscribeToScroll();
        UpdateRopeVisual();

        if (currentTarget == null)
        {
            return;
        }

        float distance = Vector3.Distance(RopeOrigin.position, currentTarget.AttachmentPosition);
        if (distance > autoReleaseDistance)
        {
            if (IsSpawned)
            {
                RequestReleaseServerRpc();
            }
            else
            {
                ReleaseInternal();
            }
        }
    }

    protected override void Use()
    {
        Debug.Log($"[LassoItem] Use triggered on '{name}'.");

        if (currentTarget != null)
        {
            Debug.Log($"[LassoItem] Releasing target from '{name}'.");
            if (IsSpawned)
            {
                RequestReleaseServerRpc();
            }
            else
            {
                ReleaseInternal();
            }
            return;
        }

        if (!TryFindTarget(out LassoTarget target))
        {
            Debug.Log($"[LassoItem] No valid lasso target found from '{name}'.");
            return;
        }

        float ropeLength = Mathf.Clamp(initialRopeLength, minRopeLength, maxRopeLength);
        Debug.Log($"[LassoItem] Target found: '{target.name}'. Requested rope length: {ropeLength}.");
        if (IsSpawned && target.TryGetComponent(out NetworkObject targetNetworkObject) && targetNetworkObject.IsSpawned)
        {
            RequestAttachServerRpc(targetNetworkObject, ropeLength);
            return;
        }

        ApplyAttachedState(target, ropeLength);
    }

    protected override bool CanUse()
    {
        return base.CanUse() && RopeOrigin != null;
    }

    private bool TryFindTarget(out LassoTarget target)
    {
        target = null;

        Transform aimTransform = ResolveAimTransform();
        if (aimTransform == null)
        {
            return false;
        }

        Vector3 origin = aimTransform.position;
        Vector3 direction = aimTransform.forward;
        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            castRadius,
            direction,
            castDistance,
            targetMask,
            QueryTriggerInteraction.Ignore
        );

        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            LassoTarget candidate = hit.collider.GetComponentInParent<LassoTarget>();
            if (candidate == null || !candidate.CanBeLassoed())
            {
                continue;
            }

            if (candidate.transform.root == transform.root)
            {
                continue;
            }

            target = candidate;
            return true;
        }

        return false;
    }

    private Transform ResolveAimTransform()
    {
        if (playerLook == null)
        {
            playerLook = GetComponentInParent<PlayerLook>();
        }

        if (playerLook != null && playerLook.playerCamera != null)
        {
            return playerLook.playerCamera.transform;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            return mainCamera.transform;
        }

        return RopeOrigin;
    }

    private void HandleScrollAdjusted(float scrollDelta)
    {
        if (currentTarget == null)
        {
            return;
        }

        float updatedLength = currentRopeLength - scrollDelta * scrollLengthFactor;
        updatedLength = Mathf.Clamp(updatedLength, minRopeLength, maxRopeLength);

        if (Mathf.Abs(updatedLength - currentRopeLength) < 0.01f)
        {
            return;
        }

        if (IsSpawned && currentTarget.TryGetComponent(out NetworkObject targetNetworkObject) && targetNetworkObject.IsSpawned)
        {
            UpdateRopeLengthServerRpc(updatedLength);
            return;
        }

        ApplyRopeLength(updatedLength);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestAttachServerRpc(NetworkObjectReference targetReference, float ropeLength)
    {
        if (!targetReference.TryGet(out NetworkObject targetNetworkObject))
        {
            Debug.LogWarning("[LassoItem] Server attach failed: target network object could not be resolved.");
            return;
        }

        LassoTarget target = targetNetworkObject.GetComponent<LassoTarget>();
        if (target == null || !target.CanBeLassoed())
        {
            Debug.LogWarning("[LassoItem] Server attach failed: target is missing LassoTarget or cannot be lassoed.");
            return;
        }

        Debug.Log($"[LassoItem] Server attached '{name}' to '{target.name}'.");
        ApplyAttachedState(target, ropeLength);
        AttachClientRpc(targetReference, currentRopeLength);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestReleaseServerRpc()
    {
        Debug.Log($"[LassoItem] Server release requested for '{name}'.");
        ReleaseInternal();
        ReleaseClientRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void UpdateRopeLengthServerRpc(float ropeLength)
    {
        if (currentTarget == null)
        {
            return;
        }

        ApplyRopeLength(ropeLength);
        UpdateRopeLengthClientRpc(currentRopeLength);
    }

    [ClientRpc]
    private void AttachClientRpc(NetworkObjectReference targetReference, float ropeLength)
    {
        if (!targetReference.TryGet(out NetworkObject targetNetworkObject))
        {
            return;
        }

        LassoTarget target = targetNetworkObject.GetComponent<LassoTarget>();
        if (target == null)
        {
            return;
        }

        ApplyAttachedState(target, ropeLength);
    }

    [ClientRpc]
    private void ReleaseClientRpc()
    {
        ReleaseInternal();
    }

    [ClientRpc]
    private void UpdateRopeLengthClientRpc(float ropeLength)
    {
        ApplyRopeLength(ropeLength);
    }

    private void ApplyAttachedState(LassoTarget target, float ropeLength)
    {
        if (currentTarget != null && currentTarget != target)
        {
            currentTarget.Detach(this);
        }

        currentTarget = target;
        currentRopeLength = Mathf.Clamp(ropeLength, minRopeLength, maxRopeLength);
        currentTarget.Attach(this, currentRopeLength);
        Debug.Log($"[LassoItem] '{name}' attached to '{target.name}' with rope length {currentRopeLength}.");
        if (lineRenderer != null)
        {
            lineRenderer.enabled = true;
        }
    }

    private void ApplyRopeLength(float ropeLength)
    {
        currentRopeLength = Mathf.Clamp(ropeLength, minRopeLength, maxRopeLength);
        currentTarget?.SetDesiredDistance(this, currentRopeLength);
    }

    private void ReleaseInternal()
    {
        if (currentTarget == null)
        {
            return;
        }

        Debug.Log($"[LassoItem] '{name}' released '{currentTarget.name}'.");
        currentTarget.Detach(this);
        currentTarget = null;
        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
        }
    }

    private void ClearAttachedState()
    {
        currentTarget?.Detach(this);
        currentTarget = null;

        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
        }
    }

    private void UpdateRopeVisual()
    {
        if (lineRenderer == null || currentTarget == null)
        {
            if (lineRenderer != null)
            {
                lineRenderer.enabled = false;
            }

            return;
        }

        Vector3 start = RopeOrigin.position;
        Vector3 end = currentTarget.AttachmentPosition;

        int segmentCount = Mathf.Max(2, ropeSegments);
        if (lineRenderer.positionCount != segmentCount)
        {
            lineRenderer.positionCount = segmentCount;
        }

        lineRenderer.enabled = true;

        float distance = Vector3.Distance(start, end);
        float sagStrength = ropeSag * Mathf.Clamp01(currentRopeLength / Mathf.Max(distance, 0.01f));

        for (int i = 0; i < segmentCount; i++)
        {
            float t = i / (segmentCount - 1f);
            Vector3 point = Vector3.Lerp(start, end, t);
            point += Vector3.down * Mathf.Sin(t * Mathf.PI) * sagStrength;
            lineRenderer.SetPosition(i, point);
        }
    }

    private void OnValidate()
    {
        castDistance = Mathf.Max(1f, castDistance);
        castRadius = Mathf.Max(0.01f, castRadius);
        minRopeLength = Mathf.Max(0.5f, minRopeLength);
        maxRopeLength = Mathf.Max(minRopeLength, maxRopeLength);
        initialRopeLength = Mathf.Clamp(initialRopeLength, minRopeLength, maxRopeLength);
        scrollLengthFactor = Mathf.Max(0.0001f, scrollLengthFactor);
        autoReleaseDistance = Mathf.Max(maxRopeLength, autoReleaseDistance);
        ropeSegments = Mathf.Max(2, ropeSegments);
        ropeSag = Mathf.Max(0f, ropeSag);
    }

    private void TrySubscribeToScroll()
    {
        if (isSubscribedToScroll || inputManager == null)
        {
            return;
        }

        inputManager.OnScrollAdjusted += HandleScrollAdjusted;
        isSubscribedToScroll = true;
    }

    private void UnsubscribeFromScroll()
    {
        if (!isSubscribedToScroll || inputManager == null)
        {
            return;
        }

        inputManager.OnScrollAdjusted -= HandleScrollAdjusted;
        isSubscribedToScroll = false;
    }
}
