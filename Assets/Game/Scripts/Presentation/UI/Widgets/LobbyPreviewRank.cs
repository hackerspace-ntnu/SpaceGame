using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The rank of astronauts standing in the lobby: one per player, in their own suit colour, with
    /// their name above their head, and a colour cycler under your own.
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
    /// This replaces the roster's text list. The names above their heads say everything the list
    /// said, so keeping both would be the same information twice on a page that had no room for the
    /// first copy.
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

        /// <summary>
        /// Metres between figures.
        ///
        /// 1.55 rather than the 1.15 this started at: at 1.15 a rank of four read as a huddle, with
        /// each figure's shoulder occluding the next one's arm, and an occluded suit is a suit whose
        /// colour is half hidden. Wide enough that all four are separate silhouettes.
        /// </summary>
        private const float Spacing = 1.45f;

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

        /// <summary>Slots are fixed, so nobody slides sideways when somebody joins.</summary>
        private static readonly int Slots = LobbySession.MaxPlayers;

        private GameObject figurePrefab;
        private Transform anchor;
        private bool anchorIsOurs;

        // The camera pose the lobby borrowed, so it can be handed back exactly. Stored as values
        // rather than as a parent or a copied transform: reparenting the menu camera would leave it
        // somewhere unexpected if this object died without tidying up.
        private Transform borrowedCamera;
        private Vector3 returnPosition;
        private Quaternion returnRotation;

        private readonly GameObject[] figures = new GameObject[LobbySession.MaxPlayers];
        private readonly Transform[] heads = new Transform[LobbySession.MaxPlayers];
        private readonly SuitRecolor[] recolors = new SuitRecolor[LobbySession.MaxPlayers];

        private RectTransform labelLayer;
        private readonly RectTransform[] labelRows = new RectTransform[LobbySession.MaxPlayers];
        private readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[LobbySession.MaxPlayers];
        private readonly TextMeshProUGUI[] labelShadows = new TextMeshProUGUI[LobbySession.MaxPlayers];
        private readonly RectTransform[] underlines = new RectTransform[LobbySession.MaxPlayers];

        private RectTransform cyclerRow;
        private TextMeshProUGUI cyclerName;
        private Image cyclerChip;

        private GameObject entryPrefab;
        private Action<int> onStep;

        /// <summary>Which figure the cycler belongs under, or -1 while that is unknown.</summary>
        private int localSlot = -1;

        // What the lobby says should be on screen, kept apart from what is actually visible this
        // frame. LateUpdate hides an overlay whose world point went behind the camera, and without
        // somewhere to record intent it could never put it back — a label hidden once would stay
        // hidden until the next poll happened to rebuild it.
        private readonly bool[] occupied = new bool[LobbySession.MaxPlayers];
        private bool cyclerWanted;

        /// <summary>
        /// Puts the rank up.
        ///
        /// <paramref name="page"/> is the screen's own rect, which the name labels are built into so
        /// they are destroyed with the page. <paramref name="onStep"/> is called with -1 or +1 when a
        /// chevron is pressed.
        /// </summary>
        public static LobbyPreviewRank Create(RectTransform page, GameObject entryPrefab,
            Action<int> onStep)
        {
            var host = new GameObject(nameof(LobbyPreviewRank));
            var rank = host.AddComponent<LobbyPreviewRank>();

            rank.entryPrefab = entryPrefab;
            rank.onStep = onStep;
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
        /// anchor so they inherit its transform — so destroying this object would leave four
        /// astronauts standing in the menu with nothing driving them. Which is exactly what happened
        /// before this method existed: backing out to the join page left the rank behind, and opening
        /// the roster again built a second one on top of it.
        /// </summary>
        public void Dispose()
        {
            for (int i = 0; i < figures.Length; i++)
            {
                if (figures[i] != null) Destroy(figures[i]);
                figures[i] = null;
            }

            // Only if we invented it. An authored anchor belongs to the scene.
            if (anchorIsOurs && anchor != null) Destroy(anchor.gameObject);

            if (labelLayer != null) Destroy(labelLayer.gameObject);

            RestoreCamera();

            Destroy(gameObject);
        }

        // ─────────────────────────────────────────────────────────────────────── camera

        /// <summary>
        /// Swings the menu camera onto the lobby's own shot, remembering where it was.
        ///
        /// Silently does nothing when the scene has no <see cref="CameraViewName"/>, which is the right
        /// answer rather than an error: the menu's own framing is a perfectly usable shot, and a
        /// missing view means nobody has composed a better one yet.
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

            borrowedCamera.SetPositionAndRotation(view.transform.position, view.transform.rotation);
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

        // ─────────────────────────────────────────────────────────────────────── render

        /// <summary>
        /// Fills the rank from the lobby.
        ///
        /// <paramref name="localColor"/> is passed separately and wins for the local slot, because
        /// the lobby's own copy of our colour is up to a poll and a debounce behind what the player
        /// just pressed. Reading it from the poll would make our own astronaut the last one on screen
        /// to show our choice.
        /// </summary>
        public void Render(string[] names, int[] colors, int localSlotIndex, int hostSlot,
            int localColor)
        {
            localSlot = localSlotIndex;

            for (int i = 0; i < Slots; i++)
            {
                bool present = names != null && i < names.Length;

                if (!present)
                {
                    occupied[i] = false;
                    if (figures[i] != null) figures[i].SetActive(false);
                    continue;
                }

                EnsureFigure(i);

                if (figures[i] == null)
                {
                    occupied[i] = false;
                    continue;
                }

                occupied[i] = true;
                figures[i].SetActive(true);

                int color = i == localSlot
                    ? localColor
                    : colors != null && i < colors.Length ? colors[i] : 0;

                if (recolors[i] != null) recolors[i].Apply(color);

                EnsureLabel(i);
                labels[i].text = names[i];
                labelShadows[i].text = names[i];

                // An underline instead of the word "host": the page has no room for a caption per
                // figure, and a rank of four only ever needs to mark one of them.
                if (underlines[i] != null) underlines[i].gameObject.SetActive(i == hostSlot);
            }

            cyclerWanted = localSlot >= 0 && localSlot < Slots && occupied[localSlot];
            if (cyclerWanted) SetCyclerColor(localColor);

            PositionOverlays();
        }

        /// <summary>Repaints only our own figure, for the frame an arrow is pressed.</summary>
        public void SetLocalColor(int color)
        {
            if (localSlot >= 0 && localSlot < recolors.Length && recolors[localSlot] != null)
                recolors[localSlot].Apply(color);

            SetCyclerColor(color);
        }

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

            for (int i = 0; i < Slots; i++)
            {
                if (labelRows[i] == null) continue;

                bool visible = occupied[i] && heads[i] != null
                               && Place(camera, labelRows[i],
                                        heads[i].position + Vector3.up * LabelLift);

                labelRows[i].gameObject.SetActive(visible);
            }

            if (cyclerRow == null) return;

            bool cyclerVisible = cyclerWanted && localSlot >= 0 && figures[localSlot] != null
                                 && Place(camera, cyclerRow,
                                          figures[localSlot].transform.position
                                          - Vector3.up * CyclerDrop);

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

            // Centred on MaxPlayers rather than on how many are here, so a figure never shifts
            // sideways because somebody else joined or left.
            float offset = (slot - (Slots - 1) * 0.5f) * Spacing;
            figure.transform.localPosition = new Vector3(offset, 0f, 0f);

            figures[slot] = figure;
            heads[slot] = FindHead(figure.transform) ?? figure.transform;
            recolors[slot] = figure.GetComponentInChildren<SuitRecolor>(true);

            FaceCamera(figure.transform);
            SetupAnimator(figure, slot);
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
        /// property of the asset, and a future edit that flips it would leave four astronauts falling
        /// on the spot in the menu with nothing to explain why.
        ///
        /// IdleIndex is staggered so a rank of four does not breathe in perfect lockstep, which is
        /// the single clearest tell that they are clones.
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

            RectTransform left = Slice(cyclerRow, "LeftSlot", 0f, ChevronWidth);
            MenuEntry.Create(entryPrefab, left, "PrevColor", PreviousGlyph, MenuEntry.ActionSize,
                             CyclerHeight, () => onStep?.Invoke(-1), out _);

            RectTransform right = Slice(cyclerRow, "RightSlot", CyclerWidth - ChevronWidth,
                                       ChevronWidth);
            MenuEntry.Create(entryPrefab, right, "NextColor", NextGlyph, MenuEntry.ActionSize,
                             CyclerHeight, () => onStep?.Invoke(1), out _);

            RectTransform middle = Slice(cyclerRow, "Value", ChevronWidth,
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

        /// <summary>A fixed-width column inside a row, measured from its left edge.</summary>
        private static RectTransform Slice(RectTransform parent, string name, float fromLeft,
            float width)
        {
            RectTransform rect = UIBuilder.Rect(name, parent);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(fromLeft, 0f);
            rect.offsetMax = new Vector2(fromLeft + width, 0f);
            return rect;
        }
    }
}
