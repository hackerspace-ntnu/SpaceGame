// Proves the degradation contract across two machines: a host whose gameplay code throws must not
// take the client's session with it.
//
// Host-only verification proves nothing here — a perfect host and blind clients is the failure mode
// this whole project keeps hitting — so the claim is checked from both logs. The host deliberately
// breaks one thing; the client must still be spawned, still have its player object, and still be
// receiving messages afterwards.
using UnityEngine;
using SpaceGame.Diagnostics;

namespace SpaceGame.Core
{
    internal sealed partial class AutotestRunner
    {
        /// <summary>Faults injected before the check, so the numbers below are not accidental.</summary>
        private const int InjectedFaults = 3;

        /// <summary>
        /// Host side: throw from inside a barrier several times and report that the session is still
        /// alive afterwards.
        /// </summary>
        private void ProbeFaultContainment()
        {
            var victim = new GameObject("FaultProbeVictim");

            int contained = 0;
            for (int i = 0; i < InjectedFaults; i++)
                if (!Fault.Run(victim.transform, "Autotest.Inject",
                               () => throw new System.InvalidOperationException("injected")))
                    contained++;

            Report("HOST_FAULTS_CONTAINED", contained);
            Report("HOST_FAULTS_LEDGERED", FaultLedger.TotalFaults >= InjectedFaults);
            Report("HOST_ALIVE_AFTER_FAULTS", Network.IsNetworked && Network.Server);

            Object.Destroy(victim);
        }
    }
}
