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

        private void Awake()
        {
            if (string.IsNullOrEmpty(instanceId))
            {
                // A runtime object that nobody stamped — a prefab instantiated directly rather than
                // through the save system. Give it an identity anyway so its state is not silently
                // dropped from every save for the rest of the session.
                instanceId = Guid.NewGuid().ToString("N");
            }

            if (Live.TryGetValue(instanceId, out SaveableEntity existing) && existing != null && existing != this)
            {
                Debug.LogWarning(
                    $"[Save] '{name}' and '{existing.name}' share instance id {instanceId}. " +
                    "One was likely duplicated in a scene without OnValidate running. Reassigning.",
                    this);

                instanceId = Guid.NewGuid().ToString("N");
            }

            Live[instanceId] = this;
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

        /// <summary>Hands each saver the payload stored under its key. Keys with no saver are left untouched.</summary>
        public void Restore(StateBag bag)
        {
            if (bag == null) return;

            foreach (ISaveable saver in Savers())
            {
                if (saver == null || string.IsNullOrEmpty(saver.SaveKey)) continue;
                if (!bag.TryGetRaw(saver.SaveKey, out JObject payload)) continue;

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
            foreach (ISaveable saver in Savers())
            {
                if (saver is not IDeferredSaveable deferred) continue;

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
                AssignIfChanged(ref prefabId, guid);
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

        /// <summary>The GUID of the prefab this scene instance came from, or empty for a plain GameObject.</summary>
        private string ResolveSourcePrefabGuid()
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(gameObject);
            if (source == null) return string.Empty;

            return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
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
