using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
{
    // Pre-match config screen: the host picks gamemode, bot counts and win condition
    // here, and Start writes them into MatchSettings before running the existing
    // host-start + additive-arena-load flow (MainMenuUI.LaunchMinigame).
    //
    // Built at runtime rather than authored into MainMenu.unity, the same way
    // MatchResultUI builds its overlay. The menu's own buttons are wired straight to
    // MainMenuUI methods through the Inspector, so routing the existing "Minigame"
    // button into this screen needs no scene edits at all — MainMenuUI.StartMinigame
    // opens this instead of launching, and this calls back to launch.
    //
    // Presented the way MainMenu.unity presents itself: bold TextMeshPro on the
    // project's default SDF font, left-aligned over the live scene, no panels or
    // boxes. Rather than dimming the menu behind a backdrop, this switches the
    // menu's canvases off while it is up and switches them back on for Back — so
    // the screen reads as the menu changing pages, on the same background, instead
    // of a dialog opening on top of it.
    public class MinigameConfigUI : MonoBehaviour
    {
        // Straight from MainMenu.unity: the title is white, the menu entries are this
        // navy. Reused here as selected/unselected, so an option that is "on" looks
        // like the title and one that is "off" looks like a menu entry.
        private static readonly Color Bright = Color.white;
        private static readonly Color MenuNavy = new(0.043f, 0.118f, 0.227f, 1f);
        private static readonly Color Dim = new(0.55f, 0.60f, 0.68f, 1f);

        private const int TitleSize = 104;
        private const int ActionSize = 62;
        private const int RowSize = 40;
        private const int ValueSize = 44;
        private const int SummarySize = 26;

        private const int LabelWidth = 330;
        private const int StepperWidth = 52;
        private const int ValueWidth = 76;

        private MainMenuUI menu;
        private TMP_FontAsset font;

        private readonly List<Button> modeButtons = new();
        private readonly List<Button> conditionButtons = new();
        private readonly List<Canvas> hiddenCanvases = new();

        private GameObject allyRow;
        private GameObject enemyRow;
        private GameObject conditionRow;
        private GameObject killTargetRow;
        private GameObject teamLivesRow;
        private GameObject soloBotRow;
        private GameObject soloLivesRow;
        private GameObject battleRoyaleNoteRow;

        private TextMeshProUGUI allyValue;
        private TextMeshProUGUI enemyValue;
        private TextMeshProUGUI killTargetValue;
        private TextMeshProUGUI teamLivesValue;
        private TextMeshProUGUI soloBotValue;
        private TextMeshProUGUI soloLivesValue;
        private TextMeshProUGUI summaryText;

        public static MinigameConfigUI Open(MainMenuUI owner)
        {
            var existing = FindFirstObjectByType<MinigameConfigUI>();
            if (existing != null) return existing;

            var go = new GameObject("MinigameConfigUI");
            var ui = go.AddComponent<MinigameConfigUI>();
            ui.menu = owner;
            return ui;
        }

        private void Awake()
        {
            font = TMP_Settings.defaultFontAsset;

            // Statics outlive a return-to-menu, so every visit to this screen starts
            // from the same defaults rather than the last match's leftovers.
            MatchSettings.ResetToDefaults();

            EnsureEventSystem();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            HideMenuCanvases();
            Build();
            Refresh();
        }

        // ---------------------------------------------------------------- actions

        private void SetMode(MatchGameMode newMode)
        {
            MatchSettings.Mode = newMode;
            Refresh();
        }

        private void SetCondition(WinCondition condition)
        {
            MatchSettings.Condition = condition;
            Refresh();
        }

        private void StartMatch()
        {
            MatchSettings.ClampToLimits();
            Debug.Log($"[MinigameConfigUI] Starting: {MatchSettings.Describe()}");

            // The menu scene (and this overlay with it) is unloaded by the Single-mode
            // load inside LaunchMinigame, so there is nothing to restore here.
            if (menu != null)
                menu.LaunchMinigame();
            else
                Debug.LogError("[MinigameConfigUI] No MainMenuUI to launch through.");
        }

        private void Cancel()
        {
            RestoreMenuCanvases();
            Destroy(gameObject);
        }

        // The menu is not dimmed or covered — it is switched off, so only this
        // screen's text sits over the scene. A disabled Canvas also stops its
        // GraphicRaycaster, so the menu buttons underneath cannot be clicked either.
        private void HideMenuCanvases()
        {
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (canvas.transform.IsChildOf(transform)) continue;
                if (!canvas.enabled) continue;

                canvas.enabled = false;
                hiddenCanvases.Add(canvas);
            }
        }

        private void RestoreMenuCanvases()
        {
            foreach (var canvas in hiddenCanvases)
            {
                if (canvas != null)
                    canvas.enabled = true;
            }

            hiddenCanvases.Clear();
        }

        // --------------------------------------------------------------- refresh

        private void Refresh()
        {
            MatchSettings.ClampToLimits();

            bool teams = MatchRules.UsesTeams(MatchSettings.Mode);
            bool freeForAll = MatchSettings.Mode == MatchGameMode.FreeForAll;
            bool battleRoyale = MatchSettings.Mode == MatchGameMode.BattleRoyale;

            allyRow.SetActive(teams);
            enemyRow.SetActive(teams);
            conditionRow.SetActive(teams);
            killTargetRow.SetActive(teams && MatchSettings.Condition == WinCondition.KillTarget);
            teamLivesRow.SetActive(teams && MatchSettings.Condition == WinCondition.LivesPerPlayer);

            soloBotRow.SetActive(!teams);
            soloLivesRow.SetActive(freeForAll);
            battleRoyaleNoteRow.SetActive(battleRoyale);

            allyValue.text = MatchSettings.AllyCount.ToString();
            enemyValue.text = MatchSettings.EnemyCount.ToString();
            killTargetValue.text = MatchSettings.KillTargetCount.ToString();
            teamLivesValue.text = MatchSettings.LivesPerPlayerCount.ToString();
            soloBotValue.text = MatchSettings.SoloBotCount.ToString();
            soloLivesValue.text = MatchSettings.SoloLivesCount.ToString();

            Highlight(modeButtons, (int)MatchSettings.Mode);
            Highlight(conditionButtons, (int)MatchSettings.Condition);

            summaryText.text = MatchSettings.Describe();
        }

        private static void Highlight(List<Button> buttons, int selectedIndex)
        {
            for (int i = 0; i < buttons.Count; i++)
                TintText(buttons[i], i == selectedIndex ? Bright : MenuNavy);
        }

        // Text-only buttons: the TextMeshProUGUI is itself the Button's target
        // graphic, so the tint block recolours the glyphs directly and there is no
        // background image anywhere on this screen.
        private static void TintText(Button button, Color baseColor)
        {
            if (button.targetGraphic != null)
                button.targetGraphic.color = Color.white;

            var colors = button.colors;
            colors.normalColor = baseColor;
            colors.selectedColor = baseColor;
            colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.45f);
            colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.25f);
            colors.disabledColor = Color.Lerp(baseColor, Color.black, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        // ----------------------------------------------------------------- build

        private void Build()
        {
            var canvasGo = new GameObject("ConfigCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;

            // Same reference resolution the menu's own canvas uses, so type sized
            // against the menu's 90pt entries scales identically.
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Left column, like the menu's own entry list. Nothing behind it — the
            // scene shows through, which is exactly why the column is confined to the
            // bottom half: the menu camera has no pitch, so the horizon runs across the
            // middle and anything above it is text over open sky. Centred in this band
            // rather than in the whole screen, the rows land on ground and stay legible.
            // MenuEntry.Horizon carries the same reasoning for the other menu screens.
            var column = NewRect("Column", canvasGo.transform);
            column.anchorMin = new Vector2(0f, 0f);
            column.anchorMax = new Vector2(0f, 1f);
            column.pivot = new Vector2(0f, 0.5f);
            column.offsetMin = new Vector2(96f, 0f);
            column.offsetMax = new Vector2(96f + 1100f, -SpaceGame.Presentation.MenuEntry.Horizon);

            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 60, 60);
            layout.spacing = 16f; // the menu's own entries sit this far apart
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;

            AddLabelRow(column, "MINIGAME", TitleSize, Bright, 130);
            AddSpacer(column, 20);

            AddOptionRow(column, modeButtons,
                new[] { "Team Deathmatch", "Free-For-All", "Battle Royale" },
                index => SetMode((MatchGameMode)index));

            AddSpacer(column, 18);

            allyRow = AddStepperRow(column, "Allied bots", 0, MatchRules.MaxTeamBotsPerSide,
                () => MatchSettings.AllyCount, v => MatchSettings.AllyCount = v, out allyValue);

            enemyRow = AddStepperRow(column, "Enemy bots", 0, MatchRules.MaxTeamBotsPerSide,
                () => MatchSettings.EnemyCount, v => MatchSettings.EnemyCount = v, out enemyValue);

            conditionRow = AddOptionRow(column, conditionButtons,
                new[] { "Kill Target", "Lives Each", "Last Standing" },
                index => SetCondition((WinCondition)index), "Win condition");

            killTargetRow = AddStepperRow(column, "Kills to win", MatchRules.MinKillTarget, MatchRules.MaxKillTarget,
                () => MatchSettings.KillTargetCount, v => MatchSettings.KillTargetCount = v, out killTargetValue, step: 5);

            teamLivesRow = AddStepperRow(column, "Lives each", MatchRules.MinLives, MatchRules.MaxLives,
                () => MatchSettings.LivesPerPlayerCount, v => MatchSettings.LivesPerPlayerCount = v, out teamLivesValue);

            soloBotRow = AddStepperRow(column, "Bots", 1, MatchRules.MaxSoloBots,
                () => MatchSettings.SoloBotCount, v => MatchSettings.SoloBotCount = v, out soloBotValue);

            soloLivesRow = AddStepperRow(column, "Lives each", MatchRules.MinLives, MatchRules.MaxLives,
                () => MatchSettings.SoloLivesCount, v => MatchSettings.SoloLivesCount = v, out soloLivesValue);

            battleRoyaleNoteRow = AddLabelRow(column, "One life each. No respawns.", SummarySize, Dim, 34);

            AddSpacer(column, 10);
            summaryText = AddLabelRow(column, "", SummarySize, Dim, 40).GetComponent<TextMeshProUGUI>();
            AddSpacer(column, 22);

            AddActionRow(column, "Start Match", StartMatch);
            AddActionRow(column, "Back", Cancel);
        }

        private GameObject AddLabelRow(RectTransform parent, string text, int size, Color color, int height)
        {
            var row = NewRect("Label", parent);
            AddLayoutHeight(row, height);
            NewLabel(row.gameObject, text, size, color, TextAlignmentOptions.Left);
            return row.gameObject;
        }

        private void AddSpacer(RectTransform parent, int height)
        {
            var row = NewRect("Spacer", parent);
            AddLayoutHeight(row, height);
        }

        // A menu-style entry: just the word, tinting on hover, no box around it.
        private void AddActionRow(RectTransform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var row = NewRect(label, parent);
            AddLayoutHeight(row, 78);

            var text = NewLabel(row.gameObject, label, ActionSize, Bright, TextAlignmentOptions.Left);
            text.raycastTarget = true;

            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = text;
            button.onClick.AddListener(onClick);
            TintText(button, Bright);
        }

        private GameObject AddOptionRow(RectTransform parent, List<Button> into, string[] labels,
            UnityEngine.Events.UnityAction<int> onSelect, string rowLabel = null)
        {
            var row = NewHorizontalRow(parent, "OptionRow", 60, 34f, out RectTransform content);

            if (rowLabel != null)
                AddFixedLabel(content, rowLabel, LabelWidth, RowSize, Dim);

            foreach (string label in labels)
            {
                int index = into.Count;

                var option = NewRect("Option", content);
                var text = NewLabel(option.gameObject, label, rowLabel == null ? RowSize + 6 : RowSize, Bright,
                    TextAlignmentOptions.Left);
                text.raycastTarget = true;

                var fitter = option.gameObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

                var button = option.gameObject.AddComponent<Button>();
                button.targetGraphic = text;
                button.onClick.AddListener(() => onSelect(index));
                into.Add(button);
            }

            return row.gameObject;
        }

        // A row is a plain LayoutElement holding a stretched child that carries the
        // HorizontalLayoutGroup. The two must not share a GameObject: LayoutElement
        // and LayoutGroup both report layout priority 1, so uGUI takes the larger of
        // their preferred heights — the group's measurement of its text children wins
        // and the row balloons to ~4x the height it was given.
        private RectTransform NewHorizontalRow(RectTransform parent, string name, int height, float spacing,
            out RectTransform content)
        {
            var row = NewRect(name, parent);
            AddLayoutHeight(row, height);

            content = NewRect("Content", row);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            var group = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;
            group.childAlignment = TextAnchor.MiddleLeft;

            return row.gameObject.GetComponent<RectTransform>();
        }

        // "Allied bots   -  3  +" — a number field made of text, which suits this
        // screen better than a slider's coloured bar and handle.
        private GameObject AddStepperRow(RectTransform parent, string label, int min, int max,
            System.Func<int> get, System.Action<int> set, out TextMeshProUGUI valueLabel, int step = 1)
        {
            var row = NewHorizontalRow(parent, "StepperRow", 62, 6f, out RectTransform content);

            AddFixedLabel(content, label, LabelWidth, RowSize, Dim);

            AddStepperButton(content, "-", () => set(Mathf.Clamp(get() - step, min, max)));

            var valueRect = NewRect("Value", content);
            AddLayoutWidth(valueRect, ValueWidth);
            valueLabel = NewLabel(valueRect.gameObject, get().ToString(), ValueSize, Bright, TextAlignmentOptions.Center);

            AddStepperButton(content, "+", () => set(Mathf.Clamp(get() + step, min, max)));

            return row.gameObject;
        }

        private void AddStepperButton(RectTransform parent, string glyph, System.Action apply)
        {
            var rect = NewRect(glyph, parent);
            AddLayoutWidth(rect, StepperWidth);

            var text = NewLabel(rect.gameObject, glyph, ValueSize, Dim, TextAlignmentOptions.Center);
            text.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = text;
            button.onClick.AddListener(() => { apply(); Refresh(); });

            // Dim rather than the menu navy: these are the controls you actually
            // click, and navy on dark terrain is close to invisible.
            TintText(button, Dim);
        }

        private void AddFixedLabel(RectTransform parent, string label, int width, int size, Color color)
        {
            var rect = NewRect("Label", parent);
            AddLayoutWidth(rect, width);
            NewLabel(rect.gameObject, label, size, color, TextAlignmentOptions.Left);
        }

        // -------------------------------------------------------------- plumbing

        private TextMeshProUGUI NewLabel(GameObject host, string text, int size, Color color,
            TextAlignmentOptions alignment)
        {
            var label = host.AddComponent<TextMeshProUGUI>();
            if (font != null)
                label.font = font;

            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.fontStyle = FontStyles.Bold; // the menu's own entries are bold
            label.raycastTarget = false;       // only the clickable ones opt back in
            return label;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void AddLayoutHeight(RectTransform rect, int height)
        {
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
        }

        private static void AddLayoutWidth(RectTransform rect, int width)
        {
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            element.flexibleWidth = 0f;
        }

        // The menu scene ships with an EventSystem, but this screen is also the only
        // thing on screen when it is up — without one, nothing is clickable. The
        // project is on the new Input System, so StandaloneInputModule is inert.
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
    }
}
