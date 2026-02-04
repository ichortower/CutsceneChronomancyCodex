using StardewValley;
using StardewValley.Extensions;
using System;
using System.Collections.Generic;

using Log = ichortower.TowerCore.Log;
using Main = ichortower.TowerCore.Main;
using SEvent = StardewValley.Event;

namespace ichortower.ECC;

/*
 * This class is for replacement commands for misbehaving vanilla ones. When a new stream is
 * declared, this class is responsible for checking and replacing the problematic commands,
 * so it Just Works and users don't have to remember to do things differently.
 */
internal class StreamSafe
{

    public static void command_Emote(SEvent evt, string[] args, EventContext context)
    {
        // it might seem weird to check the actor arg first, before the status/end condition,
        // but the end condition requires us to ask the Character so we need to resolve that
        // every time

        if (!ArgUtility.TryGet(args, 1, out string actorName, out string err, allowBlank:true)) {
            context.LogErrorAndSkip(err);
            return;
        }
        Character who = null;
        if (evt.IsFarmerActorId(actorName, out int farmerNumber)) {
            who = evt.GetFarmerActor(farmerNumber);
        }
        else {
            who = evt.getActorByName(actorName, out bool isOptional);
            if (who is null && !isOptional) {
                context.LogErrorAndSkip($"no NPC found with name '{actorName}'");
                return;
            }
        }
        // don't log the error here. missing npc was already optional, and missing farmer is ignored
        if (who is null) {
            ++evt.CurrentCommand;
            return;
        }

        if (evt.IsStatus(StreamStatus.AwaitingEmote)) {
            if (!who.isEmoting) {
                evt.SetStatus(StreamStatus.Active);
                ++evt.CurrentCommand;
            }
            return;
        }

        // now we can check the rest of the arguments and set up the emote
        if (!ArgUtility.TryGetInt(args, 2, out int emoteId, out err)) {
            context.LogErrorAndSkip(err);
            return;
        }
        bool waitMode = false;
        for (int i = 3; i < args.Length; ++i) {
            if (args[i].EqualsIgnoreCase("wait")) {
                waitMode = true;
            }
            else {
                context.LogError($"unknown argument '{args[i]}'", willSkip: false);
            }
        }

        // passing false here bypasses the hardcoded event command increment when emote terminates
        bool nextEventCommand = false;
        who.doEmote(emoteId, nextEventCommand);

        if (waitMode) {
            evt.SetStatus(StreamStatus.AwaitingEmote);
        }
        else {
            ++evt.CurrentCommand;
        }
    }


    public static void command_FaceDirection(SEvent evt, string[] args, EventContext context)
    {
        // in this one, we can check the end condition first since we don't need the args
        if (evt.IsStatus(StreamStatus.AwaitingDelay)) {
            if (evt.TickDownDelayTimer(Game1.currentGameTime)) {
                evt.SetStatus(StreamStatus.Active);
                ++evt.CurrentCommand;
            }
            return;
        }

        if (!ArgUtility.TryGet(args, 1, out string actorName, out string err, allowBlank:true) ||
                !ArgUtility.TryGetDirection(args, 2, out int direction, out err)) {
            context.LogErrorAndSkip(err);
            return;
        }
        // the optional arg can be an integer (how long to delay) or the word "delay" to use
        // the default 500 ms. no arg, no delay (like but not exactly like vanilla's "true")
        int delayTime = 0;
        for (int i = 3; i < args.Length; ++i) {
            if (int.TryParse(args[i], out delayTime)) {
                break;
            }
            else if (args[i].EqualsIgnoreCase("delay")) {
                delayTime = 500;
                break;
            }
            else {
                context.LogError($"unknown argument '{args[i]}'", willSkip: false);
            }
        }

        if (evt.IsFarmerActorId(actorName, out int farmerNumber)) {
            Farmer f = evt.GetFarmerActor(farmerNumber);
            if (f is not null) {
                f.FarmerSprite.StopAnimation();
                f.completelyStopAnimatingOrDoingAction();
                f.faceDirection(direction);
            }
        }
        else if (actorName.Contains("spouse")) {
            if (!Game1.player.hasRoommate()) {
                NPC who = evt.getActorByName(Game1.player.spouse);
                who?.faceDirection(direction);
            }
        }
        else {
            NPC who = evt.getActorByName(actorName, out bool isOptional);
            if (who is null && !isOptional) {
                context.LogErrorAndSkip($"no NPC found with name '{actorName}'");
                return;
            }
            who.faceDirection(direction);
        }

        if (delayTime > 0) {
            evt.SetDelayTimer(delayTime);
            evt.SetStatus(StreamStatus.AwaitingDelay);
        }
        else {
            ++evt.CurrentCommand;
        }
    }

    public static void command_Pause(SEvent evt, string[] args, EventContext context)
    {
        if (evt.IsStatus(StreamStatus.AwaitingDelay)) {
            if (evt.TickDownDelayTimer(Game1.currentGameTime)) {
                evt.SetStatus(StreamStatus.Active);
                ++evt.CurrentCommand;
            }
            return;
        }

        List<int> times = new();
        for (int i = 1; i < Math.Max(2, args.Length); ++i) {
            if (!ArgUtility.TryGetInt(args, i, out int millis, out string error)) {
                context.LogErrorAndSkip(error);
                return;
            }
            times.Add(millis);
        }
        int duration = (times.Count > 1 ? Game1.random.ChooseFrom(times) : times[0]);
        evt.SetDelayTimer(duration);
        evt.SetStatus(StreamStatus.AwaitingDelay);
    }

    public static void command_Speak(SEvent evt, string[] args, EventContext context)
    {
        ++evt.CurrentCommand;
    }
}
