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

namespace ichortower.ECC;

/*
 * 
 * Event commands for manipulating Time Streams (or just Streams), parallel execution
 * queues that let you more easily schedule simultaneous actions.
 * 
 */
internal class Stream
{

    /*
     * ichortower.ECC_StreamStart <id>
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
            if (evt.eventCommands[i].StartsWith($"{Main.ModId}_StreamStart",
                    StringComparison.OrdinalIgnoreCase)) {
                ++depth;
            }
            if (evt.eventCommands[i].StartsWith($"{Main.ModId}_StreamEnd",
                    StringComparison.OrdinalIgnoreCase)) {
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

        if (!Streams.New(evt, streamId, commands.ToArray())) {
            context.LogError($"stream id '{streamId}' currently in use");
        }
        ++evt.CurrentCommand;
    }


    /*
     * ichortower.ECC_StreamEnd
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
     * ichortower.ECC_StreamPause <duration> [duration... ]
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
        evt.UpdateStream(context.Location, context.Time);
    }


    /*
     * ichortower.ECC_StreamLoop
     *
     * This command resets the current stream's command index to 0, causing it to restart
     * from the beginning. This creates a stream which loops continuously until stopped
     * with _StreamHalt (or until the event ends).
     */
    public static void command_StreamLoop(SEvent evt, string[] args, EventContext context)
    {
        int target = (evt.Equals(Game1.CurrentEvent) ? 3 : 0);
        evt.CurrentCommand = target;
    }


    /*
     * ichortower.ECC_StreamHalt <id> [id... ]
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
            if (!Streams.OpenStreams.TryGetValue(args[i], out SEvent target)) {
                context.LogErrorAndSkip($"requested unknown stream id '{args[i]}'");
                return;
            }
            target.CurrentCommand = target.eventCommands.Length;
            target.int_useMeForAnything = Streams.StreamEnded;
        }
        ++evt.CurrentCommand;
    }


    /*
     * ichortower.ECC_StreamRestart <id> [id... ]
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
            if (!Streams.OpenStreams.TryGetValue(args[i], out SEvent target)) {
                context.LogErrorAndSkip($"requested unknown stream id '{args[i]}'");
                return;
            }
            target.CurrentCommand = 0;
            target.int_useMeForAnything = 0;
            Streams.StartStreamRunner();
        }
        ++evt.CurrentCommand;
    }


    /*
     * ichortower.ECC_StreamAwait <id> [id... ]
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
            if (!Streams.OpenStreams.TryGetValue(args[i], out SEvent target)) {
                context.LogErrorAndSkip($"requested unknown stream id '{args[i]}'");
                return;
            }
            if (target.Equals(evt)) {
                context.LogErrorAndSkip($"stream '{args[i]}' cannot await itself");
                return;
            }
            if (target.int_useMeForAnything != Streams.StreamEnded) {
                return;
            }
        }
        ++evt.CurrentCommand;
    }

}


/*
 *
 * This class is where the implementation details live.
 * It is named "Streams" to make certain calls nicer to read.
 *
 */

internal class Streams
{
    internal const int StreamEnded = -484;

    internal static System.EventHandler<UpdateTickedEventArgs> streamRunner = null;

    internal static Dictionary<string, SEvent> OpenStreams = new();

    internal static bool CleanupQueued = false;

    internal static bool New(SEvent source, string streamId, string[] commands)
    {
        if (OpenStreams.ContainsKey(streamId) &&
                OpenStreams[streamId].int_useMeForAnything != StreamEnded) {
            return false;
        }
        SEvent stream = new();
        stream.id = $"{Main.ModId}_stream_{streamId}";
        stream.ReplaceAllCommands(commands);
        // actors and farmerActors should be ref copies in the new event
        stream.actors = source.actors;
        stream.farmerActors = source.farmerActors;
        // see Extensions.cs
        // actorPositionsAfterMove must be manually init or commands barf
        Extensions.EventAPAM.SetValue(stream, new Dictionary<string, Vector3>());

        OpenStreams[streamId] = stream;
        StartStreamRunner();
        if (!CleanupQueued) {
            source.onEventFinished += CleanUp;
            CleanupQueued = true;
        }
        return true;
    }

