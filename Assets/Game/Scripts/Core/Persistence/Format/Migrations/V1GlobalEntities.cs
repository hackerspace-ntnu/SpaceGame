using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace SpaceGame.Persistence
{
    /// <summary>
    /// v1 → v2: lifts the world's per-scene records into one global, id-keyed entity registry.
    ///
    /// v1 stored <c>world.scenes["chunk:7,5"].authored["&lt;id&gt;"] = StateBag</c> and a parallel
    /// <c>entities</c> list per scene. That address was wrong: an object's scene changes while the
    /// game runs (see <see cref="WorldRecord.Entities"/>), so a record filed under the scene an
    /// object wandered into could never be found again from the scene it was authored in.
    ///
    /// The lift is faithful and lossless — every v1 record becomes a v2 record with the same id, the
    /// same state, and its former scene key preserved in <c>scene</c>. Position and rotation only
    /// existed on v1 runtime entities; an authored record carries whatever its
    /// <c>TransformSaveable</c> wrote, which the store reads back out of the state bag as before, so
    /// nothing is invented here.
    /// </summary>
    public class V1GlobalEntities : ISaveMigration
    {
        public int FromVersion => 1;

        public void Apply(JObject root)
        {
            if (root["world"] is not JObject world) return;

            var entities = new JObject();
            var destroyed = new HashSet<string>();

            if (world["scenes"] is JObject scenes)
            {
                foreach (JProperty scene in scenes.Properties())
                {
                    if (scene.Value is not JObject record) continue;

                    LiftRuntime(record["entities"] as JArray, scene.Name, entities);
                    LiftAuthored(record["authored"] as JObject, scene.Name, entities);
                    LiftDestroyed(record["destroyedAuthored"] as JArray, destroyed);
                }
            }

            world.Remove("scenes");
            world["entities"] = entities;
            world["destroyed"] = new JArray(destroyed);
        }

        private static void LiftRuntime(JArray runtime, string sceneKey, JObject into)
        {
            if (runtime == null) return;

            foreach (JToken token in runtime)
            {
                if (token is not JObject entity) continue;

                string id = (string)entity["instanceId"];
                if (string.IsNullOrEmpty(id)) continue;

                entity["scene"] = sceneKey;
                entity["authored"] = false;

                // v1 wrote the same runtime object once per session it survived, so a file can hold
                // several records for one object under DIFFERENT ids. Keying by id here collapses
                // only the exact-id repeats; the rest are indistinguishable from real objects and
                // are carried across rather than guessed at.
                into[id] = entity;
            }
        }

        /// <summary>
        /// A v1 authored record was a bare state bag — no pose of its own, because an authored object
        /// was only ever a delta and its position, if it had one at all, lived inside its
        /// TransformSaveable payload. v2 puts the pose on the record so that position persists for
        /// every object rather than only for the ones somebody gave a transform saver, so the pose is
        /// lifted out of that payload here where there is one to lift.
        ///
        /// Where there is not, <c>hasPose</c> says so. A missing pose defaulting to zero would look
        /// exactly like an object that belongs at the origin, and the store would dutifully teleport
        /// it there on the first load after the update.
        /// </summary>
        private static void LiftAuthored(JObject authored, string sceneKey, JObject into)
        {
            if (authored == null) return;

            foreach (JProperty entry in authored.Properties())
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var record = new JObject
                {
                    ["instanceId"] = entry.Name,
                    ["prefabId"] = string.Empty,
                    ["scene"] = sceneKey,
                    ["authored"] = true,
                    ["state"] = entry.Value,
                };

                JToken pose = entry.Value?["entries"]?["transform"];

                if (pose == null)
                {
                    record["hasPose"] = false;
                }
                else
                {
                    if (pose["position"] != null) record["position"] = pose["position"].DeepClone();
                    if (pose["rotation"] != null) record["rotation"] = pose["rotation"].DeepClone();
                    if (pose["scale"] != null) record["scale"] = pose["scale"].DeepClone();
                }

                into[entry.Name] = record;
            }
        }

        private static void LiftDestroyed(JArray tombstones, HashSet<string> into)
        {
            if (tombstones == null) return;

            foreach (JToken id in tombstones)
            {
                string value = (string)id;
                if (!string.IsNullOrEmpty(value)) into.Add(value);
            }
        }
    }
}
