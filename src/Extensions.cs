using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Reflection;

using SEvent = StardewValley.Event;

namespace ichortower.CCC;

public static class Extensions
{

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
