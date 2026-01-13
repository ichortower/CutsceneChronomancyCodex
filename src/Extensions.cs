using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.Monsters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Log = ichortower.TowerCore.Log;

using SEvent = StardewValley.Event;

namespace ichortower.CCC;

public static class Extensions
{

    /*
     * Unfortunate reimplementation of Event.Update, since there's no other way to sanely
     * run Update without setting off at least one unwanted side effect (UpdateBeforeNextCommand
     * in particular updates all NPCs, and we only want the main stream to do that, or else we
     * get multiple-speed emotes and some other problems).
     *
     * This also bypasses any harmony patches on Event.Update, but that's probably a good thing.
     */
    internal static void UpdateStream(this SEvent evt, GameLocation location, GameTime time)
    {
        if ((bool) EventFinished.GetValue(evt)) {
            return;
        }
        // left out the bit that decides whether to run Initialize. we never want to run it
        try {
            if (evt.UpdateStreamBeforeNextCommand(location, time)) {
                //evt.CheckForNextCommand(location, time);
                EventCFNC.Invoke(evt, new object[] {location, time});
            }
        }
        catch (Exception e) {
            evt.LogErrorAndHalt(e);
        }
    }

    /*
     * Unfortunate reimplementation of Event.UpdateBeforeNextCommand. See above for reasoning.
     * See below for reproductions of the vanilla logic in various pieces.
     */
    internal static bool UpdateStreamBeforeNextCommand(this SEvent evt, GameLocation location, GameTime time)
    {
        if (evt.skipped || Game1.farmEvent != null) {
            return false;
        }
        // skip bit that calls update() on the NPCs
        evt.UpdateGrabBag(location, time);
        evt.UpdateControllers(location, time);
        if (Game1.fadeToBlack) {
            return true;
        }
        if (evt.eventCommands.Length <= evt.CurrentCommand) {
            return false;
        }
        evt.UpdateVanillaViewport(location, time);
        return evt.UpdateActorPositions(location, time);
    }

    internal static void UpdateGrabBag(this SEvent evt, GameLocation location, GameTime time)
    {
        evt.aboveMapSprites?.RemoveWhere(sprite => sprite.update(time));
        if (evt.underwaterSprites is not null) {
            foreach (var s in evt.underwaterSprites) {
                s.update(time);
            }
        }
        if (!evt.playerControlSequence) {
            evt.farmer.setRunning(isRunning: false);
        }
        if (evt.isFestival) {
            evt.festivalUpdate(time);
        }
        // TODO more reflection fuck me
        //if (evt.temporaryLocation is not null && !Game1.currentLocation.Equals(evt.temporaryLocation)) {
            //evt.temporaryLocation.updateEvenIfFarmerIsntHere(time, ignoreWasUpdatedFlush: true);
        //}
    }

    internal static void UpdateControllers(this SEvent evt, GameLocation location, GameTime time)
    {
        evt.npcControllers?.RemoveWhere((c) => {
            c.puppet.isCharging = !evt.isFestival;
            return c.update(time, location, evt.npcControllers);
       });
        /*
        if (evt.npcControllers is null) {
            return;
        }
        for (int i = evt.npcControllers.Count - 1; i >= 0; --i) {
            evt.npcControllers[i].puppet.isCharging = !this.isFestival;
            if (evt.npcControllers[i].update(time, location, evt.npcControllers)) {
                evt.npcControllers.RemoveAt(i);
            }
        }
        */
    }

    internal static void UpdateVanillaViewport(this SEvent evt, GameLocation location, GameTime time)
    {
        Vector3 vTarget = (Vector3)EventViewportTarget.GetValue(evt);
        if (vTarget == Vector3.Zero) {
            return;
        }
        int playerSpeed = evt.farmer.speed;
        evt.farmer.speed = (int)vTarget.X;
        int oldX = Game1.viewport.X;
        Game1.viewport.X += (int)vTarget.X;
        if (oldX > 0 && Game1.viewport.X <= 0 && location.IsOutdoors) {
            Game1.viewport.X = 0;
            vTarget.X = 0f;
        }
        else if (oldX < location.map.DisplayWidth - Game1.viewport.Width &&
                Game1.viewport.X >= location.Map.DisplayWidth - Game1.viewport.Width) {
            Game1.viewport.X = location.Map.DisplayWidth - Game1.viewport.Width;
            vTarget.X = 0f;
        }
        if (vTarget.X != 0f) {
            Game1.updateRainDropPositionForPlayerMovement((!(vTarget.X < 0f)) ? 1 : 3, Math.Abs(vTarget.X + (float)((evt.farmer.isMoving() && evt.farmer.FacingDirection == 3) ? (-evt.farmer.speed) : ((evt.farmer.isMoving() && evt.farmer.FacingDirection == 1) ? evt.farmer.speed : 0))));
        }
        int oldY = Game1.viewport.Y;
        Game1.viewport.Y += (int)vTarget.Y;
        if (oldY > 0 && Game1.viewport.Y <= 0 && location.IsOutdoors) {
            Game1.viewport.Y = 0;
            vTarget.Y = 0f;
        }
        else if (oldY < location.map.DisplayHeight - Game1.viewport.Height &&
                Game1.viewport.Y >= location.Map.DisplayHeight - Game1.viewport.Height) {
            Game1.viewport.Y = location.Map.DisplayHeight - Game1.viewport.Height;
            vTarget.Y = 0f;
        }
        evt.farmer.speed = (int)vTarget.Y;
        if (vTarget.Y != 0f) {
            Game1.updateRainDropPositionForPlayerMovement((!(vTarget.Y < 0f)) ? 2 : 0, Math.Abs(vTarget.Y - (float)((evt.farmer.isMoving() && evt.farmer.FacingDirection == 0) ? (-evt.farmer.speed) : ((evt.farmer.isMoving() && evt.farmer.FacingDirection == 2) ? evt.farmer.speed : 0))));
        }
        evt.farmer.speed = playerSpeed;
        vTarget.Z -= time.ElapsedGameTime.Milliseconds;
        if (vTarget.Z <= 0f) {
            vTarget = Vector3.Zero;
        }
        EventViewportTarget.SetValue(evt, vTarget);
    }

