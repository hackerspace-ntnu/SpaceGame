// Renders every carried tool in a Raxy's hand and on its belt, so a grip can be judged by looking.
//
// A tool's grip is baked data (ItemGrip rotation and position offsets, the belt's hang point) and
// it goes wrong quietly: the item is seated exactly as asked and still points at the floor. So this
// uses the real arithmetic: EquipItemSocket seats the grip, BeltSeat hangs the belt, and the
// Upper Body pose is the clip the humanoid profile gives that hold style (HandToolRig). No play
// mode and no scene of its own.
//
// Output: Temp/HandToolPreview/<Id>.png, one row of tiles per tool: held (whole body, close, side)
// then belt (close, back), then the whole body from the side.
//
// Run from: Tools ▸ SpaceGame ▸ Items ▸ Hand Tools ▸ Preview Held And Belt
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

        [MenuItem("Tools/SpaceGame/Items/Hand Tools/Preview Held And Belt")]
        public static void PreviewAll() => Preview(null);

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

            var stage = new GameObject("HandToolPreviewStage") { hideFlags = HideFlags.DontSave };
            var cam = new GameObject("lens").AddComponent<Camera>();
            cam.transform.SetParent(stage.transform, false);
            var key = new GameObject("key").AddComponent<Light>();
            key.transform.SetParent(stage.transform, false);
            key.type = LightType.Directional;
            key.intensity = 1.1f;
            key.transform.rotation = Quaternion.Euler(38f, 155f, 0f);

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

                Shoot(cam, bodyBounds, 200f, 8f, 0f, sheet, 0);
                Shoot(cam, bodyBounds, 290f, 4f, 0f, sheet, 5);
                Shoot(cam, Merge(itemBounds, rig.Hand.position, 0.5f), 200f, 12f, 0.9f, sheet, 1);
                Shoot(cam, Merge(itemBounds, rig.Hand.position, 0.5f), 290f, 12f, 0.9f, sheet, 2);
            }
            finally
            {
                if (held != null) Object.DestroyImmediate(held);
            }
        }

        private static void RenderBelt(GameObject prefab, HandToolRig rig, Camera cam, Texture2D sheet)
        {
            if (prefab.GetComponent<BeltMount>() == null) return;

            // The slot the item prefers, on whatever belt or pack the preview Raxy wears.
            Dictionary<BeltSlot, Transform> anchors = BeltSeat.CreateAnchors(rig.Body.transform, rig.Animator);
            BeltSlot slot = prefab.GetComponent<BeltMount>().Preferred;
            if (!anchors.TryGetValue(slot, out Transform anchor))
            {
                Debug.LogWarning($"[HandToolPreview] {prefab.name}: the preview Raxy offers no {slot} mount.");
                DestroyAnchors(anchors);
                return;
            }

            GameObject hung = BeltSeat.Hang(anchor, prefab, out _);
            try
            {
                if (hung == null) return;

                Bounds bounds = Merge(BoundsOf(hung), anchor.position, 0.5f);
                float yaw = slot == BeltSlot.HipRight ? 90f : slot == BeltSlot.HipLeft ? 270f : 20f;
                Shoot(cam, bounds, yaw, 8f, 0.9f, sheet, 3);
                Shoot(cam, Merge(BoundsOf(hung), anchor.position, 0.5f), 200f, 8f, 0.9f, sheet, 4);
            }
            finally
            {
                if (hung != null) Object.DestroyImmediate(hung);
                DestroyAnchors(anchors);
            }
        }

        private static void Shoot(Camera cam, Bounds bounds, float yaw, float pitch, float tightness,
                                  Texture2D sheet, int tileIndex)
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

            var target = new RenderTexture(Tile, Tile, 24);
            cam.targetTexture = target;
            cam.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Tile, Tile, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Tile, Tile), 0, 0);
            RenderTexture.active = previous;
            cam.targetTexture = null;

            sheet.SetPixels(tileIndex * Tile, 0, Tile, Tile, image.GetPixels());
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
