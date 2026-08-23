using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Puts the player's suit colour in their save record, alongside the machine preference it is
    /// chosen from.
    ///
    /// <para>
    /// <b>What was wrong with PlayerPrefs alone.</b> The swatch index lived only in
    /// <c>GameSettings.SuitColorIndex</c>, which is a property of the INSTALL rather than of the
    /// character: it does not travel with a copied save file, it is not per-world, and a second
    /// player on the same machine wears the first one's colour. It also meant the appearance of a
    /// saved character was the one thing about them stored outside their record.
    /// </para>
    /// <para>
    /// <b>PlayerPrefs still seeds a NEW player.</b> A profile with no record keeps whatever the
    /// settings screen and the lobby cycler chose — <see cref="CaptureState"/> falls back to the
    /// setting when nothing has been painted yet, so the first save of a new character writes the
    /// colour they have been wearing all along rather than a blank.
    /// </para>
    /// <para>
    /// <b>Why the restore writes the setting back.</b> The colour reaches other players through
    /// <c>PlayerIdentity</c>'s owner-write NetworkVariable, which is republished from
    /// <c>GameSettings</c> on every change and on spawn. Painting the model alone would therefore be
    /// undone by the owner's next publish. So the record is applied to the setting — but only on the
    /// machine that owns the body, which is the only machine entitled to publish it.
    /// </para>
    /// </summary>
    public class SuitColorSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "suit";

        private SuitRecolor recolor;

        private SuitRecolor Recolor =>
            recolor != null ? recolor : recolor = GetComponentInChildren<SuitRecolor>(true);

        public string SaveKey => Key;

        public struct State
        {
            public int swatch;
        }

        public object CaptureState()
        {
            if (Recolor == null) return null;

            // -1 means nothing has been painted yet — a body that spawned a frame ago, or one whose
            // owner has not published. The machine preference is the honest answer there, and it is
            // exactly the "PlayerPrefs seeds a new player" rule.
            int swatch = Recolor.Current >= 0 ? Recolor.Current : GameSettings.SuitColorIndex;

            return new State { swatch = SuitPalette.Clamp(swatch) };
        }

        public void RestoreState(JObject state)
        {
            if (Recolor == null) return;

            // No record: leave the player wearing whatever this machine chose. A colour is a
            // preference, not world state, so "nothing stored" must not mean "repaint them".
            if (state == null) return;

            int swatch = SuitPalette.Clamp(state.ToObject<State>(SaveSerializer.Serializer).swatch);

            // Painted directly as well as through the setting, so the server's own copy of a body it
            // does not own still looks right even where the write below is skipped.
            Recolor.Apply(swatch);

            // Only the owner may publish an owner-write NetworkVariable, and only through the
            // setting — see the class summary. On a server restoring a remote client's record this
            // is skipped, and that client's own preference wins.
            if (Network.Owns(transform)) GameSettings.SuitColorIndex = swatch;
        }
    }
}
