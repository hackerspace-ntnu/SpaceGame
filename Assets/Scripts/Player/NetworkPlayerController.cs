using Unity.Netcode;
using UnityEngine;

public class NetworkPlayerController : NetworkBehaviour
{
    [SerializeField] private PlayerController baseController;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;

        baseController.EnablePlayer();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();

        if (!IsOwner) return;

        baseController.DisablePlayer();
    }
}
