// Names the settlement's walkable heart: the open ground every other place must be reachable from.
//
// Generate looks for the heart by asking which group of its places can walk to one another is the biggest. That finds the streets of a
// settlement whose places are all out in the open, but a colony of sealed buildings hides most of its places indoors, and the biggest
// group is then a building's roof or its interior. A prop that stands on the open ground puts this on itself and Generate takes it as the
// heart, over the guess.
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class SettlementHeart : MonoBehaviour
    {
    }
}
