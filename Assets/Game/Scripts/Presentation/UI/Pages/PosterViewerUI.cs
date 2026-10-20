// A poster held up to the eye: one printed sheet filling most of the screen over a dark backdrop.
//
// Opened by WantedPoster when the player right-clicks one. Reading is local; a poster that offers something
// (IPosterOffer: a bounty) shows its prompt under the sheet, takes it on Y, and stamps the sheet CLAIMED once it has been handed in. Built in code like every
// overlay here (UI.md): a lazily built DontDestroyOnLoad singleton that holds GameplayMenuScope
// while it is up.
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    public sealed class PosterViewerUI : MonoBehaviour
    {
        private const float SheetHeight = 900f;
        private const float OpenSeconds = 0.14f;
        private const int SortingOrder = 2030;
        private const float StampAngle = 14f;
        private const int StampSize = 120;

        private static PosterViewerUI instance;

        public static bool IsOpen => instance != null && instance.open;

        private bool open;
        private bool built;
        private float visibility;
        private int openedFrame;

        private CanvasGroup group;
        private RectTransform sheet;
        private RawImage picture;
        private TextMeshProUGUI prompt;
        private TextMeshProUGUI stamp;
        private IPosterOffer offer;

        /// <summary>
        /// Put <paramref name="poster"/> in front of the local player until they put it down, with what it
        /// <paramref name="offers"/> (null for nothing).
        /// </summary>
        public static void Show(Texture poster, IPosterOffer offers = null)
        {
            if (poster == null) return;

            if (instance == null)
            {
                var go = new GameObject("PosterViewerUI");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<PosterViewerUI>();
            }

            instance.ShowInternal(poster, offers);
        }

        private void ShowInternal(Texture poster, IPosterOffer offers)
        {
            if (!open)
            {
                if (!GameplayMenuScope.Enter(this)) return;
                open = true;
            }

            if (!built) Build();

            offer = offers;
            ShowOffer();
            picture.texture = poster;
            sheet.sizeDelta = new Vector2(SheetHeight * poster.width / poster.height, SheetHeight);

            // The right-click that opened the sheet is still "pressed this frame" when Update runs.
            openedFrame = Time.frameCount;
            group.gameObject.SetActive(true);
            group.blocksRaycasts = true;
        }

        private void Close()
        {
            if (!open) return;

            open = false;
            offer = null;
            group.blocksRaycasts = false;
            GameplayMenuScope.Exit(this);
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            GameplayMenuScope.Exit(this);
        }

        private void Update()
        {
            if (open && Time.frameCount != openedFrame)
            {
                ShowOffer();
                if (TakePressed() && offer != null && offer.CanTake)
                {
                    offer.Take();
                    Close();
                }
                else if (PutDownPressed()) Close();
            }

            if (!built) return;

            float target = open ? 1f : 0f;
            if (Mathf.Approximately(visibility, target)) return;

            // Unscaled: the scope stops the clock in a solo session.
            visibility = Mathf.MoveTowards(visibility, target, Time.unscaledDeltaTime / OpenSeconds);
            float eased = visibility * visibility * (3f - 2f * visibility);
            group.alpha = eased;
            sheet.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, eased);

            if (visibility <= 0f) group.gameObject.SetActive(false);
        }

        private void ShowOffer()
        {
            if (!built) return;
            prompt.text = offer != null ? offer.Prompt : string.Empty;
            stamp.text = offer != null ? offer.Stamp : string.Empty;
            if (offer != null) stamp.color = offer.StampColour;
        }

        private static bool TakePressed() => Keyboard.current != null && Keyboard.current.yKey.wasPressedThisFrame;

        private static bool PutDownPressed()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            return (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
                   (mouse != null && (mouse.rightButton.wasPressedThisFrame || mouse.leftButton.wasPressedThisFrame));
        }

        private void Build()
        {
            built = true;

            var canvasGo = new GameObject("PosterCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            UIScale.Configure(canvasGo.GetComponent<CanvasScaler>());

            group = canvasGo.GetComponent<CanvasGroup>();
            group.alpha = 0f;

            var root = (RectTransform)canvasGo.transform;
            UIBuilder.Solid(UIBuilder.Fill(UIBuilder.Rect("Backdrop", root)), UITheme.Backdrop);

            sheet = UIBuilder.Rect("Sheet", root);
            sheet.anchorMin = sheet.anchorMax = new Vector2(0.5f, 0.5f);
            sheet.anchoredPosition = new Vector2(0f, 20f);
            picture = sheet.gameObject.AddComponent<RawImage>();
            picture.raycastTarget = false;

            RectTransform hint = UIBuilder.Rect("Hint", root);
            hint.anchorMin = Vector2.zero;
            hint.anchorMax = new Vector2(1f, 0f);
            hint.pivot = new Vector2(0.5f, 0f);
            hint.anchoredPosition = new Vector2(0f, 28f);
            hint.sizeDelta = new Vector2(0f, 30f);
            UIBuilder.Label(hint, "CLICK OR ESC TO PUT IT DOWN", UITheme.CaptionSize, UITheme.Faint,
                TextAlignmentOptions.Center, FontStyles.Bold);

            RectTransform offerLine = UIBuilder.Rect("Offer", root);
            offerLine.anchorMin = Vector2.zero;
            offerLine.anchorMax = new Vector2(1f, 0f);
            offerLine.pivot = new Vector2(0.5f, 0f);
            offerLine.anchoredPosition = new Vector2(0f, 64f);
            offerLine.sizeDelta = new Vector2(0f, 34f);
            prompt = UIBuilder.Label(offerLine, string.Empty, UITheme.LabelSize, UITheme.AccentWarm,
                TextAlignmentOptions.Center, FontStyles.Bold);

            RectTransform stampRect = UIBuilder.Fill(UIBuilder.Rect("Stamp", sheet));
            stampRect.localRotation = Quaternion.Euler(0f, 0f, StampAngle);
            stamp = UIBuilder.Label(stampRect, string.Empty, StampSize, UITheme.Danger, TextAlignmentOptions.Center, FontStyles.Bold);

            visibility = 0f;
            group.gameObject.SetActive(false);
        }
    }
}
