using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;
using SpaceGame.World;

namespace SpaceGame.Items
{
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

        // ── Live registry ──────────────────────────────────────────────────────

        private static readonly List<Leash> LiveLeashes = new();

        /// <summary>
        /// Every rope currently in the session.
        ///
        /// <para>
        /// A leash is not spawned from a prefab and is not parented to anything — it is a bare
        /// <c>new GameObject</c> that immediately calls <c>DontDestroyOnLoad</c> — so nothing else in
        /// the game can find one, and a saver had no way to ask what ropes exist. This list is that
        /// way. Kept static because the ropes belong to the session rather than to any object,
        /// which is also why their saver is a global one.
        /// </para>
        /// </summary>
        public static IReadOnlyList<Leash> All => LiveLeashes;

        private void OnEnable()
        {
            if (!LiveLeashes.Contains(this)) LiveLeashes.Add(this);
        }

        private void OnDisable() => LiveLeashes.Remove(this);

        // ── Construction ───────────────────────────────────────────────────────

        /// <summary>Everything about a rope that is a decision rather than a measurement.</summary>
        public struct Settings
        {
            public float maxLength;
            public float stiffness;
            public float damping;
            public float breakForce;
            public int segments;
            public float ropeSag;
            public Color color;
            public float width;
            public Material material;
        }

        /// <summary>
        /// Build an unattached rope with its renderer already set up.
        ///
        /// <para>
        /// One factory rather than two copies of the same fifteen lines: the artifact builds ropes
        /// when a player clicks, and the save system builds them when a world is loaded, and a rope
        /// that came back from a save must be indistinguishable from one that did not.
        /// </para>
        /// </summary>
        public static Leash Create(in Settings settings)
        {
            var go = new GameObject("Leash");
            var leash = go.AddComponent<Leash>();
            var lr = go.AddComponent<LineRenderer>();

            if (settings.material != null) lr.material = settings.material;
            lr.startColor = settings.color;
            lr.endColor = settings.color;
            lr.startWidth = settings.width;
            lr.endWidth = settings.width;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            leash.line = lr;
            leash.maxLength = settings.maxLength;
            leash.stiffness = settings.stiffness;
            leash.damping = settings.damping;
            leash.breakForce = settings.breakForce;
            leash.segments = Mathf.Max(2, settings.segments);
            leash.ropeSag = settings.ropeSag;

            return leash;
        }

        public bool IsHeld => aKind == EndpointKind.PlayerHand || bKind == EndpointKind.PlayerHand;

        /// <summary>The object end A is tied to, or null for a rope pinned to bare geometry.</summary>
        public Transform EndATransform => aTransform;

        /// <summary>The object end B is tied to, or null.</summary>
        public Transform EndBTransform => bTransform;

        public Vector3 EndAPos => aTransform != null ? aTransform.TransformPoint(aLocalOffset) : Vector3.zero;
        public Vector3 EndBPos => bTransform != null ? bTransform.TransformPoint(bLocalOffset) : Vector3.zero;

        private void Awake()
        {
            Debug.Log($"[Leash] Awake id={GetInstanceID()} scene='{gameObject.scene.name}'");
            // Protect against streaming chunk scene unload destroying the leash.
            DontDestroyOnLoad(gameObject);
        }

        // ── Constraint ─────────────────────────────────────────────────────────

        /// <summary>
        /// Whether this machine runs the constraint.
        ///
        /// The server, or nobody. A rope has two ends and no natural owner — one may be a crate the
        /// server simulates while the other is a player who simulates themselves — so letting each
        /// machine run its own copy means two authorities fighting over the same bodies. Every
        /// machine still BUILDS the rope and draws it (see LateUpdate); only one machine decides
        /// what it does.
        /// </summary>
        private static bool Simulating => !Network.IsNetworked || Network.Server;

        // What the rope owes each player endpoint since the last flush, as a velocity delta.
        // Accumulated rather than sent per step: at 50 physics steps a second, one message per
        // step per rope would drown the channel for a force the player cannot feel at that
        // resolution anyway.
        private Vector3 pendingTugA;
        private Vector3 pendingTugB;
        private float nextTugSendTime;

        /// <summary>How often accumulated tugs go out. Ten a second reads as continuous pull.</summary>
        private const float TugSendInterval = 0.1f;

        private void FixedUpdate()
        {
            if (_disposed) return;
            if (!Simulating) return;

            FlushPlayerTugs();

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
            ResolveEndpoint(aRigidbody, aReactionRb, aAgent, n * forceMag, n, positionStepPerSide, ref pendingTugA);
            ResolveEndpoint(bRigidbody, bReactionRb, bAgent, -n * forceMag, -n, positionStepPerSide, ref pendingTugB);
        }

