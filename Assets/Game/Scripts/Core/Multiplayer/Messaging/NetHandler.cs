namespace SpaceGame.Core
{
    /// <summary>Receives a networked message. <paramref name="sender"/> is the client that sent it.</summary>
    public delegate void NetHandler(in NetArg arg, ulong sender);
}
