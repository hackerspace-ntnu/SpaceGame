// A reusable harness for asking "does this prefab actually persist?".
//
// Persistence fails silently by construction: a prefab that is not opted in produces no error, no
// warning and no failing test — the game runs, saves are written, and the object simply is not in
// them. That is how every mount and vehicle in this project went unsaved for months. A test is the
// only place that class of bug can be caught, and a test nobody can write cheaply is a test nobody
// writes.
//
// So this file exists to make the per-prefab test three lines. See PrefabPersistenceTests.cs for
// worked examples, and docs/AI/systems/Persistence.md (Flows, Gotchas) for when to reach for which method.
//
// ── The two things worth asserting ────────────────────────────────────────────────────────────
//
//   AssertWiredCorrectly()      structural. Is this prefab opted in, does it carry the savers its
//                               own components imply, and are their keys unique?
//
//   AssertSurvivesRoundTrip()   behavioural. Capture the state, put it through real JSON TEXT, and
//                               restore it onto a DIFFERENT instance — then capture again and
//                               require the two to agree.
//
// The second is the strong one, and both details in it are load-bearing. Real text, because the
// Unity converters live on SaveSerializer.Serializer and a saver that skips them round-trips fine
// as objects and stack-overflows as JSON. A different instance, because restoring onto the object
// you captured from passes even when the saver restores nothing at all.
//
// ── The expectation oracle ────────────────────────────────────────────────────────────────────
//
// AssertWiredCorrectly does NOT compare against a hand-written list of savers. It runs the real
// SaveablePolicy.Ensure on a throwaway copy and asks what it ADDED — anything it had to add is
// something the prefab was missing. So the day somebody adds a new saver to the policy, every
// prefab test starts checking for it without a single test being edited.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;

namespace SpaceGame.EditorTools
{
    public sealed class PersistenceProbe
    {
        /// <summary>Where the sweep looks for world-entity prefabs.</summary>
        public const string PrefabRoot = "Assets/Game/Prefabs";

        private readonly GameObject prefab;
        private readonly string path;
        private readonly HashSet<Type> excluded = new();
        private Action<GameObject> mutate;

        private PersistenceProbe(GameObject prefab, string path)
        {
            this.prefab = prefab;
            this.path = path;
        }

        // ─────────────────────────────────────────────
        //  Building a probe
        // ─────────────────────────────────────────────

        /// <summary>
        /// Probes the prefab at an asset path. Fails the test immediately if it is not there, rather
        /// than passing vacuously — a typo'd path that silently tested nothing is worse than no test.
        /// </summary>
        public static PersistenceProbe For(string assetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

            Assert.IsNotNull(asset,
                $"No prefab at '{assetPath}'. A persistence test that cannot find its subject " +
                "passes without checking anything, so this is a failure rather than a skip.");

            return new PersistenceProbe(asset, assetPath);
        }

        /// <summary>
        /// Leaves one saver out of the round trip.
        ///
        /// For savers that genuinely cannot work outside play mode, not for ones that are merely
        /// failing. The honest cases are savers that depend on state built in <c>Awake</c> or on a
        /// runtime registry — <see cref="EntityInventorySaveable"/> needs the item registry, which
        /// <c>RegistryLoader</c> only fills at runtime. Excluding a saver because its round trip fails
        /// is hiding the bug this harness exists to find.
        /// </summary>
        public PersistenceProbe Excluding<T>() where T : ISaveable
        {
            excluded.Add(typeof(T));
            return this;
        }

        /// <summary>
        /// Puts the instance into a state that is not the prefab's default, before it is captured.
        ///
        /// Without this the round trip is close to meaningless: a saver that restores nothing still
        /// agrees with itself when everything is already at its default value. Change the things a
        /// player changes — damage it, open a hatch, sheet a sail in, move it.
        /// </summary>
        public PersistenceProbe Mutate(Action<GameObject> change)
        {
            mutate = change;
            return this;
        }

