using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists what is plugged into the oxygen plant.
    ///
    /// <para>
    /// Both docks are real inventory: a power cell and a bottle left in the machine are items the
    /// player took out of their own hotbar, so losing them on a reload is not a cosmetic reset —
    /// they are gone, and a machine that came back dark would need a cell the player no longer has.
    /// </para>
    /// <para>
    /// The fill deadline is deliberately not stored. It is an instant on a clock the loaded session
    /// does not share, and <c>OxygenGenerator.RestoreDock</c> starts a fresh fill instead, so a
    /// world saved with a bottle half-filled in a powered machine reloads and fills it again.
    /// </para>
    /// <para>
    /// Server-authoritative on the way back in, like the repair station's: the restore writes
    /// through the generator's <c>NetworkVariable</c>, so clients get the docked cell by
    /// replication rather than by each of them running a second copy of this saver.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(OxygenGenerator))]
    public class OxygenGeneratorSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "oxygen";      // written into save files — NEVER rename

        private OxygenGenerator generator;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private OxygenGenerator Generator =>
            generator != null ? generator : generator = GetComponent<OxygenGenerator>();

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>Is a power cell fitted?</summary>
            public bool cell;

            /// <summary>An <c>OxygenGenerator.DockedTank</c>. Numbers are frozen — never renumber.</summary>
            public int tank;
        }

        public object CaptureState()
        {
            if (Generator == null) return null;

            bool cell = Generator.Powered;
            var tank = (int)Generator.Tank;

            // An untouched machine holds nothing, which is what the prefab already says. Storing a
            // pair of zeros for it would put a record on every ship that has never been used.
            if (!cell && tank == 0) return null;

            return new State { cell = cell, tank = tank };
        }

        public void RestoreState(JObject state)
        {
            if (Generator == null) return;

            if (state == null)
            {
                Generator.RestoreDock(false, OxygenGenerator.DockedTank.None);
                return;
            }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);
            Generator.RestoreDock(restored.cell, (OxygenGenerator.DockedTank)restored.tank);
        }
    }
}
