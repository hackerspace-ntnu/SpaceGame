// Equips the first item found in EntityInventoryComponent into a hand socket.
// Automatically uses the equipped item on a configurable interval (for weapons).
// Swap equipped slot at runtime by calling EquipSlot(index).
using UnityEngine;

public class EntityEquipmentController : MonoBehaviour
{
    [Header("Socket")]
    [SerializeField] private Transform handSocket;

    [Header("Auto-use")]
    [Tooltip("If true, automatically calls TryUse on the equipped item at the given interval.")]
    [SerializeField] private bool autoUse = false;
    [SerializeField] private float autoUseInterval = 1f;

    private EntityInventoryComponent entityInventory;
    private EquipItemSocket socket;
    private GameObject equippedObject;
    private int equippedSlotIndex = -1;
    private float autoUseTimer;

    private void Awake()
    {
        entityInventory = GetComponent<EntityInventoryComponent>();
        socket = new EquipItemSocket(handSocket);

        if (!handSocket)
            Debug.LogWarning($"{name}: EntityEquipmentController has no handSocket assigned.", this);
    }

    private void Start()
    {
        // Auto-equip whatever is in slot 0 on start.
        EquipSlot(0);

        if (entityInventory)
            entityInventory.OnSlotChanged += OnInventorySlotChanged;
    }

    private void OnDestroy()
    {
        if (entityInventory)
            entityInventory.OnSlotChanged -= OnInventorySlotChanged;
    }

    private void Update()
    {
        if (!autoUse || !equippedObject)
            return;

        autoUseTimer -= Time.deltaTime;
        if (autoUseTimer > 0f)
            return;

        autoUseTimer = autoUseInterval;
        UseEquipped();
    }

    public void EquipSlot(int slotIndex)
    {
        if (entityInventory == null)
            return;

        InventorySlot slot = entityInventory.GetSlot(slotIndex);
        if (slot == null || slot.IsEmpty)
        {
            Unequip();
            return;
        }

        equippedSlotIndex = slotIndex;
        equippedObject = socket.Equip(slot.Item.itemPrefab);
    }

    public void Unequip()
    {
        socket.Unequip();
        equippedObject = null;
        equippedSlotIndex = -1;
    }

    public void UseEquipped()
    {
        if (!equippedObject)
            return;

        UsableItem usable = equippedObject.GetComponent<UsableItem>();
        if (usable)
            usable.TryUse(gameObject);
    }

    private void OnInventorySlotChanged(int index, InventorySlot slot)
    {
        if (index == equippedSlotIndex)
            EquipSlot(index);
    }
}
