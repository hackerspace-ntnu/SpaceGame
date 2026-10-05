// The crank at the foot of a weathervane: right-click to turn it a quarter. On the crank's own
// collider, so the plinth and pole — solid colliders with no interactable — block the ray rather
// than answering for the crank.
using UnityEngine;

namespace SpaceGame.Gameplay.Puzzles
{
    public class WeathervaneCrank : MonoBehaviour, IInteractable, IInteractionReadout
    {
        private WeathervaneVane vane;
        private WeathervaneRing ring;

        // Lazy: resolved on first use so a crank works however its hierarchy was assembled, and an
        // EditMode test that never runs Awake can still ask it questions.
        private WeathervaneVane Vane => vane != null ? vane : vane = GetComponentInParent<WeathervaneVane>();
        private WeathervaneRing Ring => ring != null ? ring : ring = GetComponentInParent<WeathervaneRing>();

        private int VaneIndex => Ring != null && Vane != null ? Ring.IndexOf(Vane) : -1;

        public bool CanInteract() => Ring != null && VaneIndex >= 0 && Ring.CanTurn;

        public void Interact(Interactor interactor)
        {
            if (!CanInteract()) return;
            Ring.RequestTurn(VaneIndex);
        }

        public string Label => "Weathervane crank";

        public string Prompt => "RMB: turn";

        public float? Value01 => null;

        public string ValueText
        {
            get
            {
                if (Ring == null || !Ring.IsKnown || VaneIndex < 0) return string.Empty;
                return Ring.Positions[VaneIndex] == 0 ? "pointing up the plateau" : string.Empty;
            }
        }
    }
}
