using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Delegates;
using StardewValley.Triggers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Main = ichortower.TowerCore.Main;

namespace ichortower.ECC
{
    internal sealed class ModMain : Mod
    {
        public override void Entry(IModHelper helper)
        {
            Main.Mod = this;

            List<Type> types = new() {
                typeof(ichortower.ECC.Actor),
                typeof(ichortower.ECC.Stream),
                typeof(ichortower.ECC.Viewport),
                typeof(ichortower.ECC.World),
            };
            types.ForEach(RegisterCommands);
            // TODO unhardcode this
            StardewValley.Event.RegisterCommandAlias($"{Main.ModId}_StreamBegin",
                    $"{Main.ModId}_StreamStart");
            TriggerActionManager.RegisterAction($"{Main.ModId}_WorldAdvanceTime",
                    ichortower.ECC.World.traction_WorldAdvanceTime);
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
}
