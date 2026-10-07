// A coat of paint left where the stream landed.
//
// One quad. Everything that makes it look like a splash — the lobes flying outward, the drips
// running down, the wet sheen, the drying — happens in PortalSplat.shader, on the splat's own
// clock. This class only decides where the quad goes and hands the shader the four things it cannot
// work out for itself: which way is down ON THIS SURFACE, what colour the paint is, how big the
// splash is, and a seed.
//
// IT IS DELIBERATELY BRIEF — half a second, start to gone. The paint that makes the APERTURE
// stays, because that is the thing the player made; the paint that merely spattered the ground
// around it is exhaust, and exhaust that lingers turns a wall into a mess and buries the portal in
// its own splatter. Long-lived decals were tried at eight seconds and a two-second sweep left
// thirty overlapping coats still drying around a portal you were trying to look at.
//
// The gravity projection is the part worth reading. The shader drips along a direction in the
// quad's own 2D space, so the same shader runs down a wall, spreads flat on a floor and hangs off a
// ceiling without ever being told which of those it is on — a floor simply projects gravity to
// nearly nothing and the drip length collapses to zero on its own.
using UnityEngine;

namespace SpaceGame.Portals
{
    [DisallowMultipleComponent]
    public sealed class PortalSplat : MonoBehaviour
    {
        [SerializeField] private Renderer quad;

        [Tooltip("How far off the surface the quad floats, so it does not z-fight with it.")]
        [SerializeField] private float surfaceOffset = 0.015f;

        [Tooltip("Seconds before the splat is gone. Must match the shader's Life.")]
        [SerializeField] private float life = 0.5f;

        private static readonly int ColourId  = Shader.PropertyToID("_Colour");
        private static readonly int BornId    = Shader.PropertyToID("_Born");
        private static readonly int SeedId    = Shader.PropertyToID("_Seed");
        private static readonly int GravityId = Shader.PropertyToID("_Gravity");
        private static readonly int LifeId    = Shader.PropertyToID("_Life");
        private static readonly int DripId    = Shader.PropertyToID("_Drip");

        /// <summary>How long this splat lives, so the caller can time its own cleanup to it.</summary>
        public float Life => life;

        /// <summary>
        /// Put a splat on a surface.
        ///
        /// <paramref name="seed"/> is what makes the lobes fall where they do. Deriving it from the
        /// impact point rather than from a random number is deliberate: every machine spawns this
        /// splat from the same landing, so every machine draws the same splash, and two players
        /// standing beside each other are not looking at different paint.
        /// </summary>
        public void Place(Vector3 point, Vector3 normal, Color colour, float radius, float seed)
        {
            // Rolled so the quad's local up is the most "uphill" direction available. Any roll
            // would do — the shader gets gravity in quad space either way — but keeping it aligned
            // means the drip runs down the quad's own Y, which is far easier to author against.
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.right, normal);

            transform.SetPositionAndRotation(point + normal * surfaceOffset,
                                             Quaternion.LookRotation(-normal, up.normalized));
            transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);

            if (quad == null) quad = GetComponentInChildren<Renderer>();
            if (quad == null) return;

            // Gravity, flattened onto this surface and expressed in the quad's own axes. On a floor
            // this comes out near zero, which is exactly right: paint does not run on a floor.
            Vector3 downWorld = Vector3.ProjectOnPlane(Physics.gravity.normalized, normal);
            var downLocal = new Vector2(Vector3.Dot(downWorld, transform.right),
                                        Vector3.Dot(downWorld, transform.up));

            float slope = downLocal.magnitude;
            if (slope > 1e-4f) downLocal /= slope;

            // A per-splat material instance, because every splat has its own birth time and seed
            // and they are all on screen at once. Cheap: one quad, one material, gone in seconds.
            Material material = quad.material;
            material.SetColor(ColourId, colour);
            material.SetFloat(BornId, Time.time);
            material.SetFloat(SeedId, seed);
            material.SetVector(GravityId, new Vector4(downLocal.x, downLocal.y, 0f, 0f));
            material.SetFloat(LifeId, life);

            // Drips scale with how steep the surface is, so a shallow slope weeps and a sheer wall
            // runs. The shader does the rest.
            material.SetFloat(DripId, Mathf.Lerp(0f, 0.9f, slope) * radius);

            Destroy(gameObject, life);
        }

        /// <summary>
        /// A stable seed for a point in the world.
        ///
        /// Quantised to the centimetre before hashing, so two machines whose impact points differ
        /// in the last float bit still pick the same splash.
        /// </summary>
        public static float SeedFor(Vector3 point)
        {
            int x = Mathf.RoundToInt(point.x * 100f);
            int y = Mathf.RoundToInt(point.y * 100f);
            int z = Mathf.RoundToInt(point.z * 100f);

            int hash = x * 73856093 ^ y * 19349663 ^ z * 83492791;
            return Mathf.Abs(hash % 10007) * 0.01f;
        }
    }
}
