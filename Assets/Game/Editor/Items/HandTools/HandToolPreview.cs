// Renders every carried tool in a Raxy's hand and on its belt, so a grip can be judged by looking.
//
// A tool's grip is baked data (ItemGrip rotation and position offsets, the belt's hang point) and
// it goes wrong quietly: the item is seated exactly as asked and still points at the floor. So this
// uses the real arithmetic: EquipItemSocket seats the grip, BeltSeat hangs the belt, and the
// Upper Body pose is the clip the humanoid profile gives that hold style (HandToolRig). No play
// mode and no scene of its own.
//
// Output: Temp/HandToolPreview/<Id>.png, one row of tiles per tool: held (whole body, close, side)
// then belt (close, back), then the whole body from the side. Temp/HandToolPreview/stowed_<page>.png
// is the contact sheet: every stowable tool hung on the belt or pack, seen from the side its anchor
// faces and from a second side, ToolsPerPage to a page in roster order.
//
// Run from: Tools ▸ SpaceGame ▸ Items ▸ Hand Tools ▸ Preview Held And Belt  /  Preview Stowed Contact Sheets
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    public static class HandToolPreview
    {
        private const string OutputDir = "Temp/HandToolPreview";
        private const int Tile = 420;

        private const int StowedTile = 300;
        private const int ToolsPerRow = 3;
        private const int ToolsPerPage = 12;

        private const int HeldTile = 300;
        private const int HeldToolsPerRow = 2;
        private const int HeldToolsPerPage = 10;
        private const int HeldViews = 3;

        /// <summary>Metres around the palm a close view frames: the hand and the tool leaving it.</summary>
        private const float HandFrame = 0.45f;

        [MenuItem("Tools/SpaceGame/Items/Hand Tools/Preview Held And Belt")]
        public static void PreviewAll() => Preview(null);

        [MenuItem("Tools/SpaceGame/Items/Hand Tools/Preview Held Contact Sheets")]
        public static void PreviewHeldAll() => PreviewHeld(null);

        [MenuItem("Tools/SpaceGame/Items/Hand Tools/Preview Stowed Contact Sheets")]
        public static void PreviewStowedAll() => PreviewStowed(null);

        /// <summary>Batch-mode entry: <c>-executeMethod ...PreviewFromCommandLine -handTools Id,Id</c>; every tool when the flag is absent.</summary>
        public static void PreviewFromCommandLine()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int flag = System.Array.IndexOf(args, "-handTools");
            Preview(flag >= 0 && flag + 1 < args.Length ? args[flag + 1].Split(',') : null);
        }

        /// <param name="ids">Which tools to render, or null for the whole roster.</param>
        public static void Preview(string[] ids)
        {
            Directory.CreateDirectory(OutputDir);

            using HandToolRig rig = HandToolRig.Create();
            if (rig == null) return;

            GameObject stage = CreateStage(out Camera cam);
            try
            {
                foreach (HandToolSpec spec in HandToolRoster.All)
                {
                    if (ids != null && !ids.Contains(spec.Id)) continue;

                    var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(spec.ItemPath);
                    if (item == null || item.itemPrefab == null)
                    {
                        Debug.LogWarning($"[HandToolPreview] {spec.Id}: not built yet.");
                        continue;
                    }

                    var sheet = new Texture2D(Tile * 6, Tile, TextureFormat.RGB24, false);
                    RenderHeld(item.itemPrefab, rig, cam, sheet);
                    RenderBelt(item.itemPrefab, rig, cam, sheet);

                    string file = Path.Combine(OutputDir, spec.Id + ".png");
                    byte[] png = sheet.EncodeToPNG();
                    using (var stream = new FileStream(file, FileMode.Create)) stream.Write(png, 0, png.Length);
                    Object.DestroyImmediate(sheet);
                }
            }
            finally
            {
                Object.DestroyImmediate(stage);
            }

            Debug.Log($"[HandToolPreview] Wrote sheets to {Path.GetFullPath(OutputDir)}");
        }

        /// <summary>
        /// The stowed look of every stowable tool, a page at a time: each tool twice (from the side its anchor
        /// faces, then from the other side), left to right and top to bottom in roster order, which the log repeats.
        /// </summary>
        /// <param name="ids">Which tools to render, or null for the whole roster.</param>
        public static void PreviewStowed(string[] ids)
        {
            Directory.CreateDirectory(OutputDir);

            using HandToolRig rig = HandToolRig.Create();
            if (rig == null) return;

            List<HandToolSpec> specs = HandToolRoster.All.Where(s => ids == null || ids.Contains(s.Id)).ToList();
            GameObject stage = CreateStage(out Camera cam);
            try
            {
                for (int first = 0; first < specs.Count; first += ToolsPerPage)
                {
                    int count = Mathf.Min(ToolsPerPage, specs.Count - first);
                    int rows = (count + ToolsPerRow - 1) / ToolsPerRow;
                    var sheet = new Texture2D(StowedTile * 2 * ToolsPerRow, StowedTile * rows, TextureFormat.RGB24, false);
                    for (int i = 0; i < count; i++)
                    {
                        int column = i % ToolsPerRow * 2;
                        int row = rows - 1 - i / ToolsPerRow;
                        RenderStowed(specs[first + i], rig, cam, sheet, column, row);
                    }

                    string page = $"{OutputDir}/stowed_{first / ToolsPerPage + 1}.png";
                    File.WriteAllBytes(page, sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                    Debug.Log($"[HandToolPreview] {page}: {string.Join(", ", specs.Skip(first).Take(count).Select(s => s.Id))}");
                }
            }
            finally
            {
                Object.DestroyImmediate(stage);
            }
        }

        /// <summary>
        /// The held look of every tool, a page at a time, in roster order (the log repeats it): each tool is the whole
        /// body from the side, then the hand from the front and from the side, framed on the palm so the fist and the
        /// shaft through it can be judged whatever the length of the tool.
        /// </summary>
        /// <param name="ids">Which tools to render, or null for the whole roster.</param>
        /// <param name="tile">Pixels of one view; the default is a contact sheet, a larger one a close look at a few tools.</param>
        public static void PreviewHeld(string[] ids, int tile = HeldTile)
        {
            Directory.CreateDirectory(OutputDir);

            using HandToolRig rig = HandToolRig.Create();
            if (rig == null) return;

            List<HandToolSpec> specs = HandToolRoster.All.Where(s => ids == null || ids.Contains(s.Id)).ToList();
            GameObject stage = CreateStage(out Camera cam);
            try
            {
                for (int first = 0; first < specs.Count; first += HeldToolsPerPage)
                {
                    int count = Mathf.Min(HeldToolsPerPage, specs.Count - first);
                    int rows = (count + HeldToolsPerRow - 1) / HeldToolsPerRow;
                    var sheet = new Texture2D(tile * HeldViews * HeldToolsPerRow, tile * rows, TextureFormat.RGB24, false);
                    for (int i = 0; i < count; i++)
                        RenderHeldTiles(specs[first + i], rig, cam, sheet, tile, i % HeldToolsPerRow * HeldViews, rows - 1 - i / HeldToolsPerRow);

                    string page = $"{OutputDir}/{(tile == HeldTile ? "held" : "held_close")}_{first / HeldToolsPerPage + 1}.png";
                    File.WriteAllBytes(page, sheet.EncodeToPNG());
                    Object.DestroyImmediate(sheet);
                    Debug.Log($"[HandToolPreview] {page}: {string.Join(", ", specs.Skip(first).Take(count).Select(s => s.Id))}");
                }
            }
            finally
            {
                Object.DestroyImmediate(stage);
            }
        }

        private static void RenderHeldTiles(HandToolSpec spec, HandToolRig rig, Camera cam, Texture2D sheet, int tile, int column, int row)
        {
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(spec.ItemPath);
            if (item == null || item.itemPrefab == null) return;

            rig.Pose(item.itemPrefab.GetComponent<ItemGrip>().Style);
            var socket = new EquipItemSocket(rig.Hand, rig.Frame, 1f);
            GameObject held = socket.Equip(item.itemPrefab);
            if (held == null) return;

            try
            {
                Bounds body = BoundsOf(rig.Body);
                body.Encapsulate(BoundsOf(held));
                var hand = new Bounds(socket.GripPosition, Vector3.one * HandFrame * 2f);
                using (rig.Bake())
                {
                    Shoot(cam, body, 290f, 4f, 0f, sheet, column, tile, row);
                    Shoot(cam, hand, 200f, 8f, 0.9f, sheet, column + 1, tile, row);
                    Shoot(cam, hand, 290f, 8f, 0.9f, sheet, column + 2, tile, row);
                }
            }
            finally
            {
                Object.DestroyImmediate(held);
            }
        }

        private static GameObject CreateStage(out Camera cam)
        {
            var stage = new GameObject("HandToolPreviewStage") { hideFlags = HideFlags.DontSave };
            cam = new GameObject("lens").AddComponent<Camera>();
            cam.transform.SetParent(stage.transform, false);
            var key = new GameObject("key").AddComponent<Light>();
            key.transform.SetParent(stage.transform, false);
            key.type = LightType.Directional;
            key.intensity = 1.1f;
            key.transform.rotation = Quaternion.Euler(38f, 155f, 0f);
            return stage;
        }

        private static void RenderHeld(GameObject prefab, HandToolRig rig, Camera cam, Texture2D sheet)
        {
            // The pose the item itself asks for: what was built, not what the roster says.
            rig.Pose(prefab.GetComponent<ItemGrip>().Style);

            var socket = new EquipItemSocket(rig.Hand, rig.Frame, 1f);
            GameObject held = socket.Equip(prefab);
            if (held == null) return;

            try
            {
                Bounds itemBounds = BoundsOf(held);
                Bounds bodyBounds = BoundsOf(rig.Body);

                using (rig.Bake())
                {
                    Shoot(cam, bodyBounds, 200f, 8f, 0f, sheet, 0, Tile);
                    Shoot(cam, bodyBounds, 290f, 4f, 0f, sheet, 5, Tile);
                    Shoot(cam, Merge(itemBounds, rig.Hand.position, 0.5f), 200f, 12f, 0.9f, sheet, 1, Tile);
                    Shoot(cam, Merge(itemBounds, rig.Hand.position, 0.5f), 290f, 12f, 0.9f, sheet, 2, Tile);
                }
            }
            finally
            {
                if (held != null) Object.DestroyImmediate(held);
            }
        }

        private static void RenderBelt(GameObject prefab, HandToolRig rig, Camera cam, Texture2D sheet)
        {
            if (prefab.GetComponent<BeltMount>() == null) return;

            if (!TryHang(prefab, rig, out Dictionary<BeltSlot, Transform> anchors, out BeltSlot slot, out GameObject hung))
            {
                DestroyAnchors(anchors);
                return;
            }

            try
            {
                Bounds bounds = StowedFrame(rig, anchors[slot], slot, hung, out float tightness);
                Shoot(cam, bounds, SideFacing(anchors[slot]), 8f, tightness, sheet, 3, Tile);
                Shoot(cam, bounds, OtherSide(slot), 8f, tightness, sheet, 4, Tile);
            }
            finally
            {
                Object.DestroyImmediate(hung);
                DestroyAnchors(anchors);
            }
        }

        private static void RenderStowed(HandToolSpec spec, HandToolRig rig, Camera cam, Texture2D sheet, int column, int row)
        {
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(spec.ItemPath);
            if (item == null || item.itemPrefab == null || item.itemPrefab.GetComponent<BeltMount>() == null) return;

            if (!TryHang(item.itemPrefab, rig, out Dictionary<BeltSlot, Transform> anchors, out BeltSlot slot, out GameObject hung))
            {
                DestroyAnchors(anchors);
                return;
            }

            try
            {
                Bounds bounds = StowedFrame(rig, anchors[slot], slot, hung, out float tightness);
                Shoot(cam, bounds, SideFacing(anchors[slot]), 6f, tightness, sheet, column, StowedTile, row);
                Shoot(cam, bounds, OtherSide(slot), 6f, tightness, sheet, column + 1, StowedTile, row);
            }
            finally
            {
                Object.DestroyImmediate(hung);
                DestroyAnchors(anchors);
            }
        }

        /// <summary>
        /// Hang the tool where a carrier would put it on the preview Raxy. False, with the reason logged, when
        /// none of the tool's slots is offered; the anchors are made either way and the caller destroys them.
        /// </summary>
        private static bool TryHang(GameObject prefab, HandToolRig rig, out Dictionary<BeltSlot, Transform> anchors,
                                    out BeltSlot slot, out GameObject hung)
        {
            hung = null;
            anchors = BeltSeat.CreateAnchors(rig.Body.transform, rig.Animator);
            if (!BeltSeat.Plan(new[] { prefab.GetComponent<BeltMount>() }, anchors.Keys).TryGetValue(0, out slot))
            {
                Debug.LogWarning($"[HandToolPreview] {prefab.name}: the preview Raxy offers none of its slots.");
                return false;
            }

            hung = BeltSeat.Hang(anchors[slot], prefab, out _);
            return hung != null;
        }

        /// <summary>
        /// A tool slung on the back is judged against the whole body; a hip tool close up. Tightness is
        /// <see cref="Shoot"/>'s zoom: 0 frames the bounds loosely.
        /// </summary>
        private static Bounds StowedFrame(HandToolRig rig, Transform anchor, BeltSlot slot, GameObject hung, out float tightness)
        {
            bool onBack = !IsHip(slot);
            tightness = onBack ? 0f : 0.9f;
            Bounds frame = onBack ? BoundsOf(rig.Body) : BoundsOf(hung);
            frame.Encapsulate(BoundsOf(hung));
            frame.Encapsulate(anchor.position);
            frame.Expand(onBack ? 0.2f : 0.5f);
            return frame;
        }

        private static bool IsHip(BeltSlot slot) => slot == BeltSlot.HipRight || slot == BeltSlot.HipLeft;

        /// <summary>Camera yaw that looks at the anchor from the side it faces out of.</summary>
        private static float SideFacing(Transform anchor) =>
            Mathf.Atan2(-anchor.forward.x, -anchor.forward.z) * Mathf.Rad2Deg;

        /// <summary>The second view: the wearer's right for a back tool, the front for a hip tool.</summary>
        private static float OtherSide(BeltSlot slot) => IsHip(slot) ? 200f : 270f;

        private static void Shoot(Camera cam, Bounds bounds, float yaw, float pitch, float tightness,
                                  Texture2D sheet, int column, int size, int row = 0)
        {
            Vector3 direction = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
            float distance = bounds.extents.magnitude * (tightness > 0f ? 2.6f / (0.5f + tightness) : 2.4f);
            cam.transform.SetPositionAndRotation(bounds.center - direction * distance,
                                                 Quaternion.LookRotation(direction, Vector3.up));
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.43f, 0.45f);

            var target = new RenderTexture(size, size, 24);
            cam.targetTexture = target;
            cam.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(size, size, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            RenderTexture.active = previous;
            cam.targetTexture = null;

            sheet.SetPixels(column * size, row * size, size, size, image.GetPixels());
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }

        private static Bounds BoundsOf(GameObject root)
        {
            Bounds bounds = default;
            bool any = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        private static void DestroyAnchors(Dictionary<BeltSlot, Transform> anchors)
        {
            foreach (Transform anchor in anchors.Values)
                Object.DestroyImmediate(anchor.gameObject);
        }

        private static Bounds Merge(Bounds bounds, Vector3 point, float pad)
        {
            bounds.Encapsulate(point);
            bounds.Expand(pad);
            return bounds;
        }
    }
}
