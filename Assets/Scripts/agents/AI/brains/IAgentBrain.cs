// Brain interface for all agent decision modules.
// Implementations read AgentContext and output a MoveIntent each frame.
public interface IAgentBrain
{
    MoveIntent Tick(in AgentContext context, float deltaTime);
}
