// Drives a vehicle's ArticulatedParts off the mount lifecycle: deploy the flight surfaces when
// a pilot takes the controls, stow them again when they leave, and shut the doors in between.
//
// On ShipRV this is what swings the two engine pylons down into flight position when you take
// the wheel, and folds them back up over the hull when you get out.
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Characters;

namespace SpaceGame.Vehicles
{
    [RequireComponent(typeof(MountModule))]
    public class VehicleDeploymentController : MonoBehaviour
    {
        [SerializeField] private MountModule mountModule;

        [Header("On Mount")]
        [Tooltip("Parts driven to their open pose when a pilot mounts — engine pylons, landing gear, wings.")]
        [SerializeField] private ArticulatedPart[] deployOnMount;

        [Tooltip("Parts driven closed when a pilot mounts — doors and hatches that shouldn't fly open.")]
        [SerializeField] private ArticulatedPart[] closeOnMount;

        [Header("On Dismount")]
        [Tooltip("Return the deployed parts to their stowed pose when the pilot leaves.")]
        [SerializeField] private bool retractOnDismount = true;

        private void Awake()
        {
            if (!mountModule)
                mountModule = GetComponent<MountModule>();
        }

        private void OnEnable()
        {
            if (!mountModule)
                return;

            mountModule.Mounted += HandleMounted;
            mountModule.Dismounted += HandleDismounted;

            // Late enable (module toggled off and back on) with a pilot already aboard.
            if (mountModule.IsMounted)
                HandleMounted(mountModule.MountedPlayerMovement);
        }

        private void OnDisable()
        {
            if (!mountModule)
                return;

            mountModule.Mounted -= HandleMounted;
            mountModule.Dismounted -= HandleDismounted;
        }

        private void HandleMounted(PlayerMovement _)
        {
            SetAll(deployOnMount, true);
            SetAll(closeOnMount, false);
        }

        private void HandleDismounted(PlayerMovement _)
        {
            if (retractOnDismount)
                SetAll(deployOnMount, false);
        }

        private static void SetAll(ArticulatedPart[] parts, bool open)
        {
            if (parts == null)
                return;
            foreach (ArticulatedPart part in parts)
                if (part)
                    part.SetOpen(open);
        }
    }
}
