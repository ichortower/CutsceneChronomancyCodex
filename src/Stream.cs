using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Extensions;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

using Log = ichortower.TowerCore.Log;
using Main = ichortower.TowerCore.Main;
using SEvent = StardewValley.Event;

namespace ichortower.CCC;

/*
 * 
 * Event commands for manipulating Time Streams (or just Streams), parallel execution
 * queues that let you more easily schedule simultaneous actions.
 * 
 */
internal class Stream
{

    /*
     * ichortower.CCC_StreamStart <id>
     *
     * Starts a stream, which consists of all commands following this one and until a matching
     * _StreamEnd command. A stream will start executing immediately, disregarding the main
     * command loop (and any other streams) and proceeding at its own pace until it runs out
     * of commands.
     *
     * There are no guardrails on the various event commands which manipulate the shared world
     * state: NPCs, dialogue, viewport, etc. It is the author's responsibility to avoid modifying
     * such things from multiple contexts at once.
     */
    public static void command_StreamStart(SEvent evt, string[] args, EventContext context)
    {
        // first, snarf the command list. then we avoid executing if the start is malformed
        bool matched = false;
        int depth = 1;
        int i = evt.CurrentCommand + 1;
        List<string> commands = new();
        for (; i < evt.eventCommands.Length; ++i) {
            if (evt.eventCommands[i].StartsWith($"{Main.ModId}_StreamStart")) {
                ++depth;
            }
            if (evt.eventCommands[i].StartsWith($"{Main.ModId}_StreamEnd")) {
                --depth;
                if (depth <= 0) {
                    matched = true;
                    break;
                }
            }
            commands.Add(evt.eventCommands[i]);
        }
        evt.CurrentCommand = i;
        if (!matched) {
            context.LogErrorAndSkip("did not find a matching _StreamEnd command");
            return;
        }
        if (!ArgUtility.TryGet(args, 1, out string streamId, out string error, allowBlank:false, "string id")) {
            context.LogErrorAndSkip(error);
            return;
        }
        // TODO check for collisions?
        SEvent stream = new();
        stream.id = $"{Main.ModId}_stream_{streamId}";
        // necessary to allow Update() to work without trying to Initialize()
        stream.eventSwitched = true;
        stream.ReplaceAllCommands(commands.ToArray());
        stream.actors = evt.actors;
        stream.farmerActors = evt.farmerActors;
        // see Extensions.cs
        Extensions.EventAPAM.SetValue(stream, new Dictionary<string, Vector3>());
        OpenStreams[streamId] = stream;
        StartStreamRunner();
        ++evt.CurrentCommand;
    }


    /*
     * ichortower.CCC_StreamEnd
     *
     * This command ends the declaration of a stream's command list. There should be one of
     * these for each _StreamStart.
     *
     * Executing this command is an error: in order to check as early as possible that the
     * script has a balanced number of stream commands, _StreamStart consumes this command
     * during the scan ahead.
     */
    public static void command_StreamEnd(SEvent evt, string[] args, EventContext context)
    {
        context.LogErrorAndSkip("this command was executed, which shouldn't" +
                " happen. Check your script and make sure every stream is ended once.");
    }


    /*
     * ichortower.CCC_StreamPause <duration> [duration... ]
     *
     * Pauses execution of the current stream (works on the "main" script as well, since
     * streams are no different technically). Accepts any number of integer arguments
     * representing different pause durations (in milliseconds), and will choose one at
     * random from those provided.
     *
     * This works by picking a random value, then replacing itself with a
     * precisePause command using the chosen value.
     */
    public static void command_StreamPause(Event evt, string[] args, EventContext context)
    {
        List<int> times = new();
        for (int i = 1; i < Math.Max(2, args.Length); ++i) {
            if (!ArgUtility.TryGetInt(args, i, out int millis, out string error)) {
                context.LogErrorAndSkip(error);
                return;
            }
            times.Add(millis);
        }
        int duration = (times.Count > 1 ? Game1.random.ChooseFrom(times) : times[0]);
        evt.ReplaceCurrentCommand($"precisePause {duration}");
        // update immediately so we don't waste a tick before starting the timer
        evt.Update(context.Location, context.Time);
    }


    /*
     * ichortower.CCC_StreamLoop
     *
     * This command resets the current stream's command index to 0, causing it to restart
     * from the beginning. This creates a stream which loops continuously until stopped
     * with _StreamHalt (or until the event ends).
     */
    public static void command_StreamLoop(SEvent evt, string[] args, EventContext context)
    {
        evt.CurrentCommand = 0;
    }


