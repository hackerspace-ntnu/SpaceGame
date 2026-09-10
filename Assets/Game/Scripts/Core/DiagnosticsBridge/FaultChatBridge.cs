// What a player is told when something breaks, and how they report it.
//
// Separate from SpaceGame.Diagnostics because that assembly has an empty reference list — which is
// the only reason all 19 module assemblies can use it — and ChatLog lives in Assembly-CSharp. So the
// primitive raises events and this decides what they mean to a person.
//
// Chat rather than a new HUD widget: the channel already exists, already survives scene loads,
// already has scrollback, and already has a command table anything may register into. A second
// notification system would be the same thing again with its own bugs.
//
// Only quarantines are announced. A single fault is for the log; a quarantine means a feature the
// player was using has stopped, and saying nothing about that is how "the gun does nothing now"
// becomes an hour of somebody's evening.
using System;
using System.Text;
using UnityEngine;
using SpaceGame.Diagnostics;

namespace SpaceGame.Core
{
    public static class FaultChatBridge
    {
        /// <summary>How many recent faults /faults prints. More than this and it is a file, not a chat line.</summary>
        private const int PrintLimit = 10;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            // Unsubscribe first: statics survive play-mode exit here, so without this the second
            // play session in an Editor has two of these attached and announces everything twice.
            Fault.Quarantined -= Announce;
            Fault.Quarantined += Announce;

            // Register replaces by name, so running this again after a domain reload leaves one
            // entry rather than a duplicate. Same contract ChatBuiltinCommands relies on.
            ChatCommands.Register("faults", "/faults [dump]",
                                  "List the features that have faulted this session, or write a report file.",
                                  List, "errors");
        }

        private static void Announce(FaultRecord record) =>
            ChatLog.AddSystem($"{record.Site} on '{record.Owner}' faulted and has been switched off. " +
                              "The rest of the game keeps running. Type /faults for detail.");

        private static string List(ulong sender, string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "dump", StringComparison.OrdinalIgnoreCase))
            {
                string path = FaultReport.Write(out string problem);
                return string.IsNullOrEmpty(path)
                    ? $"Could not write the report: {problem}"
                    : $"Wrote {FaultLedger.TotalFaults} fault(s) to {path}";
            }

            if (FaultLedger.TotalFaults == 0) return "No faults this session.";

            var text = new StringBuilder();
            text.Append($"{FaultLedger.TotalFaults} fault(s) this session");

            int from = Mathf.Max(0, FaultLedger.Recent.Count - PrintLimit);
            if (from > 0 || FaultLedger.TotalFaults > FaultLedger.Recent.Count)
                text.Append($" — showing the last {FaultLedger.Recent.Count - from}");
            text.Append(':');

            for (int i = from; i < FaultLedger.Recent.Count; i++)
                text.Append('\n').Append(FaultLedger.Recent[i]);

            return text.ToString();
        }
    }
}
