using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.Items
{
    /// <summary>
    /// Every patch of <see cref="GroundFire"/> standing on this machine: where new fire merges into
    /// old, how many patches may exist, and which of them are allowed to light the world.
    ///
    /// <para>
    /// <b>The grid is what makes a swept jet lay a trail instead of a heap.</b> Fire is laid several
    /// times a second from a ray the player is swinging, so without merging a two-second burst over
    /// one spot would stack thirty patches inside a metre — thirty lights, thirty overlap queries
    /// and thirty times the fill. Every ignition is quantized to a cell of the world and the cell
    /// either has a patch, which is refreshed, or does not, which is one new patch. It is also what
    /// keeps the host and a client roughly agreeing on where the fire is: two machines whose aim
    /// rays differ by a few centimetres still land in the same cell.
    /// </para>
    /// <para>
    /// <b>The lights are budgeted, not authored.</b> A patch has no idea how many others are
    /// competing or where the camera is, so it never switches its own light on — this does, for the
    /// nearest few, once per camera per frame. Past that budget a patch is still fire and still
    /// burns what stands in it; it simply stops being a light source (GDC-L1-PERF-0004 — the budget
    /// is the number of lights, and it is held here rather than hoped for).
    /// </para>
    /// </summary>
    public static class GroundFireField
    {
        /// <summary>
        /// The world grid fire merges on, in metres. Sized just under a patch's own diameter, so
        /// neighbouring cells overlap and a swept trail has no cold gaps down the middle of it.
        /// </summary>
        public const float CellSize = 1.2f;

        /// <summary>
        /// How many patches may burn at once on one machine. Reached by about eight seconds of
        /// continuous sweeping, at which point the oldest is put out to make room — which is the
        /// right end to give up, since it is the one closest to going out anyway.
        /// </summary>
        public const int MaxPatches = 40;

        /// <summary>How many of them may be lights. The rest are fire without illumination.</summary>
        public const int MaxLitPatches = 6;

        private static readonly Dictionary<Vector3Int, GroundFire> ByCell =
            new Dictionary<Vector3Int, GroundFire>();

        /// <summary>Oldest first. That ordering is the recycling policy.</summary>
        private static readonly List<GroundFire> Live = new List<GroundFire>();

        private static readonly Stack<GroundFire> Idle = new Stack<GroundFire>();

        /// <summary>Reused by the light budget so a per-camera sort allocates nothing.</summary>
        private static readonly List<GroundFire> Sorted = new List<GroundFire>(MaxPatches);

        private static Vector3 sortOrigin;

        private static bool listening;

        /// <summary>How many patches are burning here. Exposed so the budget can be exercised.</summary>
        public static int LiveCount => Live.Count;

        // Statics survive a world unload, a return to the menu and — with Enter Play Mode Options
        // on, which they are here — play mode itself. A dictionary still holding last session's
        // destroyed patches hands the next one a null and a cell that can never be lit again.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (listening)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                listening = false;
            }

            ByCell.Clear();
            Live.Clear();
            Idle.Clear();
            Sorted.Clear();
        }

        /// <summary>
        /// Set fire to the ground at <paramref name="point"/>, or feed the fire already there.
        ///
        /// <para>
        /// Called on EVERY machine from the flamethrower's presentation path, and every one of
        /// those patches sets what stands in it alight. Nothing here asks who decides:
        /// <c>StatusReceiver.Apply</c> bills the announcement exactly once, on the machine that
        /// simulates the body, however many patches make it — see <see cref="GroundFire"/>.
        /// </para>
        /// </summary>
        public static void Kindle(GameObject prefab, Vector3 point, Transform igniter)
        {
            if (prefab == null) return;

            Vector3Int cell = CellOf(point);

            if (ByCell.TryGetValue(cell, out GroundFire existing))
            {
                if (existing != null)
                {
                    // Refreshed at its own centre rather than at the new point, so a patch does not
                    // creep across the ground as the player keeps pouring fire into the same cell.
                    existing.Kindle(existing.Centre, igniter);
                    return;
                }

                // Destroyed out from under us — a scene unload does exactly that — so the cell is
                // freed rather than left pointing at nothing, which would make it unlightable.
                ByCell.Remove(cell);
                Live.Remove(existing);
            }

            GroundFire fire = Take(prefab);
            if (fire == null) return;

            fire.Kindle(point, igniter);

            ByCell[cell] = fire;
            Live.Add(fire);

            Listen();
        }

        /// <summary>
        /// A patch has finished burning out. Called by the patch itself once nothing is left in the
        /// air; it returns to the pool and frees its cell for the next fire laid there.
        /// </summary>
        public static void Release(GroundFire fire)
        {
            // Only a patch that was actually burning goes back in the pool. A patch releases itself
            // from Update, and an Update that ran twice before the object went inactive would
            // otherwise push the same one onto the pool twice and hand it out to two fires at once.
            if (fire == null || !Live.Remove(fire)) return;

            ForgetCell(fire);

            fire.gameObject.SetActive(false);
            Idle.Push(fire);

            if (Live.Count == 0) Unlisten();
        }

        private static Vector3Int CellOf(Vector3 point) =>
            new Vector3Int(Mathf.FloorToInt(point.x / CellSize),
                           Mathf.FloorToInt(point.y / CellSize),
                           Mathf.FloorToInt(point.z / CellSize));

        /// <summary>
        /// A patch to use: one out of the pool, one recycled off the front of the queue, or a new
        /// one. The middle case is the budget doing its job, and it is deliberately silent — a
        /// player holding the trigger down has done nothing wrong.
        /// </summary>
        private static GroundFire Take(GameObject prefab)
        {
            while (Idle.Count > 0)
            {
                GroundFire pooled = Idle.Pop();

                // A pooled patch can be destroyed while it waits: it lives in whatever scene it was
                // laid in, and that scene can be streamed out from under the pool.
                if (pooled == null) continue;

                pooled.gameObject.SetActive(true);
                return pooled;
            }

            if (Live.Count >= MaxPatches)
            {
                GroundFire oldest = Live[0];
                Live.RemoveAt(0);
                ForgetCell(oldest);

                if (oldest != null)
                {
                    oldest.Douse();
                    return oldest;
                }
            }

            GameObject instance = Object.Instantiate(prefab);
            instance.name = prefab.name;

            var fire = instance.GetComponent<GroundFire>();
            if (fire != null) return fire;

            Debug.LogError($"[GroundFire] Prefab '{prefab.name}' has no GroundFire component, so " +
                           "the flamethrower can lay fire that nothing will ever put out.", prefab);
            Object.Destroy(instance);
            return null;
        }

        /// <summary>
        /// Drop whichever cell holds <paramref name="fire"/>. A linear scan, over at most
        /// <see cref="MaxPatches"/> entries and only when a patch goes out — cheaper than the second
        /// dictionary it would take to avoid, and impossible to leave out of step with the first.
        /// </summary>
        private static void ForgetCell(GroundFire fire)
        {
            bool found = false;
            Vector3Int cell = default;

            foreach (KeyValuePair<Vector3Int, GroundFire> entry in ByCell)
            {
                if (entry.Value != fire) continue;

                cell = entry.Key;
                found = true;
                break;
            }

            if (found) ByCell.Remove(cell);
        }

        private static void Listen()
        {
            if (listening) return;

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            listening = true;
        }

        private static void Unlisten()
        {
            if (!listening) return;

            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            listening = false;
        }

        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null) return;

            // A thumbnail and a reflection probe would only re-sort the same field against a camera
            // nobody is looking through, and the answer they arrived at would be the one the game
            // view then rendered with.
            if (camera.cameraType == CameraType.Preview ||
                camera.cameraType == CameraType.Reflection)
                return;

            Budget(camera.transform.position);
        }

        /// <summary>
        /// Give the light budget to the patches nearest <paramref name="viewpoint"/>. Exposed so the
        /// policy can be exercised without a render loop.
        /// </summary>
        public static void Budget(Vector3 viewpoint)
        {
            sortOrigin = viewpoint;

            Sorted.Clear();
            for (int i = 0; i < Live.Count; i++)
            {
                GroundFire fire = Live[i];

                // A patch can be destroyed without ever running OnDisable — a scene unload does
                // exactly that — so the list is swept here rather than trusted.
                if (fire == null)
                {
                    Live.RemoveAt(i--);
                    continue;
                }

                Sorted.Add(fire);
            }

            if (Sorted.Count == 0)
            {
                Unlisten();
                return;
            }

            Sorted.Sort(ByDistance);

            for (int i = 0; i < Sorted.Count; i++) Sorted[i].AllowGlow(i < MaxLitPatches);
        }

        private static readonly System.Comparison<GroundFire> ByDistance = (a, b) =>
            SortKey(a).CompareTo(SortKey(b));

        /// <summary>
        /// Distance to the patch's edge rather than its centre, so a patch the camera is standing
        /// in cannot lose its light to one parked slightly nearer.
        /// </summary>
        private static float SortKey(GroundFire fire) =>
            fire != null
                ? Vector3.Distance(fire.Centre, sortOrigin) - fire.Radius
                : float.PositiveInfinity;
    }
}
