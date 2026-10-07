using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The profiler-marker convention, so a capture can be searched for <c>SpaceGame.</c> and every
    /// marker found is ours and means one thing. <see cref="ProfilerMarker"/> has no name getter, so
    /// each marker keeps its name in a <c>const string</c> beside it, named <c>&lt;Field&gt;Name</c>:
    /// <c>UpdateMarker</c> reads <c>UpdateMarkerName</c>.
    /// </summary>
    public class ProfilerMarkerNamingTests
    {
        private const BindingFlags Statics =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private const string NameSuffix = "Name";

        // Assembly-CSharp plus every SpaceGame.* module assembly (SpaceGame.Diagnostics holds Fault).
        private static IEnumerable<Type> GameTypes() =>
            AppDomain.CurrentDomain.GetAssemblies()
                     .Where(a => a.GetName().Name == "Assembly-CSharp" || a.GetName().Name.StartsWith("SpaceGame."))
                     .SelectMany(a => a.GetTypes());

        private static List<(Type Owner, string Name)> MarkerNames() =>
            GameTypes().SelectMany(t => t.GetFields(Statics)
                                         .Where(f => f.IsLiteral && f.FieldType == typeof(string) &&
                                                     f.Name.EndsWith("Marker" + NameSuffix))
                                         .Select(f => (t, (string)f.GetRawConstantValue())))
                       .ToList();

        [Test]
        public void EveryMarkerIsNamespacedAndUnique()
        {
            List<(Type Owner, string Name)> markers = MarkerNames();
            Assert.IsNotEmpty(markers, "no profiler markers found");

            foreach ((Type owner, string name) in markers)
                StringAssert.StartsWith("SpaceGame.", name, $"{owner.FullName} names a marker outside the SpaceGame. prefix");

            string[] duplicates = markers.GroupBy(m => m.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            CollectionAssert.IsEmpty(duplicates, "two markers share a name, so a capture cannot tell them apart");
        }

        [Test]
        public void EveryMarkerFieldHasItsNameConstBesideIt()
        {
            foreach (Type type in GameTypes())
            foreach (FieldInfo marker in type.GetFields(Statics).Where(f => f.FieldType == typeof(ProfilerMarker)))
            {
                FieldInfo name = type.GetField(marker.Name + NameSuffix, Statics);
                Assert.IsTrue(name != null && name.IsLiteral,
                              $"{type.FullName}.{marker.Name} has no const {marker.Name + NameSuffix}");
            }
        }

        [Test]
        public void TheAgentTickAndRouteMarkersExist()
        {
            HashSet<string> names = new(MarkerNames().Select(m => m.Name));
            foreach (string expected in new[]
                     {
                         "SpaceGame.Agent.Update", "SpaceGame.Agent.Modules", "SpaceGame.Agent.Facing",
                         "SpaceGame.Fault.Run", "SpaceGame.NavPath.Repath", "SpaceGame.Formation.Tick",
                         "SpaceGame.NpcWorldSim.Spawn", "SpaceGame.NpcTask.Resolve",
                     })
                Assert.IsTrue(names.Contains(expected), $"missing marker {expected}");
        }
    }
}
