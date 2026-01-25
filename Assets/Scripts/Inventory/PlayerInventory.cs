
public class PlayerInventory : MonoBehaviour, IInventoryComponent
{
    private override void Awake()
    {
        Inventory = new Inventory(24);
    }
}
