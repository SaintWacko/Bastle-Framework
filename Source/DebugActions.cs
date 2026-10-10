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
                // A scaling trait gets one entry per rarity it can roll at.
                for (var r = def.rarity; r <= def.MaxRarity; r++)
                {
                    var rarity = r;
                    string label = def.scalesWithRarity ? $"{def.defName} ({r})" : def.defName;
                    list.Add(new DebugActionNode(label, DebugActionType.ToolMapForPawns, null, p =>
                    {
                        if (TitanUtility.AddTrait(p, local, rarity: rarity) == null)
                            Messages.Message($"{local.defName} cannot go on {p.LabelShort} (filters or a conflicting trait).", MessageTypeDefOf.RejectInput, false);
                    }));
                }
            }
            return list;
        }

        [DebugAction(Category, "Roll traits...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> RollTraits()
        {
            var list = new List<DebugActionNode>
            {
                new DebugActionNode("settings target", DebugActionType.ToolMapForPawns, null, p => Report(p, TitanUtility.RollTraits(p))),
            };
            foreach (int budget in new[] { 1, 3, 6, 10, 20 })
            {
                int b = budget;
                list.Add(new DebugActionNode($"{b} points", DebugActionType.ToolMapForPawns, null, p => Report(p, TitanUtility.RollTraits(p, b))));
            }
            return list;
        }

        private static void Report(Pawn p, List<TitanTrait> added)
        {
            Log.Message($"[Titan Framework] Rolled {added.Count} trait(s) on {p} for {added.Sum(t => t.Points):0.#} points: "
                + string.Join(", ", added.Select(t => $"{t.def.defName} ({t.Rarity}, {t.Points:0.#})")));
        }

        [DebugAction(Category, "Set material...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> SetMaterial()
        {
            var list = new List<DebugActionNode>
            {
                new DebugActionNode("(flesh)", DebugActionType.ToolMapForPawns, null, p => TitanUtility.SetMaterial(p, null)),
                new DebugActionNode("(random)", DebugActionType.ToolMapForPawns, null, p =>
                    Log.Message($"[Titan Framework] Rolled material for {p}: {TitanUtility.RollMaterial(p)?.defName ?? "none"}")),
            };
            foreach (var def in DefDatabase<ThingDef>.AllDefs.Where(TitanMaterialUtility.IsMaterialCandidate).OrderBy(d => d.defName))
            {
                var local = def;
                list.Add(new DebugActionNode(def.defName, DebugActionType.ToolMapForPawns, null, p => TitanUtility.SetMaterial(p, local)));
            }
            return list;
        }

        [DebugAction(Category, "Discover all traits", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DiscoverAll(Pawn p) => TitanUtility.DiscoverAll(p, notify: true);

        [DebugAction(Category, "Clear titan", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Clear(Pawn p) => TitanUtility.ClearTitan(p);
    }
}