    internal static void StartStreamRunner()
    {
        if (streamRunner is not null) {
            return;
        }
        Log.Debug("Starting stream runner");
        streamRunner = StreamFunction;
        Main.Helper.Events.GameLoop.UpdateTicked += streamRunner;
    }

    internal static void StreamFunction(object sender, UpdateTickedEventArgs tickedArgs)
    {
        if (!ichortower.TowerCore.Game.IsActive()) {
            return;
        }
        if (Game1.eventOver || !Game1.eventUp) {
            StopStreamRunner();
            return;
        }
        bool alive = false;
        foreach (var kvp in OpenStreams) {
            SEvent e = kvp.Value;
            if (e.int_useMeForAnything == StreamEnded) {
                continue;
            }
            if (e.CurrentCommand >= e.eventCommands.Length) {
                e.int_useMeForAnything = StreamEnded;
                continue;
            }
            alive = true;

            bool simul = false;
            do {
                int prev = e.CurrentCommand;
                // see Extensions.cs
                e.UpdateStream(Game1.currentLocation, Game1.currentGameTime);
                if (prev != e.CurrentCommand) {
                    simul = e.simultaneousCommand;
                }
            } while(simul);
        }
        if (!alive) {
            StopStreamRunner();
        }
    }

    internal static void StopStreamRunner()
    {
        if (streamRunner is null) {
            return;
        }
        Log.Debug("Stopping stream runner");
        Main.Helper.Events.GameLoop.UpdateTicked -= streamRunner;
        streamRunner = null;
    }

    internal static void CleanUp()
    {
        Log.Debug("Running cleanup function");
        streamRunner = null;
        OpenStreams.Clear();
        CleanupQueued = false;
    }

    internal static bool HasControllerFor(Character actor)
    {
        if (Game1.CurrentEvent is null) {
            return false;
        }
        IEnumerable<SEvent> fabric = OpenStreams.Values.Concat(new[] {Game1.CurrentEvent});
        return fabric.Any((evt) => {
            return evt?.npcControllers?.Any(c => c.puppet.Equals(actor)) ?? false;
        });
    }

    internal static bool TryRemoveControllersFor(Character actor, RemoveTiming timing)
    {
        int total = 0;
        IEnumerable<SEvent> fabric = OpenStreams.Values.Concat(new[] {Game1.CurrentEvent});
        if (timing == RemoveTiming.Now) {
            foreach (SEvent evt in fabric) {
                total += (evt?.npcControllers?.RemoveAll(c => c.puppet.Equals(actor)) ?? 0);
            }
        }
        else if (timing == RemoveTiming.AfterThisLeg) {
            foreach (SEvent evt in fabric) {
                if (evt?.npcControllers is null) {
                    continue;
                }
                var active = evt.npcControllers.Where(c => c.puppet.Equals(actor));
                foreach (NPCController c in active) {
                    c.destroyAtNextCrossroad();
                    ++total;
                }
            }
        }
        return total > 0;
    }

    internal enum RemoveTiming {
        Now,
        AfterThisLeg,
    }

    internal static bool HasBasicMoveFor(string actorName)
    {
        if (Game1.CurrentEvent is null || actorName is null) {
            return false;
        }
        IEnumerable<SEvent> fabric = OpenStreams.Values.Concat(new[] {Game1.CurrentEvent});
        return fabric.Any((evt) => {
            var moves = (Dictionary<string, Vector3>)Extensions.EventAPAM.GetValue(evt);
            return moves.ContainsKey(actorName);
        });
    }

    internal static bool TryRemoveBasicMovesFor(string actorName)
    {
        if (Game1.CurrentEvent is null || actorName is null) {
            return false;
        }
        IEnumerable<SEvent> fabric = OpenStreams.Values.Concat(new[] {Game1.CurrentEvent});
        int total = 0;
        foreach (SEvent evt in fabric) {
            var moves = (Dictionary<string, Vector3>)Extensions.EventAPAM.GetValue(evt);
            if (moves.Remove(actorName)) {
                ++total;
            }
        }
        return total > 0;
    }

}
