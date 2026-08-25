using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceGame.Items
{
    /// <summary>
    /// Turns the cursor into questions about a deployed pack: <em>which item is under it</em>,
    /// <em>where on the rig is it pointing</em> — and, for a carry crossing open ground, where
    /// the cursor ray passes the pack's height.
    ///
    /// <para>
    /// Split out of <c>PackHandController</c> because it is the half with no state at all. Every
    /// method here is a pure function of the camera, the cursor and the rig, which means the hand
    /// never has to reason about geometry and this can be read on its own.
    /// </para>
    /// <para>
    /// <c>Camera.ScreenPointToRay</c> is a new pattern in this project — every other pick in the
    /// game is a ray out of the player's face, because until now there was no cursor to shoot
    /// from.
    /// </para>
    /// </summary>
    public static class PackPointer
    {
        /// <summary>How far the cursor ray is allowed to travel. The rig is under two metres away.</summary>
        private const float Reach = 12f;

        private static readonly RaycastHit[] hits = new RaycastHit[8];

        /// <summary>Where the mouse is, or the screen centre when there is no mouse.</summary>
        public static Vector2 CursorPosition =>
            Mouse.current != null
                ? Mouse.current.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        /// <summary>The ray under the cursor. Degenerate — origin, zero direction — with no camera.</summary>
        public static Ray CursorRay(Camera cam) =>
            cam != null ? cam.ScreenPointToRay(CursorPosition) : new Ray(Vector3.zero, Vector3.zero);

        /// <summary>
        /// Where the cursor ray meets a horizontal plane at <paramref name="height"/> — the seat
        /// for a carried copy crossing ground that has no face under it. A ray that misses the
        /// plane, by pointing at the sky or by grazing it so shallowly the hit runs out past
        /// <see cref="Reach"/>, answers with a point <paramref name="fallbackMetres"/> along the
        /// ray instead, so the caller always gets somewhere under the cursor.
        /// </summary>
        public static Vector3 CursorPointAtHeight(Camera cam, float height, float fallbackMetres)
        {
            Ray ray = CursorRay(cam);
            if (ray.direction.sqrMagnitude < 1e-6f) return ray.origin;

            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));

            if (plane.Raycast(ray, out float along) && along <= Reach) return ray.GetPoint(along);

            return ray.GetPoint(fallbackMetres);
        }

        /// <summary>
        /// The display copy under the cursor, if there is one.
        ///
        /// <para>
        /// Masked to the pack-item layer, which is the whole reason that layer exists: without it
        /// this is a ray against the entire world that then has to walk every hit's parent chain
        /// looking for a <see cref="PackSurface"/>, and the first thing it would find on a deployed
        /// pack is the rig's own body collider.
        /// </para>
        /// </summary>
        /// <param name="visual">The copy's root — the child of the surface, not the collider's own
        /// object, which on a multi-part prefab is somewhere below it.</param>
        public static bool TryHitItem(Camera cam, out GameObject visual, out PackSurface surface, out Vector2 uv)
        {
            visual = null;
            surface = null;
            uv = Vector2.zero;

            int layer = BackpackItemVisual.ItemLayer;
            if (cam == null || layer < 0) return false;

            Ray ray = CursorRay(cam);
            if (ray.direction.sqrMagnitude < 1e-6f) return false;

            int count = Physics.RaycastNonAlloc(ray, hits, Reach, 1 << layer, QueryTriggerInteraction.Ignore);
            if (count <= 0) return false;

            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == null || hits[i].distance >= nearest) continue;

                PackSurface owner = hits[i].collider.GetComponentInParent<PackSurface>();
                if (owner == null) continue;

                nearest = hits[i].distance;
                surface = owner;
                visual = RootUnder(hits[i].collider.transform, owner.transform);

                // Projected down the surface normal, because ToUv drops the height. That is
                // exactly right: the point the player clicked on the TOP of a canister belongs to
                // the footprint the canister occupies on the mat, not to a point beside it.
                uv = owner.ToUv(hits[i].point);
            }

            return visual != null;
        }

        /// <summary>
        /// Where the cursor meets one of the rig's faces, and which face that is.
        ///
        /// <para>
        /// Intersected against each surface's PLANE rather than raycast, so a surface needs no
        /// collider of its own — the <c>SURF_</c> empties are empties. It also means a point that
        /// falls outside a face's rectangle is rejected by arithmetic rather than by the physics
        /// scene's idea of where that face's mesh happens to end.
        /// </para>
        /// <para>
        /// Nearest wins. The faces of a deployed rig do not overlap in plan, but the back panel
        /// stands at 65&#176; and its plane runs on past its own rectangle — the bounds test is
        /// what keeps that plane from swallowing the leaf behind it.
        /// </para>
        /// </summary>
        public static bool TryHitSurface(Camera cam, IReadOnlyList<PackSurface> surfaces,
                                         out PackSurface surface, out Vector2 uv)
        {
            surface = null;
            uv = Vector2.zero;

            if (cam == null || surfaces == null) return false;

            Ray ray = CursorRay(cam);
            if (ray.direction.sqrMagnitude < 1e-6f) return false;

            float nearest = float.MaxValue;

            for (int i = 0; i < surfaces.Count; i++)
            {
                PackSurface candidate = surfaces[i];
                if (candidate == null) continue;

                if (!TryIntersect(ray, candidate, out float distance, out Vector2 candidateUv)) continue;
                if (distance >= nearest) continue;

                Vector2 size = candidate.Size;
                if (candidateUv.x < 0f || candidateUv.y < 0f ||
                    candidateUv.x > size.x || candidateUv.y > size.y) continue;

                nearest = distance;
                surface = candidate;
                uv = candidateUv;
            }

            return surface != null;
        }

        private static bool TryIntersect(Ray ray, PackSurface surface, out float distance, out Vector2 uv)
        {
            distance = 0f;
            uv = Vector2.zero;

            Transform t = surface.transform;
            Vector3 normal = t.up;

            float facing = Vector3.Dot(ray.direction, normal);

            // Edge-on. Not a near miss to be tolerated: at grazing incidence the intersection
            // point runs off to infinity and the uv it produces is noise.
            if (Mathf.Abs(facing) < 1e-4f) return false;

            distance = Vector3.Dot(t.position - ray.origin, normal) / facing;
            if (distance <= 0f || distance > Reach) return false;

            uv = surface.ToUv(ray.origin + ray.direction * distance);
            return true;
        }

        /// <summary>The ancestor of <paramref name="hit"/> that is a direct child of the surface.</summary>
        private static GameObject RootUnder(Transform hit, Transform surface)
        {
            Transform current = hit;

            while (current != null && current.parent != surface) current = current.parent;

            return current != null ? current.gameObject : hit.gameObject;
        }
    }
}
