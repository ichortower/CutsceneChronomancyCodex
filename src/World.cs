using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Delegates;
using StardewValley.Locations;
using StardewValley.Pathfinding;
using System.Collections.Generic;
using System.Reflection;

using Log = ichortower.TowerCore.Log;
using Main = ichortower.TowerCore.Main;
using SEvent = StardewValley.Event;

namespace ichortower.ECC;

internal class World
{

    /*
     * ichortower.ECC_WorldAdvanceTime <hhmm>
     *
     * Causes world time to pass when this event finishes: machines and objects process, etc.
     * NPCs get warped along their schedule to where they should be at the new time, so they
     * can resume correctly.
     * Spouses with no schedule will get warped to bed if the target time is after 2200 (10 pm).
     *
     * In multiplayer, this command has no effect: events don't freeze time in multiplayer, so
     * time will already have passed just by watching the event.
     */
    public static void command_WorldAdvanceTime(SEvent evt, string[] args, EventContext context)
    {
        if (args.Length < 2) {
            context.LogErrorAndSkip("requires a time parameter (hhmm)");
            return;
        }
        evt.ReplaceCurrentCommand($"action {Main.ModId}_WorldAdvanceTime {args[1]}");
    }


    /*
     * The traction version of _WorldAdvanceTime (this is what the event command calls).
     */
    public static bool traction_WorldAdvanceTime(string[] args,
            TriggerActionContext context,
            out string error)
    {
        int targetTime = 0;
        if (!ArgUtility.TryGetInt(args, 1, out targetTime, out error)) {
            return false;
        }
        if (targetTime <= Game1.timeOfDay) {
            error = "Target time is not in the future. " +
                $"({targetTime} <= {Game1.timeOfDay})";
            return false;
        }
        if (targetTime >= 2600) {
            error = $"Target time is too late. ({targetTime} >= 2600)";
            return false;
        }
        // ignore completely unless in single-player mode
        if (Game1.multiplayerMode != Game1.singlePlayer) {
            Log.Info($"Ignoring AdvanceTime: game is not in single-player mode.");
            error = null;
            return true;
        }

        int timePass = Utility.CalculateMinutesBetweenTimes(Game1.timeOfDay, targetTime);
        if (Game1.eventUp) {
            Game1.timeOfDayAfterFade = targetTime;
        }
        else {
            Game1.timeOfDay = targetTime;
        }
        // advance time for machines and other objects
        foreach (GameLocation loc in Game1.locations) {
            foreach (Vector2 position in new List<Vector2>(loc.objects.Keys)) {
                if (loc.objects[position].minutesElapsed(timePass)) {
                    loc.objects.Remove(position);
                }
            }
            (loc as Farm)?.timeUpdate(timePass);
        }

        // advance time for NPCs
        foreach (NPC person in Utility.getAllVillagers()) {
            if (person.IsInvisible) {
                continue;
            }
            // spouses (sometimes) and certain NPCs have null schedules.
            // null-schedule spouses should go to bed after 10 pm
            if (person.Schedule is null) {
                Farmer spouseFarmer = person.getSpouse();
                if (spouseFarmer != null && spouseFarmer.isMarriedOrRoommates()) {
                    FarmHouse home = Utility.getHomeOfFarmer(spouseFarmer);
                    if (targetTime >= 2200) {
                        person.controller = null;
                        person.temporaryController = null;
                        person.Halt();
                        Game1.warpCharacter(person, home,
                                Utility.PointToVector2(home.getSpouseBedSpot(spouseFarmer.spouse)));
                        if (home.GetSpouseBed() != null) {
                            FarmHouse.spouseSleepEndFunction(person, home);
                        }
                        person.ignoreScheduleToday = true;
                    }
                    // for earlier times, refresh marriage dialogue
                    if (targetTime >= 1800) {
                        person.currentMarriageDialogue.Clear();
                        person.checkForMarriageDialogue(1800, home);
                    }
                    else if (targetTime >= 1100) {
                        person.currentMarriageDialogue.Clear();
                        person.checkForMarriageDialogue(1100, home);
                    }
                }
                continue;
            }
            // find the last schedule entry preceding the target time
            // (or the last entry if it's already too late)
            SchedulePathDescription target = null;
            foreach (var entry in person.Schedule.Values) {
                if (entry.time > targetTime) {
                    break;
                }
                target = entry;
            }
            if (target != null) {
                // remove controllers from NPC so they don't keep walking
                person.controller = null;
                person.temporaryController = null;
                person.DirectionsToNewLocation = null;
                // synchronously end the current route animation. to do
                // this, we have to call finishRouteBehavior (if needed),
                // THEN zero out the closing animation and tell the route
                // behavior to stop (if zeroed, finishRouteBehavior won't
                // be called, since routeEndAnimationFinished, which normally
                // runs after it, clears the field it checks).
                FieldInfo seorb = typeof(NPC).GetField(
                        "_startedEndOfRouteBehavior",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                string behavior = (string)seorb.GetValue(person);
                if (behavior != null) {
                    MethodInfo frb = typeof(NPC).GetMethod(
                            "finishRouteBehavior",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                    frb.Invoke(person, new object[]{behavior});
                }
                FieldInfo reo = typeof(NPC).GetField("routeEndOutro",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                reo.SetValue(person, new int[]{});
                person.EndActivityRouteEndBehavior();
                // set up island attire according to destination
                person.shouldWearIslandAttire.Value = (target.targetLocationName == "IslandSouth");
                // move character to destination. face direction, then
                // force sprite to face that direction for real (resets
                // frame to neutral standing pose).
                Game1.warpCharacter(person,
                        target.targetLocationName,
                        target.targetTile);
                person.faceDirection(target.facingDirection);
                person.Sprite.faceDirectionStandard(target.facingDirection);
                // activate route behavior (e.g. animation). manually set
                // the route message if present, since we are bypassing the
                // PathFindController, which usually does it.
                person.StartActivityRouteEndBehavior(
                        target.endOfRouteBehavior,
                        target.endOfRouteMessage);
                person.endOfRouteMessage.Value = person.nextEndOfRouteMessage
                        ?.Replace("\"", "");
            }
        }

        error = null;
        return true;
    }


    /*
     * ichortower.ECC_TemporaryMapTiles (<layer> <x> <y> <sheet> <index>)+
     *
     * Make any number of temporary tile edits to the event map. Tile edits normally
     * persist, so this uses onEventFinished to undo the changes (by reloading the map)
     * when the event finishes.
     *
     * Use a negative index (e.g. -1) to remove a tile. Sheet will be
     * ignored in this case.
     */
    public static void command_TemporaryMapTiles(SEvent evt, string[] args, EventContext context)
    {
        int changes = 0;
        string error;
        for (int i = 1; i < args.Length; i += 5) {
            if (!ArgUtility.TryGet(args, i, out string layer, out error,
                    allowBlank: false, "string layer") ||
                !ArgUtility.TryGetPoint(args, i+1, out Point pos, out error,
                    "Point pos") ||
                !ArgUtility.TryGet(args, i+3, out string sheet, out error,
                    allowBlank: false, "string sheet") ||
                !ArgUtility.TryGetInt(args, i+4, out int index, out error,
                    "int index")) {
                context.LogError(error);
                continue;
            }
            if (index < 0) {
                context.Location.removeTile(pos.X, pos.Y, layer);
            }
            else {
                context.Location.setMapTile(pos.X, pos.Y, index, layer, sheet);
            }
            ++changes;
        }
        if (changes > 0 && !revertQueued) {
            evt.onEventFinished += delegate {
                RevertMap(context.Location);
            };
            revertQueued = true;
        }
        evt.CurrentCommand++;
    }

    public static void command_TemporaryMapOverride(SEvent evt, string[] args, EventContext context)
    {
        string error;
        if (!ArgUtility.TryGet(args, 1, out string asset, out error,
                allowBlank: false, "string asset")) {
            context.LogErrorAndSkip(error);
            return;
        }
        if (!ArgUtility.TryGetPoint(args, 2, out Point pos, out error, "Point coords")) {
            context.LogErrorAndSkip(error);
            return;
        }
        // -1, -1 is fine for width/height because ApplyMapOverride doesn't
        // use the values
        Microsoft.Xna.Framework.Rectangle destRect = new(pos.X, pos.Y, -1, -1);
        Game1.currentLocation.ApplyMapOverride(asset, null, destRect);
        tempOverrides.Add(asset);
        if (!revertQueued) {
            evt.onEventFinished += delegate {
                RevertMap(context.Location);
            };
            revertQueued = true;
        }
        evt.CurrentCommand++;
    }


    internal static void RevertMap(GameLocation loc)
    {
        HashSet<string> amo = (HashSet<string>)
                typeof(GameLocation).GetField("_appliedMapOverrides",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(loc);
        foreach (string name in tempOverrides) {
            amo.Remove(name);
        }
        loc.loadMap(loc.mapPath.Value, force_reload: true);
        tempOverrides.Clear();
        revertQueued = false;
    }
    internal static List<string> tempOverrides = new();
    internal static bool revertQueued = false;
}
