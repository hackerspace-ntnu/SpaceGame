using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds the item scanner's display: a world-space canvas laid on the screen plate, carrying
    /// the radar plot and its readouts, wired into the prefab's <see cref="ItemScannerScreen"/>.
    ///
    /// <para>
    /// The same three primitives the standing terminal's glass is made of
    /// (<see cref="WorldCanvasBuilder"/>), for the same reasons: text on a canvas stays crisp at
    /// any distance, costs no render texture, and lets the plate's own emissive green show through
    /// as the tube behind the ink. It replaced a fragment shader that drew the whole instrument
    /// into the plate's UVs — see the doc's gotchas for what that cost.
    /// </para>
    /// <para>
    /// Run from <see cref="GauntletReseat"/> rather than on its own, because the canvas is seated
    /// on the MODEL: a re-export moves the plate, and a canvas built against the old one would
    /// float beside the new one with nothing in the console to say so.
    /// </para>
    /// </summary>
    public static class ItemScannerScreenBuilder
    {
        private const string CanvasName = "Screen";

        /// <summary>How far the canvas stands off the glass, as a share of the plate's shorter side.</summary>
        private const float Standoff = 0.005f;

        private const float Margin = 8f;
        private const float HeaderHeight = 34f;
        private const float FooterHeight = 40f;

        /// <summary>
        /// Rebuilds the canvas on <paramref name="root"/> from the plate named
        /// <paramref name="plateName"/> inside <paramref name="model"/>. Destroys any canvas a
        /// previous run left, so it is re-runnable.
        /// </summary>
        public static bool Rebuild(GameObject root, Transform model, string plateName, StringBuilder log)
        {
            var screen = root.GetComponentInChildren<ItemScannerScreen>(true);
            if (screen == null)
            {
                log.AppendLine($"  {root.name}: no ItemScannerScreen to build a display for.");
                return false;
            }

            Transform plate = GauntletPrefab.FindDeep(model, plateName);
            var renderer = plate != null ? plate.GetComponent<MeshRenderer>() : null;
            var filter = plate != null ? plate.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (renderer == null || mesh == null)
            {
                log.AppendLine($"  {root.name}: the model has no drawable '{plateName}' to put a display on.");
                return false;
            }

            Transform old = root.transform.Find(CanvasName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            Face face = MeasureFace(plate, mesh, renderer.bounds.center);
            if (face.Width <= 1e-4f || face.Height <= 1e-4f)
            {
                log.AppendLine($"  {root.name}: '{plateName}' measures {face.Width:0.000} x {face.Height:0.000} m — not a screen.");
                return false;
            }

            RectTransform canvas = WorldCanvasBuilder.Canvas(
                root.transform, CanvasName,
                new Vector2(face.Width, face.Height) / WorldCanvasBuilder.CanvasUnit,
                face.Centre + face.Normal * (Standoff * Mathf.Min(face.Width, face.Height)),
                Quaternion.LookRotation(-face.Normal, face.Up));

            var group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            Rect safe = MeasureAperture(model, plate, face);

            (ScannerRadar radar, TextMeshProUGUI header, TextMeshProUGUI nearest, TextMeshProUGUI count) =
                BuildFace(canvas, safe);

            var so = new SerializedObject(screen);
            so.FindProperty("canvas").objectReferenceValue = canvas.GetComponent<Canvas>();
            so.FindProperty("group").objectReferenceValue = group;
            so.FindProperty("radar").objectReferenceValue = radar;
            so.FindProperty("headerText").objectReferenceValue = header;
            so.FindProperty("nearestText").objectReferenceValue = nearest;
            so.FindProperty("countText").objectReferenceValue = count;
            so.FindProperty("screenRenderer").objectReferenceValue = renderer;
            so.FindProperty("materialIndex").intValue = face.Slot;
            so.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine($"  {root.name}: display {face.Width:0.000} x {face.Height:0.000} m on " +
                           $"'{plateName}' slot {face.Slot}, {safe.width:0}x{safe.height:0} mm of it in the open.");
            return true;
        }

        /// <summary>The widgets on the glass: the plot, and the three things worth reading beside it.</summary>
        private static (ScannerRadar, TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI) BuildFace(
            RectTransform canvas, Rect safe)
        {
            float w = safe.width;
            float top = safe.yMax, bottom = safe.yMin;

            WorldCanvasBuilder.Fill(canvas, "Tube", WorldCanvasBuilder.Ink);

            TextMeshProUGUI header = WorldCanvasBuilder.Label(canvas, "Header", 20f,
                new Vector2(safe.center.x, top - HeaderHeight * 0.5f), new Vector2(w - Margin * 2f, HeaderHeight));
            header.text = "SCAN  50M";
            header.color = WorldCanvasBuilder.Phosphor;

            Image rule = WorldCanvasBuilder.Panel(canvas, "HeaderRule", WorldCanvasBuilder.Dim);
            rule.rectTransform.anchoredPosition = new Vector2(safe.center.x, top - HeaderHeight);
            rule.rectTransform.sizeDelta = new Vector2(w - Margin * 2f, 1.5f);

            // The plot takes everything the readouts leave, and stays square: a disc drawn into an
            // oblong is an ellipse, and an ellipse says a contact is nearer in one direction than
            // it is.
            float plotSize = Mathf.Min(w - Margin * 2f, safe.height - HeaderHeight - FooterHeight - Margin * 2f);
            var plotGo = new GameObject("Radar", typeof(RectTransform), typeof(CanvasRenderer), typeof(ScannerRadar));
            plotGo.layer = canvas.gameObject.layer;
            var radar = plotGo.GetComponent<ScannerRadar>();
            radar.raycastTarget = false;
            radar.rectTransform.SetParent(canvas, false);
            radar.rectTransform.sizeDelta = new Vector2(plotSize, plotSize);
            radar.rectTransform.anchoredPosition =
                new Vector2(safe.center.x, (top - HeaderHeight + bottom + FooterHeight) * 0.5f);

            // The nearest contact's range is the one number a wearer reads mid-stride, so it is the
            // largest thing on the glass after the plot itself.
            TextMeshProUGUI nearest = WorldCanvasBuilder.Label(canvas, "Nearest", 30f,
                new Vector2(safe.center.x, bottom + FooterHeight * 0.62f), new Vector2(w - Margin * 2f, FooterHeight * 0.7f));
            nearest.text = "--";
            nearest.color = WorldCanvasBuilder.Phosphor;

            TextMeshProUGUI count = WorldCanvasBuilder.Label(canvas, "Count", 15f,
                new Vector2(safe.center.x, bottom + FooterHeight * 0.18f), new Vector2(w - Margin * 2f, FooterHeight * 0.4f),
                TextAlignmentOptions.Center, FontStyles.Normal);
            count.text = "0 CONTACTS";
            count.color = WorldCanvasBuilder.Dim;

            return (radar, header, nearest, count);
        }

        /// <summary>How finely the glass is sampled when looking for what covers it.</summary>
        private const int ApertureSamples = 33;

        /// <summary>
        /// The part of the glass a reader can actually see, in canvas units, centred like the
        /// canvas is.
        ///
        /// <para>
        /// The plate is not the display: the console's own frame stands proud of it and eats a
        /// band off each end, and a layout that fills the plate puts its header and its readout
        /// under that frame — which is what the first build of this screen did, with the title
        /// half-swallowed and the contact count cut off at the chin.
        /// </para>
        /// <para>
        /// Measured rather than trimmed by a margin somebody tuned by eye: the glass is sampled on
        /// a grid, every triangle in the model that stands in FRONT of it is projected onto it, and
        /// a sample a triangle covers is struck out. What survives is the aperture. A vertex test
        /// is not enough — the housing's front is one big quad whose corners are all outside the
        /// plate, so it covers the glass without putting a single vertex on it.
        /// </para>
        /// </summary>
        private static Rect MeasureAperture(Transform model, Transform plate, Face face)
        {
            Vector3 right = Vector3.Cross(face.Up, face.Normal).normalized;
            float halfW = face.Width * 0.5f, halfH = face.Height * 0.5f;
            var covered = new bool[ApertureSamples, ApertureSamples];

            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.transform == plate) continue;
                var filter = r.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;

                Vector3[] verts = mesh.vertices;
                int[] tris = mesh.triangles;
                Matrix4x4 m = r.transform.localToWorldMatrix;

                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    Vector3 da = m.MultiplyPoint3x4(verts[tris[t]]) - face.Centre;
                    Vector3 db = m.MultiplyPoint3x4(verts[tris[t + 1]]) - face.Centre;
                    Vector3 dc = m.MultiplyPoint3x4(verts[tris[t + 2]]) - face.Centre;

                    // Wholly behind the glass: it cannot hide anything drawn on the front of it.
                    if (Vector3.Dot(da, face.Normal) < 0f && Vector3.Dot(db, face.Normal) < 0f &&
                        Vector3.Dot(dc, face.Normal) < 0f) continue;

                    Vector2 a = new(Vector3.Dot(da, right), Vector3.Dot(da, face.Up));
                    Vector2 b = new(Vector3.Dot(db, right), Vector3.Dot(db, face.Up));
                    Vector2 c = new(Vector3.Dot(dc, right), Vector3.Dot(dc, face.Up));

                    for (int i = 0; i < ApertureSamples; i++)
                    for (int j = 0; j < ApertureSamples; j++)
                    {
                        if (covered[i, j]) continue;
                        Vector2 p = new(Mathf.Lerp(-halfW, halfW, i / (ApertureSamples - 1f)),
                                        Mathf.Lerp(-halfH, halfH, j / (ApertureSamples - 1f)));
                        if (Covers(p, a, b, c)) covered[i, j] = true;
                    }
                }
            }

            int minI = ApertureSamples, maxI = -1, minJ = ApertureSamples, maxJ = -1;
            for (int i = 0; i < ApertureSamples; i++)
            for (int j = 0; j < ApertureSamples; j++)
            {
                if (covered[i, j]) continue;
                minI = Mathf.Min(minI, i); maxI = Mathf.Max(maxI, i);
                minJ = Mathf.Min(minJ, j); maxJ = Mathf.Max(maxJ, j);
            }

            // Nothing open, which means the sampling disagrees with the model. Use the whole plate
            // and let the build be looked at, rather than laying the display out inside nothing.
            if (maxI < minI || maxJ < minJ)
            {
                Debug.LogWarning("[ItemScannerScreenBuilder] The whole plate reads as covered; " +
                                 "laying the display out on all of it.");
                return InCanvasUnits(-halfW, -halfH, face.Width, face.Height);
            }

            float left = Mathf.Lerp(-halfW, halfW, minI / (ApertureSamples - 1f));
            float rightEdge = Mathf.Lerp(-halfW, halfW, maxI / (ApertureSamples - 1f));
            float bottom = Mathf.Lerp(-halfH, halfH, minJ / (ApertureSamples - 1f));
            float topEdge = Mathf.Lerp(-halfH, halfH, maxJ / (ApertureSamples - 1f));

            return InCanvasUnits(left, bottom, rightEdge - left, topEdge - bottom);
        }

        /// <summary>Metres on the glass as the millimetre units the canvas is laid out in.</summary>
        private static Rect InCanvasUnits(float x, float y, float width, float height)
        {
            const float unit = WorldCanvasBuilder.CanvasUnit;
            return new Rect(x / unit, y / unit, width / unit, height / unit);
        }

        /// <summary>Whether a triangle projected onto the glass covers a point on it.</summary>
        private static bool Covers(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Side(p, a, b), d2 = Side(p, b, c), d3 = Side(p, c, a);
            bool anyNegative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool anyPositive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(anyNegative && anyPositive);
        }

        private static float Side(Vector2 p, Vector2 a, Vector2 b) =>
            (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

        /// <summary>The display face of a plate: which material slot it is, and its frame in metres.</summary>
        private readonly struct Face
        {
            public readonly Vector3 Centre;
            public readonly Vector3 Normal;
            public readonly Vector3 Up;
            public readonly float Width;
            public readonly float Height;
            public readonly int Slot;

            public Face(Vector3 centre, Vector3 normal, Vector3 up, float width, float height, int slot)
            {
                Centre = centre;
                Normal = normal;
                Up = up;
                Width = width;
                Height = height;
                Slot = slot;
            }
        }

        /// <summary>
        /// Measures the plate's display face in world space.
        ///
        /// <para>
        /// <b>Which face.</b> A plate is a thin box with a front and a back of identical area, so
        /// area alone cannot pick the glass. Each material slot's area-weighted normal is compared
        /// against the direction out of the plate, and the slot facing outward is the display —
        /// which is also the answer to the question that broke the old display, where the screen
        /// material was painted onto the slot holding the back and the sides and the shader drew
        /// on faces nobody could see.
        /// </para>
        /// <para>
        /// <b>Which way up.</b> From the face's own UV gradients, not from world up: this plate is
        /// worn on a forearm at whatever angle the arm happens to be, so "up in the world" means
        /// nothing here. The author's v axis is the display's up, and the UVs are the one place
        /// that survives the unbaked node rotation and non-uniform node scale the model carries.
        /// </para>
        /// </summary>
        private static Face MeasureFace(Transform plate, Mesh mesh, Vector3 housingCentre)
        {
            Matrix4x4 m = plate.localToWorldMatrix;
            Vector3[] verts = mesh.vertices;
            Vector2[] uv = mesh.uv;

            int slot = 0;
            float bestFacing = float.MinValue;
            Vector3 normal = Vector3.forward, gradU = Vector3.right, gradV = Vector3.up;
            Vector3 centre = plate.position;

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                Vector3 weighted = Vector3.zero, middle = Vector3.zero;
                float area = 0f;
                int biggest = -1;
                float biggestArea = 0f;

                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(verts[tris[i]]);
                    Vector3 b = m.MultiplyPoint3x4(verts[tris[i + 1]]);
                    Vector3 c = m.MultiplyPoint3x4(verts[tris[i + 2]]);
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    float size = cross.magnitude;
                    if (size <= 1e-12f) continue;

                    weighted += cross;
                    middle += (a + b + c) * (size / 3f);
                    area += size;
                    if (size <= biggestArea) continue;
                    biggestArea = size;
                    biggest = i;
                }

                if (area <= 1e-10f || biggest < 0) continue;

                Vector3 slotNormal = weighted.normalized;
                Vector3 slotCentre = middle / area;
                float facing = Vector3.Dot(slotNormal, (slotCentre - housingCentre).normalized);
                if (facing <= bestFacing) continue;

                bestFacing = facing;
                slot = s;
                normal = slotNormal;
                centre = slotCentre;
                (gradU, gradV) = UvAxes(m, verts, uv, tris, biggest, slotNormal);
            }

            Vector3 up = Vector3.ProjectOnPlane(gradV, normal).normalized;
            Vector3 right = Vector3.Cross(up, normal).normalized;

            float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue;
            foreach (int index in mesh.GetTriangles(slot))
            {
                Vector3 d = m.MultiplyPoint3x4(verts[index]) - centre;
                float along = Vector3.Dot(d, right), across = Vector3.Dot(d, up);
                minR = Mathf.Min(minR, along); maxR = Mathf.Max(maxR, along);
                minU = Mathf.Min(minU, across); maxU = Mathf.Max(maxU, across);
            }

            return new Face(centre, normal, up, maxR - minR, maxU - minU, slot);
        }

        /// <summary>
        /// The metres the surface covers per unit of u and of v, solved off one triangle. Falls
        /// back to an arbitrary frame in the plane for a plate that ships without usable UVs.
        /// </summary>
        private static (Vector3, Vector3) UvAxes(Matrix4x4 m, Vector3[] verts, Vector2[] uv, int[] tris,
                                                 int at, Vector3 normal)
        {
            if (uv == null || uv.Length != verts.Length)
                return (Vector3.Cross(Vector3.up, normal), Vector3.up);

            Vector2 uvA = uv[tris[at]], uvB = uv[tris[at + 1]], uvC = uv[tris[at + 2]];
            Vector3 pA = m.MultiplyPoint3x4(verts[tris[at]]);
            Vector3 pB = m.MultiplyPoint3x4(verts[tris[at + 1]]);
            Vector3 pC = m.MultiplyPoint3x4(verts[tris[at + 2]]);

            Vector2 dUv1 = uvB - uvA, dUv2 = uvC - uvA;
            Vector3 dP1 = pB - pA, dP2 = pC - pA;
            float det = dUv1.x * dUv2.y - dUv2.x * dUv1.y;
            if (Mathf.Abs(det) < 1e-8f)
                return (Vector3.Cross(Vector3.up, normal), Vector3.up);

            return ((dP1 * dUv2.y - dP2 * dUv1.y) / det, (dP2 * dUv1.x - dP1 * dUv2.x) / det);
        }
    }
}
