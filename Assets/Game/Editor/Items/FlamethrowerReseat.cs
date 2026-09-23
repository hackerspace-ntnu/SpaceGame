using System.Text;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Puts the flamethrower's attach points back onto the model's markers after a re-export.
    ///
    /// <para>
    /// The prefab carries hand-authored content the jet builder owns — particle systems, a light,
    /// sounds, colliders — so there is no builder that rebuilds this item from nothing. What a
    /// re-export DOES break is mechanical: the muzzle, the jet, the grip point and the supply
    /// gauge are empties sitting at hard-coded positions that were read off the model's
    /// <c>Marker_*</c> meshes once, by hand. Move a part in the .blend and those numbers point at
    /// air, with nothing in the console to say so — the flame simply leaves from the middle of the
    /// barrel and the gauge floats beside the gun.
    /// </para>
    /// <para>
    /// So this reads the markers again. Each seat is stored as the offset, rotation and scale it
    /// was authored at <b>in its marker's own frame</b>, which is what makes it survive a hand edit
    /// that moves, turns or resizes a whole cluster: the marker rides along with the parts around
    /// it, and the seat rides with the marker. <see cref="AuthoredMarker"/> and
    /// <see cref="AuthoredMarkerScale"/> are the pose every marker had in the generated
    /// 2026-09-07 export, which is the frame the authored numbers below were measured in.
    /// </para>
    /// <para>
    /// <b>Verified out loud.</b> Unity discards prefab saves when the AssetDatabase is read-only
    /// and says nothing, so the prefab is re-loaded off disk
    /// afterwards and every seat re-measured against the marker it should be sitting on.
    /// </para>
    /// </summary>
    public static class FlamethrowerReseat
    {
        private const string LogTag = "Flamethrower";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/Flamethrower.prefab";
        private const string ModelChild = "Model";
        private const string MarkerPrefix = "Marker_";

        /// <summary>
        /// The rotation every <c>Marker_*</c> had in the export the seats below were measured
        /// against: Blender +Z up arriving as Unity +Y up, and nothing else.
        /// </summary>
        private static readonly Quaternion AuthoredMarker = Quaternion.Euler(-90f, 0f, 0f);

        /// <summary>
        /// The scale those markers arrived at — the FBX importer's centimetre conversion, on an
        /// object the generator left at scale 1. A marker that comes back at twice this was scaled
        /// by hand along with the parts around it, and its seat is scaled to match.
        /// </summary>
        private const float AuthoredMarkerScale = 100f;

        /// <summary>One attach point, as it was authored against <see cref="AuthoredMarker"/>.</summary>
        private readonly struct Seat
        {
            /// <summary>Path under the prefab root.</summary>
            public readonly string Path;
            /// <summary>Marker inside the model that carries this seat.</summary>
            public readonly string Marker;
            /// <summary>Authored offset from the marker, in the prefab root's own axes.</summary>
            public readonly Vector3 Offset;
            /// <summary>Authored rotation, in the prefab root's own axes.</summary>
            public readonly Vector3 Euler;
            /// <summary>Authored local scale, or zero to leave the scale alone.</summary>
            public readonly Vector3 Scale;

            public Seat(string path, string marker, Vector3 offset, Vector3 euler, Vector3 scale = default)
            {
                Path = path;
                Marker = marker;
                Offset = offset;
                Euler = euler;
                Scale = scale;
            }
        }

        /// <summary>
        /// Every seat on the item. The muzzle, the jet and the grip sit ON their markers, which is
        /// what those markers are for. The gauge pair sits a few millimetres proud of its marker so
        /// the bar clears the gauge face it is drawn over, and is the only seat with a size: it is
        /// two flat plates rather than an empty, so it has to grow with the instrument it covers.
        /// </summary>
        private static readonly Seat[] Seats =
        {
            new("Muzzle", "Marker_Muzzle", Vector3.zero, Vector3.zero),
            new("Jet", "Marker_Muzzle", Vector3.zero, Vector3.zero),
            new("Jet/Pilot", "Marker_Pilot", Vector3.zero, Vector3.zero),
            new("GripPoint", "Marker_Grip", Vector3.zero, Vector3.zero),
            new("Gauge_Track", "Marker_Gauge", new Vector3(-0.0033f, 0f, 0f), new Vector3(0f, 270f, 0f),
                new Vector3(0.068f, 0.018f, 0.0008f)),
            new("Gauge_Anchor", "Marker_Gauge", new Vector3(-0.0041f, 0f, -0.034f), new Vector3(0f, 270f, 0f)),
        };

        /// <summary>
        /// The lit bar, a child of <c>Gauge_Anchor</c> whose parent is scaled at runtime to fill it.
        /// Its own numbers are in the anchor's frame, so they only need the marker's size factor.
        /// </summary>
        private static readonly Vector3 FillPosition = new(0.034f, 0f, 0f);
        private static readonly Vector3 FillScale = new(0.068f, 0.015f, 0.0016f);

        [MenuItem("Tools/SpaceGame/Items/Reseat Flamethrower")]
        public static void Reseat()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            var log = new StringBuilder($"[{LogTag}] reseat\n");
            try
            {
                Transform model = contents.transform.Find(ModelChild);
                if (model == null)
                {
                    Debug.LogError($"[{LogTag}] No '{ModelChild}' child on {PrefabPath}.");
                    return;
                }

                foreach (Seat seat in Seats)
                {
                    Transform t = contents.transform.Find(seat.Path);
                    Transform marker = model.Find(seat.Marker);
                    if (t == null || marker == null)
                    {
                        Debug.LogError($"[{LogTag}] Missing {(t == null ? seat.Path : seat.Marker)}.");
                        return;
                    }

                    Apply(t, marker, seat, Axis(model));
                    log.Append("  ").Append(seat.Path).Append("  -> ").Append(seat.Marker)
                       .Append("  ").Append(t.localPosition.ToString("F4")).Append('\n');
                }

                Transform fill = contents.transform.Find("Gauge_Anchor/Gauge_Fill");
                Transform gauge = model.Find("Marker_Gauge");
                if (fill == null)
                {
                    Debug.LogError($"[{LogTag}] Missing Gauge_Anchor/Gauge_Fill.");
                    return;
                }

                float gaugeFactor = Factor(gauge);
                fill.localPosition = FillPosition * gaugeFactor;
                fill.localScale = FillScale * gaugeFactor;
                log.Append("  Gauge_Fill  x").Append(gaugeFactor.ToString("F3")).Append('\n');

                log.Append("  markers hidden: ").Append(HideMarkers(model)).Append('\n');
                log.Append("  collider: ").Append(FitCollider(contents)).Append('\n');

                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            AssetDatabase.SaveAssets();
            Verify(log);
            Debug.Log(log.ToString());
        }

        /// <summary>How much bigger the marker is than the export it was authored against.</summary>
        private static float Factor(Transform marker) => marker.localScale.x / AuthoredMarkerScale;

        private static void Apply(Transform t, Transform marker, Seat seat, Vector3 axis)
        {
            t.position = Seated(marker, seat, axis);
            t.rotation = marker.rotation * (Quaternion.Inverse(AuthoredMarker) * Quaternion.Euler(seat.Euler));
            if (seat.Scale != Vector3.zero) t.localScale = seat.Scale * Factor(marker);
        }

        /// <summary>Where a seat belongs: its authored offset, carried by the marker's own frame.</summary>
        private static Vector3 Seated(Transform marker, Seat seat, Vector3 axis)
        {
            Vector3 offset = marker.rotation *
                             (Quaternion.Inverse(AuthoredMarker) * seat.Offset * Factor(marker));
            return marker.position + Outward(offset, marker.position, axis);
        }

        /// <summary>
        /// The part of an offset that stands the seat off the model's skin, forced to point away
        /// from the gun rather than into it.
        ///
        /// <para>
        /// The gauge bar is drawn a couple of millimetres proud of the instrument it covers, and
        /// which way "proud" is comes out of the marker's rotation — which is its part's rotation.
        /// The 2026-09-09 edit moved the gauge to the other flank, so that rotation now carries a
        /// flip, and honouring it verbatim buries the bar inside the fuel bottle where it renders
        /// as nothing at all. Everything else about the marker's frame is worth keeping; only the
        /// sign of the radial step is not.
        /// </para>
        /// </summary>
        private static Vector3 Outward(Vector3 offset, Vector3 at, Vector3 axis)
        {
            Vector3 radial = at - new Vector3(axis.x, axis.y, at.z);
            if (radial.sqrMagnitude < 1e-8f) return offset;

            Vector3 n = radial.normalized;
            float along = Vector3.Dot(offset, n);
            return along < 0f ? offset - 2f * along * n : offset;
        }

        /// <summary>The model's own centre, which the gun's long axis runs through.</summary>
        private static Vector3 Axis(Transform model)
        {
            Bounds bounds = default;
            bool any = false;
            foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.gameObject.activeInHierarchy) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }

            return any ? bounds.center : Vector3.zero;
        }

        /// <summary>
        /// Markers are 4 mm cubes with a renderer on them, so one left visible is a speck of
        /// material floating on the gun. A hand edit that duplicates a cluster brings its markers
        /// along as <c>Marker_Grip.001</c> and friends, which arrive switched on.
        /// </summary>
        private static int HideMarkers(Transform model)
        {
            int hidden = 0;
            foreach (Transform t in model)
            {
                if (!t.name.StartsWith(MarkerPrefix) || !t.gameObject.activeSelf) continue;
                t.gameObject.SetActive(false);
                hidden++;
            }

            return hidden;
        }

        /// <summary>
        /// The pickup box, refitted to what the model is now. It is the item's whole presence in
        /// the world — what you aim at to pick it up and what it lands on — so a box left at the
        /// old model's length leaves a longer gun half inside the sand.
        /// </summary>
        private static string FitCollider(GameObject contents)
        {
            var box = contents.GetComponent<BoxCollider>();
            if (box == null) return "none";

            var renderers = contents.GetComponentsInChildren<MeshRenderer>();
            Bounds bounds = default;
            bool any = false;
            foreach (MeshRenderer r in renderers)
            {
                if (!r.gameObject.activeInHierarchy) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (!any) return "no renderers";

            box.center = contents.transform.InverseTransformPoint(bounds.center);
            box.size = bounds.size;
            return $"center {box.center.ToString("F4")} size {box.size.ToString("F4")}";
        }

        /// <summary>Re-read the saved prefab and prove every seat landed on its marker.</summary>
        private static void Verify(StringBuilder log)
        {
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Transform model = saved.transform.Find(ModelChild);
            Vector3 axis = Axis(model);
            foreach (Seat seat in Seats)
            {
                Transform t = saved.transform.Find(seat.Path);
                Transform marker = model.Find(seat.Marker);
                float off = Vector3.Distance(t.position, Seated(marker, seat, axis));
                log.Append(off < 0.0005f ? "  OK       " : "  DISCARDED ")
                   .Append(seat.Path).Append("  ").Append((off * 1000f).ToString("F2")).Append(" mm\n");
            }
        }
    }
}
