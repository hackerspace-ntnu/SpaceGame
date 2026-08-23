using NUnit.Framework;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    /// <summary>
    /// Covers the parts of the lobby's busy state that are logic rather than layout: the cadence
    /// the two busy animations run at, and the table deciding what each wait switches off.
    ///
    /// <para>
    /// The rest of it — the rects, the CanvasGroups, the sweeping rule actually moving — is not
    /// tested here and is verified by hand in the editor. Standing a canvas up in an EditMode test
    /// to assert that an anchored position is what the line above set it to would be a test of
    /// uGUI, not of this screen.
    /// </para>
    /// </summary>
    public class MenuBusyTests
    {
        // ────────────────────────────────────────────────────────────── the dots

        /// <summary>
        /// Sampled in the middle of each band rather than on its edge. A dot count is a floor of a
        /// division, so an assertion sitting exactly on a boundary is really an assertion about
        /// whether <c>0.36f * 3f / 0.36f</c> lands on 3 or on 2.9999998 — which is not the
        /// behaviour anyone here cares about.
        /// </summary>
        [Test]
        public void DotsStartEmptyAndBuildUp()
        {
            Assert.AreEqual(0, MenuBusy.DotCount(Midband(0)));
            Assert.AreEqual(1, MenuBusy.DotCount(Midband(1)));
            Assert.AreEqual(2, MenuBusy.DotCount(Midband(2)));
            Assert.AreEqual(3, MenuBusy.DotCount(Midband(3)));
        }

        /// <summary>
        /// The count wraps rather than saturating. A caption that reached three dots and stayed
        /// there would stop being an animation at the exact moment the wait got long enough to
        /// need one.
        /// </summary>
        [Test]
        public void DotsWrapBackToNone()
        {
            Assert.AreEqual(0, MenuBusy.DotCount(Midband(MenuBusy.MaxDots + 1)));
            Assert.AreEqual(1, MenuBusy.DotCount(Midband(MenuBusy.MaxDots + 2)));
        }

        /// <summary>A negative clock is clamped rather than throwing out of new string(char, int).</summary>
        [Test]
        public void DotsSurviveANegativeClock()
        {
            Assert.AreEqual(string.Empty, MenuBusy.DotSuffix(-5f));
        }

        [Test]
        public void DotSuffixMatchesTheCount()
        {
            Assert.AreEqual("..", MenuBusy.DotSuffix(Midband(2)));
        }

        /// <summary>The clock reading halfway through the band that shows <paramref name="dots"/>.</summary>
        private static float Midband(int dots) => MenuBusy.DotSeconds * (dots + 0.5f);

        // ───────────────────────────────────────────────────────────── the sweep

        /// <summary>
        /// The dash starts fully off the left edge and ends fully off the right one, so it is never
        /// seen to appear or to stop — which is the whole difference between a sweep and a bar.
        /// </summary>
        [Test]
        public void SweepEntersAndLeavesOffTheEnds()
        {
            const float track = 1000f;
            const float dash = 280f;

            Assert.AreEqual(-dash, MenuBusy.SweepOffset(0f, track, dash), 0.001f);
            Assert.AreEqual(track, MenuBusy.SweepOffset(MenuBusy.SweepSeconds * 0.999f, track, dash), 1f);
        }

        [Test]
        public void SweepRepeats()
        {
            const float track = 1000f;
            const float dash = 280f;

            Assert.AreEqual(MenuBusy.SweepOffset(0.3f, track, dash),
                            MenuBusy.SweepOffset(0.3f + MenuBusy.SweepSeconds, track, dash), 0.001f);
        }

        [Test]
        public void SweepStaysOnTheTrackThroughout()
        {
            const float track = 1000f;
            const float dash = 280f;

            for (float t = 0f; t < MenuBusy.SweepSeconds; t += 0.02f)
            {
                float x = MenuBusy.SweepOffset(t, track, dash);

                Assert.GreaterOrEqual(x, -dash - 0.001f, $"dash left the track at t={t}");
                Assert.LessOrEqual(x, track + 0.001f, $"dash left the track at t={t}");
            }
        }

        // ──────────────────────────────────────────────────── what a wait locks

        /// <summary>
        /// Nothing is switched off when nothing is happening. Worth pinning down because every
        /// other case in the table is a departure from it, and a page that came back from a wait
        /// with a control still dead would be the worst failure this state can have.
        /// </summary>
        [Test]
        public void IdleLocksNothing()
        {
            LobbyUI.BusyState state = LobbyUI.BusyState.For(LobbyUI.BusyScope.None);

            Assert.IsFalse(state.LockCodeColumn);
            Assert.IsFalse(state.LockBrowser);
            Assert.IsFalse(state.LockRefresh);
            Assert.IsFalse(state.OfferCancel);
        }

        /// <summary>
        /// Querying leaves the code field alone. There is no reason you cannot type a code while
        /// the list loads, and locking it would make the fast route wait on the slow one.
        /// </summary>
        [Test]
        public void QueryingLeavesTheCodeFieldAlone()
        {
            LobbyUI.BusyState state = LobbyUI.BusyState.For(LobbyUI.BusyScope.Querying);

            Assert.IsFalse(state.LockCodeColumn);
            Assert.IsTrue(state.LockBrowser, "stale rows must not be clickable mid-query");
            Assert.IsTrue(state.LockRefresh);
        }

        /// <summary>
        /// Every join locks the whole page. This is the fix for the original bug: a second click
        /// during a join hit LobbySession's one-at-a-time guard, which refuses silently, and the
        /// screen reported "could not join" over an attempt that was still succeeding.
        /// </summary>
        [Test]
        public void EveryJoinLocksTheWholePage()
        {
            foreach (LobbyUI.BusyScope scope in new[]
                     { LobbyUI.BusyScope.JoiningByCode, LobbyUI.BusyScope.JoiningRow })
            {
                LobbyUI.BusyState state = LobbyUI.BusyState.For(scope);

                Assert.IsTrue(state.LockCodeColumn, $"{scope} left the code column live");
                Assert.IsTrue(state.LockBrowser, $"{scope} left the browser live");
                Assert.IsTrue(state.LockRefresh, $"{scope} left Refresh live");
            }
        }

        /// <summary>
        /// Only a join offers Cancel. Signing in and querying have nothing to hand back if the
        /// player changes their mind, so Back already does everything cancelling them would — and
        /// replacing Back with a Cancel that did the same thing is a worse label for it.
        /// </summary>
        [Test]
        public void OnlyAJoinCanBeCancelled()
        {
            Assert.IsTrue(LobbyUI.BusyState.For(LobbyUI.BusyScope.JoiningByCode).OfferCancel);
            Assert.IsTrue(LobbyUI.BusyState.For(LobbyUI.BusyScope.JoiningRow).OfferCancel);

            Assert.IsFalse(LobbyUI.BusyState.For(LobbyUI.BusyScope.SigningIn).OfferCancel);
            Assert.IsFalse(LobbyUI.BusyState.For(LobbyUI.BusyScope.Querying).OfferCancel);
            Assert.IsFalse(LobbyUI.BusyState.For(LobbyUI.BusyScope.None).OfferCancel);
        }

        /// <summary>Signing in precedes everything, so nothing on the page is usable yet.</summary>
        [Test]
        public void SigningInLocksEverything()
        {
            LobbyUI.BusyState state = LobbyUI.BusyState.For(LobbyUI.BusyScope.SigningIn);

            Assert.IsTrue(state.LockCodeColumn);
            Assert.IsTrue(state.LockBrowser);
            Assert.IsTrue(state.LockRefresh);
        }
    }
}