        /// <summary>
        /// Hand each player endpoint what the rope owes it, for that player's own machine to apply.
        ///
        /// Sent to the PLAYER's channel rather than the rope's: the rope is a plain scene object
        /// with no NetworkObject and therefore no relay of its own, while the player has both.
        /// </summary>
        private void FlushPlayerTugs()
        {
            if (Time.time < nextTugSendTime) return;
            nextTugSendTime = Time.time + TugSendInterval;

            SendTug(aRigidbody, aReactionRb, ref pendingTugA);
            SendTug(bRigidbody, bReactionRb, ref pendingTugB);
        }

        private static void SendTug(Rigidbody primary, Rigidbody reaction, ref Vector3 pending)
        {
            if (pending.sqrMagnitude < 1e-6f) return;

            Rigidbody target = primary != null ? primary : reaction;
            if (target == null) { pending = Vector3.zero; return; }

            NetMessaging.NetSendTo(target.gameObject, NetMsg.RopeTug,
                                   new NetArg { P = pending }, NetTo.All);

            pending = Vector3.zero;
        }

        /// <summary>
        /// Whether this body is a player's, and therefore not the server's to push.
        ///
        /// The tag, for the same reason the spawn clearance check uses it: the player capsule is on
        /// layer 0 so no mask can pick it out, and PlayerCharacter is the only prefab in the
        /// project carrying the tag.
        /// </summary>
        private static bool IsPlayerBody(Rigidbody body) =>
            body != null && body.CompareTag("Player");

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
            float positionStep,
            ref Vector3 pendingTug)
        {
            Rigidbody target = primary != null ? primary : reaction;
            if (target == null) return;

            // A player's body is theirs to move, not ours. Bank what the rope owes them as a
            // velocity delta — mass applied here, where the body actually is — and let their own
            // machine apply it. Pushing it from the server would be undone within the tick.
            if (IsPlayerBody(target))
            {
                if (!target.isKinematic && target.mass > 0f)
                    pendingTug += forceTowardOther * (Time.fixedDeltaTime / target.mass);

                return;
            }

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

        // ── Restore ────────────────────────────────────────────────────────────

        /// <summary>One end of a rope as a save file can describe it.</summary>
        public struct EndpointRestore
        {
            /// <summary>What this end was tied to. Static ends have none and use <see cref="WorldPoint"/>.</summary>
            public GameObject Root;

            /// <summary>Where on <see cref="Root"/>, in its local space.</summary>
            public Vector3 LocalOffset;

            /// <summary>Where in the world, for an end pinned to bare geometry rather than an object.</summary>
            public Vector3 WorldPoint;

            /// <summary>True for the end that was in a player's hand.</summary>
            public bool Held;
        }

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreEndpointA(in EndpointRestore endpoint) => RestoreEndpoint(true, endpoint);

        /// <summary>Restore-only. Called by the save system; do not call from gameplay.</summary>
        public void RestoreEndpointB(in EndpointRestore endpoint) => RestoreEndpoint(false, endpoint);

        /// <summary>
        /// Tie one end of this rope to a described endpoint.
        ///
        /// <para>
        /// Not <see cref="ConfigureEndpointA_OnObject"/>: that takes a world hit point and derives
        /// the local offset from wherever the object is standing at the time, which for a restore is
        /// wherever the object was PLACED this session. The offset is a property of the knot and is
        /// stored, so it is handed in here rather than recomputed.
        /// </para>
        /// </summary>
        private void RestoreEndpoint(bool isA, in EndpointRestore endpoint)
        {
            EndpointKind kind;
            Transform xform;
            Rigidbody rb = null;
            Rigidbody reaction = null;
            LeashAttachable attachable = null;
            NavMeshAgent agent = null;
            Vector3 offset = endpoint.LocalOffset;

            if (endpoint.Root == null)
            {
                // Bare geometry. A fresh stand-in at the recorded point, exactly as LeashArtifact
                // makes one for a rope tied to a wall — the point is the anchor, and it is the same
                // point in every session.
                var anchor = new GameObject("LeashAnchor");
                anchor.transform.position = endpoint.WorldPoint;

                kind = EndpointKind.Static;
                xform = anchor.transform;
                offset = Vector3.zero;
            }
            else if (endpoint.Held)
            {
                kind = EndpointKind.PlayerHand;
                xform = endpoint.Root.transform;
                reaction = endpoint.Root.GetComponentInParent<Rigidbody>();
            }
            else
            {
                rb = endpoint.Root.GetComponentInParent<Rigidbody>();
                xform = rb != null ? rb.transform : endpoint.Root.transform;
                kind = rb != null ? EndpointKind.Object : EndpointKind.Static;

                attachable = LeashAttachable.GetOrAdd(xform.gameObject);
                attachable.AddLeash(this);
                agent = xform.GetComponentInParent<NavMeshAgent>();
            }

            if (isA)
            {
                aKind = kind; aTransform = xform; aRigidbody = rb;
                aReactionRb = reaction; aLocalOffset = offset; aAttachable = attachable; aAgent = agent;
            }
            else
            {
                bKind = kind; bTransform = xform; bRigidbody = rb;
                bReactionRb = reaction; bLocalOffset = offset; bAttachable = attachable; bAgent = agent;
            }
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
}
