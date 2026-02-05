using StardewValley.Extensions;

namespace ichortower.ECC;

internal class Coords
{
    internal static bool TryGetTarget(string arg, out int target, out CoordType type, out string err)
    {
        target = 0;
        type = CoordType.Absolute;
        err = null;
        string toParse = arg;
        if (arg.StartsWithIgnoreCase("a")) {
            toParse = arg.Substring(1);
            type = CoordType.Absolute;
        }
        else if (arg.StartsWithIgnoreCase("-")) {
            toParse = arg;
            type = CoordType.Relative;
        }
        else if (arg.StartsWithIgnoreCase("+")) {
            toParse = arg.Substring(1);
            type = CoordType.Relative;
        }

        if (!int.TryParse(toParse, out target)) {
            err = $"'{arg}': could not convert '{toParse}' to integer";
            type = CoordType.None;
            return false;
        }
        return true;
    }
}

internal enum CoordType {
    None,
    Absolute,
    Relative,
}
