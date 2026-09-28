using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Voice;

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

        [Test]
        public void ASpeakerIsNeverRoutedBackToThemselves()
        {
            // Hearing your own voice a network round trip later is the most disorienting thing a
            // voice system can do, and on a host the relay passes through the sender's own machine.
            var listeners = new List<ulong>();
            VoiceRouter.Everyone(new ulong[] { 0, 1, 2 }, 1, listeners);

            CollectionAssert.AreEquivalent(new ulong[] { 0, 2 }, listeners);
        }

        [Test]
        public void TheListenerListIsClearedBeforeItIsFilled()
        {
            // It is reused every frame rather than reallocated; a stale entry would keep sending
            // one player's voice to somebody who left earshot — or left the session.
            var listeners = new List<ulong> { 99 };
            VoiceRouter.Everyone(new ulong[] { 0, 1 }, 0, listeners);

            CollectionAssert.AreEquivalent(new ulong[] { 1 }, listeners);
        }

        [Test]
        public void RoutingWithNobodyConnectedProducesNoListeners()
        {
            var listeners = new List<ulong> { 99 };
            VoiceRouter.Everyone(null, 0, listeners);

            CollectionAssert.IsEmpty(listeners);
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
