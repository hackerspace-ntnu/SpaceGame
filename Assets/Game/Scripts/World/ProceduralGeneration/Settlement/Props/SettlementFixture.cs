// A piece of settlement furniture a player can find and ask about: a forge, a loom, a table, a bed of tubers.
// The crosshair names it and says who is working at it right now; right-click reads its description on the
// visor; the item scanner lists it. Residents find the same fixture through the SettlementSpot children it
// carries (a post, a seat, an errand stop) — this component is only the player's side of it.
//
// Purely presentational and local: who stands where is already replicated by ResidentPresence, so nothing
// here is sent or saved.
using SpaceGame.Agents.Residents;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class SettlementFixture : MonoBehaviour, IInteractable, IInteractionReadout, IScanTarget
    {
        private const string MessageId = "settlement-fixture";

        [SerializeField] private string displayName;
        [SerializeField, TextArea(2, 4)] private string description;
        [SerializeField] private ScanClass scanClass = ScanClass.Site;
        [Tooltip("How long the description stays on the visor.")]
        [SerializeField, Min(1f)] private float lookSeconds = 6f;

        private SettlementSpot[] spots;
        private Settlement settlement;

        public string DisplayName => displayName;
        public string Description => description;

        private void Awake()
        {
            spots = GetComponentsInChildren<SettlementSpot>(true);
            settlement = GetComponentInParent<Settlement>();
        }

        private void OnEnable() => ScannerRegistry.Register(this);
        private void OnDisable() => ScannerRegistry.Unregister(this);

        public bool CanInteract() => !string.IsNullOrEmpty(description);

        public void Interact(Interactor interactor)
        {
            Resident worker = WorkerHere();
            string who = worker != null ? $" {worker.DisplayName}, the {worker.RoleName}, is here." : string.Empty;
            SystemMessages.Post(MessageId, $"{displayName}: {description}{who}", MessageSeverity.Info, lookSeconds);
        }

        public string Label => displayName;
        public string Prompt => "RMB: look";
        public float? Value01 => null;

        public string ValueText
        {
            get
            {
                Resident worker = WorkerHere();
                return worker != null ? $"{worker.DisplayName} is here" : null;
            }
        }

        public bool IsScannable => isActiveAndEnabled;
        public Vector3 ScanPosition => transform.position;
        public ScanClass ScanClass => scanClass;
        public string ScanLabel => displayName;

        // The resident holding one of this fixture's spots, read from the replicated held place.
        private Resident WorkerHere()
        {
            SettlementSociety society = settlement != null ? settlement.Society : null;
            if (society == null || spots == null || spots.Length == 0) return null;

            foreach (SettlementSpot spot in spots)
            {
                int place = society.PlaceOf(spot);
                if (place < 0) continue;
                foreach (Resident resident in society.Residents)
                    if (resident != null && resident.Presence != null && resident.Presence.Place == place) return resident;
            }
            return null;
        }
    }
}