        // ─────────────────────────────────────────────
        //  Assertions
        // ─────────────────────────────────────────────

        /// <summary>
        /// Asserts the prefab is opted in, carries every saver its components imply, and that no two
        /// of those savers claim the same key.
        /// </summary>
        public void AssertWiredCorrectly()
        {
            Assert.IsTrue(SaveablePolicy.NeedsSaving(prefab, out string why),
                $"'{path}' is not opted in to saving at all, so nothing about it survives a " +
                "reload. Give one of its root components SpaceGame.Persistence.IPersistentEntity — " +
                "see docs/AI/systems/Persistence.md (Model).");

            Assert.IsNotNull(prefab.GetComponent<SaveableEntity>(),
                $"'{path}' qualifies for saving ({why}) but has no SaveableEntity, so it can " +
                "only ever get a hierarchy-path identity assigned at runtime — which is orphaned the " +
                "moment anyone renames or re-parents it. Run Tools ▸ Save System ▸ Wire Saveable Prefabs.");

            AssertNoSaversMissing();
            AssertSaveKeysAreUnique();
        }

        /// <summary>
        /// Captures the state, runs it through real JSON text, restores it onto a fresh instance, and
        /// requires a second capture to agree with the first.
        ///
        /// This is a fixpoint test, which is what makes it generic: it needs to know nothing about
        /// what any saver means. A field that is captured but never restored, a converter that is
        /// missing, a key that is written under one name and read under another — all three show up as
        /// the two captures disagreeing, and the failure message names the key.
        /// </summary>
        public void AssertSurvivesRoundTrip()
        {
            GameObject source = null;
            GameObject target = null;

            try
            {
                source = Instantiate();
                mutate?.Invoke(source);

                StateBag captured = Capture(source);

                Assert.Greater(captured.Count, 0,
                    $"'{path}' captured nothing at all. Either it has no ISaveable components " +
                    "or every one of them returned null, which means a reload restores only its pose.");

                // Through text, not just through objects. This is the step that catches a saver
                // reading its payload without SaveSerializer.Serializer: Vector3 and Quaternion
                // recurse through their own properties and die here rather than in front of a player.
                StateBag reread = ThroughJsonText(captured);

                // A DIFFERENT instance. Restoring onto `source` would pass for a saver whose
                // RestoreState is empty, which is exactly the bug most worth catching.
                target = Instantiate();
                Restore(target, reread);

                StateBag recaptured = Capture(target);

                AssertBagsAgree(captured, recaptured);
            }
            finally
            {
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
            }
        }

        // ─────────────────────────────────────────────
        //  The project-wide sweep
        // ─────────────────────────────────────────────

        /// <summary>
        /// Asserts that EVERY prefab in the project which qualifies as a world entity carries a
        /// SaveableEntity.
        ///
        /// <b>This is the test that makes per-prefab tests optional.</b> A new creature, mount or
        /// vehicle is covered the moment it exists, with nobody having remembered anything — which is
        /// the only kind of coverage that survives contact with a real project. The bug it guards
        /// against is not hypothetical: the Ostrich, the DuneFoil, the DesertCrawler and the RigWalker
        /// all sat in persistentScene unwired, and nothing anywhere said so.
        ///
        /// Reports every offender at once rather than failing on the first, because the fix is a
        /// single run of the wiring tool and you want to know the whole list before you run it.
        /// </summary>
        public static void AssertEveryWorldEntityPrefabIsWired()
        {
            var unwired = new List<string>();

            foreach ((GameObject asset, string assetPath) in WorldEntityPrefabs())
            {
                if (asset.GetComponent<SaveableEntity>() == null)
                    unwired.Add(assetPath);
            }

            if (unwired.Count == 0) return;

            Assert.Fail(
                $"{unwired.Count} world-entity prefab(s) have no SaveableEntity, so they persist only " +
                "through the runtime fallback's hierarchy-path identity — which is orphaned by any " +
                "rename or re-parent:\n  " + string.Join("\n  ", unwired) +
                "\n\nFix: Tools ▸ Save System ▸ Wire Saveable Prefabs, then re-save any scene that " +
                "instances them so the identity overrides are written (see Persistence.md, Gotchas).");
        }

