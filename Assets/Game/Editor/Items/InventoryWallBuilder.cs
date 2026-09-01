using System.Text;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Builds <c>InventoryWall.prefab</c> from <c>inventory_wall.fbx</c>.
    ///
    /// <para>
    /// The same shape as <see cref="ExpeditionRigWiring"/> and for the same reasons: the prefab is
    /// rebuilt from the model rather than hand-edited, so a re-export cannot leave the wiring
    /// pointing at transforms that no longer exist; and it is <b>idempotent</b> — it overwrites the
    /// same asset path, so the GUID everything references survives a second run.
    /// </para>
    /// <para>
    /// The one thing that is genuinely authored rather than derived — whatever gear was left on the
    /// wall's starting-contents list — is read back off the previous prefab and put back, exactly
    /// as the rig's is.
    /// </para>
    /// <para>
    /// <b>The prefab is deliberately NOT registered as a network prefab.</b> It is never spawned at
    /// runtime; it is a child of a ship that is spawned, so it replicates as part of that ship's
    /// NetworkObject. Registering it would make it a second, separately spawned entity that had to
    /// be re-parented by hand on every machine.
    /// </para>
    /// </summary>
    public static class InventoryWallBuilder
    {
        private const string Fbx = "Assets/Game/Art/Models/Props/inventory_wall.fbx";
        private const string Folder = "Assets/Game/Prefabs/Items/Equipment";
        private const string Prefab = Folder + "/InventoryWall.prefab";

        // The BASE player, not the networked variant: the variant inherits every component added
        // here, and a script GUID grepped for on PlayerCharacterNetworked.prefab finds nothing that
        // is nevertheless there.
        private const string PlayerPrefab =
            "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";

        /// <summary>
        /// The placement face, and the numbers the model prints at build time.
        ///
        /// 30 x 22 cells of <c>PackGrid.Cell</c> exactly, so the grid fills the face edge to edge
        /// with zero hem and the model's webbing lines up with it. Change this only in whole
        /// cells, and change <c>inventory_wall.py</c>'s <c>GRID_W</c>/<c>GRID_H</c> with it.
        ///
        /// <para>
        /// The wall is one continuous grid whose bays are exactly six cells wide, so a face that
        /// is not a whole number of cells across puts the model's bay dividers out of step with
        /// the lattice the player is dropping gear onto — so the ACROSS count is a multiple of
        /// six, or a part-bay is left at one end. Expressed as the count times the cell rather
        /// than as 4.05 x 2.97, because the count is the authored decision and the metres are a
        /// consequence of it — unlike the rig's faces, which are measurements of a model.
        /// </para>
        /// <para>
        /// RE-CUT 2026-09-01 to fit the ship. The face was 60 x 30, which the same day's 1.5x
        /// enlargement (<see cref="PackScale.Factor"/>) turned into an 8.46 x 4.95 m fitting the
        /// lander's aft room does not have room for. Measured against the ship's baked collision
        /// at <c>PlayerShipBuilder.WallRibClearance</c>, the room offers 4.37 m of headroom over
        /// the fitting's footprint; 30 x 22 stands 3.87 m off the deck and 4.41 m along it, which
        /// leaves 0.50 m of air above the header, 0.75 m clear forward and 1.54 m aft. Capacity
        /// went 1800 -> 660 cells; that is the cost, and it is paid in one place.
        /// </para>
        /// <para>
        /// <b>This is the LOGICAL face and it did not move again.</b> The wall is DRAWN 1.06x
        /// larger than this since 2026-09-01 (<see cref="PackScale.WallDisplay"/>, stamped onto
        /// the prefab by <see cref="SetDisplayScale"/> and matched in the model by
        /// <c>inventory_wall_scale.py</c>) — a 4.293 x 3.148 m board over a 4.05 x 2.97 m
        /// rectangle. That enlargement moves no count, no cell and no stored uv: it is only the
        /// mapping from a uv to a point on screen. Do not "reconcile" the two numbers by editing
        /// this one, and do not raise the display scale to reach a bigger board — the room caps it
        /// at 1.065 and the constant carries the arithmetic.
        /// </para>
        /// </summary>
        private const string SurfaceNode = "SURF_WallGrid";
        private const int SurfaceCellsAcross = 30;
        private const int SurfaceCellsUp = 22;
        private static readonly Vector2 SurfaceSize =
            new(SurfaceCellsAcross * PackGrid.Cell, SurfaceCellsUp * PackGrid.Cell);

        [MenuItem("Tools/SpaceGame/Items/Build Inventory Wall Prefab")]
        public static void Build()
        {
            var log = new StringBuilder("[InventoryWall]\n");

            if (!ConfigureModel(log)) { Debug.LogError(log.ToString()); return; }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (model == null)
            {
                log.Append("  MISSING  ").Append(Fbx)
                   .Append(" — run models/props/inventory_wall_export.py first.\n");
                Debug.LogError(log.ToString());
                return;
            }

            Object[] contents = ReadStartingContents();

            GameObject staged = Object.Instantiate(model);
            staged.name = "InventoryWall";

            try
            {
                // The container FIRST, and its display scale with it. PackSurface reads that scale
                // by walking up to its container, and everything below — the rect's own corner
                // offset, the face collider — is already in the drawn frame. A face attached
                // before the wall exists measures itself at 1 and comes out 6% adrift of the model.
                var wall = staged.AddComponent<WallInventory>();
                staged.AddComponent<WallInventoryNetwork>();
                staged.AddComponent<WallInventorySaveable>();

                SetDisplayScale(wall, log);

                PackSurface surface = AttachSurface(staged.transform, log);
                if (surface == null) { Debug.LogError(log.ToString()); return; }

                WireWall(wall, surface, contents, log);
                EnsureFaceCollider(staged, surface, log);
                DropStrayChannel(staged, log);

                if (!AssetDatabase.IsValidFolder(Folder))
                {
                    log.Append("  MISSING  ").Append(Folder).Append(" does not exist.\n");
                    Debug.LogError(log.ToString());
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(staged, Prefab);
            }
            finally
            {
                Object.DestroyImmediate(staged);
            }

            // Unity discards prefab saves when the AssetDatabase goes read-only, and a stale import
            // artifact can make a correct YAML reference resolve to null at runtime while git stays
            // clean. So the last thing this does is read back what it wrote and say so.
            AssetDatabase.ImportAsset(Prefab, ImportAssetOptions.ForceUpdate);

            var written = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var readback = written != null ? written.GetComponent<WallInventory>() : null;
            PackSurface face = written != null
                ? written.GetComponentInChildren<PackSurface>(true)
                : null;

            if (readback == null || face == null)
            {
                log.Append("  FAILED   the saved prefab has no ")
                   .Append(readback == null ? "WallInventory" : "PackSurface").Append(".\n");
                Debug.LogError(log.ToString());
                return;
            }

            WirePlayer(log);

            log.Append("  wrote    ").Append(Prefab).Append("  face ").Append(face.Id)
               .Append(' ').Append(face.Size.x.ToString("0.00")).Append(" x ")
               .Append(face.Size.y.ToString("0.00")).Append(" m, ")
               .Append(PackGrid.CellsOn(face.Size).x).Append(" x ")
               .Append(PackGrid.CellsOn(face.Size).y).Append(" cells, drawn x")
               .Append(face.DisplayScale.ToString("0.00")).Append(" (")
               .Append((face.Size.x * face.DisplayScale).ToString("0.00")).Append(" x ")
               .Append((face.Size.y * face.DisplayScale).ToString("0.00")).Append(" m)\n");

            Debug.Log(log.ToString());
        }

        /// <summary>
        /// Give the player the component that aims at a wall.
        ///
        /// <para>
        /// Wired here rather than by hand, for the reason the rest of this file is a script: the
        /// wall is useless without it, and a hand-added component is one somebody has to remember
        /// on every fresh checkout. Idempotent — a player that already has one is left alone.
        /// </para>
        /// <para>
        /// On the base <c>PlayerCharacter</c>, so the networked variant inherits it. It is
        /// owner-gated at runtime, so a replica of somebody else's body carries a component that
        /// does nothing, which is what every other local-only player component here does too.
        /// </para>
        /// </summary>
        private static void WirePlayer(StringBuilder log)
        {
            GameObject player = PrefabUtility.LoadPrefabContents(PlayerPrefab);

            if (player == null)
            {
                log.Append("  MISSING  ").Append(PlayerPrefab)
                   .Append(", so nothing on the player can aim at the wall.\n");
                return;
            }

            try
            {
                if (player.GetComponent<WallAimController>() != null)
                {
                    log.Append("  player   already has WallAimController\n");
                    return;
                }

                player.AddComponent<WallAimController>();
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPrefab);
                log.Append("  player   WallAimController added to ").Append(PlayerPrefab).Append('\n');
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(player);
            }
        }

        // ── The model ────────────────────────────────────────────────────────

        /// <summary>
        /// The import settings the wall needs, all of which fail silently when wrong.
        ///
        /// Copied from <see cref="ExpeditionRigWiring"/>'s, because they are the settings this
        /// project's palette materials are known to resolve under, and because the same trap
        /// applies: <c>SURF_WallGrid</c> is a childless empty, and anything that lets Unity fold a
        /// childless transform away takes the placement face with it.
        /// </summary>
        private static bool ConfigureModel(StringBuilder log)
        {
            var importer = AssetImporter.GetAtPath(Fbx) as ModelImporter;

            if (importer == null)
            {
                log.Append("  MISSING  ").Append(Fbx)
                   .Append(" — run models/props/inventory_wall_export.py first.\n");
                return false;
            }

            bool dirty = false;

            // 1 Blender unit is 1 metre and the FBX says so. useFileScale = false was tried on the
            // first pack in this library and inflated it to 34 x 53 x 23 m; do not go back there.
            if (!importer.useFileScale) { importer.useFileScale = true; dirty = true; }
            if (!Mathf.Approximately(importer.globalScale, 1f)) { importer.globalScale = 1f; dirty = true; }

            if (importer.bakeAxisConversion) { importer.bakeAxisConversion = false; dirty = true; }
            if (importer.optimizeGameObjects) { importer.optimizeGameObjects = false; dirty = true; }
            if (!importer.importVisibility) { importer.importVisibility = true; dirty = true; }

            if (importer.importAnimation) { importer.importAnimation = false; dirty = true; }
            if (importer.animationType != ModelImporterAnimationType.None)
            {
                importer.animationType = ModelImporterAnimationType.None;
                dirty = true;
            }

            if (importer.importBlendShapes) { importer.importBlendShapes = false; dirty = true; }
            if (importer.importCameras) { importer.importCameras = false; dirty = true; }
            if (importer.importLights) { importer.importLights = false; dirty = true; }

            // The wall gets ONE box over its face, added below. Per-mesh colliders here would put a
            // MeshCollider on every bay and on the lamp, and the ray that resolves the aim would
            // then hit the webbing rather than the fitting.
            if (importer.addCollider) { importer.addCollider = false; dirty = true; }

            if (!importer.isReadable) { importer.isReadable = true; dirty = true; }

            if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                dirty = true;
            }
            if (importer.materialLocation != ModelImporterMaterialLocation.InPrefab)
            {
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
                log.Append("  import   settings corrected on ").Append(Fbx).Append('\n');
            }

            return true;
        }

        /// <summary>
        /// Put a <see cref="PackSurface"/> on the model's face empty.
        ///
        /// <para>
        /// On a CHILD of the empty, not on the empty itself, and offset to the face's lower-left
        /// corner: a <c>PackSurface</c>'s uv origin is its own transform, so the component has to
        /// sit at (0,0) of the rectangle while the model's empty is authored at its centre. The rig
        /// does exactly this, and for the same reason.
        /// </para>
        /// <para>
        /// The offset is divided by the empty's lossy scale, for the reason
        /// <c>PackSurface.ToWorld</c> divides: a local offset is in the parent's units, and this
        /// project's FBXs have arrived on the centimetre convention before — mesh data 100x small
        /// under transforms 100x large. Without the divide a 2.7 m half-width is 270 m of offset.
        /// </para>
        /// </summary>
        private static PackSurface AttachSurface(Transform root, StringBuilder log)
        {
            Transform empty = FindDeep(root, SurfaceNode);

            if (empty == null)
            {
                log.Append("  MISSING  no '").Append(SurfaceNode).Append("' under ").Append(Fbx)
                   .Append(", so the wall has no placement face.\n");
                return null;
            }

            var go = new GameObject(SurfaceNode + "_Rect");
            Transform corner = go.transform;
            corner.SetParent(empty, false);
            corner.localRotation = Quaternion.identity;
            corner.localScale = Vector3.one;

            Vector3 s = empty.lossyScale;
            float sx = Mathf.Abs(s.x) < 1e-6f ? 1f : Mathf.Abs(s.x);
            float sz = Mathf.Abs(s.z) < 1e-6f ? 1f : Mathf.Abs(s.z);

            // In the DRAWN frame: the model's face empty is authored at the centre of a board the
            // model draws at PackScale.WallDisplay, so the offset to its (0,0) corner is half the
            // face times that scale. SurfaceSize itself stays the logical rectangle below — this
            // is the one place in the builder where the two frames are visibly different.
            float drawn = PackScale.WallDisplay;

            corner.localPosition = new Vector3(-SurfaceSize.x * drawn * 0.5f / sx,
                                               0f,
                                               -SurfaceSize.y * drawn * 0.5f / sz);

            var surface = go.AddComponent<PackSurface>();

            var so = new SerializedObject(surface);
            SetEnum(so, "id", (int)PackSurfaceId.WallGrid, log);
            SetVector2(so, "size", SurfaceSize, log);
            so.ApplyModifiedPropertiesWithoutUndo();

            return surface;
        }

        /// <summary>
        /// How much bigger than its own grid the wall is drawn.
        ///
        /// <para>
        /// Stamped from <see cref="PackScale.WallDisplay"/> rather than typed into the prefab, for
        /// the reason everything else here is: the prefab is rebuilt wholesale, so a number tuned
        /// in the inspector survives exactly until the next run. The MODEL carries the same 1.06
        /// (<c>inventory_wall_scale.py</c>) and the two have to stay equal — the board and the
        /// lattice gear is dropped onto are the same lines.
        /// </para>
        /// </summary>
        private static void SetDisplayScale(WallInventory wall, StringBuilder log)
        {
            var so = new SerializedObject(wall);
            SerializedProperty property = so.FindProperty("displayScale");

            if (property == null)
            {
                log.Append("  FIELD    PackContainer has no 'displayScale' any more.\n");
                return;
            }

            property.floatValue = PackScale.WallDisplay;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireWall(WallInventory wall, PackSurface surface, Object[] contents,
                                     StringBuilder log)
        {
            var so = new SerializedObject(wall);

            SerializedProperty faces = so.FindProperty("surfaces");
            if (faces != null)
            {
                faces.arraySize = 1;
                faces.GetArrayElementAtIndex(0).objectReferenceValue = surface;
            }
            else log.Append("  FIELD    PackContainer has no 'surfaces' any more.\n");

            // The shape library, for the same reason the rig is given it: an unwired `shapes` is
            // not an error, it silently ignores every authored mask in favour of the derived
            // rectangle — which is how the rig shipped for two days without anybody noticing.
            SerializedProperty shapes = so.FindProperty("shapes");
            if (shapes != null)
            {
                shapes.objectReferenceValue = AssetDatabase.LoadAssetAtPath<PackShapeLibrary>(
                    "Assets/Game/ScriptableObjects/Items/PackShapes.asset");
            }
            else log.Append("  FIELD    PackContainer has no 'shapes' any more.\n");

            SerializedProperty starting = so.FindProperty("startingMainItems");
            if (starting != null)
            {
                starting.arraySize = contents.Length;
                for (int i = 0; i < contents.Length; i++)
                    starting.GetArrayElementAtIndex(i).objectReferenceValue = contents[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            log.Append("  wired    1 face, ").Append(contents.Length)
               .Append(" starting item(s) carried forward\n");
        }

        /// <summary>
        /// One box over the placement face, and nothing else.
        ///
        /// <para>
        /// It is what <c>WallAimController</c>'s ray hits to decide which wall the player is
        /// looking at — and, just as importantly, what stops them reaching a wall through a
        /// bulkhead: the face's PLANE runs on forever, so the ray is what makes occlusion real.
        /// </para>
        /// <para>
        /// Sized to the grid rectangle rather than to the renderers, deliberately. A bounds-fitted
        /// box would also cover the tray and the header, where there is nothing to place, and
        /// aiming at the tray would light up cells on the grid two metres above it.
        /// </para>
        /// </summary>
        private static void EnsureFaceCollider(GameObject wall, PackSurface surface, StringBuilder log)
        {
            var box = wall.GetComponent<BoxCollider>();
            if (box == null) box = wall.AddComponent<BoxCollider>();

            // The face's four corners, in the wall root's own space. Taken through the surface
            // rather than assumed from the model's numbers, so a re-export that moved the face
            // moves the collider with it.
            Vector3 a = wall.transform.InverseTransformPoint(surface.ToWorld(Vector2.zero, 0f));
            Vector3 b = wall.transform.InverseTransformPoint(surface.ToWorld(surface.Size, 0f));

            var bounds = new Bounds(a, Vector3.zero);
            bounds.Encapsulate(b);
            bounds.Encapsulate(wall.transform.InverseTransformPoint(
                surface.ToWorld(new Vector2(surface.Size.x, 0f), 0f)));
            bounds.Encapsulate(wall.transform.InverseTransformPoint(
                surface.ToWorld(new Vector2(0f, surface.Size.y), 0f)));

            // Given real thickness backwards, into the wall. A zero-depth box is a plane physics
            // treats as a degenerate solid, and a ray that starts a hair behind it passes through.
            Vector3 size = bounds.size;
            Vector3 normal = wall.transform.InverseTransformDirection(surface.transform.up);
            const float Depth = 0.20f;

            size = new Vector3(
                Mathf.Max(size.x, Mathf.Abs(normal.x) * Depth),
                Mathf.Max(size.y, Mathf.Abs(normal.y) * Depth),
                Mathf.Max(size.z, Mathf.Abs(normal.z) * Depth));

            box.center = bounds.center - normal * (Depth * 0.5f);
            box.size = size;
            box.isTrigger = false;

            log.Append("  collider face box ").Append(box.size.ToString("0.00")).Append(" at ")
               .Append(box.center.ToString("0.00")).Append('\n');
        }

        /// <summary>
        /// Take back the <c>NetChannel</c> the staged build gave itself.
        ///
        /// <para>
        /// <c>WallInventory.OnEnable</c> subscribes its two messages, and <c>NetChannel.GetOrAdd</c>
        /// walks up to the nearest <c>NetworkObject</c> to find the channel — which, on a staged
        /// object standing alone in the editor, is nothing, so it falls back to the root and adds
        /// one there. In the ship the wall is a child of the hull's NetworkObject and its messages
        /// ride the SHIP's channel, so this one would sit on every wall forever, listening to
        /// nothing.
        /// </para>
        /// </summary>
        private static void DropStrayChannel(GameObject wall, StringBuilder log)
        {
            var channel = wall.GetComponent<Core.NetChannel>();
            if (channel == null) return;

            Object.DestroyImmediate(channel);
            log.Append("  cleanup  dropped the NetChannel the staged build added to itself\n");
        }

        // ── Carrying the authored half forward ───────────────────────────────

        private static Object[] ReadStartingContents()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var wall = existing != null ? existing.GetComponent<WallInventory>() : null;
            if (wall == null) return new Object[0];

            var so = new SerializedObject(wall);
            SerializedProperty list = so.FindProperty("startingMainItems");
            if (list == null || !list.isArray) return new Object[0];

            var values = new Object[list.arraySize];
            for (int i = 0; i < values.Length; i++)
                values[i] = list.GetArrayElementAtIndex(i).objectReferenceValue;

            return values;
        }

        // ── Small shared helpers ─────────────────────────────────────────────

        private static void SetEnum(SerializedObject so, string field, int value, StringBuilder log)
        {
            SerializedProperty property = so.FindProperty(field);

            if (property == null)
            {
                log.Append("  FIELD    '").Append(field).Append("' no longer exists on ")
                   .Append(so.targetObject.GetType().Name).Append(".\n");
                return;
            }

            property.enumValueIndex = value;
        }

        private static void SetVector2(SerializedObject so, string field, Vector2 value,
                                       StringBuilder log)
        {
            SerializedProperty property = so.FindProperty(field);

            if (property == null)
            {
                log.Append("  FIELD    '").Append(field).Append("' no longer exists on ")
                   .Append(so.targetObject.GetType().Name).Append(".\n");
                return;
            }

            property.vector2Value = value;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }
    }
}
