using UnityEditor;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Custom inspector for <see cref="TerrainFeatureSpawner"/>. Mirrors <c>CaveSpawnerEditor</c>'s
    /// bake-and-save workflow, and adds the live-preview / footprint-gizmo tooling the project lead
    /// asked for.
    ///
    /// Workflow:
    ///   1. Drop a TerrainFeatureSpawner, pick a feature type, drag the Scene-view box / spline
    ///      handles (drawn by <see cref="TerrainFeatureHandles"/>) to define the footprint.
    ///   2. Tune the shared noise / overlap / height / jaggedness sliders. With "Live preview" on,
    ///      every change instantly regenerates the in-scene mesh so the result is visible immediately.
    ///   3. Click "Bake &amp; Save Mesh". This generates the feature at edit time and writes the mesh
    ///      to a TerrainFeatureBakes/ folder next to the scene; the spawner's bakedMesh field is
    ///      auto-assigned. Runtime then skips generation entirely.
    ///
    /// NavMesh is intentionally NOT baked here — feature meshes contribute to the shared WORLD
    /// NavMesh via their MeshCollider, so the world's NavMeshSurface rebuild owns that.
    /// </summary>
    [CustomEditor(typeof(TerrainFeatureSpawner))]
    public class TerrainFeatureSpawnerEditor : Editor
    {
        // Live preview is ON by default; the designer can toggle it off.
        bool _livePreview = true;

        void OnSceneGUI()
        {
            var spawner = (TerrainFeatureSpawner)target;

            // Process the footprint handles. A returned 'true' means the designer dragged a polygon
            // vertex / path point / size handle this frame — the serialized footprint changed, so the
            // in-scene mesh is now stale and must be rebuilt for the terrain to reflect the new shape.
            bool footprintChanged = TerrainFeatureHandles.Draw(spawner);

            if (footprintChanged && _livePreview)
                Regenerate(spawner);
        }

        // Serialized fields drawn manually (or not at all) below — excluded from the default pass.
        // 'area' is drawn by DrawFootprintBlock (mode-aware, with the noise knobs shown only in Noise
        // mode); the legacy footprint fields are hidden; 'materialPreset' / 'featureMaterial' are drawn
        // together by DrawMaterialBlock so the preset dropdown can auto-assign the material.
        static readonly string[] HiddenProps =
            { "featureSettings", "featureSettingsType", "area", "boxHalfExtents", "footprintShape",
              "footprintComplexity", "footprint", "materialPreset", "featureMaterial" };

        public override void OnInspectorGUI()
        {
            var spawner = (TerrainFeatureSpawner)target;

            // Keep the per-feature settings object in sync with the chosen feature type BEFORE drawing,
            // so the inspector always shows the knobs for the currently selected feature.
            spawner.SyncFeatureSettings();

            serializedObject.Update();

            // One-time cleanup of pre-rewrite legacy footprint data. TerrainFeatureSpawner.OnAfterDeserialize
            // migrates the old boxHalfExtents/footprintShape/complexity/polygon into 'area' and clears them —
            // but that clear happens only in the transient deserialized object, never persisted. The
            // inspector's serializedObject keeps re-reading the still-serialized legacy values, so every
            // ApplyModifiedProperties round-trips through OnAfterDeserialize, which re-runs MigrateFromLegacy
            // and overwrites width/height/breadth — making any footprint edit instantly snap back. Clearing
            // the legacy properties HERE, through the serializedObject, makes the cleared state stick so the
            // migration guard stops firing.
            PurgeLegacyFootprintData();

            EditorGUI.BeginChangeCheck();

            // Default inspector minus the manually-drawn settings fields.
            DrawPropertiesExcluding(serializedObject, HiddenProps);

            // Footprint — mode-aware block (box dims + Polygon/Noise + the noise knobs).
            DrawFootprintBlock(spawner);

            // Rendering material — preset dropdown + explicit material slot.
            DrawMaterialBlock();

            // Per-feature settings block, drawn from the polymorphic [SerializeReference] property.
            DrawFeatureSettings();

            bool settingsChanged = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            // A Noise-mode footprint must be rebuilt whenever a knob changed so the Scene + preview
            // reflect the new outline immediately.
            if (settingsChanged && spawner.FootprintIsGenerated)
                spawner.RefreshGeneratedFootprint();

            EditorGUILayout.Space();
            DrawImplementationStatus(spawner);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            _livePreview = EditorGUILayout.Toggle(
                new GUIContent("Live preview", "Regenerate the in-scene mesh whenever a setting changes."),
                _livePreview);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Regenerate Preview", GUILayout.Height(24)))
                    Regenerate(spawner);
                if (GUILayout.Button("Clear Preview", GUILayout.Width(120), GUILayout.Height(24)))
                {
                    spawner.ClearSpawned();
                    MarkSceneDirty(spawner);
                }
            }

            if (_livePreview && settingsChanged)
                Regenerate(spawner);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Bake & Save Mesh", GUILayout.Height(28)))
                    BakeAndSave(spawner);
                if (GUILayout.Button("Clear Baked", GUILayout.Width(110), GUILayout.Height(28)))
                {
                    spawner.AssignBakedMesh(null);
                    EditorUtility.SetDirty(spawner);
                }
            }

            if (spawner.HasBakedMesh)
                EditorGUILayout.HelpBox(
                    "Baked mesh assigned. Runtime skips generation and instantiates it directly. " +
                    "The feature contributes to the world NavMesh via its MeshCollider.",
                    MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    "No baked mesh. Runtime will generate the feature live on Awake (slow).",
                    MessageType.Warning);
        }

        // -------------------------------------------------------------------------
        // Per-feature settings block
        // -------------------------------------------------------------------------

        /// <summary>
        /// Draws the chosen feature's own settings object (e.g. CliffFeatureSettings) directly in the
        /// inspector. The object is a polymorphic [SerializeReference] field, so its children are
        /// enumerated and drawn generically — every feature's bespoke knobs appear with zero per-feature
        /// editor code.
        /// </summary>
        void DrawFeatureSettings()
        {
            SerializedProperty settings = serializedObject.FindProperty("featureSettings");
            if (settings == null) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Feature Settings", EditorStyles.boldLabel);

            if (settings.managedReferenceValue == null)
            {
                EditorGUILayout.HelpBox("This feature exposes no extra settings.", MessageType.None);
                return;
            }

            // Enumerate the settings object's serialized children and draw each one.
            SerializedProperty child = settings.Copy();
            SerializedProperty end = child.GetEndProperty();
            bool enterChildren = true;
            while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
            {
                enterChildren = false;
                EditorGUILayout.PropertyField(child, true);
            }
        }

        // -------------------------------------------------------------------------
        // Rendering material block
        // -------------------------------------------------------------------------

        /// <summary>
        /// Draws the material preset dropdown plus the explicit material slot. Picking any preset other
        /// than <see cref="TerrainMaterialPreset.Custom"/> resolves the matching sandstone material asset
        /// and writes it into <c>featureMaterial</c>, so a designer chooses a terrain look from one menu.
        /// In Custom mode the material slot is editable for hand-assigning any material.
        /// </summary>
        void DrawMaterialBlock()
        {
            SerializedProperty presetProp = serializedObject.FindProperty("materialPreset");
            SerializedProperty matProp    = serializedObject.FindProperty("featureMaterial");
            if (presetProp == null || matProp == null) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rendering / NavMesh — Material", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(presetProp, new GUIContent(
                "Material Preset",
                "Quick-pick terrain look. Any choice but 'Custom' auto-assigns the matching sandstone material."));
            bool presetChanged = EditorGUI.EndChangeCheck();

            var preset = (TerrainMaterialPreset)presetProp.enumValueIndex;

            if (presetChanged && preset != TerrainMaterialPreset.Custom)
            {
                Material resolved = TerrainFeatureSpawnerVisuals.LoadPresetMaterial(preset);
                if (resolved != null)
                {
                    matProp.objectReferenceValue = resolved;
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"Material asset for preset '{preset}' could not be found.", MessageType.Error);
                }
            }

            // In Custom mode the slot is hand-editable; with a preset it shows the resolved asset
            // (greyed out, since the dropdown owns it).
            using (new EditorGUI.DisabledScope(preset != TerrainMaterialPreset.Custom))
            {
                EditorGUILayout.PropertyField(matProp, new GUIContent(
                    "Feature Material",
                    "Material applied to the spawned mesh. Driven by the preset above unless preset = Custom."));
            }

            if (preset != TerrainMaterialPreset.Custom)
                EditorGUILayout.HelpBox(
                    $"Material driven by preset '{preset}'. Switch to 'Custom' to assign your own.",
                    MessageType.None);
        }

        // -------------------------------------------------------------------------
        // Footprint authoring help
        // -------------------------------------------------------------------------

        /// <summary>
        /// Permanently clears the pre-rewrite legacy footprint fields (boxHalfExtents, footprintShape,
        /// footprintComplexity, footprint) through the serializedObject, so the spawner's
        /// <c>OnAfterDeserialize</c> migration guard (<c>hasLegacy</c>) goes false and stops re-running
        /// <c>MigrateFromLegacy</c> on every serialize round-trip. Without this, every footprint edit
        /// snaps back. A no-op once the fields are already at their cleared sentinels.
        /// </summary>
        void PurgeLegacyFootprintData()
        {
            SerializedProperty boxHalf = serializedObject.FindProperty("boxHalfExtents");
            SerializedProperty shape   = serializedObject.FindProperty("footprintShape");
            SerializedProperty cplx    = serializedObject.FindProperty("footprintComplexity");
            SerializedProperty poly    = serializedObject.FindProperty("footprint");
            // The legacy 'footprint' is an embedded [Serializable] FeaturePolygon, not an Object
            // reference — its "is it legacy data" signal is its vertex count.
            SerializedProperty polyVerts = poly != null ? poly.FindPropertyRelative("vertices") : null;

            bool hasLegacy =
                (boxHalf   != null && boxHalf.vector3Value != Vector3.zero) ||
                (shape     != null && shape.intValue >= 0) ||
                (cplx      != null && cplx.floatValue >= 0f) ||
                (polyVerts != null && polyVerts.arraySize > 0);
            if (!hasLegacy) return;

            if (boxHalf   != null) boxHalf.vector3Value = Vector3.zero;
            if (shape     != null) shape.intValue = -1;
            if (cplx      != null) cplx.floatValue = -1f;
            if (polyVerts != null) polyVerts.ClearArray();

            // Persist the cleared sentinels immediately so the migration guard never fires again.
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
        }

        /// <summary>
        /// Draws the area footprint block: Width / Height / Breadth, the Polygon vs Noise mode toggle,
        /// the explicit noise knobs (shown only in Noise mode) and the authoring buttons. The outline
        /// itself is never shown as raw vertex fields — it is edited visually in the Scene view.
        /// </summary>
        void DrawFootprintBlock(TerrainFeatureSpawner spawner)
        {
            SerializedProperty areaProp = serializedObject.FindProperty("area");
            if (areaProp == null) return;

            SerializedProperty widthP   = areaProp.FindPropertyRelative("width");
            SerializedProperty heightP  = areaProp.FindPropertyRelative("height");
            SerializedProperty breadthP = areaProp.FindPropertyRelative("breadth");
            SerializedProperty modeP    = areaProp.FindPropertyRelative("mode");
            SerializedProperty noiseP   = areaProp.FindPropertyRelative("noise");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Footprint", EditorStyles.boldLabel);

            // --- Size: Width / Height / Breadth ---------------------------------
            EditorGUILayout.PropertyField(widthP, new GUIContent(
                "Width", "Footprint size along local X, in metres."));
            EditorGUILayout.PropertyField(breadthP, new GUIContent(
                "Breadth", "Footprint size along local Z, in metres."));
            EditorGUILayout.PropertyField(heightP, new GUIContent(
                "Height", "Vertical extent of the feature (meshing band height), in metres. " +
                          "Does NOT affect the 2D outline shape."));

            // --- Mode ------------------------------------------------------------
            EditorGUILayout.PropertyField(modeP, new GUIContent(
                "Mode", "Polygon — hand-edit the outline vertices in the Scene view.\n" +
                        "Noise — the outline is generated from the knobs below."));

            var mode = (FootprintMode)modeP.enumValueIndex;

            if (mode == FootprintMode.Noise)
            {
                EditorGUILayout.HelpBox(
                    "Noise mode: the outline is generated from the knobs below and the Width × Breadth " +
                    "box. All knobs minimal → a clean rounded blob; all knobs high → a wild, messy, " +
                    "multi-armed silhouette. Drag the box handles in the Scene view to resize.",
                    MessageType.Info);

                EditorGUILayout.PropertyField(noiseP, new GUIContent(
                    "Noise Knobs", "Lobe frequency/amplitude, detail octaves/gain, irregularity and " +
                                   "corner sharpness."), true);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Regenerate Outline"))
                    {
                        Undo.RecordObject(spawner, "Regenerate Footprint");
                        spawner.RefreshGeneratedFootprint();
                        MarkSceneDirty(spawner);
                    }
                    if (GUILayout.Button(new GUIContent("Bake to Polygon",
                            "Freeze the current generated outline into editable Polygon-mode vertices.")))
                    {
                        Undo.RecordObject(spawner, "Bake Footprint To Polygon");
                        spawner.Area.ConvertNoiseToPolygon(spawner.Seed);
                        MarkSceneDirty(spawner);
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Polygon mode: shape the outline in the Scene view — drag the blue vertex dots, " +
                    "click anywhere on an edge to insert a vertex, use the ✕ button beside a dot to " +
                    "delete it. The green arrow sets the feature Height.",
                    MessageType.Info);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reset Outline to Box"))
                    {
                        Undo.RecordObject(spawner, "Reset Footprint");

                        spawner.Footprint.ResetToBox(spawner.Area.BoxHalfExtents);
                        MarkSceneDirty(spawner);
                    }
                    if (GUILayout.Button("Reset Outline to Ellipse"))
                    {
                        Undo.RecordObject(spawner, "Reset Footprint");
                        spawner.Footprint.ResetToEllipse(spawner.Area.BoxHalfExtents);
                        MarkSceneDirty(spawner);
                    }
                }
            }
        }

        // -------------------------------------------------------------------------
        // Implementation-status banner
        // -------------------------------------------------------------------------

        static void DrawImplementationStatus(TerrainFeatureSpawner spawner)
        {
            if (TerrainFeatureRegistry.IsImplemented(spawner.FeatureType))
            {
                EditorGUILayout.HelpBox(
                    $"Feature '{spawner.FeatureType}' is implemented and ready to generate.",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Feature '{spawner.FeatureType}' has NO implementation registered yet. " +
                    "Implement a TerrainFeature subclass and register it in TerrainFeatureRegistry.",
                    MessageType.Error);
            }
        }

        // -------------------------------------------------------------------------
        // Preview + bake
        // -------------------------------------------------------------------------

        static void Regenerate(TerrainFeatureSpawner spawner)
        {
            if (!TerrainFeatureRegistry.IsImplemented(spawner.FeatureType)) return;
            spawner.GenerateNow();
            MarkSceneDirty(spawner);
        }

        static void BakeAndSave(TerrainFeatureSpawner spawner)
        {
            if (!TerrainFeatureRegistry.IsImplemented(spawner.FeatureType))
            {
                EditorUtility.DisplayDialog("Bake failed",
                    $"Feature '{spawner.FeatureType}' is not implemented.", "OK");
                return;
            }

            EditorUtility.DisplayProgressBar("Terrain feature bake", "Generating mesh…", 0.2f);
            try
            {
                TerrainFeatureResult result = spawner.Generate();
                if (result == null || !result.IsValid)
                {
                    EditorUtility.DisplayDialog("Bake failed", "Generation produced no mesh.", "OK");
                    return;
                }

                EditorUtility.DisplayProgressBar("Terrain feature bake", "Writing asset…", 0.8f);

                string folder = TerrainFeatureBakeUtility.ResolveBakeFolder(spawner);

                Mesh saved = TerrainFeatureBakeUtility.SaveSingle(result.Mesh, folder, spawner);
                spawner.AssignBakedMesh(saved);
                Debug.Log($"[TerrainFeatureSpawnerEditor] baked {spawner.FeatureType} " +
                          $"seed {spawner.Seed} ({result.Mesh.vertexCount} verts).");

                // Spawn the saved asset so the scene shows the persistent mesh, not the temporary.
                spawner.SpawnBaked();
                EditorUtility.SetDirty(spawner);
                MarkSceneDirty(spawner);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void MarkSceneDirty(TerrainFeatureSpawner spawner)
        {
            if (spawner.gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
        }
    }
}
