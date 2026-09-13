using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// The astronaut figures themselves: one per roster slot, instantiated under the scene's
    /// anchor, recoloured, and turned to face the camera.
    ///
    /// <para>
    /// A figure is created the first time its slot is filled and switched off — never destroyed —
    /// when the slot empties, so a player who rejoins gets the same figure and the same place. The
    /// figures are NOT children of the rank's own GameObject: they hang off the anchor so they
    /// inherit its transform, which is why <see cref="Clear"/> has to exist.
    /// </para>
    /// </summary>
    internal sealed class LobbyRankFigures
    {
        /// <summary>Under Assets/Game/Resources, so it loads without a serialized reference.</summary>
        private const string PrefabResource = "LobbyPreviewAstronaut";

        /// <summary>The bone the name hangs over — the one thing about the Mixamo rig the export script guarantees.</summary>
        private const string HeadBone = "mixamorig:Head";

        /// <summary>The idle animation is staggered so the rank does not breathe in lockstep, which is the clearest tell that they are clones.</summary>
        private const int IdleVariants = 3;

        private GameObject prefab;

        private readonly List<GameObject> figures = new();
        private readonly List<Transform> heads = new();
        private readonly List<SuitRecolor> recolors = new();
        private readonly List<bool> occupied = new();

        /// <summary>The head bone per slot, for the nameplates. Null where no figure stands.</summary>
        public IReadOnlyList<Transform> Heads => heads;

        /// <summary>Whether each slot has somebody standing in it.</summary>
        public IReadOnlyList<bool> Occupied => occupied;

        public bool IsStanding(int slot) =>
            slot >= 0 && slot < occupied.Count && occupied[slot] && figures[slot] != null;

        public Vector3 PositionOf(int slot) => figures[slot].transform.position;

        /// <summary>
        /// Stands a slot's figure at <paramref name="worldPosition"/> under <paramref name="anchor"/>,
        /// in <paramref name="color"/>. False when no figure could be made — no prefab, or no anchor —
        /// in which case the slot reads as empty rather than throwing.
        ///
        /// <para>
        /// A WORLD position, not a local one. Seats are laid out flat by <c>RankLayout</c> and then
        /// dropped onto the sand by <c>RankGrounding</c>, and the height that comes back is a world
        /// height. Assigned as a <c>localPosition</c> it would be measured from the
        /// anchor's own plane instead and undo the grounding entirely — which is exactly what this
        /// line used to do, and why a wide rank floated over dips and sank into rises.
        /// </para>
        /// </summary>
        public bool Seat(int slot, Transform anchor, Vector3 worldPosition, int color)
        {
            Ensure(slot, anchor);

            if (figures[slot] == null)
            {
                occupied[slot] = false;
                return false;
            }

            occupied[slot] = true;
            figures[slot].SetActive(true);
            figures[slot].transform.position = worldPosition;

            Recolor(slot, color);
            return true;
        }

        /// <summary>Switches off every figure from <paramref name="firstEmpty"/> on — the players who have left.</summary>
        public void HideFrom(int firstEmpty)
        {
            for (int i = firstEmpty; i < figures.Count; i++)
            {
                occupied[i] = false;
                if (figures[i] != null) figures[i].SetActive(false);
            }
        }

        public void Recolor(int slot, int color)
        {
            if (slot >= 0 && slot < recolors.Count && recolors[slot] != null)
                recolors[slot].Apply(color);
        }

        /// <summary>
        /// Turns every standing figure to the camera, level with the ground. Yaw only: taking the
        /// camera's pitch as well would tip the astronaut backwards to look up at a camera that
        /// sits above the rank.
        /// </summary>
        public void FaceCamera()
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            for (int i = 0; i < figures.Count; i++)
                if (IsStanding(i))
                    Face(figures[i].transform, camera);
        }

        public void Clear()
        {
            foreach (GameObject figure in figures)
                if (figure != null) Object.Destroy(figure);

            figures.Clear();
            heads.Clear();
            recolors.Clear();
            occupied.Clear();
        }

        private void Ensure(int slot, Transform anchor)
        {
            SlotLists.Grow(figures, slot);
            SlotLists.Grow(heads, slot);
            SlotLists.Grow(recolors, slot);
            SlotLists.Grow(occupied, slot);

            if (figures[slot] != null) return;

            if (prefab == null)
            {
                prefab = Resources.Load<GameObject>(PrefabResource);

                if (prefab == null)
                {
                    Debug.LogError($"[LobbyPreviewRank] No '{PrefabResource}' in a Resources folder. " +
                                   "Run Tools ▸ SpaceGame ▸ Menus ▸ Setup Lobby Preview to build it. " +
                                   "The lobby still works; it just has nobody standing in it.");
                    return;
                }
            }

            if (anchor == null) return;

            GameObject figure = Object.Instantiate(prefab, anchor);
            figure.name = $"PreviewAstronaut{slot}";

            figures[slot] = figure;
            heads[slot] = FindHead(figure.transform) ?? figure.transform;
            recolors[slot] = figure.GetComponentInChildren<SuitRecolor>(true);

            Camera camera = Camera.main;
            if (camera != null) Face(figure.transform, camera);

            SetupAnimator(figure, slot);
        }

        /// <summary>
        /// The bone the name hangs over, by name. Falls back to the figure root, which puts the
        /// name at its feet — wrong, but visible, which is the failure that gets reported.
        /// </summary>
        private static Transform FindHead(Transform root)
        {
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
                if (bone.name == HeadBone)
                    return bone;

            return null;
        }

        private static void Face(Transform figure, Camera camera)
        {
            Vector3 toCamera = camera.transform.position - figure.position;
            toCamera.y = 0f;

            if (toCamera.sqrMagnitude < 0.0001f) return;

            figure.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        }

        /// <summary>
        /// Stands the figure still. IsGrounded is set even though the controller already defaults
        /// it true: the default is a property of the asset, and a future edit that flips it would
        /// leave astronauts falling on the spot in the menu with nothing to explain why.
        /// </summary>
        private static void SetupAnimator(GameObject figure, int slot)
        {
            var animator = figure.GetComponentInChildren<Animator>(true);
            if (animator == null) return;

            animator.applyRootMotion = false;
            animator.SetBool("IsGrounded", true);
            animator.SetFloat("SpeedX", 0f);
            animator.SetFloat("SpeedY", 0f);
            animator.SetFloat("IdleIndex", slot % IdleVariants);
        }
    }
}
