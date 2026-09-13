// Put a thing down on the ground. The ordinary placeable: a lantern, a crate, a bedroll.
//
// Criteria: ground flat enough to stand on.
// Logic:    spawn the placed prefab there, facing the way the placer was looking.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    public class GroundPlacement : PlacementRule
    {
        [Tooltip("What ends up on the ground. MUST be a registered network prefab, and its " +
                 "PlacedObject must return the item that placed it — otherwise placing and " +
                 "picking up either transmutes the item or loses it.")]
        [SerializeField] private GameObject placedPrefab;

        [Tooltip("Steepest ground it will stand on, in degrees. Anything sharper and it sits " +
                 "half-buried in a dune face.")]
        [SerializeField] private float maxGroundAngle = 35f;

        [Tooltip("Face it away from the placer rather than keeping the prefab's own rotation. " +
                 "What you want for anything with a front — a chair, a workbench, a sign.")]
        [SerializeField] private bool faceAwayFromPlacer = true;

        public override bool CanPlace(in PlacementAim aim)
        {
            if (placedPrefab == null || !aim.IsValid) return false;

            // Zero normal means the aim came off the wire, which does not carry one. The owner
            // already tested the slope with a real normal; re-testing against a fabricated
            // straight-up one would pass everything and is worse than not testing.
            if (aim.Normal == Vector3.zero) return true;

            return Vector3.Angle(aim.Normal, Vector3.up) <= maxGroundAngle;
        }

        public override bool Place(in PlacementAim aim, GameObject placer)
        {
            if (!CanPlace(aim)) return false;

            Quaternion rotation = faceAwayFromPlacer
                ? Quaternion.Euler(0f, aim.Yaw, 0f)
                : Quaternion.identity;

            return GameServices.World.Spawn(placedPrefab, aim.Point, rotation) != null;
        }

        public override string RefusalHint(in PlacementAim aim) => "Too steep";

        private void OnValidate() => maxGroundAngle = Mathf.Clamp(maxGroundAngle, 0f, 89f);
    }
}
