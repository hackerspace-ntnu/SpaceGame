using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class InventorySlotUI : MonoBehaviour/*,*/
    /*IPointerDownHandler,
    IPointerUpHandler,
    IPointerEnterHandler*/
{

    public int slotIndex;
    [SerializeField] private Image icon;

    public InventorySlotUI()
    {
        
    }

    public void Refresh(InventorySlot slot)
    {
    
        if (slot != null)
        {
            icon.sprite = slot.Item.icon;
        }
        else
        {
            icon.enabled = false;
        }
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