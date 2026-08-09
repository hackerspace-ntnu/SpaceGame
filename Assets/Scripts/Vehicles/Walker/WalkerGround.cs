// Finding the ground under a walking machine.
//
// Three questions, and every walker asks all three: what is directly below this point, what
// surface will a whole SOLE meet there, and what is the tallest thing a step has to clear. None of
// the answers depend on how many legs the machine has, so none of them belong in a locomotion
// component -- they were duplicated almost verbatim between the ostrich and the walking station,
// which is two places for the same bug to hide.
//
// The one subtlety is that a walker is wrapped in its own colliders, so a plain masked raycast
// plants its feet on itself. Every ray here rejects hits on the machine casting it, and that is
// most of the reason this is worth having in one place.
using UnityEngine;

namespace SpaceGame.Walker
{
    /// Built once from the machine's own settings, then queried per frame. Owns its hit buffer so
    /// the per-frame rays allocate nothing.
    public class WalkerGround
    {
        private readonly Transform self;
        private readonly LayerMask mask;
        private readonly float startAbove;
        private readonly float length;
        private readonly RaycastHit[] buffer = new RaycastHit[16];

        /// `startAbove` is how far above a query point the ray starts, so a foot already slightly
        /// below the surface still finds it; `length` is how far down it looks from there.
        public WalkerGround(Transform self, LayerMask mask, float startAbove, float length)
        {
            this.self = self;
            this.mask = mask;
            this.startAbove = startAbove;
            this.length = length;
        }

        /// Nearest ground below `origin`, never the machine itself.
        public bool Ray(Vector3 origin, float distance, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, buffer, distance,
                                            mask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                Collider col = buffer[i].collider;
                if (col == null || col.transform.IsChildOf(self)) continue;
                if (buffer[i].distance >= nearest) continue;
                nearest = buffer[i].distance;
                best = buffer[i];
                found = true;
            }
            return found;
        }

        /// Ground below `at`, using the configured start height and length.
        public bool Below(Vector3 at, out RaycastHit hit)
            => Ray(at + Vector3.up * startAbove, length, out hit);

        /// The surface a whole sole will meet, sampled across the footprint rather than at one
        /// point. Takes the HIGHEST hit, because that is what the foot will actually rest on, and
        /// the MEAN normal, because a single ray on the lip of a rock reports a wall.
        public bool Sample(Vector3 at, float footprintRadius, out Vector3 point, out Vector3 normal)
        {
            point = at;
            normal = Vector3.up;
            if (!Below(at, out RaycastHit centre)) return false;

            Vector3 sum = centre.normal;
            float highest = centre.point.y;
            int hits = 1;

            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * 0.5f;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * footprintRadius;
                if (!Below(at + offset, out RaycastHit hit)) continue;
                sum += hit.normal;
                highest = Mathf.Max(highest, hit.point.y);
                hits++;
            }

            normal = sum.sqrMagnitude > 1e-6f ? (sum / hits).normalized : Vector3.up;
            point = new Vector3(centre.point.x, highest, centre.point.z);
            return true;
        }

        /// Highest ground anywhere along a step, so a swing can be lifted over what is actually
        /// under it rather than following a fixed arc through a rock. The ends are included, since
        /// they are where the foot has to leave from and arrive at.
        public float HighestAlong(Vector3 from, Vector3 to, int probes)
        {
            float highest = Mathf.Max(from.y, to.y);
            for (int i = 1; i < probes; i++)
            {
                Vector3 along = Vector3.Lerp(from, to, i / (float)probes);
                if (Below(along, out RaycastHit hit)) highest = Mathf.Max(highest, hit.point.y);
            }
            return highest;
        }
    }
}
