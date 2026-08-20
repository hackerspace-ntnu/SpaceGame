using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace SpaceGame.Persistence
{
    /// <summary>
    /// The one JSON configuration the save system uses, and the only place a save document turns
    /// into text or back.
    ///
    /// Sharing a single <see cref="JsonSerializer"/> matters: a <see cref="StateBag"/> payload
    /// written with the Unity converters registered and read back without them would round-trip a
    /// Vector3 into a stack overflow. Nothing in the save system should construct its own.
    /// </summary>
    public static class SaveSerializer
    {
        /// <summary>Settings, exposed so callers that need their own serializer stay consistent.</summary>
        public static JsonSerializerSettings Settings => BuildSettings();

        /// <summary>The shared serializer. Newtonsoft's <see cref="JsonSerializer"/> is thread-safe for reads.</summary>
        public static readonly JsonSerializer Serializer = JsonSerializer.Create(BuildSettings());

        private static JsonSerializerSettings BuildSettings() => new()
        {
            // Fields, not properties: the DTOs above are plain public fields, and a MonoBehaviour's
            // payload struct usually is too. Auto-properties would otherwise be missed entirely.
            ContractResolver = new DefaultContractResolver
            {
                IgnoreSerializableAttribute = true,
            },

            Converters =
            {
                new Vector2Converter(),
                new Vector3Converter(),
                new Vector2IntConverter(),
                new QuaternionConverter(),
                new ColorConverter(),
            },

            // Round-trips DateTime as UTC rather than shifting it into the reader's local zone,
            // so a save written in Oslo does not claim to be from the future in Los Angeles.
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,

            // A field the running build no longer knows about is an expected input from an older
            // or newer save, not a reason to fail the load.
            MissingMemberHandling = MissingMemberHandling.Ignore,

            // Deserializing into a fresh object rather than merging into the field's initializer.
            // Without this, a list that starts non-empty in the DTO gets the file's entries
            // APPENDED to the defaults instead of replacing them.
            ObjectCreationHandling = ObjectCreationHandling.Replace,

            // No $type in the file. Type names in a save file are a deserialization gadget and
            // break on every namespace move; StateBag defers typing to the reader instead.
            TypeNameHandling = TypeNameHandling.None,

            NullValueHandling = NullValueHandling.Ignore,
        };

        public static string ToJson(SaveDocument document, bool pretty = true)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            JsonSerializerSettings settings = BuildSettings();
            settings.Formatting = pretty ? Formatting.Indented : Formatting.None;

            return JsonConvert.SerializeObject(document, settings);
        }

        /// <summary>
        /// Parses text into a document, running any needed migrations first.
        /// Throws <see cref="SaveFormatException"/> for anything a caller cannot recover from —
        /// malformed JSON, or a file written by a newer build than this one.
        /// </summary>
        public static SaveDocument FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new SaveFormatException("Save file is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new SaveFormatException($"Save file is not valid JSON: {e.Message}", e);
            }

            SaveMigrator.Migrate(root);

            try
            {
                SaveDocument document = root.ToObject<SaveDocument>(Serializer);
                if (document == null) throw new SaveFormatException("Save file parsed to nothing.");
                return document.Normalized();
            }
            catch (JsonException e)
            {
                throw new SaveFormatException($"Save file could not be read: {e.Message}", e);
            }
        }

        /// <summary>
        /// The header alone, for a slot listing. Deliberately does not migrate or parse the body:
        /// a save from a newer build, or one whose world section is corrupt, must still produce a
        /// row in the load menu that says so.
        /// </summary>
        public static bool TryReadHeader(string json, out SaveHeader header)
        {
            header = null;
            if (string.IsNullOrWhiteSpace(json)) return false;

            try
            {
                JToken headerToken = JObject.Parse(json)["header"];
                if (headerToken == null) return false;

                header = headerToken.ToObject<SaveHeader>(Serializer);
                return header != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>A save file that exists but cannot be turned into a usable document.</summary>
    public class SaveFormatException : Exception
    {
        public SaveFormatException(string message) : base(message) { }
        public SaveFormatException(string message, Exception inner) : base(message, inner) { }
    }
}
