using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The emote wheel's geometry and paging as pure functions, so they can be reasoned about — and
    /// tested — without a canvas, a player or an input device.
    ///
    /// <para>
    /// Angles are measured in degrees clockwise from straight up, because that is how a wheel is
    /// read: wedge 0 is at twelve o'clock and the count goes round like a clock face.
    /// </para>
    /// </summary>
    public static class EmoteWheelLayout
    {
        /// <summary>One wheel's worth of emotes: which category it shows, and which entries.</summary>
        public readonly struct Page
        {
            /// <summary>The category every entry on this page shares. Empty for uncategorised entries.</summary>
            public readonly string Category;

            /// <summary>Zero-based part of the category this page is, when it spills over more than one.</summary>
            public readonly int Part;

            /// <summary>How many pages the category spans.</summary>
            public readonly int PartCount;

            /// <summary>Indexes into the catalog, in catalog order.</summary>
            public readonly IReadOnlyList<int> Entries;

            public Page(string category, int part, int partCount, IReadOnlyList<int> entries)
            {
                Category = category;
                Part = part;
                PartCount = partCount;
                Entries = entries;
            }
        }

        /// <summary>
        /// Splits a catalog into wheel pages: one run per category, in the order each category
        /// first appears, cut into pages of at most <paramref name="perPage"/>. Entries keep their
        /// catalog order within a category, so appending an emote never moves an existing one to a
        /// different page. A null category groups with the empty one.
        /// </summary>
        public static List<Page> Paginate(IReadOnlyList<string> categories, int perPage)
        {
            var pages = new List<Page>();
            if (categories == null || perPage <= 0) return pages;

            var order = new List<string>();
            var members = new Dictionary<string, List<int>>();
            for (int i = 0; i < categories.Count; i++)
            {
                string category = categories[i] ?? string.Empty;
                if (!members.TryGetValue(category, out List<int> list))
                {
                    list = new List<int>();
                    members.Add(category, list);
                    order.Add(category);
                }
                list.Add(i);
            }

            foreach (string category in order)
            {
                List<int> list = members[category];
                int partCount = (list.Count + perPage - 1) / perPage;
                for (int part = 0; part < partCount; part++)
                {
                    int start = part * perPage;
                    int count = Mathf.Min(perPage, list.Count - start);
                    pages.Add(new Page(category, part, partCount, list.GetRange(start, count)));
                }
            }

            return pages;
        }

        /// <summary>
        /// The wedge <paramref name="offset"/> points into, or -1 while it is inside
        /// <paramref name="deadZone"/> — which is how releasing in the middle cancels. Wedge 0 is
        /// centred on straight up; boundaries sit half a wedge either side of each centre.
        /// </summary>
        public static int WedgeAt(Vector2 offset, int wedges, float deadZone)
        {
            if (wedges <= 0) return -1;
            if (offset.sqrMagnitude <= deadZone * deadZone || offset == Vector2.zero) return -1;

            float step = 360f / wedges;
            float angle = Mathf.Repeat(Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg + step * 0.5f, 360f);
            return Mathf.Min(Mathf.FloorToInt(angle / step), wedges - 1);
        }

        /// <summary>The centre of <paramref name="wedge"/>, in degrees clockwise from up.</summary>
        public static float WedgeAngle(int wedge, int wedges) => wedges > 0 ? wedge * 360f / wedges : 0f;

        /// <summary>A unit vector (canvas space, y up) through the centre of <paramref name="wedge"/>.</summary>
        public static Vector2 WedgeDirection(int wedge, int wedges)
        {
            float radians = WedgeAngle(wedge, wedges) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        /// <summary>
        /// The wedge the <paramref name="item"/>-th of <paramref name="itemCount"/> entries sits in,
        /// spreading a short page evenly round the wheel instead of bunching it on one side: four
        /// emotes land on the four compass points, and the first always sits at the top. Distinct
        /// for every item as long as a page holds no more entries than the wheel has wedges.
        /// </summary>
        public static int SlotOf(int item, int itemCount, int wedges)
        {
            if (itemCount <= 0 || itemCount > wedges) return -1;
            return Mathf.FloorToInt(item * (float)wedges / itemCount + 0.5f);
        }

        /// <summary><paramref name="index"/> wrapped into [0, count): paging past either end comes round.</summary>
        public static int Wrap(int index, int count) => count <= 0 ? 0 : ((index % count) + count) % count;
    }
}
