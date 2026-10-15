// The station contract, read off the data: every kind of spot is in the table, every station that holds a cue has a loop its
// body can play and a worker who carries the tool the clips are made for, and the document's table is the one the data makes.
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpaceGame.Agents.Residents.EditorTools;
using SpaceGame.World;
using UnityEditor;

namespace SpaceGame.Agents.Residents.Tests
{
    public class StationTableTests
    {
        [Test]
        public void EveryKindOfSpotAppearsInTheTable()
        {
            var rows = StationTable.Stations();
            foreach (string guid in AssetDatabase.FindAssets("t:SpotUse", new[] { "Assets/Game/ScriptableObjects/Settlements/Spots" }))
            {
                var use = AssetDatabase.LoadAssetAtPath<SpotUse>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.IsTrue(rows.Any(r => r.Use == use), $"the spot use '{use.name}' has no row in the station table");
            }
        }

        [Test]
        public void EveryStationHasAClipThatFitsAndEveryWorkerTheToolItsClipsAreMadeFor()
        {
            var problems = StationTable.Stations()
                .Where(s => s.Status.StartsWith("MISSING") || s.Status.StartsWith("TOOL"))
                .Select(s => $"{s.Use.name} ({(s.Cue != null ? s.Cue.name : "-")}): {s.Status}").ToList();
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void TheDocumentedTableIsTheOneTheDataMakes()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), StationTable.DocPath);
            string text = File.ReadAllText(path);
            Assert.AreEqual(StationTable.Replace(text, StationTable.Markdown(StationTable.Stations())), text,
                            $"{StationTable.DocPath} is stale: run Tools > SpaceGame > Residents > Write Station Table");
        }
    }
}