        /// <summary>
        /// Asserts that every already-wired world-entity prefab carries the savers its own components
        /// imply — so a prefab that gained a MountModule after it was wired does not quietly stop
        /// saving its rider.
        /// </summary>
        public static void AssertEveryWiredPrefabHasItsSavers()
        {
            var incomplete = new List<string>();

            foreach ((GameObject asset, string assetPath) in WorldEntityPrefabs())
            {
                if (asset.GetComponent<SaveableEntity>() == null) continue;   // reported by the sweep above

                string missing = MissingSaversOn(asset);
                if (!string.IsNullOrEmpty(missing)) incomplete.Add($"{assetPath} — missing {missing}");
            }

            if (incomplete.Count == 0) return;

            Assert.Fail(
                $"{incomplete.Count} prefab(s) are missing savers their components call for. Each one " +
                "loses exactly the state that saver owns, silently:\n  " +
                string.Join("\n  ", incomplete) +
                "\n\nFix: Tools ▸ Save System ▸ Wire Saveable Prefabs.");
        }

        /// <summary>
        /// Every prefab under <see cref="PrefabRoot"/> that the save policy considers a world entity.
        ///
        /// Driven by <c>SaveablePolicy</c> rather than by a folder convention or a name list, so the
        /// sweep's idea of "world entity" cannot drift from the runtime's.
        /// </summary>
        public static IEnumerable<(GameObject asset, string path)> WorldEntityPrefabs()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { PrefabRoot }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

                if (asset == null) continue;
                if (asset.GetComponent<IPersistentEntity>() == null) continue;

                // NeedsSaving also steps around the player, which is owned by PlayerSaveService and
                // deliberately carries no world identity.
                if (!SaveablePolicy.NeedsSaving(asset, out _)) continue;