    /*
     * ichortower.CCC_StreamHalt <id> [id... ]
     *
     * This command terminates all specified streams by setting their command indexes beyond
     * the ends of their scripts and setting them as having ended, so the stream runner will
     * no longer execute them.
     */
    public static void command_StreamHalt(SEvent evt, string[] args, EventContext context)
    {
        if (args.Length < 2) {
            context.LogErrorAndSkip($"requires 1 or more stream ids");
            return;
        }
        for (int i = 1; i < args.Length; ++i) {
            if (!OpenStreams.TryGetValue(args[i], out SEvent target)) {
                context.LogErrorAndSkip($"requested unknown stream id '{args[i]}'");
                return;
            }
            target.CurrentCommand = target.eventCommands.Length;
            target.int_useMeForAnything = StreamEnded;
        }
        ++evt.CurrentCommand;
    }


    /*
     * ichortower.CCC_StreamRestart <id> [id... ]
     *
     * This command tells all specified streams to start over by setting their command indexes
     * to 0 and unsetting the ended status.
     */
    public static void command_StreamRestart(SEvent evt, string[] args, EventContext context)
    {
        if (args.Length < 2) {
            context.LogErrorAndSkip($"requires 1 or more stream ids");
            return;
        }
        for (int i = 1; i < args.Length; ++i) {
            if (!OpenStreams.TryGetValue(args[i], out SEvent target)) {
                context.LogErrorAndSkip($"requested unknown stream id '{args[i]}'");
                return;
            }
            target.CurrentCommand = 0;
            target.int_useMeForAnything = 0;
        }
        ++evt.CurrentCommand;
    }


    /*
     * ichortower.CCC_StreamAwait <id> [id... ]
     *
     * This command blocks until all specified streams have finished executing. Please take
     * care not to await a stream which is looping, as this will never complete unless some
     * other stream Halts it.
     */
    public static void command_StreamAwait(SEvent evt, string[] args, EventContext context)
    {
        if (args.Length < 2) {
            context.LogErrorAndSkip($"requires 1 or more stream ids");
            return;
        }
        for (int i = 1; i < args.Length; ++i) {
            if (!OpenStreams.TryGetValue(args[i], out SEvent target)) {
                context.LogErrorAndSkip($"requested unknown stream id '{args[i]}'");
                return;
            }
            if (target.int_useMeForAnything != StreamEnded) {
                return;
            }
        }
        ++evt.CurrentCommand;
    }


    /*
    internal static bool HasControllerFor(Character actor)
    {
        return OpenStreams.Values.Any((evt) => {
            return evt.npcControllers?.Any(c => c.puppet.Equals(actor)) ?? false;
        });
    }

    internal static bool HasBasicMoveFor(string actorName)
    {
        return OpenStreams.Values.Any((evt) => {
            var moves = (Dictionary<string, Vector3>) Extensions.EventAPAM.GetValue(evt);
            return moves.ContainsKey(actorName);
        });
    }
    */


    /*
     * 
     * Implementation details
     * 
     */

    private const int StreamEnded = -484;

    private static System.EventHandler<UpdateTickedEventArgs> streamRunner = null;

    private static Dictionary<string, SEvent> OpenStreams = new();

    private static void StartStreamRunner()
    {
        if (streamRunner is not null) {
            return;
        }
        Log.Debug("Starting stream runner");
        streamRunner = StreamFunction;
        Main.Helper.Events.GameLoop.UpdateTicked += streamRunner;
    }

    private static void StreamFunction(object sender, UpdateTickedEventArgs tickedArgs) {
        if (Game1.eventOver || !Game1.eventUp) {
            StopStreamRunner();
            return;
        }
        foreach (var kvp in OpenStreams) {
            SEvent e = kvp.Value;
            if (e.int_useMeForAnything == StreamEnded) {
                continue;
            }
            if (e.CurrentCommand >= e.eventCommands.Length) {
                e.int_useMeForAnything = StreamEnded;
                continue;
            }

            bool simul = false;
            do {
                int prev = e.CurrentCommand;
                e.Update(Game1.currentLocation, Game1.currentGameTime);
                if (prev != e.CurrentCommand) {
                    simul = e.simultaneousCommand;
                }
            } while(simul);
        }
    }

    private static void StopStreamRunner()
    {
        if (streamRunner is null) {
            return;
        }
        Log.Debug("Stopping stream runner");
        Main.Helper.Events.GameLoop.UpdateTicked -= streamRunner;
        streamRunner = null;
    }

}
