using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Shoots one prefab against a flat background from a fixed angle, framed from its own
    /// renderer bounds.
    ///
    /// <para>
    /// This was <see cref="BatchIconGenerator"/>'s private renderer until the library site needed
    /// the same picture at a different size. Both callers depend on the framing being identical —
    /// a set of previews only reads as a set when every model is fitted the same way — so the rig
    /// lives here once rather than being copied and left to drift.
    /// </para>
    /// </summary>
    public static class PrefabPreviewRenderer
    {
        /// <summary>Fraction of the frame the model fills. Leaves a consistent margin.</summary>
        private const float Fill = 0.82f;

        /// <summary>The house 3/4 view: high enough to show a top face, turned enough to show two sides.</summary>
        public static readonly Vector2 DefaultAngle = new Vector2(22f, 135f);

        /// <summary>
        /// Renders <paramref name="prefab"/> at <paramref name="resolution"/> square.
        /// Returns null when the prefab has nothing visible to shoot; <paramref name="note"/>
        /// then says why, and otherwise carries anything the caller should know about the shot.
        /// </summary>
        public static Texture2D Render(
            GameObject prefab, Vector2 angle, int resolution, Color background, out string note)
        {
            note = "";
            if (prefab == null) { note = "no prefab"; return null; }

            var preview = new PreviewRenderUtility();
            GameObject inst = null;

            try
            {
                inst = Object.Instantiate(prefab);
                inst.hideFlags = HideFlags.HideAndDontSave;
                inst.transform.position = Vector3.zero;
                inst.transform.rotation = Quaternion.identity;
                inst.SetActive(true);

                // Line and trail renderers describe a rope or beam that is only meaningful in
                // flight, and their bounds dwarf the item's own. Left in, they drag the bounds
                // fit outward until the actual object is a few pixels across — which is exactly
                // why the shipped Lasso icon was a near-invisible hairline. Particles are
                // silenced for the same reason and because they render nothing at time zero.
                //
                // They are switched off rather than destroyed: a component another script
                // declares it requires cannot be removed, and Unity logs an error rather than
                // declining quietly — the dune foil's rigging ropes hit exactly that. Disabling
                // costs nothing here, since a disabled renderer draws nothing and the bounds fit
                // only ever considers mesh and skinned renderers anyway.
                int stripped = 0;
                foreach (var lr in inst.GetComponentsInChildren<LineRenderer>(true))
                { lr.enabled = false; stripped++; }
                foreach (var tr in inst.GetComponentsInChildren<TrailRenderer>(true))
                { tr.enabled = false; stripped++; }
                foreach (var ps in inst.GetComponentsInChildren<ParticleSystemRenderer>(true))
                { ps.enabled = false; stripped++; }
                if (stripped > 0) note = "silenced " + stripped + " line/particle";

                if (!TryGetBounds(inst, out Bounds bounds))
                {
                    note = "no visible renderer";
                    return null;
                }

                Quaternion camRot = Quaternion.Euler(angle.x, angle.y, 0f);
                float radius = Mathf.Max(bounds.extents.magnitude, 0.0001f);

                // Fit by projecting the eight corners into camera space rather than using the
                // bounding sphere, so a long thin item is framed as tightly as a chunky one.
                float maxX = 0f, maxY = 0f;
                Quaternion inv = Quaternion.Inverse(camRot);
                foreach (Vector3 corner in Corners(bounds))
                {
                    Vector3 local = inv * (corner - bounds.center);
                    maxX = Mathf.Max(maxX, Mathf.Abs(local.x));
                    maxY = Mathf.Max(maxY, Mathf.Abs(local.y));
                }

                var cam = preview.camera;
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(maxX, maxY, 0.0001f) / Fill;
                cam.transform.rotation = camRot;
                cam.transform.position = bounds.center - (camRot * Vector3.forward) * (radius * 2f + 1f);
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = radius * 8f + 20f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = background;

                // Lights are oriented relative to the camera, not the world, so an item shot
                // from an overridden angle is still lit the same way as the rest of the set.
                preview.ambientColor = new Color(0.34f, 0.34f, 0.36f, 1f);
                preview.lights[0].type = LightType.Directional;
                preview.lights[0].intensity = 1.35f;
                preview.lights[0].color = new Color(1f, 0.98f, 0.94f);
                preview.lights[0].transform.rotation = camRot * Quaternion.Euler(38f, -32f, 0f);
                preview.lights[1].type = LightType.Directional;
                preview.lights[1].intensity = 0.7f;
                preview.lights[1].color = new Color(0.86f, 0.90f, 1f);
                preview.lights[1].transform.rotation = camRot * Quaternion.Euler(-18f, 145f, 0f);

                preview.AddSingleGO(inst);

                preview.BeginStaticPreview(new Rect(0, 0, resolution, resolution));
                cam.Render();
                return preview.EndStaticPreview();
            }
            finally
            {
                if (inst != null) Object.DestroyImmediate(inst);
                preview.Cleanup();
            }
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
                yield return c + new Vector3(
                    ((i & 1) == 0 ? -e.x : e.x),
                    ((i & 2) == 0 ? -e.y : e.y),
                    ((i & 4) == 0 ? -e.z : e.z));
        }

        /// <summary>
        /// Bounds over what will actually be drawn. Falls back to including disabled renderers,
        /// since a few prefabs keep their visual switched off until equipped.
        /// </summary>
        private static bool TryGetBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            var all = go.GetComponentsInChildren<Renderer>(true)
                .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer)
                .ToList();

            var live = all.Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            var use = live.Count > 0 ? live : all;
            if (use.Count == 0) return false;

            foreach (var r in use)
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy)
                {
                    r.enabled = true;
                    r.gameObject.SetActive(true);
                }
            }

            bool first = true;
            foreach (var r in use)
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds.size.sqrMagnitude > 1e-10f;
        }
    }
}
