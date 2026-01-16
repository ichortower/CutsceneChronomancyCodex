using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Extensions;
using StardewModdingAPI.Events;
using System.Collections.Generic;

using Log = ichortower.TowerCore.Log;
using Main = ichortower.TowerCore.Main;
using SEvent = StardewValley.Event;

namespace ichortower.ECC;

/*
 *
 * Event commands for adjusting the ambient light of the event map over time.
 * Vanilla has `ambientLight` but it's merely instant, so this lets you do
 * gradual transitions (e.g. for a sunset effect).
 *
 */

internal class AmbientLight
{

    public static void command_AmbientLightShift(Event evt, string[] args, EventContext context)
    {
        bool queueMode = true;
        string error;
        if (!ArgUtility.TryGetInt(args, 1, out int red, out error, "int red") ||
                !ArgUtility.TryGetInt(args, 2, out int green, out error, "int green") ||
                !ArgUtility.TryGetInt(args, 3, out int blue, out error, "int blue") ||
                !ArgUtility.TryGetInt(args, 4, out int duration, out error, "int duration")) {
            context.LogErrorAndSkip(error);
            return;
        }
        for (int i = 5; i < args.Length; ++i) {
            if (args[i].EqualsIgnoreCase("override")) {
                queueMode = false;
            }
            else if (args[i].EqualsIgnoreCase("wait")) {
                evt.InsertNextCommand($"{Main.ModId}_AmbientLightAwait");
            }
            else {
                context.LogError($"unknown argument '{args[i]}'",
                        willSkip: false);
            }
        }
        if (!queueMode) {
            // this also clears the queue
            StopAmbientLightWatcher();
        }
        ambientLightQueue.Add(new AmbientLightShift(new Color(red, green, blue), duration));
        StartAmbientLightWatcher();
        ++evt.CurrentCommand;
    }

    public static void command_AmbientLightAwait(Event evt, string[] args, EventContext context)
    {
        if (ambientLightQueue.Count == 0) {
            ++evt.CurrentCommand;
        }
    }

    public static void command_AmbientLightHalt(Event evt, string[] args, EventContext context)
    {
        StopAmbientLightWatcher();
        ++evt.CurrentCommand;
    }


    /*
     * Implementation details
     */

    private static List<AmbientLightShift> ambientLightQueue = new();

    private static System.EventHandler<UpdateTickedEventArgs> ambientLightWatcher = null;

    private static void StartAmbientLightWatcher()
    {
        if (ambientLightWatcher != null) {
            return;
        }
        Log.Debug("Starting ambientLight watcher");
        ambientLightWatcher = AmbientLightFunction;
        ichortower.TowerCore.Main.Helper.Events.GameLoop.UpdateTicked += ambientLightWatcher;
    }

    private static void AmbientLightFunction(object sender, UpdateTickedEventArgs e)
    {
        if (!ichortower.TowerCore.Game.IsActive()) {
            return;
        }
        if (Game1.eventOver || !Game1.eventUp || ambientLightQueue.Count == 0) {
            StopAmbientLightWatcher();
            return;
        }
        int now = (int)Game1.currentGameTime.TotalGameTime.TotalMilliseconds;
        AmbientLightShift head = ambientLightQueue[0];
        if (head.StartMs == 0) {
            head.StartMs = now;
            head.Start = Game1.ambientLight;
            Log.Debug($"starting shift {head.Start} -> {head.End}");
            return;
        }
        if (now >= head.StartMs + head.Duration) {
            Log.Debug("ambientLight shift complete");
            Game1.ambientLight = head.End;
            ambientLightQueue.RemoveAt(0);
            return;
        }
        float t = (float)(now - head.StartMs) / (float)head.Duration;
        int r = (int)Utility.Lerp((float)head.Start.R, (float)head.End.R, t);
        int g = (int)Utility.Lerp((float)head.Start.G, (float)head.End.G, t);
        int b = (int)Utility.Lerp((float)head.Start.B, (float)head.End.B, t);
        Game1.ambientLight = new Color(r, g, b);
    }

    private static void StopAmbientLightWatcher()
    {
        ambientLightQueue.Clear();
        if (ambientLightWatcher is null) {
            return;
        }
        Log.Debug("Stopping ambientLight watcher");
        ichortower.TowerCore.Main.Helper.Events.GameLoop.UpdateTicked -= ambientLightWatcher;
        ambientLightWatcher = null;
    }

}

internal class AmbientLightShift
{
    public Color Start = Color.White;
    public Color End = Color.White;
    public int Duration = 0;
    public int StartMs = 0;

    // doesn't set Start and StartMs because those are read when the queue starts the shift
    public AmbientLightShift(Color target, int duration)
    {
        End = target;
        Duration = duration;
    }
}
