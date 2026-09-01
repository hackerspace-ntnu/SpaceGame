using System;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Core.Lobbies;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The host's Teams / Team size steppers on the roster page, hidden entirely outside a VS
    /// lobby.
    ///
    /// <para>
    /// Under the session strip rather than squeezed inside it: a <see cref="MenuStepper"/> is
    /// authored at <see cref="MenuStepper.Height"/> — nearly double the strip — because it is a
    /// control built for a full page column elsewhere, and squashing it would read as cramped on
    /// the one page where the host is actually deciding something.
    /// </para>
    ///
    /// <para>
    /// A joiner sees these too, but never gets to press them: they need to know whether a team
    /// still has room before clicking its plate, which the plate's own dimming already answers
    /// visually, but the steppers say it in numbers as well. Only the host can change what is
    /// shown, and <see cref="Render"/> is the only place either stepper's displayed value is ever
    /// written.
    /// </para>
    /// </summary>
    internal sealed class LobbyTeamRulesStrip
    {
        private const float Top = LobbySessionStrip.Top - LobbySessionStrip.Height - 16f;
        private const float Spacing = 64f;

        private readonly RectTransform row;

        // Kept so each stepper's onChanged can report the OTHER axis's value alongside the one that
        // was actually pressed — VersusRules.ClampTeams/ClampTeamSize both need both numbers, and a
        // stepper only ever knows its own.
        private int shownTeamCount;
        private int shownTeamSize;

        public MenuStepper Teams { get; }

        public MenuStepper TeamSize { get; }

        public bool Shown => row != null && row.gameObject.activeSelf;

        /// <param name="onSetTeamRules">Called with (teamCount, teamSize) when a chevron is pressed.</param>
        public LobbyTeamRulesStrip(RectTransform page, GameObject entryPrefab, Action<int, int> onSetTeamRules)
        {
            row = UIBuilder.PinnedTop(page, "TeamRules", MenuEntry.ColumnX, Top, MenuEntry.ColumnWidth,
                                      MenuStepper.Height);
            UIBuilder.Row(row, Spacing);

            Teams = MenuStepper.Create(entryPrefab, row, "Teams",
                VersusRules.DefaultTeams, VersusRules.MinTeams, VersusRules.MaxTeams,
                value => onSetTeamRules(value, shownTeamSize));
            FixedWidth(Teams);

            TeamSize = MenuStepper.Create(entryPrefab, row, "Team size",
                VersusRules.DefaultTeamSize, VersusRules.MinTeamSize, VersusRules.MaxTeamSize,
                value => onSetTeamRules(shownTeamCount, value));
            FixedWidth(TeamSize);

            row.gameObject.SetActive(false);
        }

        /// <summary>Shows and fills the strip for a VS lobby, and hides it entirely for a story one.</summary>
        public void Render(RosterSnapshot snapshot, bool isHost)
        {
            if (row == null) return;

            row.gameObject.SetActive(snapshot.IsVersus);
            if (!snapshot.IsVersus) return;

            shownTeamCount = snapshot.TeamCount;
            shownTeamSize = snapshot.TeamSize;

            Teams.SetLimits(VersusRules.MinTeams, VersusRules.ClampTeams(VersusRules.MaxTeams, shownTeamSize));
            Teams.SetValue(shownTeamCount);
            Teams.SetInteractable(isHost);

            TeamSize.SetLimits(VersusRules.MinTeamSize,
                               VersusRules.ClampTeamSize(VersusRules.MaxTeamSize, shownTeamCount));
            TeamSize.SetValue(shownTeamSize);
            TeamSize.SetInteractable(isHost);
        }

        /// <summary>
        /// Gives a stepper's row an explicit width inside the strip's horizontal layout.
        ///
        /// <see cref="MenuStepper"/> only ever sizes its own HEIGHT — it was built to sit in a
        /// vertical column that expands its children to the full column width, which this strip
        /// does not do (two steppers share one row). The existing <see cref="LayoutElement"/> is
        /// reused rather than a second one added: Unity would happily attach two, and which one the
        /// layout system then honours is not a bet worth making.
        /// </summary>
        private static void FixedWidth(MenuStepper stepper)
        {
            float width = MenuStepper.LabelWidth + MenuStepper.ChevronWidth * 2f + MenuStepper.ValueWidth;

            var element = stepper.Root.GetComponent<LayoutElement>();
            if (element == null) element = stepper.Root.gameObject.AddComponent<LayoutElement>();

            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
        }
    }
}
