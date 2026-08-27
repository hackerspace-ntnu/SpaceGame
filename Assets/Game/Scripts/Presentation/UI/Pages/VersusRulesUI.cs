using TMPro;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The screen between "Host a game" and the VS lobby: how many teams, how big.
    ///
    /// Built from <see cref="MenuScreen"/>'s shared page skeleton the same way
    /// <see cref="MenuChoiceUI"/> is — a title over the live scene, a left column of navy entries
    /// below the horizon — because this sits in exactly the same flow, one page further in:
    /// <see cref="MainMenuUI.HostVersus"/> opens it, and "Start lobby" calls back into
    /// <see cref="MainMenuUI.EnterVersusLobby"/>.
    /// </summary>
    public class VersusRulesUI : MenuScreen
    {
        /// <summary>What the lobby reads when it creates the session.</summary>
        public static int StagedTeams { get; set; } = VersusRules.DefaultTeams;

        /// <summary>What the lobby reads when it creates the session.</summary>
        public static int StagedTeamSize { get; set; } = VersusRules.DefaultTeamSize;

        private MenuStepper teamsStepper;
        private MenuStepper teamSizeStepper;
        private TextMeshProUGUI seatsLabel;

        /// <summary>
        /// Opens the screen, resetting the staged rules to defaults first.
        ///
        /// The reset happens only on an actual new visit — after the "already open" guard below,
        /// the same guard every runtime screen in this menu uses — so a second call while the page
        /// is already up (which nothing in this flow should ever make, but a stray double-click on
        /// "Host a game" could) returns the existing screen untouched rather than snapping a host's
        /// already-tuned numbers back to defaults out from under them.
        ///
        /// Resetting at all is still necessary: the staged values are statics and survive a return
        /// to the menu, so without this a second visit would start from whatever the last match
        /// left behind rather than from <see cref="VersusRules.DefaultTeams"/> and
        /// <see cref="VersusRules.DefaultTeamSize"/> — the same reasoning
        /// <c>MinigameConfigUI.Awake</c> gives for <c>MatchSettings.ResetToDefaults</c>.
        /// </summary>
        public static VersusRulesUI Open(MainMenuUI owner)
        {
            var existing = FindFirstObjectByType<VersusRulesUI>();
            if (existing != null) return existing;

            ResetToDefaults();

            var ui = new GameObject(nameof(VersusRulesUI)).AddComponent<VersusRulesUI>();
            ui.Present(owner);
            return ui;
        }

        // ------------------------------------------------------------ staged rules

        public static void ResetToDefaults()
        {
            StagedTeams = VersusRules.DefaultTeams;
            StagedTeamSize = VersusRules.DefaultTeamSize;
        }

        /// <summary>
        /// Stages a new team count. Teams is the axis being moved, so it lands wherever the host
        /// asked (clamped only to its own <see cref="VersusRules.MinTeams"/> /
        /// <see cref="VersusRules.MaxTeams"/> range) — team size is what gives way underneath it if
        /// the pair no longer fits <see cref="VersusRules.MaxSeats"/>.
        ///
        /// <para>
        /// This is deliberately NOT <c>VersusRules.ClampTeams(teams, StagedTeamSize)</c>. That call
        /// would clamp the requested team count DOWN to whatever the CURRENT team size still allows
        /// — so raising teams while size is already large would refuse the very axis the host just
        /// asked to move, which is exactly the silent-refusal failure this staging exists to avoid.
        /// </para>
        ///
        /// <para>
        /// The actual invariant: <see cref="StagedTeams"/> is clamped to its own
        /// <c>[MinTeams, MaxTeams]</c> range, so it is always a value <see cref="VersusRules"/> would
        /// accept on its own terms. <see cref="VersusRules.ClampTeamSize"/> then bounds
        /// <see cref="StagedTeamSize"/> to at most <c>MaxSeats / StagedTeams</c> (integer division),
        /// so <c>StagedTeams * StagedTeamSize &lt;= MaxSeats</c> by construction — the product can
        /// never cross the ceiling no matter what <see cref="StagedTeamSize"/> held before this ran.
        /// That is the invariant a future edit here has to preserve.
        /// </para>
        /// </summary>
        public static void StageTeams(int teams)
        {
            StagedTeams = Clamp(teams, VersusRules.MinTeams, VersusRules.MaxTeams);
            StagedTeamSize = VersusRules.ClampTeamSize(StagedTeamSize, StagedTeams);
        }

        /// <summary>Mirror of <see cref="StageTeams"/>: team size wins, teams gives way.</summary>
        public static void StageTeamSize(int teamSize)
        {
            StagedTeamSize = Clamp(teamSize, VersusRules.MinTeamSize, VersusRules.MaxTeamSize);
            StagedTeams = VersusRules.ClampTeams(StagedTeams, StagedTeamSize);
        }

        public static string DescribeSeats(int teams, int teamSize) =>
            $"{VersusRules.Seats(teams, teamSize)} of {VersusRules.MaxSeats} seats";

        private static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);

        // ---------------------------------------------------------------- actions

        /// <summary>
        /// Closes this screen, then hands off to the lobby.
        ///
        /// The owner is read into a local before <see cref="MenuScreen.Close"/> runs, for the same
        /// reason <see cref="MenuChoiceUI.Pick"/> reads its route ahead of time: <c>Close</c>
        /// destroys this object, and reaching for <see cref="MenuScreen.Menu"/> afterwards would be
        /// reading a field on a corpse.
        /// </summary>
        private void StartLobby()
        {
            MainMenuUI owner = Menu;
            Close();
            owner.EnterVersusLobby();
        }

        /// <summary>
        /// Redraws both steppers and the seat caption from the staged values.
        ///
        /// Required after every stage: <see cref="MenuStepper"/> reports what a chevron press asks
        /// for, it does not decide what ends up on screen — see its own class doc. Without this, a
        /// press that (say) grows teams and, underneath it, shrinks team size would move neither
        /// number the row is showing.
        /// </summary>
        private void Refresh()
        {
            teamsStepper.SetValue(StagedTeams);
            teamSizeStepper.SetValue(StagedTeamSize);
            seatsLabel.text = DescribeSeats(StagedTeams, StagedTeamSize);
        }

        // ----------------------------------------------------------------- build

        protected override void Build()
        {
            Title("VERSUS");

            RectTransform column = Column();

            // Both steppers keep VersusRules' full own-axis range for the lifetime of the page,
            // rather than being re-narrowed from Refresh via SetLimits to whatever the OTHER axis's
            // current value still allows. Narrowing them would make a chevron stop responding the
            // instant its own axis hit the ceiling implied by the other one — the same silent
            // refusal StageTeams/StageTeamSize's doc explains they exist to avoid. Correctness
            // instead comes entirely from the staging methods: whichever axis the host just moved
            // lands where asked, and the other one is pulled down to fit.
            teamsStepper = MenuStepper.Create(EntryPrefab, column, "Teams", StagedTeams,
                VersusRules.MinTeams, VersusRules.MaxTeams,
                value => { StageTeams(value); Refresh(); });

            teamSizeStepper = MenuStepper.Create(EntryPrefab, column, "Team size", StagedTeamSize,
                VersusRules.MinTeamSize, VersusRules.MaxTeamSize,
                value => { StageTeamSize(value); Refresh(); });

            UIBuilder.Spacer(column, 24f);

            RectTransform seatsRow = UIBuilder.Rect("Seats", column);
            UIBuilder.FixedHeight(seatsRow, 44f);
            seatsLabel = UIBuilder.LabelIn(seatsRow, "Text (TMP)", DescribeSeats(StagedTeams, StagedTeamSize),
                MenuEntry.CaptionSize, MenuEntry.Caption);

            // 14, not the 44 MenuChoiceUI uses before its own terminal action: this column is
            // already the tallest page built on MenuScreen's skeleton (two steppers, a caption, and
            // two 78px actions), and the full 44 pushed its bottom edge to within ~32px of the
            // frame, well inside MenuEntry.FooterBottom's usual 64px margin. If a future row is
            // added above this line, tighten a spacer again rather than letting Back run close to
            // the bottom edge.
            UIBuilder.Spacer(column, 14f);

            Entry(column, "StartButton", "Start lobby", StartLobby);

            UIBuilder.Spacer(column, 30f);

            Entry(column, "BackButton", "Back", Close);
        }
    }
}
