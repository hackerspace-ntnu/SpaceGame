// The standing terminal's half of the two-process run.
//
// What this exists to catch: a client asks for a page and the host's screen flips while the
// client's does not (the RPC never crossed, or the NetworkVariable never came back), and a client
// that walks up to a terminal the host is already at is let in anyway. Both look like a working
// feature on the host. The terminal is nested on the ship FitShipPartsAsHost spawns, so it exists
// on both machines by the time either half gets here.
using System.Collections;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Core
{
    internal sealed partial class AutotestRunner
    {
        /// <summary>The page the client asks for. Not the default, or a page that never moved would pass.</summary>
        private const int TerminalTestPage = 2;

        /// <summary>Longest either side waits for the other's terminal traffic to land.</summary>
        private const float TerminalWaitSeconds = 30f;

        /// <summary>
        /// Client: ask the replicated terminal for a page it does not show, and wait for the
        /// server's answer to come back over the wire. Then press it the way a player does and
        /// read the operator claim the server replicated, and let go.
        /// </summary>
        private IEnumerator UseTerminalAsClient()
        {
            TerminalConsole console = Object.FindFirstObjectByType<TerminalConsole>();
            if (console == null)
            {
                Report("CLIENT_TERMINAL", "none replicated");
                yield break;
            }

            Report("CLIENT_TERMINAL_PAGE_BEFORE", console.Page);
            console.RequestPage(TerminalTestPage);
            yield return WaitAtMost(() => console.Page == TerminalTestPage, TerminalWaitSeconds);
            Report("CLIENT_TERMINAL_PAGE_SEEN", console.Page);

            GameObject player = AutotestProbes.LocalPlayerObject();
            Interactor interactor = player != null ? player.GetComponentInChildren<Interactor>(true) : null;
            if (interactor == null)
            {
                Report("CLIENT_TERMINAL_INTERACTOR", "none");
                yield break;
            }

            // The press. The zoom-in is local and may refuse in a headless player (no eye to
            // fly from); the claim is what has to cross, and it is only sent once the session
            // opened, so both are reported.
            Report("CLIENT_TERMINAL_CAN_INTERACT", console.CanInteract(interactor));
            console.Interact(interactor);
            TerminalFocusSession session = console.GetComponent<TerminalFocusSession>();
            Report("CLIENT_TERMINAL_SESSION_OPEN", session != null && session.IsOpen);
            yield return WaitAtMost(() => console.Occupied, TerminalWaitSeconds);
            Report("CLIENT_TERMINAL_OCCUPIED_SEEN", console.Occupied);

            if (session != null) session.Exit();
            yield return WaitAtMost(() => !console.Occupied, TerminalWaitSeconds);
            Report("CLIENT_TERMINAL_RELEASED_SEEN", !console.Occupied);
        }

        /// <summary>
        /// Host: watch the page the client asked for arrive on the authority, then the claim,
        /// then its release. Every one of these values is decided here and only READ on the
        /// client, so agreement between the two logs is the proof.
        /// </summary>
        private IEnumerator WatchTerminalAsHost()
        {
            TerminalConsole console = Object.FindFirstObjectByType<TerminalConsole>();
            if (console == null)
            {
                Report("HOST_TERMINAL", "none on the ship");
                yield break;
            }

            yield return WaitAtMost(() => console.Page == TerminalTestPage, TerminalWaitSeconds);
            Report("HOST_TERMINAL_PAGE", console.Page);

            yield return WaitAtMost(() => console.Occupied, TerminalWaitSeconds);
            Report("HOST_TERMINAL_OCCUPIED", console.Occupied);

            yield return WaitAtMost(() => !console.Occupied, TerminalWaitSeconds);
            Report("HOST_TERMINAL_RELEASED", !console.Occupied);
        }
    }
}
