// Builds the wingsuit: the worn prefab, its cloth material, and the InventoryItem that puts it in
// the torso slot.
//
//   Assets/Game/Prefabs/Items/Equipment/Wingsuit.prefab      the thing worn and dropped
//   Assets/Game/Art/Materials/Items/WingsuitMembrane.mat     the ClothWind cloth
//   Assets/Game/Resources/Items/Artifacts/Wingsuit.asset     the InventoryItem
//
// EVERYTHING the prefab needs is created here, including the netcode and the savers. That is not
// thoroughness for its own sake: SaveAsPrefabAsset replaces the prefab wholesale, so anything added
// by hand in the Inspector is silently stripped on the next run. The wing pack lost its
// NetworkObject, its PickupableItem and both savers exactly that way, with no error anywhere.
//
// Re-run from: Tools ▸ SpaceGame ▸ Items ▸ Build Wingsuit.
using System.Linq;
using SpaceGame.Items;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class WingsuitBuilder
    {
        private const string ModelPath = "Assets/Game/Art/Models/Items/wingsuit.fbx";
        private const string WornModelPath = "Assets/Game/Art/Models/Items/wingsuit_worn.fbx";
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Equipment/Wingsuit.prefab";
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/Wingsuit.asset";
        private const string MaterialPath = "Assets/Game/Art/Materials/Items/WingsuitMembrane.mat";
        private const string WornMaterialPath =
            "Assets/Game/Art/Materials/Items/WingsuitWornMembrane.mat";

        /// <summary>
        /// The worn wing's own wind amplitude, metres. Small on purpose: it is cloth on somebody
        /// standing about, not cloth in a 24 m/s glide, and a wing that thrashed while its wearer
        /// walked would read as a bug.
        ///
        /// <para>
        /// It gets its OWN material for exactly this number. ClothWind's _WindStrength defaults to
        /// 0.22 m and the flight material never overrides it — the flight wings are driven by a
        /// per-renderer property block instead — so a worn membrane sharing that material would
        /// inherit 0.22 m of displacement, pinned along an axis measured off a differently-shaped
        /// mesh. That is the nomad-cape failure: cloth bent inside out with a clean console.
        /// </para>
        /// </summary>
        private const float WornWind = 0.035f;

        private const string LeftMembrane = "Mesh_Wingsuit_Membrane_L";
        private const string RightMembrane = "Mesh_Wingsuit_Membrane_R";

        /// <summary>
        /// How big the folded suit is drawn in the hand, metres. A bracket off the item scale
        /// ladder rather than a measurement: the model is authored at the size it is worn at, and
        /// the hand is only ever a way of carrying it to the gear screen.
        /// </summary>
        private const float HoldSize = 0.85f;

        /// <summary>
        /// Zero, meaning the stowed size follows the hand size.
        ///
        /// The wing pack is the one item that diverges, and it diverges for a reason: it is a whole
        /// aircraft and is meant to fill the rack edge to edge. A wingsuit is a bundle of cloth on a
        /// spar and has nothing to say about how big it looks on a mat, so a second number here
        /// would be a number nobody could justify — which is exactly what `PackSizeTests` refuses.
        /// </summary>
        private const float PackSize = 0f;

        [MenuItem("Tools/SpaceGame/Items/Build Wingsuit")]
        public static void Build()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[Wingsuit] No model at {ModelPath}. Run " +
                               "_Source~/models/gear/wingsuit_export.py first.");
                return;
            }

            var root = new GameObject("Wingsuit");

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = "Model";
            visual.transform.SetParent(root.transform, false);

            // A bare static mesh arrives in Blender's frame; this is the standard −90° X that puts
            // up on +Y. The same rotation every other nested item model in this project gets.
            visual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            Transform left = Find(root.transform, LeftMembrane);
            Transform right = Find(root.transform, RightMembrane);


            Transform rightBatten = Find(root.transform, "Mesh_Wingsuit_Batten_R");
            Transform leftBatten = Find(root.transform, "Mesh_Wingsuit_Batten_L");

            Material cloth = EnsureClothMaterial(
                MaterialPath,
                right != null ? right : left,
                right != null ? rightBatten : leftBatten,
                -1f);
            Paint(left, cloth);
            Paint(right, cloth);

            AddWings(root, left, right);
            AddWornWings(root);
            AddFit(root);

            root.AddComponent<WingsuitRecolor>();
            AddItem(root, root.GetComponent<WingsuitWings>());

            ItemGrip grip = root.AddComponent<ItemGrip>();
            var gripSo = new SerializedObject(grip);
            SetFloat(gripSo, "holdSize", HoldSize);
            SetFloat(gripSo, "packSize", PackSize);
            gripSo.ApplyModifiedPropertiesWithoutUndo();

            AddIfPresent(root, "SpaceGame.Items.PickupableItem");

            // Without a NetworkObject a dropped wingsuit exists only on the host and never survives
            // a reload — and it must be registered in DefaultNetworkPrefabs.asset as well, which is
            // Tools ▸ SpaceGame ▸ Multiplayer ▸ Sync Network Prefabs.
            Unity.Netcode.NetworkObject netObject = root.AddComponent<Unity.Netcode.NetworkObject>();
            netObject.SynchronizeTransform = true;

            // Body, collider measured off the model, sizing, and the netcode that lets another
            // machine watch it be shoved about. One shared block.
            ItemWorldPresence.Apply(root);

            // prefabId and instanceId are left blank on purpose — SaveableEntity.OnValidate stamps
            // them, and a hand-written id is how two prefabs end up sharing one.
            root.AddComponent<SpaceGame.Core.Persistence.SaveableEntity>();
            root.AddComponent<SpaceGame.Core.Persistence.TransformSaveable>();

            // The wings ship SWITCHED OFF on the asset, not merely switched off by Awake.
            //
            // WornSeat scales a worn item so its measured size matches the fit, and ItemBounds
            // measures only the renderers that are on — so if the wings are visible at the moment
            // the suit is seated, the folded suit measures 2.5 m across and the pack on the
            // wearer's back is scaled down to a sliver. WingsuitWings.Awake does turn them off, and
            // at runtime that lands before the seat; but that is an ordering dependency between a
            // MonoBehaviour and a static call, and it is invisible in the editor because Awake does
            // not run there. Shipping them off makes the folded state the asset's own truth, and
            // Awake's call becomes a belt to the braces rather than the only thing holding it up.
            SetWingRenderers(visual.transform, false);

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            BuildInventoryItem(saved);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Wingsuit] Built {PrefabPath}, {MaterialPath} and {ItemPath}. " +
                      "Run Sync Network Prefabs, then Generate All Item Icons.");
        }

        /// <summary>
        /// The cloth material, with its wind anchor MEASURED off the membrane it will be on.
        ///
        /// <para>
        /// `ClothWind` pins a garment by a gradient along one object-space axis: vertices at the
        /// anchor plane are held still and vertices a `_FreeLength` away are free to blow. For this
        /// wing that is the leading edge (pinned, on the arm) to the trailing edge (free).
        /// </para>
        /// <para>
        /// Measured every run rather than carried as constants, and that is the whole point of this
        /// method. The nomad's cape shipped with measured-then-stale numbers once; a re-export
        /// changed its object space by about ninety times and every vertex ended up pinned at
        /// maximum displacement, which read in game as the cloak wrapping round the character's
        /// front. Nothing about that failure looks like a stale constant until you go looking.
        /// </para>
        /// <para>
        /// Note the axis is measured in UNITY's frame, not Blender's. The FBX conversion bakes
        /// `(x, y, z) → (−x, z, −y)` into the vertices, so the pin axis authored as Blender −Y
        /// arrives here as +Z — which is exactly the kind of thing a constant would get wrong.
        /// </para>
        /// </summary>
        /// <param name="path">Which material asset: the flight wing's or the worn wing's. Two of
        /// them, because their wind amplitudes and their object spaces both differ.</param>
        /// <param name="windStrength">Negative leaves _WindStrength at the shader's own default —
        /// the flight wing, whose amplitude is written per renderer at runtime.</param>
        private static Material EnsureClothMaterial(string path, Transform membrane,
                                                    Transform batten, float windStrength)
        {
            var shader = Shader.Find("SpaceGame/ClothWind");
            if (shader == null)
            {
                Debug.LogError("[Wingsuit] SpaceGame/ClothWind not found — the membrane will not " +
                               "move, and will render single-sided because only that shader " +
                               "declares Cull Off. Check the shader compiled.");
                return null;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            // The authored colour is only ever seen before a player's suit index lands on it —
            // WingsuitRecolor repaints this per renderer through a property block. Sun-cured
            // sailcloth, the same note as the palette's Mat_Fabric_Wing_Beige.
            mat.SetColor("_BaseColor", new Color(0.796f, 0.714f, 0.557f));
            mat.SetFloat("_Smoothness", 0.12f);

            // The membrane carries no UVs worth sampling a weave from, and a weave on a wing this
            // size would read as noise anyway.
            mat.SetFloat("_WeaveDepth", 0f);

            // Sun through cloth. This is what makes a wing lit from above read as a lit membrane
            // from underneath, which is most of what sells air pushing up into it.
            mat.SetFloat("_Backlight", 0.85f);

            if (windStrength >= 0f) mat.SetFloat("_WindStrength", windStrength);

            ApplyAnchor(mat, membrane, batten);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Find the axis the membrane's chord runs along and write the pin onto the material.
        ///
        /// <para>
        /// <b>The leading-edge spar decides it.</b> The spar lies along the wing's span by
        /// construction — it is the thing the cloth is stretched over — and it is a tapered tube
        /// fifteen times longer than it is thick, so which of its axes is its length is not a
        /// judgement call. The chord is then the wider of the membrane's two remaining extents,
        /// and the third is the panel's own thickness.
        /// </para>
        /// <para>
        /// This replaced "the span is simply the longest extent", which held for the flight wing
        /// (0.95 m span, 0.86 m chord) and INVERTED on the worn one (0.74 m span, 0.78 m chord) —
        /// pinning that panel across its span, so the shoulder end was held and the wrist end
        /// billowed. It also replaced a taper test, which looked principled and does not work: a
        /// wing is a triangle and therefore narrows along both axes, so both assignments score
        /// high and the answer is a coin flip.
        /// </para>
        /// <para>
        /// The leading edge is the end nearer the origin, because the model puts the origin on the
        /// shoulder end of the leading edge.
        /// </para>
        /// </summary>
        private static void ApplyAnchor(Material mat, Transform membrane, Transform batten)
        {
            Mesh mesh = membrane != null ? membrane.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (mesh == null)
            {
                Debug.LogError("[Wingsuit] No membrane mesh to measure the cloth anchor off; the " +
                               "wing will not billow. Check the FBX imported and the mesh names.");
                return;
            }

            Bounds b = mesh.bounds;
            Vector3 size = b.size;

            Mesh spar = batten != null ? batten.GetComponent<MeshFilter>()?.sharedMesh : null;
            int span;
            if (spar != null)
            {
                span = LargestAxis(spar.bounds.size, exclude: -1);
            }
            else
            {
                // No spar to ask. Fall back to the old assumption and SAY SO, because it is the
                // one that shipped a wing pinned the wrong way once.
                span = LargestAxis(size, exclude: -1);
                Debug.LogWarning($"[Wingsuit] No leading-edge spar beside '{membrane.name}', so " +
                                 "the wing's span was guessed as its longest axis. That guess is " +
                                 "wrong whenever the chord is deeper than the span is long.");
            }

            int chord = LargestAxis(size, exclude: span);

            float min = b.min[chord];
            float max = b.max[chord];

            // Whichever end is nearer zero is the leading edge — the model's origin sits on it.
            bool minIsLeading = Mathf.Abs(min) < Mathf.Abs(max);
            float anchor = minIsLeading ? min : max;
            float free = (minIsLeading ? max : min) - anchor;

            mat.SetFloat("_AnchorAxis", chord);
            mat.SetFloat("_AnchorOrigin", anchor);
            mat.SetFloat("_FreeLength", free);

            // How far a free vertex may actually travel. The billow amplitude itself is driven at
            // runtime from airspeed by WingsuitWings; this is the ceiling on it.
            //
            // In METRES, unlike the three above — and that difference is the trap. ClothWind's
            // anchor is expressed in the mesh's own object space, but its amplitudes are metres
            // that the shader converts to object units itself. A Blender FBX lands here at a lossy
            // scale of 100, so a ceiling computed from the object-space chord came out at three
            // MILLIMETRES: the wing was pinned rigid and read as a sheet of plywood, with nothing
            // in the console to say why.
            float chordMetres = Mathf.Abs(free) * membrane.lossyScale[chord];
            mat.SetFloat("_MaxStretch", chordMetres * 0.35f);

            // Higher than the cape's: a membrane on a spar is taut near its leading edge and only
            // loose out at the hem, where a cloak hangs slack most of the way down.
            mat.SetFloat("_Stiffness", 2.2f);

            Debug.Log($"[Wingsuit] Cloth anchor measured on {membrane.name}: " +
                      $"span {"XYZ"[span]}, axis {"XYZ"[chord]}, origin {anchor:F4}, " +
                      $"free length {free:F4} object units ({chordMetres:F3} m), " +
                      $"max stretch {chordMetres * 0.35f:F3} m.");
        }

        private static int LargestAxis(Vector3 size, int exclude)
        {
            int best = -1;
            for (int i = 0; i < 3; i++)
            {
                if (i == exclude) continue;
                if (best < 0 || size[i] > size[best]) best = i;
            }
            return best;
        }

        /// <summary>
        /// Every renderer that belongs to a FLIGHT wing — both membranes and both spars.
        ///
        /// <para>
        /// Hand it the flight model, never the prefab root. The worn model's parts are named
        /// <c>Mesh_WingsuitWorn_Membrane_L</c> and <c>Mesh_WingsuitWorn_Batten_L</c>, and both of
        /// those contain the substrings below — so a sweep from the root switched the worn wing's
        /// cloth off as well, and it stayed off for the life of the asset. The symptom was a worn
        /// suit that showed a yoke, two spars and two cuffs with nothing stretched between them,
        /// and the measurement gave it away first: 0.63 m tall where the model is 0.91.
        /// </para>
        /// </summary>
        private static void SetWingRenderers(Transform root, bool enabled)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.Contains("Membrane") && !t.name.Contains("Batten")) continue;

                var renderer = t.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = enabled;
            }
        }

        private static void Paint(Transform membrane, Material cloth)
        {
            if (membrane == null || cloth == null) return;

            var renderer = membrane.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterials = new[] { cloth };
        }

        private static void AddWings(GameObject root, Transform left, Transform right)
        {
            if (left == null || right == null)
            {
                Debug.LogError($"[Wingsuit] The model has no '{LeftMembrane}' / '{RightMembrane}'. " +
                               "A rename in wingsuit.blend has to be matched here, or the wings " +
                               "stay folded forever.");
            }

            WingsuitWings wings = root.AddComponent<WingsuitWings>();
            var so = new SerializedObject(wings);
            Set(so, "leftMembrane", left);
            Set(so, "rightMembrane", right);

            // The leading-edge spars, named so the wings can adopt them at runtime. They are NOT
            // reparented here: the model arrives as a nested prefab instance and Unity refuses to
            // restructure the interior of one, silently — the reparent appears to take and is gone
            // again by the time the asset is saved. WingsuitWings does it in Awake instead, where
            // there is no prefab to argue with.
            Set(so, "leftBatten", Find(root.transform, "Mesh_Wingsuit_Batten_L"));
            Set(so, "rightBatten", Find(root.transform, "Mesh_Wingsuit_Batten_R"));

            // A first fit, tuned in play. The membrane's origin is the shoulder end of its own
            // leading edge, so it wants to sit near the top of the upper-arm bone with its span
            // running down the arm — which on this rig is the bone's own −Y.
            SetVector(so, "rightLocalPosition", new Vector3(0f, 0f, 0f));
            SetVector(so, "rightLocalEuler", new Vector3(0f, 0f, -90f));
            SetVector(so, "leftLocalPosition", new Vector3(0f, 0f, 0f));
            SetVector(so, "leftLocalEuler", new Vector3(0f, 0f, 90f));

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Nest the worn wing — what the suit looks like ON somebody, as opposed to the model
        /// above, which is what it looks like in their hand and what their arms wear in flight.
        ///
        /// <para>
        /// Two cloth panels between the wearer's arms, on an over-shoulder yoke. It replaces the
        /// spar case that used to be the whole of the worn look: a box on a back says nothing
        /// about what the item is, and the gear screen is where a player decides what to put on.
        /// </para>
        /// <para>
        /// Switched off here, on the asset, and switched on by <see cref="WornSeat"/> through
        /// <see cref="WornVisual"/> — the same reason the flight wings ship off. <c>ItemBounds</c>
        /// measures only what is switched on, and both <c>ItemGrip</c> sizes scale from that
        /// measurement, so a visible 2 m wing would shrink the folded suit in the hand to a chip.
        /// </para>
        /// <para>
        /// The cloth gets its own <c>ClothWind</c> material, with its anchor measured off its own
        /// mesh and a much smaller wind — see <see cref="WornWind"/> for why sharing the flight
        /// wing's would have bent it inside out. It still takes the wearer's suit colour:
        /// <see cref="WingsuitRecolor"/> matches by material NAME and its table names both.
        /// </para>
        /// </summary>
        private static void AddWornWings(GameObject root)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(WornModelPath);
            if (model == null)
            {
                Debug.LogError($"[Wingsuit] No worn model at {WornModelPath}. Run " +
                               "_Source~/models/gear/wingsuit_worn_export.py first. Without it " +
                               "the suit is worn as a spar case.");
                return;
            }

            // NO rotation, unlike the flight model above, and that is the fix for a real bug. An
            // FBX from `_exportlib` arrives ALREADY converted: every mesh node carries the
            // Blender-to-Unity position ((x, y, z) → (−x, z, −y)) and its own −90 X, with the
            // vertices left in Blender's axes. A −90 X on the parent is therefore a SECOND
            // conversion. This model is authored in the wearer's own frame — its wing roots ARE
            // the measured shoulder joints — so any turn at all moves it off the wearer: applied
            // here it put both wings at the waist, pointing backwards, and it still looked
            // plausibly like a wingsuit.
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = WornVisual.ChildName;
            visual.transform.SetParent(root.transform, false);

            var membranes = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in visual.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains("Membrane")) membranes.Add(t);

            if (membranes.Count == 0)
                Debug.LogError("[Wingsuit] The worn model has no object with 'Membrane' in its " +
                               "name, so its cloth keeps the palette's flat beige and never takes " +
                               "the wearer's suit colour. A rename in wingsuit_worn.blend has to " +
                               "be matched here.");

            Transform wornBatten = null;
            foreach (Transform t in visual.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains("Batten")) { wornBatten = t; break; }

            Material worn = membranes.Count > 0
                ? EnsureClothMaterial(WornMaterialPath, membranes[0], wornBatten, WornWind)
                : null;
            foreach (Transform t in membranes) Paint(t, worn);

            visual.SetActive(false);
        }

        /// <summary>
        /// Where the worn suit sits on the torso.
        ///
        /// <para>
        /// <b>Bone-anchored, unlike every other back item.</b> The worn wings are shaped around
        /// the WEARER — their roots are the measured upper-arm joints and their hems stop just
        /// above the measured hips — so the model's origin is the spine bone and the offset is
        /// zero. Seating it on the pack's lash rail instead, which is what a back item normally
        /// gets, would hang the wings half a metre behind the shoulders they belong to.
        /// </para>
        /// </summary>
        private static void AddFit(GameObject root)
        {
            WornFit fit = root.AddComponent<WornFit>();
            var so = new SerializedObject(fit);
            SetVector(so, "localPosition", Vector3.zero);
            SetVector(so, "localEuler", Vector3.zero);
            SetBool(so, "anchorToBone", true);

            // The span of the worn wing across the wearer, in metres — and therefore the scale
            // correction, because the model is authored at exactly this span, off the same
            // measurements as the shoulder and hip it is fitted to. Pinned rather than left at
            // zero ("keep the prefab's own scale") so a re-export that changes the size shows up
            // as a number disagreeing with the exporter's own printout
            // (`wingsuit_worn_export.py` prints "pin WornFit.size to ...") rather than as wings
            // that quietly no longer reach the arms.
            //
            // 1.58 -> 2.60 on 2026-09-04: the panel was rebuilt at twice its own size
            // (`wingsuit_worn.py`'s WING_SCALE), so the cloth now runs out past the hands rather
            // than ending at the wrist. Grown in the MODEL rather than here on purpose — this
            // number scales about the spine bone, so raising it alone would lift the wing roots
            // off the shoulders it is measured against.
            SetFloat(so, "size", 2.60f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddItem(GameObject root, WingsuitWings wings)
        {
            WingsuitItem item = root.AddComponent<WingsuitItem>();
            var so = new SerializedObject(item);
            Set(so, "wings", wings);

            // Unlimited: the suit is equipment, not a consumable. −1 is UsableItem's sentinel.
            SetInt(so, "maxUses", -1);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildInventoryItem(GameObject prefab)
        {
            InventoryItem item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            bool isNew = item == null;
            if (isNew)
                item = ScriptableObject.CreateInstance<InventoryItem>();

            item.itemName = "Wingsuit";
            item.itemPrefab = prefab;

            // The torso slot, which it shares with the wing pack — one or the other, never both.
            item.equipKind = EquipKind.Back;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ItemPath));
            if (isNew)
                AssetDatabase.CreateAsset(item, ItemPath);
            else
                EditorUtility.SetDirty(item);
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static void AddIfPresent(GameObject go, string typeName)
        {
            System.Type t = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName))
                .FirstOrDefault(x => x != null);
            if (t != null) go.AddComponent(t);
            else Debug.LogWarning($"[Wingsuit] No type '{typeName}'; skipped.");
        }

        private static void Set(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.floatValue = value;
        }

        private static void SetInt(SerializedObject so, string field, int value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.intValue = value;
        }

        private static void SetVector(SerializedObject so, string field, Vector3 value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.vector3Value = value;
        }

        private static void SetBool(SerializedObject so, string field, bool value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.boolValue = value;
        }

        private static SerializedProperty Find(SerializedObject so, string field)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
                Debug.LogWarning($"[Wingsuit] {so.targetObject.GetType().Name} has no serialized " +
                                 $"field '{field}'; left at its default.");
            return p;
        }
    }
}
