using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Registers one chat command per emote in <see cref="PlayerEmotes.Table"/> — <c>/wave</c>,
    /// <c>/cheer</c> and so on — the way <see cref="ChatBuiltinCommands"/> registers its own, so
    /// the command table never has to know what an emote is.
    ///
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
            // Register replaces by name, so a domain reload leaves one entry per emote.
            for (int i = 0; i < PlayerEmotes.Table.Length; i++)
            {
                int index = i;
                PlayerEmotes.Emote emote = PlayerEmotes.Table[i];
                ChatCommands.Register(emote.Name, "/" + emote.Name, $"Emote: {emote.Trigger.ToLowerInvariant()}.",
                                      (sender, args) => Run(sender, index));
            }
        }

        private static string Run(ulong sender, int index)
        {
            GameObject body = ChatBuiltinCommands.FindBody(sender);
            if (body == null) return "You have no body to do that with right now.";

            var emotes = body.GetComponent<PlayerEmotes>();
            if (emotes == null) return "This body cannot emote.";

            emotes.Play(index);

            PlayerEmotes.Emote emote = PlayerEmotes.Table[index];
            if (!string.IsNullOrEmpty(emote.Line))
            {
                var identity = body.GetComponent<PlayerIdentity>();
                string name = identity != null ? identity.DisplayName : "Somebody";
                ChatNetwork.Announce(string.Format(emote.Line, name));
            }
            return null;
        }
    }
}
