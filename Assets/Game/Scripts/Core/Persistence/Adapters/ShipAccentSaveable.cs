using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Puts a ship's team livery in its save record.
    ///
    /// <para>
    /// Without this the colour is the one thing about a team's hull that lives only in a
    /// NetworkVariable: a match saved and reloaded brings every ship back in its authored paint,
    /// with nothing logged, so both sides end up the same colour in the one mode where colour is
    /// how you tell friend from enemy. The ship itself already persists — pose, motor, hull parts —
    /// which is exactly what makes the omission invisible.
    /// </para>
    ///
    /// <para>
    /// The restore is SERVER-ONLY because the swatch is a server-write variable, the same shape
    /// <c>SuitColorSaveable</c> documents on the other side of the fence: there the value is
    /// owner-write, so only the owner may put it back; here only the server may. A client that
    /// wrote it would have the write rejected and the ship would simply stay unpainted, which looks
    /// identical to no record existing at all.
    /// </para>
    /// </summary>
    public class ShipAccentSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "ship_accent";

        private ShipTeamAccent accent;

        private ShipTeamAccent Accent =>
            accent != null ? accent : accent = GetComponent<ShipTeamAccent>();

        public string SaveKey => Key;

        public struct State
        {
            public int swatch;
        }

        public object CaptureState()
        {
            if (Accent == null) return null;

            return new State { swatch = Accent.Swatch };
        }

        public void RestoreState(JObject state)
        {
            if (Accent == null || state == null) return;

            // A hull with no record keeps the paint it was authored in rather than being repainted
            // in the first swatch — the sentinel survives the round trip for the same reason it
            // exists on the wire.
            if (!Network.Server) return;

            Accent.SetSwatch(state.ToObject<State>(SaveSerializer.Serializer).swatch);
        }
    }
}
