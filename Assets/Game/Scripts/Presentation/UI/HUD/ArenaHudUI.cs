using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Arena;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The arena's scoreboard: the score and round at the top of the screen, how many each side
    /// has left, and a centre banner for whatever the match is doing — waiting, counting down, a
    /// round's result, the match's result with a way back to the menu.
    ///
    /// <para>
    /// Self-instantiating and <c>DontDestroyOnLoad</c> like the other in-game overlays, and dormant
    /// until an <see cref="ArenaMatch"/> exists. Everything it shows is read from
    /// <see cref="ArenaMatch.State"/>, which replicates, so a client shows exactly what the host does.
    /// Read every frame rather than on change, because the countdown ticks every frame anyway.
    /// </para>
    /// </summary>
    public class ArenaHudUI : MonoBehaviour
    {
        // Over the player HUD and world prompts, under every menu: the pause menu covers it.
        private const int SortingOrder = 700;

        private static ArenaHudUI instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;

            var go = new GameObject(nameof(ArenaHudUI));
            instance = go.AddComponent<ArenaHudUI>();
        }

        [SerializeField] private Vector2 scorePanelSize = new(620f, 96f);
        [SerializeField] private Vector2 bannerSize = new(900f, 120f);

        private RectTransform root;
        private TextMeshProUGUI team0Label;
        private TextMeshProUGUI team1Label;
        private TextMeshProUGUI scoreLabel;
        private TextMeshProUGUI roundLabel;
        private TextMeshProUGUI bannerLabel;
        private TextMeshProUGUI bannerDetail;
        private RectTransform leaveButton;

        // Whether this overlay currently holds the cursor, so the result screen's button can be clicked.
        private bool holdsScope;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            Build();
            root.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            ReleaseScope();
            if (instance == this) instance = null;
        }

        private void Update()
        {
            ArenaMatch match = ArenaMatch.Instance;
            bool shown = match != null && match.IsSpawned;

            if (root.gameObject.activeSelf != shown) root.gameObject.SetActive(shown);

            if (!shown)
            {
                ReleaseScope();
                return;
            }

            Render(match, match.State);
        }

        private void Render(ArenaMatch match, in ArenaMatchState state)
        {
            team0Label.text = $"{VersusRules.TeamName(0)}  {state.AliveTeam0} left";
            team0Label.color = TeamColor(0);
            team1Label.text = $"{state.AliveTeam1} left  {VersusRules.TeamName(1)}";
            team1Label.color = TeamColor(1);

            scoreLabel.text = $"{state.ScoreTeam0}  -  {state.ScoreTeam1}";
            roundLabel.text = state.Round > 0
                ? $"Round {state.Round}  /  first to {match.RoundsToWin}"
                : $"First to {match.RoundsToWin}";

            bool matchOver = state.Phase == ArenaPhase.MatchOver;
            Banner(state, match.SecondsLeft, out string headline, out string detail, out Color tint);

            bannerLabel.text = headline;
            bannerLabel.color = tint;
            bannerDetail.text = detail;
            bannerLabel.gameObject.SetActive(!string.IsNullOrEmpty(headline));
            bannerDetail.gameObject.SetActive(!string.IsNullOrEmpty(detail));
            leaveButton.gameObject.SetActive(matchOver);

            if (matchOver) TakeScope();
            else ReleaseScope();
        }

        private static void Banner(in ArenaMatchState state, float secondsLeft,
                                   out string headline, out string detail, out Color tint)
        {
            tint = UITheme.Bright;
            detail = string.Empty;

            switch (state.Phase)
            {
                case ArenaPhase.Waiting:
                    headline = "Waiting for both teams";
                    detail = "A round starts once each side has a player.";
                    return;

                case ArenaPhase.Countdown:
                    headline = $"Round {state.Round} in {Mathf.CeilToInt(secondsLeft)}";
                    detail = "Grab a weapon from the pads when it starts.";
                    return;

                case ArenaPhase.RoundOver:
                    if (state.RoundWinner >= 0)
                    {
                        headline = $"{VersusRules.TeamName(state.RoundWinner)} takes the round";
                        tint = TeamColor(state.RoundWinner);
                    }
                    else
                    {
                        headline = "Nobody left standing";
                        detail = "Draw - the round is played again.";
                    }
                    return;

                case ArenaPhase.MatchOver:
                    headline = $"{VersusRules.TeamName(state.RoundWinner)} wins the match";
                    tint = TeamColor(state.RoundWinner);
                    detail = $"{state.ScoreTeam0}  -  {state.ScoreTeam1}";
                    return;

                default:
                    PlayerController local = GameplayMenuScope.FindLocalPlayer();
                    bool eliminated = local != null && local.IsDead;
                    headline = eliminated ? "Eliminated" : string.Empty;
                    detail = eliminated ? "Spectating until the round ends.  WASD to fly, Space/Ctrl up and down." : string.Empty;
                    tint = UITheme.Danger;
                    return;
            }
        }

        private static Color TeamColor(int team) => SuitPalette.ColorOf(VersusSession.ColorOf(team));

        // The result screen needs a free cursor to press its button. GameplayMenuScope is the one
        // owner of cursor and input; time is not frozen, since the session is shared.
        private void TakeScope()
        {
            if (holdsScope) return;

            holdsScope = GameplayMenuScope.Enter(this, freezeTime: false);
            UIBuilder.EnsureEventSystem();
        }

        private void ReleaseScope()
        {
            if (!holdsScope) return;

            holdsScope = false;
            GameplayMenuScope.Exit(this);
        }

        private void Leave()
        {
            ReleaseScope();
            SessionExit.ToMainMenu(saveWorld: false);
        }

        // ───────────────────────────────────────────────────────────────── build

        private void Build()
        {
            var canvasObject = new GameObject("ArenaCanvas", typeof(Canvas), typeof(CanvasScaler),
                                              typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            UIScale.Configure(canvasObject.GetComponent<CanvasScaler>());

            root = UIBuilder.Fill(UIBuilder.Rect("Root", (RectTransform)canvasObject.transform));

            BuildScore();
            BuildBanner();
        }

        private void BuildScore()
        {
            RectTransform panel = Centered("Score", new Vector2(0.5f, 1f), new Vector2(0f, -24f), scorePanelSize);
            panel.pivot = new Vector2(0.5f, 1f);
            UIBuilder.Sprite(panel, UITheme.PanelSprite, UITheme.Panel);

            float third = scorePanelSize.x / 3f;

            team0Label = UIBuilder.LabelIn(UIBuilder.Slice(panel, "Team0", 16f, third - 16f), "Text",
                string.Empty, UITheme.LabelSize, UITheme.Bright, TextAlignmentOptions.Left, FontStyles.Bold);

            RectTransform middle = UIBuilder.Slice(panel, "Middle", third, third);
            scoreLabel = UIBuilder.LabelIn(middle, "Score", string.Empty, UITheme.TitleSize / 2 + 8,
                UITheme.Bright, TextAlignmentOptions.Center, FontStyles.Bold);

            team1Label = UIBuilder.LabelIn(UIBuilder.Slice(panel, "Team1", 2f * third, third - 16f), "Text",
                string.Empty, UITheme.LabelSize, UITheme.Bright, TextAlignmentOptions.Right, FontStyles.Bold);

            RectTransform roundRow = Centered("Round", new Vector2(0.5f, 1f),
                new Vector2(0f, -24f - scorePanelSize.y - 4f), new Vector2(scorePanelSize.x, 28f));
            roundRow.pivot = new Vector2(0.5f, 1f);
            roundLabel = UIBuilder.LabelIn(roundRow, "Text", string.Empty, UITheme.CaptionSize,
                UITheme.Muted, TextAlignmentOptions.Center);
        }

        private void BuildBanner()
        {
            RectTransform banner = Centered("Banner", new Vector2(0.5f, 0.62f), Vector2.zero, bannerSize);

            RectTransform headline = UIBuilder.Rect("Headline", banner);
            headline.anchorMin = new Vector2(0f, 0.45f);
            headline.anchorMax = Vector2.one;
            headline.offsetMin = headline.offsetMax = Vector2.zero;
            bannerLabel = UIBuilder.Label(headline, string.Empty, UITheme.TitleSize / 2 + 12, UITheme.Bright,
                TextAlignmentOptions.Center, FontStyles.Bold);

            RectTransform detail = UIBuilder.Rect("Detail", banner);
            detail.anchorMin = Vector2.zero;
            detail.anchorMax = new Vector2(1f, 0.45f);
            detail.offsetMin = detail.offsetMax = Vector2.zero;
            bannerDetail = UIBuilder.Label(detail, string.Empty, UITheme.LabelSize, UITheme.Muted,
                TextAlignmentOptions.Center);

            leaveButton = Centered("BackToMenu", new Vector2(0.5f, 0.62f),
                new Vector2(0f, -bannerSize.y * 0.5f - 48f), new Vector2(320f, 64f));
            Image background = UIBuilder.Sprite(leaveButton, UITheme.ChipSprite, UITheme.Accent);
            UIBuilder.Clickable(leaveButton, background, UITheme.Accent,
                Color.Lerp(UITheme.Accent, Color.white, 0.25f)).onClick.AddListener(Leave);
            UIBuilder.LabelIn(leaveButton, "Text", "Back to menu", UITheme.LabelSize, UITheme.Bright,
                TextAlignmentOptions.Center, FontStyles.Bold);
            leaveButton.gameObject.SetActive(false);
        }

        private RectTransform Centered(string name, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            RectTransform rect = UIBuilder.Rect(name, root);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
