using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkPlayerController : NetworkBehaviour
{

    //Example of a networkvariable.

    //NetworkVariable objects have to be declared in the field, or in OnNetworkSpawn(), due to the fact they utilize generics.
    //By default, clients do not have write access to network variables. (Server authoritative)
    //But we can define the write and read permission in the constructor.
    //NetworkVariables only accept value types as generic input. Refrence types such as strings or tables cannot be directly synchronized.
    private NetworkVariable<int> randomNumber = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    //OnNetworkSpawn() functions similarly to Start or Awake, but should be used when dealing with network instead of the aforementioned methods.
    public override void OnNetworkSpawn()
    {
        //Subscribes to the "OnValueChanged" event, which is triggered when the NetworkVariable is changed.
        randomNumber.OnValueChanged += (int previousValue, int newValue) =>
        {
            Debug.Log(OwnerClientId +"; RandomNumber: " + randomNumber.Value);
        };
    }

    //Variable used to determine how fast players move.
    [SerializeField]
    private float movementSpeed;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        //Here we check if the player is the owner of this object.
        if(!IsOwner)
        {
            return;
        }
        
        if(InputSystem.actions.FindAction("Interact").WasPressedThisFrame())
        {
            randomNumber.Value = Random.Range(0, 10);
            
            //This log is only for the client that pressed "Interact"
            Debug.Log(randomNumber.Value);

            //Runs the method defined below on the server.
            TestServerRpc(new ServerRpcParams());
        }

        //Only performs movement if player owns this object
        Vector2 wishDir = InputSystem.actions.FindAction("Move").ReadValue<Vector2>() * movementSpeed;
        transform.position += new Vector3(wishDir.x, 0, wishDir.y) * Time.deltaTime;
    }

    //Example of a server RPC method. Note that method name MUST end with "ServerRpc".
    //Server RPCs are run on the server. If a client calls this method, it is "redirected" to the host.
    //Useful when the clients want to inform or query the server about changes.
    //An example could be if a client equips an artifact.
    //Instead of synchronizing it as a variable, the client could tell the server about a new equip using RPC.
    //The server could then call client RPCs to inform all clients about the new equipped artifact.
    //RPCs can use parameters, but all parameters must be value types (no refrence types allowed (except string))

    //In this example, we use the parameter ServerRpcParams, which can tell us about who sent the message.
    [ServerRpc]
    private void TestServerRpc(ServerRpcParams serverRpcParams)
    {
        Debug.Log("TestServerRpc " + OwnerClientId + "; " + serverRpcParams.Receive.SenderClientId);
    }
}
