// One measured quantity in the agent benchmark: a ProfilerRecorder plus the per-frame samples of
// the baseline phase (no agents) and the loaded phase (every agent live).
//
// Samples land in arrays sized up front, so sampling inside the measured frames allocates nothing;
// the sorting and string building for the report happen once, after the last frame.
using System;
using System.Globalization;
using System.Text;
using Unity.Profiling;

namespace SpaceGame.Agents
{
    internal sealed class BenchmarkChannel : IDisposable
    {
        private const float Percentile95 = 0.95f;

        private readonly string label;
        private readonly double scale;
        private readonly string format;
        private readonly long[] baseline;
        private readonly long[] loaded;
        private int baselineCount;
        private int loadedCount;
        private ProfilerRecorder recorder;

        /// <param name="scale">Multiplies a raw sample into the reported unit (ns → ms is 1e-6).</param>
        public BenchmarkChannel(ProfilerCategory category, string stat, string label, double scale,
                                string format, int baselineFrames, int loadedFrames)
        {
            this.label = label;
            this.scale = scale;
            this.format = format;
            baseline = new long[Math.Max(baselineFrames, 0)];
            loaded = new long[Math.Max(loadedFrames, 0)];
            recorder = ProfilerRecorder.StartNew(category, stat);
        }

        /// <summary>
        /// Records the last completed frame. LastValue is the previous frame's total, which is why the
        /// harness yields one frame before each phase starts sampling.
        /// </summary>
        public void Sample(bool loadedPhase)
        {
            if (!recorder.Valid) return;

            long value = recorder.LastValue;
            if (loadedPhase)
            {
                if (loadedCount < loaded.Length) loaded[loadedCount++] = value;
            }
            else if (baselineCount < baseline.Length)
            {
                baseline[baselineCount++] = value;
            }
        }

        public void AppendReport(StringBuilder report)
        {
            report.Append(label).Append(": ");
            if (!recorder.Valid)
            {
                report.AppendLine("UNAVAILABLE (profiler stat not found)");
                return;
            }

            AppendPhase(report, "baseline", baseline, baselineCount);
            report.Append(" | ");
            AppendPhase(report, "agents", loaded, loadedCount);
            report.AppendLine();
        }

        private void AppendPhase(StringBuilder report, string phase, long[] samples, int count)
        {
            report.Append(phase).Append(' ');
            if (count == 0)
            {
                report.Append("no samples");
                return;
            }

            var sorted = new long[count];
            Array.Copy(samples, sorted, count);
            Array.Sort(sorted);

            double sum = 0;
            for (int i = 0; i < count; i++) sum += sorted[i];

            report.Append("median ").Append((sorted[count / 2] * scale).ToString(format, CultureInfo.InvariantCulture))
                  .Append(" p95 ").Append((sorted[Math.Min(count - 1, (int)(count * Percentile95))] * scale).ToString(format, CultureInfo.InvariantCulture))
                  .Append(" mean ").Append((sum / count * scale).ToString(format, CultureInfo.InvariantCulture))
                  .Append(" (n=").Append(count).Append(')');
        }

        public void Dispose() => recorder.Dispose();
    }
}
