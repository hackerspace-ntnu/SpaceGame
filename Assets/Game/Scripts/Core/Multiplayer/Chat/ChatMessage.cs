namespace SpaceGame.Core
{
    /// <summary>What a chat line is, which decides both how it is drawn and who was sent it.</summary>
    public enum ChatKind
    {
        /// <summary>Somebody typed it. Carries a <see cref="ChatMessage.Sender"/> and went to everyone.</summary>
        Player,

        /// <summary>The session talking about itself — joins, leaves. Server-authored, sent to everyone.</summary>
        System,

        /// <summary>
        /// The server answering one player: a command's result, or why it was refused. Sent to that
        /// player alone, which is the whole reason chat needs a unicast path and cannot ride
        /// <see cref="NetMessaging"/>.
        /// </summary>
        Notice,
    }

    /// <summary>
    /// One line in the log, as it is held on the machine showing it.
    /// <para>
    /// A plain readonly struct with no Unity or Netcode types in it: the wire format is the RPC
    /// signature in <see cref="ChatNetwork"/>, and keeping the two apart means the log and its
    /// formatting can be tested without a network session or a scene.
    /// </para>
    /// </summary>
    public readonly struct ChatMessage
    {
        public readonly ChatKind Kind;

        /// <summary>Who said it. Empty for <see cref="ChatKind.System"/> and <see cref="ChatKind.Notice"/>.</summary>
        public readonly string Sender;

        public readonly string Text;

        /// <summary>
        /// When it landed, on the unscaled clock.
        /// <para>
        /// Unscaled because the closed-mode fade has to keep running while the game clock is
        /// stopped — a solo session with the pause menu up freezes <see cref="UnityEngine.Time.time"/>,
        /// and lines that never aged out would still be on screen an hour later.
        /// </para>
        /// </summary>
        public readonly float ArrivedUnscaled;

        public ChatMessage(ChatKind kind, string sender, string text, float arrivedUnscaled)
        {
            Kind = kind;
            Sender = sender ?? string.Empty;
            Text = text ?? string.Empty;
            ArrivedUnscaled = arrivedUnscaled;
        }

        public bool HasSender => Kind == ChatKind.Player && !string.IsNullOrEmpty(Sender);
    }
}
