using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists how far a cracked machine has been soldered shut (<see cref="TorchRepairable"/>).
    ///
    /// <para>
    /// A machine that is whole writes nothing, and a record that is absent reads as whole — so a save from before the machine
    /// could crack, and a machine soldered shut, both load undamaged. Half-done work keeps its seconds: a reload resumes on the
    /// seam that was glowing, not from the first.
    /// </para>
    /// <para>
    /// A child saver, collected by the entity above it (the hull, for the oxygen plant), and wired by hand by whatever builds
    /// the machine, for the reason <c>OxygenPlantMountSaveable</c> gives: <c>SaveablePolicy.Ensure</c> runs on an entity's root
    /// alone.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(TorchRepairable))]
    public class TorchRepairableSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "torchRepair";           // written into save files — never rename

        private TorchRepairable repairable;

        // Lazy-resolved, NOT cached in Awake: EditMode tests never run Awake.
        private TorchRepairable Repairable => repairable != null ? repairable : repairable = GetComponent<TorchRepairable>();

        public string SaveKey => Key;

        public struct State
        {
            public bool damaged;
            public float soldered;
        }

        public object CaptureState() =>
            Repairable == null || !Repairable.NeedsRepair
                ? null
                : new State { damaged = true, soldered = Repairable.SolderedSeconds };

        public void RestoreState(JObject state)
        {
            if (Repairable == null) return;

            if (state == null)
            {
                Repairable.Restore(false, 0f);
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);
            Repairable.Restore(restored.damaged, restored.soldered);
        }
    }
}
