using System.Collections.Generic;
using System.Linq;
using SpaceGame.Core;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Registers one chat command per emote in the <see cref="EmoteCatalog"/> — <c>/wave</c>,
    /// <c>/cheer</c> and so on — the way <see cref="ChatBuiltinCommands"/> registers its own, so
    /// the command table never has to know what an emote is.
    ///
    /// <para>
    /// Also <c>/act</c>, the door onto the whole library rather than the curated few: any action by
    /// name (<c>/act Walk Drunk</c>) or any cue word (<c>/act greet</c>, picked to fit the body the
    /// way a reaction would be), and with no argument the vocabulary itself.
    /// </para>
    /// <para>
    /// A command runs on the server, as the sender. It finds the sender's body, asks its
    /// <see cref="PlayerEmotes"/> to play, and says so in the log for everyone: an emote nobody
    /// is looking at is still worth a line of chat.
    /// </para>
    /// </summary>
    public static class PlayerEmoteCommands
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            EmoteCatalog catalog = EmoteCatalog.Default;
            if (catalog == null) return;

            // Register replaces by name, so a domain reload leaves one entry per emote.
            IReadOnlyList<EmoteCatalog.Entry> entries = catalog.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                int index = i;
                string word = entries[i].word;
                ChatCommands.Register(word, "/" + word, $"Emote: {word}.",
                                      (sender, args) => Run(sender, index));
            }

            ChatCommands.Register("act", "/act <action or cue>",
                                  "Play any animation by name or cue word; /act alone lists the cues.", Act);
        }

        private static string Act(ulong sender, string[] args)
        {
            CharacterActionCatalog actions = CharacterActionCatalog.Default;
            if (actions == null) return "No animation catalog in this build.";
            if (args.Length == 0)
                return "Cues: " + string.Join(", ", actions.Cues.Select(c => c.name)) + ". Or an action by name.";

            GameObject body = ChatBuiltinCommands.FindBody(sender);
            var emotes = body != null ? body.GetComponent<PlayerEmotes>() : null;
            if (emotes == null) return "You have no body to do that with right now.";

            string word = string.Join(" ", args);
            if (!actions.TryFind(word, out CharacterAction action) && actions.TryFindCue(word, out CharacterCue cue))
            {
                BodyLanguage language = BodyLanguage.Of(body.transform);
                action = language != null ? language.Pick(cue) : null;
                if (action == null) return $"Nothing tagged '{cue.name}' fits what your body is doing right now.";
            }

            if (action == null) return $"No action or cue called '{word}'. /act lists the cues.";
            return emotes.PlayAction(action) ? null : $"'{action.name}' could not be played.";
        }

        private static string Run(ulong sender, int index)
        {
            GameObject body = ChatBuiltinCommands.FindBody(sender);
            if (body == null) return "You have no body to do that with right now.";

            var emotes = body.GetComponent<PlayerEmotes>();
            if (emotes == null) return "This body cannot emote.";

            emotes.Play(index);

            EmoteCatalog.Entry emote = EmoteCatalog.Default.At(index);
            if (!string.IsNullOrEmpty(emote.line))
            {
                var identity = body.GetComponent<PlayerIdentity>();
                string name = identity != null ? identity.DisplayName : "Somebody";
                ChatNetwork.Announce(string.Format(emote.line, name));
            }
            return null;
        }
    }
}
