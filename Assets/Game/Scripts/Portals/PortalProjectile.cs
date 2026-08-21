// The blob of fluid thrown at the wall.
//
// It is pure presentation and knows it. Where the aperture opens was settled by
// the shooter before this existed and travelled in the use message, so the blob
// never raycasts, never collides, and cannot miss — it flies to a point it was
// handed and reports arrival. That is what keeps every machine's portal in the
// same place: if the blob decided anything, two machines simulating it a frame
// apart would decide differently.
//
// It also deliberately has no NetworkObject. One is instantiated per machine
// from Present(), which is both cheaper than replicating a projectile and
// immune to the failure mode where an unregistered network prefab works on the
// host and throws on every client (see project memory on network prefab rules).
using System;
using UnityEngine;

namespace SpaceGame.Portals
{
    [DisallowMultipleComponent]
    public sealed class PortalProjectile : MonoBehaviour
    {
        [Header("Flight")]
        [Tooltip("Metres per second. Fast enough to feel hitscan, slow enough to see leave the horn.")]
        [SerializeField] private float speed = 62f;

        [Tooltip("Sideways bow on the path, in metres at mid-flight. A dead-straight line reads as a laser rather than as something thrown.")]
        [SerializeField] private float arc = 0.18f;

        [Tooltip("Destroyed after this long no matter what, so a blob whose callback never fires cannot leak.")]
        [SerializeField] private float maxLifetime = 4f;

        [Header("Parts")]
        [SerializeField] private Renderer blobRenderer;
        [SerializeField] private Light blobLight;

        [Tooltip("Spawned at the impact point on arrival. Optional.")]
        [SerializeField] private GameObject impactEffect;

        [Tooltip("How long the impact effect is left alive.")]
        [SerializeField] private float impactLifetime = 1.5f;

        private static readonly int DeepId = Shader.PropertyToID("_ColourDeep");
        private static readonly int BrightId = Shader.PropertyToID("_ColourBright");

        private Vector3 from;
        private Vector3 to;
        private Vector3 bow;
        private float travelled;
        private float distance;
        private float age;
        private Action onArrive;
        private bool launched;

        /// <summary>
        /// Send the blob from <paramref name="origin"/> to <paramref name="target"/>.
        ///
        /// <paramref name="arrived"/> runs once, on arrival, on this machine only —
        /// it is the local half of opening an aperture every machine already
        /// agreed on.
        /// </summary>
        public void Launch(Vector3 origin, Vector3 target, Color colour, Action arrived)
        {
            from = origin;
            to = target;
            onArrive = arrived;
            distance = Vector3.Distance(origin, target);
            travelled = 0f;
            age = 0f;
            launched = true;

            // Bow the path across whichever axis is most perpendicular to it, so
            // the arc is visible from wherever the shooter happens to stand.
            Vector3 direction = distance > 1e-4f ? (target - origin) / distance : Vector3.forward;
            Vector3 side = Vector3.Cross(direction, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(direction, Vector3.forward);
            bow = side.normalized * arc;

            Tint(colour);
            transform.position = origin;
        }

        private void Tint(Color colour)
        {
            if (blobLight != null) blobLight.color = colour;
            if (blobRenderer == null) return;

            var block = new MaterialPropertyBlock();
            blobRenderer.GetPropertyBlock(block);
            block.SetColor(DeepId, colour * 0.55f);
            block.SetColor(BrightId, Color.Lerp(colour, Color.white, 0.45f));
            blobRenderer.SetPropertyBlock(block);
        }

        private void Update()
        {
            age += Time.deltaTime;

            if (!launched || age > maxLifetime)
            {
                if (age > maxLifetime) Destroy(gameObject);
                return;
            }

            travelled += speed * Time.deltaTime;
            float t = distance > 1e-4f ? Mathf.Clamp01(travelled / distance) : 1f;

            // Sine bow: zero at both ends, so the blob leaves the horn and
            // arrives at the wall exactly where it was told to.
            transform.position = Vector3.Lerp(from, to, t) + bow * Mathf.Sin(t * Mathf.PI);

            if (t < 1f) return;

            Arrive();
        }

        private void Arrive()
        {
            launched = false;

            if (impactEffect != null)
            {
                GameObject effect = Instantiate(impactEffect, to, transform.rotation);
                Destroy(effect, impactLifetime);
            }

            Action callback = onArrive;
            onArrive = null;
            callback?.Invoke();

            Destroy(gameObject);
        }
    }
}
