using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Wires <c>expedition_rig.fbx</c> and <c>pack_holders.fbx</c> into the prefabs the physical
    /// inventory needs, in one pass, from a single menu item.
    ///
    /// <para>
    /// One pass on purpose. Every step here depends on the one before it — the library has to exist
    /// before the rig can reference it, the rig has to exist before the player can point at it —
    /// and doing them as five separate menu items means five chances to do four of them. It is also
    /// <b>idempotent</b>: it rebuilds from the FBXs and overwrites the same asset paths, so the
    /// GUIDs everything references survive a second run and nothing accumulates.
    /// </para>
    ///
    /// <para><b>What it carries forward.</b> The rig prefab is rebuilt from the model rather than
    /// edited, so anything hand-tuned on it would be lost. The one thing that is genuinely authored
    /// rather than derived — the pack's starting contents — is read back off the previous rig, or
    /// off <c>ExpeditionBackpack</c> the first time, and put back.</para>
    ///
    /// <para><b>Two things that fail silently and are therefore checked out loud.</b> Unity discards
    /// prefab saves when the AssetDatabase goes read-only, and a stale import artifact can make a
    /// perfectly correct YAML reference resolve to null at runtime while git stays clean. So the
    /// last thing this does is re-load everything it wrote, off disk, and assert that every
    /// surface, every hinge, every holder and the player's pack reference all actually
    /// resolved.</para>
    /// </summary>
    public static class ExpeditionRigWiring
    {
        // ── Paths ────────────────────────────────────────────────────────────

        private const string RigFbx = "Assets/Game/Art/Models/Props/expedition_rig.fbx";
        private const string HoldersFbx = "Assets/Game/Art/Models/Props/pack_holders.fbx";

        private const string EquipmentFolder = "Assets/Game/Prefabs/Items/Equipment";
        private const string HolderFolder = EquipmentFolder + "/Holders";
        private const string RigPrefab = EquipmentFolder + "/ExpeditionRig.prefab";
        private const string LegacyPackPrefab = EquipmentFolder + "/ExpeditionBackpack.prefab";

        // Beside the other gameplay ScriptableObjects. NOT under Resources: nothing loads this by
        // name — the rig holds a direct reference — and a Resources asset is in every build whether
        // or not anything points at it.
        private const string LibraryFolder = "Assets/Game/ScriptableObjects/Items";
        private const string LibraryAsset = LibraryFolder + "/PackHolderLibrary.asset";

        // PackShapeLibraryTool creates and fills this; the rig only needs the reference. Wired here
        // too because a rebuild used to ship `shapes` null — nothing failed, every drawn mask was
        // just silently ignored in favour of the derived default.
        private const string ShapeLibraryAsset = LibraryFolder + "/PackShapes.asset";

        private const string PlayerPrefab = "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";

        // ── The tables this script exists to apply ───────────────────────────

        /// <summary>
        /// The usable rectangles, in metres, measured off the built rig.
        ///
        /// <para>
        /// <b>Every face the model has carries inventory</b> — the board (mat, rack, lash rail),
        /// both back panels and both wings. An earlier pass wired only the board, on the theory
        /// that gear on the folding flanks would be crushed when the pack closes; playtest
        /// (2026-08-25) overruled it, because an unwired face does not read as forbidden — the
        /// placement outline just dies over it with nothing to say why. WHEN a face is usable
        /// (worn, racked) is <c>BackpackObject.Reaches</c>' question, not this table's: wiring
        /// puts a face on the rig, <c>Reaches</c> decides the moment. The board rows stay first
        /// so first-fit still prefers the board.
        /// </para>
        /// <para>
        /// They are inset from the physical panels so an item placed at the edge does not overhang,
        /// and nothing in the .blend encodes them — the <c>SURF_</c> empties are deliberately
        /// identity-scaled, because Unity parents items under them and a scaled parent would
        /// rescale every item. Which is exactly why they have to be written down here.
        /// </para>
        /// <para>
        /// <c>SURF_Rack</c> is the odd one and needs no special handling, which is worth saying
        /// out loud. It is the leaf's underside, so it is authored pointing at the sand and only
        /// becomes a usable face once <c>PIVOT_Leaf</c> turns X -90 — but everything below reads
        /// the empty's own transform and inherits its rotation, so a face that is currently upside
        /// down wires exactly like one that is not.
        /// </para>
        /// </summary>
        // Overhang: the rack additionally lets items LONGER than its 9-cell u span hang past both
        // ends ski-fashion, and the two back panels — the smallest wired faces — allow it on BOTH
        // axes, bedroll-fashion. See PackOverhang for the rule and its limits.
        //
        // DEEPENED 2026-08-25 by the .blend's `LEAF_EXTRA` (0.30 m at the board's leading edge,
        // 0.20 m before the enlargement), after ItemScaleLadder roughly doubled the gear. These
        // numbers are measurements of the model, not preferences: they must equal the `SURFACES`
        // table in `_Source~/components/props/expedition_rig.py`, and `Verify` below re-reads the
        // built prefab to say so out loud. Changing one without the other lays gear out over sand.
        //
        // RE-CELLED 2026-08-25 (second pass): every rectangle is an EXACT multiple of
        // PackGrid.Cell — Leaf 8x8, LongGoods 18x1, Rack 9x9, Back 3x6, Wings 4x7 — so the grid
        // fills each face edge to edge with zero hem. The model's stitching and webbing pitch was
        // re-drawn onto the same cell boundaries in the same pass, so resizing a row here without
        // moving the .blend's decoration un-aligns the two.
        //
        // SCALED by PackScale.Factor, with the cell and the .blend — 1.5 on 2026-09-01, then 1.05
        // on 2026-09-02 when the 1.5 rig came out too big to wear. The CELL COUNTS above are
        // unchanged through both and that is the point: a move of the factor is a similarity
        // transform, so no mask, no shape and no capacity moved — only how big the rig is.
        // Written out at their current values rather than as `PackScale.Apply(...)` because these
        // are measurements read by eye off the model, and a reader comparing this table against
        // the .blend must be able to see the same numbers in both — `expedition_rig_scale.py`
        // prints exactly this list. Verify below re-reads the built prefab and checks the cell
        // counts.
        private static readonly (string node, PackSurfaceId id, Vector2 size)[] SurfaceTable =
        {
            ("SURF_Leaf",      PackSurfaceId.Leaf,           new Vector2(0.756f,  0.756f)),
            ("SURF_LongGoods", PackSurfaceId.LongGoods,      new Vector2(1.701f,  0.0945f)),
            ("SURF_Rack",      PackSurfaceId.Rack,           new Vector2(0.8505f, 0.8505f)),
            ("SURF_Back_L",    PackSurfaceId.BackPanelLeft,  new Vector2(0.2835f, 0.567f)),
            ("SURF_Back_R",    PackSurfaceId.BackPanelRight, new Vector2(0.2835f, 0.567f)),
            // The strip between those two, where the modelled bottle used to be bolted. The same
            // 3 x 6 cells as its neighbours by construction, not by coincidence: it is their row
            // with x zeroed, and the panels' inner edges leave it 15 mm of clearance each side.
            ("SURF_Back_C",    PackSurfaceId.BackPanelCentre, new Vector2(0.2835f, 0.567f)),
            ("SURF_Wing_L",    PackSurfaceId.WingLeft,       new Vector2(0.378f,  0.6615f)),
            ("SURF_Wing_R",    PackSurfaceId.WingRight,      new Vector2(0.378f,  0.6615f)),
        };

        /// <summary>
        /// The five moving parts, with the STOW travel from the authored pose.
        ///
        /// <para>
        /// The wing pivots are CHILDREN of <c>PIVOT_Leaf</c> in the .blend (reparented 2026-08-24
        /// with the stakes, after ground-hinged wings kept reading as the board abandoning its
        /// sides) — so their ±90 below is relative to the BOARD: closing folds each side panel
        /// square up off it, and as the board rises the panels come round to hug the pack's
        /// flanks, closing it like a box. The rack asks every part for exactly its stow angle,
        /// so there is nothing extra to wire for it.
        /// </para>
        ///
        /// <para>
        /// <c>expedition_rig.blend</c> is authored <b>deployed</b> — every pivot at rotation zero in
        /// the open pose, because that is the pose whose measurements the spec gives and the only
        /// one whose surface rectangles can be checked in the file. So <c>restIsOpen</c> is true on
        /// all four and these angles fold the rig UP, which is the sense the generator's docstring
        /// prints them in.
        /// </para>
        /// <para>
        /// The axes are in each pivot's OWN local space. Every pivot is unrotated in Blender, so its
        /// local frame is the model's; the axis conversion carries that frame across intact, which
        /// is why the panel and leaf turn about local X and the wings about local Y here exactly as
        /// they do in the .blend.
        /// </para>
        /// </summary>
        /// <summary>
        /// The one reserved face on the rig, and the items it takes.
        ///
        /// <para>
        /// The centre back strip is the oxygen bottle's socket rather than a shelf: the pack is
        /// plumbed into whatever stands there, so a rifle in it would be plumbed into nothing.
        /// BOTH bottles are listed — a player takes a full one out and puts a drained one back,
        /// and a socket that only accepted one of the two would be a rule nobody could learn.
        /// </para>
        /// <para>
        /// Wired here rather than named in <c>PackSurface</c>, because a reservation names an
        /// ASSET and the component cannot reach one from a static table. Missing assets are a
        /// warning, not a failure: a rig built before the bottles exist is a rig whose socket
        /// takes anything, which is visible and recoverable, where a refusal to build is not.
        /// </para>
        /// </summary>
        private static readonly string[] CentreBackAccepts =
        {
            "Assets/Game/Resources/Items/Supplies/OxygenTank.asset",
            "Assets/Game/Resources/Items/Supplies/OxygenTankEmpty.asset",
        };

        // The marker the breathing hose leaves the manifold from, and the component that draws it.
        private const string HoseOutletNode = "Marker_Rig_HoseOutlet";

        private static readonly (string node, BackpackHingePart part, Vector3 axis, float fold)[] HingeTable =
        {
            // If a hinge sign is ever in doubt: measure it IN UNITY on the imported asset
            // (rest * AngleAxis(fold, axis) against the mesh bounds), never derive it from the
            // .blend — the axis conversion mirrors handedness on root empties, which is how the
            // wings once spent a morning folding 0.41 m through the floor.
            ("PIVOT_Back",   BackpackHingePart.Panel,     Vector3.right,  25f),
            ("PIVOT_Leaf",   BackpackHingePart.Leaf,      Vector3.right, -90f),
            // ±90 folds each side panel square UP off the board, so that when the board rises the
            // panels wrap round the pack's flanks and hug the exterior. Signs measured on the
            // imported prefab (deployed pose, leaf at rest):
            //   Wing_L -90 -> y 0.00..0.44 (up)   Wing_L +90 -> y -0.41..0.03 (through the floor)
            //   Wing_R mirrored.
            ("PIVOT_Wing_L", BackpackHingePart.WingLeft,  Vector3.up,    -90f),
            ("PIVOT_Wing_R", BackpackHingePart.WingRight, Vector3.up,     90f),
            // The lid rides the board like the wings do — PIVOT_Lid is a child of PIVOT_Leaf —
            // so its -90 is relative to the BOARD: mid-stow it stands up as the end wall, then
            // rides the leaf over to cap the closed pack. Sign measured on the imported prefab
            // (X-hinges arrive sign-true, unlike the wings' mirrored Y): rest * AngleAxis(-90,
            // right) carries the apron's far edge up over the board, +90 buries it.
            ("PIVOT_Lid",    BackpackHingePart.Lid,       Vector3.right, -90f),
        };

        private static readonly (string node, HolderKind kind)[] HolderTable =
        {
            ("Holder_Cord",    HolderKind.Cord),
            ("Holder_Webbing", HolderKind.Webbing),
            ("Holder_Bungee",  HolderKind.Bungee),
            ("Holder_Sleeve",  HolderKind.Sleeve),
            ("Holder_Clip",    HolderKind.Clip),
        };

        /// <summary>The two pins that hold the leaf's front corners down.</summary>
        private static readonly string[] StakeNodes = { "Mesh_Rig_Stake_L", "Mesh_Rig_Stake_R" };

        /// <summary>
        /// The rig's fixed landmark, and what the holders pop outward from on the last beat of
        /// the deploy.
        ///
        /// <para>
        /// It was the modelled oxygen bottle until 2026-09-03, when that became a real item and
        /// the geometry was deleted. The MANIFOLD is its successor and is the better landmark of
        /// the two anyway: it is bolted to the panel rather than to the bottle, so it is there
        /// whether or not a bottle is in the socket above it, and it carries the only emissive in
        /// the whole rig — which is the actual claim this constant makes, that it is where a
        /// player's eye is already resting when the pack finishes opening.
        /// </para>
        /// </summary>
        private const string TankNode = "Mesh_Rig_OxygenTank_Manifold";

        // ── Entry point ──────────────────────────────────────────────────────

        [MenuItem("Tools/SpaceGame/Items/Build Expedition Rig Prefab")]
        public static void Build()
        {
            var log = new StringBuilder("Expedition rig wiring\n");

            // The rig keeps Unity's default handling — its hinges turn relative to whatever rest
            // pose the FBX hands them, so a rotated root costs it nothing. The holders must have
            // the conversion baked into their vertices; see ConfigureModel's second overload.
            if (!ConfigureModel(RigFbx, log) | !ConfigureModel(HoldersFbx, log, bakeAxes: true))
            {
                // Non-short-circuiting `|` above so both models are reported, not just the first.
                Fail(log, "the models could not be imported; nothing else was touched.");
                return;
            }

            HolderLibrary library = BuildHolders(log);
            if (library == null)
            {
                Fail(log, "the holder library was not built; the rig was not touched.");
                return;
            }

            GameObject rig = BuildRig(library, log);
            if (rig == null)
            {
                Fail(log, "the rig prefab was not saved; the player was not touched.");
                return;
            }

            WirePlayer(rig, log);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Verify does the reporting when it fails, since a failure has to be an error rather
            // than a line at the bottom of a success log.
            if (Verify(log)) Debug.Log(log.ToString());
        }

        private static void Fail(StringBuilder log, string why)
        {
            log.Append("ABORTED: ").Append(why).Append('\n');
            Debug.LogError(log.ToString());
        }

        // ── 1. Import settings ───────────────────────────────────────────────

        /// <summary>
        /// Turn a holder root's imported rotation into identity, folding it into its children so
        /// every world pose is preserved.
        ///
        /// <para>
        /// Safe precisely because it runs before any fit: the holder is at scale 1, so
        /// <c>local = rootRotation * local</c> on each child is an exact rigid change of frame. The
        /// same operation after <c>HolderBuilder</c>'s non-uniform fit would shear, which is why the
        /// rotation has to be gone from the saved prefab rather than corrected at build time.
        /// </para>
        /// <para>
        /// Refuses a root that carries geometry of its own — that would need the vertices rotated,
        /// not the transform, and it means the art is not shaped the way this expects.
        /// </para>
        /// </summary>
        private static bool NormaliseRoot(Transform root, string node, StringBuilder log)
        {
            float tilt = Quaternion.Angle(root.localRotation, Quaternion.identity);
            if (tilt <= 0.5f) return true;

            if (root.GetComponent<MeshFilter>() != null)
            {
                log.Append("  BAD AXES '").Append(node).Append("' is rotated ")
                   .Append(tilt.ToString("F2"))
                   .Append(" deg AND carries its own mesh, so the rotation cannot be folded into ")
                   .Append("children. Author the holder as an empty root with mesh children.\n");
                return false;
            }

            Quaternion rotation = root.localRotation;

            foreach (Transform child in root)
            {
                child.localPosition = rotation * child.localPosition;
                child.localRotation = rotation * child.localRotation;
            }

            root.localRotation = Quaternion.identity;

            log.Append("  levelled '").Append(node).Append("' (was ")
               .Append(tilt.ToString("F2")).Append(" deg out; folded into ")
               .Append(root.childCount).Append(" child(ren)).\n");

            return true;
        }

        /// <summary>
        /// Put one FBX on the settings the rest of this depends on, and reimport if anything moved.
        ///
        /// <para>
        /// Guarded on having actually changed something, because <see cref="AssetImporter.SaveAndReimport"/>
        /// is the expensive call here and this script is meant to be re-runnable without thinking
        /// about it.
        /// </para>
        /// </summary>
        private static bool ConfigureModel(string path, StringBuilder log) =>
            ConfigureModel(path, log, bakeAxes: false);

        /// <summary>
        /// As above, but <paramref name="bakeAxes"/> puts the axis conversion into the MESH DATA
        /// instead of onto the root transform.
        ///
        /// <para>
        /// This is the holders' fix, and it belongs here rather than in the exporter. Blender is
        /// Z-up and Unity is Y-up, so something has to turn the model 90 degrees; by default Unity
        /// does it by rotating every root object, which is why this project's FBX empties arrive at
        /// poses like euler (270.02, 0, 0). Harmless on the rig — its hinges turn relative to their
        /// captured rest pose — and fatal on a holder, because <c>HolderBuilder</c> overwrites the
        /// root's rotation with the surface's and then counter-scales the <c>HARD_</c> children
        /// componentwise, which only inverts a non-uniform scale when parent and child axes agree.
        /// Ninety degrees between them swaps a buckle's height and width factors.
        /// </para>
        /// <para>
        /// Exporting the holders on identity axes was tried first and cannot work: it just declares
        /// the FBX Z-up, and Unity applies its own conversion on import instead. The rotation has to
        /// be baked into the vertices, and <c>bakeAxisConversion</c> is the importer asking exactly
        /// that.
        /// </para>
        /// </summary>
        private static bool ConfigureModel(string path, StringBuilder log, bool bakeAxes)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;

            if (importer == null)
            {
                log.Append("  MISSING  ").Append(path)
                   .Append(" — run components/props/expedition_rig_export.py first.\n");
                return false;
            }

            bool dirty = false;

            // 1 Blender unit is 1 metre and the FBX says so, so the file's own scale is the right
            // one. ModelImporter.useFileScale = false was tried on the previous pack and inflated
            // it to 34 x 53 x 23 m; do not go back there.
            if (!importer.useFileScale) { importer.useFileScale = true; dirty = true; }
            if (!Mathf.Approximately(importer.globalScale, 1f)) { importer.globalScale = 1f; dirty = true; }

            if (importer.bakeAxisConversion != bakeAxes)
            {
                importer.bakeAxisConversion = bakeAxes;
                dirty = true;
            }

            // The PIVOT_, SURF_ and HARD_ empties are the whole point of both files: BackpackObject
            // swings the first, PackSurface sits on the second, HolderBuilder counter-scales the
            // third. Anything that lets Unity fold a childless transform away takes them with it.
            if (importer.optimizeGameObjects) { importer.optimizeGameObjects = false; dirty = true; }
            if (importer.importVisibility != true) { importer.importVisibility = true; dirty = true; }

            // Neither file has a rig or a clip, and importing animation on them yields nothing but
            // an Animator something downstream would have to strip.
            if (importer.importAnimation) { importer.importAnimation = false; dirty = true; }
            if (importer.animationType != ModelImporterAnimationType.None)
            {
                importer.animationType = ModelImporterAnimationType.None;
                dirty = true;
            }

            if (importer.importBlendShapes) { importer.importBlendShapes = false; dirty = true; }
            if (importer.importCameras) { importer.importCameras = false; dirty = true; }
            if (importer.importLights) { importer.importLights = false; dirty = true; }
            if (importer.addCollider) { importer.addCollider = false; dirty = true; }

            // Read/write on, matching the other pack: a non-readable mesh cannot be cooked into a
            // MeshCollider, and the failure is a silent null sharedMesh rather than an error.
            if (!importer.isReadable) { importer.isReadable = true; dirty = true; }

            // Copied off expedition_backpack.fbx, which is the settings combination this project's
            // palette materials are known to resolve under.
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
            if (importer.materialSearch != ModelImporterMaterialSearch.RecursiveUp)
            {
                importer.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
                dirty = true;
            }

            if (dirty) importer.SaveAndReimport();

            log.Append("  import   ").Append(path)
               .Append(dirty ? "  (settings changed, reimported)\n" : "  (already correct)\n");

            return true;
        }

        // ── 4. The holders and their library ─────────────────────────────────

        /// <summary>
        /// Split <c>pack_holders.fbx</c> into five prefabs and map them into a
        /// <see cref="HolderLibrary"/>.
        ///
        /// <para>
        /// The instance is <b>unpacked</b> before the five are split off, because saving a child of
        /// a model-prefab instance as its own prefab is not something Unity will do. The cost is
        /// that re-exporting the FBX no longer flows into these five automatically; the answer to
        /// that is to re-run this menu item, which is what it is for.
        /// </para>
        /// </summary>
        private static HolderLibrary BuildHolders(StringBuilder log)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(HoldersFbx);
            if (source == null)
            {
                log.Append("  MISSING  ").Append(HoldersFbx).Append(" would not load as a model.\n");
                return null;
            }

            EnsureFolder(HolderFolder);

            var staged = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(staged, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            var built = new Dictionary<HolderKind, GameObject>();

            // The five as they are lifted out of the staged copy, so the cleanup below destroys
            // exactly what this run created. Looking them up by name in the open scene afterwards
            // would work and would also delete somebody's identically named scene object.
            var detached = new List<GameObject>();

            bool ok = true;

            try
            {
                foreach ((string node, HolderKind kind) in HolderTable)
                {
                    Transform found = FindDeep(staged.transform, node);

                    if (found == null)
                    {
                        log.Append("  MISSING  no '").Append(node).Append("' in ")
                           .Append(HoldersFbx).Append(".\n");
                        ok = false;
                        continue;
                    }

                    // HolderBuilder overwrites the holder root's rotation with the surface's, and
                    // CounterScaleHardware inverts the fit componentwise — both need ZERO rotation
                    // between this root and its HARD_ empties.
                    //
                    // Blender is Z-up and Unity is Y-up, so something has to turn the model 90
                    // degrees, and chasing that through the exporter did not converge: identity
                    // export axes landed 90 degrees out, and adding bakeAxisConversion made it 180.
                    // Each layer applies its own conversion and they compound.
                    //
                    // So it is normalised HERE instead, where it is exact and there is nothing to
                    // guess: the holder is still at scale 1, so folding the root's rotation into its
                    // children preserves every world pose without shear. Doing the same thing after
                    // a non-uniform fit would shear, which is the whole reason the rotation must be
                    // gone before HolderBuilder ever sees the prefab.
                    if (!NormaliseRoot(found, node, log))
                    {
                        ok = false;
                        continue;
                    }

                    found.SetParent(null, true);
                    detached.Add(found.gameObject);

                    string path = HolderFolder + "/" + node + ".prefab";
                    GameObject saved = PrefabUtility.SaveAsPrefabAsset(found.gameObject, path);

                    if (saved == null)
                    {
                        log.Append("  FAILED   could not save ").Append(path)
                           .Append(" — is the AssetDatabase read-only?\n");
                        ok = false;
                        continue;
                    }

                    built[kind] = saved;
                    log.Append("  holder   ").Append(kind).Append(" -> ").Append(path).Append('\n');
                }
            }
            finally
            {
                // Whatever is left of the staged copy, plus the ones lifted out of it, which are
                // now scene roots of their own. Runs even on the failure paths above, so a
                // half-finished build leaves nothing lying in whatever scene happened to be open.
                if (staged != null) Object.DestroyImmediate(staged);

                foreach (GameObject stray in detached)
                    if (stray != null) Object.DestroyImmediate(stray);
            }

            if (!ok) return null;

            return WriteLibrary(built, log);
        }

        private static HolderLibrary WriteLibrary(Dictionary<HolderKind, GameObject> built, StringBuilder log)
        {
            EnsureFolder(LibraryFolder);

            var library = AssetDatabase.LoadAssetAtPath<HolderLibrary>(LibraryAsset);
            bool created = library == null;

            if (created)
            {
                library = ScriptableObject.CreateInstance<HolderLibrary>();
                AssetDatabase.CreateAsset(library, LibraryAsset);
            }

            // Through SerializedObject because `entries` is a private [SerializeField] on a class
            // this file does not own — the same rule every setter below follows.
            var so = new SerializedObject(library);
            SerializedProperty entries = so.FindProperty("entries");

            if (entries == null)
            {
                log.Append("  FAILED   HolderLibrary has no 'entries' field any more.\n");
                return null;
            }

            entries.arraySize = HolderTable.Length;

            for (int i = 0; i < HolderTable.Length; i++)
            {
                SerializedProperty row = entries.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("kind").enumValueIndex = (int)HolderTable[i].kind;
                row.FindPropertyRelative("prefab").objectReferenceValue = built[HolderTable[i].kind];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(library);

            log.Append("  library  ").Append(created ? "created " : "updated ").Append(LibraryAsset)
               .Append(" with ").Append(HolderTable.Length).Append(" kinds\n");

            return library;
        }

        // ── 2, 3, 5. The rig ─────────────────────────────────────────────────

        private static GameObject BuildRig(HolderLibrary library, StringBuilder log)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(RigFbx);
            if (source == null)
            {
                log.Append("  MISSING  ").Append(RigFbx).Append(" would not load as a model.\n");
                return null;
            }

            // Read BEFORE the old prefab is overwritten. The starting contents are the one thing on
            // this prefab that is authored rather than derived, so losing them on a rebuild would
            // hand the player an empty pack and look like the swap broke the inventory.
            (Object[] strap, Object[] main) contents = ReadStartingContents(log);

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(source);
            rig.name = "ExpeditionRig";

            try
            {
                var pack = rig.GetComponent<BackpackObject>();
                if (pack == null) pack = rig.AddComponent<BackpackObject>();

                var surfaces = new List<PackSurface>();
                bool ok = true;

                foreach ((string node, PackSurfaceId id, Vector2 size) in SurfaceTable)
                {
                    PackSurface surface = AttachSurface(rig.transform, node, id, size, log);
                    if (surface == null) { ok = false; continue; }
                    surfaces.Add(surface);
                }

                AttachHose(rig.transform, pack, log);

                var hinges = new List<(Transform pivot, BackpackHingePart part, Vector3 axis, float fold)>();

                foreach ((string node, BackpackHingePart part, Vector3 axis, float fold) in HingeTable)
                {
                    Transform pivot = FindDeep(rig.transform, node);

                    if (pivot == null)
                    {
                        log.Append("  MISSING  no '").Append(node).Append("' under ")
                           .Append(RigFbx).Append(", so that hinge cannot be wired.\n");
                        ok = false;
                        continue;
                    }

                    hinges.Add((pivot, part, axis, fold));
                }

                if (!ok) return null;

                var stakes = new List<Transform>();
                foreach (string node in StakeNodes)
                {
                    Transform stake = FindDeep(rig.transform, node);
                    if (stake != null) stakes.Add(stake);
                    else log.Append("  note     no '").Append(node)
                            .Append("'; the pack will open without the stake beat.\n");
                }

                Transform tank = FindDeep(rig.transform, TankNode);
                if (tank == null)
                    log.Append("  note     no '").Append(TankNode)
                       .Append("'; holders will pop outward from the rig origin instead.\n");

                EnsureBodyCollider(rig, log);

                WirePack(pack, surfaces, hinges, stakes, tank, library, contents, log);

                // Everything above is an override on a MODEL prefab instance. Without this the
                // added components and the corner children are in the scene object only, and the
                // save writes a prefab with none of them in it — silently.
                PrefabUtility.RecordPrefabInstancePropertyModifications(rig);

                EnsureFolder(EquipmentFolder);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(rig, RigPrefab, out bool success);

                if (!success || saved == null)
                {
                    log.Append("  FAILED   could not save ").Append(RigPrefab)
                       .Append(" — is the AssetDatabase read-only?\n");
                    return null;
                }

                log.Append("  rig      saved ").Append(RigPrefab)
                   .Append(" with ").Append(surfaces.Count).Append(" surfaces and ")
                   .Append(hinges.Count).Append(" hinges\n");

                return saved;
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }
        }

        /// <summary>
        /// Put a <see cref="PackSurface"/> where the rig's uv space actually starts.
        ///
        /// <para>
        /// <b>Why this is a child and not the <c>SURF_</c> empty itself.</b> The empties are
        /// authored at the CENTRE of their rectangles — <c>SURF_Leaf</c> sits at x = 0 on a leaf
        /// that is symmetric about x — while <see cref="PackSurface"/> reads its transform origin as
        /// the rect's <c>(0,0)</c> CORNER: a uv runs from <c>(0,0)</c> up to <c>Size</c>, and
        /// <see cref="PackGrid"/> lays its cells across exactly that span. Putting the component
        /// straight on the empty would offset every item by half the surface and put a quarter of
        /// the usable area off the panel, with nothing anywhere saying so.
        /// </para>
        /// <para>
        /// A child at the corner is the smallest fix that touches neither the art nor the runtime
        /// convention, and it costs nothing else: items are parented under the surface's transform
        /// and <see cref="PackPointer"/> resolves surfaces with <c>GetComponentInParent</c>, so a
        /// node in between changes nothing. Its rotation and scale are the empty's, which is what
        /// keeps "+X is width, +Z is depth, +Y is out of the surface" true.
        /// </para>
        /// </summary>
        private static PackSurface AttachSurface(Transform rig, string node, PackSurfaceId id,
                                                 Vector2 size, StringBuilder log)
        {
            Transform empty = FindDeep(rig, node);

            if (empty == null)
            {
                log.Append("  MISSING  no '").Append(node).Append("' under ").Append(RigFbx)
                   .Append(", so surface ").Append(id).Append(" cannot be wired.\n");
                return null;
            }

            string cornerName = node + "_Rect";
            Transform corner = empty.Find(cornerName);

            if (corner == null)
            {
                var go = new GameObject(cornerName);
                corner = go.transform;
                corner.SetParent(empty, false);
            }

            corner.localRotation = Quaternion.identity;
            corner.localScale = Vector3.one;

            // Halved along the surface's own width and depth, and divided by the empty's lossy
            // scale for the same reason PackSurface.ToWorld divides: a local offset is in the
            // parent's units, and this project's pack art has arrived on the centimetre convention
            // before — mesh data 100x small under transforms 100x large. Without the divide a
            // 0.39 m half-width would be 39 m of offset.
            Vector3 s = empty.lossyScale;
            float sx = Mathf.Abs(s.x) < 1e-6f ? 1f : Mathf.Abs(s.x);
            float sz = Mathf.Abs(s.z) < 1e-6f ? 1f : Mathf.Abs(s.z);

            corner.localPosition = new Vector3(-size.x * 0.5f / sx, 0f, -size.y * 0.5f / sz);

            var surface = corner.GetComponent<PackSurface>();
            if (surface == null) surface = corner.gameObject.AddComponent<PackSurface>();

            var so = new SerializedObject(surface);
            SetEnum(so, "id", (int)id, log);
            SetReservation(so, id, log);
            SetVector2(so, "size", size, log);
            so.ApplyModifiedPropertiesWithoutUndo();

            log.Append("  surface  ").Append(node).Append(" -> ").Append(id)
               .Append("  ").Append(size.x.ToString("0.00")).Append(" x ")
               .Append(size.y.ToString("0.00")).Append(" m\n");

            return surface;
        }

        private static void WirePack(
            BackpackObject pack,
            List<PackSurface> surfaces,
            List<(Transform pivot, BackpackHingePart part, Vector3 axis, float fold)> hinges,
            List<Transform> stakes,
            Transform tank,
            HolderLibrary library,
            (Object[] strap, Object[] main) contents,
            StringBuilder log)
        {
            var so = new SerializedObject(pack);

            SerializedProperty surfaceArray = so.FindProperty("surfaces");
            if (surfaceArray != null)
            {
                surfaceArray.arraySize = surfaces.Count;
                for (int i = 0; i < surfaces.Count; i++)
                    surfaceArray.GetArrayElementAtIndex(i).objectReferenceValue = surfaces[i];
            }
            else log.Append("  FIELD    BackpackObject has no 'surfaces' any more.\n");

            SerializedProperty hingeArray = so.FindProperty("hinges");
            if (hingeArray != null)
            {
                hingeArray.arraySize = hinges.Count;

                for (int i = 0; i < hinges.Count; i++)
                {
                    SerializedProperty row = hingeArray.GetArrayElementAtIndex(i);

                    row.FindPropertyRelative("pivot").objectReferenceValue = hinges[i].pivot;
                    row.FindPropertyRelative("part").enumValueIndex = (int)hinges[i].part;
                    row.FindPropertyRelative("localAxis").vector3Value = hinges[i].axis;
                    row.FindPropertyRelative("foldAngle").floatValue = hinges[i].fold;

                    // The rig is authored DEPLOYED. Getting this backwards folds it inside out,
                    // silently, so it is set on every row rather than left to the default.
                    row.FindPropertyRelative("restIsOpen").boolValue = true;

                    log.Append("  hinge    ").Append(hinges[i].pivot.name).Append(" -> ")
                       .Append(hinges[i].part).Append("  axis ").Append(hinges[i].axis)
                       .Append("  stow ").Append(hinges[i].fold.ToString("+0;-0")).Append(" deg\n");
                }
            }
            else log.Append("  FIELD    BackpackObject has no 'hinges' any more.\n");

            SetObject(so, "holders", library, log);
            SetObject(so, "holderOrigin", tank, log);

            // Without this every rebuild ships `shapes` null and PackShapes.For silently falls
            // back to the derived footprint for every item, drawn masks included. Null stays a
            // legitimate value only while the asset genuinely does not exist yet.
            var shapeLibrary = AssetDatabase.LoadAssetAtPath<PackShapeLibrary>(ShapeLibraryAsset);
            if (shapeLibrary == null)
                log.Append("  note     no ").Append(ShapeLibraryAsset)
                   .Append("; items keep their derived footprint shapes.\n");
            SetObject(so, "shapes", shapeLibrary, log);

            SerializedProperty stakeArray = so.FindProperty("stakes");
            if (stakeArray != null)
            {
                stakeArray.arraySize = stakes.Count;
                for (int i = 0; i < stakes.Count; i++)
                    stakeArray.GetArrayElementAtIndex(i).objectReferenceValue = stakes[i];
            }

            RestoreList(so, "startingStrapItems", contents.strap);
            RestoreList(so, "startingMainItems", contents.main);

            so.ApplyModifiedPropertiesWithoutUndo();

            if (PrefabUtility.IsPartOfPrefabInstance(pack))
                PrefabUtility.RecordPrefabInstancePropertyModifications(pack);
        }

        /// <summary>
        /// A body collider, because <see cref="BackpackObject"/> is an <c>IInteractable</c> and
        /// without one there is no way to aim at the pack to open it or take it back.
        ///
        /// Fitted to the renderers rather than to a number, so it stays right if the model changes.
        /// </summary>
        private static void EnsureBodyCollider(GameObject rig, StringBuilder log)
        {
            var box = rig.GetComponent<BoxCollider>();
            if (box == null) box = rig.AddComponent<BoxCollider>();

            Renderer[] renderers = rig.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                log.Append("  note     the rig has no renderers, so its collider was left as-is.\n");
                return;
            }

            // Accumulated in the ROOT's local space. Renderer.bounds is world-space and axis
            // aligned, which is only the same thing while the staged instance sits unrotated at the
            // origin — true here, but not something to rely on silently.
            Bounds local = default;
            bool started = false;

            foreach (Renderer renderer in renderers)
            {
                Bounds b = renderer.bounds;
                Vector3 centre = rig.transform.InverseTransformPoint(b.center);
                Vector3 extents = rig.transform.InverseTransformVector(b.extents);
                extents = new Vector3(Mathf.Abs(extents.x), Mathf.Abs(extents.y), Mathf.Abs(extents.z));

                var one = new Bounds(centre, extents * 2f);

                if (!started) { local = one; started = true; }
                else local.Encapsulate(one);
            }

            box.center = local.center;
            box.size = local.size;

            log.Append("  collider fitted ").Append(local.size.ToString("F3"))
               .Append(" about ").Append(local.center.ToString("F3")).Append('\n');
        }

        /// <summary>
        /// The starting contents off the previous rig, or off the pack it replaces the first time
        /// round. Empty is a legitimate answer and is not warned about.
        /// </summary>
        private static (Object[] strap, Object[] main) ReadStartingContents(StringBuilder log)
        {
            GameObject from = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab)
                              ?? AssetDatabase.LoadAssetAtPath<GameObject>(LegacyPackPrefab);

            if (from == null) return (new Object[0], new Object[0]);

            var pack = from.GetComponent<BackpackObject>();
            if (pack == null) return (new Object[0], new Object[0]);

            var so = new SerializedObject(pack);
            Object[] strap = ReadList(so, "startingStrapItems");
            Object[] main = ReadList(so, "startingMainItems");

            if (strap.Length + main.Length > 0)
                log.Append("  contents carried ").Append(strap.Length + main.Length)
                   .Append(" starting item(s) over from ").Append(from.name).Append('\n');

            return (strap, main);
        }

        // ── 6. The player ────────────────────────────────────────────────────

        private static void WirePlayer(GameObject rig, StringBuilder log)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefab);

            if (contents == null)
            {
                log.Append("  MISSING  ").Append(PlayerPrefab).Append(" would not open.\n");
                return;
            }

            try
            {
                var controller = contents.GetComponentInChildren<BackpackController>(true);

                if (controller == null)
                {
                    log.Append("  MISSING  no BackpackController under ").Append(PlayerPrefab)
                       .Append(", so the pack reference was not set.\n");
                    return;
                }

                var so = new SerializedObject(controller);
                SetObject(so, "backpackPrefab", rig, log);
                so.ApplyModifiedPropertiesWithoutUndo();

                // Only meaningful if the controller turns out to live on a NESTED prefab instance
                // inside the player. It does not today — but if it ever does, the modification is
                // dropped on save without this, and nothing says so.
                if (PrefabUtility.IsPartOfPrefabInstance(controller))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(controller);

                PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefab, out bool success);

                log.Append(success ? "  player   " : "  FAILED   ")
                   .Append(PlayerPrefab).Append(" backpackPrefab -> ").Append(rig.name).Append('\n');
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // ── 7. Prove it ──────────────────────────────────────────────────────

        /// <summary>
        /// Re-load everything off disk and check it resolved.
        ///
        /// <para>
        /// Not paranoia. Unity discards prefab saves outright when the AssetDatabase goes read-only,
        /// and separately a stale import artifact can leave a YAML reference that looks perfect in
        /// the file and resolves to null at runtime — with git clean, so neither shows up in a diff.
        /// Both of those produce exactly the same symptom as this script never having been run.
        /// </para>
        /// </summary>
        private static bool Verify(StringBuilder log)
        {
            var problems = new List<string>();

            // The table itself, before anything that was built from it. Every rectangle must be a
            // WHOLE number of cells with zero hem: that is what makes an authored PackShape mask
            // mean the same thing on every face, and it is the first thing a change to
            // PackGrid.Cell or PackScale.Factor breaks. A hem does not throw — the face quietly
            // loses a row and the model's webbing stops lining up with the lattice.
            foreach ((string node, PackSurfaceId id, Vector2 size) in SurfaceTable)
            {
                Vector2 hem = PackGrid.Hem(size);
                if (hem.sqrMagnitude <= 1e-8f) continue;

                Vector2Int cells = PackGrid.CellsOn(size);
                problems.Add($"{node} ({id}) is {size.x:F3} x {size.y:F3} m, which is " +
                             $"{cells.x} x {cells.y} cells of {PackGrid.Cell:F3} m plus a " +
                             $"{hem.x * 2f:F3} x {hem.y * 2f:F3} m hem. Author it as a whole " +
                             "number of cells.");
            }

            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            if (rig == null) problems.Add(RigPrefab + " did not reload.");
            else
            {
                var pack = rig.GetComponent<BackpackObject>();

                if (pack == null) problems.Add("the saved rig has no BackpackObject.");
                else
                {
                    var so = new SerializedObject(pack);

                    SerializedProperty surfaces = so.FindProperty("surfaces");
                    int wired = surfaces != null ? surfaces.arraySize : 0;
                    if (wired != SurfaceTable.Length)
                        problems.Add($"the saved rig has {wired} surfaces wired, expected {SurfaceTable.Length}.");

                    foreach ((string node, PackSurfaceId id, Vector2 size) in SurfaceTable)
                    {
                        PackSurface found = null;

                        foreach (PackSurface candidate in pack.Surfaces)
                            if (candidate != null && candidate.Id == id) { found = candidate; break; }

                        if (found == null) problems.Add($"surface {id} ({node}) is missing from the saved rig.");
                        else if ((found.Size - size).sqrMagnitude > 1e-6f)
                            problems.Add($"surface {id} saved at {found.Size}, expected {size}.");
                        else problems.AddRange(ReservationProblems(found));
                    }

                    // The hose is the only thing that says the pack is PLUMBED INTO the bottle
                    // rather than just carrying it, and a missing one is invisible: the socket
                    // still works, the bottle still sits in it, and nothing anywhere complains.
                    var hoses = rig.GetComponentsInChildren<PackHose>(true);
                    if (hoses.Length != 1)
                        problems.Add($"the saved rig has {hoses.Length} hoses, expected 1.");
                    else
                    {
                        var hoseSo = new SerializedObject(hoses[0]);
                        foreach (string field in new[] { "container", "outlet" })
                            if (hoseSo.FindProperty(field).objectReferenceValue == null)
                                problems.Add($"the hose's '{field}' is null, so it draws nothing.");
                    }

                    SerializedProperty hinges = so.FindProperty("hinges");
                    int hinged = hinges != null ? hinges.arraySize : 0;
                    if (hinged != HingeTable.Length)
                        problems.Add($"the saved rig has {hinged} hinges, expected {HingeTable.Length}.");
                    else
                        for (int i = 0; i < hinged; i++)
                            if (hinges.GetArrayElementAtIndex(i).FindPropertyRelative("pivot")
                                      .objectReferenceValue == null)
                                problems.Add($"hinge {i} on the saved rig has a null pivot.");

                    SerializedProperty holders = so.FindProperty("holders");
                    if (holders == null || holders.objectReferenceValue == null)
                        problems.Add("the saved rig's holder library reference is null.");

                    var shapeLibrary = AssetDatabase.LoadAssetAtPath<PackShapeLibrary>(ShapeLibraryAsset);
                    if (shapeLibrary != null)
                    {
                        SerializedProperty shapes = so.FindProperty("shapes");
                        if (shapes == null || shapes.objectReferenceValue != shapeLibrary)
                            problems.Add("the saved rig's shapes reference does not point at " +
                                         ShapeLibraryAsset + ".");
                    }
                }
            }

            var library = AssetDatabase.LoadAssetAtPath<HolderLibrary>(LibraryAsset);
            if (library == null) problems.Add(LibraryAsset + " did not reload.");
            else
                foreach ((string node, HolderKind kind) in HolderTable)
                    if (!library.Has(kind))
                        problems.Add($"the holder library has no prefab for {kind} ({node}).");

            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            if (player == null) problems.Add(PlayerPrefab + " did not reload.");
            else
            {
                var controller = player.GetComponentInChildren<BackpackController>(true);

                if (controller == null) problems.Add("the saved player has no BackpackController.");
                else
                {
                    var so = new SerializedObject(controller);
                    SerializedProperty field = so.FindProperty("backpackPrefab");

                    if (field == null || field.objectReferenceValue == null)
                        problems.Add("the saved player's backpackPrefab is null.");
                    else if (field.objectReferenceValue != rig)
                        problems.Add("the saved player's backpackPrefab points at " +
                                     field.objectReferenceValue.name + ", not the rig.");
                }
            }

            if (problems.Count == 0)
            {
                // Counted rather than spelled out, because "all six surfaces" was already wrong the
                // day a seventh was added and a stale count in a success line is the least likely
                // thing anybody re-reads.
                log.Append("  VERIFIED all ").Append(SurfaceTable.Length).Append(" surfaces, ")
                   .Append(HingeTable.Length).Append(" hinges, ").Append(HolderTable.Length)
                   .Append(" holders and the player's pack reference reloaded off disk.\n");
                return true;
            }

            log.Append("  VERIFY FAILED — the write did not land:\n");
            foreach (string problem in problems) log.Append("    - ").Append(problem).Append('\n');

            Debug.LogError(log.ToString());
            return false;
        }

        // ── Serialized-field helpers ─────────────────────────────────────────
        //
        // Every setter goes through SerializedObject and reports instead of throwing when a field
        // is missing. These are private [SerializeField]s on components this file does not own, so
        // a rename should surface as one clear line the next time somebody runs this — not as a
        // prefab that looks fine and quietly has a default in it.

        private static void SetObject(SerializedObject so, string field, Object value, StringBuilder log)
        {
            SerializedProperty property = so.FindProperty(field);

            if (property == null)
            {
                log.Append("  FIELD    '").Append(field).Append("' no longer exists on ")
                   .Append(so.targetObject.GetType().Name).Append(".\n");
                return;
            }

            property.objectReferenceValue = value;
        }

        /// <summary>
        /// Give the rig its breathing hose: the component that draws a tube from the manifold's
        /// outlet marker to whatever is standing in the reserved socket.
        ///
        /// <para>
        /// The marker is the model's; the tube is not modelled at all, because a hose in the model
        /// runs to thin air whenever the bottle is out — which it is meant to be. See
        /// <see cref="PackHose"/>.
        /// </para>
        /// <para>
        /// Its material is taken off the rig's OWN meshes rather than authored here, so the hose
        /// is a palette material by construction and a re-export that retints the pack retints the
        /// hose with it. Rubber if the model has it, the manifold's own material otherwise.
        /// </para>
        /// </summary>
        private static void AttachHose(Transform rig, PackContainer pack, StringBuilder log)
        {
            Transform outlet = FindDeep(rig, HoseOutletNode);

            if (outlet == null)
            {
                log.Append("  MISSING  no '").Append(HoseOutletNode).Append("' in ").Append(RigFbx)
                   .Append(", so the pack has no visible connection to the bottle it carries.\n");
                return;
            }

            var hose = outlet.GetComponent<PackHose>();
            if (hose == null) hose = outlet.gameObject.AddComponent<PackHose>();

            var so = new SerializedObject(hose);
            SetObject(so, "container", pack, log);
            SetObject(so, "outlet", outlet, log);
            SetEnum(so, "socket", (int)PackSurfaceId.BackPanelCentre, log);
            SetObject(so, "material", HoseMaterial(rig), log);
            so.ApplyModifiedPropertiesWithoutUndo();

            log.Append("  HOSE     drawn from ").Append(HoseOutletNode).Append(" to whatever is in ")
               .Append(PackSurfaceId.BackPanelCentre).Append(".\n");
        }

        /// <summary>
        /// Is this face reserved exactly as it should be? Both directions are checked, because
        /// both fail silently: a socket that lost its reservation quietly becomes a shelf and the
        /// first thing that fits goes in it, and a face that gained one quietly stops taking gear
        /// the player has always put there.
        /// </summary>
        private static IEnumerable<string> ReservationProblems(PackSurface surface)
        {
            int wanted = surface.Id == PackSurfaceId.BackPanelCentre ? CentreBackAccepts.Length : 0;
            int got = surface.AcceptsOnly != null ? surface.AcceptsOnly.Count : 0;

            if (got != wanted)
            {
                yield return $"surface {surface.Id} accepts {got} named item(s), expected {wanted}" +
                             (wanted == 0
                                 ? " — an ordinary face takes anything that fits."
                                 : " — the oxygen bottle's socket takes both bottles and nothing else.");
                yield break;
            }

            for (int i = 0; i < got; i++)
                if (surface.AcceptsOnly[i] == null)
                    yield return $"surface {surface.Id} has a null entry in its reservation, which " +
                                 "refuses everything rather than the one thing it names.";
        }

        /// <summary>A rubber material off the rig's own meshes, or the nearest thing to one.</summary>
        private static Material HoseMaterial(Transform rig)
        {
            Material fallback = null;

            foreach (Renderer renderer in rig.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    if (material.name.Contains("Rubber")) return material;

                    fallback ??= material;
                }
            }

            return fallback;
        }

        /// <summary>
        /// Write the face's reservation, or clear it. Cleared explicitly on every other face,
        /// because this pass rebuilds the prefab from the model and a field left alone is a field
        /// carrying whatever the last build put there.
        /// </summary>
        private static void SetReservation(SerializedObject so, PackSurfaceId id, StringBuilder log)
        {
            SerializedProperty list = so.FindProperty("acceptsOnly");

            if (list == null)
            {
                log.Append("  FIELD    'acceptsOnly' no longer exists on PackSurface.\n");
                return;
            }

            if (id != PackSurfaceId.BackPanelCentre)
            {
                list.arraySize = 0;
                return;
            }

            var items = new List<Object>();

            foreach (string path in CentreBackAccepts)
            {
                var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(path);

                if (item == null)
                {
                    // A warning, not a failure. A rig built before the bottles exist is a rig whose
                    // socket takes anything — visible, and fixed by building them and re-running.
                    log.Append("  MISSING  no item at ").Append(path)
                       .Append(", so the centre back socket is not reserved for it.\n");
                    continue;
                }

                items.Add(item);
            }

            list.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];

            log.Append("  RESERVED ").Append(PackSurfaceId.BackPanelCentre).Append(" for ")
               .Append(items.Count).Append(" item(s): the oxygen bottle's socket.\n");
        }

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

        private static void SetVector2(SerializedObject so, string field, Vector2 value, StringBuilder log)
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

        private static Object[] ReadList(SerializedObject so, string field)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null || !property.isArray) return new Object[0];

            var values = new Object[property.arraySize];
            for (int i = 0; i < values.Length; i++)
                values[i] = property.GetArrayElementAtIndex(i).objectReferenceValue;

            return values;
        }

        private static void RestoreList(SerializedObject so, string field, Object[] values)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null || !property.isArray) return;

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        // ── Small helpers ────────────────────────────────────────────────────

        /// <summary>
        /// The first descendant with this exact name. Depth-first over the whole subtree, because
        /// the FBX puts a root of its own above everything and the empties sit at varying depths
        /// under it — <c>SURF_Leaf</c> is two levels down, <c>Mesh_Rig_Stake_L</c> is one.
        /// </summary>
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

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string built = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }
    }
}
