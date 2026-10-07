// A name over every other player's head.
//
// Nothing new crosses the wire for this. PlayerIdentity already replicates each player's chosen
// name to every peer, and already answers with a "Player N" stand-in for the moment between a
// player spawning and their name arriving — so a nameplate is a view of state that was there all
// along.
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Voice;

namespace SpaceGame.Presentation
{
    [DisallowMultipleComponent]
    public class PlayerNameplates : MonoBehaviour
    {
        [Header("Look")]
        [SerializeField] private Color nameColor = new(0.88f, 0.94f, 1f);
        [SerializeField] private float fontSize = 26f;

        [Tooltip("Canvas units above the projected head point.")]
        [SerializeField] private float verticalOffset = 8f;

        [Header("Speaking")]
        [Tooltip("The pip beside a name while that player is talking, where there are no teams. " +
                 "In a versus match it takes the speaker's team colour instead. Matches UITheme.Accent.")]
        [SerializeField] private Color speakingColor = new(0.239f, 0.549f, 0.949f);

        [SerializeField] private float speakingPipSize = 14f;

        [Tooltip("Canvas units between the pip and the first letter of the name.")]
        [SerializeField] private float speakingPipGap = 16f;

        [Header("Range")]
        [Tooltip("Hide nameplates farther away than this (m). 0 = no limit.")]
        [SerializeField] private float maxDistance = 120f;

        [Tooltip("Nameplates start fading at this distance and are gone by maxDistance.")]
        [SerializeField] private float fadeStartDistance = 60f;

        [Tooltip("Faintest a nameplate goes before it is culled entirely.")]
        [SerializeField, Range(0f, 1f)] private float minAlpha = 0.25f;

        [Header("Occlusion")]
        [Tooltip("Hide a name when something solid stands between the camera and that player.")]
        [SerializeField] private bool hideBehindGeometry = true;

        [SerializeField] private LayerMask occluders = ~0;

        /// <summary>
        /// How far along the ray to start the occlusion test, so the local player's own body — which
        /// the camera sits inside — never counts as a wall.
        /// </summary>
        private const float NearClearance = 0.6f;

        /// <summary>
        /// How far short of the head to stop, so the target's own collider never blocks the view of
        /// their own name. Cheaper and steadier than filtering hits by hierarchy.
        /// </summary>
        private const float FarClearance = 0.9f;

        private sealed class Plate
        {
            public TextMeshProUGUI Text;
            public string Shown;        // last string pushed into TMP
            public float HeadOffset;    // metres above the player's origin
            public VoicePip Pip;        // child of Text, so it is destroyed with it
            public float HalfWidth;     // half the rendered name, for placing the pip beside it
        }

        private readonly Dictionary<PlayerIdentity, Plate> plates = new();
        private readonly HashSet<PlayerIdentity> seenThisFrame = new();
        private readonly List<PlayerIdentity> stale = new();

        private void OnDisable()
        {
            foreach (Plate plate in plates.Values)
                if (plate.Text != null) Destroy(plate.Text.gameObject);

            plates.Clear();
        }

        private void LateUpdate()
        {
            WorldOverlay overlay = WorldOverlay.Instance;
            if (overlay == null) return;

            Camera eye = overlay.Eye;
            if (eye == null)
            {
                HideAll();
                return;
            }

            Vector3 eyePosition = eye.transform.position;
            IReadOnlyList<PlayerIdentity> roster = PlayerIdentity.All;

            seenThisFrame.Clear();

            for (int i = 0; i < roster.Count; i++)
            {
                PlayerIdentity player = roster[i];

                // IsOwner is the local player — you cannot see your own head in first person, and in
                // third person a name pinned to yourself only hides the world behind it.
                if (player == null || !player.IsSpawned || player.IsOwner) continue;

                seenThisFrame.Add(player);
                Draw(overlay, eyePosition, player);
            }

            Prune();
        }

