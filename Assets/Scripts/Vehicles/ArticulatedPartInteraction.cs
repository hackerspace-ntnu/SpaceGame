// Player-facing switch for one or more ArticulatedParts: look at it, press Interact, it toggles.
// Put this on the part's pivot (or on any GameObject carrying the part's collider) — Interactor
// resolves IInteractable via GetComponent then GetComponentInParent, so a collider anywhere under
// the pivot finds it.
//
// Driving several parts from one switch covers double doors: both leaves answer to a single panel.
using UnityEngine;

public class ArticulatedPartInteraction : MonoBehaviour, IInteractable
{
    [Tooltip("Parts this switch drives. Leave empty to use the ArticulatedPart on this GameObject.")]
    [SerializeField] private ArticulatedPart[] parts;

    [Tooltip("Ignore interaction while any driven part is still moving, so it can't be stuttered mid-swing.")]
    [SerializeField] private bool blockWhileMoving = true;

    [Header("Mount Lock")]
    [Tooltip("Refuse to open/close while someone is piloting the vehicle — no dropping the ramp mid-flight.")]
    [SerializeField] private bool lockedWhileMounted = true;

    [Tooltip("Vehicle this door belongs to. Auto-resolved from the parents if left empty.")]
    [SerializeField] private MountModule mountLock;

    private void Awake()
    {
        if (parts == null || parts.Length == 0)
        {
            ArticulatedPart own = GetComponent<ArticulatedPart>();
            parts = own ? new[] { own } : new ArticulatedPart[0];
        }

        if (!mountLock)
            mountLock = GetComponentInParent<MountModule>();
    }

    public bool CanInteract()
    {
        if (parts == null || parts.Length == 0)
            return false;

        if (lockedWhileMounted && mountLock && mountLock.IsMounted)
            return false;

        if (blockWhileMoving)
        {
            foreach (ArticulatedPart part in parts)
                if (part && part.IsMoving)
                    return false;
        }

        return true;
    }

    public void Interact(Interactor interactor)
    {
        if (!CanInteract())
            return;

        // Mixed states resolve toward "close everything" — one press always leaves the
        // group in a single, predictable state.
        bool anyOpen = false;
        foreach (ArticulatedPart part in parts)
            if (part && part.IsOpen)
                anyOpen = true;

        foreach (ArticulatedPart part in parts)
            if (part)
                part.SetOpen(!anyOpen);
    }
}
