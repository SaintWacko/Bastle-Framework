using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace TitanFramework
{
    public static class TitanDebugActions
    {
        private const string Category = "Titan Framework";

        [DebugAction(Category, "Add trait...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> AddTrait()
        {
            var list = new List<DebugActionNode>();
            foreach (var def in DefDatabase<TitanTraitDef>.AllDefs.OrderBy(d => d.defName))
            {
                var local = def;
                list.Add(new DebugActionNode(def.defName, DebugActionType.ToolMapForPawns, null, p =>
                {
                    if (TitanUtility.AddTrait(p, local) == null)
                        Messages.Message($"{local.defName} cannot go on {p.LabelShort} (filters or a conflicting trait).", MessageTypeDefOf.RejectInput, false);
                }));
            }
            return list;
        }

        [DebugAction(Category, "Roll traits...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> RollTraits()
        {
            var list = new List<DebugActionNode>();
            foreach (int budget in new[] { 1, 3, 5, 10 })
            {
                int b = budget;
                list.Add(new DebugActionNode($"budget {b}", DebugActionType.ToolMapForPawns, null, p =>
                {
                    var added = TitanUtility.RollTraits(p, b);
                    Log.Message($"[Titan Framework] Rolled {added.Count} trait(s) on {p}: {string.Join(", ", added.Select(t => t.def.defName))}");
                }));
            }
            return list;
        }

        [DebugAction(Category, "Discover all traits", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DiscoverAll(Pawn p) => TitanUtility.DiscoverAll(p, notify: true);

        [DebugAction(Category, "Clear titan", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Clear(Pawn p) => TitanUtility.ClearTitan(p);
    }
}
