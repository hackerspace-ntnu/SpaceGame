// A single, deliberate place you can climb aboard a vehicle from: a steering wheel, a yoke,
// a pilot's seat. Interacting with this mounts the player into the vehicle's MountModule.
//
// Why not just interact with the vehicle itself? Interactor resolves IInteractable by walking
// up from the collider it hit, so on a big vehicle every hull collider would otherwise resolve
// to the root MountModule and let players mount by looking at the outside of the fuselage.
// Pair this with MountModule.mountableByDirectInteraction = false to make the cockpit control
// the only way in.
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class MountStation : MonoBehaviour, IInteractable
{
    [Tooltip("Vehicle this station drives. Auto-resolved from the parents if left empty.")]
    [SerializeField] private MountModule mount;

    private void Awake()
    {
        if (!mount)
            mount = GetComponentInParent<MountModule>();

        if (!mount)
            Debug.LogWarning($"[MountStation] No MountModule found above {name}; this control does nothing.", this);
    }

    public bool CanInteract() => mount && mount.IsAvailableForMount;

    // Calls TryMount directly rather than mount.Interact() so the station keeps working even
    // when the vehicle's own direct-interaction surface is switched off.
    public void Interact(Interactor interactor)
    {
        if (!mount)
            return;
        mount.TryMount(interactor, null);
    }
}
