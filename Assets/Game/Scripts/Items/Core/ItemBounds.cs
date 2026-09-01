using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Measures how big an item prefab actually is, in its own root's local space.
    ///
    /// <para>
    /// Shared by everything that has to fit an item to something: the hand socket scales a held
    /// item to a hold size, the backpack seats a display copy on a surface, and placement needs
    /// the same footprint again. All three want the same answer, so they ask the same code.
    /// </para>
    /// </summary>
    public static class ItemBounds
    {
        /// <summary>
        /// The item's own extents, in the item root's local space, before any scaling the caller
        /// applies. <paramref name="subtree"/> narrows the measurement to part of the prefab.
        ///
        /// <para>
        /// Deliberately reads meshes rather than <c>Renderer.bounds</c>. Renderer bounds are a
        /// world-axis-aligned box, so they grow and shrink as the item turns; worse, line, trail
        /// and particle renderers report the extent of effects that are not the object at all —
        /// the Lasso's rope renderer would have it measured as metres of nothing.
        /// </para>
        /// <para>
        /// Renderers that are switched off are skipped too. A hidden mesh is not part of the shape
        /// the player sees, and counting one shrinks everything visible to make room for it — the
        /// GrapplingHook's disabled muzzle marker sits a third of a metre down the barrel and was
        /// doing exactly that.
        /// </para>
        /// <para>
        /// "Switched off" is judged relative to <paramref name="item"/>, not by
        /// <c>activeInHierarchy</c>. The backpack measures a display copy while it is still
        /// parented to a deactivated staging object — nothing under it is active in the hierarchy,
        /// and an <c>activeInHierarchy</c> test would measure every stowed item as nothing at all.
        /// Walking <c>activeSelf</c> up to the item root asks the question that was actually meant:
        /// is this part disabled <i>within the item</i>.
        /// </para>
        /// </summary>
        public static Bounds Measure(GameObject item, Transform subtree)
        {
            Transform root = item.transform;
            Transform from = subtree != null && subtree.IsChildOf(root) ? subtree : root;
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            bool any = false;
            Bounds result = new Bounds(Vector3.zero, Vector3.zero);

            var filters = from.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null) continue;

                var renderer = filters[i].GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled) continue;
                if (!ActiveUnder(filters[i].transform, root)) continue;

                Accumulate(ref result, ref any, mesh.bounds, toRoot * filters[i].transform.localToWorldMatrix);
            }

            var skinned = from.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinned.Length; i++)
            {
                Mesh mesh = skinned[i].sharedMesh;
                if (mesh == null || !skinned[i].enabled) continue;
                if (!ActiveUnder(skinned[i].transform, root)) continue;

                Accumulate(ref result, ref any, mesh.bounds, toRoot * skinned[i].transform.localToWorldMatrix);
            }

            return any ? result : new Bounds(Vector3.zero, Vector3.zero);
        }

        /// <summary>
        /// Is <paramref name="t"/> enabled all the way up to <paramref name="root"/>? Whatever the
        /// item itself is parented to is deliberately not consulted — see the note on
        /// <see cref="Measure"/>.
        /// </summary>
        private static bool ActiveUnder(Transform t, Transform root)
        {
            for (Transform step = t; step != null; step = step.parent)
            {
                if (!step.gameObject.activeSelf) return false;
                if (step == root) break;
            }

            return true;
        }

        private static void Accumulate(ref Bounds result, ref bool any, Bounds meshBounds, Matrix4x4 matrix)
        {
            Vector3 c = meshBounds.center;
            Vector3 e = meshBounds.extents;

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));

                Vector3 p = matrix.MultiplyPoint3x4(corner);

                if (!any)
                {
                    result = new Bounds(p, Vector3.zero);
                    any = true;
                }
                else
                {
                    result.Encapsulate(p);
                }
            }
        }
    }
}
