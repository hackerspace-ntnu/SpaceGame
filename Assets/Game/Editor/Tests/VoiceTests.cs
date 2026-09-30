using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using SpaceGame.Presentation;
using SpaceGame.Voice;
using Vector3 = UnityEngine.Vector3;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The parts of proximity voice that need neither a microphone nor a running FMOD: the noise
    /// gate, the level scale both it and the settings page are expressed in, the framing constants
    /// every machine in a session has to agree on, and an actual Opus round trip through Concentus.
    /// <para>
    /// The round trip is the one that earns its place. Concentus is a vendored managed port of
    /// libopus compiled into its own assembly, and "does the codec work at all in this Unity
    /// version" is otherwise a question only answerable by plugging in a headset and listening.
    /// </para>
    /// </summary>
    public class VoiceTests
    {
        // ------------------------------------------------------------------- framing

        [Test]
        public void TheFrameIsADurationOpusActuallyAccepts()
        {
            // Opus encodes 2.5, 5, 10, 20, 40, 60, 80, 100 or 120 ms and nothing else. A frame of
            // any other length is refused at runtime, so this is not a free dial to turn.
            int[] legal = { 5, 10, 20, 40, 60, 80, 100, 120 };
            CollectionAssert.Contains(legal, VoiceFormat.FrameMilliseconds);

            Assert.AreEqual(VoiceFormat.SampleRate / 1000 * VoiceFormat.FrameMilliseconds,
                            VoiceFormat.FrameSamples);
        }

        [Test]
        public void ThePacketCeilingCoversTheWorstCaseFrame()
        {
            // The largest frame the encoder can legally produce, plus Opus's own overhead. If this
            // ever fails, encoded audio is being silently truncated at the top bitrate.
            int worstCase = VoiceFormat.MaxBitrate / 8 * VoiceFormat.FrameMilliseconds / 1000;
            Assert.Greater(VoiceFormat.MaxPacketBytes, worstCase);
        }

        // ---------------------------------------------------------------------- level

        [Test]
        public void PeakReadsSilenceAsZeroAndFullScaleAsOne()
        {
            Assert.AreEqual(0f, VoiceGate.Peak(new short[64], 64), 0.0001f);
            Assert.AreEqual(1f, VoiceGate.Peak(new short[] { short.MaxValue }, 1), 0.0001f);
        }

        [Test]
        public void PeakSurvivesTheMostNegativeSampleThereIs()
        {
            // Negating short.MinValue does not fit back into a short. Done carelessly this reads
            // as a large NEGATIVE peak, and the gate then never opens on the loudest audio there is.
            float peak = VoiceGate.Peak(new short[] { short.MinValue }, 1);

            Assert.GreaterOrEqual(peak, 0f);
            Assert.LessOrEqual(peak, 1f);
            Assert.AreEqual(1f, peak, 0.001f);
        }

        [Test]
        public void PeakLooksAtNoMoreSamplesThanItWasGiven()
        {
            // The capture buffer is reused and longer than the audio in it; reading past the count
            // measures the previous poll.
            Assert.AreEqual(0f, VoiceGate.Peak(new short[] { 0, short.MaxValue }, 1), 0.0001f);
        }

        // ----------------------------------------------------------------------- gate

        [Test]
        public void TheGateOpensOnLevelAndStaysShutBelowIt()
        {
            var gate = new VoiceGate();

            Assert.IsFalse(gate.Step(0.02f, 0.1f, 0.06f), "a quiet room opened the gate");
            Assert.IsTrue(gate.Step(0.50f, 0.1f, 0.06f), "speech did not open the gate");
            Assert.IsTrue(gate.IsOpen);
        }

        [Test]
        public void TheGateHoldsThroughAPauseAndThenCloses()
        {
            // The hold is what makes a phrase one continuous transmission instead of one per
            // syllable — and it is why the tail of a word is not chopped off.
            var gate = new VoiceGate();
            gate.Step(0.5f, 0.1f, 0.06f);

            Assert.IsTrue(gate.Step(0f, 0.1f, VoiceGate.HoldSeconds * 0.5f), "the gate shut mid-phrase");
            Assert.IsFalse(gate.Step(0f, 0.1f, VoiceGate.HoldSeconds * 0.6f), "the gate never shut");
            Assert.IsFalse(gate.IsOpen);
        }

        [Test]
        public void ALevelHoveringJustUnderTheThresholdDoesNotChatterTheGate()
        {
            // Without the release margin a voice sitting right on the line opens and shuts the gate
            // repeatedly, which is audible as stuttering rather than as a clean cut-off.
            var gate = new VoiceGate();
            gate.Step(0.5f, 0.1f, 0.06f);

            float justUnder = 0.1f * VoiceGate.ReleaseMargin + 0.001f;

            for (int i = 0; i < 20; i++)
                Assert.IsTrue(gate.Step(justUnder, 0.1f, 1f), "the gate chattered shut on iteration " + i);
        }

        [Test]
        public void ResetShutsTheGateAtOnce()
        {
            var gate = new VoiceGate();
            gate.Step(0.5f, 0.1f, 0.06f);
            Assert.IsTrue(gate.IsOpen);

            gate.Reset();
            Assert.IsFalse(gate.IsOpen);
        }

        // ---------------------------------------------------------------------- codec

        [Test]
        public void TheCodecRoundTripsAudioWithItsLevelIntact()
        {
            using var encoder = new VoiceEncoder();
            using var decoder = new VoiceDecoder();

            var packet = new byte[VoiceFormat.MaxPacketBytes];
            var decoded = new short[VoiceFormat.FrameSamples];
            short[] input = null;

            // Several frames, because Opus has a few milliseconds of algorithmic delay: the first
            // frame out is mostly the encoder filling its lookahead, and judging a working codec
            // on it would fail it.
            for (int frame = 0; frame < 10; frame++)
            {
                input = Tone(VoiceFormat.FrameSamples, 220f, 0.5f, frame * VoiceFormat.FrameSamples);

                int bytes = encoder.Encode(input, packet);
                Assert.Greater(bytes, 0, "the encoder produced nothing");
                Assert.LessOrEqual(bytes, VoiceFormat.MaxPacketBytes, "a packet overran the buffer");

                Assert.AreEqual(VoiceFormat.FrameSamples, decoder.Decode(packet, bytes, decoded),
                                "the decoder returned a different number of samples than a frame holds");
            }

            // A tolerance, not an equality: Opus is lossy and a sine is not what it is tuned for.
            // What this pins is that audio comes out at roughly the level that went in — silence
            // and garbage both fail it.
            double inputLevel = Rms(input);
            double outputLevel = Rms(decoded);

            Assert.Greater(outputLevel, inputLevel * 0.2, "the decoder returned near-silence");
            Assert.Less(outputLevel, inputLevel * 3.0, "the decoder returned something far too loud");
        }

        [Test]
        public void ADroppedPacketIsConcealedRatherThanLeavingAHole()
        {
            using var decoder = new VoiceDecoder();
            var decoded = new short[VoiceFormat.FrameSamples];

            // Length zero is how a frame that never arrived is handed to Opus: it interpolates from
            // what it last heard. Voice rides an unreliable channel, so this path is the normal
            // case under any packet loss, not an error path.
            Assert.AreEqual(VoiceFormat.FrameSamples, decoder.Decode(null, 0, decoded));
        }

        [Test]
        public void AnUndersizedBufferIsRefusedRatherThanOverrun()
        {
            using var encoder = new VoiceEncoder();

            // The encoder is handed a reused array every frame; a short one means the framing
            // upstream is wrong, and encoding it anyway would send a frame of somebody's stale audio.
            Assert.AreEqual(0, encoder.Encode(new short[VoiceFormat.FrameSamples - 1],
                                              new byte[VoiceFormat.MaxPacketBytes]));
        }

        // --------------------------------------------------------------------- routing

        private static readonly Dictionary<ulong, Vector3> NoBodies = new Dictionary<ulong, Vector3>();

        [Test]
        public void ASpeakerIsNeverRoutedBackToThemselves()
        {
            // Hearing your own voice a network round trip later is the most disorienting thing a
            // voice system can do, and on a host the relay passes through the sender's own machine.
            var listeners = new List<ulong>();
            VoiceRouter.Proximity(new ulong[] { 0, 1, 2 }, 1, NoBodies, 30f, listeners);

            CollectionAssert.AreEquivalent(new ulong[] { 0, 2 }, listeners);
        }

        [Test]
        public void TheListenerListIsClearedBeforeItIsFilled()
        {
            // It is reused every frame rather than reallocated; a stale entry would keep sending
            // one player's voice to somebody who left earshot — or left the session.
            var listeners = new List<ulong> { 99 };
            VoiceRouter.Proximity(new ulong[] { 0, 1 }, 0, NoBodies, 30f, listeners);

            CollectionAssert.AreEquivalent(new ulong[] { 1 }, listeners);
        }

        [Test]
        public void RoutingWithNobodyConnectedProducesNoListeners()
        {
            var listeners = new List<ulong> { 99 };
            VoiceRouter.Proximity(null, 0, NoBodies, 30f, listeners);

            CollectionAssert.IsEmpty(listeners);
        }

        [Test]
        public void InTheLobbyEveryoneHearsEveryone()
        {
            // No bodies at all is the lobby. Null is treated the same, so a caller that has not
            // gathered positions yet errs towards being heard rather than towards silence.
            var listeners = new List<ulong>();

            VoiceRouter.Proximity(new ulong[] { 0, 1, 2, 3 }, 2, NoBodies, 30f, listeners);
            CollectionAssert.AreEquivalent(new ulong[] { 0, 1, 3 }, listeners);

            VoiceRouter.Proximity(new ulong[] { 0, 1, 2, 3 }, 2, null, 30f, listeners);
            CollectionAssert.AreEquivalent(new ulong[] { 0, 1, 3 }, listeners);
        }

        [Test]
        public void OnlyListenersWithinRangeAreSentAVoice()
        {
            var bodies = new Dictionary<ulong, Vector3>
            {
                [0] = new Vector3(0f, 0f, 0f),     // the speaker
                [1] = new Vector3(10f, 0f, 0f),    // near
                [2] = new Vector3(0f, 0f, 45f),    // beyond range
                [3] = new Vector3(0f, 29.9f, 0f),  // just inside, straight up — distance is 3D
            };

            var listeners = new List<ulong>();
            VoiceRouter.Proximity(new ulong[] { 0, 1, 2, 3 }, 0, bodies, 30f, listeners);

            CollectionAssert.AreEquivalent(new ulong[] { 1, 3 }, listeners);
        }

        [Test]
        public void AListenerExactlyAtTheEdgeIsStillSent()
        {
            // Inclusive, so the edge is the last place a voice is heard rather than the first place
            // it is not — and a float landing precisely on it does not flicker in and out.
            var bodies = new Dictionary<ulong, Vector3>
            {
                [0] = Vector3.zero,
                [1] = new Vector3(30f, 0f, 0f),
            };

            var listeners = new List<ulong>();
            VoiceRouter.Proximity(new ulong[] { 0, 1 }, 0, bodies, 30f, listeners);

            CollectionAssert.AreEquivalent(new ulong[] { 1 }, listeners);
        }

        [Test]
        public void AListenerWithoutABodyHearsEveryone()
        {
            // Loading in, or between bodies: dropping them out of every conversation without a
            // word is worse than letting them hear from nowhere in particular for a moment.
            var bodies = new Dictionary<ulong, Vector3>
            {
                [0] = Vector3.zero,
                [1] = new Vector3(500f, 0f, 0f),
            };

            var listeners = new List<ulong>();
            VoiceRouter.Proximity(new ulong[] { 0, 1, 2 }, 0, bodies, 30f, listeners);

            CollectionAssert.AreEquivalent(new ulong[] { 2 }, listeners);
        }

        [Test]
        public void ASpeakerWithoutABodyIsHeardByEveryone()
        {
            var bodies = new Dictionary<ulong, Vector3>
            {
                [1] = Vector3.zero,
                [2] = new Vector3(500f, 0f, 0f),
            };

            var listeners = new List<ulong>();
            VoiceRouter.Proximity(new ulong[] { 0, 1, 2 }, 0, bodies, 30f, listeners);

            CollectionAssert.AreEquivalent(new ulong[] { 1, 2 }, listeners);
        }

        // ---------------------------------------------------------------------- tuning

        [Test]
        public void TheShippedVoiceTuningLoads()
        {
            // The asset is what keeps host routing and listener fading on the same numbers; if its
            // script reference ever breaks, voice silently falls back to built-in defaults.
            Assert.IsNotNull(UnityEngine.Resources.Load<VoiceTuning>(VoiceTuning.ResourcePath));
        }

        [Test]
        public void VoiceTuningDistancesAreAlwaysOrdered()
        {
            // FMOD needs min < max, and the host must send at least as far as a voice is audible,
            // or the fade would be cut off before it finishes.
            VoiceTuning tuning = UnityEngine.ScriptableObject.CreateInstance<VoiceTuning>();
            try
            {
                Assert.Less(tuning.FullVolumeDistance, tuning.SilentDistance);
                Assert.GreaterOrEqual(tuning.RouteDistance, tuning.SilentDistance);
                Assert.GreaterOrEqual(tuning.MouthHeight, 0f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tuning);
            }
        }

        // ----------------------------------------------------------------------- names

        [Test]
        public void TheIdentityTableRoundTripsIncludingNonAsciiNames()
        {
            // Æ, Ø and Å are not edge cases for a game made in Trondheim.
            var table = new Dictionary<ulong, VoiceRoster.Entry>
            {
                [0] = new VoiceRoster.Entry("Host", "a1B2c3D4e5"),
                [3] = new VoiceRoster.Entry("Øyvind", "Zz9_-yY8"),
                [7] = new VoiceRoster.Entry("Åse Ær", string.Empty),
            };
            var buffer = new byte[VoiceRoster.MaxEncodedBytes];

            int length = VoiceRoster.Encode(table, buffer);

            var decoded = new Dictionary<ulong, VoiceRoster.Entry>();
            Assert.IsTrue(VoiceRoster.Decode(buffer, length, decoded));
            CollectionAssert.AreEquivalent(table, decoded);
        }

        [Test]
        public void AMalformedTableIsRejectedWhole()
        {
            var table = new Dictionary<ulong, VoiceRoster.Entry>
            {
                [1] = new VoiceRoster.Entry("Alpha", "acct1"),
                [2] = new VoiceRoster.Entry("Bravo", "acct2"),
            };
            var buffer = new byte[VoiceRoster.MaxEncodedBytes];
            int length = VoiceRoster.Encode(table, buffer);

            var decoded = new Dictionary<ulong, VoiceRoster.Entry>();

            // Cut short inside the second entry: nothing may be applied, not even the first entry,
            // which was complete — a half-applied table pairs fresh identities with stale ones.
            Assert.IsFalse(VoiceRoster.Decode(buffer, length - 2, decoded));
            CollectionAssert.IsEmpty(decoded);

            // Trailing bytes mean the two ends disagree about the format.
            Assert.IsFalse(VoiceRoster.Decode(buffer, length + 1, decoded));
            CollectionAssert.IsEmpty(decoded);
        }

        [Test]
        public void ATableClaimingMoreEntriesThanAnySessionHoldsIsRejected()
        {
            var decoded = new Dictionary<ulong, VoiceRoster.Entry>();
            Assert.IsFalse(VoiceRoster.Decode(new byte[] { 0xFF, 0xFF }, 2, decoded));
        }

        [Test]
        public void AnAnnouncementRoundTripsNameAndAccount()
        {
            var buffer = new byte[VoiceRoster.MaxIdentityBytes];
            int length = VoiceRoster.EncodeIdentity("Kari", "UgsPlayer_42", buffer);

            Assert.IsTrue(VoiceRoster.DecodeIdentity(buffer, length, out VoiceRoster.Entry entry));
            Assert.AreEqual("Kari", entry.Name);
            Assert.AreEqual("UgsPlayer_42", entry.AccountId);
        }

        [Test]
        public void AccountIdsThatAreNotShapedLikeOneAreRefused()
        {
            // The id becomes part of a PlayerPrefs key on every machine that hears it, so anything
            // that is not letters, digits, '-' or '_' is refused outright — never "cleaned", since a
            // cleaned id could be somebody else's.
            Assert.AreEqual("a1B2-c3_D4", VoiceRoster.CleanAccountId("a1B2-c3_D4"));

            Assert.AreEqual(string.Empty, VoiceRoster.CleanAccountId("../../prefs"));
            Assert.AreEqual(string.Empty, VoiceRoster.CleanAccountId("has space"));
            Assert.AreEqual(string.Empty, VoiceRoster.CleanAccountId("dotted.id"));
            Assert.AreEqual(string.Empty, VoiceRoster.CleanAccountId(new string('a', VoiceRoster.MaxAccountBytes + 1)));
            Assert.AreEqual(string.Empty, VoiceRoster.CleanAccountId(null));
        }

        [Test]
        public void AHostileAccountIdInATableArrivesEmpty()
        {
            // Encode cleans what it writes, so a hostile table has to be built by hand — which is
            // exactly what a malicious peer would do. Decode must clean it again on the way in.
            byte[] name = Encoding.UTF8.GetBytes("Mallory");
            byte[] account = Encoding.ASCII.GetBytes("x/../y");

            var data = new List<byte> { 1, 0 };
            var id = new byte[VoiceFormat.ClientIdBytes];
            VoiceFormat.WriteClientId(id, 0, 5);
            data.AddRange(id);
            data.Add((byte)name.Length);
            data.AddRange(name);
            data.Add((byte)account.Length);
            data.AddRange(account);

            var decoded = new Dictionary<ulong, VoiceRoster.Entry>();
            Assert.IsTrue(VoiceRoster.Decode(data.ToArray(), data.Count, decoded));
            Assert.AreEqual("Mallory", decoded[5].Name);
            Assert.AreEqual(string.Empty, decoded[5].AccountId);
        }

        [Test]
        public void ALongNameIsCutOnACharacterBoundary()
        {
            // Every Å is two bytes of UTF-8. Cut blindly at the byte limit, the last one would split
            // and arrive as a replacement glyph.
            byte[] bytes = VoiceRoster.NameBytes(new string('Å', VoiceRoster.MaxNameBytes));

            Assert.LessOrEqual(bytes.Length, VoiceRoster.MaxNameBytes);
            StringAssert.DoesNotContain("\uFFFD", Encoding.UTF8.GetString(bytes));
        }

        [Test]
        public void ClientIdsSurviveTheWireAtTheirExtremes()
        {
            // Written at an offset, because in a Down message the id is not at the start of the
            // buffer it is read from, only at the start of the payload.
            var buffer = new byte[VoiceFormat.ClientIdBytes + 3];

            foreach (ulong id in new[] { 0UL, 1UL, 0x0123456789ABCDEFUL, ulong.MaxValue })
            {
                VoiceFormat.WriteClientId(buffer, 3, id);
                Assert.AreEqual(id, VoiceFormat.ReadClientId(buffer, 3));
            }
        }

        // ---------------------------------------------------------------------- levels

        [Test]
        public void APersonsLevelIsHeldBetweenSilenceAndDoubleVolume()
        {
            // Pure: Clamp never touches PlayerPrefs, so this cannot disturb the settings of whoever
            // is running the suite.
            Assert.AreEqual(0f, VoicePeerLevels.Clamp(-1f), 0.0001f);
            Assert.AreEqual(2f, VoicePeerLevels.Clamp(5f), 0.0001f);
            Assert.AreEqual(1.35f, VoicePeerLevels.Clamp(1.35f), 0.0001f);
            Assert.AreEqual(1f, VoicePeerLevels.DefaultGain, 0.0001f);
        }

        [Test]
        public void TheLevelCeilingIsTheMostPlaybackWillAmplify()
        {
            // Two constants that must not drift apart: a 200% slider over a playback that clamps at
            // 100% would be a slider whose top half does nothing.
            Assert.AreEqual(VoicePlayback.MaxVolume, VoicePeerLevels.MaxGain, 0.0001f);
            Assert.AreEqual(2f, VoicePeerLevels.MaxGain, 0.0001f);
        }

        // ---------------------------------------------------------------- push to talk

        private const float Frame = 1f / 60f;

        [Test]
        public void HoldToTalkIsOpenWhileTheKeyIsDown()
        {
            var latch = new PushToTalkLatch();

            Assert.IsFalse(latch.Step(false, false, false, Frame), "open with the key up");
            Assert.IsTrue(latch.Step(false, true, true, Frame), "shut on the press");
            Assert.IsTrue(latch.Step(false, false, true, Frame), "shut while held");
        }

        [Test]
        public void HoldToTalkKeepsASyllableOfTailAfterRelease()
        {
            // People let go a fraction before the word ends; without the tail the last syllable is
            // cut off, which is the complaint every push-to-talk without one gets.
            var latch = new PushToTalkLatch();
            latch.Step(false, true, true, Frame);

            Assert.IsTrue(latch.Step(false, false, false, PushToTalkLatch.ReleaseTailSeconds * 0.5f),
                          "the tail cut off the end of the word");
            Assert.IsFalse(latch.Step(false, false, false, PushToTalkLatch.ReleaseTailSeconds),
                           "the channel stayed open long after the key was released");
        }

        [Test]
        public void ToggleFlipsOnEachPressAndIgnoresHolding()
        {
            var latch = new PushToTalkLatch();

            Assert.IsTrue(latch.Step(true, true, true, Frame), "the first press did not open it");
            Assert.IsTrue(latch.Step(true, false, false, Frame), "it closed on release, like hold mode");
            Assert.IsFalse(latch.Step(true, true, true, Frame), "the second press did not close it");
            Assert.IsFalse(latch.Step(true, false, false, 5f), "a toggle should have no release tail");
        }

        [Test]
        public void SwitchingToHoldNeverLeavesALatchOpen()
        {
            // A latch left on after switching to hold mode would be a live microphone that no key
            // can now turn off.
            var latch = new PushToTalkLatch();
            latch.Step(true, true, true, Frame);
            Assert.IsTrue(latch.Latched);

            Assert.IsFalse(latch.Step(false, false, false, Frame));
            Assert.IsFalse(latch.Latched);
        }

        [Test]
        public void ResetClosesTheChannelAtOnce()
        {
            var latch = new PushToTalkLatch();
            latch.Step(true, true, true, Frame);

            latch.Reset();

            Assert.IsFalse(latch.Latched);
            Assert.IsFalse(latch.Step(false, false, false, 0f), "a hold tail survived the reset");
        }

        // ---------------------------------------------------------------------- keys

        [Test]
        public void ABindingNamesTheKeyNotTheParticularDevice()
        {
            Assert.AreEqual("<Keyboard>/v", KeyCapture.BindingPathOf("Keyboard", "/Keyboard", "/Keyboard/v"));
            Assert.AreEqual("<Mouse>/backButton",
                            KeyCapture.BindingPathOf("Mouse", "/Mouse", "/Mouse/backButton"));

            // A second keyboard is "/Keyboard1"; the binding must still be plain "<Keyboard>/space"
            // or it would only ever work on that one device.
            Assert.AreEqual("<Keyboard>/space",
                            KeyCapture.BindingPathOf("Keyboard", "/Keyboard1", "/Keyboard1/space"));
        }

        [Test]
        public void AnUnboundKeySaysSo()
        {
            Assert.AreEqual("UNBOUND", KeyCapture.Describe(string.Empty));
            Assert.AreEqual("UNBOUND", KeyCapture.Describe(null));
        }

        // ------------------------------------------------------------------- plumbing

        private static short[] Tone(int samples, float hz, float amplitude, int startSample)
        {
            var pcm = new short[samples];

            for (int i = 0; i < samples; i++)
            {
                double t = (startSample + i) / (double)VoiceFormat.SampleRate;
                pcm[i] = (short)(Math.Sin(2.0 * Math.PI * hz * t) * amplitude * short.MaxValue);
            }

            return pcm;
        }

        private static double Rms(short[] pcm)
        {
            double sum = 0;
            foreach (short sample in pcm) sum += (double)sample * sample;

            return Math.Sqrt(sum / Math.Max(1, pcm.Length));
        }
    }
}
