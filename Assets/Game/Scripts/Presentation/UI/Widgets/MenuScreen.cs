using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// A full-screen menu page that shows its text over the live scene rather than over a panel.
    ///
    /// This is the shape MinigameConfigUI and MultiplayerChoiceUI arrived at independently, and each
    /// carries its own copy of: spawn a canvas, switch every OTHER canvas off, build a column of
    /// text, put the canvases back on the way out. Written down once here so a third screen is a
    /// subclass rather than a third copy of the same twenty lines.
    ///
    /// The menu is switched off rather than dimmed or covered, which is what keeps the 3D menu scene
    /// visible behind the words. A disabled Canvas also stops its GraphicRaycaster, so the buttons
    /// underneath cannot be clicked through this screen either.
    /// </summary>
    public abstract class MenuScreen : MonoBehaviour
    {
        /// <summary>Above the menu's own canvases, which sort at 0.</summary>
        protected virtual int SortingOrder => 900;

        /// <summary>The full-screen rect this screen builds into. Valid from <see cref="Build"/> on.</summary>
        protected RectTransform Surface { get; private set; }

        private readonly List<Canvas> hidden = new();
        private GameObject canvasObject;

        /// <summary>Builds the page. Called once, after the subclass's own fields are assigned.</summary>
        protected abstract void Build();

        /// <summary>
        /// Puts the screen up.
        ///
        /// Deliberately not <c>Awake</c>: <c>AddComponent</c> runs Awake before the caller's next
        /// statement, so a screen that built itself there would build before its owner and its
        /// arguments had been handed to it. Every subclass's static Open therefore assigns its
        /// fields first and calls this last.
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

        /// <summary>Restores the menu and goes away — the Back/Cancel route.</summary>
        public void Close()
        {
            RestoreOtherCanvases();
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
            if (canvasObject != null) canvasObject.SetActive(false);
            Destroy(gameObject);
        }

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
