using StardewValley;
using StardewValley.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Log = ichortower.TowerCore.Log;
using Main = ichortower.TowerCore.Main;
using SEvent = StardewValley.Event;

namespace ichortower.ECC;

internal class IfElse
{

    /*
     * ichortower.ECC_If <GSQ>
     *
     * Declare sets of event commands to execute conditionally, just like in a traditional
     * procedural programming language. If the GSQ evaluates to true, run the first block.
     * Any number of subsequent blocks can follow using ElseIf, which will evaluate their
     * conditions in turn until one is satisfied, and the rest will be ignored. Finally,
     * you can have exactly one (optional) Else block, which will run if none of the rest
     * were satisfied. Finish the entire thing with EndIf.
     *
     * So, for example:
     *   ichortower.ECC_If PLAYER_NPC_RELATIONSHIP Current Abigail married
     *   jump Lewis
     *   pause 500
     *   ichortower.ECC_ElseIf PLAYER_NPC_RELATIONSHIP Current Emily married
     *   emote Lewis 40
     *   ichortower.ECC_Else
     *   speak Lewis "I guess you didn't like either of them, huh?"
     *   ichortower.ECC_EndIf
     *
     * ... will cause Lewis to jump if the player is married to Abigail, do the "..."
     * emote if they are married to Emily, or say the line if neither one is true.
     *
     * This command is quote-aware, so quoting the GSQ is fine. But it will also accept
     * the query without quotes and treat it as one argument, which should save some
     * parsing/escaping headaches.
     */
    public static void command_If(SEvent evt, string[] args, EventContext context)
    {
        IfBlock state = new();
        int i = evt.CurrentCommand + 1;
        int depth = 1;
        bool matched = false;
        for (; i < evt.eventCommands.Length; ++i) {
            string c = evt.eventCommands[i];
            if (c.StartsWithIgnoreCase($"{Main.ModId}_If")) {
                ++depth;
            }
            else if (c.StartsWithIgnoreCase($"{Main.ModId}_EndIf")) {
                --depth;
                if (depth == 0) {
                    matched = true;
                    break;
                }
            }
        }
        if (!matched) {
            evt.LogErrorAndHalt("did not find a matching EndIf command");
            return;
        }
        int count = i - evt.CurrentCommand + 1;
        ReadOnlySpan<string> arr = new(evt.eventCommands, evt.CurrentCommand, count);
        if (!state.Parse(arr.ToArray(), out string err)) {
            context.LogError(err, willSkip: false);
            evt.CurrentCommand = i + 1;
            return;
        }

        // hotswap out the block
        List<string> temp = evt.eventCommands.ToList();
        temp.RemoveRange(evt.CurrentCommand, count);
        temp.InsertRange(evt.CurrentCommand, state.GetMatchingBody() ?? new string[] {});
        evt.eventCommands = temp.ToArray();
    }

    public static void command_ElseIf(SEvent evt, string[] args, EventContext context)
    {
        context.LogErrorAndSkip("this command was executed, which shouldn't" +
                " happen. Check your script for missing If");
    }

    public static void command_Else(SEvent evt, string[] args, EventContext context)
    {
        context.LogErrorAndSkip("this command was executed, which shouldn't" +
                " happen. Check your script for missing If");
    }

    public static void command_EndIf(SEvent evt, string[] args, EventContext context)
    {
        context.LogErrorAndSkip("this command was executed, which shouldn't" +
                " happen. Check your script for missing If");
    }
}

internal class IfBlock
{
    List<string> Conditions = new();
    List<List<string>> Bodies = new();

    internal bool Parse(string[] commands, out string error)
    {
        error = null;
        bool foundElse = false;
        int depth = 0;
        for (int i = 0; i < commands.Length && depth >= 0; ++i) {
            string c = commands[i];
            if (c.StartsWithIgnoreCase($"{Main.ModId}_If")) {
                ++depth;
                if (depth == 1) {
                    StartBody(c);
                }
                else {
                    AddCommand(c);
                }
            }
            else if (c.StartsWithIgnoreCase($"{Main.ModId}_EndIf")) {
                --depth;
                if (depth == 0) {
                    break;
                }
                else {
                    AddCommand(c);
                }
            }
            else if (c.StartsWithIgnoreCase($"{Main.ModId}_ElseIf")) {
                if (depth == 1) {
                    StartBody(c);
                    if (foundElse) {
                        Log.Warn("Found an ElseIf after an Else, which will never execute");
                    }
                }
                else {
                    AddCommand(c);
                }
            }
            else if (c.StartsWithIgnoreCase($"{Main.ModId}_Else")) {
                if (depth == 1) {
                    StartBody(c);
                    if (foundElse) {
                        Log.Warn("Found an Else after an Else, which will never execute");
                    }
                    else {
                        foundElse = true;
                    }
                }
                else {
                    AddCommand(c);
                }
            }
            else {
                AddCommand(c);
            }
        }
        if (depth != 0) {
            error = "Unbalanced blocks. Cannot proceed.";
            Conditions = new();
            Bodies = new();
            return false;
        }
        return true;
    }

    internal string GetQuery(string command)
    {
        int spaceloc = command.IndexOf(" ");
        if (spaceloc == -1) {
            return null;
        }
        string ret = command.Substring(spaceloc + 1);
        return ret.Trim(new Char [] {' ', '"'});
    }

    internal void StartBody(string command)
    {
        Conditions.Add(GetQuery(command) ?? "TRUE");
        Log.Debug($"new block with query '{Conditions[Conditions.Count-1]}'");
        Bodies.Add(new List<string>());
    }

    internal void AddCommand(string command)
    {
        Bodies[Bodies.Count - 1].Add(command);
    }

    internal string[] GetMatchingBody()
    {
        for (int i = 0; i < Conditions.Count; ++i) {
            Log.Debug($"Evaluating '{Conditions[i]}'");
            if (GameStateQuery.CheckConditions(Conditions[i])) {
                return Bodies[i].ToArray();
            }
        }
        return null;
    }
}
