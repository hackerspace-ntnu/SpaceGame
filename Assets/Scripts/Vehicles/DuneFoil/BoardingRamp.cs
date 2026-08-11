using UnityEngine;

namespace SpaceGame.Vehicles.DuneFoil
{
    /// <summary>
    /// Puts the gangway down when the craft is parked and stows it once it lifts onto the foil.
    ///
    /// The deck is only reachable while the hull is resting on the sand; at speed it is thirteen
    /// metres up and a ramp would be a plank dangling in the air. Tying it to ride height means
    /// the way aboard appears exactly when boarding is possible, and disappears when it is not.
    /// </summary>
    [ExecuteAlways]
    public class BoardingRamp : MonoBehaviour
    {
        [Tooltip("Ride height source. Found on the craft root when empty.")]
        [SerializeField] private FoilLift foil;

        [Tooltip("The ramp geometry and its collider.")]
        [SerializeField] private GameObject ramp;

        [Tooltip("Ride height, as a fraction of the strut, above which the ramp stows. Small: " +
                 "the moment the hull is clear of the sand there is nothing to step onto.")]
        [SerializeField, Range(0f, 1f)] private float stowAboveRideHeight = 0.05f;

        /// <summary>Whether the gangway is currently down.</summary>
        public bool IsDown => ramp != null && ramp.activeSelf;

        private void Awake()
        {
            if (foil == null) foil = GetComponentInParent<FoilLift>();
        }

        private void OnEnable() => Refresh();

        private void Update() => Refresh();

        private void Refresh()
        {
            if (ramp == null) return;
            if (foil == null) foil = GetComponentInParent<FoilLift>();

            // No foil to ask means the craft is not moving under its own power — leave it down
            // so the prefab is boardable when dropped into a scene as scenery.
            bool down = foil == null || foil.RideHeight01 <= stowAboveRideHeight;
            if (ramp.activeSelf != down) ramp.SetActive(down);
        }

        /// <summary>Wire the ramp up. Used by the prefab builder.</summary>
        public void Bind(FoilLift lift, GameObject rampObject)
        {
            foil = lift;
            ramp = rampObject;
        }
    }
}
