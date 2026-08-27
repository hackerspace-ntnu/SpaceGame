using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Presentation
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
    /// rank should stand. A RenderTexture would have needed its own lighting and its own alpha
    /// handling, and would have looked like a cut-out pasted over the scene.
    /// </para>
    ///
    /// <para>
    /// Where everyone stands is <see cref="RankLayout"/>'s job, not this class's: it addresses a seat
    /// positionally whether or not anyone stands in it, which is what stops a figure sliding
    /// sideways because somebody else joined — the rule the rank held even before there were teams.
    /// </para>
    ///
    /// <para>
    /// A MonoBehaviour, unlike <see cref="LobbyRosterView"/>, because the labels are UI tracking
    /// world positions and that needs a frame hook. <see cref="LobbyRosterView"/> is redrawn from the
    /// poll and needs nothing of the sort.
    /// </para>
    /// </summary>
    public class LobbyPreviewRank : MonoBehaviour
    {
        /// <summary>
        /// The empty in MainMenu.unity the rank stands on. Its position is the centre of the line and
        /// its right vector is the direction the line runs.
        ///
        /// Found by name because a runtime-built screen has no Inspector to be handed a reference,
        /// and the alternative — a field on MainMenuUI — is a third thing to remember to assign.
        /// Placed by Tools ▸ SpaceGame ▸ Menus ▸ Setup Lobby Preview.
        /// </summary>
        public const string AnchorName = "LobbyPreviewAnchor";

        /// <summary>
        /// An empty in MainMenu.unity holding the pose the camera takes while the lobby is up.
        ///
        /// <para>
        /// The lobby gets its own shot of the same set. The menu's own framing is composed around the
        /// ruin and the three decorative astronauts on the right, which leaves the rank nowhere to
        /// stand: the left of the frame belongs to the control column, and the middle is where a
        /// mannequin has its arm out. Swinging the camera onto open dune instead gives the rank clean
        /// ground to stand on and clean sky behind its heads, and costs nothing — it is the same scene
        /// and the same lighting, just pointed somewhere else.
        /// </para>
        ///
        /// <para>
        /// Borrowed, not permanent: the pose is saved on the way in and put back on the way out, the
        /// same shape as <see cref="MenuScreen"/> switching the menu's canvases off and on again.
        /// </para>
        /// </summary>
        public const string CameraViewName = "LobbyCameraView";

        /// <summary>Under Assets/Game/Resources, so it loads without a serialized reference.</summary>
        private const string PrefabResource = "LobbyPreviewAstronaut";

        /// <summary>How far above the head bone the name floats.</summary>
        private const float LabelLift = 0.42f;

        /// <summary>
        /// How far below the anchor line the cycler sits, in metres of world space.
        ///
        /// It has been down at 0.85 and up at 0.55. The clearance it used to need is gone — the code
        /// and privacy controls moved to a strip along the top of the page — so it sits close under
        /// the boots, where it reads as belonging to the figure above it rather than floating in the
        /// sand between that figure and the footer.
        /// </summary>
        private const float CyclerDrop = 0.4f;

        /// <summary>
        /// How far above a team's centre its plate floats, in metres of world space.
        ///
        /// High enough to clear a second row of heads (<see cref="RankLayout.RowSpacing"/> stacks a
        /// back row behind the front one, not above it, but a name plate already sits
        /// <see cref="LabelLift"/> above a head roughly 1.8m tall) without drifting so far up that it
        /// reads as unrelated to the cluster underneath it. Not measured against a capture — flag
        /// this if the plate reads too high or too low once someone can see it rendered.
        /// </summary>
        private const float PlateLift = 2.35f;

        private const float PlateWidth = 520f;
        private const float PlateHeight = 72f;
        private const int PlateSize = 46;

        /// <summary>What a team plate fades to when it cannot be joined right now. Present, not gone.</summary>
        private const float PlateDimmedAlpha = 0.45f;

        /// <summary>
        /// How much air the fitted camera leaves around the rank — see
        /// <see cref="RankLayout.CameraDistance"/>. 1.2 leaves about a sixth of the frame as margin.
        /// Not measured against a capture — flag this if the rank reads cramped or lost in the frame.
        /// </summary>
        private const float CameraFitMargin = 1.2f;

        // ASCII, not ◀ and ▶. The project's TMP default is LiberationSans SDF, which has neither
        // U+25C0 nor U+25B6 and no fallback that does — TMP silently substitutes U+25A1 and both
        // arrows render as empty BOXES. Caught from a warning in a capture, where the cycler read as
        // "□ Ember □". Anything fancier than this has to be checked against the font first.
        private const string PreviousGlyph = "<";
        private const string NextGlyph = ">";

        private const int NameSize = 40;
        private const float CyclerWidth = 460f;
        private const float CyclerHeight = 74f;
        private const float ChevronWidth = 74f;

        private GameObject figurePrefab;
        private Transform anchor;
        private bool anchorIsOurs;

        // The camera pose the lobby borrowed, so it can be handed back exactly. Stored as values
        // rather than as a parent or a copied transform: reparenting the menu camera would leave it
        // somewhere unexpected if this object died without tidying up.
        private Transform borrowedCamera;
        private Vector3 returnPosition;
        private Quaternion returnRotation;

        /// <summary>
        /// The pose <see cref="CameraViewName"/> was authored at, kept apart from
        /// <see cref="returnPosition"/>/<see cref="returnRotation"/> (the menu's OWN pose, put back on
        /// teardown). The camera fit measures and pushes back from this one, never from wherever the
        /// camera happens to be sitting when a render runs.
        /// </summary>
        private Vector3 viewPosition;
        private Quaternion viewRotation;

        // One entry per player, grown as the roster grows. Never shrunk — a figure for a player who
        // has left is switched off, not destroyed, in case they rejoin.
        private readonly List<GameObject> figures = new();
        private readonly List<Transform> heads = new();
        private readonly List<SuitRecolor> recolors = new();
        private readonly List<bool> occupied = new();

        private RectTransform labelLayer;
        private readonly List<RectTransform> labelRows = new();
        private readonly List<TextMeshProUGUI> labels = new();
        private readonly List<TextMeshProUGUI> labelShadows = new();
        private readonly List<RectTransform> underlines = new();

        /// <summary>One nameplate per team, rebuilt only when the team shape changes.</summary>
        private sealed class TeamPlate
        {
            public RectTransform Row;
            public TextMeshProUGUI Label;
            public TextMeshProUGUI Shadow;
            public Button Button;
        }

        private readonly List<TeamPlate> plates = new();

        /// <summary>The team shape the plates were last built for, so they are rebuilt only when it moves.</summary>
        private int plateTeamCount = -1;
        private int plateTeamSize = -1;

        private RectTransform cyclerRow;
        private TextMeshProUGUI cyclerName;
        private Image cyclerChip;

        private GameObject entryPrefab;
        private Action<int> onStep;
        private Action<int> onJoinTeam;

        /// <summary>Which figure the cycler belongs under, or -1 while that is unknown.</summary>
        private int localSlot = -1;

        private bool cyclerWanted;

        /// <summary>
        /// Puts the rank up.
        ///
        /// <paramref name="page"/> is the screen's own rect, which the name labels are built into so
        /// they are destroyed with the page. <paramref name="onStep"/> is called with -1 or +1 when a
        /// chevron is pressed. <paramref name="onJoinTeam"/> is called with a team number when its
        /// plate is clicked.
        /// </summary>
        public static LobbyPreviewRank Create(RectTransform page, GameObject entryPrefab,
            Action<int> onStep, Action<int> onJoinTeam)
        {
            var host = new GameObject(nameof(LobbyPreviewRank));
            var rank = host.AddComponent<LobbyPreviewRank>();

            rank.entryPrefab = entryPrefab;
            rank.onStep = onStep;
            rank.onJoinTeam = onJoinTeam;
            rank.labelLayer = UIBuilder.Fill(UIBuilder.Rect("PreviewLabels", page));

            // Before the anchor is resolved, because the anchor's own fallback is computed from where
            // the camera is looking — and by then it should be looking at the lobby's shot.
            rank.AdoptCameraView();
            rank.ResolveAnchor();
            rank.BuildCycler();

            return rank;
        }

        /// <summary>
        /// Tears down everything the rank put in the world.
        ///
        /// The figures are NOT children of this component's GameObject — they hang off the scene's
        /// anchor so they inherit its transform — so destroying this object would leave astronauts
        /// standing in the menu with nothing driving them. Which is exactly what happened before this
        /// method existed: backing out to the join page left the rank behind, and opening the roster
        /// again built a second one on top of it.
        /// </summary>
        public void Dispose()
        {
            foreach (GameObject figure in figures)
                if (figure != null) Destroy(figure);
            figures.Clear();

            // Only if we invented it. An authored anchor belongs to the scene.
            if (anchorIsOurs && anchor != null) Destroy(anchor.gameObject);

            // The plates and the nameplates are both children of labelLayer, so destroying it takes
            // them all with it. Cleared here too so nothing on this dying object still points at a
            // GameObject that no longer exists.
            if (labelLayer != null) Destroy(labelLayer.gameObject);
            plates.Clear();

            RestoreCamera();

            Destroy(gameObject);
        }

        // ─────────────────────────────────────────────────────────────────────── camera

        /// <summary>
        /// Swings the menu camera onto the lobby's own shot, remembering where it was.
        ///
        /// Silently does nothing when the scene has no <see cref="CameraViewName"/>, which is the right
        /// answer rather than an error: the menu's own framing is a perfectly usable shot, and a
        /// missing view means nobody has composed a better one yet. It also means the rank never
        /// fits the camera — see <see cref="FitCamera"/> — because there is no authored backward
        /// direction to push it along.
        /// </summary>
        private void AdoptCameraView()
        {
            GameObject view = GameObject.Find(CameraViewName);
            if (view == null) return;

            Camera camera = Camera.main;
            if (camera == null) return;

            borrowedCamera = camera.transform;
            returnPosition = borrowedCamera.position;
            returnRotation = borrowedCamera.rotation;

            viewPosition = view.transform.position;
            viewRotation = view.transform.rotation;

            borrowedCamera.SetPositionAndRotation(viewPosition, viewRotation);
        }

        /// <summary>
        /// Puts the camera back where the menu had it.
        ///
        /// Guarded on <see cref="borrowedCamera"/> rather than on the view still existing, so a view
        /// deleted while the lobby is open cannot strand the camera pointing at the dunes with a main
        /// menu drawn over it.
        /// </summary>
        private void RestoreCamera()
        {
            if (borrowedCamera == null) return;

            borrowedCamera.SetPositionAndRotation(returnPosition, returnRotation);
            borrowedCamera = null;
        }

        /// <summary>
        /// Backs the camera off from the authored view so the whole rank fits in frame.
        ///
        /// <para>
        /// Measured from <see cref="viewPosition"/>/<see cref="viewRotation"/> — the authored pose —
        /// and only ever pushed FURTHER back along that pose's own backward direction, never
        /// recomputed from the anchor outright. That is what guarantees a small rank (today's default
        /// story line, or a 2-a-side VS match) reproduces the exact composed shot rather than drifting
        /// off its axis: when the rank already fits at the authored distance, the extra distance below
        /// is zero and the camera sits exactly where <see cref="CameraViewName"/> put it.
        /// </para>
        /// </summary>
        private void FitCamera(int teams, int teamSize)
        {
            // No adopted view means no authored backward direction to push along, so there is
            // nothing safe to fit against — the rank keeps whatever framing the scene already has.
            if (borrowedCamera == null || anchor == null) return;

            Camera camera = Camera.main;
            if (camera == null) return;

            float vFovRad = camera.fieldOfView * Mathf.Deg2Rad;
            float horizontalFov = 2f * Mathf.Atan(Mathf.Tan(vFovRad * 0.5f) * camera.aspect) * Mathf.Rad2Deg;

            float width = RankLayout.TotalWidth(teams, teamSize);
            float wanted = RankLayout.CameraDistance(width, horizontalFov, CameraFitMargin);
            float authoredDistance = Vector3.Distance(viewPosition, anchor.position);

            // Never negative: a rank that already fits inside the authored shot must not pull the
            // camera IN, which is the one thing the class doc promises it never does.
            float extra = Mathf.Max(0f, wanted - authoredDistance);

            Vector3 backward = viewRotation * Vector3.back;
            borrowedCamera.SetPositionAndRotation(viewPosition + backward * extra, viewRotation);
        }

        // ─────────────────────────────────────────────────────────────────────── render

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

            for (int i = 0; i < snapshot.Names.Length; i++)
            {
                EnsureFigure(i);

                if (figures[i] == null)
                {
                    occupied[i] = false;
                    continue;
                }

                occupied[i] = true;
                figures[i].SetActive(true);

                int team = versus && i < snapshot.Teams.Length ? snapshot.Teams[i] : 0;
                int seat = SeatOf(i, teamsBySlot);
                figures[i].transform.localPosition = RankLayout.SeatPosition(team, seat, teams, teamSize);

                int color = versus
                    ? snapshot.ColorOfTeam(team)
                    : i < snapshot.SuitColors.Length ? snapshot.SuitColors[i] : 0;

                if (recolors[i] != null) recolors[i].Apply(color);

                EnsureLabel(i);
                labels[i].text = snapshot.Names[i];
                labelShadows[i].text = snapshot.Names[i];

                // An underline instead of the word "host": there is no room for a caption per figure,
                // and the rank only ever needs to mark one of them.
                if (underlines[i] != null) underlines[i].gameObject.SetActive(i == snapshot.HostSlot);
            }

            // Anyone the snapshot no longer lists has left. Switched off, not destroyed — they keep
            // their figure and their place if they rejoin.
            for (int i = snapshot.Names.Length; i < figures.Count; i++)
            {
                occupied[i] = false;
                if (figures[i] != null) figures[i].SetActive(false);
            }

            if (versus)
            {
                EnsurePlates(teams, teamSize);
                UpdatePlates(snapshot, teamSize);
            }
            else if (plates.Count > 0)
            {
                // A story lobby shows no plates at all — there is nothing to click, since there is
                // only ever the one line.
                DestroyPlates();
            }

            cyclerWanted = localSlot >= 0 && localSlot < occupied.Count && occupied[localSlot];
            if (cyclerWanted)
            {
                int cyclerColor = versus ? snapshot.ColorOfTeam(snapshot.LocalTeam) : snapshot.SuitColors[localSlot];
                SetCyclerColor(cyclerColor);
            }

            FitCamera(teams, teamSize);

            // Re-faced after the fit, not before: FaceCamera reads the camera's CURRENT position, and
            // fitting is what just moved it.
            for (int i = 0; i < figures.Count; i++)
                if (i < occupied.Count && occupied[i] && figures[i] != null)
                    FaceCamera(figures[i].transform);

            PositionOverlays();
        }

        /// <summary>Repaints only our own figure, for the frame an arrow is pressed.</summary>
        public void SetLocalColor(int color)
        {
            if (localSlot >= 0 && localSlot < recolors.Count && recolors[localSlot] != null)
                recolors[localSlot].Apply(color);

            SetCyclerColor(color);
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
        /// Keeps the labels over the heads.
        ///
        /// LateUpdate rather than Update so it runs after anything that moved the camera or the
        /// figures this frame; done in Update, a label trails its head by one frame, which reads as
        /// the text being loosely attached rather than as a nameplate.
        /// </summary>
        private void LateUpdate() => PositionOverlays();

        private void PositionOverlays()
        {
            Camera camera = Camera.main;
            if (camera == null || labelLayer == null) return;

            for (int i = 0; i < labelRows.Count; i++)
            {
                if (labelRows[i] == null) continue;

                bool visible = i < occupied.Count && occupied[i] && i < heads.Count && heads[i] != null
                               && Place(camera, labelRows[i], heads[i].position + Vector3.up * LabelLift);

                labelRows[i].gameObject.SetActive(visible);
            }

            // Recomputed every frame from the cached team shape rather than stored once at build
            // time: the anchor never moves, but a plate's world position still has to be re-derived
            // through Place() like every other overlay so it goes through the same behind-the-camera
            // guard the nameplates and the cycler already rely on.
            if (anchor != null)
            {
                for (int team = 0; team < plates.Count; team++)
                {
                    TeamPlate plate = plates[team];
                    if (plate.Row == null) continue;

                    Vector3 worldPoint = anchor.TransformPoint(
                        RankLayout.TeamCenter(team, plateTeamCount, plateTeamSize) + Vector3.up * PlateLift);

                    bool visible = Place(camera, plate.Row, worldPoint);
                    plate.Row.gameObject.SetActive(visible);
                }
            }

            if (cyclerRow == null) return;

            bool cyclerVisible = cyclerWanted && localSlot >= 0 && localSlot < figures.Count
                                 && figures[localSlot] != null
                                 && Place(camera, cyclerRow,
                                          figures[localSlot].transform.position - Vector3.up * CyclerDrop);

            cyclerRow.gameObject.SetActive(cyclerVisible);
        }

        /// <summary>
        /// Moves a UI row onto a world position. False when that position is behind the camera and
        /// the row should not be drawn at all.
        ///
        /// The behind-the-camera test is not defensive padding: WorldToScreenPoint happily returns a
        /// mirrored on-screen point for anything behind the lens, so without it a figure the camera
        /// has turned away from puts its name back on screen in the wrong place.
        /// </summary>
        private bool Place(Camera camera, RectTransform row, Vector3 worldPoint)
        {
            Vector3 screen = camera.WorldToScreenPoint(worldPoint);
            if (screen.z <= 0f) return false;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                labelLayer, screen, null, out Vector2 local);

            row.anchoredPosition = local;
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────── figures

        private void EnsureFigure(int slot)
        {
            Grow(figures, slot);
            Grow(heads, slot);
            Grow(recolors, slot);
            Grow(occupied, slot);

            if (figures[slot] != null) return;

            if (figurePrefab == null)
            {
                figurePrefab = Resources.Load<GameObject>(PrefabResource);

                if (figurePrefab == null)
                {
                    Debug.LogError($"[LobbyPreviewRank] No '{PrefabResource}' in a Resources folder. " +
                                   "Run Tools ▸ SpaceGame ▸ Menus ▸ Setup Lobby Preview to build it. " +
                                   "The lobby still works; it just has nobody standing in it.");
                    return;
                }
            }

            if (anchor == null) return;

            GameObject figure = Instantiate(figurePrefab, anchor);
            figure.name = $"PreviewAstronaut{slot}";

            // No local position set here: RankLayout.SeatPosition depends on the team and the seat
            // within it, which is settled per Render call, not at creation time.

            figures[slot] = figure;
            heads[slot] = FindHead(figure.transform) ?? figure.transform;
            recolors[slot] = figure.GetComponentInChildren<SuitRecolor>(true);

            FaceCamera(figure.transform);
            SetupAnimator(figure, slot);
        }

        /// <summary>Pads a list with defaults up to and including <paramref name="index"/>.</summary>
        private static void Grow<T>(List<T> list, int index)
        {
            while (list.Count <= index) list.Add(default);
        }

        /// <summary>
        /// The bone the name hangs over.
        ///
        /// By name, because the astronaut is a Mixamo rig and mixamorig:Head is the one thing about
        /// it that the export script actively guarantees. Falls back to the figure root, which puts
        /// the name at its feet — wrong, but visible, which is the failure that gets reported.
        /// </summary>
        private static Transform FindHead(Transform root)
        {
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
                if (bone.name == "mixamorig:Head")
                    return bone;

            return null;
        }

        /// <summary>
        /// Turns a figure to the camera, level with the ground.
        ///
        /// Yaw only. Taking the camera's pitch as well would tip the astronaut backwards to look up
        /// at a camera that sits above the rank.
        /// </summary>
        private static void FaceCamera(Transform figure)
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            Vector3 toCamera = camera.transform.position - figure.position;
            toCamera.y = 0f;

            if (toCamera.sqrMagnitude < 0.0001f) return;

            figure.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        }

        /// <summary>
        /// Stands the figure still.
        ///
        /// IsGrounded is set even though the controller already defaults it true: the default is a
        /// property of the asset, and a future edit that flips it would leave astronauts falling on
        /// the spot in the menu with nothing to explain why.
        ///
        /// IdleIndex is staggered so the rank does not breathe in perfect lockstep, which is the
        /// single clearest tell that they are clones.
        /// </summary>
        private static void SetupAnimator(GameObject figure, int slot)
        {
            var animator = figure.GetComponentInChildren<Animator>(true);
            if (animator == null) return;

            animator.applyRootMotion = false;
            animator.SetBool("IsGrounded", true);
            animator.SetFloat("SpeedX", 0f);
            animator.SetFloat("SpeedY", 0f);
            animator.SetFloat("IdleIndex", slot % 3);
        }

        // ─────────────────────────────────────────────────────────────────────── anchor

        /// <summary>
        /// Finds where the rank stands, or invents somewhere reasonable.
        ///
        /// The fallback is not a nicety. A scene that has not had the setup tool run on it — a second
        /// world's menu, a colleague's branch — would otherwise show an empty lobby with an error in
        /// the console, and "the lobby is empty" is indistinguishable from a networking failure.
        /// Building a spot in front of the camera degrades instead: the framing may be wrong, but
        /// everyone is visibly there.
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

            Vector3 spot = camera.transform.position + forward * 6f;

            // Dropped onto whatever is under it, so they stand on the sand rather than hover at
            // camera height.
            if (Physics.Raycast(spot + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 60f))
                spot = hit.point;

            var host = new GameObject(AnchorName + " (temporary)");
            host.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(forward, Vector3.up));

            anchor = host.transform;
            anchorIsOurs = true;

            Debug.LogWarning($"[LobbyPreviewRank] No '{AnchorName}' in the scene, so the rank was " +
                             "placed in front of the camera. Run Tools ▸ SpaceGame ▸ Menus ▸ Setup " +
                             "Lobby Preview and drag the anchor to frame it properly.");
        }

        // ─────────────────────────────────────────────────────────────────────── overlays

        /// <summary>
        /// A nameplate: the name in white over a navy copy of itself, offset three pixels.
        ///
        /// <para>
        /// The drop shadow is doing real work, not decoration. The menu's rule is that white reads
        /// against sky and navy reads against sand, and a nameplate cannot pick one — at the authored
        /// framing the heads sit BELOW the horizon, so a plain white name lands on bright sand and
        /// disappears, while a navy one would vanish the moment somebody stood against the sky
        /// instead. Two offset copies read against both, and unlike a TMP outline they cost no
        /// per-label material instance, and unlike a plate behind the text they do not put a box on a
        /// screen whose whole language has none.
        /// </para>
        ///
        /// <para>
        /// Note that <see cref="MenuEntry.Horizon"/> claims the menu camera has no pitch and that the
        /// horizon therefore cuts the screen in half. It does not — the camera is pitched about 11.6°
        /// and the skyline sits nearer 40%. Do not use that constant to reason about what is behind a
        /// head.
        /// </para>
        /// </summary>
        private void EnsureLabel(int slot)
        {
            Grow(labelRows, slot);
            Grow(labels, slot);
            Grow(labelShadows, slot);
            Grow(underlines, slot);

            if (labelRows[slot] != null) return;

            RectTransform row = Centred(labelLayer, $"Name{slot}", 600f, 60f);

            RectTransform shadowRect = UIBuilder.Fill(UIBuilder.Rect("Shadow", row));
            shadowRect.anchoredPosition = new Vector2(3f, -3f);
            labelShadows[slot] = UIBuilder.Label(shadowRect, string.Empty, NameSize, MenuEntry.Idle,
                                                 TextAlignmentOptions.Center, FontStyles.Bold);

            RectTransform frontRect = UIBuilder.Fill(UIBuilder.Rect("Front", row));
            labels[slot] = UIBuilder.Label(frontRect, string.Empty, NameSize, MenuEntry.Title,
                                           TextAlignmentOptions.Center, FontStyles.Bold);

            // Navy, not white: it sits under the name, so it is over whatever the name is over, and
            // sand is the likelier of the two.
            RectTransform rule = UIBuilder.Rect("HostRule", row);
            rule.anchorMin = new Vector2(0.5f, 0f);
            rule.anchorMax = new Vector2(0.5f, 0f);
            rule.pivot = new Vector2(0.5f, 1f);
            rule.anchoredPosition = new Vector2(0f, 8f);
            rule.sizeDelta = new Vector2(NameSize * 3f, 3f);
            UIBuilder.Solid(rule, new Color(MenuEntry.Idle.r, MenuEntry.Idle.g, MenuEntry.Idle.b, 0.85f));

            underlines[slot] = rule;
            rule.gameObject.SetActive(false);

            labelRows[slot] = row;
        }

        /// <summary>
        /// Builds one plate per team, only when the team shape (<paramref name="teamCount"/>,
        /// <paramref name="teamSize"/>) is not what the standing plates were already built for — that
        /// pair is the only thing that ever moves a plate, so anything else in a poll must not pay
        /// for a rebuild.
        /// </summary>
        private void EnsurePlates(int teamCount, int teamSize)
        {
            if (teamCount == plateTeamCount && teamSize == plateTeamSize) return;

            DestroyPlates();

            for (int team = 0; team < teamCount; team++)
                plates.Add(BuildPlate(team));

            plateTeamCount = teamCount;
            plateTeamSize = teamSize;
        }

        private void DestroyPlates()
        {
            foreach (TeamPlate plate in plates)
                if (plate.Row != null) Destroy(plate.Row.gameObject);

            plates.Clear();
            plateTeamCount = -1;
            plateTeamSize = -1;
        }

        /// <summary>
        /// One team's plate: its name, drawn the same white-over-navy way the nameplates are — see
        /// <see cref="EnsureLabel"/> — because a plate hangs over the same sky and heads. A
        /// <see cref="Button"/> over the whole row rather than just the text, so the click target is
        /// as wide as the plate reads.
        /// </summary>
        private TeamPlate BuildPlate(int team)
        {
            RectTransform row = Centred(labelLayer, $"TeamPlate{team}", PlateWidth, PlateHeight);

            RectTransform shadowRect = UIBuilder.Fill(UIBuilder.Rect("Shadow", row));
            shadowRect.anchoredPosition = new Vector2(3f, -3f);
            TextMeshProUGUI shadow = UIBuilder.Label(shadowRect, VersusRules.TeamName(team), PlateSize,
                                                     MenuEntry.Idle, TextAlignmentOptions.Center,
                                                     FontStyles.Bold);

            RectTransform frontRect = UIBuilder.Fill(UIBuilder.Rect("Front", row));
            TextMeshProUGUI label = UIBuilder.Label(frontRect, VersusRules.TeamName(team), PlateSize,
                                                    MenuEntry.Title, TextAlignmentOptions.Center,
                                                    FontStyles.Bold);
            label.raycastTarget = true;

            Button button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = label;
            button.transition = Selectable.Transition.None;

            int capturedTeam = team;
            button.onClick.AddListener(() => onJoinTeam?.Invoke(capturedTeam));

            return new TeamPlate { Row = row, Label = label, Shadow = shadow, Button = button };
        }

        /// <summary>
        /// Repaints every plate from the current snapshot: the team's own colour, and whether it can
        /// be joined right now — greyed to <see cref="PlateDimmedAlpha"/> and unclickable when it
        /// cannot, rather than hidden, because a full or your-own team is still part of the match.
        /// </summary>
        private void UpdatePlates(RosterSnapshot snapshot, int teamSize)
        {
            for (int team = 0; team < plates.Count; team++)
            {
                TeamPlate plate = plates[team];
                if (plate.Row == null) continue;

                Color color = SuitPalette.ColorOf(snapshot.ColorOfTeam(team));
                bool canJoin = CanJoin(team, snapshot.LocalTeam, snapshot.HeadsOn(team), teamSize);
                float alpha = canJoin ? 1f : PlateDimmedAlpha;

                if (plate.Label != null) plate.Label.color = new Color(color.r, color.g, color.b, alpha);
                if (plate.Button != null) plate.Button.interactable = canJoin;
            }
        }

        /// <summary>
        /// The colour cycler: a chevron, the swatch, its name, a chevron.
        ///
        /// The name is its own object rather than a chevron's label, because the menu button's
        /// animator rewrites its own label's colour on every state change — anything written there
        /// survives until the next frame. <see cref="MenuField.Trailing"/> carries the same note.
        /// </summary>
        private void BuildCycler()
        {
            cyclerRow = Centred(labelLayer, "SuitCycler", CyclerWidth, CyclerHeight);
            cyclerRow.gameObject.SetActive(false);

            RectTransform left = UIBuilder.Slice(cyclerRow, "LeftSlot", 0f, ChevronWidth);
            MenuEntry.Create(entryPrefab, left, "PrevColor", PreviousGlyph, MenuEntry.ActionSize,
                             CyclerHeight, () => onStep?.Invoke(-1), out _);

            RectTransform right = UIBuilder.Slice(cyclerRow, "RightSlot", CyclerWidth - ChevronWidth,
                                       ChevronWidth);
            MenuEntry.Create(entryPrefab, right, "NextColor", NextGlyph, MenuEntry.ActionSize,
                             CyclerHeight, () => onStep?.Invoke(1), out _);

            RectTransform middle = UIBuilder.Slice(cyclerRow, "Value", ChevronWidth,
                                        CyclerWidth - ChevronWidth * 2f);

            // The swatch itself, because a colour's name is not a colour. "Aqua" and "Cyan" are
            // indistinguishable as words and obvious as chips.
            RectTransform chip = UIBuilder.Rect("Chip", middle);
            chip.anchorMin = new Vector2(0f, 0.5f);
            chip.anchorMax = new Vector2(0f, 0.5f);
            chip.pivot = new Vector2(0f, 0.5f);
            chip.anchoredPosition = new Vector2(6f, 0f);
            chip.sizeDelta = new Vector2(34f, 34f);
            cyclerChip = UIBuilder.Solid(chip, Color.white);

            RectTransform text = UIBuilder.Rect("Name", middle);
            text.anchorMin = new Vector2(0f, 0f);
            text.anchorMax = new Vector2(1f, 1f);
            text.pivot = new Vector2(0.5f, 0.5f);
            text.offsetMin = new Vector2(48f, 0f);
            text.offsetMax = Vector2.zero;

            cyclerName = UIBuilder.Label(text, string.Empty, MenuEntry.RowSize, MenuEntry.Idle,
                                         TextAlignmentOptions.Left, FontStyles.Bold);
        }

        private void SetCyclerColor(int index)
        {
            if (cyclerName != null) cyclerName.text = SuitPalette.NameOf(index);
            if (cyclerChip != null) cyclerChip.color = SuitPalette.ColorOf(index);
        }

        /// <summary>A row placed by its centre, which is what a projected world point gives us.</summary>
        private static RectTransform Centred(RectTransform parent, string name, float width,
            float height)
        {
            RectTransform rect = UIBuilder.Rect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
    }
}
