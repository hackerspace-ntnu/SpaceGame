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

        // Where an invented anchor goes when the scene has none: this far in front of the camera,
        // dropped onto whatever is under it.
        private const float FallbackDistance = 6f;
        private const float FallbackProbeHeight = 20f;
        private const float FallbackProbeDepth = 60f;

        /// <summary>
        /// How far above and below a seat the ground is looked for, in metres. The same probe
        /// <c>LobbyPreviewSetup.EnsureAnchor</c> uses to place the anchor, so a seat lands on exactly
        /// the surface the anchor itself was dropped onto.
        /// </summary>
        private const float SeatProbeHeight = 30f;

        private const float SeatProbeDepth = 100f;

        /// <summary>
        /// What the seat probe is allowed to hit.
        ///
        /// Not everything: the menu is full of set dressing — the ruin, its rubble, the decorative
        /// astronauts' own props — and a seat that lands on top of a rock reads as a bug rather than
        /// as terrain. The preview astronauts themselves cannot be hit at all, because
        /// <c>LobbyPreviewSetup</c> strips every collider off the prefab, so this mask is about the
        /// scenery rather than about them.
        /// </summary>
        private static readonly int GroundMask = LayerMask.GetMask("Default", "Ground", "Terrain");

        private Transform anchor;
        private bool anchorIsOurs;

        /// <summary>
        /// Where every seat actually stands, and how much the ground rises across the rank.
        ///
        /// Solved when the shape of the rank changes rather than on every poll: neither the anchor
        /// nor the sand moves, so re-probing twice a second would be 24 raycasts a second spent
        /// arriving at the same answer.
        /// </summary>
        private GroundedRank grounded;

        /// <summary>The rank shape <see cref="grounded"/> was solved for.</summary>
        private int groundedTeams = -1;

        private int groundedTeamSize = -1;

        private readonly LobbyPreviewCamera view = new();
        private readonly LobbySetDressing dressing = new();
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
            rank.plates = new LobbyTeamPlates(rank.overlays, onJoinTeam);

            // Before the anchor is resolved, because the anchor's own fallback is computed from where
            // the camera is looking — and by then it should be looking at the lobby's shot.
            rank.view.Adopt(CameraViewName);
            rank.dressing.Hide();
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
            dressing.Restore();
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

            GroundSeats(teams, teamSize);

            nameplates.SetContext(snapshot.LocalSlot, versus ? snapshot.LocalTeam : -1);

            for (int slot = 0; slot < snapshot.Names.Length; slot++)
            {
                int team = versus && slot < snapshot.Teams.Length ? snapshot.Teams[slot] : 0;
                int seat = SeatOf(slot, teamsBySlot);

                int color = versus
                    ? snapshot.ColorOfTeam(team)
                    : slot < snapshot.SuitColors.Length ? snapshot.SuitColors[slot] : 0;

                if (figures.Seat(slot, anchor, GroundedSeat(team, seat, teams, teamSize), color))
                    nameplates.Set(slot, snapshot.Names[slot], slot == snapshot.HostSlot, team);
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

            view.Fit(anchor, teams, teamSize, grounded.HeightSpread);

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
            plates.Position(camera, anchor, GroundOfTeam);

            bool cyclerVisible = figures.IsStanding(localSlot);
            cycler.Position(camera, cyclerVisible,
                            cyclerVisible ? figures.PositionOf(localSlot) - Vector3.up * CyclerDrop : default);
        }

        /// <summary>
        /// Puts every seat of the current rank shape on the ground under it, if that shape has
        /// changed since the last time.
        ///
        /// Seats are addressed by index whether or not anybody is standing in them — the same rule
        /// <see cref="RankLayout"/> follows — so an empty seat already has a height, and somebody
        /// joining lands on the sand without a re-solve.
        /// </summary>
        private void GroundSeats(int teams, int teamSize)
        {
            if (teams == groundedTeams && teamSize == groundedTeamSize) return;

            groundedTeams = teams;
            groundedTeamSize = teamSize;

            if (anchor == null)
            {
                grounded = default;
                return;
            }

            var seats = new Vector3[teams * teamSize];

            for (int team = 0; team < teams; team++)
                for (int seat = 0; seat < teamSize; seat++)
                    seats[team * teamSize + seat] =
                        anchor.TransformPoint(RankLayout.SeatPosition(team, seat, teams, teamSize));

            grounded = RankGrounding.Solve(seats, anchor.position.y, Probe);
        }

        /// <summary>
        /// The real cast behind <see cref="RankGrounding.GroundProbe"/>.
        ///
        /// Triggers are ignored so a music zone or a spawn volume lying over the dunes cannot become
        /// the floor.
        /// </summary>
        private static bool Probe(Vector3 seat, out float groundY)
        {
            if (Physics.Raycast(seat + Vector3.up * SeatProbeHeight, Vector3.down,
                                out RaycastHit hit, SeatProbeDepth, GroundMask,
                                QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                return true;
            }

            groundY = 0f;
            return false;
        }

        /// <summary>
        /// Where a given seat ended up standing, in world space. Falls back to the flat anchor plane
        /// for a seat outside the shape that was solved, which is exactly what the rank did before
        /// it was ever grounded.
        /// </summary>
        private Vector3 GroundedSeat(int team, int seat, int teams, int teamSize)
        {
            int index = team * teamSize + seat;

            if (grounded.Positions != null && index >= 0 && index < grounded.Positions.Length)
                return grounded.Positions[index];

            return anchor != null
                ? anchor.TransformPoint(RankLayout.SeatPosition(team, seat, teams, teamSize))
                : Vector3.zero;
        }

        /// <summary>
        /// The ground height under a team's centre — its first seat, which is where its plate hangs.
        /// Falls back to the anchor's own height, which is where plates hung before the rank was
        /// grounded.
        /// </summary>
        private float GroundOfTeam(int team, int teams, int teamSize)
        {
            if (grounded.Positions == null || grounded.Positions.Length == 0)
                return anchor != null ? anchor.position.y : 0f;

            int index = team * teamSize;

            return index >= 0 && index < grounded.Positions.Length
                ? grounded.Positions[index].y
                : grounded.MinY;
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
