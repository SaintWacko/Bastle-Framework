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

        private static readonly int[] SpawnPoints = { 1, 3, 6, 10, 15, 20, 30 };

        [DebugAction(Category, "Spawn bastle...", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> SpawnBastle()
        {
            var kinds = DefDatabase<PawnKindDef>.AllDefs
                .Where(k => k.RaceProps != null && k.RaceProps.Animal && !k.RaceProps.Dryad && k.combatPower > 0f)
                .OrderBy(k => k.defName).ToList();
            var big = kinds.Where(k => k.combatPower >= 100f).ToList();

            var byKind = new DebugActionNode("pick kind");
            foreach (var kind in kinds)
                byKind.AddChild(PointsNode($"{kind.defName} ({kind.combatPower:0})", kind, null));
            return new List<DebugActionNode>
            {
                PointsNode("random animal", null, kinds),
                PointsNode("random big animal (combat power 100+)", null, big),
                byKind,
            };
        }

        private static DebugActionNode PointsNode(string label, PawnKindDef kind, List<PawnKindDef> pool)
        {
            var node = new DebugActionNode(label);
            foreach (int points in SpawnPoints)
            {
                var pointsNode = new DebugActionNode($"{points} points");
                pointsNode.AddChild(SpawnNode("manhunter", kind, pool, points, true));
                pointsNode.AddChild(SpawnNode("wild", kind, pool, points, false));
                node.AddChild(pointsNode);
            }
            return node;
        }

        // Traits are discovered on spawn so the name and the card show what was rolled; the
        // overlays and effects show either way.
        private static DebugActionNode SpawnNode(string label, PawnKindDef kind, List<PawnKindDef> pool, int points, bool manhunter) =>
            new DebugActionNode(label, DebugActionType.ToolMap, () =>
            {
                var map = Find.CurrentMap;
                var cell = UI.MouseCell();
                if (!cell.InBounds(map) || !cell.Standable(map))
                {
                    Messages.Message("Pick a standable cell.", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                if (kind == null && pool.Count == 0)
                {
                    Messages.Message("No animal kind fits this pool.", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                var pawn = BastleEvents.TryMakeBastle(map.Tile, kind,
                    refused => pool.Where(k => !refused.Contains(k)).TryRandomElementByWeight(k => k.combatPower, out var picked) ? picked : null,
                    p => BastleWorld.MakeBastle(p, points));
                if (pawn == null)
                {
                    Messages.Message("No bastle could be made (every kind tried refused the traits).", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                GenSpawn.Spawn(pawn, cell, map);
                if (manhunter) BastleEvents.MakeManhunterNoExit(pawn);
                BastleUtility.DiscoverAll(pawn, notify: false);
                Report(pawn, BastleUtility.GetBastle(pawn)?.Traits ?? new List<BastleTrait>());
            });

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
            var b = BastleUtility.GetBastle(p);
            if (b == null || b.TraitsForReading.Count == 0 && b.Material == null)
            {
                Messages.Message($"{p.LabelShort} has no traits or material to copy.", MessageTypeDefOf.RejectInput, false);
                return;
            }
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
