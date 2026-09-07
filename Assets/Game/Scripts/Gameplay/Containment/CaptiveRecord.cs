// What is actually inside the bottle.
//
// A captive is a SAVED RECORD, never a disabled GameObject. Keeping the body alive but hidden
// would leave it in every physics query, every targeting sweep and every save — disabling a
// collider removes a body from contacts and from nothing else, which is the trap the NPC rider work
// already paid for once. So a capture serialises the entity to exactly the record the world save
// already writes for it, and despawns the object properly.
//
// The record is a RECIPE, not an identity claim, and that is a deliberate difference from an
// EntityRecord sitting in a save file. See CaptiveRecord.Entity below for why.
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Gameplay.Containment
{
    /// <summary>
    /// One captive, in the form that survives being carried, dropped, saved and reloaded.
    ///
    /// <para>
    /// <b>Why an <see cref="EntityRecord"/> rather than a shape of its own.</b> The world save
    /// already knows how to turn a creature into data and back — prefab id, pose, scale and one
    /// <see cref="StateBag"/> per saver, covering health, faction, inventory, agent mind, gait and
    /// everything else a subsystem has ever taught it. Inventing a second, thinner shape here
    /// would mean a captive comes back at prefab defaults for every saver nobody remembered, and
    /// it would drift the moment a new saver is added. This carries the whole record.
    /// </para>
    /// <para>
    /// <b>The record does NOT keep the captive's instance id.</b> A record and its identity mean
    /// two different things to <c>WorldSaveStore</c>: an id names a record the store owns, and a
    /// captive's record is owned by the item instead. Keeping the original id would put the two in
    /// direct contradiction — capturing an AUTHORED creature tombstones it (that is what
    /// <c>GameServices.World.Despawn</c> does, and it is right: it must not come back at its
    /// authored spot), and <c>WorldSaveStore.Compact</c> deletes any record whose id is also
    /// tombstoned, so a released creature carrying its old id would be silently erased on the next
    /// save. Releasing mints a fresh runtime identity instead, which is what
    /// <c>SaveableEntity.EnsureRuntime</c> does for every other runtime spawn in the game.
    /// </para>
    /// <para>
    /// The consequence, stated rather than hidden: a <c>SaveRef</c> held by something else and
    /// pointing at the captive does not survive the trip. Every such reference in the project
    /// (a mount's rider, a leash's end) resolves in a deferred pass that keeps its pending value on
    /// failure rather than consuming it, so the failure mode is "not re-established", not "throws".
    /// The one relationship that must survive is a rider, and that is carried here explicitly by
    /// <see cref="Carried"/>.
    /// </para>
    /// </summary>
    public sealed class CaptiveRecord
    {
        /// <summary>
        /// What to show the player on the glass and in a tooltip. The captive's object name at the
        /// moment it went in, which for every creature prefab in this project is its species.
        /// </summary>
        [JsonProperty("name")] public string DisplayName = string.Empty;

        /// <summary>The whole of the captive, in the world save's own shape. See the class summary.</summary>
        [JsonProperty("entity")] public EntityRecord Entity = new();

        /// <summary>
        /// Anything that went in ON the captive and must come out on it again — today, an NPC
        /// rider in a saddle.
        ///
        /// <para>
        /// A list rather than a single field because a mount is not the only thing that can carry
        /// somebody, and because "nobody" is then the empty list rather than a null nobody checks.
        /// Nested rather than flat so the pairing survives: the whole point is that a rider and
        /// their mount are one capture or no capture, never separated silently.
        /// </para>
        /// </summary>
        [JsonProperty("carried")] public List<CaptiveRecord> Carried = new();

        /// <summary>Does this record name a prefab that could ever be rebuilt?</summary>
        public bool NamesAPrefab => Entity != null && !string.IsNullOrEmpty(Entity.PrefabId);

        /// <summary>
        /// The record as one string, for the item's <c>ItemState</c> bag.
        ///
        /// <para>
        /// Through <see cref="SaveSerializer"/>'s own serializer and no other: a
        /// <see cref="StateBag"/> payload written with the Unity converters registered and read
        /// back without them round-trips a Vector3 into a stack overflow, which is the single
        /// trap every hand-rolled reader in this project has fallen into.
        /// </para>
        /// </summary>
        public static string Encode(CaptiveRecord record)
        {
            if (record == null) return null;

            JsonSerializerSettings settings = SaveSerializer.Settings;

            // Compact rather than indented. This string is a VALUE inside another JSON document
            // (the slot's ItemState bag, itself inside the save file), so every newline in it is
            // escaped and paid for twice.
            settings.Formatting = Formatting.None;

            return JsonConvert.SerializeObject(record, settings);
        }

        /// <summary>
        /// Read a record back. Answers false — loudly — for anything that is not one.
        ///
        /// <para>
        /// <b>The caller must keep the raw string on failure.</b> A record that cannot be parsed
        /// by this build is still the only copy of a creature that exists anywhere, and throwing it
        /// away is how a creature disappears with nothing in the console. Every caller here holds
        /// the encoded form as its truth and decodes on demand for exactly that reason.
        /// </para>
        /// </summary>
        public static bool TryDecode(string encoded, out CaptiveRecord record)
        {
            record = null;
            if (string.IsNullOrEmpty(encoded)) return false;

            try
            {
                record = JsonConvert.DeserializeObject<CaptiveRecord>(encoded, SaveSerializer.Settings);
            }
            catch (JsonException e)
            {
                // Loud, and it has to be. This is a captive that went into a container and cannot
                // be got back out, which is a creature the player owns and can no longer reach.
                Debug.LogError("[Containment] A captive record could not be read back, so whatever " +
                               $"is in this container cannot be released: {e.Message}. The record " +
                               "is being kept rather than dropped, so it can still be recovered by " +
                               "a build that understands it.");
                return false;
            }

            if (record == null) return false;

            record.Entity ??= new EntityRecord();
            record.Carried ??= new List<CaptiveRecord>();
            record.Entity.EnsureState();

            foreach (CaptiveRecord carried in record.Carried)
            {
                carried.Entity ??= new EntityRecord();
                carried.Carried ??= new List<CaptiveRecord>();
                carried.Entity.EnsureState();
            }

            return true;
        }

        /// <summary>
        /// A short description of what a container holds, for a tooltip or a log line. Never the
        /// empty string: a captive nobody named still has to read as something.
        /// </summary>
        public string Describe()
        {
            if (!string.IsNullOrEmpty(DisplayName)) return DisplayName;

            return NamesAPrefab ? Entity.PrefabId : "something";
        }
    }
}
