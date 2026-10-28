using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEditor;

namespace SpaceGame.EditorTools
{
    public enum ClipVerdict { Unreviewed, Good, Fix, Broken }

    /// <summary>
    /// The verdicts and notes from reviewing creature clips, kept in one JSON file in the project
    /// so they survive the window, travel with the repo and can be handed to whoever fixes the clips.
    /// Written on every change.
    /// </summary>
    public sealed class CreatureAnimationReviewLog
    {
        public const string FilePath = "Assets/Game/Art/Animations/Creatures/CreatureAnimationReview.json";

        private sealed class Entry
        {
            public ClipVerdict verdict;
            public string note;
        }

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter() },
        };

        // creature -> clip -> entry; sorted so the file diffs cleanly.
        private readonly SortedDictionary<string, SortedDictionary<string, Entry>> creatures;

        private CreatureAnimationReviewLog(SortedDictionary<string, SortedDictionary<string, Entry>> creatures) =>
            this.creatures = creatures;

        public static CreatureAnimationReviewLog Load() => new CreatureAnimationReviewLog(File.Exists(FilePath)
            ? JsonConvert.DeserializeObject<SortedDictionary<string, SortedDictionary<string, Entry>>>(File.ReadAllText(FilePath), Settings)
            : new SortedDictionary<string, SortedDictionary<string, Entry>>());

        public ClipVerdict Verdict(string creature, string clip) => Find(creature, clip)?.verdict ?? ClipVerdict.Unreviewed;

        public string Note(string creature, string clip) => Find(creature, clip)?.note ?? string.Empty;

        public void SetVerdict(string creature, string clip, ClipVerdict verdict) =>
            Write(creature, clip, verdict, Note(creature, clip));

        public void SetNote(string creature, string clip, string note) =>
            Write(creature, clip, Verdict(creature, clip), note);

        public int Reviewed(IEnumerable<(ReviewCreature creature, ReviewClip clip)> rows) =>
            rows.Count(r => Verdict(r.creature.Name, r.clip.Name) != ClipVerdict.Unreviewed);

        /// <summary>Every clip marked Fix or Broken, grouped by creature, as Markdown.</summary>
        public string Report(IReadOnlyList<ReviewCreature> catalog)
        {
            var rows = catalog.SelectMany(c => c.Clips.Select(clip => (creature: c, clip))).ToList();
            var counts = rows.GroupBy(r => Verdict(r.creature.Name, r.clip.Name)).ToDictionary(g => g.Key, g => g.Count());
            int Count(ClipVerdict v) => counts.TryGetValue(v, out int n) ? n : 0;

            var text = new StringBuilder();
            text.AppendLine($"# Creature animation review — {DateTime.Now:yyyy-MM-dd}");
            text.AppendLine();
            text.AppendLine($"{rows.Count - Count(ClipVerdict.Unreviewed)} of {rows.Count} clips reviewed: " +
                            $"{Count(ClipVerdict.Good)} good, {Count(ClipVerdict.Fix)} need work, {Count(ClipVerdict.Broken)} broken.");
            foreach (var creature in rows.GroupBy(r => r.creature))
            {
                var flagged = creature.Where(r => Verdict(r.creature.Name, r.clip.Name) >= ClipVerdict.Fix).ToList();
                if (flagged.Count == 0) continue;
                text.AppendLine();
                text.AppendLine($"## {creature.Key.Name}");
                text.AppendLine();
                foreach (var (c, clip) in flagged)
                {
                    string roles = clip.Roles.Count > 0 ? $" — plays as {string.Join(", ", clip.Roles)}" : " — unused in game";
                    string note = Note(c.Name, clip.Name);
                    text.AppendLine($"- **{clip.Name}** ({Verdict(c.Name, clip.Name)}{roles})" +
                                    (note.Length > 0 ? $": {note}" : string.Empty));
                }
            }
            return text.ToString();
        }

        private Entry Find(string creature, string clip) =>
            creatures.TryGetValue(creature, out var clips) && clips.TryGetValue(clip, out Entry entry) ? entry : null;

        private void Write(string creature, string clip, ClipVerdict verdict, string note)
        {
            if (!creatures.TryGetValue(creature, out var clips))
                creatures[creature] = clips = new SortedDictionary<string, Entry>();
            if (verdict == ClipVerdict.Unreviewed && string.IsNullOrEmpty(note))
                clips.Remove(clip);
            else
                clips[clip] = new Entry { verdict = verdict, note = note };
            if (clips.Count == 0) creatures.Remove(creature);

            bool created = !File.Exists(FilePath);
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(creatures, Settings));
            if (created) AssetDatabase.ImportAsset(FilePath);
        }
    }
}
