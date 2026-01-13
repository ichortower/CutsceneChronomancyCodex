using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.Pathfinding;
using System;
using System.Linq;
using System.Collections.Generic;

using Log = ichortower.TowerCore.Log;
using Main = ichortower.TowerCore.Main;
using SEvent = StardewValley.Event;

namespace ichortower.CCC;

/*
 *
 * Event commands for sophisticated actor manipulation, generally dealing with async
 * timing and positioning.
 * 
 */
internal class Actor
{

    /*
     * ichortower.CCC_ActorAwaitMovement <actor> [actor... ]
     *
     * This command blocks until all named event actors have completed their current
     * movements. This works a lot like vanilla's waitForAllStationary (all actors) and
     * proceedPosition (one actor only), but it checks for ongoing movement a bit differently.
     *
     * In particular, this command does not consider a character in a pause step during an
     * advancedMove to have stopped (waitForAllStationary and proceedPosition both do this).
     * This means that using this command to wait for a looping advancedMove will block
     * forever, so do not do this without a plan to _ActorHalt from some other stream.
     *
     * waitForAllStationary only checks .isMoving(), which returns false during a pause
     * step of an advancedMove, and can incorrectly return true if using (the undocumented)
     * "stopAdvancedMoves next".
     * 
     * proceedPosition, meanwhile, checks for active NPCControllers, but assumes that an
     * active one must be controlling the requested NPC. This is a valid assumption in
     * vanilla, where the only way to abort an advancedMove aborts all of them, but it
     * doesn't hold when time wizards are involved.
     */
    public static void command_ActorAwaitMovement(SEvent evt, string[] args, EventContext context)
    {
        if (args.Length < 2) {
            context.LogErrorAndSkip("requires at least one actor argument");
            return;
        }
        bool wait = false;
        for (int i = 1; i < args.Length; ++i) {
            string actorName = args[i];
            Character actor = evt.getCharacterByName(actorName);
            if (actor is null) {
                context.LogErrorAndSkip($"no actor found with name '{actorName}'");
                return;
            }
            // check controllers and move targets and ignore .isMoving(), so pause steps
            // don't count as being done moving.
            // if neither is active, it's safe (and required, for animations) to call Halt().
            if (Streams.HasControllerFor(actor) || Streams.HasBasicMoveFor(actorName)) {
                wait = true;
                break;
            }
            actor.Halt();
        }
        if (!wait) {
            ++evt.CurrentCommand;
        }
    }


    /*
     * ichortower.CCC_ActorHalt [next|waitnext] <actor> [actor... ]
     *
     * This command stops the movement of all named actors, and removes any NPCControllers
     * that may have been puppeting them.
     *
     * The first argument can be one of two special strings in order to change the behavior
     * (both case-insensitive):
     *
     *   "next": actors will be allowed to finish the current leg of their movement before
     *     halting.
     *   "waitnext": as next, but this command will also block until the movements complete.
     *     this is done by inserting the correct _ActorAwaitMovement command.
     *
     * Generally, waitnext is advised over next, since some moves may not halt correctly
     * unless the _ActorAwaitMovement command helps. Next is useful if you have another
     * stream awaiting the movement already.
     */
    public static void command_ActorHalt(SEvent evt, string[] args, EventContext context)
    {
        bool nextMode = false;
        bool waitMode = false;
        int start = 1;
        if (args[start].EqualsIgnoreCase("next")) {
            nextMode = true;
            ++start;
        }
        else if (args[start].EqualsIgnoreCase("waitnext")) {
            nextMode = true;
            waitMode = true;
            ++start;
        }
        if (args.Length <= start) {
            context.LogErrorAndSkip("requires at least one actor argument");
            return;
        }

        List<string> puppets = new();
        for (int i = start; i < args.Length; ++i) {
            string actorName = args[i];
            Character actor = evt.getCharacterByName(actorName);
            if (actor is null) {
                context.LogErrorAndSkip($"no actor found with name '{actorName}'");
                return;
            }

            if (nextMode) {
                // don't need to do any removal on basic moves
                if (Streams.TryRemoveControllersFor(actor, Streams.RemoveTiming.AfterThisLeg) ||
                        Streams.HasBasicMoveFor(actorName)) {
                    puppets.Add(actorName);
                }
            }
            else {
                _ = Streams.TryRemoveControllersFor(actor, Streams.RemoveTiming.Now);
                _ = Streams.TryRemoveBasicMovesFor(actorName);
                actor.Halt();
            }
        }
        if (waitMode && puppets.Count > 0) {
            evt.InsertNextCommand($"{Main.ModId}_ActorAwaitMovement {String.Join(' ', puppets)}");
        }
        ++evt.CurrentCommand;
    }


    /*
     * ichortower.CCC_ActorPathTo <actor> <x> <y> <facingDirection> [wait]
     *
     * Tells an event actor to move to a specific tile coordinate, but relies on the
     * pathfinder to calculate a route instead of requiring you to type out the steps.
     * This is mainly useful if you have halted a stream or otherwise don't know
     * exactly where an NPC will be standing, but want them to proceed to a fixed
     * location.
     */
    public static void command_ActorPathTo(SEvent evt, string[] args, EventContext context)
    {
        string error;
        if (!ArgUtility.TryGet(args, 1, out string actorName, out error) ||
                !ArgUtility.TryGetInt(args, 2, out int targetX, out error) ||
                !ArgUtility.TryGetInt(args, 3, out int targetY, out error) ||
                !ArgUtility.TryGetInt(args, 4, out int facingDirection, out error)) {
            context.LogErrorAndSkip(error);
            return;
        }
        Character actor = evt.getCharacterByName(actorName);
        if (actor is null) {
            context.LogErrorAndSkip($"no actor found with name '{actorName}'");
            return;
        }
        Stack<Point> foundPath = null;
        try {
            foundPath = PathFindController.findPath(actor.TilePoint,
                    new Point(targetX, targetY), PathFindController.isAtEndPoint,
                    context.Location, actor, 10000);
        }
        catch (Exception e) {
            context.LogErrorAndSkip($"pathfinder barfed: {e}");
            return;
        }
        if (foundPath is null || foundPath.Count == 0) {
            context.LogErrorAndSkip($"path could not be found for '{actorName}' from " +
                    $"({actor.TilePoint.X},{actor.TilePoint.Y}) to ({targetX},{targetY})");
            return;
        }

        if (ArgUtility.TryGet(args, 5, out string wait, out error) && wait.EqualsIgnoreCase("wait")) {
            evt.InsertNextCommand($"{Main.ModId}_ActorAwaitMovement {actorName}");
        }

        // a bunch of extra checks in here in order to collapse repeated entries into one
        // long one, so e.g. (-1 0) (-1 0) (-1 0) becomes (-3 0). this is needed in order to
        // prevent animation hiccups.
        Point current = foundPath.Pop();
        List<Vector2> advancedPath = new();
        Vector2 priorStep = Vector2.Zero;
        while (foundPath.Count > 0) {
            Point next = foundPath.Pop();
            Vector2 thisStep = new(next.X - current.X, next.Y - current.Y);
            if (priorStep.Equals(thisStep)) {
                advancedPath[advancedPath.Count - 1] += thisStep;
            }
            else {
                advancedPath.Add(new Vector2(next.X - current.X, next.Y - current.Y));
            }
            priorStep = thisStep;
            current = next;
        }
        evt.npcControllers ??= new();
        evt.npcControllers.Add(new NPCController(actor, advancedPath, loop: false,
                endBehavior: () => {
                    actor.Halt();
                    actor.faceDirection(facingDirection);
                }));

        ++evt.CurrentCommand;
    }

}
