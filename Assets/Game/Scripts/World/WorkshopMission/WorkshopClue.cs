// One clue on the raiders' trail, local to this machine (WorkshopClueTrail lays it). The item scanner finds it while it
// has not been examined; the interact button examines it: the finding is read out here at once, and the server is asked
// to count it.
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class WorkshopClue : MonoBehaviour, IInteractable, IInteractionReadout, IScanTarget
    {
        private const string ExaminePrompt = "Examine";
        // Small enough to stand over, big enough for the crosshair to find a cog half buried in sand.
        private const float MinHitboxRadius = 0.5f;

        private WorkshopMission mission;
        private int index;
        private bool registered;

        /// <summary>Already examined by somebody: no longer on the scanner, no longer offered.</summary>
        public bool Examined { get; set; }

        public void Bind(WorkshopMission owner, int clueIndex, GameObject model, float scale)
        {
            mission = owner;
            index = clueIndex;
            if (model != null)
            {
                GameObject drawn = Instantiate(model, transform);
                drawn.transform.localScale = Vector3.Scale(drawn.transform.localScale, Vector3.one * scale);
                foreach (Collider solid in drawn.GetComponentsInChildren<Collider>()) solid.enabled = false;
            }

            // What the crosshair finds: a sphere round the clue, a trigger so nobody trips on it.
            var hitbox = gameObject.AddComponent<SphereCollider>();
            hitbox.isTrigger = true;
            hitbox.radius = Mathf.Max(MinHitboxRadius, scale);
            ScannerRegistry.Register(this);
            registered = true;
        }

        private void OnDestroy()
        {
            if (registered) ScannerRegistry.Unregister(this);
        }

        public bool CanInteract() => mission != null && !Examined;

        public void Interact(Interactor interactor)
        {
            if (!CanInteract()) return;
            NpcDialogPopupUI.Instance?.Show(mission.Trail.Finding(index));
            mission.RequestExamine(index, interactor);
        }

        public string Label => Examined ? string.Empty : "Something the raiders dropped";
        public string Prompt => CanInteract() ? ExaminePrompt : string.Empty;
        public float? Value01 => null;
        public string ValueText => string.Empty;

        public bool IsScannable => !Examined;
        public Vector3 ScanPosition => transform.position;
        public ScanClass ScanClass => ScanClass.Signal;
        public string ScanLabel => "Raiders' trail";
    }
}
