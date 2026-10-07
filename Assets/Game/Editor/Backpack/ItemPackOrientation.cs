using System.Text;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Puts two artifact prefabs the right way up.
    ///
    /// <para><b>Why the pack cannot fix this itself.</b> A stowed item keeps its own up: the
    /// backpack turns it about the surface normal by the player's yaw and nothing else. That is
    /// deliberate — <c>ItemFootprint.FootprintOf</c> is DEFINED as <c>(size.x, size.z)</c>, the
    /// shadow an item casts with its own up still up, so an item re-oriented at seating time would
    /// occupy a different rectangle from the one the layout reserved for it. (This is the same
    /// reasoning that deleted <c>BackpackSeat</c>.) So "this artifact points the wrong way" is
    /// never a seating bug. It is authored data, and it is fixed here.</para>
    ///
    /// <para><b>How the wrong ones were identified.</b> Not by eye. Three of these prefabs
    /// disagree with their own hand-authored root collider by exactly 90&#176; about X:</para>
    /// <list type="bullet">
    /// <item><c>AntiGravityPotion</c> — CapsuleCollider on the <b>Y</b> axis, height 0.271,
    /// radius 0.0718, centred at y 0.123. The mesh is 0.271 long, 0.144 wide... along <b>Z</b>,
    /// spanning z -0.259..0.012. Rotate the mesh +90&#176; about X and it lands on y -0.012..0.259:
    /// the capsule, to three decimals. Its <c>Grip</c> child sits at y 0.055 — 22 mm above
    /// anything the mesh currently occupies, and on the stem once it stands up.</item>
    /// <item><c>LightningSpell</c> — SphereCollider centred at y 0.132, radius 0.133; the mesh runs
    /// z -0.264..0. Half of 0.264 is 0.132.</item>
    /// </list>
    /// <para>Both carry the identical override on their model instance — a -90&#176; X
    /// rotation, quaternion (-0.7071068, 0, 0, 0.7071067) — which is exactly the Blender-Z-up to
    /// Unity-Y-up conversion the FBX importer had already done, applied a second time. The
    /// colliders and grip points were authored before it. Removing it is the fix, and it also
    /// makes <c>rotationOffset (0,0,0)</c> mean what <see cref="ItemGrip"/> documents it to mean:
    /// "the item's +Y points out the thumb side, as a torch's flame would."</para>
    ///
    /// <para><b>Three more entries used to live here</b> — <c>Leash</c>, <c>ItemScanner</c> and
    /// <c>RuinScanner</c>, all forearm devices standing on the edge of the webbing cuff they were
    /// mounted on. They went when the gauntlets were rebuilt on the shared gauntlet base
    /// (2026-09-02): a gauntlet now lies deck-up with the arm's own axis flat on the mat, which is
    /// both its smallest footprint and the one that shows what the item is. See the note in
    /// <c>Fixes</c>.</para>
    ///
    /// </summary>
    public static class ItemPackOrientation
    {
        private const string Folder = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/";

        /// <summary>What kind of wrongness an entry describes.</summary>
        private enum Mode
        {
            /// <summary>The geometry disagrees with the prefab's own frame. Turn the geometry;
            /// leave the collider, the grip point and the hand offset exactly where they are.</summary>
            AlignModel,

            /// <summary>The whole frame is wrong. Turn everything under the root, carry the root's
            /// colliders with it, and undo the turn in <c>rotationOffset</c> so the hand does not
            /// notice.</summary>
            Reframe,
        }

        private readonly struct Fix
        {
            public readonly string Path;
            public readonly Mode Mode;

            /// <summary>Rotation applied, in the item root's own frame.</summary>
            public readonly Vector3 Rotation;

            /// <summary>The state this entry refuses to run against anything but.
            /// For <see cref="Mode.AlignModel"/> the model child's current local euler; for
            /// <see cref="Mode.Reframe"/> the grip's current <c>rotationOffset</c>.</summary>
            public readonly Vector3 Expected;

            public readonly string Why;

            public Fix(string file, Mode mode, Vector3 rotation, Vector3 expected, string why)
            {
                Path = Folder + file;
                Mode = mode;
                Rotation = rotation;
                Expected = expected;
                Why = why;
            }
        }

        private static readonly Fix[] Fixes =
        {
            new("AntiGravityPotion.prefab", Mode.AlignModel, new Vector3(90f, 0f, 0f),
                new Vector3(-90f, 0f, 0f),
                "mesh lies along -Z; its own CapsuleCollider is on Y, h 0.271, centre y 0.123"),

            new("LightningSpell.prefab", Mode.AlignModel, new Vector3(90f, 0f, 0f),
                new Vector3(-90f, 0f, 0f),
                "mesh lies along -Z; its own SphereCollider is centred at y 0.132, r 0.133"),

            // The gauntlet family had three rows here and no longer needs them. They turned a
            // device that was standing on the edge of its webbing cuff onto its flank. Since the
            // family was rebuilt on `components/props/gauntlet_base.blend` (2026-09-02) a gauntlet
            // already lies the way it should: across the arm on X, the arm's own axis on Z, and
            // the device standing UP on Y off the hardpoint. That is both the smaller footprint
            // and the readable one — a bracer laid on its flank hides its device behind its own
            // shell, and the device is how a player tells one gauntlet from another at a glance
            // (GDC-L1-UX-0003). Its hand offset is identity now too, so there is nothing left to
            // divide out.
        };

        /// <summary>Degrees of slop when matching an expected rotation.</summary>
        private const float Slack = 1f;

        /// <summary>
        /// Where an <see cref="Mode.AlignModel"/> entry leaves the model child, and where a
        /// <see cref="Mode.Reframe"/> entry leaves <c>rotationOffset</c> — derived from the entry
        /// rather than typed beside it, so a table row can never disagree with its own check.
        ///
        /// <para>
        /// The first four entries all land on identity, and hard-coding that is how the check
        /// quietly stopped being a check: an entry whose <c>Expected</c> is not the exact inverse
        /// of its <c>Rotation</c> — <c>RuinScanner</c>, whose hand-authored offset was already
        /// (0, 90, 0) — reports a clean save and then FAILS verification for landing where it was
        /// supposed to land.
        /// </para>
        /// </summary>
        private static Quaternion Landing(Fix fix)
        {
            var rotation = Quaternion.Euler(fix.Rotation);
            var expected = Quaternion.Euler(fix.Expected);

            // AlignModel turns the child by the rotation; Reframe divides the rotation back out of
            // the hand offset, which is the whole reason the hand does not notice.
            return fix.Mode == Mode.AlignModel
                ? rotation * expected
                : expected * Quaternion.Inverse(rotation);
        }

        // ── Menu ─────────────────────────────────────────────────────────────

        [MenuItem("Tools/SpaceGame/Items/Fix Artifact Pack Orientation")]
        public static void Apply()
        {
            var log = new StringBuilder("Artifact pack orientation\n");
            int changed = 0;

            foreach (Fix fix in Fixes)
            {
                if (ApplyOne(fix, log)) changed++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Measurements are cached per prefab for the life of a session, and every one of them
            // just moved.
            ItemFootprint.ClearCache();

            log.Append("  changed  ").Append(changed).Append(" of ").Append(Fixes.Length).Append('\n');

            if (Verify(log)) Debug.Log(log.ToString());
        }

        /// <summary>Report what each entry would do, without writing anything.</summary>
        [MenuItem("Tools/SpaceGame/Items/Audit Artifact Pack Orientation")]
        public static void Audit()
        {
            var log = new StringBuilder("Artifact pack orientation (audit only)\n");

            foreach (Fix fix in Fixes)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fix.Path);
                if (prefab == null)
                {
                    log.Append("  MISSING  ").Append(fix.Path).Append('\n');
                    continue;
                }

                Bounds bounds = ItemBounds.Measure(prefab, null);
                log.Append("  ").Append(System.IO.Path.GetFileNameWithoutExtension(fix.Path))
                   .Append("\n    now      size ").Append(bounds.size.ToString("F3"))
                   .Append(" centre ").Append(bounds.center.ToString("F3"))
                   .Append("\n    would    ").Append(fix.Mode).Append(' ').Append(fix.Rotation)
                   .Append("\n    because  ").Append(fix.Why).Append('\n');
            }

            Debug.Log(log.ToString());
        }

        // ── One prefab ───────────────────────────────────────────────────────

        private static bool ApplyOne(Fix fix, StringBuilder log)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(fix.Path);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(fix.Path) == null)
            {
                log.Append("  MISSING  ").Append(fix.Path).Append('\n');
                return false;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(fix.Path);
            if (contents == null)
            {
                log.Append("  FAILED   could not open ").Append(fix.Path).Append('\n');
                return false;
            }

            try
            {
                var rotation = Quaternion.Euler(fix.Rotation);
                bool did = fix.Mode == Mode.AlignModel
                    ? AlignModel(contents, fix, rotation, name, log)
                    : Reframe(contents, fix, rotation, name, log);

                if (!did) return false;

                // Everything above is an override on prefab-instance children. Without this the
                // save writes a prefab with none of it in it — silently.
                PrefabUtility.RecordPrefabInstancePropertyModifications(contents);

                PrefabUtility.SaveAsPrefabAsset(contents, fix.Path, out bool success);

                if (!success)
                {
                    log.Append("  FAILED   could not save ").Append(fix.Path)
                       .Append(" — is the AssetDatabase read-only?\n");
                    return false;
                }

                log.Append("    saved  ").Append(fix.Path).Append('\n');
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Turn the geometry so it agrees with the frame the rest of the prefab was authored in.
        /// Only the children that actually carry renderers move: the grip point, the colliders and
        /// the hand offset are all part of the frame being agreed WITH, so they stay put.
        /// </summary>
        private static bool AlignModel(GameObject root, Fix fix, Quaternion rotation,
                                       string name, StringBuilder log)
        {
            var expected = Quaternion.Euler(fix.Expected);
            int moved = 0;
            int skipped = 0;

            foreach (Transform child in root.transform)
            {
                if (child.GetComponentInChildren<Renderer>(true) == null) continue;

                if (Quaternion.Angle(child.localRotation, expected) > Slack)
                {
                    skipped++;
                    log.Append("  ").Append(name).Append("\n    SKIPPED  '").Append(child.name)
                       .Append("' is at ").Append(child.localEulerAngles.ToString("F1"))
                       .Append(", expected ").Append(fix.Expected.ToString("F1"))
                       .Append(" — already fixed, or changed since this table was written\n");
                    continue;
                }

                child.localRotation = rotation * child.localRotation;
                child.localPosition = rotation * child.localPosition;
                moved++;
            }

            if (moved == 0)
            {
                if (skipped == 0)
                    log.Append("  ").Append(name).Append("\n    SKIPPED  no child carries geometry\n");
                return false;
            }

            log.Append("  ").Append(name).Append("\n    align    turned ").Append(moved)
               .Append(" model child(ren) by ").Append(fix.Rotation.ToString("F0"))
               .Append("\n    because  ").Append(fix.Why).Append('\n');
            return true;
        }

        /// <summary>
        /// Turn the whole item and undo the turn in <c>rotationOffset</c>, so the pack and the
        /// ground see a new resting pose and the hand sees no change at all.
        /// </summary>
        private static bool Reframe(GameObject root, Fix fix, Quaternion rotation,
                                    string name, StringBuilder log)
        {
            var grip = root.GetComponent<ItemGrip>();
            if (grip == null)
            {
                log.Append("  ").Append(name).Append("\n    SKIPPED  no ItemGrip to compensate on; ")
                   .Append("re-framing without one would move the item in the hand\n");
                return false;
            }

            if (Quaternion.Angle(Quaternion.Euler(grip.RotationOffset),
                                 Quaternion.Euler(fix.Expected)) > Slack)
            {
                log.Append("  ").Append(name).Append("\n    SKIPPED  rotationOffset is ")
                   .Append(grip.RotationOffset.ToString("F1")).Append(", expected ")
                   .Append(fix.Expected.ToString("F1"))
                   .Append(" — already fixed, or retuned since this table was written\n");
                return false;
            }

            foreach (Transform child in root.transform)
            {
                child.localRotation = rotation * child.localRotation;
                child.localPosition = rotation * child.localPosition;
            }

            // The root's own colliders are in the frame that just turned under them. Children's
            // colliders came along with their transforms and need nothing.
            int colliders = RotateRootColliders(root, rotation);

            // t.rotation = handRotation * Euler(offset). The contents turned by R, so the offset
            // has to turn by R inverse for the product to come out where it already was.
            Quaternion compensated = Quaternion.Euler(grip.RotationOffset) * Quaternion.Inverse(rotation);

            var serialized = new SerializedObject(grip);
            SerializedProperty property = serialized.FindProperty("rotationOffset");

            if (property == null)
            {
                log.Append("  ").Append(name)
                   .Append("\n    FAILED   ItemGrip has no 'rotationOffset' field any more\n");
                return false;
            }

            property.vector3Value = Round(compensated.eulerAngles);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            log.Append("  ").Append(name).Append("\n    reframe  turned the item by ")
               .Append(fix.Rotation.ToString("F0")).Append(", ").Append(colliders)
               .Append(" root collider(s) with it, rotationOffset ")
               .Append(fix.Expected.ToString("F0")).Append(" -> ")
               .Append(property.vector3Value.ToString("F0"))
               .Append("\n    because  ").Append(fix.Why).Append('\n');
            return true;
        }

        /// <summary>
        /// Carry the root's own colliders through the same rotation. Only the three primitives
        /// that appear on these prefabs; a MeshCollider needs no help, since its mesh is the
        /// geometry that just turned.
        /// </summary>
        private static int RotateRootColliders(GameObject root, Quaternion rotation)
        {
            int count = 0;

            foreach (Collider collider in root.GetComponents<Collider>())
            {
                switch (collider)
                {
                    case SphereCollider sphere:
                        sphere.center = rotation * sphere.center;
                        count++;
                        break;

                    case CapsuleCollider capsule:
                        capsule.center = rotation * capsule.center;
                        capsule.direction = NearestAxis(rotation * Axis(capsule.direction));
                        count++;
                        break;

                    case BoxCollider box:
                        box.center = rotation * box.center;
                        box.size = Abs(rotation * box.size);
                        count++;
                        break;
                }
            }

            return count;
        }

        private static Vector3 Axis(int index) =>
            index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;

        private static int NearestAxis(Vector3 v)
        {
            Vector3 a = Abs(v);
            if (a.x >= a.y && a.x >= a.z) return 0;
            return a.y >= a.z ? 1 : 2;
        }

        private static Vector3 Abs(Vector3 v) =>
            new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>
        /// Snap a euler that came out of a quaternion back onto whole degrees, and out of the
        /// 0..360 range. <c>359.99998</c> in an inspector is how a right angle stops looking like
        /// an authored value and starts looking like drift.
        /// </summary>
        private static Vector3 Round(Vector3 euler)
        {
            return new Vector3(RoundAngle(euler.x), RoundAngle(euler.y), RoundAngle(euler.z));
        }

        private static float RoundAngle(float degrees)
        {
            float wrapped = Mathf.Repeat(degrees, 360f);
            if (wrapped > 180f) wrapped -= 360f;

            float snapped = Mathf.Round(wrapped);
            return Mathf.Abs(snapped - wrapped) < 0.01f ? snapped : wrapped;
        }

        // ── Verification ─────────────────────────────────────────────────────

        /// <summary>
        /// Re-load everything off disk and check it took.
        ///
        /// <para>
        /// Not paranoia. Unity discards prefab saves when the AssetDatabase is read-only and logs
        /// nothing, so a run can report four successes and change no file at all.
        /// </para>
        /// </summary>
        private static bool Verify(StringBuilder log)
        {
            bool ok = true;

            foreach (Fix fix in Fixes)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fix.Path);
                if (prefab == null) continue;

                string name = System.IO.Path.GetFileNameWithoutExtension(fix.Path);
                Bounds bounds = ItemBounds.Measure(prefab, null);
                Quaternion landing = Landing(fix);

                if (fix.Mode == Mode.AlignModel)
                {
                    foreach (Transform child in prefab.transform)
                    {
                        if (child.GetComponentInChildren<Renderer>(true) == null) continue;

                        if (Quaternion.Angle(child.localRotation, landing) > Slack)
                        {
                            ok = false;
                            log.Append("  FAILED   ").Append(name).Append(": '").Append(child.name)
                               .Append("' is at ").Append(child.localEulerAngles.ToString("F1"))
                               .Append(", wanted ").Append(landing.eulerAngles.ToString("F1"))
                               .Append(" — the save did not land\n");
                        }
                    }
                }
                else
                {
                    var grip = prefab.GetComponent<ItemGrip>();
                    if (grip != null &&
                        Quaternion.Angle(Quaternion.Euler(grip.RotationOffset), landing) > Slack)
                    {
                        ok = false;
                        log.Append("  FAILED   ").Append(name).Append(": rotationOffset is ")
                           .Append(grip.RotationOffset.ToString("F1")).Append(", wanted ")
                           .Append(landing.eulerAngles.ToString("F1"))
                           .Append(" — the save did not land\n");
                    }
                }

                log.Append("  verify   ").Append(name).Append(" size ")
                   .Append(bounds.size.ToString("F3")).Append(" centre ")
                   .Append(bounds.center.ToString("F3")).Append('\n');
            }

            if (!ok) Debug.LogError(log.ToString());

            return ok;
        }
    }
}
