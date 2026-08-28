// Drives one end of a two-process multiplayer test from the command line.
//
// This exists because the client half of the netcode cannot be tested any other way. Running two
// NetworkManagers in one process — which is how Netcode's own integration tests work — is useless
// here, because this codebase asks NetworkManager.Singleton who it is; a second manager in the same
// process is invisible to Network.IsNetworked/Simulates/Owns, so the test would exercise Netcode
// rather than the game. A real client has to be a real second process.
//
// Inert unless -sgmode is on the command line, so it costs a shipped build nothing but this check.
//
//   Player.app/Contents/MacOS/<exe> -batchmode -nographics -sgmode host   -logFile host.log
//   Player.app/Contents/MacOS/<exe> -batchmode -nographics -sgmode client -logFile client.log
//
// Each side prints [MPTEST] key=value lines. The caller asserts across both logs — a fact only one
// side can observe (a client's view of health the server changed) is only meaningful when read
// against the other side's report of what it did.
//
// There is a third mode, `persist`, which runs alone and asks the other half of this project's
// non-negotiables: that a feature survives save, quit and load. It has no peer because none of what
// it asks is a question about one.
//
//   Player.app/Contents/MacOS/<exe> -batchmode -nographics -sgmode persist -logFile persist.log
//
// The scripts themselves live on AutotestRunner, one partial per mode.
using UnityEngine;

namespace SpaceGame.Core
{
    public static class MultiplayerAutotest
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string mode = CommandLineArgs.Value(System.Environment.GetCommandLineArgs(), "-sgmode");
            if (string.IsNullOrEmpty(mode)) return;

            var runner = new GameObject("[MultiplayerAutotest]");
            Object.DontDestroyOnLoad(runner);
            runner.AddComponent<AutotestRunner>().Begin(mode);
        }
    }
}
