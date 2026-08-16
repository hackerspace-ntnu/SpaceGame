using System;
using System.Collections;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// The player's half of the backpack: which pack is theirs, where it rides, and the four states
    /// it moves between.
    ///
    /// The pack instance is created once and never destroyed. Deploying unparents it, re-shouldering
    /// parents it back. Spawning a fresh pack per deploy would be simpler, but the strap items would
    /// pop out of existence on every toggle and the contents would need somewhere else to live.
    /// </summary>
    public class BackpackController : MonoBehaviour
    {
        public enum State { Shouldered, Deploying, Open, Stowing }

        [Header("Pack")]
        [SerializeField] private GameObject backpackPrefab;

        [Header("Back socket")]
        [Tooltip("Which bone the pack rides on when the rig is humanoid.")]
        [SerializeField] private HumanBodyBones backBone = HumanBodyBones.Spine;

        [Tooltip("Substring hints used when auto-resolving the back socket on a non-humanoid rig " +
                 "(case-insensitive). The first child Transform whose name contains any of these wins.")]
        [SerializeField] private string[] backBoneNameHints = { "Spine", "Chest", "Torso" };

        [Tooltip("Manual override, used only when auto-resolve fails.")]
        [SerializeField] private Transform backSocketOverride;

        [SerializeField] private Vector3 wornLocalPosition = new(0f, 0.12f, -0.18f);
        [SerializeField] private Vector3 wornLocalEuler = new(0f, 0f, 0f);

        [Header("Deploy")]
        [Tooltip("What the drop point is measured from. Left empty it resolves to the player camera, " +
                 "which is what you want in first person: the body and the view can disagree for a " +
                 "frame after a fast turn, and the pack must land where the player is LOOKING.")]
        [SerializeField] private Transform aimTransform;

        [SerializeField, Min(0.05f)] private float deploySeconds = 0.9f;
        [Tooltip("Metres in front of the player the pack is set down.")]
        [SerializeField, Min(0.2f)] private float deployDistance = 0.9f;
        [SerializeField] private float arcHeight = 0.6f;
        [Tooltip("Metres the pack bows sideways mid-flight so it clears the player's own body.")]
        [SerializeField] private float arcOutward = 0.35f;

        [Tooltip("Metres the pack is lifted off the ground hit point along the surface normal. The " +
                 "field backpack's origin is already at the bottom centre of its footprint and it " +
                 "stands upright, so this only has to clear z-fighting with the sand. A mesh whose " +
                 "origin sits at its centre would need half its height here.")]
        [SerializeField] private float groundLift = 0.01f;

        [SerializeField] private LayerMask groundMask = ~0;

        public State CurrentState { get; private set; } = State.Shouldered;
        public BackpackObject Pack { get; private set; }

        private PlayerInputManager input;
        private Transform backSocket;
        private Coroutine arcRoutine;

        // Sized for the clutter a 3 m downward probe plausibly crosses on the way to the sand: the
        // player's own capsule, their pack, a doorway lip, then ground.
        private readonly RaycastHit[] groundHits = new RaycastHit[12];

        private void Awake()
        {
            input = GetComponent<PlayerInputManager>();

            if (aimTransform == null)
            {
                var cam = GetComponentInChildren<Camera>(true);
                aimTransform = cam != null ? cam.transform : transform;
            }

            backSocket = ResolveBackSocket();
            if (backSocket == null)
            {
                Debug.LogError("BackpackController: could not resolve a back socket. Assign " +
                               "backSocketOverride or add hints in backBoneNameHints.", this);
                return;
            }

            if (backpackPrefab == null)
            {
                Debug.LogError("BackpackController: no backpack prefab assigned.", this);
                return;
            }

            GameObject instance = Instantiate(backpackPrefab, backSocket);
            Pack = instance.GetComponent<BackpackObject>();

            if (Pack == null)
            {
                Debug.LogError("BackpackController: backpack prefab has no BackpackObject.", this);
                Destroy(instance);
                return;
            }

            Pack.Bind(this);
            SnapToWorn();
        }

        /// <summary>
        /// Mirrors EquipmentController.ResolveHandSocket: the armature bone is always preferred, and
        /// the serialized Transform is only a manual override for rigs the resolver cannot handle.
        /// </summary>
        private Transform ResolveBackSocket()
        {
            var animator = GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman)
            {
                Transform bone = animator.GetBoneTransform(backBone);
                if (bone != null) return bone;
            }

            if (backBoneNameHints != null)
            {
                var all = GetComponentsInChildren<Transform>(true);
                foreach (Transform candidate in all)
                {
                    foreach (string hint in backBoneNameHints)
                    {
                        if (string.IsNullOrEmpty(hint)) continue;
                        if (candidate.name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                            return candidate;
                    }
                }
            }

            return backSocketOverride;
        }

        private void OnEnable()
        {
            if (input != null) input.OnBackpackPressed += Toggle;
        }

        private void OnDisable()
        {
            if (input != null) input.OnBackpackPressed -= Toggle;

            // A coroutine dies with the component. Without this the pack is left hanging in mid-air,
            // unparented, halfway through an arc — which survives a scene reload as a floating pack.
            if (arcRoutine != null)
            {
                StopCoroutine(arcRoutine);
                arcRoutine = null;

                if (CurrentState == State.Deploying) FinishDeploy(CurrentWorldPose(Pack));
                else if (CurrentState == State.Stowing) SnapToWorn();
            }
        }

        public void Toggle()
        {
            switch (CurrentState)
            {
                case State.Shouldered: Deploy(); break;
                case State.Open: Reshoulder(); break;
                // Mid-flight. Swallowing the press is what makes the key safe to mash — restarting an
                // arc from a half-travelled pose would fling it somewhere neither end expects.
                default: break;
            }
        }

        public void Deploy()
        {
            if (CurrentState != State.Shouldered || Pack == null) return;

            if (!TryFindGroundPose(out Pose grounded))
            {
                Debug.Log("Backpack: no ground to set it down on.", this);
                return;
            }

            Pose start = CurrentWorldPose(Pack);

            CurrentState = State.Deploying;
            Pack.SetWorn(false);
            Pack.transform.SetParent(null, true);

            arcRoutine = StartCoroutine(RunArc(start, () => grounded, () => FinishDeploy(grounded)));
        }

        public void Reshoulder()
        {
            if (CurrentState != State.Open || Pack == null) return;

            Pose start = CurrentWorldPose(Pack);

            CurrentState = State.Stowing;

            // Closes over the first third of the flight rather than before it. Waiting for the lid
            // would put a visible pause between the interaction and the pack moving.
            Pack.SetOpen(false);

            // The target is recomputed every frame instead of captured: re-shouldering is allowed
            // from across the map, and the player is usually walking while it flies back.
            arcRoutine = StartCoroutine(RunArc(start, WornWorldPose, SnapToWorn));
        }

        private IEnumerator RunArc(Pose start, Func<Pose> end, Action onArrive)
        {
            for (float elapsed = 0f; elapsed < deploySeconds; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / deploySeconds);
                Pose pose = BackpackDeployArc.Evaluate(start, end(), t, arcHeight, arcOutward);
                Pack.transform.SetPositionAndRotation(pose.position, pose.rotation);
                yield return null;
            }

            arcRoutine = null;
            onArrive();
        }

        private void FinishDeploy(Pose grounded)
        {
            Pack.transform.SetParent(null, true);
            Pack.transform.SetPositionAndRotation(grounded.position, grounded.rotation);
            Pack.SetWorn(false);
            CurrentState = State.Open;
            Pack.SetOpen(true);
        }

        private void SnapToWorn()
        {
            Pack.SetOpen(false);
            Pack.SetWorn(true);
            Pack.transform.SetParent(backSocket, false);
            Pack.transform.SetLocalPositionAndRotation(wornLocalPosition, Quaternion.Euler(wornLocalEuler));
            CurrentState = State.Shouldered;
        }

        private Pose WornWorldPose()
        {
            Quaternion rotation = backSocket.rotation * Quaternion.Euler(wornLocalEuler);
            return new Pose(backSocket.TransformPoint(wornLocalPosition), rotation);
        }

        private static Pose CurrentWorldPose(BackpackObject pack) =>
            new(pack.transform.position, pack.transform.rotation);

        /// <summary>
        /// Where the player is looking, flattened to the ground plane. Falls back to the body's
        /// facing when the view is straight up or down, where the horizontal component vanishes.
        /// </summary>
        private Vector3 AimForward()
        {
            Vector3 forward = aimTransform != null ? aimTransform.forward : transform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = transform.forward;
                forward.y = 0f;
            }

            return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        }

        /// <summary>
        /// Where the pack lands. The probe has to ignore the player's own hierarchy: the capsule sits
        /// on the Default layer, so no layer mask excludes it, and a plain Physics.Raycast started
        /// above head height hits the player's own collider and drops the pack at chest level. The
        /// same trap is documented at length in Interactor.DoInteractionTest.
        /// </summary>
        private bool TryFindGroundPose(out Pose pose)
        {
            pose = default;

            Vector3 ahead = transform.position + AimForward() * deployDistance;

            // Started above the player's eyeline, not 1 m up. On a rise or a step the ground in front
            // can sit higher than the player's own feet, and a short probe silently finds nothing —
            // which reads to the player as "the key does nothing" while the pack stays on their back.
            Vector3 origin = ahead + Vector3.up * 2f;

            int count = Physics.RaycastNonAlloc(new Ray(origin, Vector3.down), groundHits,
                                                6f, groundMask, QueryTriggerInteraction.Ignore);
            if (count == 0) return false;

            Transform root = transform.root;
            var best = new RaycastHit();
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                Transform hit = groundHits[i].transform;
                if (hit == null) continue;
                if (hit.IsChildOf(root)) continue;                                  // the player
                if (Pack != null && hit.IsChildOf(Pack.transform)) continue;        // the pack itself

                if (!found || groundHits[i].distance < best.distance)
                {
                    best = groundHits[i];
                    found = true;
                }
            }

            if (!found) return false;

            // The pack STANDS UP where it lands, doors toward the player. Its local +Y is the height
            // axis and its local +Z is the door side — the frame that rides against the wearer's
            // back is on -Z — so pointing local +Z at the player is what puts the opening interior
            // in front of them rather than showing them the back of a cabinet.
            Vector3 toPlayer = Vector3.ProjectOnPlane(transform.position - ahead, best.normal);
            if (toPlayer.sqrMagnitude < 1e-6f)
                toPlayer = Vector3.ProjectOnPlane(-AimForward(), best.normal);

            pose = new Pose(best.point + best.normal * groundLift,
                            Quaternion.LookRotation(toPlayer.normalized, best.normal));
            return true;
        }
    }
}
