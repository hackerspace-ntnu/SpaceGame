using UnityEngine;

namespace SpaceGame.Characters
{
    public class AimProvider : MonoBehaviour
    {

        [SerializeField] private Camera playerCamera;

        /// <summary>
        /// The transform every aim in the game is measured from, or null if none is wired. Exposed
        /// so other systems can agree with the aim rather than hunting for a camera of their own —
        /// GetComponentInChildren&lt;Camera&gt; finds inactive and secondary cameras too, and a
        /// system that picks the wrong one silently disagrees with where the player is pointing.
        /// </summary>
        public Transform AimTransform => playerCamera != null ? playerCamera.transform : null;

        public Ray GetAimRay()
        {
            return new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        }

        public RaycastHit? GetRayCast(float maxDistance = 100f)
        {
            Ray ray = GetAimRay();
            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                return hit;
            }
            Debug.LogWarning("Raycast did not hit anything.");
            return null;
        }
    }
}
