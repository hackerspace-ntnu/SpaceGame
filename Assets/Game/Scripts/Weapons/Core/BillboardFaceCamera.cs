using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.Weapons
{
    /// <summary>
    /// Turns this object toward each camera as that camera starts rendering, so a flat quad — the
    /// ball lightning orb, an impact glow — is seen face-on by whoever is drawing it.
    ///
    /// <para>
    /// Per rendering camera, not <c>Camera.main</c>. <c>Camera.main</c> is only the player's own
    /// on-foot view: riding switches that camera off and renders through an Untagged orbit camera,
    /// and spectating, cutscenes and focus shots render through cameras of their own. Following it
    /// left the quad frozen at whatever heading the projectile gave it, so from the side the orb was
    /// a card seen edge-on. Hooking the render also covers every peer and every camera on one
    /// machine without asking which one is "the" camera.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class BillboardFaceCamera : MonoBehaviour
    {
        [Tooltip("Face only this camera. Empty faces every camera that renders this object.")]
        [SerializeField] private Camera targetCamera;

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (cam == null) return;
            if (targetCamera != null && cam != targetCamera) return;

            // Game cameras only. The heading this leaves behind is what the next Update reads —
            // BallLightningBoltTargeting maps world points into the quad's UVs through it — so a
            // Scene view, thumbnail or reflection probe rendering last would aim the orb's bolt
            // for a view no player is looking through.
            if (cam.cameraType != CameraType.Game) return;

            transform.forward = cam.transform.forward;
        }
    }
}
