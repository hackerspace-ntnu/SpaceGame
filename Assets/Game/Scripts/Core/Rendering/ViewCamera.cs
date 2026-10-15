using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.Core
{
    /// <summary>
    /// The camera this machine is drawing the game from: the last <see cref="CameraType.Game"/>
    /// camera that rendered to the screen (no <c>targetTexture</c>), or <c>Camera.main</c> until
    /// one has.
    ///
    /// <para>
    /// Not <c>Camera.main</c>. That is only the player's own on-foot camera: riding switches it off
    /// and draws through an Untagged orbit camera, so while mounted <c>Camera.main</c> is null, and
    /// spectating, cutscenes and focus shots draw through cameras of their own. Recording whichever
    /// camera actually drew last covers every one of those without asking which is "the" camera.
    /// Render-texture cameras (maps, previews, the ship schematic) and Scene-view or preview
    /// cameras are never the view.
    /// </para>
    /// <para>
    /// Read from <c>Update</c>/<c>LateUpdate</c>, this is the camera of the previous frame — the
    /// one the player is looking through. Presentation-only: never a gameplay decision, which must
    /// not depend on where one machine's camera happens to be.
    /// </para>
    /// </summary>
    public static class ViewCamera
    {
        private static Camera drawing;

        /// <summary>The camera drawing this machine's view, or null when there is none at all.</summary>
        public static Camera Current =>
            drawing != null && drawing.isActiveAndEnabled ? drawing : Camera.main;

        /// <summary>Metres from the view camera to <paramref name="point"/>; NaN with no camera at all.</summary>
        public static float DistanceTo(Vector3 point)
        {
            Camera cam = Current;
            return cam == null ? float.NaN : Vector3.Distance(cam.transform.position, point);
        }

        // Edit mode too (previews, edit-mode tests), and again on entering Play Mode: domain reload
        // is off, so statics survive it and the camera from the last session must be forgotten.
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Hook()
        {
            drawing = null;
            RenderPipelineManager.beginCameraRendering -= Record;
            RenderPipelineManager.beginCameraRendering += Record;
        }

        private static void Record(ScriptableRenderContext context, Camera cam)
        {
            if (cam.cameraType != CameraType.Game || cam.targetTexture != null) return;
            drawing = cam;
        }
    }
}
