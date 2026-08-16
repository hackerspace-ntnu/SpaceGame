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
        /// What the crosshair is on right now, or null. Same resolution the E key uses, so the HUD
        /// can never describe one control while the key works another.
        /// </summary>
        public IInteractable HoveredInteractable { get; private set; }

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

        private void Update()
        {
            if (!DoInteractionTest(out IInteractable interactable))
            {
                IsHoveringInteractable = false;
                HoveredInteractable = null;
                return;
            }

            if (interactable is Behaviour behaviour && !behaviour.isActiveAndEnabled)
            {
                IsHoveringInteractable = false;
                HoveredInteractable = null;
                return;
            }

            // Match the crosshair to what pressing Interact would actually do. Without the
            // CanInteract test the crosshair lights up over things that refuse the interaction —
            // e.g. a whole vehicle hull whose root MountModule is the nearest IInteractable.
            IsHoveringInteractable = IsAvailable(interactable);
            HoveredInteractable = IsHoveringInteractable ? interactable : null;
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
                    if (own == null) continue;
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
