// Makes an entity mountable. Wraps MountController and exposes mount state to other modules.
// Add this alongside MountController. SteerModule requires MountModule to function.
using UnityEngine;

[RequireComponent(typeof(MountController))]
public class MountModule : BehaviourModuleBase
{
    [SerializeField] private MountController mountController;

    public bool IsMounted => mountController != null && mountController.IsMounted;
    public MountController MountController => mountController;

    private void Awake()
    {
        if (!mountController)
            mountController = GetComponent<MountController>();
    }

    private void Reset() => SetPriorityDefault(ModulePriority.Fallback);

    public override string ModuleDescription =>
        "Marks this entity as mountable. Wraps MountController and exposes mount state.\n\n" +
        "• Required by SteerModule\n" +
        "• Does not produce movement on its own — add SteerModule for rider input\n" +
        "• Pair with MountSuppressorModule to silence other AI modules while mounted";

    // MountModule itself never claims movement — it is purely a state provider.
    public override MoveIntent? Tick(in AgentContext context, float deltaTime) => null;
}