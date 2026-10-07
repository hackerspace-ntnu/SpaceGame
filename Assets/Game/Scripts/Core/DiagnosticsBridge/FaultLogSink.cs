// Everything that goes wrong outside a barrier, captured so it can be reported.
//
// The barriers only see what they wrap. A throw in an Update, an OnDestroy, or inside Netcode still
// happens, still breaks something, and still leaves nothing behind except a console the player
// cannot read. This puts those in the same ledger, so /faults answers the real question — "what went
// wrong in that session" — rather than "what went wrong in the places we thought to guard".
//
// Errors and exceptions only. Warnings are routine here (unregistered relays, unreplicated actions)
// and would bury the thing being looked for.
//
// It does not throw its own errors back into the sink. The handler is re-entrant by construction —
// Debug.LogError inside a log callback calls the callback again — so anything it might log would
// loop until the stack ran out.
using System;
using UnityEngine;
using SpaceGame.Diagnostics;

namespace SpaceGame.Core
{
    public static class FaultLogSink
    {
        /// <summary>Prefix on the messages the barrier itself writes; they are already in the ledger.</summary>
        private const string BarrierPrefix = "[Fault]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            // Statics survive play-mode exit here, so a second play session would otherwise install
            // a second handler and record everything twice.
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;

            // The barrier has already written a richer record for these, with the owner and the site
            // on it. Recording them again would double every guarded fault in the ledger.
            if (condition != null && condition.StartsWith(BarrierPrefix, StringComparison.Ordinal)) return;

            FaultLedger.Add(new FaultRecord(
                site: type.ToString(),
                owner: SessionContext(),
                detail: Trim(condition) + " | " + Trim(stackTrace),
                count: 1,
                quarantined: false,
                time: Time.realtimeSinceStartup));
        }

        /// <summary>
        /// Who this machine was when the error happened. Host and client see different bugs — and
        /// most of what breaks a session here is a race between them — so a record that does not say
        /// which side it came from is half a report.
        /// </summary>
        private static string SessionContext()
        {
            if (!Network.IsNetworked) return "offline";
            return Network.Server ? $"host:{Network.LocalClientId}" : $"client:{Network.LocalClientId}";
        }

        // Bounded because a stack trace is unbounded, the ledger holds 64 of these, and this is
        // going into a chat line and a text file, not a debugger.
        private static string Trim(string text)
        {
            const int limit = 400;
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Replace('\n', ' ').Replace('\r', ' ');
            return text.Length <= limit ? text : text.Substring(0, limit) + "…";
        }
    }
}
