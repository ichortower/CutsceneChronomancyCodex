using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Extensions;
using System;
using System.Collections.Generic;

using Log = ichortower.TowerCore.Log;
using Main = ichortower.TowerCore.Main;
using SEvent = StardewValley.Event;

namespace ichortower.ECC;

/*
 * 
 * Event commands for manipulating the viewport. The goal is to be more powerful and easier to
 * grok than the vanilla viewport command(s):
 * 
 *   - express commands in useful units (tile coordinates) instead of pixels per frame
 *   - queue movements to enable complex behavior
 *   - wait for, stop, or preempt the queue whenever needed
 * 
 */
internal class Viewport
{

    /*
     * ichortower.ECC_ViewportMove <x> <y> <time> [override] [wait]
     *
     * Each coordinate can be either relative or absolute:
     *   plain integer (e.g. '12', '38'): absolute tile coordinate
     *   + or - integer (e.g. '+2', '-8'): relative tile distance
     *   'a' with integer (e.g. 'a54', 'a-12'): absolute tile coordinate, for negative absolutes
     *
     * time is in milliseconds and specifies how long the move should take.
     *
     * By default, the move is queued. If the optional argument 'override' is
     * found, empty the queue before starting this one.
     *
     * If the optional argument 'wait' is found, wait for the viewport queue to
     * finish and become empty before continuing the event.
     */
    public static void command_ViewportMove(SEvent evt, string[] args, EventContext context)
    {
        bool queueMode = true;
        string err = "";
        if (!Coords.TryGetTarget(args[1], out int xDest, out CoordType xType, out err) ||
                !Coords.TryGetTarget(args[2], out int yDest, out CoordType yType, out err) ||
                !ArgUtility.TryGetInt(args, 3, out int duration, out err, "int duration")) {
            context.LogErrorAndSkip(err);
            return;
        }
        xDest = xDest * 64 + (xType == CoordType.Absolute ? 32 : 0);
        yDest = yDest * 64 + (yType == CoordType.Absolute ? 32 : 0);
        for (int i = 4; i < args.Length; ++i) {
            if (args[i].EqualsIgnoreCase("override")) {
                queueMode = false;
            }
            else if (args[i].EqualsIgnoreCase("wait")) {
                evt.InsertNextCommand($"{Main.ModId}_ViewportAwait");
            }
            else {
                context.LogError($"unknown argument '{args[i]}'",
                        willSkip: false);
            }
        }
        if (!queueMode) {
            // this also clears the queue
            StopViewportWatcher();
        }
        viewportQueue.Add(new ViewportMove(xDest, xType, yDest, yType, duration));
        StartViewportWatcher();
        ++evt.CurrentCommand;
    }


    /*
     * ichortower.ECC_ViewportHalt
     *
     * Aborts any ongoing viewport moves (those started by _ViewportMove, not the vanilla
     * viewport command) and empties the queue.
     */
    public static void command_ViewportHalt(SEvent evt, string[] args, EventContext context)
    {
        StopViewportWatcher();
        ++evt.CurrentCommand;
    }


    /*
     * ichortower.ECC_ViewportAwait
     *
     * Wait for all queued viewport moves (via _ViewportMove) to finish.
     */
    public static void command_ViewportAwait(SEvent evt, string[] args, EventContext context)
    {
        if (viewportQueue.Count == 0) {
            ++evt.CurrentCommand;
        }
    }


    /*
     * 
     * Implementation details for viewport queue. Move along, folks
     * 
     */

    private static System.EventHandler<UpdateTickedEventArgs> viewportWatcher = null;

    private static List<ViewportMove> viewportQueue = new();

    private static void StartViewportWatcher()
    {
        if (viewportWatcher != null) {
            return;
        }
        Log.Debug("Starting viewport watcher");
        viewportWatcher = ViewportFunction;
        ichortower.TowerCore.Main.Helper.Events.GameLoop.UpdateTicked += viewportWatcher;
    }

    private static void ViewportFunction(object sender, UpdateTickedEventArgs e) {
        if (Game1.eventOver || !Game1.eventUp || viewportQueue.Count == 0) {
            StopViewportWatcher();
            return;
        }
        ViewportMove head = viewportQueue[0];
        if (!ichortower.TowerCore.Game.IsActive()) {
            if (head.StartMs > 0) {
                head.StartMs += (int)Game1.currentGameTime.ElapsedGameTime.Milliseconds;
            }
            return;
        }
        int now = (int)Game1.currentGameTime.TotalGameTime.TotalMilliseconds;
        if (head.StartMs == 0) {
            head.StartMs = now;
            head.StartX = Game1.viewport.X;
            head.EndX += head.TypeX switch {
                CoordType.Relative => head.StartX,
                CoordType.Absolute => -1 * Game1.viewport.Width/2,
                _ => 0,
            };
            head.StartY = Game1.viewport.Y;
            head.EndY += head.TypeY switch {
                CoordType.Relative => head.StartY,
                CoordType.Absolute => -1 * Game1.viewport.Height/2,
                _ => 0,
            };
            return;
        }
        if (now >= head.StartMs + head.Duration) {
            Log.Debug("Viewport move complete");
            Game1.viewport.X = head.EndX;
            Game1.viewport.Y = head.EndY;
            viewportQueue.RemoveAt(0);
            return;
        }
        // FIXME also do the raindrop position adjustment
        float t = (float)(now - head.StartMs) / (float)head.Duration;
        Game1.viewport.X = (int)Utility.Lerp((float)head.StartX, (float)head.EndX, t);
        Game1.viewport.Y = (int)Utility.Lerp((float)head.StartY, (float)head.EndY, t);
    }

    private static void StopViewportWatcher()
    {
        viewportQueue.Clear();
        if (viewportWatcher is null) {
            return;
        }
        Log.Debug("Stopping viewport watcher");
        ichortower.TowerCore.Main.Helper.Events.GameLoop.UpdateTicked -= viewportWatcher;
        viewportWatcher = null;
    }

}

internal class ViewportMove
{
    public int StartX = 0;
    public int StartY = 0;
    public int EndX = 0;
    public int EndY = 0;
    public CoordType TypeX = CoordType.None;
    public CoordType TypeY = CoordType.None;
    public int Duration = 0;
    public int StartMs = 0;

    public ViewportMove(int endx, CoordType typex, int endy, CoordType typey, int duration)
    {
        EndX = endx;
        EndY = endy;
        TypeX = typex;
        TypeY = typey;
        Duration = duration;
    }
}

