using System;
using UnityEngine;

public class EquipmentController : MonoBehaviour
{
    [Tooltip("Where equipped items are parented. If left empty, the controller resolves it automatically: humanoid rigs use Animator.GetBoneTransform(handBone); generic rigs fall back to a name search using handBoneNameHints.")]
    [SerializeField] private Transform handSocket;
    [Tooltip("Which hand bone to use when auto-resolving handSocket from a humanoid rig.")]
    [SerializeField] private HumanBodyBones handBone = HumanBodyBones.RightHand;
    [Tooltip("Substring hints used when auto-resolving handSocket on a non-humanoid rig (case-insensitive). The first child Transform whose name contains any of these wins.")]
    [SerializeField] private string[] handBoneNameHints = { "RightHand", "Hand_R", "R_Hand", "hand.R" };

    private EquipItemSocket equipmentSocket;
    private IPlayerInventory inventory;

    private GameObject equippedItemObject;

    public event Action<InventoryItem> OnEquipRequested;

    private void Awake()
    {
        // Always prefer the actual armature bone — the serialized handSocket is
        // only a manual override for rigs the auto-resolver can't handle.
        var resolved = ResolveHandSocket();
        if (resolved != null)
        {
            handSocket = resolved;
        }
        else if (handSocket == null)
        {
            Debug.LogError("EquipmentController: could not resolve a hand bone. Assign handSocket manually or add hints in handBoneNameHints.", this);
        }
        else
        {
            Debug.LogWarning("EquipmentController: hand bone auto-resolve failed; falling back to the serialized handSocket Transform.", this);
        }

        equipmentSocket = new EquipItemSocket(handSocket);
    }

    private Transform ResolveHandSocket()
    {
        // Humanoid rig: ask the Animator for the actual bone Transform.
        var anim = GetComponentInChildren<Animator>(true);
        if (anim != null && anim.isHuman)
        {
            var bone = anim.GetBoneTransform(handBone);
            if (bone != null) return bone;
        }

        // Generic rig: substring-search the hierarchy by bone name.
        if (handBoneNameHints != null && handBoneNameHints.Length > 0)
        {
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].name;
                for (int h = 0; h < handBoneNameHints.Length; h++)
                {
                    var hint = handBoneNameHints[h];
                    if (string.IsNullOrEmpty(hint)) continue;
                    if (n.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                        return all[i];
                }
            }
        }

        return null;
    }

    private void Start()
    {
        var player = GetComponent<PlayerController>();
        inventory = player.PlayerInventory;

        inventory.OnSlotSelected += HandleEquip;
        inventory.OnSlotChanged += OnSlotChanged;
        inventory.OnItemDropped += OnItemDropped;

        player.Input.OnUsePressed += OnUse;
    }

    private void OnSlotChanged(int index, InventorySlot slot)
    {
        if (inventory.SelectedSlotIndex != index) return;
        HandleEquip(slot);
    }

    private void HandleEquip(InventorySlot slot)
    {
        if (slot == null || slot.IsEmpty)
        {
            Unequip();
            return;
        }
        
        OnEquipRequested?.Invoke(slot.Item);
        Equip(slot.Item);
    }


    public void Equip(InventoryItem item)
    {
        Unequip();

        if (item == null || item.itemPrefab == null)
        {
            Debug.LogError("EquipmentController.Equip: InventoryItem or itemPrefab is null!", this);
            return;
        }

        equippedItemObject = equipmentSocket.Equip(item.itemPrefab);

        if (equippedItemObject == null)
        {
            Debug.LogError($"EquipmentController.Equip: Failed to equip {item.name} - prefab instantiation failed!", this);
            return;
        }

        var usableItem = equippedItemObject.GetComponent<UsableItem>();
        if (usableItem)
        {
            usableItem.OnItemDepleted += ItemDepleted;
            usableItem.OnEquipped(gameObject);
        }
    }

    private void Unequip()
    {
        if (equippedItemObject)
        {
            var usable = equippedItemObject.GetComponent<UsableItem>();
            if (usable)
            {
                usable.OnUnequipped(gameObject);
                usable.OnItemDepleted -= ItemDepleted;
            }
        }

        equipmentSocket.Unequip();
        equippedItemObject = null;
    }

    private void ItemDepleted(UsableItem item)
    {
        item.OnItemDepleted -= ItemDepleted;
        inventory.TryRemoveItem(inventory.SelectedSlotIndex);
        Unequip();
    }

    private void OnUse()
    {
        if (!equippedItemObject) return;

        var usable = equippedItemObject.GetComponent<UsableItem>();
        if (!usable) return;
        
        var player = gameObject;
        
        usable.TryUse(player);
    }

    private void OnItemDropped(InventoryItem item)
    {
        GameServices.ItemDropService.DropItem(handSocket, item);
    }
}
