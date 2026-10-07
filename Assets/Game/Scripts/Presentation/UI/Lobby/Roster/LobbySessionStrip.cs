using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The strip of session controls along the very top of the roster page: the code, a Copy
    /// action, and the host's privacy toggle.
    ///
    /// <para>
    /// They used to be a stack down the left, under the horizon, where dark navy reads against
    /// sand. Up here they are over sky instead — so the two plain labels carry a drop shadow, the
    /// same trick the nameplates use, and everything is set small. Small is the point: the code
    /// and the privacy toggle are things you glance at once and then ignore, and the astronauts
    /// are what the page is actually for.
    /// </para>
    ///
    /// <para>
    /// The label on the toggle is never written here. It is rendered from the session the owner
    /// hands back, so what the toggle says is what is actually in force rather than what was last
    /// asked for — which matters precisely when the update fails.
    /// </para>
    /// </summary>
    internal sealed class LobbySessionStrip
    {
        internal const float Top = -36f;
        internal const float Height = 48f;

        /// <summary>Small. This strip is a caption, not a heading. Shared with
        /// <see cref="LobbyTeamRulesStrip"/> so the whole bar is set in one voice.</summary>
        internal const int CaptionSize = 24;

        internal const int ValueSize = 34;

        // Left-to-right slots inside the strip, measured from the shared column inset.
        //
        // Widths are deliberate, not padding. UIBuilder labels are built with word wrap off and
        // Ellipsis overflow, so a slot narrower than its text does not overflow — it silently
        // TRUNCATES, which is how "CODE" first shipped reading as "CO…". Each slot below is sized to
        // its longest possible content: the caption to "CODE", the value to a six-character lobby
        // code (142px at this size), the privacy slot only just wide enough to hold "Private" and
        // its on/off state, so the two read as one control rather than as a word and a distant
        // switch.
        private const float CodeCaptionX = 0f;
        private const float CodeCaptionWidth = 120f;
        private const float CodeValueX = 124f;
        private const float CodeValueWidth = 160f;
        private const float CopyX = 296f;
        private const float CopyWidth = 120f;
        private const float PrivacyX = 438f;

        // Sized backwards from the two things inside it. MenuField.Trailing right-ALIGNS the state
        // against the slot's right edge, so the only way to bring "off" nearer "Private" is to make
        // the SLOT narrower. The floor is "Private" at 120px plus Trailing's own 24px inset plus the
        // state band, so 190 is about as tight as this goes before the word itself truncates.
        private const float PrivacyWidth = 190f;
        private const float PrivacyStateWidth = 40f;

        private const string NoCode = "—";

        internal static readonly Vector2 ShadowOffset = new(2f, -2f);

        // The strip sits over sky, so the on/off state reads in white too — a translucent white for
        // "off" carries the same "not in force" meaning the navy version did over sand.
        private static readonly Color PrivacyOn = Color.white;
        private static readonly Color PrivacyOff = new(1f, 1f, 1f, 0.6f);

        private readonly Action<bool> onSetPrivacy;

        private readonly TextMeshProUGUI code;

        /// <summary>The navy copy behind <see cref="code"/>. Written with it or the shadow goes stale.</summary>
        private readonly TextMeshProUGUI codeShadow;

        private readonly GameObject copyAction;
        private readonly GameObject privacyRow;
        private readonly TextMeshProUGUI privacyState;

        /// <summary>Mirrors the lobby's own flag so the toggle knows which way to flip.</summary>
        private bool isPrivate;

        /// <summary>The whole strip, so the owner can lock it while the session is being created.</summary>
        public CanvasGroup Group { get; }

        /// <param name="onSetPrivacy">Called with the privacy the host just asked for.</param>
        public LobbySessionStrip(RectTransform page, GameObject entryPrefab, Action onCopy, Action<bool> onSetPrivacy)
        {
            this.onSetPrivacy = onSetPrivacy;

            RectTransform bar = UIBuilder.PinnedTop(page, "TopBar", MenuEntry.ColumnX, Top,
                                                    MenuEntry.ColumnWidth, Height);
            Group = bar.gameObject.AddComponent<CanvasGroup>();

            UIBuilder.ShadowedLabel(UIBuilder.Slice(bar, "CodeCaption", CodeCaptionX, CodeCaptionWidth),
                                    "CODE", CaptionSize, MenuEntry.Title, MenuEntry.Idle, ShadowOffset,
                                    TextAlignmentOptions.Left, out _);

            code = UIBuilder.ShadowedLabel(UIBuilder.Slice(bar, "CodeValue", CodeValueX, CodeValueWidth),
                                           NoCode, ValueSize, MenuEntry.Title, MenuEntry.Idle, ShadowOffset,
                                           TextAlignmentOptions.Left, out codeShadow);

            Button copy = MenuEntry.Create(entryPrefab, UIBuilder.Slice(bar, "CopySlot", CopyX, CopyWidth),
                                           "CopyButton", "Copy", ValueSize, Height, () => onCopy(),
                                           out TextMeshProUGUI copyLabel);
            MenuEntry.MakeLight(copy, copyLabel);
            copyAction = copy.gameObject;

            RectTransform privacySlot = UIBuilder.Slice(bar, "PrivacySlot", PrivacyX, PrivacyWidth);
            privacyRow = privacySlot.gameObject;

            Button toggle = MenuEntry.Create(entryPrefab, privacySlot, "PrivacyButton", "Private",
                                             ValueSize, Height, TogglePrivacy, out TextMeshProUGUI label);
            MenuEntry.MakeLight(toggle, label);

            privacyState = MenuField.Trailing(toggle, label, "off", PrivacyStateWidth, PrivacyOff);
        }

        /// <summary>Redraws from the session. Privacy is the host's alone: a client shown the toggle gets a control whose whole behaviour is to refuse.</summary>
        public void Render(string sessionCode, bool sessionIsPrivate, bool isHost)
        {
            SetCode(string.IsNullOrEmpty(sessionCode) ? NoCode : sessionCode);
            if (copyAction != null) copyAction.SetActive(!string.IsNullOrEmpty(sessionCode));

            if (privacyRow != null) privacyRow.SetActive(isHost);

            isPrivate = sessionIsPrivate;

            if (privacyState != null)
            {
                privacyState.text = isPrivate ? "on" : "off";
                privacyState.color = isPrivate ? PrivacyOn : PrivacyOff;
            }
        }

        private void TogglePrivacy() => onSetPrivacy(!isPrivate);

        /// <summary>Writes the code to both copies, so the drop shadow cannot fall behind.</summary>
        private void SetCode(string value)
        {
            if (code != null) code.text = value;
            if (codeShadow != null) codeShadow.text = value;
        }
    }
}
