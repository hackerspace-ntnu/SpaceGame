public interface IAgentBrain
{
    MoveIntent Tick(in AgentContext context, float deltaTime);
}