        private void Draw(WorldOverlay overlay, Vector3 eyePosition, PlayerIdentity player)
        {
            if (!plates.TryGetValue(player, out Plate plate))
            {
                plate = new Plate
                {
                    Text = WorldOverlay.CreateLabel(overlay.Layer, "Nameplate", fontSize, 460f),
                    // Measured once. A player's height does not change, and walking every collider
                    // under a rigged character each frame for every player would not be free.
                    HeadOffset = WorldOverlay.HeadOffset(player.gameObject),
                };
                plate.Text.color = nameColor;
                plate.Pip = new VoicePip(plate.Text.rectTransform, speakingPipSize, speakingColor);
                plates.Add(player, plate);
            }

            Vector3 head = player.transform.position + Vector3.up * plate.HeadOffset;
            float distance = Vector3.Distance(eyePosition, head);

            if (maxDistance > 0f && distance > maxDistance)
            {
                Hide(plate);
                return;
            }

            if (!overlay.Project(head, out Vector2 point) || !overlay.IsOnScreen(point, 120f))
            {
                Hide(plate);
                return;
            }

            if (hideBehindGeometry && IsOccluded(eyePosition, head, distance))
            {
                Hide(plate);
                return;
            }

            string name = player.DisplayName;
            if (plate.Shown != name)
            {
                plate.Text.text = name;
                plate.Shown = name;

                // Measured on change rather than every frame; the name is what decides it.
                plate.HalfWidth = plate.Text.GetPreferredValues(name).x * 0.5f;
            }

            plate.Text.enabled = true;
            plate.Text.rectTransform.anchoredPosition = point + new Vector2(0f, verticalOffset);
            plate.Text.alpha = Fade(distance);

            // The same client id voice relays under — OwnerClientId is what the netcode session
            // numbers this player by, so no lookup table stands between the plate and the voice.
            bool speaking = VoiceSession.IsSpeaking(player.OwnerClientId);
            plate.Pip.Show(speaking);

            if (!speaking) return;

            // The same team colour as the speaking list, so one person reads as one team everywhere.
            plate.Pip.SetColor(SpeakerTeams.TryColorOf(player.Team, out Color team) ? team : speakingColor);
            plate.Pip.Rect.anchoredPosition = new Vector2(-(plate.HalfWidth + speakingPipGap), 0f);
            plate.Pip.Animate(Time.unscaledTime, plate.Text.alpha);
        }

        private float Fade(float distance)
        {
            if (maxDistance <= 0f || fadeStartDistance >= maxDistance) return 1f;
            if (distance <= fadeStartDistance) return 1f;

            float t = (distance - fadeStartDistance) / (maxDistance - fadeStartDistance);
            return Mathf.Lerp(1f, minAlpha, Mathf.Clamp01(t));
        }

        private bool IsOccluded(Vector3 from, Vector3 head, float distance)
        {
            float span = distance - NearClearance - FarClearance;
            if (span <= 0f) return false;

            Vector3 direction = (head - from) / distance;

            return Physics.Raycast(from + direction * NearClearance, direction, span,
                                   occluders, QueryTriggerInteraction.Ignore);
        }

        private void HideAll()
        {
            foreach (Plate plate in plates.Values) Hide(plate);
        }

        /// <summary>
        /// Hides a plate and its speaking pip together. Disabling the TMP component alone does not
        /// reach the pip — it is a child GameObject, and would go on breathing over empty space
        /// beside a name that is no longer drawn.
        /// </summary>
        private static void Hide(Plate plate)
        {
            if (plate.Text != null) plate.Text.enabled = false;
            plate.Pip?.Show(false);
        }

        /// <summary>
        /// Drops plates for players who left, died out of the roster, or became the local player.
        /// Driven off what was actually drawn this frame rather than off the roster, so there is
        /// only ever one rule deciding whether a plate should exist.
        /// </summary>
        private void Prune()
        {
            foreach (KeyValuePair<PlayerIdentity, Plate> entry in plates)
            {
                if (seenThisFrame.Contains(entry.Key)) continue;

                if (entry.Value.Text != null) Destroy(entry.Value.Text.gameObject);
                stale.Add(entry.Key);
            }

            for (int i = 0; i < stale.Count; i++) plates.Remove(stale[i]);
            stale.Clear();
        }
    }
}
