using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace BastleFramework
{
    public static class BastleDebugActions
    {
        private const string Category = "Bastle Framework";

        [DebugAction(Category, "Add trait...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> AddTrait()
        {
            var list = new List<DebugActionNode>();
            foreach (var def in DefDatabase<BastleTraitDef>.AllDefs.OrderBy(d => d.defName))
            {
                var rarities = new List<BastleRarity>();
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
                foreach (var r in rarities) node.AddChild(AddTraitNode($"{def.defName} ({r})", def, r));
                list.Add(node);
            }
            return list;
        }

        private static DebugActionNode AddTraitNode(string label, BastleTraitDef def, BastleRarity rarity) =>
            new DebugActionNode(label, DebugActionType.ToolMapForPawns, null, p =>
            {
                if (BastleUtility.AddTrait(p, def, rarity: rarity) == null)
                    Messages.Message($"{def.defName} cannot go on {p.LabelShort} (filters or a conflicting trait).", MessageTypeDefOf.RejectInput, false);
            });

        [DebugAction(Category, "Roll traits...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> RollTraits()
        {
            var list = new List<DebugActionNode>
            {
                new DebugActionNode("settings target", DebugActionType.ToolMapForPawns, null, p => Report(p, BastleUtility.RollTraits(p))),
            };
            foreach (int budget in new[] { 1, 3, 6, 10, 20 })
            {
                int b = budget;
                list.Add(new DebugActionNode($"{b} points", DebugActionType.ToolMapForPawns, null, p => Report(p, BastleUtility.RollTraits(p, b))));
            }
            return list;
        }

        private static void Report(Pawn p, List<BastleTrait> added)
        {
            Log.Message($"[Bastle Framework] Rolled {added.Count} trait(s) on {p} for {added.Sum(t => t.Points):0.#} points: "
                + string.Join(", ", added.Select(t => $"{t.def.defName} ({t.Rarity}, {t.Points:0.#})")));
        }

        [DebugAction(Category, "Set material...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> SetMaterial()
        {
            var list = new List<DebugActionNode>
            {
                new DebugActionNode("(flesh)", DebugActionType.ToolMapForPawns, null, p => BastleUtility.SetMaterial(p, null)),
                new DebugActionNode("(random)", DebugActionType.ToolMapForPawns, null, p =>
                    Log.Message($"[Bastle Framework] Rolled material for {p}: {BastleUtility.RollMaterial(p)?.defName ?? "none"}")),
            };
            foreach (var def in DefDatabase<ThingDef>.AllDefs.Where(BastleMaterialUtility.IsMaterialCandidate).OrderBy(d => d.defName))
            {
                var local = def;
                list.Add(new DebugActionNode(def.defName, DebugActionType.ToolMapForPawns, null, p => BastleUtility.SetMaterial(p, local)));
            }
            return list;
        }

        [DebugAction(Category, "Discover all traits", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DiscoverAll(Pawn p) => BastleUtility.DiscoverAll(p, notify: true);

        // Stands in for a content mod spawning a pack: three linked copies of the clicked bastle.
        [DebugAction(Category, "Spawn linked pack (3 copies)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnLinkedPack(Pawn p)
        {
            if (!BastleUtility.IsBastle(p)) return;
            for (int i = 0; i < 3; i++)
            {
                var copy = PawnGenerator.GeneratePawn(new PawnGenerationRequest(p.kindDef, p.Faction, forceGenerateNewPawn: true));
                BastleUtility.CopyBastle(p, copy, linkDiscovery: true);
                GenSpawn.Spawn(copy, CellFinder.RandomClosewalkCellNear(p.Position, p.Map, 3), p.Map);
            }
        }

        [DebugAction(Category, "Log discovery group", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogGroup(Pawn p)
        {
            var members = BastleUtility.DiscoveryGroupOf(p);
            Log.Message($"[Bastle Framework] {p} discovery group {BastleUtility.GetBastle(p)?.DiscoveryGroup ?? 0}: "
                + (members.Count == 0 ? "none" : string.Join(", ", members.Select(m => m.ToString()))));
        }

        [DebugAction(Category, "Clear bastle", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Clear(Pawn p) => BastleUtility.ClearBastle(p);
    }
}
