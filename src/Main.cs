using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Delegates;
using StardewValley.Triggers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using ichortower.TowerCore;
using Main = ichortower.TowerCore.Main;

namespace ichortower.ECC;

internal sealed class ModMain : Mod
{
    public override void Entry(IModHelper helper)
    {
        Main.Mod = this;

        Type[] types = Assembly.GetExecutingAssembly().GetTypes();
        foreach (Type t in types) {
            RegisterCommands(t);
        }
        // TODO unhardcode this
        StardewValley.Event.RegisterCommandAlias($"{Main.ModId}_StreamBegin",
                $"{Main.ModId}_StreamStart");
        TriggerActionManager.RegisterAction($"{Main.ModId}_WorldAdvanceTime",
                ichortower.ECC.World.traction_WorldAdvanceTime);

        ichortower.TowerCore.ConsoleCommands.Register(this.GetType(), out int _);
    }

    [ConsoleCommand("ast", "directly eval an event variable string to test AST")]
    public static void TestAst(string command, string[] args)
    {
        string input = string.Join(" ", args);
        Log.Debug(input);
        if (!ExprNode.EvalString(input, out string res, out string err)) {
            Log.Error(err);
            return;
        }
        Log.DebugWarn($"eval '{input}': result '{res}'");
    }

    private static void RegisterCommands(Type t)
    {
        MethodInfo[] funcs = t.GetMethods(BindingFlags.Public | BindingFlags.Static);
        foreach (var func in funcs) {
            if (!func.Name.StartsWith("command_")) {
                continue;
            }
            string key = func.Name.Replace("command_",
                    $"{Main.ModId}_");
            StardewValley.Event.RegisterCommand(key,
                    (EventCommandDelegate) Delegate.CreateDelegate(
                    typeof(EventCommandDelegate), func));
        }
    }
}
