using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The world screens, and the only place a world is chosen.
    ///
    /// Three pages, one component: the list, naming a new world, and confirming a delete. They are
    /// pages rather than a panel with a sidebar because this screen is now drawn in the main menu's
    /// own language — bold text over the live scene, nothing behind it — and that language has no
    /// boxes to put a sidebar in.
    ///
    /// One screen serves both singleplayer and multiplayer because the choice is the same in each;
    /// only where the player goes afterwards differs. Picking here rather than inside the lobby is
    /// also what lets the lobby open with the world already decided, so a host arrives at a session
    /// that exists rather than at a form asking them to make one.
    ///
    /// Deleting asks first. A save is the one thing in this game a player cannot make again, and the
    /// button that destroys it sits next to the button that plays it.
    /// </summary>
    public class WorldSelectUI : MenuScreen
    {
        public enum Destination { Singleplayer, Lobby }

        private const float ColumnX = MenuEntry.ColumnX;
        private const float ColumnWidth = MenuEntry.ColumnWidth;

        private const float TitleTop = MenuEntry.TitleTop;
        private const float TitleHeight = MenuEntry.TitleHeight;

        private const float RowHeight = 84f;
        private const float ActionHeight = 78f;

        /// <summary>Left inset inside a row, holding the selection marker.</summary>
        private const float Marker = 52f;

        /// <summary>Right inset inside a row, holding the timestamp.</summary>
        private const float DateWidth = 420f;

        /// <summary>Width of the underline a world name is typed on.</summary>
        private const float FieldWidth = 900f;

        private MainMenuUI menu;
        private Destination destination;

        private RectTransform page;
        private string selectedWorldId;
        private string pendingMessage;

        private readonly List<Row> rows = new();
        private GameObject deleteAction;
        private GameObject startAction;
        private TextMeshProUGUI message;
        private TMP_InputField nameInput;
        private TextMeshProUGUI nameError;

        /// <summary>A built row, and the two parts of it that say whether it is the selected one.</summary>
        private readonly struct Row
        {
            public readonly string SlotId;
            public readonly GameObject Marker;
            public readonly TextMeshProUGUI Date;

            public Row(string slotId, GameObject marker, TextMeshProUGUI date)
            {
                SlotId = slotId;
                Marker = marker;
                Date = date;
            }
        }

        /// <summary>
        /// Opens the world list. Fields are assigned before <see cref="MenuScreen.Present"/> because
        /// the pages are built from them.
        /// </summary>
        public static WorldSelectUI Open(MainMenuUI owner, Destination target)
        {
            var existing = Existing<WorldSelectUI>();
            if (existing != null) return existing;

            var ui = new GameObject(nameof(WorldSelectUI)).AddComponent<WorldSelectUI>();
            ui.menu = owner;
            ui.destination = target;
            ui.Present();
            return ui;
        }

        protected override void Build() => ShowList();

        /// <summary>
        /// What the button that leaves this screen does, which is not the same on both routes: on
        /// the multiplayer path it opens the lobby, where the game has not started yet.
        /// </summary>
        private string StartLabel =>
            destination == Destination.Singleplayer ? "Start game" : "Continue to lobby";

        private GameObject EntryPrefab => menu != null ? menu.MenuButtonPrefab : null;

        // ────────────────────────────────────────────────────────────── the world list

        private void ShowList()
        {
            RectTransform root = NewPage("List", "WORLDS");

            MenuEntry.Create(EntryPrefab, PinnedRow(root, MenuEntry.ContentTop, 720f), "NewWorldButton",
                             "New world", MenuEntry.ActionSize, ActionHeight, ShowNewWorld, out _);

            BuildList(root);
            BuildFooter(root);

            SetMessage(pendingMessage);
            pendingMessage = null;

            // Nothing is selected on the way in, so the two actions that need a world start hidden.
            ApplySelection();
        }

        private void BuildList(RectTransform root)
        {
            RectTransform frame = UIBuilder.Rect("WorldList", root);
            frame.anchorMin = new Vector2(0f, 0f);
            frame.anchorMax = new Vector2(0f, 1f);
            frame.pivot = new Vector2(0f, 0.5f);
            // Starts below the New world entry, which itself starts below the horizon, so the rows
            // are over ground rather than over sky — see MenuEntry.ContentTop.
            frame.offsetMin = new Vector2(ColumnX, MenuEntry.MessageBottom + 78f);
            frame.offsetMax = new Vector2(ColumnX + ColumnWidth, MenuEntry.ContentTop - 96f);

            RectTransform viewport = UIBuilder.Fill(UIBuilder.Rect("Viewport", frame));
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = UIBuilder.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            UIBuilder.Column(content, 8f);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = frame.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;

            List<SaveSlotInfo> slots = new SaveSlots(SaveManager.DefaultRoot).List();

            if (slots.Count == 0)
            {
                RectTransform empty = UIBuilder.Rect("Empty", content);
                UIBuilder.FixedHeight(empty, RowHeight);
                UIBuilder.Label(empty, "No worlds yet.", MenuEntry.CaptionSize, MenuEntry.Caption);
                return;
            }

            foreach (SaveSlotInfo slot in slots) BuildRow(content, slot);
        }

        private void BuildRow(RectTransform content, SaveSlotInfo slot)
        {
            // Captured into a local so every row's handler closes over its own id.
            string id = slot.SlotId;

            Button row = MenuEntry.Create(EntryPrefab, content, "Row", DisplayName(slot),
                                          MenuEntry.RowSize, RowHeight, () => Select(id),
                                          out TextMeshProUGUI label);

            MenuEntry.InsetLabel(label, Marker, DateWidth + 24f);

            var rect = (RectTransform)row.transform;

            RectTransform markerRect = UIBuilder.Rect("Marker", rect);
            markerRect.anchorMin = new Vector2(0f, 0f);
            markerRect.anchorMax = new Vector2(0f, 1f);
            markerRect.pivot = new Vector2(0f, 0.5f);
            markerRect.offsetMin = Vector2.zero;
            markerRect.offsetMax = new Vector2(Marker, 0f);

            // The marker, not the row's own label, is what says "selected": the button's animator
            // rewrites that label's colour on every state change and would undo any tint here.
            UIBuilder.Label(markerRect, ">", MenuEntry.RowSize, MenuEntry.Idle);
            markerRect.gameObject.SetActive(false);

            RectTransform dateRect = UIBuilder.Rect("Date", rect);
            dateRect.anchorMin = new Vector2(1f, 0f);
            dateRect.anchorMax = new Vector2(1f, 1f);
            dateRect.pivot = new Vector2(1f, 0.5f);
            dateRect.offsetMin = new Vector2(-DateWidth, 0f);
            dateRect.offsetMax = Vector2.zero;

            TextMeshProUGUI date = UIBuilder.Label(dateRect, Subtitle(slot), MenuEntry.CaptionSize,
                                                   MenuEntry.Caption, TextAlignmentOptions.Right);

            rows.Add(new Row(id, markerRect.gameObject, date));
        }

        private void BuildFooter(RectTransform root)
        {
            RectTransform messageRect = PinnedRow(root, 0f, ColumnWidth,
                                                  fromBottom: MenuEntry.MessageBottom, height: 44f);
            message = UIBuilder.Label(messageRect, string.Empty, MenuEntry.CaptionSize, MenuEntry.Warning);

            RectTransform footer = PinnedRow(root, 0f, ColumnWidth,
                                             fromBottom: MenuEntry.FooterBottom, height: ActionHeight);

            var layout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 64f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            MenuEntry.Width(MenuEntry.Create(EntryPrefab, footer, "BackButton", "Back",
                                             MenuEntry.ActionSize, ActionHeight, Close, out _), 200f);

            deleteAction = MenuEntry.Create(EntryPrefab, footer, "DeleteButton", "Delete",
                                            MenuEntry.ActionSize, ActionHeight, ShowConfirmDelete,
                                            out _).gameObject;
            MenuEntry.Width(deleteAction.GetComponent<Button>(), 260f);

            startAction = MenuEntry.Create(EntryPrefab, footer, "StartButton", StartLabel,
                                           MenuEntry.ActionSize, ActionHeight, StartSelected,
                                           out _).gameObject;
            MenuEntry.Width(startAction.GetComponent<Button>(), 620f);
        }

        /// <summary>Remembers which row is chosen. Playing and deleting are separate, deliberate presses.</summary>
        public void Select(string worldId)
        {
            selectedWorldId = worldId;
            SetMessage(string.Empty);
            ApplySelection();
        }

        /// <summary>
        /// Shows the marker on the chosen row, and reveals the two actions that need a world. They
        /// are hidden rather than disabled: a greyed-out entry needs a disabled look this menu does
        /// not have — the button animator's Disabled clip is empty.
        /// </summary>
        private void ApplySelection()
        {
            foreach (Row row in rows)
            {
                bool chosen = row.SlotId == selectedWorldId;

                if (row.Marker != null) row.Marker.SetActive(chosen);
                if (row.Date != null) row.Date.color = chosen ? MenuEntry.Idle : MenuEntry.Caption;
            }

            bool any = !string.IsNullOrEmpty(selectedWorldId);
            if (deleteAction != null) deleteAction.SetActive(any);
            if (startAction != null) startAction.SetActive(any);
        }

        /// <summary>Loads the selected world and enters it.</summary>
        public void StartSelected()
        {
            if (string.IsNullOrEmpty(selectedWorldId))
            {
                SetMessage("Pick a world first.");
                return;
            }

            if (!WorldSession.StageExisting(selectedWorldId, WorldConfig, out string error))
            {
                SetMessage(error);
                return;
            }

            Enter();
        }

        // ───────────────────────────────────────────────────────────── the new world page

        private void ShowNewWorld()
        {
            RectTransform root = NewPage("NewWorld", "NEW WORLD");
            RectTransform column = Column(root);

            RectTransform caption = UIBuilder.Rect("Caption", column);
            UIBuilder.FixedHeight(caption, 44f);
            UIBuilder.Label(caption, "Name your world", MenuEntry.CaptionSize, MenuEntry.Caption);

            nameInput = BuildNameField(column);

            RectTransform errorRow = UIBuilder.Rect("Error", column);
            UIBuilder.FixedHeight(errorRow, 48f);
            nameError = UIBuilder.Label(errorRow, string.Empty, MenuEntry.CaptionSize, MenuEntry.Warning);

            UIBuilder.Spacer(column, 40f);

            MenuEntry.Width(MenuEntry.Create(EntryPrefab, column, "CreateButton", StartLabel,
                                             MenuEntry.ActionSize, ActionHeight, CreateWorld, out _), 620f);
            MenuEntry.Width(MenuEntry.Create(EntryPrefab, column, "BackButton", "Back",
                                             MenuEntry.ActionSize, ActionHeight, ShowList, out _), 220f);

            // Typing is the only thing to do on this page, so the caret starts in the field.
            nameInput.ActivateInputField();
        }

        /// <summary>
        /// A name typed on a rule rather than in a box, because this menu has no boxes.
        /// </summary>
        private TMP_InputField BuildNameField(RectTransform parent)
        {
            RectTransform rect = UIBuilder.Rect("NameInput", parent);

            // Width stated explicitly: the field is an invisible rect with a rule under it, so it
            // has no content of its own for the column to measure and would otherwise collapse.
            LayoutElement element = UIBuilder.FixedHeight(rect, 96f);
            element.minWidth = FieldWidth;
            element.preferredWidth = FieldWidth;

            Image hit = UIBuilder.HitArea(rect);

            RectTransform underline = UIBuilder.Rect("Underline", rect);
            underline.anchorMin = new Vector2(0f, 0f);
            underline.anchorMax = new Vector2(0f, 0f);
            underline.pivot = new Vector2(0f, 0f);
            underline.anchoredPosition = Vector2.zero;
            underline.sizeDelta = new Vector2(FieldWidth, 3f);
            UIBuilder.Solid(underline, MenuEntry.Caption);

            RectTransform textArea = UIBuilder.Rect("TextArea", rect);
            textArea.anchorMin = new Vector2(0f, 0f);
            textArea.anchorMax = new Vector2(0f, 1f);
            textArea.pivot = new Vector2(0f, 0.5f);
            textArea.offsetMin = new Vector2(0f, 12f);
            textArea.offsetMax = new Vector2(FieldWidth, 0f);
            textArea.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI hint = UIBuilder.LabelIn(textArea, "Placeholder", "World name…",
                                                     MenuEntry.ActionSize, MenuEntry.Caption,
                                                     TextAlignmentOptions.Left, FontStyles.Bold);
            TextMeshProUGUI text = UIBuilder.LabelIn(textArea, "Text", string.Empty,
                                                     MenuEntry.ActionSize, MenuEntry.Idle,
                                                     TextAlignmentOptions.Left, FontStyles.Bold);

            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = hit;
            input.textViewport = textArea;
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.caretColor = MenuEntry.Idle;
            input.customCaretColor = true;

            // A world name becomes a file name. WorldIdentity sanitises it anyway, but capping the
            // length here stops a 400-character name reaching it at all.
            input.characterLimit = 40;

            // Enter is what a player types after a name; making them find the button as well is a
            // step for nothing.
            input.onSubmit.AddListener(_ => CreateWorld());
            return input;
        }

        /// <summary>Starts a world with no save behind it.</summary>
        public void CreateWorld()
        {
            string typed = nameInput != null ? nameInput.text : string.Empty;

            if (!WorldIdentity.ValidateNewName(typed, new SaveSlots(SaveManager.DefaultRoot), out string error))
            {
                if (nameError != null) nameError.text = error;
                if (nameInput != null) nameInput.ActivateInputField();
                return;
            }

            WorldSession.StageNew(typed.Trim(), WorldConfig);
            Enter();
        }

        // ──────────────────────────────────────────────────────── the delete confirmation

        private void ShowConfirmDelete()
        {
            if (string.IsNullOrEmpty(selectedWorldId))
            {
                SetMessage("Pick a world first.");
                return;
            }

            // Read again rather than remembered from the row: this page states what is about to be
            // destroyed, so it should be reading the file it is about to delete.
            string world = selectedWorldId;
            string display = world;
            string subtitle = string.Empty;

            foreach (SaveSlotInfo slot in new SaveSlots(SaveManager.DefaultRoot).List())
            {
                if (slot.SlotId != world) continue;

                display = DisplayName(slot);
                subtitle = slot.Unreadable ? "This save cannot be read." : $"Last played {Stamp(slot)}";
                break;
            }

            RectTransform root = NewPage("ConfirmDelete", "DELETE WORLD");
            RectTransform column = Column(root);

            RectTransform name = UIBuilder.Rect("World", column);
            UIBuilder.FixedHeight(name, 86f);
            UIBuilder.Label(name, display, MenuEntry.ActionSize, MenuEntry.Idle,
                            TextAlignmentOptions.Left, FontStyles.Bold);

            RectTransform when = UIBuilder.Rect("When", column);
            UIBuilder.FixedHeight(when, 44f);
            UIBuilder.Label(when, subtitle, MenuEntry.CaptionSize, MenuEntry.Caption);

            UIBuilder.Spacer(column, 28f);

            RectTransform warning = UIBuilder.Rect("Warning", column);
            UIBuilder.FixedHeight(warning, 48f);
            UIBuilder.Label(warning, "This cannot be undone.", MenuEntry.CaptionSize, MenuEntry.Warning);

            UIBuilder.Spacer(column, 40f);

            MenuEntry.Width(MenuEntry.Create(EntryPrefab, column, "ConfirmButton", "Delete world",
                                             MenuEntry.ActionSize, ActionHeight, ConfirmDelete, out _), 460f);
            MenuEntry.Width(MenuEntry.Create(EntryPrefab, column, "CancelButton", "Cancel",
                                             MenuEntry.ActionSize, ActionHeight, ShowList, out _), 260f);
        }

        /// <summary>Deletes the world's file and returns to the list.</summary>
        public void ConfirmDelete()
        {
            if (string.IsNullOrEmpty(selectedWorldId)) return;

            string deleted = selectedWorldId;
            new SaveSlots(SaveManager.DefaultRoot).Delete(deleted);

            selectedWorldId = null;
            pendingMessage = $"Deleted '{deleted}'.";
            ShowList();
        }

        // ─────────────────────────────────────────────────────────────────────── plumbing

        /// <summary>
        /// The world new saves are stamped with, and the world existing saves are checked against
        /// before loading. Read from the menu rather than serialized here, because this screen has
        /// no Inspector to assign it in.
        /// </summary>
        private WorldStreamingConfig WorldConfig => menu != null ? menu.WorldConfig : null;

        /// <summary>
        /// Hands off to wherever this screen was opened for. The world is already staged by the time
        /// this runs, so all that is left is deciding how to get out of the way.
        ///
        /// The two destinations leave differently, and the difference matters. Singleplayer is a
        /// scene load, which discards the menu's canvases anyway — so this hands off, leaving them
        /// switched off rather than restoring canvases about to be unloaded. The lobby is now a page
        /// over this same menu and loads nothing, so this has to Close: handing off there would
        /// leave the menu's canvases off with only this destroyed screen remembering it hid them,
        /// and backing out of the lobby would land on an empty screen.
        /// </summary>
        private void Enter()
        {
            if (menu == null)
            {
                Debug.LogError("[WorldSelectUI] No MainMenuUI to enter through.");
                return;
            }

            if (destination == Destination.Lobby)
            {
                MainMenuUI owner = menu;
                Close();
                owner.EnterLobby();
                return;
            }

            MainMenuUI singleplayerOwner = menu;
            HandOff();
            singleplayerOwner.EnterWorld();
        }

        /// <summary>
        /// Swaps the visible page. The outgoing one is switched off before it is destroyed because
        /// Destroy does not take effect until the end of the frame, and two live pages would both
        /// draw and both take clicks until then.
        /// </summary>
        private RectTransform NewPage(string name, string title)
        {
            if (page != null)
            {
                page.gameObject.SetActive(false);

                // Destroy is deferred and therefore safe to call from the button handler that
                // triggered the switch; DestroyImmediate is not, but it is the only one edit mode
                // accepts — and an editor tool previewing this screen is the one caller in edit
                // mode. UIBuilder.EnsureEventSystem draws the same distinction.
                if (Application.isPlaying) Destroy(page.gameObject);
                else DestroyImmediate(page.gameObject);
            }

            rows.Clear();
            deleteAction = null;
            startAction = null;
            message = null;
            nameInput = null;
            nameError = null;

            page = UIBuilder.Fill(UIBuilder.Rect(name, Surface));

            RectTransform titleRect = PinnedRow(page, TitleTop, ColumnWidth, height: TitleHeight);
            UIBuilder.Label(titleRect, title, MenuEntry.TitleSize, MenuEntry.Title,
                            TextAlignmentOptions.Left, FontStyles.Bold);

            return page;
        }

        /// <summary>A left-aligned row pinned to the top of the page, or to the bottom when asked.</summary>
        private static RectTransform PinnedRow(RectTransform parent, float fromTop, float width,
            float fromBottom = float.NaN, float height = ActionHeight)
        {
            RectTransform rect = UIBuilder.Rect("Row", parent);
            bool bottom = !float.IsNaN(fromBottom);

            rect.anchorMin = rect.anchorMax = new Vector2(0f, bottom ? 0f : 1f);
            rect.pivot = new Vector2(0f, bottom ? 0f : 1f);
            rect.anchoredPosition = new Vector2(ColumnX, bottom ? fromBottom : fromTop);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        /// <summary>The stacked column the two simple pages are laid out in.</summary>
        private static RectTransform Column(RectTransform parent)
        {
            RectTransform column = UIBuilder.Rect("Column", parent);
            column.anchorMin = column.anchorMax = new Vector2(0f, 1f);
            column.pivot = new Vector2(0f, 1f);
            column.anchoredPosition = new Vector2(ColumnX, MenuEntry.ContentTop);
            column.sizeDelta = new Vector2(ColumnWidth, 0f);

            VerticalLayoutGroup layout = UIBuilder.Column(column, 14f);

            // Rows take their own width rather than the column's. An entry stretched across the
            // full column would hover — and play the menu's hover sound — with the cursor a long
            // way from the word it belongs to.
            layout.childForceExpandWidth = false;

            var fitter = column.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return column;
        }

        private static string DisplayName(SaveSlotInfo slot) =>
            slot.Unreadable ? slot.SlotId : WorldIdentity.DisplayNameFor(slot.Header, slot.SlotId);

        // Unreadable files are listed rather than hidden: a world the player can see and delete
        // beats one that silently is not there.
        private static string Subtitle(SaveSlotInfo slot) =>
            slot.Unreadable ? "(unreadable)" : Stamp(slot);

        private static string Stamp(SaveSlotInfo slot) =>
            slot.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        private void SetMessage(string text)
        {
            if (message != null) message.text = text ?? string.Empty;
        }
    }
}
