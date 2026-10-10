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
                var rarities = new List<TitanRarity>();
                for (var r = def.rarity; r <= def.MaxRarity; r++)
                    if (def.CanAppearAt(r)) rarities.Add(r);
                if (rarities.Count == 0) continue;
                if (rarities.Count == 1)
                {
                    list.Add(AddTraitNode($"{def.defName} ({rarities[0]})", def, rarities[0]));
                    continue;
                }
                // A trait that rolls at several rarities gets one entry that opens a
                // list of them, so the main list stays one line per trait.
                var node = new DebugActionNode($"{def.defName} (variable)");
                foreach (var r in rarities) node.AddChild(AddTraitNode(r.ToString(), def, r));
                list.Add(node);
            }
            return list;
        }

        private static DebugActionNode AddTraitNode(string label, TitanTraitDef def, TitanRarity rarity) =>
            new DebugActionNode(label, DebugActionType.ToolMapForPawns, null, p =>
            {
                if (TitanUtility.AddTrait(p, def, rarity: rarity) == null)
                    Messages.Message($"{def.defName} cannot go on {p.LabelShort} (filters or a conflicting trait).", MessageTypeDefOf.RejectInput, false);
            });

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
