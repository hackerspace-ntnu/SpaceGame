using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Items
{
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
        /// <summary>
        /// Aimed by the holder, simulated by the server.
        ///
        /// The authority here is nearly moot — <see cref="Use"/> does nothing, because the rope is
        /// built by <see cref="Present"/> on every machine and <see cref="Leash"/> decides for
        /// itself which machine runs the constraint. Left as Owner so the aim, which is the only
        /// thing this item genuinely owns, stays with the machine that has the camera.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Owner;

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

        /// <summary>
        /// Owner side: aim, and put the answer in the message.
        ///
        /// The raycast has to happen here and only here, because this is the one machine with the
        /// camera that aimed it. A peer re-running it would trace from its own view and rope
        /// something else — or, on the host, rope whatever the host happens to be looking at.
        ///
        /// <see cref="NetArg.Target"/> carries the object when it is a spawned NetworkObject, and
        /// <see cref="NetArg.P"/> always carries the world hit point. Between them every endpoint
        /// that CAN be consistent across machines is addressable: static geometry by its point,
        /// which is identical everywhere, and networked objects by id. A dynamic prop that nobody
        /// networked is neither, and ropes to it stay local — its physics already differs per
        /// machine, so a shared rope to it could not have been made to agree anyway.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            base.OnRequestUse(ref arg);

            arg.B = MissVerb;

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

            arg = arg.With(rootGO);
            arg.P = hit.point;
            arg.B = HitVerb;
        }

        private const int MissVerb = 0;
        private const int HitVerb = 1;

        /// <summary>
        /// Nothing. The rope is built by <see cref="Present"/> on every machine, and which machine
        /// SIMULATES it is decided inside <see cref="Leash"/> — the server, or nobody.
        ///
        /// This used to be where the whole feature lived, and that was the bug: Use() is the
        /// authority-only half of UsableItem, so the rope was constructed on exactly one machine
        /// and did not exist anywhere else. Everyone but its creator saw objects moving under an
        /// invisible force.
        /// </summary>
        protected override void Use() { }

        /// <summary>Every machine: build the rope the owner aimed.</summary>
        protected override void Present()
        {
            NetArg arg = UseArg;
            if (arg.B != HitVerb) return;

            GameObject rootGO = arg.Resolve();

            // No id resolved: either we are offline (where the local reference survives in the arg
            // and Resolve already answered), or the endpoint is static geometry, which has no
            // NetworkObject and does not need one — the point is the anchor, and it is the same
            // point on every machine.
            if (rootGO == null) rootGO = StaticAnchorAt(arg.P);
            if (rootGO == null) return;

            Apply(rootGO, arg.P);
        }

        /// <summary>
        /// A bodyless stand-in for an endpoint that is a place rather than an object.
        ///
        /// Leash reads an endpoint with no Rigidbody as Static, so this anchors the rope without
        /// it needing to know the difference. Parented to the rope's own lifetime by Leash, which
        /// disposes when an endpoint goes away.
        /// </summary>
        private static GameObject StaticAnchorAt(Vector3 worldPoint)
        {
            if (worldPoint == Vector3.zero) return null;

            var anchor = new GameObject("LeashAnchor");
            anchor.transform.position = worldPoint;
            return anchor;
        }

        private void Apply(GameObject rootGO, Vector3 hitPoint)
        {
            var existing = rootGO.GetComponent<LeashAttachable>();
            bool alreadyLeashed = existing != null && existing.HasLeashes;
            if (debugLogs) Debug.Log($"[Leash] Target='{rootGO.name}', alreadyLeashed={alreadyLeashed}, held={_heldLeashes.Count}.");

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

                leash.TerminateHandEndOnto(rootGO, hitPoint);
                _heldLeashes.RemoveAt(_heldLeashes.Count - 1);
                if (debugLogs) Debug.Log($"[Leash] Terminated held leash onto '{rootGO.name}'. Held now: {_heldLeashes.Count}.");
            }
            else
            {
                CreateHeldLeash(rootGO, hitPoint);
                if (debugLogs) Debug.Log($"[Leash] Created new held leash on '{rootGO.name}'. Held now: {_heldLeashes.Count}.");
            }
        }

        /// <summary>
        /// This artifact's rope tuning, as the shared factory takes it.
        ///
        /// <para>
        /// Also what a load builds ropes from — see <see cref="TryResolveSettings"/>. A rope is a
        /// runtime <c>new GameObject</c> with a material reference in it, and a save file can carry
        /// neither, so the settings have to come from the prefab that would have made it.
        /// </para>
        /// </summary>
        public Leash.Settings RopeSettings => new()
        {
            maxLength = maxLeashLength,
            stiffness = stiffness,
            damping = damping,
            breakForce = breakForce,
            segments = Mathf.Max(2, ropeSegments),
            ropeSag = ropeSag,
            color = ropeColor,
            width = ropeWidth,
            material = ropeMaterial,
        };

        /// <summary>
        /// The rope tuning to rebuild a saved leash with, read off the leash item's own prefab.
        ///
        /// <para>
        /// The registry rather than a serialized reference on the saver: the item table already
        /// holds every <c>InventoryItem</c> in the build together with the prefab it equips, so the
        /// authored numbers and — the part nothing else can supply — the rope MATERIAL are reachable
        /// without another asset to wire up and keep in step. Falls back to a plain white rope if
        /// the leash item has been removed from the build, which draws something visible rather than
        /// nothing at all.
        /// </para>
        /// </summary>
        public static bool TryResolveSettings(out Leash.Settings settings)
        {
            foreach (InventoryItem item in Registry<InventoryItem>.All)
            {
                if (item == null || item.itemPrefab == null) continue;

                var artifact = item.itemPrefab.GetComponent<LeashArtifact>();
                if (artifact == null) continue;

                settings = artifact.RopeSettings;
                return true;
            }

            settings = new Leash.Settings
            {
                maxLength = 8f, stiffness = 600f, damping = 30f, breakForce = 8000f,
                segments = 18, ropeSag = 0.6f, color = new Color(0.6f, 0.5f, 0.35f), width = 0.04f,
            };
            return false;
        }

        private void CreateHeldLeash(GameObject targetRoot, Vector3 worldHit)
        {
            Leash leash = Leash.Create(RopeSettings);

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
}
