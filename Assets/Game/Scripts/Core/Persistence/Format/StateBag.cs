using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SpaceGame.Persistence
{
    /// <summary>
    /// A namespaced pile of per-subsystem payloads: saver key -> that saver's own JSON object.
    ///
    /// Every <see cref="ISaveable"/> owns exactly one key and the entire shape underneath it. That
    /// is what makes this format survive refactors without a migration: adding a saver adds a key
    /// no old file has (and <see cref="TryGet{T}"/> reports absent), removing one leaves a key
    /// nothing reads. Neither can corrupt a neighbour, because no two savers share a namespace.
    ///
    /// Payloads are stored as <see cref="JObject"/> rather than as the concrete type. The bag is
    /// deserialized long before anyone knows which savers exist in this build — the concrete type
    /// may live in an assembly the format layer cannot see, or may no longer exist at all — so the
    /// conversion is deferred to the moment a saver asks for its own key.
    /// </summary>
    public class StateBag
    {
        [JsonProperty("entries")]
        private Dictionary<string, JObject> entries;

        /// A file written before this field existed — or one with an explicit "entries": null —
        /// deserializes the field back as null, so nothing may touch it directly.
        private Dictionary<string, JObject> Entries => entries ??= new Dictionary<string, JObject>();

        // Newtonsoft serializes public getters by default, and a derived view of `entries` written
        // alongside `entries` is both dead weight in the file and a second source of truth.
        [JsonIgnore] public int Count => Entries.Count;

        [JsonIgnore] public IEnumerable<string> Keys => Entries.Keys;

        public bool Has(string key) => key != null && Entries.ContainsKey(key);

        /// <summary>
        /// Stores <paramref name="payload"/> under <paramref name="key"/>, replacing anything there.
        /// A null payload clears the key rather than writing a JSON null, so "this saver produced
        /// nothing" and "this saver was never present" read identically on the way back in.
        /// </summary>
        public void Set(string key, object payload, JsonSerializer serializer = null)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (payload == null)
            {
                Entries.Remove(key);
                return;
            }

            if (payload is JObject already)
            {
                Entries[key] = already;
                return;
            }

            try
            {
                Entries[key] = JObject.FromObject(payload, serializer ?? SaveSerializer.Serializer);
            }
            catch (Exception e)
            {
                // A payload that is not an object — a bare list, an int, a string — cannot be a state
                // bag entry, because the bag is a map of key to object. Newtonsoft says so by
                // throwing, and an unhandled throw here takes down the capture of the ENTIRE entity,
                // and with it every other saver on it. One saver returning the wrong shape is not
                // worth the other twenty.
                //
                // Dropping the key is the same outcome the saver would have got from returning null,
                // and the error names the culprit so it can be fixed rather than merely survived.
                Entries.Remove(key);

                Debug.LogError($"[Save] Saver '{key}' returned a payload that is not an object " +
                               $"({payload.GetType().Name}), so it was dropped: {e.Message}. " +
                               "CaptureState must return a struct or class with fields — wrap a bare " +
                               "collection in one.");
            }
        }

        public bool TryGetRaw(string key, out JObject payload)
        {
            payload = null;
            return key != null && Entries.TryGetValue(key, out payload) && payload != null;
        }

        /// <summary>
        /// Reads a key back as <typeparamref name="T"/>. Returns false — rather than throwing — when
        /// the key is absent or the stored shape no longer converts, because a save file written by
        /// an older build is an expected input, not an error. Callers keep their current state when
        /// this returns false.
        /// </summary>
        public bool TryGet<T>(string key, out T value, JsonSerializer serializer = null)
        {
            value = default;
            if (!TryGetRaw(key, out JObject raw)) return false;

            try
            {
                value = raw.ToObject<T>(serializer ?? SaveSerializer.Serializer);

                // True because the key was THERE and the payload parsed — not because the result
                // happens to be non-null. `return value != null` made a stored value that legitimately
                // deserializes to null or to default(T) indistinguishable from a key that was never
                // written, which is exactly the distinction this method exists to report.
                return true;
            }
            catch (JsonException)
            {
                value = default;
                return false;
            }
        }

        public void Remove(string key)
        {
            if (key != null) Entries.Remove(key);
        }

        /// <summary>Copies every entry from <paramref name="other"/>, overwriting shared keys.</summary>
        public void MergeFrom(StateBag other)
        {
            if (other == null) return;
            foreach (KeyValuePair<string, JObject> entry in other.Entries)
                Entries[entry.Key] = entry.Value;
        }
    }
}
