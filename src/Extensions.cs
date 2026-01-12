using Microsoft.Xna.Framework;
using StardewValley;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using SEvent = StardewValley.Event;

namespace ichortower.CCC;

public static class Extensions
{

    public static void RefCopyActorPositionsAfterMove(this SEvent evt, SEvent other)
    {
        var existing = (Dictionary<string, Vector3>) EventAPAM.GetValue(other);
        EventAPAM.SetValue(evt, existing);
    }

    public static bool TryRemoveControllers(this SEvent evt, Character actor, RemoveTiming timing)
    {
        if (evt.npcControllers is null) {
            return false;
        }
        if (timing == RemoveTiming.Now) {
            return evt.npcControllers.RemoveAll(c => c.puppet.Equals(actor)) > 0;
        }
        var active = evt.npcControllers.Where(c => c.puppet.Equals(actor));
        bool ret = false;
        foreach (NPCController c in active) {
            c.destroyAtNextCrossroad();
            ret = true;
        }
        return ret;
    }

    public enum RemoveTiming {
        Now,
        AfterThisLeg,
    }

    public static bool HasAdvancedMoveFor(this SEvent evt, Character actor)
    {
        return evt.npcControllers?.Any(c => c.puppet.Equals(actor)) ?? false;
    }

    public static bool HasBasicMoveFor(this SEvent evt, string actorName)
    {
        Dictionary<string, Vector3> moves = (Dictionary<string, Vector3>)EventAPAM.GetValue(evt);
        return moves.ContainsKey(actorName);
    }

    internal static FieldInfo EventAPAM {
        get {
            _apam ??= typeof(SEvent).GetField("actorPositionsAfterMove",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return _apam;
        }
        set {
            _apam = value;
        }
    }
    private static FieldInfo _apam;
}
