using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Renders one inventory icon per <see cref="InventoryItem"/> from that item's own
    /// <see cref="InventoryItem.itemPrefab"/>, and writes the result back into the asset's
    /// <see cref="InventoryItem.icon"/> field.
    ///
    /// This exists beside <see cref="IconGenerator"/> rather than replacing it. That window is
    /// the manual tool: you hand it one prefab and dial in pitch, yaw and zoom by eye. It is
    /// still the right thing for a one-off. But because every icon was framed by hand, no two
    /// in the set share a framing, and because nothing ties the rendered prefab back to the
    /// asset, several items ended up pointing at another item's sprite entirely — the basic gun
    /// wore the grappling hook's icon, the ball lightning weapon wore a grey sphere.
    ///
    /// So the rule here is that the prefab is never chosen by name, by folder, or by hand: it is
    /// read from the asset being written to — <see cref="InventoryItem.iconPrefab"/> when the item
    /// sets one (for items whose held form is not what the icon should show), otherwise
    /// <see cref="InventoryItem.itemPrefab"/>. An item whose <c>itemPrefab</c> is null is skipped
    /// and reported rather than guessed at.
    /// </summary>
    public static class BatchIconGenerator
    {
        private const string ItemsRoot = "Assets/Game/Resources/Items";
        private const string SpriteDir = "Assets/Game/Art/Sprites/Items";
        private const int Resolution = 256;

        /// <summary>Neutral grey. Opaque, so icons read the same on any slot background.</summary>
        private static readonly Color Background = new Color32(0x3A, 0x3A, 0x3A, 0xFF);

        /// <summary>Fraction of the frame the model fills. Leaves a consistent margin.</summary>
        private const float Fill = 0.82f;

        /// <summary>The house 3/4 view: high enough to show a top face, turned enough to show two sides.</summary>
        private static readonly Vector2 DefaultAngle = new Vector2(22f, 135f);

        /// <summary>
        /// Per-asset (pitch, yaw) overrides.
        ///
        /// Only needed where two items share one model: <c>basicgun</c> and
        /// <c>BallLightningWeapon</c> are both <c>cixinGunFinal.fbx</c>, so rendering them from
        /// the same angle would produce two identical icons. Shooting them from opposite sides
        /// at least keeps them tellable apart in the hotbar. The real fix is a distinct model for
        /// the ball lightning weapon.
        /// </summary>
        private static readonly Dictionary<string, Vector2> AngleOverrides =
            new Dictionary<string, Vector2>
            {
                { "basicgun", new Vector2(18f, 125f) },
                { "BallLightningWeapon", new Vector2(30f, 305f) },
            };

        [MenuItem("Tools/Generate Icon For Selected Item")]
        private static void GenerateForSelection()
        {
            var item = Selection.activeObject as InventoryItem;
            if (item == null)
            {
                Debug.LogWarning("Select an InventoryItem asset first.");
                return;
            }

            Debug.Log(GenerateFor(item, out string note)
                ? "Icon regenerated for " + item.name
                  + (note.Length > 0 ? "   (" + note + ")" : "")
                : "Could not regenerate " + item.name + " — " + note);
        }

        /// <summary>
        /// Re-render one item's icon in place, with the same framing <see cref="GenerateAll"/>
        /// uses.
        ///
        /// <para>
        /// For the common case of a single model changing. <see cref="GenerateAll"/> rewrites
        /// every PNG in the set, so a one-model change arrives as forty modified files and the
        /// reviewer cannot see which one mattered. This writes over the sprite the item already
        /// owns and touches nothing else — which is also why it only works for an item that has
        /// one. An item with no icon yet has no path to write to and no claim on one; deciding
        /// that is the batch pass's job, so this reports and declines.
        /// </para>
        /// </summary>
        public static bool GenerateFor(InventoryItem item, out string note)
        {
            note = "";
            if (item == null) { note = "no item"; return false; }
            if (item.itemPrefab == null) { note = "itemPrefab is null"; return false; }
            if (item.icon == null) { note = "no icon yet — run Generate All Item Icons"; return false; }

            string target = AssetDatabase.GetAssetPath(item.icon);
            Vector2 angle = AngleOverrides.TryGetValue(item.name, out Vector2 a) ? a : DefaultAngle;
            GameObject renderPrefab = item.iconPrefab != null ? item.iconPrefab : item.itemPrefab;

            Texture2D tex = Render(renderPrefab, angle, out note);
            if (tex == null) return false;

            File.WriteAllBytes(target, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.Refresh();
            ImportAsSprite(target);
            AssetDatabase.SaveAssets();
            return true;
        }

        [MenuItem("Tools/Generate All Item Icons")]
        public static void GenerateAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:InventoryItem", new[] { ItemsRoot });
            var items = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<InventoryItem>)
                .Where(i => i != null)
                .OrderBy(i => i.name)
                .ToList();

            Directory.CreateDirectory(SpriteDir);

            // An icon file is only safe to overwrite in place when it belongs to exactly one
            // item. Where two assets currently share a sprite, one of them is wrong — writing
            // over it would corrupt whichever item it legitimately belonged to.
            var iconUsers = new Dictionary<string, int>();
            foreach (var item in items)
            {
                if (item.icon == null) continue;
                string p = AssetDatabase.GetAssetPath(item.icon);
                iconUsers.TryGetValue(p, out int n);
                iconUsers[p] = n + 1;
            }

            var log = new StringBuilder();
            var written = new List<string>();

            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    InventoryItem item = items[i];
                    EditorUtility.DisplayProgressBar("Generating item icons",
                        item.name, (float)i / items.Count);

                    if (item.itemPrefab == null)
                    {
                        log.Append("\nSKIP  " + item.name + " — itemPrefab is null");
                        continue;
                    }

                    string current = item.icon != null
                        ? AssetDatabase.GetAssetPath(item.icon) : null;
                    bool ownsCurrent = current != null
                        && iconUsers.TryGetValue(current, out int users) && users == 1;
                    string target = ownsCurrent
                        ? current
                        : Path.Combine(SpriteDir, item.name + ".png").Replace('\\', '/');

                    Vector2 angle = AngleOverrides.TryGetValue(item.name, out Vector2 a)
                        ? a : DefaultAngle;

                    GameObject renderPrefab = item.iconPrefab != null
                        ? item.iconPrefab : item.itemPrefab;

                    Texture2D tex = Render(renderPrefab, angle, out string note);
                    if (tex == null)
                    {
                        log.Append("\nSKIP  " + item.name + " — " + note);
                        continue;
                    }

                    File.WriteAllBytes(target, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    written.Add(target);

                    log.Append("\nOK    " + item.name.PadRight(22)
                        + renderPrefab.name.PadRight(22)
                        + "-> " + target.Substring(SpriteDir.Length + 1)
                        + (note.Length > 0 ? "   (" + note + ")" : ""));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            foreach (string path in written) ImportAsSprite(path);

            // Reassign only after import, so the Sprite sub-asset exists to bind to.
            foreach (var item in items)
            {
                if (item.itemPrefab == null) continue;
                string current = item.icon != null
                    ? AssetDatabase.GetAssetPath(item.icon) : null;
                bool ownsCurrent = current != null
                    && iconUsers.TryGetValue(current, out int users) && users == 1;
                string target = ownsCurrent
                    ? current
                    : Path.Combine(SpriteDir, item.name + ".png").Replace('\\', '/');
                if (!written.Contains(target)) continue;

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(target);
                if (sprite != null && item.icon != sprite)
                {
                    item.icon = sprite;
                    EditorUtility.SetDirty(item);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Item icons regenerated:" + log);
        }

        /// <summary>
        /// Renders one prefab against the neutral background, framed from its own bounds.
        /// Returns null when the prefab has nothing visible to shoot.
        /// </summary>
        private static Texture2D Render(GameObject prefab, Vector2 angle, out string note)
        {
            note = "";
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
                // stripped for the same reason and because they render nothing at time zero.
                int stripped = 0;
                foreach (var lr in inst.GetComponentsInChildren<LineRenderer>(true))
                { Object.DestroyImmediate(lr); stripped++; }
                foreach (var tr in inst.GetComponentsInChildren<TrailRenderer>(true))
                { Object.DestroyImmediate(tr); stripped++; }
                foreach (var ps in inst.GetComponentsInChildren<ParticleSystem>(true))
                { Object.DestroyImmediate(ps); stripped++; }
                if (stripped > 0) note = "stripped " + stripped + " line/particle";

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
                cam.backgroundColor = Background;

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

                preview.BeginStaticPreview(new Rect(0, 0, Resolution, Resolution));
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

        private static void ImportAsSprite(string assetPath)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
