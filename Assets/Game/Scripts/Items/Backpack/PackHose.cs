using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The breathing hose: a tube from the pack's valve block to whatever is standing in its
    /// reserved socket, so it is visible that the rig is plumbed into the bottle rather than
    /// merely carrying it.
    ///
    /// <para>
    /// <b>Drawn, not modelled.</b> A hose in the model would be a tube running to thin air
    /// whenever the socket is empty — which is the whole point of the socket, since the bottle
    /// comes out. So the rig carries a MARKER where the hose leaves the manifold
    /// (<c>Marker_Rig_HoseOutlet</c>) and this stretches one segment from it to the bottle only
    /// while a bottle is there.
    /// </para>
    /// <para>
    /// <b>Computed in the outlet's own local space, once per change.</b> The outlet and the socket
    /// both ride <c>PIVOT_Back</c>, so their relative pose is fixed however the pack folds, is
    /// worn, or is thrown on the sand — which is what lets this be event-driven rather than a
    /// LateUpdate that follows a hinge.
    /// </para>
    /// <para>
    /// <b>Which END of the bottle it meets is measured, not assumed.</b> The two ends of the
    /// placed block are both computed and the nearer one wins, so the hose finds the foot without
    /// this having to know which way the socket's v axis runs — the kind of sign that is wrong
    /// half the time and looks plausible either way.
    /// </para>
    /// <para>
    /// <b>The tube is built in the OUTLET's frame, which is not metres.</b> The rig's FBX is on
    /// the centimetre convention — mesh data 100x small under transforms 100x large — so the
    /// marker's <c>lossyScale</c> is 100 and every world length written onto a child of it has to
    /// be divided by that first. The same divide <see cref="BackpackItemVisual"/>,
    /// <c>HolderBuilder</c> and <see cref="PackSurface.ToLocal"/> all make.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class PackHose : MonoBehaviour
    {
        [Tooltip("The container whose socket this hose serves.")]
        [SerializeField] private PackContainer container;

        [Tooltip("Where the hose leaves the valve block. The tube is built as a child of this.")]
        [SerializeField] private Transform outlet;

        [Tooltip("The reserved face. The hose is shown only while this holds something.")]
        [SerializeField] private PackSurfaceId socket = PackSurfaceId.BackPanelCentre;

        [Tooltip("Metres, in the rig's ORIGINAL frame: PackScale.Factor is applied on top, the " +
                 "way it is to every other length drawn on the rig. A breathing hose, not a fuel line.")]
        [SerializeField, Min(0.001f)] private float radius = 0.014f;

        [Tooltip("How far off the face the hose meets the bottle, as a fraction of the bottle's " +
                 "own thickness. 0.5 is its middle, which is where a hose would clamp on.")]
        [SerializeField, Range(0f, 1f)] private float meetsAt = 0.5f;

        [SerializeField] private Material material;

        private Transform tube;
        private PackLayout watched;

        private void OnEnable()
        {
            Watch();
            Refresh();
        }

        private void OnDisable()
        {
            if (watched != null) watched.OnChanged -= Refresh;
            watched = null;
        }

        private void Watch()
        {
            if (container == null) return;

            PackLayout layout = container.Layout;
            if (layout == watched) return;

            if (watched != null) watched.OnChanged -= Refresh;
            watched = layout;
            if (watched != null) watched.OnChanged += Refresh;
        }

        /// <summary>Rebuild the hose from what is in the socket right now.</summary>
        public void Refresh()
        {
            Watch();

            if (!TryMeasure(out Vector3 localTarget))
            {
                if (tube != null) tube.gameObject.SetActive(false);
                return;
            }

            EnsureTube();

            float length = localTarget.magnitude;
            if (length < 1e-4f)
            {
                tube.gameObject.SetActive(false);
                return;
            }

            tube.gameObject.SetActive(true);
            tube.localPosition = localTarget * 0.5f;
            tube.localRotation = Quaternion.FromToRotation(Vector3.up, localTarget.normalized);

            // The lossyScale divide, again. The LENGTH does not need it — it came back from
            // InverseTransformPoint and is already in the outlet's frame — which is exactly what
            // made the missing one so hard to see: the hose reached the bottle correctly and was
            // 100x too THICK. A 14 mm hose drawn 2.8 m across is a black slug bigger than the
            // 1.81 m rig it hangs off, and it appeared only once a bottle was in the socket.
            float outletScale = Mathf.Abs(outlet.lossyScale.x);
            if (outletScale < 1e-6f) outletScale = 1f;

            // Unity's cylinder is 2 units tall and 1 across, so half the length and the diameter.
            float thickness = PackScale.Apply(radius) * 2f / outletScale;

            tube.localScale = new Vector3(thickness, length * 0.5f, thickness);
        }

        /// <summary>
        /// Where the hose has to reach, in the outlet's own space, or false when the socket is
        /// empty and there is nothing to connect.
        /// </summary>
        private bool TryMeasure(out Vector3 localTarget)
        {
            localTarget = Vector3.zero;

            if (container == null || outlet == null) return false;

            PackSurface surface = container.SurfaceFor(socket);
            if (surface == null) return false;

            // The socket holds at most one thing — it is exactly one item wide — so the first
            // placement on it is the one.
            PackPlacement placement = default;
            bool found = false;

            foreach (PackPlacement candidate in container.Layout.Placements)
            {
                if (candidate.Surface != socket) continue;

                placement = candidate;
                found = true;
                break;
            }

            if (!found) return false;

            InventoryItem item = container.ItemFor(placement.ItemId);
            if (item == null || item.itemPrefab == null) return false;

            // How thick the bottle is off the face, and how long it lies along it. Both from the
            // same measurement the layout reserved its cells with, so the hose lands on the item
            // the player can see rather than on the one the prefab was authored at.
            Vector3 size = ItemFootprint.SizeOf(item.itemPrefab);
            float lift = size.y * meetsAt;
            float halfAlong = size.z * 0.5f;

            Vector3 a = surface.ToWorld(new Vector2(placement.Uv.x, placement.Uv.y - halfAlong), lift);
            Vector3 b = surface.ToWorld(new Vector2(placement.Uv.x, placement.Uv.y + halfAlong), lift);

            Vector3 from = outlet.position;
            Vector3 nearer = (a - from).sqrMagnitude <= (b - from).sqrMagnitude ? a : b;

            localTarget = outlet.InverseTransformPoint(nearer);
            return true;
        }

        private void EnsureTube()
        {
            if (tube != null) return;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Hose";
            go.transform.SetParent(outlet, false);

            // A collider here would join the nearest Rigidbody ABOVE it — on a worn pack that is
            // the PLAYER, which is exactly the fault BackpackObject switches its own body collider
            // off to avoid. The hose is scenery.
            // DestroyImmediate outside play mode, because Destroy is refused there and the
            // wiring pass and its tests both build the hose in the editor.
            Collider collider = go.GetComponent<Collider>();

            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;

            tube = go.transform;
        }
    }
}
