using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>How an item meets the surface it is seated on.</summary>
    public enum BackpackSeat
    {
        /// Standing on a shelf: the item's own up stays up and it rests on its base.
        StandOn,
        /// Lashed flat to a face under the cargo net: the item's thinnest axis goes into the
        /// surface and its longest lies along it, so it hugs the panel instead of jutting out.
        LieFlat
    }

    // Turns an item prefab into an inert display copy seated in one of the pack's sockets.
    //
    // A display copy is not an item: it holds no state and must never run gameplay code. All it
    // does is get looked at and hit by the interaction ray, so everything that could tick,
    // collide, animate or make noise is taken off it before it gets a chance to run.
    public static class BackpackItemVisual
    {
        /// Build a display-only copy of an item prefab, parented to socket, uniformly scaled so
        /// its largest bounds axis equals fitSize, seated according to `seat`, and given exactly
        /// one BoxCollider for the interaction ray. Returns null if itemPrefab is null.
        public static GameObject Build(GameObject itemPrefab, Transform socket, float fitSize,
                                       BackpackSeat seat = BackpackSeat.StandOn)
        {
            if (itemPrefab == null || socket == null) return null;

            // Instantiate runs Awake synchronously, so stripping afterwards is already too late —
            // a weapon script would have registered itself, an artifact spawned its effect. A copy
            // born under a DEACTIVATED parent is never activeInHierarchy, so no Awake runs at all
            // and DestroyImmediate takes the components off clean.
            var stage = new GameObject("BackpackItemStage");
            stage.SetActive(false);

            GameObject copy = Object.Instantiate(itemPrefab, stage.transform);
            Strip(copy);

            Transform t = copy.transform;

            // Normalise before measuring: the prefab's own root scale is about to be replaced by
            // the fit scale anyway, and a zero on one of its axes would make the inverse transform
            // inside LocalBounds non-finite.
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            Bounds local = LocalBounds(t);
            float largest = Mathf.Max(local.size.x, Mathf.Max(local.size.y, local.size.z));

            // fitSize is metres of finished, on-screen size, so the socket's own scale has to come
            // out of the fit. It is not 1: the pack's FBX arrives on the centimetre convention, mesh
            // data 100x small under transforms 100x large, which cancels for the pack itself and
            // multiplies anything parented into a socket. Without this a pocketed rifle is 100 m long.
            float socketScale = Mathf.Abs(socket.lossyScale.x);
            if (socketScale < 1e-6f) socketScale = 1f;

            // A prefab with no renderers has nothing to fit to; keep it at its own size rather
            // than dividing by zero.
            float scale = largest > 1e-6f ? fitSize / (largest * socketScale) : 1f / socketScale;

            t.SetParent(socket, false);
            t.localScale = Vector3.one * scale;

            // Every socket's local +Y is "out of the surface", so both seats push the item along
            // +Y by half of whatever now spans that axis. What differs is which axis that is.
            Quaternion orient;
            float acrossSurface;

            if (seat == BackpackSeat.LieFlat)
            {
                // Turn the item's THINNEST axis into the surface and its longest along it. Without
                // this an item lashed to a door sticks straight out of the panel on the end of its
                // own length, which is the single thing that reads as "floating", not "packed".
                int thin = MinAxis(local.size);
                int along = MaxAxis(local.size);
                orient = Quaternion.Inverse(Quaternion.LookRotation(Axis(along), Axis(thin)));
                acrossSurface = local.size[thin];
            }
            else
            {
                // Standing on a shelf: the item keeps its own up and rests on its base.
                orient = Quaternion.identity;
                acrossSurface = local.size.y;
            }

            t.localRotation = orient;

            // Seated by the FACE that meets the surface and centred over the socket, not by the
            // pivot: item prefabs in this project pivot anywhere from grip to muzzle, and a
            // pivot-seated copy either sinks through the shelf or floats above it.
            t.localPosition = -(orient * (local.center * scale))
                              + Vector3.up * (scale * acrossSurface * 0.5f);

            // BoxCollider.size is in local space, so the transform scale above already resizes it
            // to the fitted footprint. Passing the scaled numbers here would square the scaling.
            BoxCollider box = copy.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;
            box.isTrigger = false;

            SetLayer(t, socket.gameObject.layer);

            Object.DestroyImmediate(stage);
            return copy;
        }

        private static int MinAxis(Vector3 v) =>
            v.x <= v.y && v.x <= v.z ? 0 : (v.y <= v.z ? 1 : 2);

        private static int MaxAxis(Vector3 v) =>
            v.x >= v.y && v.x >= v.z ? 0 : (v.y >= v.z ? 1 : 2);

        private static Vector3 Axis(int i) =>
            i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        // Order matters. MonoBehaviours go first because a [RequireComponent] on a script blocks
        // removal of the Rigidbody or Collider it names. ParticleSystemRenderer goes with its
        // ParticleSystem for the same reason — the renderer requires the system, and a particle
        // renderer with nothing feeding it draws nothing anyway.
        private static void Strip(GameObject copy)
        {
            // NetworkBehaviours before the plain pass, because NetworkObject is itself a
            // MonoBehaviour and every NetworkBehaviour on the item requires it. The retry loop
            // below does get there eventually, but only after Unity has logged a refusal for each
            // one — ten warnings per pack refresh, which buries anything real. A stowed copy is
            // scenery; it has no business owning a network identity either way.
            DestroyAll<Unity.Netcode.NetworkBehaviour>(copy);

            DestroyAll<MonoBehaviour>(copy);
            DestroyAll<ParticleSystemRenderer>(copy);
            DestroyAll<ParticleSystem>(copy);

            // Line and trail renderers usually run in WORLD space, which means they ignore their
            // own transform: the copy gets scaled and seated and the rope stays exactly where the
            // original prefab drew it. On the grappling hook and the lasso that measured as a
            // 1 x 1 x 2 m item stuck at the pack's origin. They are also meaningless on a stowed
            // copy — a coil of rope in a pack is not mid-throw.
            DestroyAll<LineRenderer>(copy);
            DestroyAll<TrailRenderer>(copy);

            DestroyAll<Rigidbody>(copy);
            DestroyAll<Collider>(copy);
            DestroyAll<Animator>(copy);
            DestroyAll<AudioSource>(copy);
        }

        // Unity refuses to remove a component while another one on the same object declares it as
        // a requirement, and only logs rather than throwing — so a single pass silently leaves
        // whichever half of a [RequireComponent] pair it happened to reach first. Repeating until
        // the count stops falling clears the dependents and then what they were holding.
        private static void DestroyAll<T>(GameObject root) where T : Component
        {
            int previous = int.MaxValue;

            for (int pass = 0; pass < 8; pass++)
            {
                T[] found = root.GetComponentsInChildren<T>(true);

                int alive = 0;
                foreach (T component in found)
                    if (component != null) alive++;   // missing scripts come back as null entries

                if (alive == 0 || alive >= previous) return;
                previous = alive;

                foreach (T component in found)
                    if (component != null) Object.DestroyImmediate(component);
            }
        }

        // Renderer.bounds is a world-axis-aligned box, so it would give a different fit every time
        // the pack turned. localBounds is mesh space, and this walks each renderer's eight corners
        // into the copy root's space instead — which also works on the staged copy, whose world
        // bounds are not guaranteed while it is inactive.
        private static Bounds LocalBounds(Transform root)
        {
            var total = new Bounds();
            bool any = false;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                // Only renderers whose localBounds actually describe geometry in their own space.
                // Anything else either has no meaningful local extent or draws in world space, and
                // one of those in the set drags the measured box off into nowhere.
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;

                Bounds mesh = renderer.localBounds;
                Vector3 extents = mesh.extents;

                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);

                    Vector3 point = root.InverseTransformPoint(
                        renderer.transform.TransformPoint(mesh.center + offset));

                    if (any)
                    {
                        total.Encapsulate(point);
                    }
                    else
                    {
                        total = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                }
            }

            return any ? total : new Bounds(Vector3.zero, Vector3.zero);
        }

        // The whole hierarchy, not just the root: a child left behind on another layer would still
        // render through a camera the pack is meant to be hidden from.
        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }
    }
}
