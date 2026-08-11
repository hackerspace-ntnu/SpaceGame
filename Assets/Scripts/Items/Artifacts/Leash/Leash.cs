using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One physical leash between two endpoints.
///
/// Each Leash is a standalone scene object spawned by <see cref="LeashArtifact"/>. It
/// owns its own LineRenderer, runs its own constraint in FixedUpdate, and self-destroys
/// when an endpoint goes away or the rope snaps under load.
///
/// Endpoint kinds:
///   • PlayerHand  – tracks a muzzle Transform on the player's artifact; reaction force
///                   is applied to a separately-supplied player Rigidbody so the player
///                   gets tugged when the rope is taut.
///   • Object      – a Rigidbody-bearing world object. Receives force directly.
///   • Static      – a world Transform with no Rigidbody (walls, terrain). Anchors only.
///
/// Constraint model: rope is fully slack while distance ≤ maxLength. Beyond maxLength
/// a spring+damper force pulls the endpoints back toward each other (equal & opposite),
/// so heavier objects move less. If the per-frame force exceeds breakForce the rope
/// snaps and disposes itself.
/// </summary>
public class Leash : MonoBehaviour
{
    public enum EndpointKind { PlayerHand, Object, Static }

    // ── Endpoint A ─────────────────────────────────────────────────────────
    public EndpointKind aKind;
    public Transform aTransform;            // muzzle (PlayerHand) or attached transform (Object/Static)
    public Rigidbody aRigidbody;            // null for PlayerHand / Static
    public Vector3 aLocalOffset;            // local-space attach offset on aTransform
    public LeashAttachable aAttachable;     // null for PlayerHand
    public Rigidbody aReactionRb;           // PlayerHand only: player body that takes reaction force
    public NavMeshAgent aAgent;             // cached if endpoint is a NavMeshAgent — used to override agent position

    // ── Endpoint B ─────────────────────────────────────────────────────────
    public EndpointKind bKind;
    public Transform bTransform;
    public Rigidbody bRigidbody;
    public Vector3 bLocalOffset;
    public LeashAttachable bAttachable;
    public Rigidbody bReactionRb;
    public NavMeshAgent bAgent;

    // ── Settings (set by LeashArtifact at spawn) ───────────────────────────
    public float maxLength = 8f;
    public float stiffness = 400f;
    public float damping = 30f;
    public float breakForce = 1500f;
    public int segments = 18;
    public float ropeSag = 0.6f;

    // ── Visuals ────────────────────────────────────────────────────────────
    public LineRenderer line;

    private bool _disposed;

    public bool IsHeld => aKind == EndpointKind.PlayerHand || bKind == EndpointKind.PlayerHand;

    public Vector3 EndAPos => aTransform != null ? aTransform.TransformPoint(aLocalOffset) : Vector3.zero;
    public Vector3 EndBPos => bTransform != null ? bTransform.TransformPoint(bLocalOffset) : Vector3.zero;

    private void Awake()
    {
        Debug.Log($"[Leash] Awake id={GetInstanceID()} scene='{gameObject.scene.name}'");
        // Protect against streaming chunk scene unload destroying the leash.
        DontDestroyOnLoad(gameObject);
    }

    // ── Constraint ─────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (_disposed) return;

        // If an endpoint disappears we DO NOT auto-dispose. The GameObject stays alive
        // so we can inspect it in the Hierarchy. Physics simply freezes for this frame.
        if (aTransform == null || bTransform == null)
        {
            return;
        }

        Vector3 pa = EndAPos;
        Vector3 pb = EndBPos;
        Vector3 delta = pb - pa;
        float dist = delta.magnitude;
        if (dist < 0.0001f) return;
        if (dist <= maxLength) return;          // slack — no force

        Vector3 n = delta / dist;               // unit vector A → B
        float overshoot = dist - maxLength;

