// What the player needs to know to sail the thing, drawn only while they are standing on it.
//
// Sailing is the one vehicle skill in this game that cannot be done by looking out of the window.
// Everything a player does on this deck is chosen against the wind — which sheet to ease, which way
// to turn, whether the heading they want is even reachable — and the wind is invisible. Without a
// wind indicator the whole rig is a set of levers with no feedback, and "work the winches until it
// goes" is the only strategy available. That is precisely the failure the interaction prompts fixed
// for the individual controls, one level up.
//
// Three things, and no more: where the wind is relative to the bow, how fast the craft is going,
// and whether the sails are drawing or flogging. Everything else — sheet positions, cant, rudder —
// is already on the control you are looking at, via IInteractionReadout.
//
// Lives on the craft rather than in the scene, and builds its own canvas, so it ships with the
// prefab and cannot be quietly left out of a scene the way the interaction prompts once were.
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Presentation;
using SpaceGame.Vehicles.DuneFoil;

namespace SpaceGame.Vehicles
{
    [DefaultExecutionOrder(250)]    // after the hull has moved and the carrier has run
    public class DuneFoilHUD : MonoBehaviour
    {
        [Header("Craft")]
        [SerializeField] private DuneFoilLocomotion locomotion;
        [SerializeField] private SailRig rig;

        [Tooltip("Volume that counts as 'aboard'. The HUD is only drawn for a player inside it.")]
        [SerializeField] private Collider aboardVolume;

        [Header("Layout")]
        [Tooltip("Where the dial sits, as a fraction of the screen from the bottom-left. Top " +
                 "right, out of the way of the interaction prompt under the crosshair.")]
        [SerializeField] private Vector2 screenAnchor = new Vector2(0.90f, 0.86f);

        [SerializeField] private float dialRadius = 62f;

        [Header("Appearance")]
        [SerializeField] private Color dialColor = new Color(0.04f, 0.05f, 0.07f, 0.62f);
        [SerializeField] private Color windColor = new Color(0.45f, 0.85f, 1f);
        [SerializeField] private Color textColor = new Color(0.88f, 0.92f, 0.96f);
        [SerializeField] private Color warnColor = new Color(1f, 0.72f, 0.35f);

        [SerializeField, Min(0.01f)] private float fadeDuration = 0.25f;

        private CanvasGroup group;
        private RectTransform needle;
        private RectTransform bow;
        private TextMeshProUGUI speedText;
        private TextMeshProUGUI stateText;
        private float alpha;
        private float needleAngle;

        private void Awake()
        {
            if (locomotion == null) locomotion = GetComponent<DuneFoilLocomotion>();
            if (rig == null) rig = GetComponent<SailRig>();
            if (aboardVolume == null)
            {
                foreach (Collider c in GetComponentsInChildren<Collider>(true))
                    if (c.name == "COL_CarryVolume") { aboardVolume = c; break; }
            }
        }

        private void LateUpdate()
        {
            bool aboard = IsLocalPlayerAboard();

            if (!aboard)
            {
                Fade(0f);
                return;
            }

            if (group == null) BuildWidget();
            Fade(1f);
            Refresh();
        }

        /// <summary>
        /// Is the player whose screen this is standing on the deck?
        ///
        /// Through <see cref="GameplayMenuScope.LocalPlayerTransform"/>, which is the project's one
        /// answer to "which of these bodies is mine" — the parameterless overload, because this HUD
        /// lives on the craft rather than on a player and has no owner to read off its own parents.
        ///
        /// It used to be <c>FindFirstObjectByType&lt;Interactor&gt;</c>, copied from what is now
        /// <c>VisorReticle</c>. Alone in a session that is indistinguishable from correct,
        /// and in a crew it is a coin toss: every remote player carries an Interactor on an active
        /// GameObject too, so whichever of them happened to spawn first decided whose deck this dial
        /// answered to. A player left standing in the sand would watch the craft sail away with its
        /// wind gauge on their screen.
        /// </summary>
        private bool IsLocalPlayerAboard()
        {
            if (aboardVolume == null) return false;

            Transform player = GameplayMenuScope.LocalPlayerTransform;
            if (player == null) return false;

            Vector3 p = player.position;
            return (aboardVolume.ClosestPoint(p) - p).sqrMagnitude < 1e-4f;
        }

        private void Fade(float target)
        {
            alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / fadeDuration);
            if (group != null) group.alpha = alpha;
        }

