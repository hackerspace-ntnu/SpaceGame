using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Core;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Handles raycasting for object interactions
    /// Sends out a raycast when a button is pressed to detect interactable objects
    /// </summary>
    public class Interactor : MonoBehaviour
    {
        [SerializeField]
        private float _castDistance = 5f;

        [SerializeField] private Transform lookTransform;
    
    
        public bool IsHoveringInteractable { get; private set; }

        /// <summary>
        /// The eye the E key looks down, and how far it reaches.
        ///
        /// <para>
        /// Exposed for things the crosshair points at that are not <see cref="IInteractable"/> —
        /// the ship's inventory wall, whose verb changes per cell and so cannot be one. They have
        /// to cast the SAME ray this does, or the player aims at a cell with one control and
        /// presses E into another.
        /// </para>
        /// <para>
        /// Degenerate — origin, zero direction — with no look transform wired, which every caller
        /// must test rather than assume: this component is on a camera rig that a mount disables.
        /// </para>
        /// </summary>
        public Ray LookRay => lookTransform != null
            ? new Ray(lookTransform.position, lookTransform.forward)
            : new Ray(Vector3.zero, Vector3.zero);

        /// <summary>How far <see cref="LookRay"/> reaches — the E key's own range.</summary>
        public float CastDistance => _castDistance;

        /// <summary>
        /// What the crosshair is on right now, or null. Same resolution the E key uses, so the HUD
        /// can never describe one control while the key works another.
        /// </summary>
        public IInteractable HoveredInteractable { get; private set; }

        /// <summary>
        /// The collider the ray actually met to reach <see cref="HoveredInteractable"/>, or null.
        ///
        /// <para>
        /// Published because the interactable alone does not say WHERE the player is pointing, and
        /// a HUD that re-derives that from the component's own hierarchy gets a different answer:
        /// an interactable resolved off a parent names the whole hull, and one whose collider is a
        /// trigger standing proud of a fixture names air. This is the hit that was arbitrated, so
        /// anything drawing on the target draws on the same place the press will act.
        /// </para>
        /// </summary>
        public Collider HoveredCollider { get; private set; }

        /// <summary>Where the look ray met <see cref="HoveredCollider"/>. Meaningless with no hover.</summary>
        public Vector3 HoveredPoint { get; private set; }

        [Header("Debug")]
        [SerializeField] private bool _debugRay = true;
        private Color hitNormalColor = Color.blue;
         private Color hitInteractableColor = Color.green;
         private Color missColor = Color.red;

         private RaycastHit hitInfo;
        private bool rayCastHit;

        // Sized for the worst case a look-ray plausibly crosses: a deck's carry volume, its hull
        // boxes, rails and a control. Overflow only costs the furthest hits, which are behind
        // whatever the player is looking at anyway.
        private readonly RaycastHit[] rayHits = new RaycastHit[16];

        private static readonly DistanceComparer ByDistance = new DistanceComparer();

        private class DistanceComparer : IComparer<RaycastHit>
        {
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        private void Start()
        {
            PlayerInputManager input = GetComponent<PlayerController>().Input;
            input.OnInteractPressed += Interact;
            input.OnUsePressed += SecondaryInteract;
        }

        /// <summary>
        /// Hover state must not outlive the component that maintains it.
        ///
        /// Mounting disables this Interactor (MountModule.DisableRiderComponentsForMount), which
        /// stops Update — and the hover fields then FREEZE at whatever the player was looking at
        /// when they pressed the key, which is by definition the thing they just mounted. Both HUD
        /// readers poll those fields every frame regardless, so the interaction panel sat on screen
        /// reading "Press E" for the entire ride and the crosshair stayed lit with it. Neither could
        /// tell the difference between "still hovering" and "no longer being asked".
        /// </summary>
        private void OnDisable() => ClearHoverState();

        /// <summary>
        /// Stop reporting a hover. Called on disable; public so the state can be dropped by anything
        /// that takes the player's attention away without tearing this component down.
        /// </summary>
        public void ClearHoverState()
        {
            IsHoveringInteractable = false;
            HoveredInteractable = null;
            HoveredCollider = null;
        }

        private void Update()
        {
            if (!DoInteractionTest(out IInteractable interactable))
            {
                ClearHoverState();
                return;
            }

            if (interactable is Behaviour behaviour && !behaviour.isActiveAndEnabled)
            {
                ClearHoverState();
                return;
            }

            // Match the crosshair to what pressing Interact would actually do. Without the
            // CanInteract test the crosshair lights up over things that refuse the interaction —
            // e.g. a whole vehicle hull whose root MountModule is the nearest IInteractable.
            IsHoveringInteractable = IsAvailable(interactable);
            HoveredInteractable = IsHoveringInteractable ? interactable : null;

            // The arbitrated hit, kept beside the interactable it answered with. Cleared with it,
            // so a reader can never pair a live hover with a stale point.
            HoveredCollider = IsHoveringInteractable ? hitInfo.collider : null;
            HoveredPoint = hitInfo.point;
        }

        private void Interact()
        {
            if (!DoInteractionTest(out IInteractable interactable)) return;

            if (interactable is Behaviour behaviour && !behaviour.isActiveAndEnabled) return;
            if (!IsAvailable(interactable)) return;
            interactable.Interact(this);

        }

        /// <summary>
        /// Whether this interactor may use this interactable right now.
        ///
        /// Two questions, and they are different: <see cref="IInteractable.CanInteract"/> asks
        /// whether the thing works at all, and <see cref="IContextualInteractable"/> asks whether
        /// it works for the person looking at it. Both are asked in the same place so the
        /// crosshair and the key can never disagree — a prompt that lights up and then refuses the
        /// press is the failure this method exists to prevent.
        /// </summary>
        private bool IsAvailable(IInteractable interactable)
        {
            if (interactable == null || !interactable.CanInteract()) return false;
            return interactable is not IContextualInteractable contextual
                   || contextual.CanInteract(this);
        }

        /// <summary>
        /// Use, on whatever the crosshair is on. Only reaches things that opt in by implementing
        /// <see cref="ISecondaryInteractable"/>, so looking at an ordinary interactable and
        /// clicking still falls through to the weapon.
        /// </summary>
        private void SecondaryInteract()
        {
            if (!DoInteractionTest(out IInteractable interactable)) return;
            if (interactable is not ISecondaryInteractable secondary) return;

            if (interactable is Behaviour behaviour && !behaviour.isActiveAndEnabled) return;
            if (interactable is IContextualInteractable contextual && !contextual.CanInteract(this))
                return;
            if (!secondary.CanSecondaryInteract()) return;
            secondary.SecondaryInteract(this);
        }

        private bool DoInteractionTest(out IInteractable interactable)
        {
            interactable = null;

            Vector3 origin = lookTransform.position;
            Vector3 direction = lookTransform.forward;

            int layerMask = ~LayerMask.GetMask("Player");

            Ray ray = new Ray(origin, direction);
            int count = Physics.RaycastNonAlloc(ray, rayHits, _castDistance, layerMask);

            rayCastHit = count > 0;
            if (!rayCastHit) return false;

            // ResolveAlongRay sorts the slice, so hits[0] is the nearest thing the gizmo should draw
            // when nothing interactable was found.
            // Skip our own body. The player's capsule is on the Default layer, not "Player", so the
            // mask above does not exclude it, and the camera sits just inside the top of a 3 m
            // capsule — lean or look down and the eye pokes out through its own collider, which then
            // blocks every interaction as if a wall were in the way. Intermittent and maddening.
            bool found = ResolveAlongRay(rayHits, count, out interactable, out hitInfo, transform.root);
            if (!found && hitInfo.collider == null) hitInfo = rayHits[0];
            return found;
        }

        /// <summary>
        /// Walk the hits front to back and decide what the player is actually looking at.
        ///
        /// Written as a static over a hit list rather than a single Physics.Raycast because of what
        /// a *trigger* means here. A trigger is a detection volume, not a surface: a vehicle's carry
        /// volume exists to notice riders and is deliberately drawn around the whole deck. The old
        /// single-raycast version hit that volume first and then walked UP to the hull root for an
        /// IInteractable — so on the dune foiler the carry volume answered for every rope station
        /// inside it, and on the crawler the gantry volume answered with the hull's MountModule.
        /// Standing on either deck, nothing on it could be used; mounting was the only thing that
        /// ever responded, because mounting was the thing being wrongly offered.
        ///
        /// So a trigger only counts when it carries the interactable ITSELF. It never inherits one
        /// from a parent, and the ray passes straight through it to whatever is really there. A
        /// trigger that IS a control still works: the crawler's DOOR_MountStation holds its
        /// MountStation on the very GameObject the trigger is on.
        ///
        /// The one exception is <see cref="InteractionBlocker"/>: a trigger that stands for
        /// something you can see through and cannot reach through. It offers nothing and stops the
        /// ray, which is how a hull with a deliberate hole in its collision — the PlayerShip's
        /// canopy — keeps what is behind it out of reach.
        /// </summary>
        /// <param name="ignoreRoot">
        /// Hierarchy to treat as invisible — the interacting player's own body. Optional so tests
        /// can resolve a ray without building a player.
        /// </param>
        public static bool ResolveAlongRay(RaycastHit[] hits, int count,
                                           out IInteractable interactable, out RaycastHit chosen,
                                           Transform ignoreRoot = null)
        {
            interactable = null;
            chosen = default;

            // RaycastNonAlloc does not promise any order, so sort the slice we filled.
            System.Array.Sort(hits, 0, count, ByDistance);

            for (int index = 0; index < count; index++)
            {
                Collider collider = hits[index].collider;
                if (collider == null) continue;
                if (ignoreRoot != null && collider.transform.IsChildOf(ignoreRoot)) continue;

                if (collider.isTrigger)
                {
                    // Only a trigger that is itself a control answers; everything else is see-through.
                    IInteractable own = collider.GetComponent<IInteractable>();
                    if (own == null)
                    {
                        // Unless it is glass. See-through also meant reach-through, and a hull is
                        // only as opaque as its collision: the PlayerShip's canopy dome carries
                        // none on purpose, so the four cockpit chairs' own trigger volumes were the
                        // first thing an outside ray met and the ship was boardable from the air
                        // above it. An InteractionBlocker is a trigger that stops the ray without
                        // offering anything — solid to the hand, invisible to physics.
                        if (collider.GetComponent<InteractionBlocker>() == null) continue;
                        chosen = hits[index];
                        return false;
                    }

                    interactable = own;
                    chosen = hits[index];
                    return true;
                }

                IInteractable target = collider.GetComponent<IInteractable>()
                                       ?? collider.GetComponentInParent<IInteractable>();
                chosen = hits[index];
                if (target == null) return false;   // solid and inert: it blocks the line of sight

                interactable = target;
                return true;
            }

            return false;
        }
    
        private void OnDrawGizmos()
        {
            if (!_debugRay)
                return;
        
            Vector3 origin = lookTransform.position;
            Vector3 direction = lookTransform.forward;
            Ray ray = new Ray(origin, direction);
        
            Vector3 end = ray.origin + ray.direction * _castDistance;

            if (rayCastHit && hitInfo.collider != null)
            {
                IInteractable interactable = hitInfo.collider.GetComponent<IInteractable>();
                if (interactable != null)
                {
                    Gizmos.color = hitInteractableColor;
                }
                else
                {
                    Gizmos.color = hitNormalColor;
                }

                Gizmos.DrawSphere(hitInfo.point, 0.03f);
                Gizmos.DrawLine(origin, hitInfo.point);
            }
            else
            {
                Gizmos.color = missColor;
                Gizmos.DrawLine(origin, end);
            }
        }

    }
}
