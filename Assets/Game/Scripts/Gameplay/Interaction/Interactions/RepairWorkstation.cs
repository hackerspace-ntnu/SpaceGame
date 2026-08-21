using FMODUnity;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// A machine that is repaired by feeding it ship scrap. Interacting while the required
    /// item sits in the selected hotbar slot consumes one and advances the repair; interacting
    /// with anything else (or an empty hand) is rejected.
    ///
    /// Progress is owned by the server and replicated, so the gauge on every client agrees.
    /// Runs fine offline too — <see cref="Network.Execute"/> keeps the single-player path local.
    ///
    /// <para>
    /// <see cref="IPersistentEntity"/> because four of five scrap fed into a machine is quest
    /// progress, and a workstation has none of the components <c>SaveablePolicy.NeedsSaving</c>
    /// otherwise looks for — so without the marker it would never be wired for saving and the
    /// progress would be back at zero, for everyone, after every load.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class RepairWorkstation : NetworkBehaviour, IInteractable, IPersistentEntity
    {
        [Header("Repair")]
        [Tooltip("Item the workstation accepts, e.g. Assets/Game/Resources/Items/Scraps.asset.")]
        [SerializeField] private InventoryItem requiredItem;

        [Tooltip("How many of the required item a full repair takes.")]
        [SerializeField, Min(1)] private int requiredAmount = 5;

        [Header("Visuals")]
        [Tooltip("Parts that start spinning once the workstation is fully repaired.")]
        [SerializeField] private Transform[] spinningParts;

        [Tooltip("Local axis the spinning parts turn around. Y matches a Unity cylinder's own axis.")]
        [SerializeField] private Vector3 spinAxis = Vector3.up;

        [SerializeField] private float spinSpeed = 220f;

        [Tooltip("Renderer tinted from broken to repaired colour as scrap goes in. Optional.")]
        [SerializeField] private Renderer statusLight;

        [SerializeField] private Color brokenColor = new Color(0.9f, 0.15f, 0.1f);
        [SerializeField] private Color repairedColor = new Color(0.2f, 1f, 0.35f);

        [Tooltip("Transform nudged when scrap is accepted, to sell the 'clunk'. Optional.")]
        [SerializeField] private Transform clunkTarget;

        [SerializeField] private float clunkDistance = 0.04f;
        [SerializeField] private float clunkDuration = 0.18f;

        [Header("Events")]
        [Tooltip("Fires on every accepted scrap with the new progress in 0..1.")]
        [SerializeField] private UnityEvent<float> onScrapAccepted;

        [Tooltip("Fires when a player interacts without the required item in hand.")]
        [SerializeField] private UnityEvent onScrapRejected;

        [Tooltip("Fires once, when the last piece of scrap completes the repair.")]
        [SerializeField] private UnityEvent onRepaired;

        private readonly NetworkVariable<int> networkProgress = new(0);

        // Mirrors networkProgress, and is the sole source of truth when there is no session.
        private int progress;
        private bool spawned;

        private MaterialPropertyBlock propertyBlock;
        private Vector3 clunkRestPosition;
        private float clunkTimer;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>Raised on every progress change, with the new progress in 0..1.</summary>
        public event System.Action<float> ProgressChanged;

        public int RequiredAmount => Mathf.Max(1, requiredAmount);
        public int CurrentAmount => progress;
        public float Progress01 => Mathf.Clamp01((float)progress / RequiredAmount);
        public bool IsRepaired => progress >= RequiredAmount;
        public InventoryItem RequiredItem => requiredItem;

        private void Awake()
        {
            if (clunkTarget != null) clunkRestPosition = clunkTarget.localPosition;
            ApplyStatusLight();
        }

        public override void OnNetworkSpawn()
        {
            spawned = true;
            networkProgress.OnValueChanged += HandleNetworkProgressChanged;

            if (IsServer)
            {
                networkProgress.Value = progress;
            }
            else
            {
                // Catching up on work done before we joined is not an event worth announcing.
                SetProgressLocal(networkProgress.Value, silent: true);
            }
        }

        public override void OnNetworkDespawn()
        {
            spawned = false;
            networkProgress.OnValueChanged -= HandleNetworkProgressChanged;
        }

        [Header("Audio")]
        [SerializeField] private SfxId acceptedId = SfxId.InteractWorkstationRepair;
        [SerializeField] private EventReference acceptedSound;
        [SerializeField] private SfxId rejectedId = SfxId.InteractDenied;

        // Interacting with a finished machine does nothing, so let the crosshair say so.
        public bool CanInteract() => !IsRepaired;

        public void Interact(Interactor interactor)
        {
            if (interactor == null) return;

            Network.Execute(
                local: () => TryInsertScrap(interactor),
                client: () => InteractorRelay.RequestFrom(interactor, InsertScrapServerRpc));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void InsertScrapServerRpc(NetworkObjectReference interactorRef)
        {
            if (!InteractorRelay.TryResolve(interactorRef, out Interactor interactor)) return;

            TryInsertScrap(interactor);
        }

        /// <summary>Server-side (or offline) authority: validate the held item, consume it, advance.</summary>
        private void TryInsertScrap(Interactor interactor)
        {
            if (interactor == null || IsRepaired) return;

            if (requiredItem == null)
            {
                Debug.LogWarning($"{name}: RepairWorkstation has no required item assigned.", this);
                return;
            }

            IPlayerInventory inventory = interactor.GetComponentInParent<IPlayerInventory>();
            if (inventory == null) return;

            InventorySlot slot = inventory.GetSelectedSlot();
            if (slot == null || slot.IsEmpty || !IsRequiredItem(slot.Item))
            {
                BroadcastFeedback(false);
                return;
            }

            if (!inventory.TryRemoveItem(inventory.SelectedSlotIndex)) return;

            SetProgressAuthoritative(progress + 1);
            BroadcastFeedback(true);
        }

        /// <summary>
        /// Reference equality is the reliable test — every slot resolves through
        /// <see cref="Registry{T}"/> to the same Resources asset the inspector field points at.
        /// The ID compare is a fallback for items duplicated at runtime.
        /// </summary>
        private bool IsRequiredItem(InventoryItem item)
        {
            if (item == requiredItem) return true;

            return !string.IsNullOrEmpty(item.ID)
                   && !string.IsNullOrEmpty(requiredItem.ID)
                   && item.ID == requiredItem.ID;
        }

        private void SetProgressAuthoritative(int value)
        {
            value = Mathf.Clamp(value, 0, RequiredAmount);

            if (spawned && IsServer)
            {
                // The NetworkVariable callback drives SetProgressLocal on the host as well.
                networkProgress.Value = value;
                return;
            }

            SetProgressLocal(value);
        }

        private void HandleNetworkProgressChanged(int previous, int current) => SetProgressLocal(current);

        private void SetProgressLocal(int value, bool silent = false)
        {
            value = Mathf.Clamp(value, 0, RequiredAmount);
            if (value == progress) return;

            bool wasRepaired = IsRepaired;
            progress = value;

            ApplyStatusLight();
            ProgressChanged?.Invoke(Progress01);
            if (silent) return;

            onScrapAccepted?.Invoke(Progress01);
            if (!wasRepaired && IsRepaired) onRepaired?.Invoke();
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// <para>
        /// Written through the <c>NetworkVariable</c> when there is a session, so every client gets
        /// the restored gauge by the same replication that carries an ordinary deposit — a restore
        /// that only set the local mirror would leave the host repaired and everyone else broken.
        /// </para>
        /// <para>
        /// The local mirror is set FIRST, and that ordering is the whole reason this is not
        /// <c>SetProgressAuthoritative</c>: the NetworkVariable callback lands back in
        /// <see cref="SetProgressLocal"/>, which returns early when the value already matches, so
        /// <c>onScrapAccepted</c> and — the one that matters — <c>onRepaired</c> do not fire a
        /// second time for work that was already done and already had its consequences.
        /// </para>
        /// <para>
        /// Safe before <c>OnNetworkSpawn</c> too: the mirror is the sole truth until then, and
        /// spawn publishes it.
        /// </para>
        /// </summary>
        public void RestoreProgress(int amount)
        {
            amount = Mathf.Clamp(amount, 0, RequiredAmount);

            progress = amount;

            if (spawned && IsServer) networkProgress.Value = amount;

            ApplyStatusLight();
            ProgressChanged?.Invoke(Progress01);
        }

        /// <summary>Accept/reject feedback has to reach every client, not just the deciding server.</summary>
        private void BroadcastFeedback(bool accepted)
        {
            if (spawned && IsServer)
            {
                PlayFeedbackRpc(accepted);
                return;
            }

            PlayFeedback(accepted);
        }

        [Rpc(SendTo.Everyone)]
        private void PlayFeedbackRpc(bool accepted) => PlayFeedback(accepted);

        private void PlayFeedback(bool accepted)
        {
            // This already runs on every client — it is the path that exists so accept/reject
            // feedback is not confined to the deciding server — which makes it the one place the
            // sound can go and be heard by everyone who is standing there.
            Sfx.Play(accepted ? acceptedId : rejectedId, transform.position,
                     accepted ? acceptedSound : default, GetInstanceID());

            if (accepted)
            {
                clunkTimer = clunkDuration;
                return;
            }

            onScrapRejected?.Invoke();
        }

        private void Update()
        {
            if (IsRepaired && spinningParts != null && spinAxis != Vector3.zero)
            {
                float step = spinSpeed * Time.deltaTime;
                foreach (Transform part in spinningParts)
                {
                    if (part != null) part.Rotate(spinAxis, step, Space.Self);
                }
            }

            if (clunkTimer <= 0f || clunkTarget == null) return;

            clunkTimer -= Time.deltaTime;
            float phase = Mathf.Clamp01(clunkTimer / Mathf.Max(0.01f, clunkDuration));
            float offset = Mathf.Sin(phase * Mathf.PI * 2f) * clunkDistance * phase;
            clunkTarget.localPosition = clunkRestPosition + Vector3.up * offset;

            if (clunkTimer <= 0f) clunkTarget.localPosition = clunkRestPosition;
        }

        private void ApplyStatusLight()
        {
            if (statusLight == null) return;

            propertyBlock ??= new MaterialPropertyBlock();
            Color color = Color.Lerp(brokenColor, repairedColor, Progress01);

            statusLight.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            propertyBlock.SetColor(EmissionColorId, color * 2f);
            statusLight.SetPropertyBlock(propertyBlock);
        }

    #if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying) ApplyStatusLight();
        }
    #endif
    }
}
