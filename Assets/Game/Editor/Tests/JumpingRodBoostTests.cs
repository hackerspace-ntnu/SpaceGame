using NUnit.Framework;
using UnityEngine;
using SpaceGame.Gear.JumpingRod;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The jumping rod's landing boost, as arithmetic: the timing window, the chain, and the
    /// heights the chain is supposed to reach.
    ///
    /// <para>
    /// All of it runs without a scene, a Rigidbody or a clock, which is the reason
    /// <see cref="JumpingRodChain"/> takes the time as an argument. A timing mechanic is otherwise
    /// only checkable by a human on a pogo stick — and a human can tell you whether it feels good,
    /// never whether a press 121 ms early is still a hit.
    /// </para>
    /// <para>
    /// The numbers below are the shipped defaults. They are checked against the class initialisers
    /// here and against the value actually serialized on the prefab in
    /// <see cref="JumpingRodWiringTests.Item_CarriesTheShippedBoostTuning"/> — a `[SerializeField]`
    /// keeps the number the asset was saved with, so testing the class alone proves nothing about
    /// what the game runs.
    /// </para>
    /// </summary>
    public class JumpingRodBoostTests
    {
        private static JumpingRodConfig Cfg() => new JumpingRodConfig();

        /// <summary>Height in metres a take-off speed reaches under this project's gravity.</summary>
        private static float HeightOf(float speed)
            => speed * speed / (2f * Mathf.Abs(Physics.gravity.y));

        // ── The ladder ─────────────────────────────────────────────────────────

        [Test]
        public void NoChain_IsTheHopTheRodAlwaysGave()
        {
            JumpingRodConfig cfg = Cfg();

            Assert.AreEqual(1f, JumpingRodHopModel.BoostFactor(0, cfg), 1e-4f,
                "an empty chain must multiply by exactly one, or every hop in the game changed");

            Assert.AreEqual(cfg.MinHopSpeed, JumpingRodHopModel.TakeoffSpeed(0f, cfg, 0), 1e-4f,
                "standing on the rod doing nothing still gives the cruise hop");
        }

        [Test]
        public void EachLink_CompoundsTheTakeoffSpeed()
        {
            JumpingRodConfig cfg = Cfg();

            for (int links = 0; links <= cfg.MaxChainLinks; links++)
                Assert.AreEqual(Mathf.Pow(cfg.BoostPerLink, links),
                                JumpingRodHopModel.BoostFactor(links, cfg), 1e-4f);
        }

        [Test]
        public void TheChain_CannotBuyMoreThanMaxChainLinks()
        {
            JumpingRodConfig cfg = Cfg();

            // The one thing standing between a compounding multiplier and the top of the world.
            Assert.AreEqual(JumpingRodHopModel.BoostFactor(cfg.MaxChainLinks, cfg),
                            JumpingRodHopModel.BoostFactor(cfg.MaxChainLinks + 40, cfg), 1e-4f);
        }

        [Test]
        public void TheBoost_LiftsTheCeilingAndNotJustTheFloor()
        {
            JumpingRodConfig cfg = Cfg();

            // Arriving at 40 m/s is far past what the rod hands back, so this is the clamp — and
            // the clamp has to move with the chain or the mechanic disappears above two links.
            float capped = JumpingRodHopModel.TakeoffSpeed(40f, cfg, 0);
            Assert.AreEqual(cfg.MaxHopSpeed, capped, 1e-3f);

            Assert.Greater(JumpingRodHopModel.TakeoffSpeed(40f, cfg, cfg.MaxChainLinks),
                           cfg.MaxHopSpeed + 1f);
        }

        /// <summary>
        /// The design claim, in metres: three and a bit times the cruise hop at the top of the
        /// chain. If gravity or the cruise speed is retuned this fails, which is the point — the
        /// heights are what was decided, the speeds are only how they are expressed.
        /// </summary>
        [Test]
        public void AFullChain_ReachesAboutTenMetres()
        {
            JumpingRodConfig cfg = Cfg();

            Assert.AreEqual(-18f, Physics.gravity.y, 0.01f,
                "these heights were chosen at -18 gravity; re-derive them before changing this");

            float cruise = HeightOf(JumpingRodHopModel.TakeoffSpeed(0f, cfg, 0));
            float top = HeightOf(JumpingRodHopModel.TakeoffSpeed(0f, cfg, cfg.MaxChainLinks));

            Assert.AreEqual(3.4f, cruise, 0.2f);
            Assert.AreEqual(10.4f, top, 0.6f);
        }

        // ── The window ─────────────────────────────────────────────────────────

        [Test]
        public void APressJustBeforeTouchdown_CountsAsAHit()
        {
            JumpingRodConfig cfg = Cfg();
            var chain = new JumpingRodChain(cfg);

            chain.Press(1f);
            Assert.AreEqual(1, chain.Land(1f + cfg.BoostWindowEarly - 0.001f));
            Assert.IsTrue(chain.OnBeat);
        }

        [Test]
        public void APressOlderThanTheWindow_IsAMiss()
        {
            JumpingRodConfig cfg = Cfg();
            var chain = new JumpingRodChain(cfg);

            chain.Press(1f);
            Assert.AreEqual(0, chain.Land(1f + cfg.BoostWindowEarly + 0.001f));
            Assert.IsFalse(chain.OnBeat);
        }

        [Test]
        public void NoPressAtAll_IsAMissToo()
        {
            var chain = new JumpingRodChain(Cfg());

            Assert.AreEqual(0, chain.Land(1f));
            Assert.IsFalse(chain.OnBeat, "cruising must not sound like a boosted hop");
        }

        [Test]
        public void APressJustAfterTouchdown_RescuesTheLanding()
        {
            JumpingRodConfig cfg = Cfg();
            var chain = new JumpingRodChain(cfg);

            Build(chain, 2, 0f);
            float landed = 10f;

            // Late, so the hop has already left the ground at the missed value...
            Assert.AreEqual(0, chain.Land(landed));

            // ...and the press inside the late half puts back what the miss took, plus the link.
            Assert.IsTrue(chain.Press(landed + cfg.BoostWindowLate - 0.001f));
            Assert.AreEqual(3, chain.Links);
            Assert.IsTrue(chain.OnBeat);
        }

        [Test]
        public void APressPastTheLateWindow_DoesNotRescueAnything()
        {
            JumpingRodConfig cfg = Cfg();
            var chain = new JumpingRodChain(cfg);

            Build(chain, 2, 0f);
            Assert.AreEqual(0, chain.Land(10f));

            Assert.IsFalse(chain.Press(10f + cfg.BoostWindowLate + 0.001f));
            Assert.AreEqual(0, chain.Links, "the miss stands; that press belongs to the next landing");
        }

        [Test]
        public void OnlyOnePressCanRescueALanding()
        {
            JumpingRodConfig cfg = Cfg();
            var chain = new JumpingRodChain(cfg);

            chain.Land(10f);
            Assert.IsTrue(chain.Press(10f + 0.01f));
            Assert.IsFalse(chain.Press(10f + 0.02f), "a landing pays out once");
            Assert.AreEqual(1, chain.Links);
        }

        // ── What stops it being automatic ──────────────────────────────────────

        /// <summary>
        /// The rule that makes this a skill rather than a habit. A buffer wide enough to be fair is
        /// wide enough to be hit by accident by anyone holding the key down and mashing — so the
        /// second press of a hop spoils that hop, however well the last one happens to be timed.
        /// </summary>
        [Test]
        public void MashingJump_LosesTheChainRatherThanBuyingIt()
        {
            JumpingRodConfig cfg = Cfg();
            var chain = new JumpingRodChain(cfg);

            Build(chain, 3, 0f);

            float land = 10f;
            for (float t = land - 0.5f; t < land; t += 0.06f) chain.Press(t);

            Assert.AreEqual(0, chain.Land(land));
            Assert.IsFalse(chain.OnBeat);
        }

        /// <summary>
        /// The other half of the same rule. A mash puts a press just after every touchdown as
        /// reliably as it puts one just before, so a spoiled hop must be closed at both ends or
        /// the late window hands back everything the early one refused.
        /// </summary>
        [Test]
        public void ASpoiledHop_CannotBeRescuedLateEither()
        {
            JumpingRodConfig cfg = Cfg();
            var chain = new JumpingRodChain(cfg);

            chain.Press(9.5f);
            chain.Press(9.6f);              // two presses in one hop: spoiled

            Assert.AreEqual(0, chain.Land(10f));
            Assert.IsFalse(chain.Press(10f + 0.01f));
            Assert.AreEqual(0, chain.Links);
        }

        [Test]
        public void OneMiss_CostsTheWholeChainAsShipped()
        {
            JumpingRodConfig cfg = Cfg();
            var chain = new JumpingRodChain(cfg);

            Build(chain, cfg.MaxChainLinks, 0f);
            Assert.AreEqual(cfg.MaxChainLinks, chain.Links);

            Assert.GreaterOrEqual(cfg.LinksLostOnMiss, cfg.MaxChainLinks,
                "the shipped tuning drops the whole chain; lower it only deliberately");
            Assert.AreEqual(0, chain.Land(100f));
        }

        [Test]
        public void PlantingTheRodAgain_StartsFromNothing()
        {
            var chain = new JumpingRodChain(Cfg());

            Build(chain, 3, 0f);
            chain.Reset();

            Assert.AreEqual(0, chain.Links);
            Assert.IsFalse(chain.OnBeat);

            // And the press that was pending when it was stowed is not waiting to be judged.
            Assert.AreEqual(0, chain.Land(0.05f));
        }

        // ── The tuning holds together ──────────────────────────────────────────

        [Test]
        public void TheLateWindow_ShutsBeforeAnotherBounceCanFire()
        {
            JumpingRodConfig cfg = Cfg();

            // A late press is paid into a body that is already rising from the hop it is topping
            // up. If the window outlasted the rebounce lockout it could pay into the NEXT hop
            // instead, and the boost would land on a landing nobody timed.
            Assert.Less(cfg.BoostWindowLate, cfg.RebounceLockout);
        }

        [Test]
        public void TheEarlyHalfOfTheWindow_IsTheWiderOne()
        {
            JumpingRodConfig cfg = Cfg();

            // GDC-L1-FEEL-0003: buffering an early press is invisible, paying out a late one is a
            // visible surge in mid-air. The forgiveness goes where it cannot be seen.
            Assert.Greater(cfg.BoostWindowEarly, cfg.BoostWindowLate);
        }

        /// <summary>Land <paramref name="links"/> perfect hops, one every 0.5 s from <paramref name="t"/>.</summary>
        private static void Build(JumpingRodChain chain, int links, float t)
        {
            for (int i = 0; i < links; i++)
            {
                chain.Press(t + i * 0.5f);
                chain.Land(t + i * 0.5f + 0.05f);
            }
        }
    }
}
