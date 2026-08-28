// The MonoBehaviour half of MultiplayerAutotest — coroutines need one.
//
// One class across five files. This one holds what every mode shares: the step waits, the report
// line and the exit. Each mode's script is its own partial — Host, Client, Persistence — and the
// net-gun shot two of them fire is AutotestRunner.NetGun.cs. Queries that ask "what does THIS
// machine have" are AutotestProbes.
using System.Collections;
using UnityEngine;

namespace SpaceGame.Core
{
    internal sealed partial class AutotestRunner : MonoBehaviour
    {
        private const string WorldScene = "persistentScene";
        private const ushort Port = 7897;
        private const float StepTimeout = 120f;

        // Counts relay traffic arriving from the other process. Static because the listener is
        // registered against a networked object that may be replaced during the run.
        private static int relayFromPeer;

        public void Begin(string mode)
        {
            StartCoroutine(mode switch
            {
                "host" => RunHost(),
                "persist" => RunPersistence(),
                _ => RunClient(),
            });
        }

        private static void Report(string key, object value) =>
            Debug.Log($"[MPTEST] {key}={value}");

        private static void CountRelayFromPeer(in NetArg arg, ulong sender)
        {
            if (arg.B == 31337) relayFromPeer++;
        }

        /// <summary>
        /// Waits, but never forever. A hung step has to end the process with a report saying
        /// which step hung, or a batch-mode run just sits there and the caller learns nothing.
        /// </summary>
        private IEnumerator WaitFor(System.Func<bool> condition, string what)
        {
            float deadline = Time.realtimeSinceStartup + StepTimeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Report("TIMEOUT_WAITING_FOR", what);
                    Report("DONE", false);
                    Finish();
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// Waits, and gives up quietly.
        ///
        /// The counterpart of <see cref="WaitFor"/>, for the steps where not arriving is an
        /// ANSWER rather than a broken run. "The client never saw the net" is the finding the
        /// net gun step exists to produce, and ending the process on it would throw away the
        /// numbers that say how badly it failed.
        /// </summary>
        private IEnumerator WaitAtMost(System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        }

        private void Finish()
        {
            Debug.Log("[MPTEST] EXIT");
            Application.Quit();
        }
    }
}
