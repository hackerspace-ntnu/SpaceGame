// Fills the {tokens} in a resident's line from the speaker and the resident the line is about.
// Runs on every machine when a line arrives, from baked roster data only, so every machine resolves
// the same sentence. A token with nothing to name becomes a neutral word, never raw braces.
using System;
using System.Collections.Generic;
using System.Text;

namespace SpaceGame.Agents.Residents
{
    public static class SpeechTokens
    {
        /// <summary>Every token a line may use, with the word said when there is nothing to name.</summary>
        private static readonly Dictionary<string, string> Fallbacks = new()
        {
            { "{name}", "someone" },
            { "{role}", "worker" },
            { "{friend}", "an old friend" },
            { "{kin}", "my kin" },
            { "{item}", "tool" },
            { "{ambition}", "a quiet life" },
            { "{need}", "water" },
            { "{place}", "here" },
        };

        public static IReadOnlyCollection<string> Known => Fallbacks.Keys;

        /// <summary>How many tokens <paramref name="text"/> uses; <paramref name="unknown"/> is the first unrecognised one.</summary>
        public static int CountTokens(string text, out string unknown)
        {
            unknown = null;
            int count = 0;
            for (int open = text.IndexOf('{'); open >= 0; open = text.IndexOf('{', open + 1))
            {
                int close = text.IndexOf('}', open);
                string token = close > open ? text.Substring(open, close - open + 1) : text.Substring(open);
                if (!Fallbacks.ContainsKey(token)) unknown ??= token;
                count++;
            }
            return count;
        }

        /// <summary>
        /// <c>{name}</c> is the subject when there is one (gossip, grief), else the speaker.
        /// <c>{friend}</c>/<c>{kin}</c> name the subject when the speaker holds that bond to it, else the
        /// speaker's first such bond. <c>{item}</c> is what the speaker's archetype carries.
        /// <c>{place}</c> is what the speaker's held spot is called (its use's display name), or "here" when it holds none.
        /// <c>{ambition}</c> and <c>{need}</c> have no data behind them yet and always say the neutral word.
        /// </summary>
        public static string Resolve(string text, Resident speaker, Resident subject)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;

            var builder = new StringBuilder(text);
            foreach (KeyValuePair<string, string> token in Fallbacks)
            {
                int at = IndexOf(builder, token.Key);
                if (at < 0) continue;

                string word = Name(token.Key, speaker, subject) ?? token.Value;
                builder.Replace(token.Key, word);
                if (at == 0 || EndsSentence(builder, at)) builder[at] = char.ToUpperInvariant(builder[at]);
            }

            return builder.ToString();
        }

        private static string Name(string token, Resident speaker, Resident subject) => token switch
        {
            "{name}" => NameOf(subject != null ? subject : speaker),
            "{role}" => speaker != null && !string.IsNullOrWhiteSpace(speaker.RoleName) ? speaker.RoleName : null,
            "{friend}" => Bonded(speaker, subject, BondKind.Friend),
            "{kin}" => Bonded(speaker, subject, BondKind.Family),
            "{item}" => speaker != null && speaker.archetype != null && speaker.archetype.heldItem != null
                ? speaker.archetype.heldItem.itemName
                : null,
            "{place}" => PlaceNameOf(speaker),
            _ => null,
        };

        private static string PlaceNameOf(Resident speaker)
        {
            if (speaker == null || speaker.Presence == null || speaker.Presence.Place == ResidentPresence.NoPlace) return null;

            string name = speaker.Society?.Place(speaker.Presence.Place)?.Use?.displayName;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        private static string Bonded(Resident speaker, Resident subject, BondKind kind)
        {
            if (speaker == null || speaker.bonds == null) return null;

            Resident first = null;
            foreach (ResidentBond bond in speaker.bonds)
            {
                if (bond.kind != kind) continue;
                Resident other = ResidentAt(speaker, bond.other);
                if (other == null) continue;
                if (other == subject) return NameOf(other);
                first ??= other;
            }
            return NameOf(first);
        }

        private static Resident ResidentAt(Resident speaker, int index)
        {
            return speaker.Society?.ResidentAt(index);
        }

        private static string NameOf(Resident r) =>
            r != null && !string.IsNullOrWhiteSpace(r.DisplayName) ? r.DisplayName : null;

        private static int IndexOf(StringBuilder builder, string token) =>
            builder.ToString().IndexOf(token, StringComparison.Ordinal);

        private static bool EndsSentence(StringBuilder builder, int at) =>
            at >= 2 && builder[at - 1] == ' ' && (builder[at - 2] == '.' || builder[at - 2] == '!' || builder[at - 2] == '?');
    }
}
