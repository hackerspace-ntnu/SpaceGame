using System;
using UnityEngine;
using SpaceGame.Core.Lobbies;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The host's Teams / Team size steppers, hidden entirely outside a VS lobby.
    ///
    /// <para>
    /// They sit at the right-hand end of the same top band as <see cref="LobbySessionStrip"/> —
    /// code and Copy on the left, privacy in the middle, team rules on the right — set at the
    /// strip's own caption scale and in its white-over-sky palette, because they are the same kind
    /// of thing: session facts you glance at once, while the astronauts underneath are what the
    /// page is for.
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
        // Caption slots sized to their own text at CaptionSize, the way LobbySessionStrip sizes
        // every slot to its longest content — UIBuilder labels truncate rather than overflow, so a
        // slot narrower than its word silently eats it.
        private const float TeamsCaptionWidth = 96f;
        private const float SizeCaptionWidth = 150f;

        private const float ChevronWidth = 44f;
        private const float ValueWidth = 48f;

        /// <summary>Between the two steppers, so they read as two controls rather than one run of glyphs.</summary>
        private const float Gap = 48f;

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
            MenuStepper.Skin teamsSkin = StripSkin(TeamsCaptionWidth);
            MenuStepper.Skin sizeSkin = StripSkin(SizeCaptionWidth);
            float width = teamsSkin.Width + Gap + sizeSkin.Width;

            row = UIBuilder.PinnedTop(page, "TeamRules",
                                      MenuEntry.ColumnX + MenuEntry.ColumnWidth - width,
                                      LobbySessionStrip.Top, width, LobbySessionStrip.Height);

            Teams = MenuStepper.Create(entryPrefab,
                UIBuilder.Slice(row, "TeamsSlot", 0f, teamsSkin.Width), "TEAMS",
                VersusRules.DefaultTeams, VersusRules.MinTeams, VersusRules.MaxTeams,
                value => onSetTeamRules(value, shownTeamSize), teamsSkin);

            TeamSize = MenuStepper.Create(entryPrefab,
                UIBuilder.Slice(row, "TeamSizeSlot", teamsSkin.Width + Gap, sizeSkin.Width), "TEAM SIZE",
                VersusRules.DefaultTeamSize, VersusRules.MinTeamSize, VersusRules.MaxTeamSize,
                value => onSetTeamRules(shownTeamCount, value), sizeSkin);

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

        /// <summary>The session strip's own caption scale and palette, at the given caption width.</summary>
        private static MenuStepper.Skin StripSkin(float captionWidth) =>
            new(LobbySessionStrip.CaptionSize, LobbySessionStrip.ValueSize, captionWidth,
                ChevronWidth, ValueWidth, LobbySessionStrip.Height, light: true,
                LobbySessionStrip.ShadowOffset);
    }
}
