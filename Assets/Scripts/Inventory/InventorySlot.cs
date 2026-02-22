using System;
using Unity.Netcode;
using UnityEngine;

public struct InventorySlot : INetworkSerializable, IEquatable<InventorySlot>
{
    public int ItemId;
    public int Amount;
    
    public InventorySlot(int itemId, int amount)
    {
        ItemId = itemId;
        Amount = amount;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ItemId);
        serializer.SerializeValue(ref Amount);
    }
    
    public bool Equals(InventorySlot other)
    {
        return ItemId == other.ItemId && Amount == other.Amount;
    }
    
    public bool IsEmpty()
    {
        return ItemId == -1;
    }
    
    public void Clear()
    {
        ItemId = -1;
        Amount = 0;
    }
    
    public InventorySlot UpdateSlot(int itemId)
    {
        return new InventorySlot(itemId, Amount);
    }
}
