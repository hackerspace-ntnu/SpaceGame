using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps which way the player was looking — the vertical half of it.
    ///
    /// <para>
    /// Yaw already survived, and that is what made this hard to notice: it lives on the body's
    /// Rigidbody rotation, which <c>PlayerRecord.Rotation</c> captures with the pose. Pitch does not
    /// live there. It is a private float on <see cref="PlayerLook"/>, spent on a child camera that
    /// no record has ever described, so a player who quit peering down a shaft came back facing the
    /// right way and staring at the horizon.
    /// </para>
    /// <para>
    /// Applied immediately rather than deferred: a look angle depends on nothing but the player, and
    /// <see cref="PlayerLook"/> integrates pitch from whatever it currently holds rather than
    /// recomputing it, so a value written at any point in the frame is simply carried on from.
    /// </para>
    /// </summary>
    public class PlayerLookSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "look";

        private PlayerLook look;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private PlayerLook Look =>
            look != null ? look : look = GetComponentInChildren<PlayerLook>(true);

        public string SaveKey => Key;

        public struct State
        {
            public float pitch;
        }

        public object CaptureState()
        {
            if (Look == null) return null;

            // Level is the default a fresh player starts at, so storing it would put a key in every
            // player's record to say nothing.
            return Mathf.Abs(Look.Pitch) < 0.01f ? null : new State { pitch = Look.Pitch };
        }

        public void RestoreState(JObject state)
        {
            if (Look == null) return;

            // Null means the record says nothing about the view, which now means "level" — so it is
            // written rather than skipped, or a respawned player would keep the angle they died at.
            if (state == null) { Look.RestorePitch(0f); return; }

            Look.RestorePitch(state.ToObject<State>(SaveSerializer.Serializer).pitch);
        }
    }
}
