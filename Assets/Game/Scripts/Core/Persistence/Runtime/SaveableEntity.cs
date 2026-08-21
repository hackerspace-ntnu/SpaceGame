using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Marks a GameObject as something the save system knows about, and gives it the two identities
    /// persistence needs.
    ///
    /// <b>prefabId</b> answers "what do I instantiate to bring this back?" — the asset GUID of the
    /// prefab, stamped in by <c>OnValidate</c> exactly the way <c>InventoryItem</c> already derives
    /// its registry ID. GUIDs are used rather than names because a save file outlives every rename.
    ///
    /// <b>instanceId</b> answers "which record is mine?" — a GUID per object. Authored objects get
    /// theirs at edit time and carry it in the scene file; runtime objects get theirs when they are
    /// spawned. That difference is the whole reason the two populations are stored differently:
    /// an authored object is already in the scene when the chunk loads, so re-creating it from a
    /// record would duplicate it.
    /// </summary>
    /// <summary>
    /// Who owns an entity's record.
    ///
    /// Not every saveable object belongs to the world. A player lives in the persistent scene and
    /// carries a SaveableEntity like anything else, but its record is owned by
    /// <see cref="PlayerSaveService"/> and keyed by profile. Left on <see cref="World"/> it would
    /// ALSO be captured as a world entity — and re-instantiated from its prefab on load, next to
    /// the player Netcode spawned, giving every load a lifeless duplicate of the player.
    /// </summary>
    public enum SaveScope
    {
        /// <summary>Captured and restored by the world store, with the scene it stands in.</summary>
        World,

        /// <summary>Owned by another system. The world store sees it and passes over it.</summary>
        External,
    }

    [DisallowMultipleComponent]
    public class SaveableEntity : MonoBehaviour
    {
        [Tooltip("Asset GUID of the prefab this object comes from. Assigned automatically; the " +
                 "save system uses it to instantiate the object again on load.")]
        [SerializeField] private string prefabId;

        [Tooltip("Identity of this particular object. Assigned automatically — at edit time for " +
                 "objects placed in a scene, at spawn time for objects created at runtime.")]
        [SerializeField] private string instanceId;

        [Tooltip("True when this object was placed in a scene by hand. Authored objects are " +
                 "restored in place; runtime objects are re-instantiated from prefabId.")]
        [SerializeField] private bool authored;

        [Tooltip("World: saved with the scene this object stands in. External: another system owns " +
                 "this object's record — set this on players, whose state is keyed by profile.")]
        [SerializeField] private SaveScope scope = SaveScope.World;

        public string PrefabId => prefabId;
        public string InstanceId => instanceId;
        public bool IsAuthored => authored;
        public SaveScope Scope => scope;

        /// <summary>Whether the world store should capture and restore this object with its scene.</summary>
        public bool BelongsToWorld => scope == SaveScope.World;

        /// <summary>
        /// Hand this object's record to whoever spawned it, at runtime.
        ///
        /// <para>
        /// For objects a system creates, owns and re-creates itself. <c>NpcWorldSim</c> is the case
        /// it was added for: a caravan is persisted as ONE record — a position, a destination, a
        /// task — and the members standing in the world are something that record rebuilds on
        /// demand. Left on <see cref="SaveScope.World"/> they are also captured individually, so a
        /// load re-instantiates every member from its prefab AND the simulator spawns the group
        /// again from its record. That is the same duplicate-on-load this enum's summary describes
        /// for the player, arriving by a different route.
        /// </para>
        /// <para>
        /// Runtime-only and deliberately one-way: an object whose record belongs to another system
        /// never goes back to belonging to the world, and a prefab has no business shipping with an
        /// opinion about which system spawned it.
        /// </para>
        /// </summary>
        public void DisownToExternal()
        {
#if UNITY_EDITOR
            // "Runtime-only" was a comment, not a rule. This writes a [SerializeField], so calling it
            // outside play mode against a prefab asset or a scene instance persists the change into
            // the asset — permanently removing that object from world capture, with nothing said and
            // nothing to see in the inspector unless somebody goes looking for a scope enum.
            if (!Application.isPlaying)
            {
                Debug.LogError($"[Save] DisownToExternal() was called on '{name}' outside play mode. " +
                               "Ignored: it writes a serialized field, so it would bake the change " +
                               "into the asset and take the object out of every future save.", this);
                return;
            }
#endif
            scope = SaveScope.External;
        }

        /// <summary>
        /// Every live entity, so a save can find them without a scene-wide component search per
        /// chunk. Registration is by instanceId; a duplicate id means two objects would fight over
        /// one record, which is worth a warning rather than a silent overwrite.
        ///
        /// <b>Membership lasts as long as the object, not as long as it is enabled.</b> That
        /// distinction is not academic here: <c>HealthReactionModule</c> kills an agent with
        /// <c>SetActive(false)</c>, so a corpse is a disabled GameObject rather than a destroyed one.
        /// While registration was tied to OnEnable/OnDisable, every corpse fell out of this
        /// dictionary — and two things read it as "does this still exist":
        /// <c>WorldSaveStore.SpawnEntities</c>, which then re-instantiated dead runtime entities on
        /// every hydrate, and <see cref="SaveRefBinder"/>, which could not resolve a reference to
        /// anything dead.
        /// </summary>
        private static readonly Dictionary<string, SaveableEntity> Live = new();

        public static IReadOnlyDictionary<string, SaveableEntity> LiveEntities => Live;

        private readonly List<ISaveable> savers = new();
        private bool saversGathered;

        /// <summary>
        /// Set when this object has been tombstoned and is on its way out.
        ///
        /// <c>Object.Destroy</c> is deferred to the end of the frame, so a tombstoned object is
        /// still in its scene, still enabled and still in <see cref="LiveEntities"/> for the rest of
        /// it. Any capture landing in that window re-created the record the tombstone had just
        /// removed — and an authored record is never dropped again, so the file kept a permanent
        /// entry for an object that no longer exists.
        /// </summary>
        public bool IsBuried { get; private set; }

        /// <summary>Marks this object as destroyed-for-good, so nothing captures it on the way out.</summary>
        public void MarkBuried() => IsBuried = true;

        private void Awake()
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                // A runtime object that nobody stamped — a prefab instantiated directly rather than
                // through the save system. Give it an identity anyway so its state is not silently
                // dropped from every save for the rest of the session.
                instanceId = FallbackIdentity();
            }

            if (Live.TryGetValue(instanceId, out SaveableEntity existing) && existing != null && existing != this)
            {
                Debug.LogWarning(
                    $"[Save] '{name}' and '{existing.name}' share instance id {instanceId}. " +
                    "One was likely duplicated in a scene without OnValidate running. Reassigning.",
                    this);

                instanceId = FallbackIdentity(avoid: instanceId);
            }

            Live[instanceId] = this;
        }

        /// <summary>
        /// The identity to fall back on when the serialized one is missing or already taken.
        ///
        /// <b>An authored object must never be handed a random GUID.</b> It keeps <c>authored</c>
        /// true, and authored records are deliberately never dropped by
        /// <c>WorldSaveStore.DropVanishedRuntime</c> — so a fresh GUID every session meant the
        /// object wrote its state under an id nothing ever looked up again AND left one more dead
        /// record in the file on every single launch. Two objects that collide would also swap
        /// which of them "won" depending on Awake order.
        ///
        /// Deriving from the hierarchy instead gives the same answer on every load of an unchanged
        /// scene, which is the whole property an identity needs. The collision case appends a
        /// discriminator so the loser is stable too rather than merely different.
        /// </summary>
        private string FallbackIdentity(string avoid = null)
        {
            if (!authored || !gameObject.scene.IsValid() || string.IsNullOrEmpty(gameObject.scene.name))
                return Guid.NewGuid().ToString("N");

            string derived = DeriveAuthoredId(gameObject);
            if (string.IsNullOrEmpty(derived)) return Guid.NewGuid().ToString("N");

            if (derived != avoid && !Live.ContainsKey(derived)) return derived;

            // Two authored objects deriving the same id. Walk a deterministic discriminator rather
            // than randomising, so the same pair resolves the same way next session.
            for (int i = 1; i < 64; i++)
            {
                string candidate = derived + "#" + i.ToString();
                if (candidate != avoid && !Live.ContainsKey(candidate)) return candidate;
            }

            return Guid.NewGuid().ToString("N");
        }

        private void OnDestroy()
        {
            if (!string.IsNullOrEmpty(instanceId) &&
                Live.TryGetValue(instanceId, out SaveableEntity registered) && registered == this)
            {
                Live.Remove(instanceId);
            }
        }

        /// <summary>
        /// Attaches identity to a freshly spawned object, or refreshes the identity of one that
        /// already has the component. Call this at every runtime spawn point that should persist —
        /// dropped items, deployed gear — so the object is saveable even if its prefab was never
        /// stamped in the editor.
        /// </summary>
        public static SaveableEntity EnsureRuntime(GameObject target, string prefabId)
        {
            if (target == null) return null;

            SaveableEntity entity = target.GetComponent<SaveableEntity>();
            if (entity == null) entity = target.AddComponent<SaveableEntity>();

            // Never demote an authored object. This method is documented as "call at every runtime
            // spawn point", so it is only a matter of time before it is handed a scene object by
            // mistake — and flipping `authored` on one is a duplication bug that logs nothing: the
            // scene file supplies the authored copy on the next load AND the record now asks
            // SpawnEntities to instantiate a second one from prefabId.
            if (entity.authored && entity.gameObject.scene.IsValid() &&
                !string.IsNullOrEmpty(entity.gameObject.scene.name))
            {
                Debug.LogWarning(
                    $"[Save] EnsureRuntime was called on '{target.name}', which is an AUTHORED scene " +
                    "object. Ignoring, because demoting it to a runtime record would duplicate it on " +
                    "every load. Spawn a fresh instance instead of re-keying a placed one.", target);
                return entity;
            }

            // The prefab id from the spawn site wins: a prefab stamped under one GUID can still be
            // spawned through a path that knows a better key for it (an item's own registry ID).
            if (!string.IsNullOrEmpty(prefabId)) entity.prefabId = prefabId;

            entity.authored = false;

            if (string.IsNullOrEmpty(entity.instanceId))
            {
                // No isActiveAndEnabled gate: registration is scoped to the object's lifetime, and a
                // spawn that arrives disabled (a pooled object, a corpse) still owns its record.
                entity.instanceId = Guid.NewGuid().ToString("N");
                Live[entity.instanceId] = entity;
            }

            return entity;
        }

        /// <summary>
        /// An identity for an authored object that was never stamped at edit time, derived from
        /// where it sits rather than from a GUID.
        ///
        /// A random GUID cannot be used for these: it would be a different value every session, so
        /// the object would save state under an id nothing ever looks up again — persistence that
        /// silently does nothing. The scene name plus the hierarchy path, with each step's sibling
        /// index, is the same string on every load of an unchanged scene, which is exactly the
        /// property an identity needs.
        ///
        /// Stable across sessions, NOT across scene edits: renaming the object or moving it in the
        /// hierarchy produces a new id and orphans its record. That is the cost of not having to
        /// remember an editor step, and it is why the baked GUID is still preferred where one exists
        /// — see <see cref="SaveablePolicy"/>.
        /// </summary>
        public static string DeriveAuthoredId(GameObject go)
        {
            if (go == null) return string.Empty;

            var path = new System.Text.StringBuilder(go.scene.name);

            // Built root-first so the string reads like the hierarchy, and includes the sibling
            // index so two identically named children of one parent stay distinguishable.
            var steps = new List<string>();
            for (Transform t = go.transform; t != null; t = t.parent)
                steps.Add($"{t.GetSiblingIndex()}:{t.name}");

            for (int i = steps.Count - 1; i >= 0; i--)
                path.Append('/').Append(steps[i]);

            return "auto" + Hash(path.ToString());
        }

        /// <summary>
        /// FNV-1a, written out rather than taken from <c>string.GetHashCode</c>, which Unity does
        /// not guarantee to be stable between runs or platforms — and an identity that changes
        /// between runs is not an identity.
        /// </summary>
        private static string Hash(string value)
        {
            const ulong offset = 14695981039346656037;
            const ulong prime = 1099511628211;

            ulong hash = offset;
            foreach (char c in value)
            {
                hash ^= c;
                hash *= prime;
            }

            return hash.ToString("x16");
        }

        /// <summary>
        /// Takes on a derived identity as an authored object, for something wired at runtime rather
        /// than at edit time. Replaces the random id <see cref="OnEnable"/> just handed out.
        /// </summary>
        public void AdoptAuthoredIdentity(string derivedId)
        {
            if (string.IsNullOrEmpty(derivedId)) return;

            if (!string.IsNullOrEmpty(instanceId) &&
                Live.TryGetValue(instanceId, out SaveableEntity registered) && registered == this)
            {
                Live.Remove(instanceId);
            }

            instanceId = derivedId;
            authored = true;
            Live[instanceId] = this;
        }

        /// <summary>Adopts an identity from a save record, so the restored object owns the record it came from.</summary>
        public void AdoptIdentity(string savedPrefabId, string savedInstanceId)
        {
            if (!string.IsNullOrEmpty(instanceId) &&
                Live.TryGetValue(instanceId, out SaveableEntity registered) && registered == this)
            {
                Live.Remove(instanceId);
            }

            if (!string.IsNullOrEmpty(savedPrefabId)) prefabId = savedPrefabId;
            if (!string.IsNullOrEmpty(savedInstanceId)) instanceId = savedInstanceId;
            authored = false;

            if (!string.IsNullOrEmpty(instanceId)) Live[instanceId] = this;
        }

        /// <summary>
        /// The savers this entity speaks for: those on its own GameObject and its children, but not
        /// those under a nested <see cref="SaveableEntity"/>.
        ///
        /// The cut-off matters. A player carrying a backpack has a SaveableEntity on each; without
        /// it the backpack's contents would be captured twice — once under the player's record and
        /// once under its own — and the two copies would diverge the moment either is restored.
        /// </summary>
        public IReadOnlyList<ISaveable> Savers()
        {
            if (!saversGathered)
            {
                savers.Clear();
                Collect(transform, savers);
                saversGathered = true;
            }

            // Components can be added or destroyed after the first gather (a picked-up item's
            // saver, a destroyed child), so a stale entry is dropped rather than dereferenced.
            savers.RemoveAll(s => s is Component c && c == null);
            return savers;
        }

        /// <summary>Forces the next <see cref="Savers"/> call to re-scan. Call after adding a saver at runtime.</summary>
        public void InvalidateSavers() => saversGathered = false;

        private void Collect(Transform node, List<ISaveable> into)
        {
            foreach (ISaveable saver in node.GetComponents<ISaveable>())
                into.Add(saver);

            for (int i = 0; i < node.childCount; i++)
            {
                Transform child = node.GetChild(i);
                if (child.GetComponent<SaveableEntity>() != null) continue;
                Collect(child, into);
            }
        }

        /// <summary>Writes every saver's state into <paramref name="bag"/> under its own key.</summary>
        public void Capture(StateBag bag)
        {
            if (bag == null) return;

            foreach (ISaveable saver in Savers())
            {
                if (saver == null || string.IsNullOrEmpty(saver.SaveKey)) continue;

                try
                {
                    bag.Set(saver.SaveKey, saver.CaptureState());
                }
                catch (Exception e)
                {
                    // One misbehaving saver must not cost the player the other 200 objects in the
                    // chunk, so the failure is reported and the capture continues.
                    Debug.LogError($"[Save] '{name}' saver '{saver.SaveKey}' failed to capture: {e}", this);
                }
            }
        }

        /// <summary>
        /// Hands each saver the payload stored under its key. Keys with no saver are left untouched.
        ///
        /// <b>Every saver is called, including those whose key is absent — they are handed null.</b>
        /// Skipping them looked harmless and was not. Two things follow from a key being missing,
        /// and both need the saver to hear about it:
        ///
        ///   • "absent" is what <c>CaptureState</c> returning null writes (see <c>StateBag.Set</c>),
        ///     and it means the thing was at its defaults. A saver that is never called cannot
        ///     reset, so a restored object silently kept whatever the live component happened to
        ///     hold — a looted NPC came back with its authored inventory, a kinematic body kept its
        ///     current velocity;
        ///   • every deferred saver stages its pending work in <c>RestoreState</c> and clears it
        ///     there. <c>MountSaveable.pendingRider</c>, <c>AgentStateSaveable.hasPending</c> and
        ///     <c>OrnithopterSaveable.pendingFlying</c> were all cleared in the one method that was
        ///     not being called, so a craft flying at one save and grounded at the next was
        ///     re-launched into the air on load.
        ///
        /// So a saver's contract is now: <c>RestoreState(null)</c> means "you had nothing stored —
        /// go back to your default".
        /// </summary>
        public void Restore(StateBag bag)
        {
            if (bag == null) return;

            foreach (ISaveable saver in Savers())
            {
                if (saver == null || string.IsNullOrEmpty(saver.SaveKey)) continue;

                bag.TryGetRaw(saver.SaveKey, out JObject payload);

                try
                {
                    saver.RestoreState(payload);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Save] '{name}' saver '{saver.SaveKey}' failed to restore: {e}", this);
                }
            }
        }

        /// <summary>Runs the deferred pass for savers that need the world to have finished loading.</summary>
        public void NotifyLoadComplete()
        {
            // Collected and sorted rather than walked in component order. Component order is the
            // order somebody happened to add things to a prefab, and at least one saver already had
            // to work around depending on it — see IDeferredSaveable.LoadOrder. A deferred saver may
            // also mount, spawn or destroy, so the list is materialised before any of them runs.
            List<IDeferredSaveable> deferredSavers = null;

            foreach (ISaveable saver in Savers())
            {
                if (saver is IDeferredSaveable deferred)
                    (deferredSavers ??= new List<IDeferredSaveable>()).Add(deferred);
            }

            if (deferredSavers == null) return;

            if (deferredSavers.Count > 1)
                deferredSavers.Sort((a, b) => a.LoadOrder.CompareTo(b.LoadOrder));

            foreach (IDeferredSaveable deferred in deferredSavers)
            {
                try
                {
                    deferred.OnLoadComplete();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Save] '{name}' deferred saver failed: {e}", this);
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying) return;

            bool isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(this) || EditorUtility.IsPersistent(this);

            if (isPrefabAsset)
            {
                // On the asset itself: only the prefab id is meaningful. An instance id here would
                // be copied into every instance of the prefab, giving them all the same identity.
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(this));

                // Never blank a good id because the path could not be resolved.
                //
                // This unguarded assignment is why the field could not be written to disk at all.
                // OnValidate runs again during PrefabUtility.SavePrefabAsset, and in that pass
                // GetAssetPath returns empty — so AssetPathToGUID returns empty and this line wiped
                // the value a moment before it was serialized. Every attempt to stamp the field
                // reported success and left the file unchanged, which is exactly the behaviour that
                // made "the editor works, the build does not" so hard to see.
                if (!string.IsNullOrEmpty(guid)) AssignIfChanged(ref prefabId, guid);

                AssignIfChanged(ref instanceId, string.Empty);
                AssignIfChanged(ref authored, false);
                return;
            }

            if (gameObject.scene.IsValid() && !string.IsNullOrEmpty(gameObject.scene.path))
            {
                // Placed in a scene at edit time: authored. Its state is a delta on top of what the
                // scene file already contains, and it must never be re-instantiated on load.
                AssignIfChanged(ref authored, true);

                string prefabGuid = ResolveSourcePrefabGuid();
                if (!string.IsNullOrEmpty(prefabGuid)) AssignIfChanged(ref prefabId, prefabGuid);

                if (string.IsNullOrEmpty(instanceId) || IsIdTakenBySomeoneElseInScene())
                    AssignIfChanged(ref instanceId, Guid.NewGuid().ToString("N"));

                // On a PREFAB INSTANCE the assignments above are not enough. Writing the field and
                // calling SetDirty leaves the value matching the prefab's own, so Unity records no
                // override and writes nothing into the scene file — the identity is regenerated,
                // differently, every single time the scene is opened, and no saved record can ever
                // be matched back to the object it belongs to.
                //
                // Registering the values through SerializedObject is what makes them overrides, and
                // therefore what makes them survive in the scene at all.
                if (PrefabUtility.IsPartOfPrefabInstance(this))
                    RecordAsPrefabOverrides();
            }
        }

        /// <summary>
        /// Forces this instance's identity fields to be stored as prefab overrides, so they persist
        /// in the scene file rather than falling back to the prefab's (empty) values.
        /// </summary>
        private void RecordAsPrefabOverrides()
        {
            var so = new SerializedObject(this);

            SerializedProperty instanceProp = so.FindProperty(nameof(instanceId));
            SerializedProperty authoredProp = so.FindProperty(nameof(authored));
            SerializedProperty prefabProp = so.FindProperty(nameof(prefabId));

            if (instanceProp != null) instanceProp.stringValue = instanceId;
            if (authoredProp != null) authoredProp.boolValue = authored;
            if (prefabProp != null) prefabProp.stringValue = prefabId;

            // WithoutUndo: OnValidate can run during import and asset postprocessing, where pushing
            // entries onto the undo stack is both meaningless and expensive.
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The GUID of the prefab this scene instance came from, or empty for a plain GameObject.
        ///
        /// <b>Not <c>GetCorrespondingObjectFromOriginalSource</c>.</b> "Original source" walks the
        /// whole variant chain to its root, and in this project the root of that chain is usually an
        /// imported <c>.fbx</c> — <c>Golem.prefab</c> and <c>DuneRat.prefab</c> are both variants of
        /// their model prefabs. So every scene instance of them was stamped with the FBX's GUID,
        /// which <see cref="SaveablePrefabRegistry"/> can never resolve, while the asset branch of
        /// <see cref="OnValidate"/> stamped the same object with the real prefab GUID. One object,
        /// two disagreeing ids, depending on which branch happened to run.
        ///
        /// The nearest instance root is the right answer: it is the prefab a designer actually
        /// dragged in, and it is the asset the registry knows how to instantiate.
        /// </summary>
        private string ResolveSourcePrefabGuid()
        {
            // The asset path of the prefab this instance is an instance OF — the nearest one, not
            // the far end of its variant chain.
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);

            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return AssetDatabase.AssetPathToGUID(path);

            // Fall back to the corresponding source object, still preferring the nearest source over
            // the original one. An object whose nearest root is a model (.fbx) with no prefab in
            // between genuinely has no prefab to name, and empty is the honest answer — it will be
            // reported by the wiring validator rather than silently naming an unusable asset.
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
            if (source == null) return string.Empty;

            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath) ||
                !sourcePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return AssetDatabase.AssetPathToGUID(sourcePath);
        }

        /// <summary>
        /// Detects the copy-paste and duplicate-object case, where Unity hands the new object the
        /// original's serialized instanceId and two objects start claiming one save record.
        /// </summary>
        private bool IsIdTakenBySomeoneElseInScene()
        {
            foreach (SaveableEntity other in FindObjectsByType<SaveableEntity>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (other == this || other == null) continue;
                if (other.instanceId == instanceId) return true;
            }

            return false;
        }

        private void AssignIfChanged(ref string field, string value)
        {
            if (field == value) return;
            field = value;
            EditorUtility.SetDirty(this);
        }

        private void AssignIfChanged(ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            EditorUtility.SetDirty(this);
        }
#endif
    }
}
