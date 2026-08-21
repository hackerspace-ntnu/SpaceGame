// The window half of a portal: rendering what is on the other side.
//
// One renderer drives every aperture in the scene rather than each portal
// rendering itself, because the work has to happen at a specific point in the
// frame — after everything has moved, before any camera draws — and because the
// portals have to be rendered in a known order for recursion to mean anything.
//
// Three things here are load-bearing and none of them are obvious:
//
//   • WHERE THE CAMERA GOES. The pose comes from Portal.TransferFrom, the same
//     matrix that moves a traveller. If the view were computed any other way it
//     would drift from where walking through actually puts you, and that
//     mismatch is the one portal bug a player notices immediately.
//   • THE OBLIQUE NEAR PLANE. The virtual camera sits behind the far aperture,
//     so everything between it and that aperture — the far side of the wall,
//     props behind it — is in shot and must not be. Skewing the near plane onto
//     the portal's own plane clips exactly that and nothing else.
//   • NOT DRAWING INTO WHAT YOU ARE SAMPLING. A portal's own surface is hidden
//     while its texture is being written, or the pass reads and writes one
//     render target and the result is undefined on some drivers and a feedback
//     tunnel on the rest.
//
// URP renders on a schedule this code does not own, so the render is issued
// through SubmitRenderRequest rather than Camera.Render — the sanctioned path
// in URP 17 for drawing a camera on demand.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpaceGame.Portals
{
    /// <summary>
    /// ExecuteAlways for the same reason Portal is: the subscription in OnEnable
    /// has to happen outside play mode too, or apertures are invisible to
    /// whoever is placing them.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PortalRenderer : MonoBehaviour
    {
        [Header("Quality")]
        [Tooltip("Render target size as a fraction of the screen. Portals are seen at an angle and through a fringe, so most of a full-resolution render is wasted.")]
        [Range(0.25f, 1f)]
        [SerializeField] private float resolutionScale = 0.75f;

        [Tooltip("How many portals deep the view goes. 1 is a plain window; 2 or 3 gives the tunnel. Each level is a full scene render per portal.")]
        [Range(1, 4)]
        [SerializeField] private int recursionLimit = 2;

        [Tooltip("Portals further away than this are not rendered at all. Their last frame stays on the surface, which at that distance is a few pixels.")]
        [SerializeField] private float renderDistance = 60f;

        [Header("Clipping")]
        [Tooltip("Pushes the oblique near plane slightly behind the aperture, so the portal's own surface is not clipped away by it.")]
        [SerializeField] private float nearClipOffset = 0.05f;

        [Tooltip("Below this distance the oblique matrix becomes degenerate, so the ordinary projection is used instead. Only reached when the viewer's eye is in the plane of the far portal.")]
        [SerializeField] private float nearClipLimit = 0.2f;

        private static PortalRenderer instance;

        private Camera portalCamera;
        private readonly Dictionary<Portal, RenderTexture> targets = new Dictionary<Portal, RenderTexture>();
        private readonly Plane[] frustum = new Plane[6];
        private readonly List<Portal> renderable = new List<Portal>();
        private readonly List<Matrix4x4> poses = new List<Matrix4x4>();

        private bool rendering;
        private bool warnedAboutRenderRequest;

        /// <summary>
        /// The renderer if one already exists, without creating one.
        ///
        /// Teardown paths need this: asking for <see cref="Instance"/> from an
        /// OnDestroy during a scene unload or on quit builds a fresh GameObject
        /// that Unity then complains it cannot create, and leaks it if it can.
        /// </summary>
        public static PortalRenderer Existing => instance;

        /// <summary>
        /// The renderer for this session, created on demand.
        ///
        /// Nothing has to place it in a scene: the first portal to exist asks
        /// for it. A portal that worked in one scene and silently did not in
        /// another because somebody forgot a manager object is exactly the kind
        /// of failure this codebase avoids elsewhere.
        /// </summary>
        public static PortalRenderer Instance
        {
            get
            {
                if (instance != null) return instance;

                instance = FindFirstObjectByType<PortalRenderer>();
                if (instance != null) return instance;

                var go = new GameObject("Portal Renderer") { hideFlags = HideFlags.DontSave };
                instance = go.AddComponent<PortalRenderer>();
                return instance;
            }
        }

        private void OnEnable()
        {
            instance = this;

            // beginContextRendering, not beginCameraRendering: it fires once per
            // frame with every camera about to be drawn, which is the only point
            // where all the portal work can be done before ANY of them draws.
            // Doing it per camera means re-rendering every portal once per
            // camera, and re-entering the pipeline from inside itself.
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;

            foreach (KeyValuePair<Portal, RenderTexture> pair in targets)
            {
                if (pair.Key != null) pair.Key.ViewTexture = null;
                if (pair.Value != null) pair.Value.Release();
            }
            targets.Clear();

            // DestroyImmediate outside play mode: this component runs in edit
            // mode (see the class summary), and Destroy is deferred to the end
            // of a frame that an editor scene does not have — the camera would
            // survive the disable and keep rendering into released targets.
            if (portalCamera != null)
            {
                if (Application.isPlaying) Destroy(portalCamera.gameObject);
                else DestroyImmediate(portalCamera.gameObject);
            }

            if (instance == this) instance = null;
        }

        // ── The frame ──────────────────────────────────────────────────────────

        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            // SubmitRenderRequest below re-enters the pipeline, which fires this
            // callback again for the portal camera. Without the guard that is an
            // unbounded recursion that hard-locks the editor.
            if (rendering) return;

            Camera viewer = PickViewer(cameras);
            if (viewer == null) return;

            renderable.Clear();
            GeometryUtility.CalculateFrustumPlanes(viewer, frustum);

            foreach (Portal portal in Portal.All)
            {
                if (portal == null || portal.Linked == null) continue;
                if (portal.SurfaceRenderer == null || !portal.SurfaceRenderer.enabled) continue;

                Bounds bounds = portal.SurfaceRenderer.bounds;
                if ((bounds.center - viewer.transform.position).sqrMagnitude >
                    renderDistance * renderDistance) continue;

                if (!GeometryUtility.TestPlanesAABB(frustum, bounds)) continue;

                renderable.Add(portal);
            }

            if (renderable.Count == 0) return;

            rendering = true;
            try
            {
                EnsureCamera(viewer);
                foreach (Portal portal in renderable)
                {
                    RenderPortal(portal, viewer);

                    // Immediately, not next LateUpdate: the texture only exists
                    // as of the line above, and the surface is about to be drawn
                    // in this same frame. See Portal.PublishSurfaceState.
                    portal.PublishSurfaceState();
                }
            }
            finally
            {
                rendering = false;
            }
        }

        /// <summary>
        /// Which camera the portals are drawn for.
        ///
        /// One camera, and only one: rendering portals per camera would multiply
        /// the cost by every overlay, minimap and reflection probe in the frame,
        /// and each of those would overwrite the same render targets with a view
        /// from somewhere else.
        ///
        /// The game camera wins when there is one. The scene view is the
        /// fallback rather than an equal, so that portals are live while a level
        /// is being built — an aperture that only works in play mode is one
        /// nobody can place accurately — while costing exactly nothing extra in
        /// a running game, where a game camera always exists.
        /// </summary>
        private Camera PickViewer(List<Camera> cameras)
        {
            Camera best = null;
            Camera sceneView = null;

            for (int i = 0; i < cameras.Count; i++)
            {
                Camera camera = cameras[i];
                if (camera == null || camera == portalCamera) continue;

                if (camera.cameraType == CameraType.SceneView)
                {
                    if (sceneView == null) sceneView = camera;
                    continue;
                }

                if (camera.cameraType != CameraType.Game) continue;

                // Highest depth wins, matching the order URP itself draws them:
                // the last game camera to draw is the one the player is looking
                // through when several are stacked.
                if (best == null || camera.depth > best.depth) best = camera;
            }

            return best != null ? best : sceneView;
        }

        private void EnsureCamera(Camera viewer)
        {
            if (portalCamera == null)
            {
                var go = new GameObject("Portal Camera") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(transform, false);
                portalCamera = go.AddComponent<Camera>();

                // Never enabled. An enabled camera is rendered by the pipeline
                // on its own schedule, which is once per frame from wherever it
                // happens to be standing — for a camera reposed several times
                // per frame that is a render from the wrong pose.
                portalCamera.enabled = false;
            }

            portalCamera.CopyFrom(viewer);
            portalCamera.enabled = false;
            portalCamera.targetTexture = null;

            // A portal camera that rendered post-processing would apply the
            // viewer's bloom and exposure twice — once into the texture and
            // again over the surface that samples it.
            var data = portalCamera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.renderShadows = true;
                data.requiresColorOption = CameraOverrideOption.Off;
                data.requiresDepthOption = CameraOverrideOption.Off;
            }
        }

        // ── One portal ─────────────────────────────────────────────────────────

        private void RenderPortal(Portal portal, Camera viewer)
        {
            RenderTexture target = EnsureTarget(portal, viewer);
            if (target == null) return;

            // Walk the chain outward: pose 0 is the deepest view, the last is
            // the one the player actually sees. Rendering deepest first means
            // each shallower pass finds the previous one already on the far
            // portal's surface, which is where the tunnel comes from.
            poses.Clear();
            Matrix4x4 transfer = portal.Linked.TransferFrom(portal);
            Matrix4x4 pose = viewer.transform.localToWorldMatrix;

            for (int i = 0; i < recursionLimit; i++)
            {
                pose = transfer * pose;
                poses.Insert(0, pose);
            }

            // Hidden for the whole chain: this surface is the render target
            // being written, and sampling it while writing it is undefined.
            bool wasVisible = portal.SurfaceRenderer.enabled;
            portal.SurfaceRenderer.enabled = false;

            try
            {
                for (int i = 0; i < poses.Count; i++)
                {
                    portalCamera.transform.SetPositionAndRotation(
                        poses[i].GetColumn(3), poses[i].rotation);

                    ApplyObliqueProjection(portal.Linked, viewer);
                    Submit(target);
                }
            }
            finally
            {
                portal.SurfaceRenderer.enabled = wasVisible;
            }
        }

        /// <summary>
        /// Skew the near plane onto the far aperture's own plane.
        ///
        /// Everything between the virtual camera and that aperture is on the
        /// wrong side of the wall and would otherwise be drawn straight over the
        /// view — most visibly the back face of the wall the far portal is set
        /// into, which fills the whole frame.
        /// </summary>
        private void ApplyObliqueProjection(Portal destination, Camera viewer)
        {
            Transform plane = destination.transform;
            Vector3 eye = portalCamera.transform.position;

            // Which way the plane faces relative to the camera. Portals are
            // double-sided, so this cannot be assumed.
            float facing = Mathf.Sign(Vector3.Dot(plane.forward, plane.position - eye));

            Matrix4x4 worldToCamera = portalCamera.worldToCameraMatrix;
            Vector3 pointCS = worldToCamera.MultiplyPoint(plane.position);
            Vector3 normalCS = worldToCamera.MultiplyVector(plane.forward) * facing;
            float distance = -Vector3.Dot(pointCS, normalCS) + nearClipOffset;

            // Too close to the plane and the oblique matrix degenerates into
            // clipping everything. Happens when the player's eye is level with
            // the far aperture — exactly the moment of stepping through — so it
            // has to fall back rather than flash black.
            if (Mathf.Abs(distance) <= nearClipLimit)
            {
                portalCamera.projectionMatrix = viewer.projectionMatrix;
                return;
            }

            var clipPlane = new Vector4(normalCS.x, normalCS.y, normalCS.z, distance);
            portalCamera.projectionMatrix = viewer.CalculateObliqueMatrix(clipPlane);
        }

        private void Submit(RenderTexture target)
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };

            if (RenderPipeline.SupportsRenderRequest(portalCamera, request))
            {
                RenderPipeline.SubmitRenderRequest(portalCamera, request);
                return;
            }

            // Fallback for a pipeline that does not take render requests. Works,
            // but URP complains about it, so it is said once rather than per
            // portal per frame.
            if (!warnedAboutRenderRequest)
            {
                warnedAboutRenderRequest = true;
                Debug.LogWarning("[Portal] This pipeline does not support SingleCameraRequest; " +
                                 "falling back to Camera.Render for portal views.", this);
            }

            portalCamera.targetTexture = target;
            portalCamera.Render();
            portalCamera.targetTexture = null;
        }

        private RenderTexture EnsureTarget(Portal portal, Camera viewer)
        {
            int width = Mathf.Max(16, Mathf.RoundToInt(viewer.pixelWidth * resolutionScale));
            int height = Mathf.Max(16, Mathf.RoundToInt(viewer.pixelHeight * resolutionScale));

            if (targets.TryGetValue(portal, out RenderTexture existing) && existing != null)
            {
                if (existing.width == width && existing.height == height)
                {
                    portal.ViewTexture = existing;
                    return existing;
                }

                existing.Release();
                targets.Remove(portal);
            }

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
            {
                name = $"Portal View ({portal.name})",
                // The view is sampled at the edges with an offset for the
                // refraction, so clamping stops the far side of the screen
                // wrapping into the fringe.
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            target.Create();

            targets[portal] = target;
            portal.ViewTexture = target;
            return target;
        }

        /// <summary>Drop the target for a portal that is going away.</summary>
        public void Forget(Portal portal)
        {
            if (portal == null || !targets.TryGetValue(portal, out RenderTexture target)) return;

            portal.ViewTexture = null;
            if (target != null) target.Release();
            targets.Remove(portal);
        }
    }
}
