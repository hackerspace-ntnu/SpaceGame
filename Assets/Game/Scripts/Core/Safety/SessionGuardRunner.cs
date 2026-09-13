// Runs every session guard on a slow tick.
//
// Bootstrapped from a static rather than placed in a scene, for the same reason SessionWatchdog and
// PauseMenuUI are: gameplay is spread over a persistent scene, streamed world chunks and an
// additively loaded arena, and a listener that must exist in all of them cannot be authored into
// one of them.
//
// Slow on purpose. None of these are physics: they answer "has this been wrong for a while", and
// asking twice a second is both enough to fix it before the player gives up and cheap enough to
// ignore. The same bargain UnderTerrainGuard's checkInterval makes.
//
// Each guard runs behind the fault barrier. A guard that throws is exactly the thing this system
// exists to survive, and one broken guard must not stop the others from recovering the session.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Diagnostics;

namespace SpaceGame.Core.Safety
{
    public class SessionGuardRunner : MonoBehaviour
    {
        /// <summary>Seconds between sweeps. See the header for why this is not per-frame.</summary>
        public const float CheckIntervalSeconds = 0.5f;

        private static SessionGuardRunner instance;

        private readonly List<ISessionGuard> guards = new();
        private float sinceLastCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject(nameof(SessionGuardRunner));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SessionGuardRunner>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;

            guards.Add(new StuckScopeGuard());
            guards.Add(new InputRestoreGuard());
            guards.Add(new MountGuard());
            guards.Add(new ViewGuard());
            guards.Add(new SingularityVoidGuard());
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            // Unscaled, because half of what these guards recover from is a menu scope that has
            // stopped the clock and cannot give it back. A guard measuring frozen time would wait
            // forever for exactly the failure it exists to end.
            sinceLastCheck += Time.unscaledDeltaTime;
            if (sinceLastCheck < CheckIntervalSeconds) return;

            float interval = sinceLastCheck;
            sinceLastCheck = 0f;

            foreach (ISessionGuard guard in guards)
            {
                ISessionGuard g = guard;
                Fault.Run(this, $"Guard.{g.Name}", () => g.Check(interval));
            }
        }
    }
}
