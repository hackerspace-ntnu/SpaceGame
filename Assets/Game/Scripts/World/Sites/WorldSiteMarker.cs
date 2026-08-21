// Drop-anywhere site marker. Add it to a settlement root, a ruin, a rock field or an empty
// GameObject and NPCs can be sent there.
//
// Modelled deliberately on MapPOI, which solves the identical problem for map markers: capture a
// world position on first enable, hand it to a registry that outlives the chunk, and use a stable
// serialized id so streaming the chunk back in updates the record rather than duplicating it.
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public class WorldSiteMarker : MonoBehaviour
    {
        [Tooltip("What NPCs can come here FOR. Tasks name a kind, not a place.")]
        [SerializeField] private SiteKind kind = SiteKind.Landmark;

        [Tooltip("Shown to the player in NPC chatter and dialog — \"heading up to the Vela wreck\". " +
                 "Leave empty for somewhere with no name.")]
        [SerializeField] private string siteName = string.Empty;

        [Tooltip("How big the place is. An NPC counts as arrived anywhere inside this, and wanders " +
                 "within it while it works.")]
        [SerializeField] private float radius = 12f;

        [Tooltip("Also register a map POI here, so a site the player can be told about is a site " +
                 "they can find. Requires a MapPOI component on this object.")]
        [SerializeField] private bool mirrorToMap = false;

        [Tooltip("Stable unique id. Auto-generated on first add — don't edit unless you know what " +
                 "you're doing. Changing it orphans the old record for the rest of the session.")]
        [HideInInspector]
        [SerializeField] private string id;

        public SiteKind Kind => kind;
        public string SiteId => id;
        public string SiteName => siteName;

        private void Reset()      => EnsureId();
        private void OnValidate()
        {
            EnsureId();
            radius = Mathf.Max(1f, radius);
        }

        private void EnsureId()
        {
            if (string.IsNullOrEmpty(id))
                id = System.Guid.NewGuid().ToString("N");
        }

        private void OnEnable()
        {
            EnsureId();

            // Position is read now rather than cached at bake time, so a marker parented to
            // something that moves (a caravan's own camp, a ship) reports where it actually is.
            WorldSiteRegistry.Register(kind, transform.position, radius, siteName, id);

            if (mirrorToMap && TryGetComponent(out MapPOI poi))
                poi.Refresh();
        }

        // Deliberately NOT unregistering in OnDisable. The record is the whole reason this class
        // exists: a caravan two chunks away is walking toward this site right now, and the site's
        // own chunk unloading is not news it should ever receive. The registry is cleared when play
        // starts and when a world is unloaded, which are the only two moments a site stops existing.

        /// <summary>
        /// Re-publish this marker's current position and settings. For markers on things that move,
        /// and for editing values during a live session.
        /// </summary>
        public void Refresh()
        {
            EnsureId();
            WorldSiteRegistry.Register(kind, transform.position, radius, siteName, id);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = KindColour(kind);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(1f, radius));
        }

        private static Color KindColour(SiteKind kind) => kind switch
        {
            SiteKind.Home         => new Color(0.4f, 0.9f, 0.5f),
            SiteKind.Camp         => new Color(0.6f, 0.8f, 0.4f),
            SiteKind.Ruin         => new Color(0.8f, 0.6f, 1f),
            SiteKind.ScrapField   => new Color(1f, 0.7f, 0.3f),
            SiteKind.WaterHole    => new Color(0.3f, 0.7f, 1f),
            SiteKind.TradePost    => new Color(1f, 0.9f, 0.3f),
            SiteKind.AnimalGround => new Color(1f, 0.5f, 0.5f),
            _                     => Color.gray,
        };
    }
}
