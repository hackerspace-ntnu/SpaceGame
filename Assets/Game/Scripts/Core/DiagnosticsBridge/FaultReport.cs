// Writes the session's faults to a file a player can attach to a bug report.
//
// This exists because the bugs this whole system is about are network races, and a race is not
// reproducible on demand. When you cannot reproduce, you invest in observability instead — which
// here means making the evidence a single command rather than "could you find your Player.log".
using System;
using System.IO;
using System.Text;
using UnityEngine;
using SpaceGame.Diagnostics;

namespace SpaceGame.Core
{
    public static class FaultReport
    {
        /// <summary>
        /// Writes the ledger to <c>Application.persistentDataPath</c> and returns the path, or an
        /// empty string when it could not be written.
        /// </summary>
        public static string Write(out string problem)
        {
            problem = null;

            var text = new StringBuilder();
            text.AppendLine("SpaceGame fault report");
            text.AppendLine($"written   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            text.AppendLine($"unity     {Application.unityVersion}");
            text.AppendLine($"platform  {Application.platform}");
            text.AppendLine($"faults    {FaultLedger.TotalFaults} total, {FaultLedger.Recent.Count} kept");
            text.AppendLine();

            foreach (FaultRecord record in FaultLedger.Recent)
                text.AppendLine(record.ToString());

            string path = Path.Combine(Application.persistentDataPath,
                                       $"faults-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

            try
            {
                File.WriteAllText(path, text.ToString());
                return path;
            }
            catch (IOException e)
            {
                problem = e.Message;
            }
            catch (UnauthorizedAccessException e)
            {
                problem = e.Message;
            }

            // No catch-all. A failure this does not name is a failure somebody needs to see, and
            // swallowing it here would leave a player believing a report exists that does not.
            return string.Empty;
        }
    }
}
