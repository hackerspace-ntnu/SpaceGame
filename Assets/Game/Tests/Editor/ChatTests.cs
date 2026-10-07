using NUnit.Framework;
using SpaceGame.Core;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The parts of chat that decide what crosses the wire and what gets drawn, tested without a
    /// session: sanitising, the ring buffer, and the command parser. Everything here has a wrong
    /// answer that is invisible in play — a message that overflows its FixedString throws inside
    /// Unity.Collections, and markup that survives sanitising only shows up on somebody else's
    /// screen.
    /// </summary>
    public class ChatTextTests
    {
        [Test]
        public void EmptyAndWhitespaceAreNothing()
        {
            Assert.AreEqual(string.Empty, ChatText.Sanitize(null));
            Assert.AreEqual(string.Empty, ChatText.Sanitize(string.Empty));
            Assert.AreEqual(string.Empty, ChatText.Sanitize("     "));
            Assert.AreEqual(string.Empty, ChatText.Sanitize("\n\t"));
        }

        [Test]
        public void ControlCharactersAreDropped()
        {
            // A pasted multi-line block would otherwise take over the whole log as one message.
            Assert.AreEqual("onetwo", ChatText.Sanitize("one\ntwo"));
            Assert.AreEqual("tabbed", ChatText.Sanitize("tab\tbed"));

            // Spacing inside the line is the player's, and is left alone.
            Assert.AreEqual("a  b", ChatText.Sanitize("a  b"));
        }

        [Test]
        public void LongMessagesAreClampedByCharacters()
        {
            string text = ChatText.Sanitize(new string('x', ChatText.MaxCharacters + 40));
            Assert.AreEqual(ChatText.MaxCharacters, text.Length);
        }

        [Test]
        public void WideMessagesAreClampedByBytes()
        {
            // Hiragana: one char each, three bytes each. A line at the character limit is 540
            // bytes, which is over both MaxBytes and what a FixedString512Bytes can hold — so the
            // character clamp alone would let a perfectly ordinary Japanese message throw on send.
            string text = ChatText.Sanitize(new string('\u3042', ChatText.MaxCharacters));

            Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(text), ChatText.MaxBytes);
            Assert.Less(text.Length, ChatText.MaxCharacters, "the byte pass should have trimmed it further");
        }

        [Test]
        public void ClosingNoparseIsDefused()
        {
            // The view wraps message bodies in <noparse>. Typing the closing tag would end that
            // block early and hand the rest of the line to TMP as live markup.
            string text = ChatText.Sanitize("hi</noparse><size=400%>BIG");

            StringAssert.DoesNotContain("</noparse>", text);
            StringAssert.Contains("<size=400%>", text); // still shown, but now inside the block
        }

        [Test]
        public void ClosingNoparseIsDefusedWhateverTheCase()
        {
            // TMP matches tags case-insensitively, so a lower-case-only guard would be no guard.
            string text = ChatText.Sanitize("x</NoParse>y</NOPARSE>z");

            Assert.AreEqual(-1, text.ToLowerInvariant().IndexOf("</noparse>", System.StringComparison.Ordinal));
        }

        [Test]
        public void OrdinaryTextSurvivesUntouched()
        {
            Assert.AreEqual("hello there", ChatText.Sanitize("  hello there  "));
        }
    }

    public class ChatLogTests
    {
        [SetUp]
        public void ClearLog() => ChatLog.Clear();

        [TearDown]
        public void ClearLogAfter() => ChatLog.Clear();

        [Test]
        public void EmptyMessagesAreNotStored()
        {
            ChatLog.AddPlayer("Ferdinand", string.Empty);
            Assert.AreEqual(0, ChatLog.Count);
        }

        [Test]
        public void MessagesArriveInOrder()
        {
            ChatLog.AddPlayer("A", "first");
            ChatLog.AddSystem("second");
            ChatLog.AddNotice("third");

            Assert.AreEqual(3, ChatLog.Count);
            Assert.AreEqual("first", ChatLog.Messages[0].Text);
            Assert.AreEqual(ChatKind.System, ChatLog.Messages[1].Kind);
            Assert.AreEqual(ChatKind.Notice, ChatLog.Messages[2].Kind);
        }

        [Test]
        public void OldestIsDroppedAtCapacity()
        {
            for (int i = 0; i < ChatLog.Capacity + 10; i++)
                ChatLog.AddPlayer("A", $"line {i}");

            Assert.AreEqual(ChatLog.Capacity, ChatLog.Count);
            Assert.AreEqual("line 10", ChatLog.Messages[0].Text);
            Assert.AreEqual($"line {ChatLog.Capacity + 9}", ChatLog.Messages[ChatLog.Count - 1].Text);
        }

        [Test]
        public void OnlyPlayerMessagesCarryASender()
        {
            ChatLog.AddPlayer("Ferdinand", "hi");
            ChatLog.AddSystem("Ferdinand joined the game.");

            Assert.IsTrue(ChatLog.Messages[0].HasSender);
            Assert.IsFalse(ChatLog.Messages[1].HasSender);
        }
    }

    public class ChatCommandTests
    {
        private const ulong Sender = 3;

        [SetUp]
        public void UseAnEmptyTable() => ChatCommands.Clear();

        [TearDown]
        public void RestoreTheTable() => ChatCommands.Clear();

        [Test]
        public void OnlySlashPrefixedLinesAreCommands()
        {
            Assert.IsTrue(ChatCommands.IsCommand("/tp bob"));
            Assert.IsFalse(ChatCommands.IsCommand("tp bob"));
            Assert.IsFalse(ChatCommands.IsCommand(string.Empty));
            Assert.IsFalse(ChatCommands.IsCommand(null));
        }

        [Test]
        public void ParseSplitsNameFromArguments()
        {
            Assert.IsTrue(ChatCommands.TryParse("/tp  Ferdinand  now ", out string name, out string[] args));

            Assert.AreEqual("tp", name);
            Assert.AreEqual(new[] { "Ferdinand", "now" }, args);
        }

        [Test]
        public void ParseLowercasesTheName()
        {
            Assert.IsTrue(ChatCommands.TryParse("/TP bob", out string name, out _));
            Assert.AreEqual("tp", name);
        }

        [Test]
        public void ABareSlashIsNotAnError()
        {
            // What the field holds for the whole moment between typing / and the first letter.
            Assert.IsFalse(ChatCommands.TryParse("/", out _, out _));
            Assert.IsFalse(ChatCommands.TryParse("/   ", out _, out _));
        }

        [Test]
        public void AliasesResolveToTheSameCommand()
        {
            ChatCommands.Register("tp", "/tp <player>", "Teleport.", (s, a) => "ran", "teleport");

            Assert.AreEqual("ran", ChatCommands.Execute(Sender, "/tp bob"));
            Assert.AreEqual("ran", ChatCommands.Execute(Sender, "/teleport bob"));
        }

        [Test]
        public void ReRegisteringReplacesRatherThanDuplicates()
        {
            ChatCommands.Register("tp", "/tp", "One.", (s, a) => "one");
            ChatCommands.Register("tp", "/tp", "Two.", (s, a) => "two");

            Assert.AreEqual(1, ChatCommands.All.Count);
            Assert.AreEqual("two", ChatCommands.Execute(Sender, "/tp"));
        }

        [Test]
        public void UnknownCommandsAreReported()
        {
            StringAssert.Contains("Unknown command", ChatCommands.Execute(Sender, "/nope"));
        }

        [Test]
        public void TheSenderIsPassedThrough()
        {
            ulong seen = 0;
            ChatCommands.Register("who", "/who", "Who.", (s, a) => { seen = s; return "ok"; });

            ChatCommands.Execute(Sender, "/who");

            Assert.AreEqual(Sender, seen);
        }

        [Test]
        public void ArgumentsAreEmptyRatherThanNull()
        {
            string[] seen = null;
            ChatCommands.Register("bare", "/bare", "Bare.", (s, a) => { seen = a; return "ok"; });

            ChatCommands.Execute(Sender, "/bare");

            Assert.IsNotNull(seen);
            Assert.AreEqual(0, seen.Length);
        }

        [Test]
        public void AThrowingCommandIsReportedNotPropagated()
        {
            // A command is player input, and player input must not be able to take down the
            // server's message pump.
            ChatCommands.Register("boom", "/boom", "Boom.",
                (s, a) => throw new System.InvalidOperationException("bang"));

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex(@"\[Chat\] Command 'boom' threw"));

            StringAssert.Contains("failed", ChatCommands.Execute(Sender, "/boom"));
        }
    }
}
