using Microsoft.Xna.Framework;
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
 * ... and some toys:
 *
 *   - screen shake
 * 
 */
internal class Viewport
{

    /*
     * ichortower.ECC_ViewportMove <x> <y> <time> [override] [wait]
     *
     * Each coordinate can be either relative or absolute:
     *   plain nonzero integer (e.g. '12', '38'): absolute tile coordinate
     *   + or - integer, or 0 (e.g. '+2', '-8'): relative tile distance
     *   'a' with integer (e.g. 'a54', 'a-12'): absolute tile coordinate, for absolutes <= 0
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
                evt.InsertNextCommand($"{Main.ModId}_ViewportAwait move");
            }
            else {
                context.LogError($"unknown argument '{args[i]}'", willSkip: false);
            }
        }
        if (!queueMode) {
            viewportMoveQueue.Clear();
        }
        viewportMoveQueue.Add(new ViewportMove(xDest, xType, yDest, yType, duration));
        StartViewportWatcher();
        ++evt.CurrentCommand;
    }


    /*
     * ichortower.ECC_ViewportHalt [move] [shake]
     *
     * Aborts viewport control by emptying one or both of the viewport queues. With no
     * arguments, this will empty both queues. Specify just one ('move' or 'shake', both
     * case-insensitive) to stop just that type and leave the other alone.
     */
    public static void command_ViewportHalt(SEvent evt, string[] args, EventContext context)
    {
        bool bothMode = true;
        bool moveMode = false;
        bool shakeMode = false;
        for (int i = 1; i < args.Length; ++i) {
            if (args[i].EqualsIgnoreCase("move")) {
                moveMode = true;
                bothMode = false;
            }
            else if (args[i].EqualsIgnoreCase("shake")) {
                shakeMode = true;
                bothMode = false;
            }
            else {
                context.LogError($"unknown argument '{args[i]}'", willSkip: false);
            }
        }
        if ((bothMode || moveMode)) {
            viewportMoveQueue.Clear();
        }
        if ((bothMode || shakeMode)) {
            viewportShakeQueue.Clear();
        }
        ++evt.CurrentCommand;
    }


    /*
     * ichortower.ECC_ViewportAwait [move] [shake]
     *
     * Wait for one or both viewport queues to finish. With no arguments, this will
     * block until both viewport queues (moves and shakes) are empty. Specify just
     * one type ('move' or 'shake', both case-insensitive) to wait for just that queue
     * and leave the other one to run. Specifying both is equivalent to the plain
     * no-arguments version, but is more explicit.
     */
    public static void command_ViewportAwait(SEvent evt, string[] args, EventContext context)
    {
        bool bothMode = true;
        bool moveMode = false;
        bool shakeMode = false;
        for (int i = 1; i < args.Length; ++i) {
            if (args[i].EqualsIgnoreCase("move")) {
                moveMode = true;
                bothMode = false;
            }
            else if (args[i].EqualsIgnoreCase("shake")) {
                shakeMode = true;
                bothMode = false;
            }
            else {
                context.LogError($"unknown argument '{args[i]}'", willSkip: false);
            }
        }
        if ((bothMode || moveMode) && viewportMoveQueue.Count > 0) {
            return;
        }
        if ((bothMode || shakeMode) && viewportShakeQueue.Count > 0) {
            return;
        }
        ++evt.CurrentCommand;
    }


    public static void command_ViewportShake(SEvent evt, string[] args, EventContext context)
    {
        bool queueMode = true;
        string err = "";
        if (!ArgUtility.TryGetInt(args, 1, out int intensity, out err) ||
                !ArgUtility.TryGetInt(args, 2, out int duration, out err)) {
            context.LogErrorAndSkip(err);
            return;
        }
        for (int i = 3; i < args.Length; ++i) {
            if (args[i].EqualsIgnoreCase("override")) {
                queueMode = false;
            }
            else if (args[i].EqualsIgnoreCase("wait")) {
                evt.InsertNextCommand($"{Main.ModId}_ViewportAwait shake");
            }
            else {
                context.LogError($"unknown argument '{args[i]}'", willSkip: false);
            }
        }
        if (!queueMode) {
            viewportShakeQueue.Clear();
        }
        viewportShakeQueue.Add(new ViewportShake(intensity, duration));
        StartViewportWatcher();
        ++evt.CurrentCommand;
    }


