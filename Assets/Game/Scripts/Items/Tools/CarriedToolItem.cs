using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// A tool, vessel or work implement that is carried to be seen: a hammer, a bucket, a basket,
    /// a handcart. It has no effect of its own.
    ///
    /// <para>
    /// What it adds to the item pipeline is <i>presentation</i> only, and all of it is data on the
    /// base class: <c>ItemGrip</c> says how it sits in a fist, <see cref="BeltMount"/> says how it
    /// hangs on a belt, and <c>useAction</c> is the body action the holder plays when it is used
    /// (a hammer swings, a shovel digs). Using it therefore does nothing on the server, which is
    /// correct — nothing in the world changes — and the action is played on every machine through
    /// the same <c>Present</c> path every other item uses.
    /// </para>
    /// </summary>
    public class CarriedToolItem : ToolItem
    {
    }
}
