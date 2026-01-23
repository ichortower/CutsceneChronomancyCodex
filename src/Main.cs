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

    [ConsoleCommand("treetest", "run a short test suite for the event variable ASTs")]
    public static void TestSuite(string command, string[] args)
    {
        string input = "'foobar' . (1+42) * 3 + derp";
        bool res = ExprSyntaxTree.ExprSyntaxNode.Tokenize(input, out var arr, out string err);
        if (!res) {
            Log.Error(err);
            return;
        }
        foreach (var t in arr) {
            Log.Debug(t.ToString());
        }
        /*
        ExprSyntaxTree.ExprToken[] arr = ExprSyntaxTree.ExprSyntaxNode.Tokenize(input);
        a.Operator = ExprSyntaxTree.ExprOperator.Multiply;
        a.Lhs = new ExprSyntaxTree.ExprSyntaxNode();
        a.Lhs.Operator = ExprSyntaxTree.ExprOperator.Add;
        a.Lhs.Lhs = new();
        a.Lhs.Lhs.Value = "2";
        a.Lhs.Rhs = new();
        a.Lhs.Rhs.Value = "3";
        a.Rhs = new ExprSyntaxTree.ExprSyntaxNode();
        a.Rhs.Value = "3";
        if (!a.Eval(out string res, out string err)) {
            Log.Error("eval failed: " + err);
        }
        Log.DebugWarn($"Eval '4 * 6': result '{res}'");
        Log.DebugWarn("Successfully ran test suite");
        */
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
