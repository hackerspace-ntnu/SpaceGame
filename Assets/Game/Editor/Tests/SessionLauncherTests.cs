using System.Net;
using NUnit.Framework;
using SpaceGame.Core;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The parts of <see cref="SessionLauncher"/> that can be checked without a live socket.
    ///
    /// Connecting itself needs two processes and is covered by playing the game, but the string
    /// handling around it does not — and that is where a join failed most often. A Relay code is
    /// read off a friend's screen and typed or pasted back, so it arrives lowercase, padded with a
    /// space, or both, and Relay rejects it verbatim with a generic "not found".
    /// </summary>
    public class SessionLauncherTests
    {
        [TestCase("abcdef", "ABCDEF")]
        [TestCase("  AbCdEf  ", "ABCDEF")]
        [TestCase("ABCDEF", "ABCDEF")]
        [TestCase("\tabc123\n", "ABC123")]
        public void NormalizeJoinCode_UppercasesAndTrims(string input, string expected)
        {
            Assert.AreEqual(expected, SessionLauncher.NormalizeJoinCode(input));
        }

        [Test]
        public void NormalizeJoinCode_TreatsNullAndBlankAsEmpty()
        {
            Assert.AreEqual(string.Empty, SessionLauncher.NormalizeJoinCode(null));
            Assert.AreEqual(string.Empty, SessionLauncher.NormalizeJoinCode(""));
        }

        [Test]
        public void NormalizeJoinCode_IsInvariantToCulture()
        {
            // ToUpper() in a Turkish locale maps 'i' to 'İ', which Relay does not recognise. The
            // implementation uses ToUpperInvariant for exactly this reason; this pins it down so a
            // later "simplification" to ToUpper() fails here rather than on a Turkish machine.
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("tr-TR");

                Assert.AreEqual("INVITE", SessionLauncher.NormalizeJoinCode("invite"));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void GetLocalIPv4_ReturnsAParseableAddress()
        {
            string ip = SessionLauncher.GetLocalIPv4();

            Assert.IsFalse(string.IsNullOrWhiteSpace(ip), "Direct Connect shows this to the player.");
            Assert.IsTrue(IPAddress.TryParse(ip, out IPAddress parsed), $"'{ip}' is not an IP address.");
            Assert.AreEqual(System.Net.Sockets.AddressFamily.InterNetwork, parsed.AddressFamily,
                "UnityTransport's direct path expects IPv4.");
        }

        [Test]
        public void SessionResult_OkCarriesNoError()
        {
            SessionResult result = SessionResult.Ok("JOIN42");

            Assert.IsTrue(result.Success);
            Assert.AreEqual("JOIN42", result.JoinCode);
            Assert.IsEmpty(result.Error);
        }

        [Test]
        public void SessionResult_FailCarriesAMessageAndNoJoinCode()
        {
            SessionResult result = SessionResult.Fail("Relay is unreachable.");

            Assert.IsFalse(result.Success);
            Assert.AreEqual("Relay is unreachable.", result.Error);
            Assert.IsEmpty(result.JoinCode);
        }

        [Test]
        public void SessionResult_NeverExposesNullStrings()
        {
            // The UI concatenates these straight into a label; a null here is a NullReferenceException
            // on the error path, which is the one path that must not throw.
            SessionResult ok = SessionResult.Ok();
            SessionResult fail = SessionResult.Fail(null);

            Assert.IsNotNull(ok.Error);
            Assert.IsNotNull(ok.JoinCode);
            Assert.IsNotNull(fail.Error);
            Assert.IsNotNull(fail.JoinCode);
        }
    }
}
