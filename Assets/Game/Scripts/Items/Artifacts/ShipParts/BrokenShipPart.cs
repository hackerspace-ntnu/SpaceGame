using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.Items
{
    /// <summary>
    /// A burnt-out module standing in its socket — the lander's dead transmitter — that a player
    /// looks at, reads as broken, and pulls out so a working one can go in.
    ///
    /// <para>
    /// The state is the rack's: <see cref="ShipPartRack.IsBroken"/> says the unit is there, and
    /// <see cref="ShipPartSocket"/> shows this object exactly while it is. Taking it is a request to
    /// the server on the ship's channel (<see cref="NetMsg.ShipPartTakeBroken"/>), which pops a
    /// <see cref="FizzlingHusk"/> out onto the floor and clears the bit; every machine then hides the
    /// unit through the replicated mask. It never goes into anybody's inventory.
    /// </para>
    /// <para>
    /// <b>It comes out only after it has burned</b> (<see cref="ShipPartFireRules.MayRemove"/>):
    /// before the fire it is jammed in its socket, while it burns it is too hot to touch. The
    /// readout says which, and the server asks again rather than trusting the press.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BrokenShipPart : MonoBehaviour, IInteractable, IInteractionReadout
    {
        [SerializeField] private string displayName = "Burnt-out Transmitter";

        [Tooltip("What pops out onto the floor when it is taken: a registered network prefab with " +
                 "a FizzlingHusk, which is junk, not an item.")]
        [SerializeField] private GameObject huskPrefab;

        [Tooltip("What the readout says before it has burned.")]
        [SerializeField] private string jammedPrompt = "Jammed in its socket";

        [Tooltip("What the readout says while it is on fire.")]
        [SerializeField] private string burningPrompt = "On fire: put it out first";

        [Tooltip("What the readout says once the fire is out.")]
        [SerializeField] private string removablePrompt = "Needs replacing.  RMB: take it off";

        [Tooltip("Where the husk pops out from: in front of the cradle.")]
        [SerializeField] private Transform dropPoint;

        [SerializeField] private SfxId refusedSound = SfxId.InteractDenied;

        private ShipPartRack rack;
        private ShipPartSocket socket;
        private ShipPartFire fire;

        private ShipPartRack Rack => rack != null ? rack : rack = GetComponentInParent<ShipPartRack>();
        private ShipPartSocket Socket => socket != null ? socket : socket = GetComponentInParent<ShipPartSocket>();
        private ShipPartFire Fire => fire != null ? fire : fire = GetComponentInParent<ShipPartFire>();

        private int Index => Rack != null && Socket != null ? Rack.IndexOf(Socket) : -1;

        private bool Burning => Fire != null && Fire.IsBurning;

        /// <summary>Burned and put out: the only time it comes out. A socket with no fire may always be emptied.</summary>
        private bool Removable => Fire == null || ShipPartFireRules.MayRemove(Fire.State);

        private void OnEnable() => this.NetOn(NetMsg.ShipPartTakeBroken, OnTakeRequested);

        private void OnDisable() => this.NetOff(NetMsg.ShipPartTakeBroken, OnTakeRequested);

        // ── The look ─────────────────────────────────────────────────────────

        public string Label => displayName;

        public string Prompt => Burning ? burningPrompt : Removable ? removablePrompt : jammedPrompt;

        public float? Value01 => Burning && Fire != null ? Fire.Strength : null;

        public string ValueText => Burning ? "FIRE" : "";

        // ── The press ────────────────────────────────────────────────────────

        public bool CanInteract() => Rack != null && Rack.IsBroken(Index);

        public void Interact(Interactor interactor)
        {
            if (interactor == null || !CanInteract()) return;

            // Refused on the presser's own machine with a sound, so the press is answered at
            // once; the server refuses it again on its own reading of the fire.
            if (!Removable)
            {
                Sfx.Play(refusedSound, transform.position, default, GetInstanceID());
                return;
            }

            this.NetToServer(NetMsg.ShipPartTakeBroken, new NetArg { A = Index });
        }

        // ── The server ───────────────────────────────────────────────────────

        private void OnTakeRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;
            if (arg.A != Index) return;

            // Idempotent: the second of two takers finds the bit already clear.
            TakeFor();
        }

        /// <summary>
        /// Pull the unit out and pop its husk onto the floor. Authority only. The husk is spawned
        /// first and the socket emptied after, so a failed spawn leaves the unit where it was.
        /// </summary>
        public bool TakeFor()
        {
            if (!Network.Simulates(this)) return false;
            if (Rack == null || !Rack.IsBroken(Index) || !Removable) return false;

            Transform at = dropPoint != null ? dropPoint : transform;
            if (huskPrefab != null && GameServices.World.Spawn(huskPrefab, at.position, transform.rotation) == null)
                return false;

            return Rack.TryRemoveBroken(Index);
        }
    }
}
