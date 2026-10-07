using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the burnt-out module's fire on the lander: whether it has caught yet (and how close
    /// it is to catching), how strong it is, and whether it has been put out.
    ///
    /// <para>
    /// A fire saved burning reloads burning at the same strength, and one already put out reloads
    /// out: <see cref="ShipPartFirePhase.Out"/> is permanent, so the fire never lights twice in one
    /// world. A save from before this saver existed reads null: the fire has not caught and its
    /// clock starts again from nothing, which is what a world that never had the fire should get.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(ShipPartFire))]
    public class ShipPartFireSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "partFire";              // written into save files — never rename

        private ShipPartFire fire;

        // Lazy-resolved, NOT cached in Awake: EditMode tests never run Awake.
        private ShipPartFire Fire => fire != null ? fire : fire = GetComponent<ShipPartFire>();

        public string SaveKey => Key;

        public struct State
        {
            public byte phase;
            public float strength;
            public float armedSeconds;
        }

        public object CaptureState()
        {
            if (Fire == null) return null;

            ShipPartFireState s = Fire.State;
            if (s.phase == ShipPartFirePhase.Dormant && s.armedSeconds <= 0f) return null;

            return new State { phase = (byte)s.phase, strength = s.strength, armedSeconds = s.armedSeconds };
        }

        public void RestoreState(JObject state)
        {
            if (Fire == null) return;

            if (state == null)
            {
                Fire.Restore(default);
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            Fire.Restore(new ShipPartFireState
            {
                phase = (ShipPartFirePhase)restored.phase,
                strength = Mathf.Clamp01(restored.strength),
                armedSeconds = Mathf.Max(0f, restored.armedSeconds),
            });
        }
    }
}
