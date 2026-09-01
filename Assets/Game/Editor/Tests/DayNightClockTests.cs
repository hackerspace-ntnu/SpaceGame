// What the sky agrees on.
//
// Time of day used to be an accumulator of Time.deltaTime, which is a per-machine quantity: it is
// correct on exactly one computer and silently wrong on every other one in the session. Nothing
// about that shows up in single player, and in multiplayer it shows up as an argument about
// whether it is night.
//
// The property worth protecting is that the hour is a pure FUNCTION of a clock everyone shares, so
// two machines that never speak still agree — including a machine that only joins hours in. That
// is checked below by evaluating two independently built cycles against one clock reading, which
// is exactly what a host and a late joiner do.
//
// The one hole in that argument is a host that LOADED a save: its anchor is an hour off a file no
// client has read, and arithmetic cannot recover it. So the anchor itself is replicated, and the
// second half of this file checks the two numbers that make that work — the phase AND the clock
// reading it was taken at, which are only meaningful together.
// No `using System;` here on purpose: this file calls Object.DestroyImmediate and
// Object.FindFirstObjectByType, and importing System makes `Object` ambiguous between
// UnityEngine.Object and System.Object. System.Action below is spelled out for that reason.
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Netcode;
using SpaceGame.Core.Persistence;
using SpaceGame.World;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Tests
{
    public class DayNightClockTests
    {
        private const float Cycle = 2400f;
        private const float Authored = 0.25f;

        private GameObject sun;

        /// <summary>Anything hung on <see cref="DayNightCycle.AnchorMoved"/> by a test.</summary>
        private System.Action<DayNightCycle> announcementListener;

        [TearDown]
        public void TearDown()
        {
            // Static, so a listener left behind outlives the test that made it and is handed a
            // destroyed cycle by the next one.
            if (announcementListener != null)
            {
                DayNightCycle.AnchorMoved -= announcementListener;
                announcementListener = null;
            }

            if (sun != null) Object.DestroyImmediate(sun);
            sun = null;
        }

        /// <summary>
        /// A cycle with no Light and no Start.
        ///
        /// Unity does not raise Awake or Start outside play mode, which is the point: the hour has
        /// to be computable from the anchor and the clock alone, with no frame ever having run. A
        /// cycle that only worked once Update had been ticking would be an accumulator again.
        /// </summary>
        private DayNightCycle NewCycle(string name)
        {
            if (sun == null) sun = new GameObject("sky-fixture");

            var go = new GameObject(name);
            go.transform.SetParent(sun.transform, false);

            var cycle = go.AddComponent<DayNightCycle>();
            cycle.cycleDuration = Cycle;
            cycle.startTimeOfDay = Authored;
            return cycle;
        }

        // ─────────── The shared function ───────────

        [Test]
        public void AnchorPinsTheAuthoredHourToItsClockReading()
        {
            DayNightCycle cycle = NewCycle("sun");
            cycle.AnchorTo(Authored, 0d);

            Assert.AreEqual(Authored, cycle.PhaseAt(0d), 1e-5f);
        }

        [Test]
        public void HalfACycleIsHalfADayLater()
        {
            DayNightCycle cycle = NewCycle("sun");
            cycle.AnchorTo(Authored, 0d);

            Assert.AreEqual(0.75f, cycle.PhaseAt(Cycle * 0.5f), 1e-5f);
        }

        [Test]
        public void TheDayWrapsAtMidnight()
        {
            DayNightCycle cycle = NewCycle("sun");
            cycle.AnchorTo(0.9f, 0d);

            // 0.9 + 0.2 is 1.1, which is a tenth past the next midnight, not an eleventh of a day.
            Assert.AreEqual(0.1f, cycle.PhaseAt(Cycle * 0.2f), 1e-5f);
        }

        [Test]
        public void AHostAndALateJoinerDeriveTheSameHour()
        {
            // Two machines, each anchoring the authored hour to the session's own origin — which is
            // what AnchorToClockOrigin does inside a session, on whichever frame that machine
            // happens to wake up. No message passes between them; the agreement is arithmetic.
            DayNightCycle host = NewCycle("host-sun");
            DayNightCycle joiner = NewCycle("joiner-sun");

            host.AnchorTo(Authored, 0d);
            joiner.AnchorTo(Authored, 0d);

            // Four hours and ten minutes of real time into the session, when the joiner arrives —
            // deliberately not a whole number of days, or the assertion below would be vacuous.
            const double joinedAt = 15000d;

            Assert.AreEqual(host.PhaseAt(joinedAt), joiner.PhaseAt(joinedAt), 1e-6f,
                "The joiner must land on the host's hour, not on the hour the scene was authored with.");
            Assert.That(joiner.PhaseAt(joinedAt), Is.Not.EqualTo(Authored).Within(1e-3f),
                "A joiner that simply started at startTimeOfDay would pass the line above too.");
        }

        [Test]
        public void OfflineTheWorldOpensAtTheAuthoredHour()
        {
            // No NetworkManager: Network.IsNetworked is false, so the clock is game time counted
            // from process start and the anchor has to be taken now rather than at zero. Otherwise
            // however long the player sat in the main menu would already be daylight.
            DayNightCycle cycle = NewCycle("sun");
            cycle.AnchorToClockOrigin();

            Assert.AreEqual(Authored, cycle.PhaseAt(DayNightCycle.Now), 1e-3f);
        }

        [Test]
        public void MinutesSurviveALongRunningSession()
        {
            DayNightCycle cycle = NewCycle("sun");
            cycle.AnchorTo(Authored, 0d);

            // Hundreds of thousands of whole days sit in front of the fraction that matters, and a
            // float that wide has already lost the minutes. The wrap is deliberately taken in
            // double and only then narrowed.
            double clock = Cycle * 100000d + Cycle * 0.3d;

            // 0.55 is not a float at that magnitude — the nearest one is three thousandths of a day
            // away, which is four minutes of daylight lost to arithmetic.
            Assert.AreEqual(0.55f, cycle.PhaseAt(clock), 1e-4f);
        }

        [Test]
        public void AZeroLengthDayDoesNotProduceNonsense()
        {
            DayNightCycle cycle = NewCycle("sun");
            cycle.cycleDuration = 0f;
            cycle.AnchorTo(Authored, 0d);

            // A designer typing 0 into the inspector must not hand the sun a NaN rotation.
            Assert.IsFalse(float.IsNaN(cycle.PhaseAt(10d)));
            Assert.IsFalse(float.IsInfinity(cycle.PhaseAt(10d)));
        }

        // ─────────── Save and restore ───────────

        [Test]
        public void RestoringPutsTheWorldBackAtTheSavedHour()
        {
            DayNightCycle cycle = NewCycle("sun");
            cycle.AnchorTo(Authored, 0d);

            cycle.RestoreTimeOfDay(0.8f);

            Assert.AreEqual(0.8f, cycle.TimeOfDay, 1e-3f);
        }

        [Test]
        public void AFrozenCycleIgnoresARestore()
        {
            DayNightCycle cycle = NewCycle("sun");
            cycle.AnchorTo(Authored, 0d);
            cycle.freezeCycle = true;

            cycle.RestoreTimeOfDay(0.8f);

            // The main menu's sun is frozen so the light never drifts into night. A save must not
            // be the one thing that swings it.
            Assert.AreEqual(Authored, cycle.PhaseAt(0d), 1e-5f);
        }

        [Test]
        public void TheAdapterRoundTripsTheHourThroughJson()
        {
            DayNightCycle cycle = NewCycle("sun");
            var saver = cycle.gameObject.AddComponent<DayNightSaveable>();

            cycle.AnchorTo(0.63f, 0d);
            // Anchored at clock zero, so the hour read back depends on the live clock — capture and
            // restore around the same reading rather than asserting an absolute.
            float captured = cycle.TimeOfDay;

            var written = JObject.FromObject(saver.CaptureState());
            Assert.IsNotNull(written["timeOfDay"], "The adapter must write the key it reads back.");

            cycle.AnchorTo(0.1f, 0d);
            saver.RestoreState(written);

            Assert.AreEqual(captured, cycle.TimeOfDay, 1e-3f);
        }

        [Test]
        public void TheAdapterSurvivesAWorldWithNoSky()
        {
            if (Object.FindFirstObjectByType<DayNightCycle>() != null)
                Assert.Ignore("The editor's open scene already has a sky; this test needs one without.");

            sun = new GameObject("no-sky");
            var saver = sun.AddComponent<DayNightSaveable>();

            // Null stores nothing, which is the honest answer — better than a zero that a later
            // load would read back as midnight.
            Assert.IsNull(saver.CaptureState());
            Assert.DoesNotThrow(() => saver.RestoreState(new JObject { ["timeOfDay"] = 0.5f }));
        }

        [Test]
        public void TheSaveKeyIsStable()
        {
            // It is written into save files, so renaming it orphans every world already saved.
            Assert.AreEqual("sky", DayNightSaveable.Key);
        }

        // ─────────── The replicated anchor ───────────
        //
        // Everything below is the load-a-save case. A host that loaded one has an hour no client can
        // derive, because SaveManager restores on the server only; the anchor therefore crosses the
        // wire once. These tests build the two machines as two cycles and hand the pair between them
        // by hand, which is exactly what SkyNetwork does with a NetworkVariable — and it is checkable
        // with no NetworkManager at all, which is the state an EditMode test and a scene opened
        // straight from the editor are both in.

        [Test]
        public void AJoinerToldTheHostsLoadedHourDerivesThatHourAndNotTheAuthoredOne()
        {
            DayNightCycle host = NewCycle("host-sun");
            DayNightCycle joiner = NewCycle("joiner-sun");

            // The host opens the world at the authored hour, then a save puts it at dusk. The clock
            // reading is the session's, which is the only thing that makes the pair portable.
            host.AnchorTo(Authored, 0d);
            host.AdoptAnchor(0.78f, 1500d);

            host.ReadAnchor(out float phase, out double clock);
            joiner.AdoptAnchor(phase, clock);

            // An hour and a half of session later, when the joiner is actually looking at the sky.
            const double later = 6900d;

            Assert.AreEqual(host.PhaseAt(later), joiner.PhaseAt(later), 1e-6f,
                "The joiner has to land on the hour the host LOADED, not the one the scene was authored with.");

            // What a client derived before the anchor was replicated: the authored hour pinned to
            // the session origin. If that happened to match, the assertion above would be vacuous.
            DayNightCycle unfixed = NewCycle("joiner-sun-without-the-anchor");
            unfixed.AnchorTo(Authored, 0d);

            Assert.That(unfixed.PhaseAt(later), Is.Not.EqualTo(joiner.PhaseAt(later)).Within(1e-3f),
                "This is the bug: deriving the authored hour in a world that was loaded at dusk.");
        }

        [Test]
        public void TheClockReadingHasToTravelWithThePhase()
        {
            DayNightCycle host = NewCycle("host-sun");
            host.AnchorTo(Authored, 0d);

            // What the host reads deep into a session.
            const double takenAt = 1500d;
            float hour = host.PhaseAt(takenAt);

            // A joiner told only the hour, and applying it against its own idea of "now" — which is
            // what sending a single float would amount to.
            DayNightCycle phaseOnly = NewCycle("joiner-phase-only");
            phaseOnly.AdoptAnchor(hour, 0d);

            // A joiner told both numbers.
            DayNightCycle both = NewCycle("joiner-both");
            both.AdoptAnchor(hour, takenAt);

            const double later = 5000d;

            Assert.AreEqual(host.PhaseAt(later), both.PhaseAt(later), 1e-6f);
            Assert.That(phaseOnly.PhaseAt(later), Is.Not.EqualTo(host.PhaseAt(later)).Within(1e-3f),
                "A phase without the reading it was measured at is a different hour on every machine that applies it.");
        }

        [Test]
        public void AnAdoptedAnchorSurvivesTheCyclesOwnStart()
        {
            DayNightCycle joiner = NewCycle("joiner-sun");

            // The order a client actually sees: SkyNetwork hands over the session's anchor from
            // OnEnable, and only then does this component's Start run.
            joiner.AdoptAnchor(0.78f, 1500d);
            joiner.BeginDay();

            Assert.AreEqual(0.78f, joiner.PhaseAt(1500d), 1e-5f,
                "Start re-deriving the authored hour is what put a loaded world back at the morning.");
        }

        [Test]
        public void ARestoredHourStillSurvivesTheCyclesOwnStart()
        {
            DayNightCycle cycle = NewCycle("sun");

            cycle.RestoreTimeOfDay(0.8f);
            float restored = cycle.TimeOfDay;
            cycle.BeginDay();

            Assert.AreEqual(restored, cycle.TimeOfDay, 1e-3f);
        }

        [Test]
        public void WithNobodyToSayOtherwiseTheWorldOpensAtTheAuthoredHour()
        {
            DayNightCycle cycle = NewCycle("sun");

            // No save, no session, no NetworkManager — a scene opened straight from the editor. The
            // authored hour is still the answer, and nothing above may have made that path
            // conditional on netcode existing.
            cycle.BeginDay();

            Assert.AreEqual(Authored, cycle.PhaseAt(DayNightCycle.Now), 1e-3f);
        }

        [Test]
        public void AFrozenCycleIgnoresAReplicatedAnchor()
        {
            DayNightCycle cycle = NewCycle("menu-sun");
            cycle.AnchorTo(Authored, 0d);
            cycle.freezeCycle = true;

            cycle.AdoptAnchor(0.8f, 1500d);

            // The main menu's sun is frozen so the light never drifts into night. A session coming
            // up underneath it must not be the thing that swings it, same as a save.
            Assert.AreEqual(Authored, cycle.PhaseAt(0d), 1e-5f);
        }

        [Test]
        public void AnUnsetAnchorIsNotSomethingToPublish()
        {
            DayNightCycle cycle = NewCycle("sun");

            // Before anything states an hour the anchor is two zeroes. Publishing that would tell
            // every client in the session, with the server's full authority, that it is midnight.
            Assert.IsFalse(cycle.HasAnchor);

            cycle.AnchorTo(Authored, 0d);
            Assert.IsTrue(cycle.HasAnchor);
        }

        [Test]
        public void ReadingBackTheAnchorGivesExactlyWhatWasStated()
        {
            DayNightCycle cycle = NewCycle("sun");
            cycle.AnchorTo(0.6375f, 4321.5d);

            cycle.ReadAnchor(out float phase, out double clock);

            // Exact, not approximate: this pair is what goes on the wire, and every value ever
            // stored arrived as a float, so narrowing the double back cannot lose anything.
            Assert.AreEqual(0.6375f, phase);
            Assert.AreEqual(4321.5d, clock);
        }

        [Test]
        public void MovingTheAnchorIsAnnounced()
        {
            DayNightCycle cycle = NewCycle("sun");

            int announcements = 0;
            DayNightCycle announced = null;

            announcementListener = c => { announcements++; announced = c; };
            DayNightCycle.AnchorMoved += announcementListener;

            cycle.AnchorTo(0.4f, 100d);

            // The server's only cue to replicate. Without it a save loaded after SkyNetwork spawned
            // would never reach anybody.
            Assert.AreEqual(1, announcements);
            Assert.AreSame(cycle, announced);

            cycle.AdoptAnchor(0.9f, 200d);
            Assert.AreEqual(2, announcements, "A restore moves the anchor too, and is the case that matters most.");
        }

        // ─────────── What actually goes on the wire ───────────

        [Test]
        public void ADefaultAnchorIsNotAStatement()
        {
            // The value every peer holds before the server has said anything. Phase 0 at clock 0 is
            // a perfectly plausible midnight, so the flag is the only thing separating "nobody has
            // spoken" from "it is midnight" — and a client that could not tell them apart would jump
            // to midnight the moment it joined.
            Assert.IsFalse(default(SkyAnchor).Stated);
        }

        [Test]
        public void TheAnchorRoundTripsOverTheWire()
        {
            var sent = new SkyAnchor { Stated = true, Phase = 0.8125f, Clock = 91234.5d };

            using (var writer = new FastBufferWriter(64, Allocator.Temp))
            {
                writer.WriteNetworkSerializable(sent);

                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    reader.ReadNetworkSerializable(out SkyAnchor received);

                    Assert.IsTrue(received.Stated);
                    Assert.AreEqual(sent.Phase, received.Phase);

                    // The reading is a double on purpose. A session left running puts hundreds of
                    // whole cycles in front of the fraction that matters, and a float that wide has
                    // already lost the minutes — narrowing it for the wire would put the bug back.
                    Assert.AreEqual(sent.Clock, received.Clock);
                }
            }
        }

        [Test]
        public void TwoAnchorsAtTheSameHourButDifferentClocksAreNotTheSameStatement()
        {
            var first = new SkyAnchor { Stated = true, Phase = 0.5f, Clock = 100d };
            var second = new SkyAnchor { Stated = true, Phase = 0.5f, Clock = 4900d };

            // Netcode compares old against new before it marks the variable dirty. An equality that
            // ignored the clock would silently drop the second statement — and those two describe
            // completely different days: one is noon now, the other was noon two days ago.
            Assert.IsFalse(first.Equals(second));
            Assert.IsTrue(first.Equals(new SkyAnchor { Stated = true, Phase = 0.5f, Clock = 100d }),
                "An identical re-statement must compare equal, or every announcement costs a packet.");
        }
    }
}