    internal static bool UpdateActorPositions(this SEvent evt, GameLocation location, GameTime time)
    {
        var apam = (Dictionary<string, Vector3>)EventAPAM.GetValue(evt);
        if (apam.Count == 0) {
            return true;
        }
        foreach (string s in apam.Keys.ToArray()) {
            Rectangle targetTile = new((int)apam[s].X * 64, (int)apam[s].Y * 64, 64, 64);
            targetTile.Inflate(-4, 0);
            NPC npc = evt.getActorByName(s);
            if (npc is not null) {
                Rectangle bounds = npc.GetBoundingBox();
                if (bounds.Width > 64) {
                    targetTile.Inflate(4, 0);
                    targetTile.Width = bounds.Width + 4;
                    targetTile.Height = bounds.Height + 4;
                    targetTile.X += 8;
                    targetTile.Y += 16;
                }
            }
            if (evt.IsFarmerActorId(s, out int farmerNumber)) {
                Farmer f = evt.GetFarmerActor(farmerNumber);
                if (f is not null) {
                    Rectangle bounds = f.GetBoundingBox();
                    float moveSpeed = f.getMovementSpeed();
                    // ???
                    if (targetTile.Contains(bounds) &&
                            (((float)(bounds.Y - targetTile.Top) <= 16f + moveSpeed && f.FacingDirection != 2) ||
                             ((float)(targetTile.Bottom - bounds.Bottom) <= 16f + moveSpeed && f.FacingDirection == 2))) {
                        f.showNotCarrying();
                        f.Halt();
                        f.faceDirection((int)apam[s].Z);
                        f.FarmerSprite.StopAnimation();
                        f.Halt();
                        apam.Remove(s);
                    }
                    else if (f != null) {
                        // wtf
                        f.canOnlyWalk = false;
                        f.setRunning(isRunning: false, force: true);
                        f.canOnlyWalk = true;
                        f.lastPosition = evt.farmer.Position;
                        f.MovePosition(time, Game1.viewport, location);
                    }
                }
                continue;
            }
            foreach (NPC n in evt.actors) {
                if (!n.Name.Equals(s)) {
                    continue;
                }
                Rectangle bounds = n.GetBoundingBox();
                if (targetTile.Contains(bounds) && bounds.Y - targetTile.Top <= 16) {
                    n.Halt();
                    n.faceDirection((int)apam[s].Z);
                    apam.Remove(s);
                }
                else if (n is Monster) {
                    n.MovePosition(time, Game1.viewport, location);
                }
                else {
                    n.MovePosition(time, Game1.viewport, null);
                }
                break;
            }
            if (apam.Count == 0) {
                if (evt.continueAfterMove) {
                    evt.continueAfterMove = false;
                }
                else {
                    ++evt.CurrentCommand;
                }
            }
            if (!evt.continueAfterMove) {
                return false;
            }
        }
        return true;
    }

    internal static FieldInfo EventAPAM {
        get {
            _eventAPAM ??= typeof(SEvent).GetField("actorPositionsAfterMove",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return _eventAPAM;
        }
    }
    private static FieldInfo _eventAPAM = null;

    internal static FieldInfo EventFinished {
        get {
            _eventFinished ??= typeof(SEvent).GetField("eventFinished",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return _eventFinished;
        }
    }
    private static FieldInfo _eventFinished = null;

    internal static MethodInfo EventCFNC {
        get {
            _eventCFNC ??= typeof(SEvent).GetMethod("CheckForNextCommand",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return _eventCFNC;
        }
    }
    private static MethodInfo _eventCFNC = null;

    internal static FieldInfo EventViewportTarget {
        get {
            _eventViewportTarget ??= typeof(SEvent).GetField("viewportTarget",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return _eventViewportTarget;
        }
    }
    private static FieldInfo _eventViewportTarget;
}
