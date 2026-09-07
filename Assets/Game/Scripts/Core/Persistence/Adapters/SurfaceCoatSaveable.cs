using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay.Surface;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the coats that outlive a session, which is <see cref="SurfaceCoatKind.Ice"/> and
    /// nothing else.
    ///
    /// <para>
    /// <b>Slick and Wet are deliberately not saved.</b> Both are gone inside half a minute, and a
    /// quicksave taken while crossing a slick pool that loaded the pool back would hand the player
    /// a hazard they had already walked past — the same call <c>StatusEffects</c> makes about a
    /// body that is on fire. Ice is the exception because a frozen pool somebody has made a bridge
    /// of is a change to the world rather than a moment in it.
    /// </para>
    /// <para>
    /// A global saver rather than something on a <see cref="SaveableEntity"/>, for the reason
    /// <c>SandstormSaveable</c> is one: a coat is a handful of floats on a session-wide list, not
    /// an object in a chunk, and the list has to be written even when no chunk is loaded. Which
    /// chunk a coat belongs to still decides its LIFETIME — see
    /// <c>SurfaceCoatField.OnChunkWillUnload</c> — it just does not decide where the record lives.
    /// </para>
    /// <para>
    /// Place it on the same GameObject as the <see cref="SurfaceCoatField"/>, which that class's
    /// <c>RequireComponent</c> arranges.
    /// </para>
    /// </summary>
    public class SurfaceCoatSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "coats";      // written into save files — NEVER rename

        public string SaveKey => Key;

        /// <summary>
        /// Every saved coat. A struct with a public field rather than a bare array, because a
        /// <c>CaptureState</c> that returns a bare list has its key dropped by <c>StateBag.Set</c>.
        /// </summary>
        public struct State
        {
            public SurfaceCoatRecord[] coats;
        }

        private SurfaceCoatField field;
        private readonly List<SurfaceCoatRecord> collected = new List<SurfaceCoatRecord>();

        /// <summary>
        /// The field on this object, resolved on demand rather than cached in <c>Awake</c> — a
        /// round-trip test builds this component with <c>AddComponent</c>, and Unity raises no
        /// <c>Awake</c> for that outside play mode.
        /// </summary>
        private SurfaceCoatField Field =>
            field != null ? field : field = GetComponent<SurfaceCoatField>();

        private void OnEnable() => SaveManager.RegisterGlobalSaver(this);

        private void OnDisable() => SaveManager.UnregisterGlobalSaver(this);

        public object CaptureState()
        {
            if (Field == null) return null;

            collected.Clear();
            Field.CollectSaved(collected);

            // Null at defaults, which for this saver is a world nobody has frozen anything in.
            return collected.Count == 0 ? null : new State { coats = collected.ToArray() };
        }

        public void RestoreState(JObject state)
        {
            if (Field == null) return;

            // A null payload means "restore defaults", and for coats that is an unfrozen world —
            // so the field is still told, and clears whatever it was holding.
            SurfaceCoatRecord[] coats = state == null
                ? null
                : state.ToObject<State>(SaveSerializer.Serializer).coats;

            Field.RestoreSaved(coats);
        }
    }
}