    /*
     * 
     * Implementation details for viewport queue. Move along, folks
     * 
     */

    private static System.EventHandler<UpdateTickedEventArgs> viewportWatcher = null;

    private static List<ViewportMove> viewportMoveQueue = new();
    private static List<ViewportShake> viewportShakeQueue = new();

    private static Point NullPoint = new(-1000, -1000);
    private static Point viewportShakePrev = NullPoint;

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
        if (Game1.eventOver || !Game1.eventUp) {
            StopViewportWatcher();
            return;
        }
        int moves = TryViewportMove();
        int shakes = TryViewportShake();
        // this is to fix an edge case when using ViewportHalt
        if (shakes == 0 && viewportShakePrev != NullPoint) {
            Game1.viewport.X -= viewportShakePrev.X;
            Game1.viewport.Y -= viewportShakePrev.Y;
            viewportShakePrev = NullPoint;
        }
        // FIXME raindrop position adjustment here
        if (moves + shakes == 0) {
            StopViewportWatcher();
        }
    }

    private static int TryViewportMove() {
        if (viewportMoveQueue.Count == 0) {
            return 0;
        }
        ViewportMove head = viewportMoveQueue[0];
        if (!ichortower.TowerCore.Game.IsActive()) {
            if (head.StartMs > 0) {
                head.StartMs += (int)Game1.currentGameTime.ElapsedGameTime.Milliseconds;
            }
            return viewportMoveQueue.Count;
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
            return viewportMoveQueue.Count;
        }
        if (now >= head.StartMs + head.Duration) {
            Log.Debug("Viewport move complete");
            Game1.viewport.X = head.EndX;
            Game1.viewport.Y = head.EndY;
            viewportMoveQueue.RemoveAt(0);
            return viewportMoveQueue.Count;
        }
        float t = (float)(now - head.StartMs) / (float)head.Duration;
        Game1.viewport.X = (int)Utility.Lerp((float)head.StartX, (float)head.EndX, t);
        Game1.viewport.Y = (int)Utility.Lerp((float)head.StartY, (float)head.EndY, t);
        return viewportMoveQueue.Count;
    }

    private static int TryViewportShake() {
        if (viewportShakeQueue.Count == 0) {
            return 0;
        }
        if (!ichortower.TowerCore.Game.IsActive()) {
            return viewportShakeQueue.Count;
        }
        ViewportShake head = viewportShakeQueue[0];
        Point thisTime = new() {
            X = Game1.random.Next(-1 * head.Intensity, head.Intensity + 1),
            Y = Game1.random.Next(-1 * head.Intensity, head.Intensity + 1),
        };
        // no need to subtract out previous move if viewport queue is actively setting x/y
        if (viewportMoveQueue.Count == 0 && viewportShakePrev != NullPoint) {
            Game1.viewport.X -= viewportShakePrev.X;
            Game1.viewport.Y -= viewportShakePrev.Y;
        }

        head.Duration -= (int)Game1.currentGameTime.ElapsedGameTime.Milliseconds;
        if (head.Duration <= 0) {
            viewportShakeQueue.RemoveAt(0);
            viewportShakePrev = NullPoint;
        }
        else {
            Game1.viewport.X += thisTime.X;
            Game1.viewport.Y += thisTime.Y;
            viewportShakePrev = thisTime;
        }

        return viewportShakeQueue.Count;
    }

    private static void StopViewportWatcher()
    {
        viewportMoveQueue.Clear();
        viewportShakeQueue.Clear();
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

internal class ViewportShake
{
    public int Intensity = 4;
    public int Duration = 0;

    public ViewportShake(int intensity, int duration)
    {
        Intensity = intensity;
        Duration = duration;
    }
}
