using UnityEngine;
using SpaceGame.Persistence;

namespace SpaceGame.Items
{
    /// <summary>
    /// Puts anything on the scanner without that thing knowing the scanner exists.
    ///
    /// <para>
    /// Drop it on a crate, a wreck, a cache, a quest marker. <see cref="PickupableItem"/> registers
    /// itself because a loose item is the scanner's whole reason to exist and every one of them
    /// should show up by default; everything else opts in here, deliberately, so the display does
    /// not silently fill with scenery.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <see cref="IPersistentEntity"/> because a beacon is a piece of mutable world state and
    /// usually the ONLY one on its object. A spent cache is switched dark by turning
    /// <see cref="Active"/> off, and without the marker that object qualifies for nothing in
    /// <c>SaveablePolicy.NeedsSaving</c> — no health, no pickup, no agent, no loose body — so it
    /// would never get a <c>SaveableEntity</c> and every looted cache would light up again on load.
    /// </remarks>
    [DisallowMultipleComponent]
    public class ScanBeacon : MonoBehaviour, IScanTarget, IPersistentEntity
    {
        [Tooltip("Name shown for this contact. Falls back to the GameObject's name.")]
        [SerializeField] private string label;

        [Tooltip("Which glyph the display draws for this contact.")]
        [SerializeField] private ScanClass scanClass = ScanClass.Signal;

        [Tooltip("Optional point the return comes from. Leave empty to use this transform.")]
        [SerializeField] private Transform origin;

        [Tooltip("Uncheck to go dark without being removed — a spent cache, a looted crate.")]
        [SerializeField] private bool active = true;

        /// <summary>Turn the contact on or off at runtime.</summary>
        public bool Active
        {
            get => active;
            set => active = value;
        }

        public bool IsScannable => active && isActiveAndEnabled;
        public Vector3 ScanPosition => origin != null ? origin.position : transform.position;
        public ScanClass ScanClass => scanClass;
        public string ScanLabel => string.IsNullOrWhiteSpace(label) ? name : label;

        private void OnEnable() => ScannerRegistry.Register(this);
        private void OnDisable() => ScannerRegistry.Unregister(this);
    }
}
