using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

            Entries[key] = payload is JObject already
                ? already
                : JObject.FromObject(payload, serializer ?? SaveSerializer.Serializer);
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
                return value != null;
            }
            catch (JsonException)
            {
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
