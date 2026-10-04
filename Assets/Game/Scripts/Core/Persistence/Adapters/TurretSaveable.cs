using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists when a deployed <see cref="RocketLauncherTurret"/> may fire again.
    ///
    /// <b>The cooldown is the part that is unfair.</b> Every weapon in this game reloaded ready to
    /// fire, and a launcher is the worst case of it: a player can save under a hostile turret and
    /// reload into a shell that had no business being in the air yet, or reload repeatedly and never
    /// be shot at, depending which side of the barrel they are on.
    ///
    /// <b>No aim to keep.</b> The launcher has no target and no aim of its own — its head is rebuilt
    /// every frame from two serialized angles — so this is the firing clock and nothing else.
    /// </summary>
    public class TurretSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "turret";     // written into save files — NEVER rename

        public string SaveKey => Key;

        public struct State
        {
            public bool hasLauncher;
            public float launcherCooldown;
        }

        private RocketLauncherTurret launcher;
        private bool looked;

        // Lazy, not cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private void Look()
        {
            if (looked) return;
            looked = true;
            launcher = GetComponent<RocketLauncherTurret>();
        }

        public object CaptureState()
        {
            Look();
            if (launcher == null) return null;

            return new State
            {
                hasLauncher = true,
                launcherCooldown = launcher.CooldownTimer,
            };
        }

        public void RestoreState(JObject state)
        {
            Look();

            if (state == null)
            {
                // At its defaults: ready to fire.
                launcher?.RestoreCooldown(0f);
                return;
            }

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            if (launcher != null && restored.hasLauncher)
                launcher.RestoreCooldown(restored.launcherCooldown);
        }
    }
}
