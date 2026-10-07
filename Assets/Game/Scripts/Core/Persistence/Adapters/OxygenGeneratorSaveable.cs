using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists what is plugged into the oxygen plant, and how full each of them is.
    ///
    /// <para>
    /// Both docks are real inventory: a battery and a tank left in the machine are items the player
    /// took out of their own hotbar, so losing them on a reload is not a cosmetic reset — they are
    /// gone, and a machine that came back dark would need a battery the player no longer has.
    /// </para>
    /// <para>
    /// The fill DEADLINE is still not stored — it is an instant on a clock the loaded session does
    /// not share — but both CHARGES are, so <c>OxygenGenerator.RestoreDock</c> resumes the fill
    /// from where the machine stood rather than starting the tank over. Before 2026-09-04 a charge
    /// was one of three enum values and there was nothing partial to lose; there is now.
    /// </para>
    /// <para>
    /// Server-authoritative on the way back in, like the repair station's: the restore writes
    /// through the generator's <c>NetworkVariable</c>, so clients get the docked battery by
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
            /// <summary>
            /// v1: was a power cell fitted? Written by no version of this saver any more, and read
            /// only when <see cref="battery"/> is absent.
            ///
            /// Kept on the struct rather than read straight off the JObject so that the migration
            /// is visible in the type instead of buried in a string literal.
            /// </summary>
            public bool cell;

            /// <summary>
            /// v1: an <c>OxygenGenerator.DockedTank</c>. Numbers are frozen — never renumber. Read
            /// only when <see cref="tankCharge"/> is absent.
            ///
            /// <b>The new fields are NOT called <c>cell</c> and <c>tank</c>, and must not be.</b>
            /// An old payload's <c>"tank": 1</c> would deserialize straight into a float field of
            /// that name as 1.0 — a DRAINED tank restored as a full one, silently, in every world
            /// written before 2026-09-04.
            /// </summary>
            public int tank;

            /// <summary>
            /// Battery charge 0..1, or absent for an empty slot.
            ///
            /// Nullable so that "no battery" and "a flat battery" stay different worlds. They look
            /// identical on the machine — both are dark — and are not: one of them is an item the
            /// player put there and can take back.
            /// </summary>
            public float? batteryCharge;

            /// <summary>Tank charge 0..1, or absent for an empty collar.</summary>
            public float? tankCharge;
        }

        public object CaptureState()
        {
            if (Generator == null) return null;

            float battery = Generator.BatteryCharge;
            float tank = Generator.TankCharge;

            // An untouched machine holds nothing, which is what the prefab already says. Storing a
            // record for it would put one on every ship that has never been used.
            if (battery < 0f && tank < 0f) return null;

            return new State
            {
                batteryCharge = battery < 0f ? null : battery,
                tankCharge = tank < 0f ? null : tank,
            };
        }

        public void RestoreState(JObject state)
        {
            if (Generator == null) return;

            if (state == null)
            {
                Generator.RestoreDock(-1f, -1f);
                return;
            }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);

            // A payload with neither float in it was written before charges existed. Its two flags
            // are read instead, and they are exactly expressible: a fitted cell was always a full
            // battery, and the tank was empty or full with nothing in between.
            float battery = restored.batteryCharge ?? (restored.cell ? 1f : -1f);

            float tank = restored.tankCharge ?? (OxygenGenerator.DockedTank)restored.tank switch
            {
                OxygenGenerator.DockedTank.Charged => 1f,
                OxygenGenerator.DockedTank.Drained => 0f,
                _ => -1f,
            };

            Generator.RestoreDock(battery, tank);
        }
    }
}
