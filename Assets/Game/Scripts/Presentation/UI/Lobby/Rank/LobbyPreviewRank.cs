using System;
using UnityEngine;
using SpaceGame.Core.Lobbies;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The rank of astronauts standing in the lobby: one cluster per team, each in its team's
    /// colour (or one line in a story lobby, each in their own suit colour), with names above their
    /// heads, a team nameplate above each cluster you click to join it, and a colour cycler under
    /// your own figure.
    ///
    /// <para>
    /// They stand in the <b>real menu scene</b> rather than in a RenderTexture, lit by the menu's own
    /// sun and casting real shadows on the sand. That is the whole reason they look like they belong
    /// there, and it costs one authored empty — <see cref="AnchorName"/> — placed by hand where the
    /// rank should stand.
    /// </para>
    ///
    /// <para>
    /// This class is the conductor. The figures are <see cref="LobbyRankFigures"/>; where everyone
    /// stands is <see cref="RankLayout"/>'s job; the camera is <see cref="LobbyPreviewCamera"/>'s;
    /// and the three overlays — names, team plates, the cycler — each keep themselves over their
    /// world point through <see cref="LobbyOverlayLayer"/>. A MonoBehaviour, unlike
    /// <see cref="LobbyRosterView"/>, because those overlays track world positions and that needs
    /// a frame hook.
    /// </para>
    /// </summary>
    public class LobbyPreviewRank : MonoBehaviour
    {
        /// <summary>
        /// The empty in MainMenu.unity the rank stands on. Its position is the centre of the line and
        /// its right vector is the direction the line runs. Found by name because a runtime-built
        /// screen has no Inspector to be handed a reference. Placed by Tools ▸ SpaceGame ▸ Menus ▸
        /// Setup Lobby Preview.
        /// </summary>
        public const string AnchorName = "LobbyPreviewAnchor";

        /// <summary>An empty in MainMenu.unity holding the pose the camera takes while the lobby is up.</summary>
        public const string CameraViewName = "LobbyCameraView";

        /// <summary>How far above the head bone the name floats.</summary>
        private const float LabelLift = 0.42f;

        /// <summary>
        /// How far below the anchor line the cycler sits, in metres. Close under the boots, where
        /// it reads as belonging to the figure above it rather than floating in the sand.
        /// </summary>
        private const float CyclerDrop = 0.4f;

        /// <summary>
        /// How far above a team's centre its plate floats, in metres. High enough to clear a
        /// nameplate over a head roughly 1.8m tall without drifting so far up that it reads as
        /// unrelated. Not measured against a capture — flag this if it reads too high or too low.
        /// </summary>
        private const float PlateLift = 2.35f;

        // Where an invented anchor goes when the scene has none: this far in front of the camera,
        // dropped onto whatever is under it.
        private const float FallbackDistance = 6f;
        private const float FallbackProbeHeight = 20f;
        private const float FallbackProbeDepth = 60f;

        private Transform anchor;
        private bool anchorIsOurs;

        private readonly LobbyPreviewCamera view = new();
        private readonly LobbyRankFigures figures = new();
        private LobbyOverlayLayer overlays;
        private LobbyNameplates nameplates;
        private LobbyTeamPlates plates;
        private LobbySuitCycler cycler;

        /// <summary>Which figure the cycler belongs under, or -1 while that is unknown.</summary>
        private int localSlot = -1;

        /// <summary>
        /// Puts the rank up. <paramref name="page"/> is the screen's own rect, which the overlays are
        /// built into so they are destroyed with the page.
        /// </summary>
        public static LobbyPreviewRank Create(RectTransform page, GameObject entryPrefab,
            Action<int> onStep, Action<int> onJoinTeam)
        {
            var host = new GameObject(nameof(LobbyPreviewRank));
            var rank = host.AddComponent<LobbyPreviewRank>();

            rank.overlays = new LobbyOverlayLayer(page);
            rank.nameplates = new LobbyNameplates(rank.overlays, LabelLift);
            rank.plates = new LobbyTeamPlates(rank.overlays, PlateLift, onJoinTeam);

            // Before the anchor is resolved, because the anchor's own fallback is computed from where
            // the camera is looking — and by then it should be looking at the lobby's shot.
            rank.view.Adopt(CameraViewName);
            rank.ResolveAnchor();
            rank.cycler = new LobbySuitCycler(rank.overlays, entryPrefab, onStep);

            return rank;
        }

        /// <summary>
        /// Tears down everything the rank put in the world. The figures hang off the scene's anchor
        /// rather than off this object, so destroying this alone would leave astronauts standing in
        /// the menu with nothing driving them.
        /// </summary>
        public void Dispose()
        {
            figures.Clear();

            // Only if we invented it. An authored anchor belongs to the scene.
            if (anchorIsOurs && anchor != null) Destroy(anchor.gameObject);

            plates.Clear();
            overlays.Destroy();
            view.Restore();

            Destroy(gameObject);
        }

        /// <summary>
        /// Fills the rank from the roster snapshot: one cluster per team in a VS lobby, or the single
        /// line a story lobby has always drawn (<c>teams = 1</c>, seated at the roster's own current
        /// length so the line is exactly as wide as the people standing in it).
        /// </summary>
        public void Render(RosterSnapshot snapshot)
        {
            localSlot = snapshot.LocalSlot;

            bool versus = snapshot.IsVersus;
            int teams = versus ? Mathf.Max(1, snapshot.TeamCount) : 1;
            int teamSize = versus ? Mathf.Max(1, snapshot.TeamSize) : Mathf.Max(1, snapshot.Names.Length);
            int[] teamsBySlot = versus ? snapshot.Teams : null;

            for (int slot = 0; slot < snapshot.Names.Length; slot++)
            {
                int team = versus && slot < snapshot.Teams.Length ? snapshot.Teams[slot] : 0;
                Vector3 seat = RankLayout.SeatPosition(team, SeatOf(slot, teamsBySlot), teams, teamSize);

                int color = versus
                    ? snapshot.ColorOfTeam(team)
                    : slot < snapshot.SuitColors.Length ? snapshot.SuitColors[slot] : 0;

                if (figures.Seat(slot, anchor, seat, color))
                    nameplates.Set(slot, snapshot.Names[slot], isHost: slot == snapshot.HostSlot);
            }

            figures.HideFrom(snapshot.Names.Length);

            if (versus)
            {
                plates.Ensure(teams, teamSize);
                plates.Update(snapshot);
            }
            else
            {
                plates.Clear();
            }

            if (figures.IsStanding(localSlot))
                cycler.SetColor(versus ? snapshot.ColorOfTeam(snapshot.LocalTeam) : snapshot.SuitColors[localSlot]);

            view.Fit(anchor, teams, teamSize);

            // Re-faced after the fit, not before: facing reads the camera's CURRENT position, and
            // fitting is what just moved it.
            figures.FaceCamera();

            PositionOverlays();
        }

        /// <summary>Repaints only our own figure, for the frame an arrow is pressed.</summary>
        public void SetLocalColor(int color)
        {
            figures.Recolor(localSlot, color);
            cycler.SetColor(color);
        }

        /// <summary>
        /// Which seat of their OWN team a player occupies: their position among the players on that
        /// team, counted in lobby order.
        ///
        /// A null or empty <paramref name="teams"/> means every player is on the same, single team —
        /// the story-lobby case — so the seat is just the slot itself, reproducing the plain line the
        /// rank has always drawn.
        /// </summary>
        public static int SeatOf(int slot, int[] teams)
        {
            if (teams == null || teams.Length == 0) return slot;
            if (slot < 0 || slot >= teams.Length) return 0;

            int team = teams[slot];
            int seat = 0;

            for (int i = 0; i < slot; i++)
                if (teams[i] == team) seat++;

            return seat;
        }

        /// <summary>
        /// Whether a team's plate may be clicked to join it: not the team already standing on, and
        /// not full under the lobby's current team size.
        /// </summary>
        public static bool CanJoin(int team, int localTeam, int headsOn, int teamSize) =>
            team != localTeam && headsOn < teamSize;

        /// <summary>
        /// LateUpdate rather than Update so it runs after anything that moved the camera or the
        /// figures this frame; done in Update, a label trails its head by one frame, which reads as
        /// the text being loosely attached rather than as a nameplate.
        /// </summary>
        private void LateUpdate() => PositionOverlays();

        private void PositionOverlays()
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            nameplates.Position(camera, figures.Heads, figures.Occupied);
            plates.Position(camera, anchor);

            bool cyclerVisible = figures.IsStanding(localSlot);
            cycler.Position(camera, cyclerVisible,
                            cyclerVisible ? figures.PositionOf(localSlot) - Vector3.up * CyclerDrop : default);
        }

        /// <summary>
        /// Finds where the rank stands, or invents somewhere reasonable.
        ///
        /// The fallback is not a nicety. A scene that has not had the setup tool run on it would
        /// otherwise show an empty lobby with an error in the console, and "the lobby is empty" is
        /// indistinguishable from a networking failure. Building a spot in front of the camera
        /// degrades instead: the framing may be wrong, but everyone is visibly there.
        /// </summary>
        private void ResolveAnchor()
        {
            GameObject authored = GameObject.Find(AnchorName);

            if (authored != null)
            {
                anchor = authored.transform;
                anchorIsOurs = false;
                return;
            }

            Camera camera = Camera.main;
            if (camera == null) return;

            Vector3 forward = camera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 spot = camera.transform.position + forward * FallbackDistance;

            // Dropped onto whatever is under it, so they stand on the sand rather than hover at
            // camera height.
            if (Physics.Raycast(spot + Vector3.up * FallbackProbeHeight, Vector3.down, out RaycastHit hit,
                                FallbackProbeDepth))
                spot = hit.point;

            var host = new GameObject(AnchorName + " (temporary)");
            host.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(forward, Vector3.up));

            anchor = host.transform;
            anchorIsOurs = true;

            Debug.LogWarning($"[LobbyPreviewRank] No '{AnchorName}' in the scene, so the rank was " +
                             "placed in front of the camera. Run Tools ▸ SpaceGame ▸ Menus ▸ Setup " +
                             "Lobby Preview and drag the anchor to frame it properly.");
        }
    }
}
