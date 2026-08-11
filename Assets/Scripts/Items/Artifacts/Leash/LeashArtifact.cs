using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Leash artifact — a ToolItem the player equips from the inventory.
///
/// Click rules:
///   • Left-click on a fresh GameObject (no LeashAttachable, or attachable but with zero
///     leashes) → creates a new leash with end A on that object and end B in the player's
///     hand. The leash is added to <see cref="_heldLeashes"/>.
///   • Left-click on an already-leashed GameObject while holding ≥1 leash → terminates
///     the most recent held leash onto that object (the hand end becomes a real attachment).
///     The leash is removed from <see cref="_heldLeashes"/> and lives on independently.
///   • Right-click (or whatever is bound to <see cref="dropAction"/>) → disposes the most
///     recent held leash entirely.
///
/// Held leashes also dispose if the artifact is unequipped/destroyed (e.g., player swaps
/// to another item). Leashes anchored on both ends survive — they're independent scene
/// objects.
/// </summary>
public class LeashArtifact : ToolItem
{
    [Header("Targeting")]
    [SerializeField] private float maxRange = 30f;
    [SerializeField] private LayerMask leashableLayers = ~0;

    [Header("Rope Physics")]
    [SerializeField] private float maxLeashLength = 8f;
    [SerializeField] private float stiffness = 600f;
    [SerializeField] private float damping = 30f;
    [Tooltip("If the spring force needed to keep the rope at maxLength exceeds this many Newtons, the rope snaps. Set very high (e.g. 100000) for unbreakable ropes.")]
    [SerializeField] private float breakForce = 8000f;

    [Header("Rope Visuals")]
    [SerializeField] private Material ropeMaterial;
    [SerializeField] private Color ropeColor = new Color(0.6f, 0.5f, 0.35f);
    [SerializeField] private float ropeWidth = 0.04f;
    [SerializeField] private int ropeSegments = 18;
    [Tooltip("Maximum vertical droop (in world units) when the rope is fully slack.")]
    [SerializeField] private float ropeSag = 0.6f;

    [Header("Hand Anchor")]
    [Tooltip("Where the held end of the leash visually starts. Falls back to the player root if unassigned.")]
    [SerializeField] private Transform muzzle;

    [Header("Input")]
    [Tooltip("Pressing this drops the most recently held leash. Bind to RightClick (or any button).")]
    [SerializeField] private InputActionReference dropAction;

    private readonly List<Leash> _heldLeashes = new List<Leash>();

    // The drop action is read-only here: enabling/disabling it could disrupt other
    // systems that share the same InputAction (matches the pattern used by LassoArtifact).
    // The action map should be enabled by the higher-level PlayerInput component.

    // ── Left-click (Use) ───────────────────────────────────────────────────

    [Header("Debug")]
    [Tooltip("Log every step of Use() to the Console. Turn on to find out where clicks fail.")]
    [SerializeField] private bool debugLogs = true;

