// Builds the standing terminal prefab from the exported standing_terminal.fbx.
//
// The model is the user's hand-built Kiosk variation of components/props/crt_monitor.blend,
// exported whole by models/props/standing_terminal_export.py: a leaning cabinet with a CRT
// head, a lit key column, a key strip and a screen plate. Nothing in it is a marker. Everything
// above the meshes is generated here — the collider, the console and its focus session, the
// world-space screen canvas and every page on it, the telemetry feed — for the same reason the
// other fixture builders exist: a prefab wired by hand is a prefab nobody can rebuild after the
// model changes.
//
// Two measurements replace what a marker would have carried:
//   * the model is stood on its lowest point, so the prefab's origin is its floor line
//     wherever the .blend's origin happens to be;
//   * the glass is measured off the screen plate's own triangles (ScreenPlane), because the
//     plate's transform carries the FBX bake and whatever lean its author gave the cabinet.
//
// The prefab is a SHIP FIXTURE: no NetworkObject of its own. Nested under PlayerShip.prefab
// (PlayerShipBuilder.BuildStandingTerminal) it inherits the ship's, which is what makes the
// console's NetworkVariables and RPCs replicate.
//
// Re-running is safe and is the intended workflow. Re-export the FBX, run this, and the prefab
// is rebuilt in place against the new geometry.
//
// Re-run from: Tools > SpaceGame > Build Standing Terminal Prefab
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public static class StandingTerminalBuilder
    {
        public const string PrefabPath =
            "Assets/Game/Prefabs/Environment/Structures/Facilities/StandingTerminal.prefab";

        private const string Fbx = "Assets/Game/Art/Models/Props/standing_terminal.fbx";

        // The screen plate as crt_monitor.py names it. The builder finds the glass by this, so a
        // rename in Blender fails here, loudly, instead of shipping a terminal with no screen.
        private const string ScreenName = "Mesh_CrtMonitor_Kiosk_Screen";

        // The user's key strip left Blender with no material at all; Unity would draw it in the
        // pink default. Machined dark steel is what a key strip on a cream cabinet is.
        private const string FallbackMaterialPath =
            "Assets/Game/Art/Materials/Vehicles/Mat_Metal_Steel_Dark (DoubleSided).mat";

        // The canvas stands this far off the glass so it never z-fights the plate under it.
        private const float CanvasStandoff = 0.002f;

        // Phosphor on a dark tube — the ItemScannerScreen shader's own two colours, so every
        // display in the game glows the same green.
        private static readonly Color Phosphor = new(0.42f, 1f, 0.6f);
        private static readonly Color Ink = new(0.02f, 0.075f, 0.045f, 0.94f);
        private static readonly Color Dim = new(0.42f, 1f, 0.6f, 0.45f);
        private static readonly Color Faint = new(0.42f, 1f, 0.6f, 0.12f);

        private const float Margin = 14f;
        private const float HeaderHeight = 44f;
        private const float FooterHeight = 30f;
        private const float TabWidth = 92f;
        private const float TabHeight = 28f;
        private const float TabGap = 8f;

        [MenuItem("Tools/SpaceGame/Build Standing Terminal Prefab")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (model == null)
            {
                Debug.LogError($"[StandingTerminalBuilder] No model at {Fbx} — run standing_terminal_export.py first.");
                return;
            }

            var fallback = AssetDatabase.LoadAssetAtPath<Material>(FallbackMaterialPath);
            if (fallback == null)
            {
                Debug.LogError($"[StandingTerminalBuilder] No material at {FallbackMaterialPath}.");
                return;
            }

            var root = new GameObject("StandingTerminal");
            try
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
                visual.name = "Model";
                visual.transform.SetParent(root.transform, false);

                StandOnTheFloor(visual);
                int patched = PatchMissingMaterials(visual, fallback);

                Transform plate = Find(visual, ScreenName);
                if (plate == null) return;

                ScreenPlane screen = MeasureScreen(visual, plate);
                if (screen.Width <= 0.01f || screen.Height <= 0.01f)
                {
                    Debug.LogError($"[StandingTerminalBuilder] '{ScreenName}' measures {screen.Width:0.000} x {screen.Height:0.000} m — not a screen.");
                    return;
                }

                BuildCollider(root, visual);

                var anchor = new GameObject("ScreenAnchor").transform;
                anchor.SetParent(root.transform, false);
                anchor.SetPositionAndRotation(screen.Centre, screen.Rotation);

                RectTransform canvas = WorldCanvasBuilder.Canvas(
                    root.transform, "Screen",
                    new Vector2(screen.Width, screen.Height) / WorldCanvasBuilder.CanvasUnit,
                    screen.Centre + screen.Normal * CanvasStandoff,
                    Quaternion.LookRotation(-screen.Normal, screen.Up));
                var raycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
                raycaster.enabled = false;

                var console = root.AddComponent<TerminalConsole>();
                var session = root.AddComponent<TerminalFocusSession>();
                var feed = root.AddComponent<ShipTelemetrySource>();

                ShipSchematicStage stage = BuildSchematicStage(root.transform);
                if (stage == null) return;

                TerminalScreen ui = BuildScreen(canvas, console, stage);

                Wire(console, "session", session);

                var so = new SerializedObject(session);
                so.FindProperty("canvas").objectReferenceValue = canvas.GetComponent<Canvas>();
                so.FindProperty("raycaster").objectReferenceValue = raycaster;
                so.FindProperty("screenAnchor").objectReferenceValue = anchor;
                so.FindProperty("screenHeight").floatValue = screen.Height;
                so.FindProperty("screen").objectReferenceValue = ui;
                so.ApplyModifiedPropertiesWithoutUndo();

                Wire(feed, "screen", ui);

                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();

                // A read-only AssetDatabase (an MPPM clone) discards prefab saves without
                // erroring, so "saved" is only true once the file is on disk.
                if (!File.Exists(PrefabPath))
                {
                    Debug.LogError($"[StandingTerminalBuilder] {PrefabPath} did not reach disk — is this a read-only editor clone?");
                    return;
                }

                // The wiring sweep now, not at the next ship build, for the reason the station's
                // builder gives: the ship strips a nested fixture's savers when it nests it, and
                // can only strip what already exists. The terminal saves nothing, so this is
                // expected to be a no-op — and if the policy ever decides otherwise, the ship
                // build still comes out right.
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
                if (!SaveableWiring.TryWirePrefabs())
                {
                    Debug.LogError("[StandingTerminalBuilder] The save-wiring pass refused to run; rebuild it stopped.");
                    return;
                }

                Debug.Log($"[StandingTerminalBuilder] Built {PrefabPath}: glass {screen.Width:0.000} x {screen.Height:0.000} m " +
                          $"at {screen.Centre:0.000}, normal {screen.Normal:0.00}, {patched} renderer(s) given a material.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Transform Find(GameObject visual, string name)
        {
            Transform found = visual.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == name);
            if (found == null)
                Debug.LogError($"[StandingTerminalBuilder] No '{name}' in {Fbx} — crt_monitor.py names the " +
                               "screen plate this builder wires; re-export, or update both.");
            return found;
        }

        /// <summary>
        /// Lifts the model so its lowest drawn point is the prefab's y = 0. The .blend's origin
        /// is wherever its author left it; the prefab's is the floor line, which is what the
        /// ship builder stands on the deck.
        /// </summary>
        private static void StandOnTheFloor(GameObject visual)
        {
            Bounds bounds = VertexBounds(visual);
            visual.transform.localPosition += Vector3.up * -bounds.min.y;
        }

        /// <summary>
        /// The box round what is actually drawn, from the VERTICES. Not <c>Renderer.bounds</c>:
        /// that is the axis-aligned box round the mesh's own local box after the transform, and
        /// for a cabinet that leans back 24° it comes out half a metre deeper and twenty
        /// centimetres taller than the geometry — which put the first build's collider well
        /// out in the walkway and its floor line ten centimetres up in the air.
        /// </summary>
        private static Bounds VertexBounds(GameObject visual)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                foreach (Vector3 v in filter.sharedMesh.vertices)
                {
                    Vector3 w = filter.transform.TransformPoint(v);
                    if (!any) { bounds = new Bounds(w, Vector3.zero); any = true; }
                    else bounds.Encapsulate(w);
                }
            }
            return bounds;
        }

        /// <summary>
        /// A submesh that left Blender with no material imports wearing the render pipeline's
        /// default ("Lit" under URP, "Default-Material" under built-in) — a material that is not
        /// a sub-asset of the FBX. Anything not imported with the model gets the fallback.
        /// </summary>
        private static int PatchMissingMaterials(GameObject visual, Material fallback)
        {
            int patched = 0;
            foreach (MeshRenderer r in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] materials = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && AssetDatabase.GetAssetPath(materials[i]) == Fbx) continue;
                    materials[i] = fallback;
                    changed = true;
                }
                if (materials.Length == 0) { materials = new[] { fallback }; changed = true; }
                if (!changed) continue;
                r.sharedMaterials = materials;
                patched++;
            }
            return patched;
        }

        /// <summary>
        /// The glass, measured in the prefab's frame — the root is at the origin, so world is
        /// local. The housing's centre tells the plate which of its two big faces is the front.
        /// </summary>
        private static ScreenPlane MeasureScreen(GameObject visual, Transform plate)
        {
            var filter = plate.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                Debug.LogError($"[StandingTerminalBuilder] '{ScreenName}' has no mesh.");
                return default;
            }

            Vector3[] world = mesh.vertices.Select(v => plate.TransformPoint(v)).ToArray();
            return ScreenPlane.Measure(world, mesh.triangles, VertexBounds(visual).center);
        }

        /// <summary>
        /// One box over everything drawn, measured off the vertices rather than typed in, so a
        /// remodel that grows the cabinet grows the thing the interaction ray has to hit.
        /// </summary>
        private static void BuildCollider(GameObject root, GameObject visual)
        {
            Bounds bounds = VertexBounds(visual);
            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(bounds.center);
            box.size = bounds.size;
        }

        private static void Wire(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── The schematic ────────────────────────────────────────────────────

        /// <summary>
        /// The 3D half of the SHIP page: a stage inside the cabinet holding the miniature lander
        /// and the lens that draws it. Built here rather than referenced from the scene because a
        /// fixture that has to be finished by hand after every rebuild is a fixture that ships
        /// half-wired.
        /// </summary>
        private static ShipSchematicStage BuildSchematicStage(Transform root)
        {
            GameObject miniature = ShipSchematicBuilder.Rebuild();
            if (miniature == null)
            {
                Debug.LogError($"[StandingTerminalBuilder] The schematic at {ShipSchematicBuilder.PrefabPath} " +
                               "could not be built — read the [ShipSchematicBuilder] error above this one.");
                return null;
            }

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(SchematicShaderPath);
            Shader wire = AssetDatabase.LoadAssetAtPath<Shader>(SchematicWireShaderPath);
            if (shader == null || wire == null)
            {
                Debug.LogError("[StandingTerminalBuilder] Could not load " +
                               $"{(shader == null ? SchematicShaderPath : SchematicWireShaderPath)}. " +
                               "If the file is there, Unity has not imported it yet — focus the Editor, " +
                               "let the import finish, and run this again.");
                return null;
            }

            var holder = new GameObject("Schematic");
            holder.transform.SetParent(root, false);

            var stage = holder.AddComponent<ShipSchematicStage>();
            var so = new SerializedObject(stage);
            so.FindProperty("miniaturePrefab").objectReferenceValue = miniature;
            so.FindProperty("schematicShader").objectReferenceValue = shader;
            so.FindProperty("wireShader").objectReferenceValue = wire;
            so.ApplyModifiedPropertiesWithoutUndo();

            return stage;
        }

        // Loaded by PATH, not by Shader.Find. Find goes through the shader-name registry, which is
        // empty for a shader Unity has not imported yet — so a build run in the seconds after the
        // file appears fails with "no shader named X" about a shader that is plainly on disk, logs
        // it, and leaves the previous prefab in place looking fine.
        private const string SchematicShaderPath =
            "Assets/Game/Art/Shaders/UI/Terminal/SchematicHull.shader";
        private const string SchematicWireShaderPath =
            "Assets/Game/Art/Shaders/UI/Terminal/SchematicWire.shader";

        // ── The screen ───────────────────────────────────────────────────────

        /// <summary>
        /// Header with title, three tabs and a clock; one page at a time under it; a footer with
        /// the keys and a cursor. Laid out in millimetres from the canvas centre.
        /// </summary>
        private static TerminalScreen BuildScreen(RectTransform canvas, TerminalConsole console,
                                                  ShipSchematicStage stage)
        {
            float w = canvas.sizeDelta.x, h = canvas.sizeDelta.y;
            float top = h * 0.5f, bottom = -h * 0.5f, left = -w * 0.5f, right = w * 0.5f;

            WorldCanvasBuilder.Fill(canvas, "Tube", Ink);

            // Header
            TextMeshProUGUI title = WorldCanvasBuilder.Label(canvas, "Title", 18f,
                new Vector2(left + Margin + 60f, top - HeaderHeight * 0.5f), new Vector2(120f, HeaderHeight),
                TextAlignmentOptions.Left);
            title.text = "LANDER OS";
            title.color = Phosphor;

            TextMeshProUGUI clock = WorldCanvasBuilder.Label(canvas, "Clock", 18f,
                new Vector2(right - Margin - 40f, top - HeaderHeight * 0.5f), new Vector2(80f, HeaderHeight),
                TextAlignmentOptions.Right);
            clock.text = "00:00";
            clock.color = Phosphor;

            Image rule = WorldCanvasBuilder.Panel(canvas, "HeaderRule", Dim);
            rule.rectTransform.anchoredPosition = new Vector2(0f, top - HeaderHeight);
            rule.rectTransform.sizeDelta = new Vector2(w - Margin * 2f, 1.5f);

            var tabs = new Button[TerminalConsole.PageCount];
            var tabBackgrounds = new Image[TerminalConsole.PageCount];
            var tabLabels = new TextMeshProUGUI[TerminalConsole.PageCount];
            float tabsWidth = TerminalConsole.PageCount * TabWidth + (TerminalConsole.PageCount - 1) * TabGap;
            for (int i = 0; i < TerminalConsole.PageCount; i++)
            {
                float x = -tabsWidth * 0.5f + TabWidth * 0.5f + i * (TabWidth + TabGap);
                var at = new Vector2(x, top - HeaderHeight * 0.5f);

                Image background = WorldCanvasBuilder.Panel(canvas, "Tab" + TerminalConsole.PageNames[i], Color.clear);
                background.rectTransform.anchoredPosition = at;
                background.rectTransform.sizeDelta = new Vector2(TabWidth, TabHeight);
                // The one thing on the glass a click may land on.
                background.raycastTarget = true;

                Image underline = WorldCanvasBuilder.Panel(background.rectTransform, "Underline", Dim);
                underline.rectTransform.anchoredPosition = new Vector2(0f, -TabHeight * 0.5f - 2f);
                underline.rectTransform.sizeDelta = new Vector2(TabWidth, 1.5f);

                TextMeshProUGUI label = WorldCanvasBuilder.Label(background.rectTransform, "Label", 16f,
                    Vector2.zero, new Vector2(TabWidth, TabHeight));
                label.text = (i + 1) + " " + TerminalConsole.PageNames[i];
                label.color = Phosphor;

                var button = background.gameObject.AddComponent<Button>();
                button.targetGraphic = background;
                ColorBlock colours = button.colors;
                colours.normalColor = Color.white;
                colours.highlightedColor = new Color(0.8f, 1f, 0.85f);
                colours.pressedColor = new Color(0.6f, 1f, 0.7f);
                colours.selectedColor = Color.white;
                button.colors = colours;

                tabs[i] = button;
                tabBackgrounds[i] = background;
                tabLabels[i] = label;
            }

            // Pages
            float pageTop = top - HeaderHeight - 8f;
            float pageBottom = bottom + FooterHeight + 4f;
            var pageArea = new Rect(left + Margin, pageBottom, w - Margin * 2f, pageTop - pageBottom);

            GameObject shipPage = Page(canvas, "PageShip", pageArea);
            GameObject statusPage = Page(canvas, "PageStatus", pageArea);
            GameObject gpsPage = Page(canvas, "PageGps", pageArea);

            (ShipSchematicView schematic, TextMeshProUGUI summary) =
                BuildSchematic(shipPage.transform, pageArea, stage);
            TextMeshProUGUI status = BuildStatus(statusPage.transform, pageArea);
            (TextMeshProUGUI gps, RectTransform radar) = BuildGps(gpsPage.transform, pageArea);

            // Footer
            TextMeshProUGUI hint = WorldCanvasBuilder.Label(canvas, "Hint", 13f,
                new Vector2(left + Margin + 120f, bottom + FooterHeight * 0.5f), new Vector2(240f, FooterHeight),
                TextAlignmentOptions.Left, FontStyles.Normal);
            hint.text = "1-3  PAGES     RMB / ESC  LEAVE";
            hint.color = Dim;

            TextMeshProUGUI cursor = WorldCanvasBuilder.Label(canvas, "Cursor", 16f,
                new Vector2(right - Margin - 10f, bottom + FooterHeight * 0.5f), new Vector2(20f, FooterHeight));
            cursor.text = "▌";
            cursor.color = Phosphor;

            var ui = canvas.gameObject.AddComponent<TerminalScreen>();
            var so = new SerializedObject(ui);
            so.FindProperty("console").objectReferenceValue = console;
            Fill(so.FindProperty("tabs"), tabs);
            Fill(so.FindProperty("tabBackgrounds"), tabBackgrounds);
            Fill(so.FindProperty("tabLabels"), tabLabels);
            Fill(so.FindProperty("pages"), new Object[] { shipPage, statusPage, gpsPage });
            so.FindProperty("clockText").objectReferenceValue = clock;
            so.FindProperty("cursorText").objectReferenceValue = cursor;
            so.FindProperty("schematic").objectReferenceValue = schematic;
            so.FindProperty("shipSummaryText").objectReferenceValue = summary;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("gpsText").objectReferenceValue = gps;
            so.FindProperty("radar").objectReferenceValue = radar;
            so.ApplyModifiedPropertiesWithoutUndo();
            return ui;
        }

        private static void Fill(SerializedProperty array, Object[] values)
        {
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static GameObject Page(RectTransform canvas, string name, Rect area)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = canvas.gameObject.layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(canvas, false);
            rect.anchoredPosition = area.center;
            rect.sizeDelta = area.size;
            return go;
        }

        // The SHIP page, left to right: the hull in a hole in the glass, then the panel that says
        // what the module under the cursor is. Under both, one strip of subsystem words.
        private const float StripHeight = 26f;
        private const float PageGap = 14f;
        private const float ViewportShare = 0.58f;
        private const float HintWidth = 236f;

        /// <summary>
        /// The ship illustration: the lander itself, drawn in 3D by <see cref="ShipSchematicStage"/>
        /// into a texture this hangs on the glass, with a readout beside it for whichever module
        /// the cursor is over.
        ///
        /// <para>
        /// It replaced a side elevation built from flat rectangles. The rectangles could not show
        /// WHICH of two nuclear motors was missing, could not be pointed at, and were a second
        /// drawing of a ship that already exists in the model — so they went out of date the first
        /// time the hull was remodelled and nothing said so.
        /// </para>
        /// </summary>
        private static (ShipSchematicView, TextMeshProUGUI) BuildSchematic(Transform page, Rect area,
                                                                          ShipSchematicStage stage)
        {
            float contentHeight = area.height - StripHeight;
            float contentY = StripHeight * 0.5f;
            float top = contentY + contentHeight * 0.5f;

            float viewportWidth = (area.width - PageGap) * ViewportShare;
            float panelWidth = area.width - PageGap - viewportWidth;

            // The frame first, so the hole is drawn over it rather than under it.
            Image frame = WorldCanvasBuilder.Panel(page, "ViewportFrame", Faint);
            frame.rectTransform.anchoredPosition = new Vector2(-area.width * 0.5f + viewportWidth * 0.5f, contentY);
            frame.rectTransform.sizeDelta = new Vector2(viewportWidth + 4f, contentHeight + 4f);

            RawImage viewport = Viewport(page, "Viewport",
                new Vector2(-area.width * 0.5f + viewportWidth * 0.5f, contentY),
                new Vector2(viewportWidth, contentHeight));

            // The panel: name, rule, three lines of state, then what the module is for.
            float panelX = area.width * 0.5f - panelWidth * 0.5f;

            TextMeshProUGUI title = WorldCanvasBuilder.Label(page, "PanelTitle", 17f,
                new Vector2(panelX, top - 13f), new Vector2(panelWidth, 24f), TextAlignmentOptions.Left);
            title.color = Phosphor;
            title.text = "";

            Image rule = WorldCanvasBuilder.Panel(page, "PanelRule", Dim);
            rule.rectTransform.anchoredPosition = new Vector2(panelX, top - 28f);
            rule.rectTransform.sizeDelta = new Vector2(panelWidth, 1.5f);

            TextMeshProUGUI detail = WorldCanvasBuilder.Label(page, "PanelDetail", 14f,
                new Vector2(panelX, top - 62f), new Vector2(panelWidth, 58f),
                TextAlignmentOptions.TopLeft, FontStyles.Normal);
            detail.color = Phosphor;
            detail.lineSpacing = 10f;
            detail.text = "";

            float bodyHeight = Mathf.Max(20f, contentHeight - 100f);
            TextMeshProUGUI body = WorldCanvasBuilder.Label(page, "PanelBody", 13f,
                new Vector2(panelX, top - 100f - bodyHeight * 0.5f), new Vector2(panelWidth, bodyHeight),
                TextAlignmentOptions.TopLeft, FontStyles.Normal);
            body.color = Dim;
            body.lineSpacing = 8f;
            body.text = "";

            // The strip: how to drive it on the left, what the ship is on the right.
            float stripY = -area.height * 0.5f + StripHeight * 0.5f;

            TextMeshProUGUI hint = WorldCanvasBuilder.Label(page, "SchematicHint", 11f,
                new Vector2(-area.width * 0.5f + HintWidth * 0.5f, stripY), new Vector2(HintWidth, StripHeight),
                TextAlignmentOptions.Left, FontStyles.Normal);
            hint.text = "DRAG TURN · WHEEL ZOOM · CLICK MODULE";
            hint.color = Dim;

            float summaryWidth = area.width - HintWidth;
            TextMeshProUGUI summary = WorldCanvasBuilder.Label(page, "Summary", 14f,
                new Vector2(area.width * 0.5f - summaryWidth * 0.5f, stripY),
                new Vector2(summaryWidth, StripHeight), TextAlignmentOptions.Right);
            summary.color = Phosphor;
            summary.text = "";

            var view = viewport.gameObject.AddComponent<ShipSchematicView>();
            var so = new SerializedObject(view);
            so.FindProperty("stage").objectReferenceValue = stage;
            so.FindProperty("viewport").objectReferenceValue = viewport;
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("detailText").objectReferenceValue = detail;
            so.FindProperty("bodyText").objectReferenceValue = body;
            so.ApplyModifiedPropertiesWithoutUndo();

            return (view, summary);
        }

        /// <summary>
        /// The hole the miniature is drawn in. Not a raycast target: the schematic reads the mouse
        /// raw, like the session's own exits, so that the tabs stay the one thing on this glass a
        /// click can land on.
        /// </summary>
        private static RawImage Viewport(Transform page, string name, Vector2 at, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.layer = page.gameObject.layer;
            go.transform.SetParent(page, false);

            var image = go.GetComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;

            // A RawImage with no texture draws a solid white rectangle, which on a phosphor tube
            // is a flashbulb. The view switches it on with the texture the lens makes.
            image.enabled = false;
            image.rectTransform.anchoredPosition = at;
            image.rectTransform.sizeDelta = size;
            return image;
        }

        private static TextMeshProUGUI BuildStatus(Transform page, Rect area)
        {
            TextMeshProUGUI text = WorldCanvasBuilder.Label(page, "Lines", 18f, Vector2.zero,
                new Vector2(area.width - 20f, area.height - 20f), TextAlignmentOptions.TopLeft, FontStyles.Normal);
            text.color = Phosphor;
            text.lineSpacing = 14f;
            text.text = "";
            return text;
        }

        /// <summary>Readout on the left, the crew radar on the right: a square, a crosshair, the ship at the centre.</summary>
        private static (TextMeshProUGUI, RectTransform) BuildGps(Transform page, Rect area)
        {
            float radarSize = Mathf.Min(area.height - 20f, area.width * 0.42f);

            TextMeshProUGUI text = WorldCanvasBuilder.Label(page, "Readout", 17f,
                new Vector2(-area.width * 0.5f + (area.width - radarSize - 30f) * 0.5f + 10f, 0f),
                new Vector2(area.width - radarSize - 30f, area.height - 20f),
                TextAlignmentOptions.TopLeft, FontStyles.Normal);
            text.color = Phosphor;
            text.lineSpacing = 14f;
            text.text = "";

            Image radar = WorldCanvasBuilder.Panel(page, "Radar", Faint);
            radar.rectTransform.anchoredPosition = new Vector2(area.width * 0.5f - radarSize * 0.5f - 10f, 0f);
            radar.rectTransform.sizeDelta = new Vector2(radarSize, radarSize);

            Image across = WorldCanvasBuilder.Panel(radar.rectTransform, "CrossX", Dim);
            across.rectTransform.sizeDelta = new Vector2(radarSize, 1f);
            Image along = WorldCanvasBuilder.Panel(radar.rectTransform, "CrossY", Dim);
            along.rectTransform.sizeDelta = new Vector2(1f, radarSize);

            Image ship = WorldCanvasBuilder.Panel(radar.rectTransform, "Ship", Phosphor);
            ship.rectTransform.sizeDelta = new Vector2(10f, 10f);

            TextMeshProUGUI north = WorldCanvasBuilder.Label(radar.rectTransform, "Fwd", 11f,
                new Vector2(0f, radarSize * 0.5f - 9f), new Vector2(60f, 14f));
            north.text = "FWD";
            north.color = Dim;

            return (text, radar.rectTransform);
        }
    }
}
