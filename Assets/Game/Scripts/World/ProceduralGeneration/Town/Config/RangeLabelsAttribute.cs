// Label the two halves of a Vector2 / Vector2Int that mean "from this to that".
//
// Unity draws every Vector2 as X and Y, which is right for a position and actively misleading for a
// range: a count of "X 1, Y 0" reads as "one of them, second box unused" and actually means "between
// none and one", so the town rolls a coin and sometimes places nothing. That exact mistake is what
// this exists to stop, and no tooltip fixes it because the numbers are the thing being misread.
//
// Deliberately scoped to the town generator rather than dropped into a shared Core folder. It is the
// first property attribute in this project; if a second system wants it, move it up then, with a
// second caller to say what the shared version should look like.
using UnityEngine;

namespace SpaceGame.World.Towns
{
    public class RangeLabelsAttribute : PropertyAttribute
    {
        public readonly string Low;
        public readonly string High;

        public RangeLabelsAttribute(string low, string high)
        {
            Low = low;
            High = high;
        }
    }
}
