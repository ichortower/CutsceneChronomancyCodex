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
// just to make the HasXFor calls nicer to read lol
using Streams = ichortower.CCC.Stream;

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
     * waitForAllStationary only checks .isMoving(), which returns false during a pause
     * step of an advancedMove, and can incorrectly return true if using (the undocumented)
     * "stopAdvancedMove next".
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
            Character actor = evt.getCharacterByName(args[i]);
            if (actor is null) {
                context.LogErrorAndSkip($"no actor found with name '{args[i]}'");
                return;
            }
            /*
            if (actor.isEmoting) {
                wait = true;
                break;
            }
            if (actor.isMoving()) {
                if (Streams.HasControllerFor(actor) || Streams.HasBasicMoveFor(args[i])) {
                    wait = true;
                    break;
                }
            }
            actor.Halt();
            */
            // the order of these checks is important, even though the expensive ones
            // are first. we have to rule out types of movement in this order so we can
            // correctly detect when movement is over, or else we will spin forever
            // (actor thinks it's moving even though it's not)
            if (evt.npcControllers?.Exists(c => c.puppet.Equals(actor)) is true) {
                wait = true;
                break;
            }
            // see Extensions.cs
            if (evt.HasBasicMoveFor(args[i])) {
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
     * ichortower.CCC_ActorHalt [next] <actor> [actor... ]
     *
     * This command stops the movement of all named actors, and removes any NPCControllers
     * that may have been puppeting them.
     *
     * If the first argument is the string "next", then actors under an NPCController's
     * influence will be allowed to finish the current leg of their movement before halting.
     * In this case, the command will block until the named actors finish moving.
     */
    public static void command_ActorHalt(SEvent evt, string[] args, EventContext context)
    {
        if (args.Length < 2) {
            context.LogErrorAndSkip("requires at least one actor argument");
            return;
        }
        bool nextMode = false;
        int start = 1;
        if (args[start].EqualsIgnoreCase("next")) {
            ++start;
            nextMode = true;
            if (args.Length < 3) {
                context.LogErrorAndSkip("requires at least one actor argument");
                return;
            }
        }
        List<string> puppets = new();
        for (int i = start; i < args.Length; ++i) {
            Character actor = evt.getCharacterByName(args[i]);
            if (actor is null) {
                context.LogErrorAndSkip($"no actor found with name '{args[i]}'");
                return;
            }
            if (nextMode && evt.npcControllers is not null) {
                var active = evt.npcControllers.Where(c => c.puppet.Equals(actor));
                if (active.Any()) {
                    puppets.Add(args[i]);
                }
                foreach (var c in active) {
                    c.destroyAtNextCrossroad();
                }
            }
            else {
                evt.npcControllers?.RemoveAll(c => c.puppet.Equals(actor));
                actor.Halt();
            }
        }
        if (puppets.Count > 0) {
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
