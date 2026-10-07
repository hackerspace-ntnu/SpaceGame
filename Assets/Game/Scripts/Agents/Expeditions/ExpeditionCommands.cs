// The /exp chat commands: a developer's handle on settlement expeditions — list the settlements and bands, raise a
// band now, send it off, end its stages, run it days ahead, send it home, print its record (ExpeditionCommandParser.Usage).
//
// Chat commands because the chat already routes a client's line to the server and the answer back to the sender
// alone (ChatNetwork), so these work from a client with no UI of their own. The server decides: a handler runs
// where ChatCommands.Execute runs, which is the server or offline; Network.Decides refuses the one other path (a
// client whose chat object is not spawned yet handles lines locally). The gate is the HOST's developer mode, the
// setting that unlocks the item browser, because the host's world is the one changed.
//
// Answers run to many lines, and a chat notice is one sanitised line of at most ChatText.MaxCharacters, so each
// line but the last is sent to the sender here and the last is the command's own answer.
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpaceGame.Core;
using SpaceGame.Persistence;
using UnityEngine;

namespace SpaceGame.Agents.Expeditions
{
    public static class ExpeditionCommands
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            // Register replaces by name, so running this again after a domain reload leaves one entry.
            ChatCommands.Register("exp", "/exp <list|force|depart|advance|days|home|record>",
                                  "Developer: list, raise and push settlement expeditions; /exp alone explains each.",
                                  Run, "expedition");
        }

        private static string Run(ulong sender, string[] args)
        {
            List<string> lines = ExpeditionCommandText.ChatLines(Answer(args), ChatText.MaxCharacters);
            for (int i = 0; i < lines.Count - 1; i++) ChatNetwork.Notify(sender, lines[i]);
            return lines.Count > 0 ? lines[lines.Count - 1] : null;
        }

        private static string Answer(string[] args)
        {
            if (!GameSettings.DevMode) return "/exp needs developer mode on the host (pause menu, Developer tab).";
            if (!Network.Decides) return "/exp runs on the server only.";

            ExpeditionDirector director = ExpeditionDirector.Instance;
            if (director == null) return "This world has no expedition director.";

            if (!ExpeditionCommandParser.TryParse(args, out ExpeditionCommand command, out string problem)) return problem;

            switch (command.Verb)
            {
                case ExpeditionVerb.List: return ExpeditionCommandText.List(director.Settlements, director.Bands);
                case ExpeditionVerb.Force: return Force(director, command);
            }

            if (!ExpeditionCommandParser.TryResolve(command.Target, director.Bands, b => b.id, "band",
                                                     out ExpeditionRecord band, out problem))
                return problem;

            switch (command.Verb)
            {
                case ExpeditionVerb.Depart: return Depart(director, band);
                case ExpeditionVerb.Advance: return Advance(director, band, command.Count);
                case ExpeditionVerb.Days: return Days(director, band, command.Days);
                case ExpeditionVerb.Home: return Home(director, band);
                default: return Record(band);
            }
        }

        private static string Force(ExpeditionDirector director, ExpeditionCommand command)
        {
            if (!ExpeditionCommandParser.TryResolve(command.Target, director.Settlements, s => s.settlementId, "settlement",
                                                     out SettlementState state, out string problem))
                return problem;

            ExpeditionRecord band = director.ForceBand(state.settlementId, command.GoalId, out problem);
            if (band == null) return $"No band raised: {problem}.";

            return $"Raised {band.id}: {band.goalId}, {band.members.Length} members, {ExpeditionCommandText.Stage(band)}.\n" +
                   $"Members: {ExpeditionCommandText.Members(band)}";
        }

        private static string Advance(ExpeditionDirector director, ExpeditionRecord band, int count) =>
            director.AdvanceStage(band.id, count) ? Where(band) : NotOnTheRoad(band);

        private static string Days(ExpeditionDirector director, ExpeditionRecord band, float days)
        {
            if (director.SimulateDays(band.id, days)) return Where(band);
            if (band.phase != ExpeditionPhase.Out) return NotOnTheRoad(band);

            NpcGroup group = GroupOf(director, band);
            return group != null && group.Spawned
                ? $"{band.id} is spawned: a player is near it. Walk away from it, or use /exp advance."
                : $"{band.id} cannot run ahead: the world has no clock, no '{ExpeditionDirector.TemplateId}' template, " +
                  "or the band no group yet.";
        }

        private static string Depart(ExpeditionDirector director, ExpeditionRecord band) =>
            director.DepartNow(band.id) ? Where(band)
            : band.phase == ExpeditionPhase.Announced ? $"{band.id} could not leave: check the log."
            : $"{band.id} is {band.phase}; only an Announced band can leave early.";

        private static string Home(ExpeditionDirector director, ExpeditionRecord band)
        {
            switch (director.SendHome(band.id))
            {
                case ExpeditionDirector.HomeResult.NoReturnHome:
                    return $"{band.id} has no ReturnHome stage ahead.";
                case ExpeditionDirector.HomeResult.AtHandOffPoint:
                    return $"{band.id} is on ReturnHome at its hand-off point; it arrives on the director's next step.";
                case ExpeditionDirector.HomeResult.Walking:
                    return $"{band.id} is on ReturnHome. A player is near it, so it walks to its hand-off point from where it stands.";
                default:
                    return NotOnTheRoad(band);
            }
        }

        private static string Record(ExpeditionRecord band) =>
            band.id + ":\n" + JObject.FromObject(band, SaveSerializer.Serializer).ToString(Formatting.None);

        private static string Where(ExpeditionRecord band) => $"{band.id}: {band.phase}, {ExpeditionCommandText.Stage(band)}.";

        private static string NotOnTheRoad(ExpeditionRecord band) =>
            band.phase != ExpeditionPhase.Out
                ? $"{band.id} is {band.phase}, not on the road."
                : $"{band.id} has no group yet (it is adopted on the director's next step); try again.";

        private static NpcGroup GroupOf(ExpeditionDirector director, ExpeditionRecord band) =>
            director.TryGetComponent(out NpcWorldSim sim) ? sim.FindGroup(band.groupId) : null;
    }
}
