using UnityEngine;
using UnityEngine.UI;

public class InventorySlotUI : MonoBehaviour/*,*/
    /*IPointerDownHandler,
    IPointerUpHandler,
    IPointerEnterHandler*/
    {
        
    private int slotIndex;
    private InventoryUI parentUI;
        
    [SerializeField] private Image background;
    [SerializeField] private Image icon;
    
    private bool selected = false; 
    private bool hovered = false; 
    
    public void Init(int index, InventoryUI parent)
    {
        this.slotIndex = index;
        this.parentUI = parent;
    }
    
    public void Refresh(InventorySlot slot, bool isSelected, bool isHovered)
    {
        selected = isSelected;
        hovered = isHovered;
        if (slot.Item)
        {
            icon.sprite = slot.Item.icon;
            icon.enabled = true;
        }
        else
        {
            icon.enabled = false;
        }

        UpdateHighlight();
    }
    
    private void UpdateHighlight()
    {
        if (selected)
            background.color = new Color(1f, 1f, 1f, 0.9f);
        else if (hovered)
            background.color = new Color(1f, 1f, 1f, 0.6f);
        else
            background.color = new Color(1f, 1f, 1f, 0.3f);
    }

    /*public void OnPointerDown(PointerEventData eventData)
    {
        DragController.Instance.StartDrag(slotIndex);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        DragController.Instance.EndDrag(slotIndex);
    }
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        DragController.Instance.HoverSlot(slotIndex);
    }*/
}