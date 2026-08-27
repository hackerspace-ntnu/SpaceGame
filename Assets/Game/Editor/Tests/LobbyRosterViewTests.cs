// LobbyRosterView touches uGUI (RectTransform, TextMeshProUGUI, Button), so it lives in
// Assembly-CSharp. This test therefore goes in Assets/Game/Editor/Tests/, which compiles into
// Assembly-CSharp-Editor — the only test location that can see Assembly-CSharp types.
// Assets/Game/Tests/EditMode/ has its own asmdef and cannot reference it. MenuStepperTests.cs
// carries the same note.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

namespace SpaceGame.Tests
{
    /// <summary>
    /// What the in-lobby page shows, and to whom.
    ///
    /// Rendered for real from a <see cref="RosterSnapshot"/>, which is exactly the property the
    /// snapshot was introduced for: no network, no authentication service, no Unity Gaming Services.
    /// </summary>
    public class LobbyRosterViewTests
    {
        private RectTransform page;
        private LobbyRosterView view;

        private static LobbyRosterView.Actions NoActions() =>
            new(() => { }, () => { }, () => { }, _ => { }, _ => { }, _ => { }, (_, _) => { });

        private static RosterSnapshot Versus(int teams, int size, int localSlot, params int[] playerTeams)
        {
            var names = new string[playerTeams.Length];
            for (int i = 0; i < names.Length; i++) names[i] = $"P{i}";

            var occupancy = new int[teams];
            foreach (int team in playerTeams)
                if (team >= 0 && team < teams)
                    occupancy[team]++;

            return new RosterSnapshot(names, new int[names.Length], playerTeams,
                                      TeamColorRules.DefaultColors(teams, 14), occupancy,
                                      teams, size, localSlot, 0, isVersus: true);
        }

        [SetUp]
        public void Build()
        {
            var host = new GameObject("Page", typeof(RectTransform));
            page = (RectTransform)host.transform;
            view = new LobbyRosterView(page, null, NoActions());
        }

        [TearDown]
        public void Clean()
        {
            // Not view.Dispose(): it tears the rank down through UnityEngine.Object.Destroy, which
            // only defers in play mode — called here, outside it, Unity logs an error and fails the
            // test. LobbyPreviewRank.Create builds its own top-level GameObject rather than
            // parenting under the page, so it has to be swept up on its own via DestroyImmediate.
            foreach (LobbyPreviewRank leftover in
                     Object.FindObjectsByType<LobbyPreviewRank>(FindObjectsSortMode.None))
                Object.DestroyImmediate(leftover.gameObject);

            if (page != null) Object.DestroyImmediate(page.gameObject);
        }

        [Test]
        public void AStoryLobbyHasNoTeamControls()
        {
            var snapshot = new RosterSnapshot(new[] { "Pilot" }, new[] { 3 }, null, null, null,
                                              2, 2, 0, 0, isVersus: false);

            view.Render(snapshot, isHost: true, hostTitle: "DUNE");

            Assert.IsFalse(view.TeamRulesShown, "story lobbies have no teams to tune");
        }

        [Test]
        public void AVersusHostSeesTheTeamSteppers()
        {
            view.Render(Versus(2, 2, 0, 0, 1), isHost: true, hostTitle: null);

            Assert.IsTrue(view.TeamRulesShown);
            Assert.IsTrue(view.TeamsStepper.Increase.interactable);
        }

        /// <summary>
        /// A joiner reads the host's numbers and cannot press them. Shown rather than hidden: the
        /// rules are what the match is, and a client who cannot see them cannot tell whether their
        /// team is full.
        /// </summary>
        [Test]
        public void AVersusJoinerSeesTheNumbersButCannotPressThem()
        {
            view.Render(Versus(3, 2, 1, 0, 1), isHost: false, hostTitle: null);

            Assert.IsTrue(view.TeamRulesShown);
            Assert.IsFalse(view.TeamsStepper.Increase.interactable);
            Assert.IsFalse(view.TeamSizeStepper.Decrease.interactable);
        }

        [Test]
        public void TheSteppersShowTheRulesInForce()
        {
            view.Render(Versus(3, 4, 0, 0), isHost: true, hostTitle: null);

            Assert.AreEqual("3", view.TeamsStepper.ValueLabel.text);
            Assert.AreEqual("4", view.TeamSizeStepper.ValueLabel.text);
        }

        [Test]
        public void OnlyTheHostIsOfferedStart()
        {
            view.Render(Versus(2, 2, 1, 0, 1), isHost: false, hostTitle: null);
            Assert.IsFalse(view.StartShown);

            view.Render(Versus(2, 2, 0, 0, 1), isHost: true, hostTitle: null);
            Assert.IsTrue(view.StartShown);
        }

        [Test]
        public void AWarningSurvivesTheNextPoll()
        {
            view.Render(Versus(2, 1, 0, 0, 1), isHost: true, hostTitle: null);

            view.SetWarning("TEAM TWO is full.");
            view.Render(Versus(2, 1, 0, 0, 1), isHost: true, hostTitle: null);

            Assert.AreEqual("TEAM TWO is full.", view.StatusText);
        }
    }
}