        Vector3 vA = GetEndpointVelocity(aRigidbody, aReactionRb);
        Vector3 vB = GetEndpointVelocity(bRigidbody, bReactionRb);
        float vRel = Vector3.Dot(vB - vA, n);   // positive = endpoints separating

        // Damping only resists separation (rope can compress freely while slack-side).
        float forceMag = stiffness * overshoot + damping * Mathf.Max(0f, vRel);

        if (forceMag > breakForce)
        {
            Snap();
            return;
        }

        // Mixed-mode resolution: each endpoint independently uses force (non-kinematic)
        // or position correction (kinematic). Static endpoints (no rigidbody at all) do
        // not move. Position correction snaps the kinematic side fully to the rope sphere
        // so NavMeshAgents can't drift past the rope length even if their pathing fights us.
        bool aMobile = aRigidbody != null || aReactionRb != null;
        bool bMobile = bRigidbody != null || bReactionRb != null;
        int mobileSides = (aMobile ? 1 : 0) + (bMobile ? 1 : 0);
        float positionStepPerSide = mobileSides > 0 ? overshoot / mobileSides : 0f;

        // n points A → B. So A moves along n toward B; B moves along -n toward A.
        ResolveEndpoint(aRigidbody, aReactionRb, aAgent, n * forceMag, n, positionStepPerSide);
        ResolveEndpoint(bRigidbody, bReactionRb, bAgent, -n * forceMag, -n, positionStepPerSide);
    }

    private static Vector3 GetEndpointVelocity(Rigidbody primary, Rigidbody reaction)
    {
        if (primary != null) return primary.linearVelocity;
        if (reaction != null) return reaction.linearVelocity;
        return Vector3.zero;
    }

    /// <summary>
    /// Resolve the constraint on one endpoint. Non-kinematic rigidbodies receive force
    /// and respond via Newton's laws (mass-aware); kinematic rigidbodies are repositioned
    /// via MovePosition (no inertia, but compatible with NavMeshAgent and CharacterController-
    /// style controllers that don't react to AddForce). Static endpoints — neither a primary
    /// nor a reaction rigidbody — do not move.
    /// </summary>
    private static void ResolveEndpoint(
        Rigidbody primary, Rigidbody reaction, NavMeshAgent agent,
        Vector3 forceTowardOther,
        Vector3 unitTowardOther,
        float positionStep)
    {
        Rigidbody target = primary != null ? primary : reaction;
        if (target == null) return;

        if (!target.isKinematic)
        {
            target.AddForce(forceTowardOther, ForceMode.Force);
            return;
        }

        // Kinematic body. If a NavMeshAgent is on this endpoint, use Warp — the official
        // API for forcibly relocating an agent. Plain MovePosition fights the agent's
        // own position writes, so the agent ignores the rope and drifts away. Warp moves
        // the agent and re-syncs its internal navigation state.
        Vector3 newPos = target.position + unitTowardOther * positionStep;
        if (agent != null && agent.isActiveAndEnabled)
        {
            agent.Warp(newPos);
        }
        else
        {
            target.MovePosition(newPos);
        }
    }

    // ── Render ─────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (_disposed || line == null) return;
        if (aTransform == null || bTransform == null) return;

        Vector3 a = EndAPos;
        Vector3 b = EndBPos;
        float dist = Vector3.Distance(a, b);

        // Sag is full when slack, zero when taut. Visual only.
        float slackAmount = Mathf.Max(0f, 1f - dist / Mathf.Max(maxLength, 0.001f));
        float sag = slackAmount * ropeSag;

        int segs = Mathf.Max(2, segments);
        if (line.positionCount != segs) line.positionCount = segs;

        for (int i = 0; i < segs; i++)
        {
            float t = i / (float)(segs - 1);
            Vector3 p = Vector3.Lerp(a, b, t);
            p.y -= Mathf.Sin(t * Mathf.PI) * sag;
            line.SetPosition(i, p);
        }
    }

    // ── Configuration helpers (used by LeashArtifact) ──────────────────────

    public void ConfigureEndpointA_OnObject(GameObject targetRoot, Vector3 worldHitPoint)
    {
        ConfigureObjectEndpoint(targetRoot, worldHitPoint,
            out aKind, out aTransform, out aRigidbody, out aLocalOffset, out aAttachable, out aAgent);
        aReactionRb = null;
    }

    public void ConfigureEndpointB_OnObject(GameObject targetRoot, Vector3 worldHitPoint)
    {
        ConfigureObjectEndpoint(targetRoot, worldHitPoint,
            out bKind, out bTransform, out bRigidbody, out bLocalOffset, out bAttachable, out bAgent);
        bReactionRb = null;
    }

    public void ConfigureEndpointB_OnPlayerHand(Transform handAnchor, Rigidbody playerBody, Vector3 localOffset = default)
    {
        bKind = EndpointKind.PlayerHand;
        bTransform = handAnchor;
        bRigidbody = null;
        bReactionRb = playerBody;
        bLocalOffset = localOffset;
        bAttachable = null;
    }

    private void ConfigureObjectEndpoint(GameObject targetRoot, Vector3 worldHitPoint,
        out EndpointKind kind, out Transform xform, out Rigidbody rb,
        out Vector3 localOffset, out LeashAttachable attachable, out NavMeshAgent agent)
    {
        var foundRb = targetRoot.GetComponentInParent<Rigidbody>();
        Transform rootT = foundRb != null ? foundRb.transform : targetRoot.transform;

        kind = foundRb != null ? EndpointKind.Object : EndpointKind.Static;
        xform = rootT;
        rb = foundRb;
        localOffset = rootT.InverseTransformPoint(worldHitPoint);
        attachable = LeashAttachable.GetOrAdd(rootT.gameObject);
        attachable.AddLeash(this);
        agent = rootT.GetComponentInParent<NavMeshAgent>();
    }

    /// <summary>
    /// Switch whichever end is currently in the player's hand onto a real world object.
    /// Used when the player left-clicks an already-leashed object while holding a leash.
    /// </summary>
    public void TerminateHandEndOnto(GameObject targetRoot, Vector3 worldHitPoint)
    {
        if (aKind == EndpointKind.PlayerHand)
        {
            aReactionRb = null;
            ConfigureEndpointA_OnObject(targetRoot, worldHitPoint);
        }
        else if (bKind == EndpointKind.PlayerHand)
        {
            bReactionRb = null;
            ConfigureEndpointB_OnObject(targetRoot, worldHitPoint);
        }

        // Prevent instant-snap on termination: if the new geometry is already past
        // maxLength, expand maxLength to fit. The rope is now exactly taut with no
        // overshoot, so no spring force builds up on the first frame.
        float currentDist = Vector3.Distance(EndAPos, EndBPos);
        if (currentDist > maxLength)
        {
            maxLength = currentDist + 0.5f;
        }
    }

    public bool ReferencesObject(GameObject go)
    {
        if (go == null) return false;
        if (aTransform != null && aTransform.gameObject == go) return true;
        if (bTransform != null && bTransform.gameObject == go) return true;
        return false;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    public void Snap()
    {
        // Hook point for SFX/VFX on snap. Disposes the leash.
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Debug.Log($"[Leash] Dispose id={GetInstanceID()} called from:\n{System.Environment.StackTrace}");
        _disposed = true;

        if (aAttachable != null) aAttachable.RemoveLeash(this);
        if (bAttachable != null) bAttachable.RemoveLeash(this);

        if (this != null && gameObject != null)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        Debug.Log($"[Leash] OnDestroy id={GetInstanceID()} _disposed={_disposed}");
        // Defensive: if something else destroyed us (scene unload, parent death) and we
        // never went through Dispose(), make sure attachables don't keep stale refs.
        if (_disposed) return;
        _disposed = true;
        if (aAttachable != null) aAttachable.RemoveLeash(this);
        if (bAttachable != null) bAttachable.RemoveLeash(this);
    }
}
