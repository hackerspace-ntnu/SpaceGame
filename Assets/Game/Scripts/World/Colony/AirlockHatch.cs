using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Gameplay;

namespace SpaceGame.World
{
    /// <summary>
    /// One airlock hatch leaf: moves between its authored shut pose and that pose plus <see cref="openOffset"/> (a
    /// slide into the wall) and <see cref="openEuler"/> (a swing on its pivot). Put it on a clean parent of the
    /// leaf's mesh and a SOLID collider, so a shut hatch is a wall, an open one a doorway, and the crosshair lands
    /// on it.
    ///
    /// <para>
    /// Right-clicking the leaf operates its whole hatch (both leaves of the inner pair): it hands the click to the
    /// <see cref="AirlockChamber"/> that bound it, which decides. The leaf itself only moves toward what
    /// <see cref="Set"/> last said.
    /// </para>
    /// </summary>
    public sealed class AirlockHatch : MonoBehaviour, IInteractable, IInteractionReadout
    {
        [Tooltip("Where the leaf ends up open, relative to its shut position, in the parent's frame.")]
        [SerializeField] private Vector3 openOffset;

        [Tooltip("How far the leaf turns open about its own pivot, in degrees (a swinging plug door).")]
        [SerializeField] private Vector3 openEuler;

        [Tooltip("Seconds for a full open or close.")]
        [SerializeField, Min(0.05f)] private float travelSeconds = 1.2f;

        [Tooltip("The second leaf of a pair stays silent, so the hatch is one sound and not two.")]
        [SerializeField] private bool silent;

        [SerializeField] private SfxId openSound = SfxId.InteractDoorOpen;
        [SerializeField] private SfxId closeSound = SfxId.InteractDoorClose;

        private Vector3 shutPosition;
        private Quaternion shutRotation;
        private bool wantsOpen;
        private float travel;

        private AirlockChamber chamber;
        private AirlockSide side;

        /// <summary>Fully shut: the leaf is in the doorway.</summary>
        public bool IsShut => travel <= 0f;

        private void Awake()
        {
            shutPosition = transform.localPosition;
            shutRotation = transform.localRotation;
        }

        /// <summary>Called by the chamber that owns this leaf, once, in its Awake.</summary>
        public void Bind(AirlockChamber owner, AirlockSide hatch)
        {
            chamber = owner;
            side = hatch;
        }

        /// <summary>
        /// Move toward open or shut. <paramref name="instant"/> lands in the pose silently: the state was already
        /// true before this machine was looking (a late joiner).
        /// </summary>
        public void Set(bool open, bool instant)
        {
            if (instant)
            {
                wantsOpen = open;
                travel = open ? 1f : 0f;
                ApplyTravel(travel);
                return;
            }

            if (open == wantsOpen) return;
            wantsOpen = open;
            if (!silent) Sfx.Play(open ? openSound : closeSound, transform.position, GetInstanceID());
        }

        private void Update()
        {
            float target = wantsOpen ? 1f : 0f;
            if (Mathf.Approximately(travel, target)) return;

            travel = Mathf.MoveTowards(travel, target, Time.deltaTime / travelSeconds);
            ApplyTravel(travel);
        }

        private void ApplyTravel(float t)
        {
            float k = Mathf.SmoothStep(0f, 1f, t);
            transform.localPosition = shutPosition + openOffset * k;
            transform.localRotation = shutRotation * Quaternion.Euler(openEuler * k);
        }

        // ── Interaction: every press is answered, by a move or by a message saying why not ──

        public bool CanInteract() => chamber != null && chamber.isActiveAndEnabled;

        public void Interact(Interactor interactor)
        {
            if (CanInteract()) chamber.Operate(side, interactor);
        }

        public string Label => side == AirlockSide.Inner ? "Inner hatch" : "Outer hatch";
        public string Prompt => chamber != null ? chamber.PromptFor(side) : string.Empty;
        public float? Value01 => chamber != null ? chamber.CycleProgress : null;
        public string ValueText => chamber != null ? chamber.AirText : string.Empty;
    }
}
