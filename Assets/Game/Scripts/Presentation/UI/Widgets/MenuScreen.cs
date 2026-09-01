using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A full-screen menu page that shows its text over the live scene rather than over a panel.
    ///
    /// This is the shape MinigameConfigUI arrived at independently, and still keeps its own copy of:
    /// spawn a canvas, switch every OTHER canvas off, build a column of text, put the canvases back
    /// on the way out. Written down once here so a new screen is a subclass rather than a new copy of
    /// the same twenty lines.
    ///
    /// The menu is switched off rather than dimmed or covered, which is what keeps the 3D menu scene
    /// visible behind the words. A disabled Canvas also stops its GraphicRaycaster, so the buttons
    /// underneath cannot be clicked through this screen either.
    ///
    /// <para>
    /// Beyond the canvas mechanics, this also owns the page skeleton every screen built on top of it
    /// shares: a title pinned near the top, a column of <see cref="MenuEntry"/> rows below
    /// <see cref="MenuEntry.Horizon"/>, and the owning <see cref="MainMenuUI"/> those rows are cloned
    /// from. <c>MenuChoiceUI</c> and <c>VersusRulesUI</c> are two independent pages built from
    /// exactly this skeleton; a third page copying <c>PinnedRow</c>/<c>Title</c>/<c>Column</c>/
    /// <c>Entry</c> into itself rather than calling these is the same duplication this class already
    /// exists to prevent.
    /// </para>
    /// </summary>
    public abstract class MenuScreen : MonoBehaviour
    {
        /// <summary>Above the menu's own canvases, which sort at 0.</summary>
        protected virtual int SortingOrder => 900;

        /// <summary>The full-screen rect this screen builds into. Valid from <see cref="Build"/> on.</summary>
        protected RectTransform Surface { get; private set; }

        /// <summary>
        /// The menu this screen was opened from. Every subclass's static <c>Open</c> hands this to
        /// <see cref="Present"/>, which is where it is recorded — a single place to read it from
        /// rather than each page keeping its own copy of the same field.
        /// </summary>
        protected MainMenuUI Menu { get; private set; }

        /// <summary>
        /// The prefab every <see cref="Entry"/> row is cloned from. Read off <see cref="Menu"/>
        /// rather than cached, so a page built before <see cref="Present"/> has assigned
        /// <see cref="Menu"/> fails loud (a null prefab, handled by <see cref="MenuEntry.Create"/>)
        /// instead of quietly keeping a stale reference.
        /// </summary>
        protected GameObject EntryPrefab => Menu != null ? Menu.MenuButtonPrefab : null;

        /// <summary>The height every <see cref="Entry"/> row in this menu is built at.</summary>
        protected const float ActionHeight = MenuEntry.ActionHeight;

        private readonly List<Canvas> hidden = new();
        private GameObject canvasObject;
        private bool closing;

        /// <summary>Builds the page. Called once, after the subclass's own fields are assigned.</summary>
        protected abstract void Build();

        /// <summary>
        /// The live page of type <typeparamref name="T"/>, or null when there is none. Every
        /// subclass's static <c>Open</c> starts here, so a second click on the button that opened a
        /// page reuses it instead of stacking a second copy on top.
        ///
        /// <para>
        /// A page that has already been closed does not count as one. <see cref="Close"/> destroys
        /// the GameObject, but Unity does not act on that until the end of the frame — so a plain
        /// <c>FindFirstObjectByType</c> still returns the outgoing page, and still sees it as
        /// non-null, for the rest of the frame it was closed in. That matters because
        /// <c>MenuChoiceUI.Pick</c> closes and routes in the same breath: a choice leading to
        /// another page of the same type (Story ▸ Multiplayer, the only such pair today) would find
        /// the page it had just closed, hand it back as "already open", build nothing, and drop the
        /// player back on the main menu. The flag is what separates a page that is up from one that
        /// is merely not yet collected; searching with <see cref="FindObjectsInactive.Include"/>
        /// keeps that the only thing this depends on, rather than the active state
        /// <see cref="Close"/> and <see cref="HandOff"/> happen to leave behind.
        /// </para>
        /// </summary>
        protected static T Existing<T>() where T : MenuScreen
        {
            T found = FindFirstObjectByType<T>(FindObjectsInactive.Include);
            return found != null && !found.closing ? found : null;
        }

        /// <summary>
        /// Puts the screen up, recording <paramref name="menu"/> as <see cref="Menu"/> first.
        ///
        /// Deliberately not <c>Awake</c>: <c>AddComponent</c> runs Awake before the caller's next
        /// statement, so a screen that built itself there would build before its owner and its
        /// arguments had been handed to it. Every subclass's static Open therefore assigns its own
        /// fields first and calls this last.
        /// </summary>
        protected void Present(MainMenuUI menu)
        {
            Menu = menu;
            Present();
        }

        /// <summary>
        /// Puts the screen up without recording an owner.
        ///
        /// For a page that keeps its own owner reference rather than reading <see cref="Menu"/> —
        /// every screen predating that property still does. New pages should prefer
        /// <see cref="Present(MainMenuUI)"/> instead, so <see cref="Menu"/> and
        /// <see cref="EntryPrefab"/> are usable from <see cref="Build"/>.
        /// </summary>
        protected void Present()
        {
            UIBuilder.EnsureEventSystem();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            HideOtherCanvases();
            Surface = CreateSurface();
            Build();
        }

        /// <summary>
        /// Restores the menu and goes away — the Back/Cancel route.
        ///
        /// Switched off before it is destroyed, for the reason <see cref="HandOff"/> gives: Destroy
        /// does not take effect until the end of the frame, and a page opened in the meantime —
        /// which is exactly what a choice that routes onward does — would otherwise draw over this
        /// one's text for the rest of the frame, both of them at the same sorting order.
        /// </summary>
        public void Close()
        {
            closing = true;
            RestoreOtherCanvases();
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        /// <summary>
        /// Goes away ahead of a scene load, without restoring what it hid.
        ///
        /// The menu scene is about to be unloaded, so putting its canvases back would only make them
        /// flash. The canvas is switched off rather than merely destroyed because Destroy does not
        /// take effect until the end of the frame, and a loading screen raised in the meantime would
        /// come up underneath this screen's text.
        /// </summary>
        protected void HandOff()
        {
            closing = true;
            if (canvasObject != null) canvasObject.SetActive(false);
            Destroy(gameObject);
        }

        // ----------------------------------------------------------------- page skeleton
        //
        // Shared so the screens the menu opens agree with each other and with the menu itself. Every
        // one of them is a title over a left-aligned column of entries on the same 3D set, and each
        // page choosing its own copy of the anchors and constants is how a flow ends up looking like
        // several.

        /// <summary>
        /// A row pinned a fixed distance from the top of the page, at the shared column inset.
        /// <see cref="Title"/> uses this for the page's heading; a page with its own row above the
        /// column (a subtitle, a status line) can use it directly.
        /// </summary>
        protected static RectTransform PinnedRow(RectTransform parent, float fromTop, float height) =>
            UIBuilder.PinnedTop(parent, "Row", MenuEntry.ColumnX, fromTop, MenuEntry.ColumnWidth, height);

        /// <summary>
        /// The page's title, white and pinned at <see cref="MenuEntry.TitleTop"/>. Above the
        /// horizon, which is fine and deliberate — see <see cref="MenuEntry.TitleTop"/>'s own doc.
        /// </summary>
        protected void Title(string text)
        {
            RectTransform titleRect = PinnedRow(Surface, MenuEntry.TitleTop, MenuEntry.TitleHeight);
            UIBuilder.Label(titleRect, text, MenuEntry.TitleSize, MenuEntry.Title,
                            TextAlignmentOptions.Left, FontStyles.Bold);
        }

        /// <summary>
        /// The page's column of entries: anchored at <see cref="MenuEntry.ColumnX"/> /
        /// <see cref="MenuEntry.ContentTop"/>, below <see cref="MenuEntry.Horizon"/> because entries
        /// are dark navy and only read against ground. Laid out with <see cref="UIBuilder.Column"/>
        /// plus a vertical <see cref="ContentSizeFitter"/>, so children stack from the top and the
        /// column's own height follows its content.
        /// </summary>
        protected RectTransform Column(float spacing = 6f)
        {
            RectTransform column = UIBuilder.Rect("Column", Surface);
            column.anchorMin = column.anchorMax = new Vector2(0f, 1f);
            column.pivot = new Vector2(0f, 1f);
            column.anchoredPosition = new Vector2(MenuEntry.ColumnX, MenuEntry.ContentTop);
            column.sizeDelta = new Vector2(MenuEntry.ColumnWidth, 0f);

            UIBuilder.Column(column, spacing);
            var fitter = column.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return column;
        }

        /// <summary>
        /// One menu entry inside <paramref name="column"/>, cloned from <see cref="EntryPrefab"/> —
        /// or, when the menu was opened without one, built from scratch in the same palette by
        /// <see cref="MenuEntry.Create"/>.
        /// </summary>
        protected Button Entry(RectTransform column, string name, string label, UnityAction onClick) =>
            MenuEntry.Create(EntryPrefab, column, name, label, MenuEntry.ActionSize, ActionHeight,
                             onClick, out _);

        // ----------------------------------------------------------------- canvas mechanics

        private RectTransform CreateSurface()
        {
            canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            // The reference resolution MainMenu.unity's own canvas uses, so type sized against the
            // menu's 90pt entries scales identically.
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return UIBuilder.Fill(UIBuilder.Rect("Page", canvasObject.transform));
        }

        private void HideOtherCanvases()
        {
            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (canvas.transform.IsChildOf(transform)) continue;
                if (!canvas.enabled) continue;

                canvas.enabled = false;
                hidden.Add(canvas);
            }
        }

        private void RestoreOtherCanvases()
        {
            foreach (Canvas canvas in hidden)
                if (canvas != null)
                    canvas.enabled = true;

            hidden.Clear();
        }
    }
}
