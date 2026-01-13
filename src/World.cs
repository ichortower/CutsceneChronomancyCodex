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

namespace ichortower.CCC;

internal class World
{

    /*
     * ichortower.CCC_WorldAdvanceTime <hhmm>
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
        /*
        error = null;
        if (Game1.multiplayerMode != Game1.singlePlayer) {
            Log.Info($"Ignored {args[0]}: game is not in single-player mode.");
            return true;
        }
        int targetTime = 0;
        if (!ArgUtility.TryGetInt(args, 1, out targetTime, out error)) {
            return false;
        }
        if (targetTime <= Game1.timeOfDay) {
            Log.Info($"Ignored {args[0]}: target time {targetTime} is not in the future.");
            return true;
        }
        if (targetTime >= 2600) {
            error = $"Target time is too late. ({targetTime} >= 2600)";
            return false;
        }
        */

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
}
