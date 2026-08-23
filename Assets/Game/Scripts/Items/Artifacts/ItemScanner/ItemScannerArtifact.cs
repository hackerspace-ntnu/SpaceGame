using System.Collections.Generic;
using FMODUnity;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Item Scanner — a forearm-mounted set that finds loose salvage inside 100 m and draws it on
    /// a phosphor display.
    ///
    /// <para>
    /// Using it toggles power rather than firing a pulse. A one-shot ping would make the useful
    /// state — walking around watching the display — something the player has to keep re-triggering,
    /// and the whole value of the tool is that it stays on while you search.
    /// </para>
    /// <para>
    /// <b>Authority.</b> <see cref="UseAuthority.Owner"/>, and the effect is left entirely to
    /// <see cref="Present"/>. Scanning changes nothing in the world: it reads a registry, moves a
    /// dial, and lights a screen. So every machine runs the whole thing on its own copy — the
    /// holder's, so the display never waits for a round trip, and the peers', so a scanner held by
    /// somebody else is visibly lit and sweeping instead of being a dead prop. The only thing that
    /// crosses the wire is the toggle itself, which is one bit inside the use message the equipment
    /// controller already sends.
    /// </para>
    /// <para>
    /// Peers deliberately scan against <em>their own</em> copy of the registry and get their own
    /// contact list. It will not match the holder's exactly, and it does not need to: nobody else
    /// can read the screen at that size, and syncing a 24-contact list at 4 Hz to make an
    /// unreadable display accurate would be a real cost for no gain.
    /// </para>
    /// </summary>
    public class ItemScannerArtifact : ToolItem
    {
        [Header("Scan")]
        [Tooltip("Detection radius in metres.")]
        [SerializeField] private float range = 100f;

        [Tooltip("Seconds between scans. The display interpolates between them, so this can be " +
                 "slow without the screen looking slow.")]
        [SerializeField] private float scanInterval = 0.25f;

        [Tooltip("Most contacts drawn at once. Beyond this the nearest win and the header count " +
                 "still reports the true total.")]
        [SerializeField] private int maxContacts = 24;

        [Tooltip("Drop contacts with no line of sight to the scanner. Off by default: the point " +
                 "of a scanner is finding what you cannot see.")]
        [SerializeField] private bool requireLineOfSight;

        [Tooltip("What blocks the line-of-sight check, when it is enabled.")]
        [SerializeField] private LayerMask sightBlockers = ~0;

        [Header("Parts")]
        [Tooltip("The display driver on the screen plate.")]
        [SerializeField] private ItemScannerScreen screen;

        [Tooltip("Ribbed knob. Spun while scanning, faster with more contacts.")]
        [SerializeField] private Transform dial;

        [Tooltip("Whip antenna. Swayed while scanning.")]
        [SerializeField] private Transform antenna;

        [Tooltip("Degrees per second the dial turns at, at full load.")]
        [SerializeField] private float dialSpeed = 220f;

        [Tooltip("Degrees the antenna tip sways through.")]
        [SerializeField] private float antennaSway = 7f;

        [Header("Audio")]
        [Tooltip("Click when the set is switched on or off.")]
        [SerializeField] private SfxId toggleId = SfxId.InteractLever;
        [SerializeField] private EventReference toggleSound;

        [Tooltip("Contact ping. Rate rises as the nearest contact gets closer, like a detector.")]
        [SerializeField] private SfxId pingId = SfxId.InteractScannerDiscovery;
        [SerializeField] private EventReference pingSound;

        [Tooltip("Seconds between pings with a contact at arm's length.")]
        [SerializeField] private float pingIntervalNear = 0.30f;

        [Tooltip("Seconds between pings with a contact at the edge of range.")]
        [SerializeField] private float pingIntervalFar = 2.0f;

        private readonly List<ScanContact> contacts = new();

        private bool powered;
        private float nextScanTime;
        private float nextPingTime;
        private float dialAngle;
        private int totalFound;
        private float nearest;

        /// <summary>True while the set is switched on. Read by anything that wants to know.</summary>
        public bool Powered => powered;

        /// <summary>Contacts from the most recent scan, nearest first. Live list — do not keep it.</summary>
        public IReadOnlyList<ScanContact> Contacts => contacts;

        /// <summary>The whole effect is local and cosmetic. See the class summary.</summary>
        public override UseAuthority Authority => UseAuthority.Owner;

        protected override void Use()
        {
            // Nothing. Scanning changes nothing another machine could disagree about, so there is
            // no authority-side half — deliberately, and not an oversight.
        }

        /// <summary>Every machine: flip the set, and let the screen animate the tube.</summary>
        protected override void Present()
        {
            powered = !powered;

            if (screen != null) screen.SetOn(powered);
            Sfx.Play(toggleId, transform.position, toggleSound, GetInstanceID());

            if (powered)
            {
                nextScanTime = 0f;   // scan on the next frame, not in scanInterval seconds
                nextPingTime = Time.time + 0.35f;
            }
            else
            {
                contacts.Clear();
                totalFound = 0;
                nearest = 0f;
            }
        }

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);
            // Deliberately off on equip. A scanner that wakes up lit means a player who never
            // chose to scan is still broadcasting a lit screen and a ping every two seconds.
            powered = false;
            if (screen != null) screen.Blackout();
        }

        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);
            powered = false;
            contacts.Clear();
            if (screen != null) screen.Blackout();
        }

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // "Deliberately off on equip" above is right for picking a scanner up and wrong for putting
        // one down and taking it out again ten seconds later — and wrong for a reload, where the
        // player never let go of it at all. The switch position belongs to the hotbar slot; see
        // ItemState.

        private const string PoweredKey = "on";
        private const string ScanKey = "scan";
        private const string PingKey = "ping";
        private const string DialKey = "dial";
        private const string FoundKey = "found";

        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state == null || !powered) return;

            state.Set(PoweredKey, true);
            state.Set(DialKey, dialAngle);
            state.Set(FoundKey, totalFound);

            // Both timers are stamps on Time.time, which restarts every session. Stored as the wait
            // that is left, so a scanner does not come back either permanently due or never due.
            state.Set(ScanKey, Mathf.Max(0f, nextScanTime - Time.time));
            state.Set(PingKey, Mathf.Max(0f, nextPingTime - Time.time));
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);

            powered = state != null && state.GetBool(PoweredKey);

            if (!powered)
            {
                contacts.Clear();
                totalFound = 0;
                nearest = 0f;
                if (screen != null) screen.Blackout();
                return;
            }

            dialAngle = state.GetFloat(DialKey);
            totalFound = state.GetInt(FoundKey);
            nextScanTime = Time.time + state.GetFloat(ScanKey);
            nextPingTime = Time.time + state.GetFloat(PingKey);

            // The contact list itself is not stored: it is a reading of the world taken a quarter of
            // a second ago, and the next Scan() replaces it wholesale. Lighting the tube is what the
            // player actually notices.
            if (screen != null) screen.SetOn(true);
        }

        private void Update()
        {
            if (powered && Time.time >= nextScanTime)
            {
                nextScanTime = Time.time + Mathf.Max(0.02f, scanInterval);
                Scan();
            }

            AnimateParts();
            Ping();
        }

        private void Scan()
        {
            Vector3 origin = ScanOrigin();
            totalFound = ScannerRegistry.Collect(origin, range, contacts,
                                                 Mathf.Clamp(maxContacts, 1, ItemScannerScreen.MaxBlips));

            if (requireLineOfSight) FilterByLineOfSight(origin);

            nearest = contacts.Count > 0 ? contacts[0].Distance : 0f;

            if (screen == null) return;

            ResolveFrame(out Vector3 forward, out Vector3 right);
            screen.Report(contacts, totalFound, origin, forward, right, range);
        }

        private void FilterByLineOfSight(Vector3 origin)
        {
            for (int i = contacts.Count - 1; i >= 0; i--)
            {
                Vector3 to = contacts[i].Position - origin;
                float d = to.magnitude;
                if (d < 0.1f) continue;

                if (Physics.Raycast(origin, to / d, out RaycastHit hit, d - 0.05f,
                                    sightBlockers, QueryTriggerInteraction.Ignore))
                {
                    // The holder's own body is between the scanner and half the world.
                    if (owner == null || !hit.collider.transform.IsChildOf(owner.transform))
                    {
                        contacts.RemoveAt(i);
                        totalFound = Mathf.Max(0, totalFound - 1);
                    }
                }
            }
        }

        /// <summary>Where the scan is centred: the holder if there is one, else the device.</summary>
        private Vector3 ScanOrigin() =>
            owner != null ? owner.transform.position : transform.position;

        /// <summary>
        /// The horizontal frame the display is read against.
        ///
        /// <para>
        /// Taken from the holder's body rather than a camera, and that is the point: every machine
        /// has the holder's transform and only one machine has their camera. A peer resolving
        /// <c>Camera.main</c> would orient a remote player's scanner to where the local player
        /// happens to be looking, which is the exact bug the Ruin Scanner's aim plumbing exists to
        /// avoid.
        /// </para>
        /// </summary>
        private void ResolveFrame(out Vector3 forward, out Vector3 right)
        {
            Vector3 source = owner != null ? owner.transform.forward : transform.forward;
            forward = Vector3.ProjectOnPlane(source, Vector3.up);

            if (forward.sqrMagnitude < 1e-4f)
            {
                // Looking straight up or down. Fall back to the body's own up axis projected flat,
                // which still turns with the player instead of snapping to world north.
                Vector3 fallback = owner != null ? owner.transform.up : transform.up;
                forward = Vector3.ProjectOnPlane(fallback, Vector3.up);
                if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            }

            forward.Normalize();
            right = Vector3.Cross(Vector3.up, forward).normalized;
        }

        /// <summary>
        /// Spin the dial and sway the mast while the set works.
        ///
        /// Both are driven off the same load figure the display shows, so the device's mechanical
        /// noise and its readout agree — a dial racing over an empty screen reads as broken.
        /// </summary>
        private void AnimateParts()
        {
            float lit = screen != null ? screen.Power : (powered ? 1f : 0f);
            if (lit <= 0.001f) return;

            float load = contacts.Count == 0
                ? 0.25f
                : Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(contacts.Count / 8f));

            if (dial != null)
            {
                dialAngle += dialSpeed * load * lit * Time.deltaTime;
                // Local Z, because the knob's axis is its own -Y in Blender, which the FBX
                // conversion lands on local Z. Its origin is on that axis, so a local rotation
                // spins it in place.
                dial.localRotation = Quaternion.Euler(0f, 0f, dialAngle);
            }

            if (antenna != null)
            {
                float t = Time.time;
                float sway = antennaSway * lit;
                antenna.localRotation = Quaternion.Euler(
                    Mathf.Sin(t * 2.3f) * sway * 0.6f,
                    0f,
                    Mathf.Sin(t * 1.7f + 1.1f) * sway);
            }
        }

        /// <summary>
        /// Proximity ping: faster the closer the nearest contact is.
        ///
        /// The rate carries the same information as the number on the display, which is the point —
        /// it lets a player sweep an area by ear while looking where they are walking.
        /// </summary>
        private void Ping()
        {
            if (!powered || contacts.Count == 0) return;
            if (Time.time < nextPingTime) return;

            float t = Mathf.Clamp01(nearest / Mathf.Max(1f, range));
            nextPingTime = Time.time + Mathf.Lerp(pingIntervalNear, pingIntervalFar, t);

            Sfx.Play(pingId, transform.position, pingSound, GetInstanceID());
        }

        private void OnValidate()
        {
            range = Mathf.Max(1f, range);
            scanInterval = Mathf.Max(0.02f, scanInterval);
            maxContacts = Mathf.Clamp(maxContacts, 1, ItemScannerScreen.MaxBlips);
            pingIntervalNear = Mathf.Max(0.05f, pingIntervalNear);
            pingIntervalFar = Mathf.Max(pingIntervalNear, pingIntervalFar);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.35f, 1f, 0.55f, 0.35f);
            Gizmos.DrawWireSphere(ScanOrigin(), range);
        }
#endif
    }
}