                yield return (asset, assetPath);
            }
        }

        // ─────────────────────────────────────────────
        //  Internals
        // ─────────────────────────────────────────────

        private void AssertNoSaversMissing()
        {
            string missing = MissingSaversOn(prefab);

            Assert.IsEmpty(missing ?? string.Empty,
                $"'{path}' is missing {missing}. Its components call for those savers, so the " +
                "state each one owns is silently lost on every reload. Run " +
                "Tools ▸ Save System ▸ Wire Saveable Prefabs.");
        }

        /// <summary>
        /// What <c>SaveablePolicy.Ensure</c> would have to add to this prefab, as a readable list.
        ///
        /// The expectation is derived by running the real policy on a throwaway copy, never from a
        /// list written here — a hand-kept list is a second source of truth that goes stale the first
        /// time a saver is added, and goes stale silently.
        /// </summary>
        private static string MissingSaversOn(GameObject asset)
        {
            // On a COPY. Ensure adds components, and running it on the asset would edit the prefab on
            // disk as a side effect of running the test suite.
            GameObject copy = UnityEngine.Object.Instantiate(asset);

            try
            {
                return SaveablePolicy.Ensure(copy, out string added) ? added : null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        private void AssertSaveKeysAreUnique()
        {
            GameObject copy = Instantiate();

            try
            {
                var keys = new List<string>();

                foreach (ISaveable saver in EntityOn(copy).Savers())
                    if (saver != null && !string.IsNullOrEmpty(saver.SaveKey)) keys.Add(saver.SaveKey);

                // Two savers sharing a key means the second overwrites the first in the state bag and
                // only one of them ever restores — with nothing logged either way.
                CollectionAssert.AllItemsAreUnique(keys,
                    $"Two savers on '{path}' claim the same key. A key is a namespace within " +
                    "the entity's state bag, so the loser is silently dropped.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        private GameObject Instantiate()
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab);

            // Named for the failure message. An unnamed "Ostrich(Clone)(Clone)" in an assert tells you
            // nothing about which half of the round trip you are looking at.
            instance.name = prefab.name;
            return instance;
        }

        /// <summary>
        /// The entity to capture through, wiring one on if the prefab has none.
        ///
        /// Wired rather than refused so a round-trip test is useful BEFORE the editor wiring tool has
        /// been run — the runtime does exactly this in <c>SaveablePolicy.EnsureScene</c>, so the test
        /// is still exercising a real path. Whether the prefab should have been wired at edit time is
        /// <see cref="AssertWiredCorrectly"/>'s question, not this one's.
        /// </summary>
        private static SaveableEntity EntityOn(GameObject instance)
        {
            SaveablePolicy.Ensure(instance, out _);
            return instance.GetComponent<SaveableEntity>();
        }

        private StateBag Capture(GameObject instance)
        {
            var bag = new StateBag();
            EntityOn(instance).Capture(bag);

            foreach (string key in ExcludedKeysOn(instance)) bag.Remove(key);

            return bag;
        }

        private void Restore(GameObject instance, StateBag bag)
        {
            EntityOn(instance).Restore(bag);

            // The deferred half, for savers that hold a reference until the world exists. Without it a
            // MountSaveable or an OrnithopterSaveable would look like it restored nothing.
            EntityOn(instance).NotifyLoadComplete();
        }

        private IEnumerable<string> ExcludedKeysOn(GameObject instance)
        {
            foreach (Type type in excluded)
            {
                if (instance.GetComponent(type) is ISaveable saver && !string.IsNullOrEmpty(saver.SaveKey))
                    yield return saver.SaveKey;
            }
        }

        /// <summary>
        /// Serializes a bag to text and reads it back, exactly as a save file does.
        ///
        /// Through <c>SaveSerializer.Serializer</c> and through a real string, because those are the
        /// two things that differ from passing objects around in memory — and both of them are where
        /// Unity structs go wrong.
        /// </summary>
        private static StateBag ThroughJsonText(StateBag bag)
        {
            string json = JObject.FromObject(bag, SaveSerializer.Serializer).ToString();

            return JObject.Parse(json).ToObject<StateBag>(SaveSerializer.Serializer);
        }

        private void AssertBagsAgree(StateBag captured, StateBag recaptured)
        {
            // Both directions. A saver that produced nothing before the round trip and something after
            // it has state that appears out of a load, which is as wrong as state that disappears into
            // one — and only the reverse check sees it.
            CollectionAssert.AreEquivalent(captured.Keys.ToList(), recaptured.Keys.ToList(),
                $"'{path}' saved a different set of keys than a restored copy produces. A key only on " +
                "the left was lost by the load; a key only on the right was invented by it.");

            foreach (string key in captured.Keys.ToList())
            {
                Assert.IsTrue(recaptured.TryGetRaw(key, out JObject after),
                    $"'{path}' captured '{key}' but a restored copy captured nothing under it. " +
                    "That saver's RestoreState is not putting the state back.");

                captured.TryGetRaw(key, out JObject before);

                // JToken.DeepEquals rather than string comparison: property order is not part of the
                // payload's meaning, and Newtonsoft does not promise to preserve it.
                Assert.IsTrue(JToken.DeepEquals(before, after),
                    $"'{path}' did not survive a save/load round trip under the key '{key}'.\n" +
                    $"  captured:   {before}\n" +
                    $"  after load: {after}\n" +
                    "Either RestoreState is dropping a field CaptureState wrote, or it is reading the " +
                    "payload without SaveSerializer.Serializer. See Persistence.md, Persistence section.");
            }
        }
    }
}
