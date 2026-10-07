// The emote catalog, read the way the game reads it.
//
// Every failure here is silent at runtime until someone picks the emote: a null or unbuilt action
// logs an error on the owner's machine and nothing moves, a duplicate chat word quietly replaces
// the first command of that name, and a chat line with a bad format placeholder throws on the
// server in the middle of running the command.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Presentation;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class EmoteCatalogAssetTests
    {
        private static EmoteCatalog LoadCatalog()
        {
            var catalog = Resources.Load<EmoteCatalog>(EmoteCatalog.ResourcePath);
            Assert.IsNotNull(catalog, $"No emote catalog at Resources/{EmoteCatalog.ResourcePath}");
            Assert.IsNotEmpty(catalog.Entries, "the emote catalog is empty — the wheel and the chat commands have nothing to offer");
            return catalog;
        }

        [Test]
        public void EveryEmotePlaysAnActionTheControllerWasBuiltWith()
        {
            EmoteCatalog catalog = LoadCatalog();
            var actions = Resources.Load<CharacterActionCatalog>(CharacterActionCatalog.ResourcePath);
            Assert.IsNotNull(actions, "the character action catalog is missing — run Rebuild Humanoid Controller");

            foreach (EmoteCatalog.Entry entry in catalog.Entries)
            {
                Assert.IsNotNull(entry.action, $"emote '{entry.word}' has no action");
                Assert.GreaterOrEqual(actions.IndexOf(entry.action), 0,
                                      $"emote '{entry.word}' plays '{entry.action.name}', which is not in the " +
                                      "action catalog — rebuild the humanoid controller");
            }
        }

        [Test]
        public void ChatWordsAreSingleUniqueWords()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (EmoteCatalog.Entry entry in LoadCatalog().Entries)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.word), "an emote has no chat word");
                Assert.AreEqual(-1, entry.word.IndexOfAny(new[] { ' ', '\t', ChatCommands.Prefix }),
                                $"'{entry.word}' must be one word without the slash — the chat splits commands on whitespace");
                Assert.IsTrue(seen.Add(entry.word), $"'{entry.word}' is listed twice; the second would replace the first's command");
            }
        }

        [Test]
        public void ChatLinesFormatWithOnlyThePlayersName()
        {
            foreach (EmoteCatalog.Entry entry in LoadCatalog().Entries)
            {
                if (string.IsNullOrEmpty(entry.line)) continue;
                Assert.DoesNotThrow(() => string.Format(entry.line, "Somebody"),
                                    $"emote '{entry.word}' has a chat line with a placeholder other than {{0}}");
            }
        }
    }
}
