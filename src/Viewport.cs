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
     * Each of x and y can be an unadorned integer, in which case it represents
     * a tile delta (similar to `move`, except both axes can be nonzero at
     * once), or the letter 'a' plus an integer, in which case it is an
     * absolute tile coordinate.
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
        if (!TryGetTarget(args[1], out int xDest, out ViewportMoveType xType, out err) ||
                !TryGetTarget(args[2], out int yDest, out ViewportMoveType yType, out err) ||
                !ArgUtility.TryGetInt(args, 3, out int duration, out err, "int duration")) {
            context.LogErrorAndSkip(err);
            return;
        }
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

    private static bool TryGetTarget(string arg, out int target,
            out ViewportMoveType type, out string err)
    {
        target = 0;
        type = ViewportMoveType.None;
        if (arg.StartsWith("a", StringComparison.OrdinalIgnoreCase)) {
            if (!int.TryParse(arg.Substring(1), out int avalue)) {
                err = $"'{arg}': integer not found following 'a'";
                return false;
            }
            target = 64 * avalue + 32;
            type = ViewportMoveType.Absolute;
        }
        else {
            if (!int.TryParse(arg, out int dvalue)) {
                err = $"'{arg}' could not be converted to integer";
                return false;
            }
            target = 64 * dvalue;
            type = ViewportMoveType.Relative;
        }
        err = null;
        return true;
    }

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
        if (!ichortower.TowerCore.Game.IsActive()) {
            return;
        }
        if (Game1.eventOver || !Game1.eventUp || viewportQueue.Count == 0) {
            StopViewportWatcher();
            return;
        }
        int now = (int)Game1.currentGameTime.TotalGameTime.TotalMilliseconds;
        ViewportMove head = viewportQueue[0];
        if (head.StartMs == 0) {
            head.StartMs = now;
            head.StartX = Game1.viewport.X;
            head.EndX += head.TypeX switch {
                ViewportMoveType.Relative => head.StartX,
                ViewportMoveType.Absolute => -1 * Game1.viewport.Width/2,
                _ => 0,
            };
            head.StartY = Game1.viewport.Y;
            head.EndY += head.TypeY switch {
                ViewportMoveType.Relative => head.StartY,
                ViewportMoveType.Absolute => -1 * Game1.viewport.Height/2,
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
    public ViewportMoveType TypeX = ViewportMoveType.None;
    public ViewportMoveType TypeY = ViewportMoveType.None;
    public int Duration = 0;
    public int StartMs = 0;

    public ViewportMove(int endx, ViewportMoveType typex,
            int endy, ViewportMoveType typey, int duration)
    {
        EndX = endx;
        EndY = endy;
        TypeX = typex;
        TypeY = typey;
        Duration = duration;
    }
}

internal enum ViewportMoveType {
    None,
    Relative,
    Absolute,
}