    protected override void Use()
    {
        base.Use(); // populates aimProvider

        if (debugLogs) Debug.Log($"[Leash] Use() called. owner={(owner != null ? owner.name : "null")}, aimProvider={aimProvider}");

        if (aimProvider == null)
        {
            Debug.LogWarning("[Leash] aimProvider is null. The player root must have an AimProvider component.");
            return;
        }

        var hitMaybe = aimProvider.GetRayCast(maxRange);
        if (!hitMaybe.HasValue)
        {
            if (debugLogs) Debug.Log($"[Leash] Raycast hit nothing within {maxRange}m.");
            return;
        }
        var hit = hitMaybe.Value;
        if (hit.collider == null)
        {
            if (debugLogs) Debug.Log("[Leash] Raycast hit had no collider.");
            return;
        }

        if (debugLogs) Debug.Log($"[Leash] Raycast hit '{hit.collider.name}' on layer '{LayerMask.LayerToName(hit.collider.gameObject.layer)}' ({hit.collider.gameObject.layer}). Mask value: {leashableLayers.value}.");

        // Layer filter
        if ((leashableLayers.value & (1 << hit.collider.gameObject.layer)) == 0)
        {
            if (debugLogs) Debug.Log($"[Leash] Layer {hit.collider.gameObject.layer} filtered out by leashableLayers. Adjust the mask in the Inspector.");
            return;
        }

        // Don't leash to self
        if (owner != null && hit.collider.transform.IsChildOf(owner.transform))
        {
            if (debugLogs) Debug.Log($"[Leash] Hit '{hit.collider.name}' is a child of the player ('{owner.name}'); ignoring (can't leash self).");
            return;
        }

        // Resolve target root (Rigidbody if present, else the collider GO)
        var rb = hit.collider.GetComponentInParent<Rigidbody>();
        GameObject rootGO = rb != null ? rb.gameObject : hit.collider.gameObject;
        if (rootGO == owner)
        {
            if (debugLogs) Debug.Log("[Leash] Resolved target root is the player; ignoring.");
            return;
        }

        var existing = rootGO.GetComponent<LeashAttachable>();
        bool alreadyLeashed = existing != null && existing.HasLeashes;
        if (debugLogs) Debug.Log($"[Leash] Target='{rootGO.name}', hasRb={rb != null}, alreadyLeashed={alreadyLeashed}, held={_heldLeashes.Count}.");

        if (alreadyLeashed && _heldLeashes.Count > 0)
        {
            // Try to terminate the most recent held leash onto this object.
            // If the held leash already references this object (its other end is on rootGO),
            // it'd be a self-loop — skip and bail out.
            var leash = _heldLeashes[_heldLeashes.Count - 1];
            if (leash == null)
            {
                _heldLeashes.RemoveAt(_heldLeashes.Count - 1);
                return;
            }
            if (leash.ReferencesObject(rootGO)) return;

            leash.TerminateHandEndOnto(rootGO, hit.point);
            _heldLeashes.RemoveAt(_heldLeashes.Count - 1);
            if (debugLogs) Debug.Log($"[Leash] Terminated held leash onto '{rootGO.name}'. Held now: {_heldLeashes.Count}.");
        }
        else
        {
            CreateHeldLeash(rootGO, hit.point);
            if (debugLogs) Debug.Log($"[Leash] Created new held leash on '{rootGO.name}'. Held now: {_heldLeashes.Count}.");
        }
    }

    private void CreateHeldLeash(GameObject targetRoot, Vector3 worldHit)
    {
        var go = new GameObject("Leash");
        var leash = go.AddComponent<Leash>();
        var lr = go.AddComponent<LineRenderer>();

        if (ropeMaterial != null) lr.material = ropeMaterial;
        lr.startColor = ropeColor;
        lr.endColor = ropeColor;
        lr.startWidth = ropeWidth;
        lr.endWidth = ropeWidth;
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        leash.line = lr;
        leash.maxLength = maxLeashLength;
        leash.stiffness = stiffness;
        leash.damping = damping;
        leash.breakForce = breakForce;
        leash.segments = Mathf.Max(2, ropeSegments);
        leash.ropeSag = ropeSag;

        leash.ConfigureEndpointA_OnObject(targetRoot, worldHit);

        // The hand-end transform is the PLAYER ROOT, not the muzzle. The muzzle is a
        // child of this artifact prefab — when the prefab is destroyed (item depleted,
        // hot-swap, scene streaming), the muzzle dies and the leash would self-dispose.
        // By anchoring on the player root and baking the muzzle's player-local offset,
        // the rope still visually starts near the hand but the leash survives anything
        // that happens to the artifact.
        Transform handAnchor = owner != null ? owner.transform : transform;
        Rigidbody ownerRb = owner != null ? owner.GetComponentInParent<Rigidbody>() : null;
        Vector3 handLocalOffset = Vector3.zero;
        if (muzzle != null && handAnchor != null)
        {
            handLocalOffset = handAnchor.InverseTransformPoint(muzzle.position);
        }
        leash.ConfigureEndpointB_OnPlayerHand(handAnchor, ownerRb, handLocalOffset);

        _heldLeashes.Add(leash);
    }

    // ── Per-frame upkeep ───────────────────────────────────────────────────

    private void Update()
    {
        // Drop most recent held leash
        if (dropAction != null && dropAction.action != null
            && dropAction.action.WasPressedThisFrame()
            && _heldLeashes.Count > 0)
        {
            int idx = _heldLeashes.Count - 1;
            var leash = _heldLeashes[idx];
            _heldLeashes.RemoveAt(idx);
            if (leash != null) leash.Dispose();
        }

        // Sweep null entries (a leash may have self-destroyed because its target died
        // or the rope snapped while it was still in our held list).
        for (int i = _heldLeashes.Count - 1; i >= 0; i--)
        {
            if (_heldLeashes[i] == null) _heldLeashes.RemoveAt(i);
        }
    }

    private void OnDestroy()
    {
        // Held leashes are anchored to the player root (not this artifact), so they
        // survive independently. Just drop our list reference.
        _heldLeashes.Clear();
    }
}