        private void Refresh()
        {
            if (rig == null || locomotion == null) return;

            // The bearing the APPARENT wind comes from, relative to the bow. Apparent rather than
            // true, because apparent is the wind the sails are actually working in and the one
            // that decides whether a heading is sailable — and on a craft that does forty metres a
            // second the difference is most of a right angle.
            Vector3 heading = transform.forward;
            Vector3 from = -rig.ApparentWind;
            float bearing = from.sqrMagnitude > 1e-4f
                ? SailAerodynamics.SignedAngle(heading, from)
                : 0f;

            // The needle is smoothed. Gusts wander the wind a few degrees several times a second
            // and a needle that answers all of it is unreadable.
            needleAngle = Mathf.LerpAngle(needleAngle, bearing,
                                          1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            if (needle != null) needle.localRotation = Quaternion.Euler(0f, 0f, -needleAngle);

            if (speedText != null)
                speedText.text = $"{locomotion.Speed:F1} m/s";

            if (stateText == null) return;

            float off = Mathf.Abs(bearing);
            bool inIrons = off < SailAerodynamics.NoGoHalfAngle;
            float luffing = rig.Luffing;

            if (inIrons)
            {
                stateText.text = "IN IRONS — bear away";
                stateText.color = warnColor;
            }
            else if (luffing > 0.4f)
            {
                stateText.text = "luffing — trim";
                stateText.color = warnColor;
            }
            else
            {
                stateText.text = off < 75f ? "close hauled"
                               : off < 115f ? "beam reach"
                               : off < 155f ? "broad reach"
                               : "running";
                stateText.color = textColor;
            }
        }

        // ----------------------------------------------------------------------
        // Built in code, like VisorReticle, and for the same reason: there is no authored
        // canvas to hang it under, because the player is spawned rather than placed.

        private void BuildWidget()
        {
            GameObject canvasObject = new GameObject("DuneFoilHUD",
                typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 49;   // just under the interaction prompt

            UIScale.Configure(canvasObject.GetComponent<CanvasScaler>());

            group = canvasObject.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            RectTransform dial = NewRect("Dial", canvasObject.transform);
            dial.anchorMin = dial.anchorMax = screenAnchor;
            dial.pivot = new Vector2(0.5f, 0.5f);
            dial.anchoredPosition = Vector2.zero;
            dial.sizeDelta = new Vector2(dialRadius * 2f, dialRadius * 2f);

            Image face = dial.gameObject.AddComponent<Image>();
            face.color = dialColor;
            face.raycastTarget = false;

            // The bow marker: a fixed pip at the top of the dial, so the needle is read against
            // the craft rather than against the screen.
            bow = NewRect("Bow", dial);
            Anchor(bow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(-3f, -10f), new Vector2(3f, 0f));
            Image bowPip = bow.gameObject.AddComponent<Image>();
            bowPip.color = textColor;
            bowPip.raycastTarget = false;

            // The needle rotates about the dial centre and points at where the wind comes FROM.
            RectTransform pivot = NewRect("NeedlePivot", dial);
            Anchor(pivot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                   new Vector2(-dialRadius, -dialRadius), new Vector2(dialRadius, dialRadius));
            needle = pivot;

            RectTransform shaft = NewRect("Needle", pivot);
            shaft.anchorMin = new Vector2(0.5f, 0.5f);
            shaft.anchorMax = new Vector2(0.5f, 0.5f);
            shaft.pivot = new Vector2(0.5f, 0f);
            shaft.anchoredPosition = Vector2.zero;
            shaft.sizeDelta = new Vector2(5f, dialRadius - 8f);
            Image shaftImage = shaft.gameObject.AddComponent<Image>();
            shaftImage.color = windColor;
            shaftImage.raycastTarget = false;

            speedText = NewText("Speed", dial, 22f, FontStyles.Bold, textColor);
            Anchor(speedText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                   new Vector2(0f, -14f), new Vector2(0f, 12f));

            stateText = NewText("State", dial, 18f, FontStyles.Normal, textColor);
            Anchor(stateText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                   new Vector2(-30f, -26f), new Vector2(30f, -4f));
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, float size,
                                               FontStyles style, Color colour)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.fontStyle = style;
            text.color = colour;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            return text;
        }

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max,
                                   Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        /// <summary>Wire it up. Used by the prefab builder.</summary>
        public void Bind(DuneFoilLocomotion craft, SailRig sailRig, Collider aboard)
        {
            locomotion = craft;
            rig = sailRig;
            aboardVolume = aboard;
        }
    }
}
