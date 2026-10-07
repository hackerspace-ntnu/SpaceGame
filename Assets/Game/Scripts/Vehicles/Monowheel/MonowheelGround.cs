// Assets/Game/Scripts/Vehicles/Monowheel/MonowheelGround.cs
using UnityEngine;

namespace SpaceGame.Vehicles.Monowheel
{
    /// <summary>
    /// The one ground probe the monowheel's presentation and its chassis pose share. Cast through
    /// the object's OWN physics scene, so the builder's preview-scene check, the EditMode tests and
    /// a live world take the same path; the vehicle's own colliders are skipped by hierarchy rather
    /// than excluded by layer, so no layer has to be reserved for them.
    /// </summary>
    public static class MonowheelGround
    {
        /// <summary>The nearest hit along the ray that is not under <paramref name="self"/>.</summary>
        public static bool TryHit(GameObject owner, Transform self, Vector3 origin, Vector3 direction, float distance,
                                  LayerMask layers, RaycastHit[] buffer, out RaycastHit nearest)
        {
            int n = owner.scene.GetPhysicsScene().Raycast(origin, direction, buffer, distance, layers,
                                                          QueryTriggerInteraction.Ignore);
            nearest = default;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (buffer[i].collider.transform.IsChildOf(self)) continue;
                if (found && buffer[i].distance >= nearest.distance) continue;
                nearest = buffer[i];
                found = true;
            }
            return found;
        }
    }
}
