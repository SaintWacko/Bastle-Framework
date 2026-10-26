using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    // The bastle events (Defs/Incidents/BF_BastleEvents.xml). Each one is off while its World
    // switch is off and fires at its def's base chance times its frequency slider, so 0 means
    // never. None of these defs carries BastleIncidentExtension: their animals are made into
    // bastles here, and the animal-event conversion would otherwise roll them a second time.
    internal static class BastleEvents
    {
        // A rampaging animal's own combat power stays under this share of the event's points,
        // so most of the threat is in its traits.
        internal const float RampageKindShare = 0.3f;

        internal static readonly HashSet<BastleRarity> RareAndUp = new HashSet<BastleRarity>
        {
            BastleRarity.Rare, BastleRarity.Epic, BastleRarity.Legendary,
        };

        /// <summary>Animal kinds living in the map's biomes that can stand the season, each
        /// weighted toward big animals but not so far that the rarest (thrumbos) take over.
        /// manhunter: only kinds that can arrive manhunting, as vanilla's packs; when the
        /// biomes have none, any such kind that can stand the season, as vanilla falls back.</summary>
        internal static List<(PawnKindDef kind, float weight)> WildKinds(Map map, bool manhunter)
        {
            var kinds = new List<(PawnKindDef, float)>();
            var seen = new HashSet<PawnKindDef>();
            foreach (var biome in map.Biomes)
                foreach (var kind in biome.AllWildAnimals)
                {
                    if (!seen.Add(kind) || !Fits(kind, map, manhunter)) continue;
                    float commonality = 0f;
                    foreach (var b in map.Biomes) commonality = Mathf.Max(commonality, b.CommonalityOfAnimal(kind));
                    kinds.Add((kind, kind.combatPower * Mathf.Sqrt(Mathf.Max(commonality, 0.001f))));
                }
            if (kinds.Count == 0 && manhunter)
                foreach (var kind in DefDatabase<PawnKindDef>.AllDefsListForReading)
                    if (Fits(kind, map, true)) kinds.Add((kind, kind.combatPower));
            return kinds;
        }

        private static bool Fits(PawnKindDef kind, Map map, bool manhunter)
        {
            var race = kind.RaceProps;
            if (race == null || !race.Animal || kind.combatPower <= 0f) return false;
            if (manhunter && (!kind.canArriveManhunter || !race.CanPassFences)) return false;
            return map.mapTemperature.SeasonAndOutdoorTemperatureAcceptableFor(kind.race);
        }

        internal static bool TryFindRampageKind(Map map, float points, out PawnKindDef kind, HashSet<PawnKindDef> refused = null)
        {
            kind = null;
            var kinds = WildKinds(map, true);
            if (refused != null) kinds.RemoveAll(k => refused.Contains(k.kind));
            if (kinds.Count == 0) return false;
            float most = points * RampageKindShare;
            var fitting = kinds.FindAll(k => k.kind.combatPower <= most);
            if (fitting.TryRandomElementByWeight(k => k.weight, out var pick))
            {
                kind = pick.kind;
                return true;
            }
            // Too few points for any of them: the weakest still comes, with the traits.
            kind = kinds[0].kind;
            foreach (var k in kinds)
                if (k.kind.combatPower < kind.combatPower) kind = k.kind;
            return true;
        }

        /// <summary>Whether any trait can roll at one of these rarities with the current
        /// rarity weights (a player can weight a rarity to 0).</summary>
        internal static bool AnyTraitRollsAt(HashSet<BastleRarity> rarities)
        {
            var weights = BastleFrameworkMod.Settings.rarityWeights;
            foreach (var def in DefDatabase<BastleTraitDef>.AllDefsListForReading)
            {
                if (def.commonality <= 0f) continue;
                foreach (var r in rarities)
                    if (weights[(int)r] > 0f && def.FitsAt(r)) return true;
            }
            return false;
        }

        /// <summary>Generates an animal and makes it a bastle. A content mod's trait filters
        /// can refuse a whole race, so a kind that rolled nothing is set aside and another
        /// picked, a few times, before the event gives up. forced (parms.pawnKind) is tried once.</summary>
        internal static Pawn TryMakeBastle(Map map, PawnKindDef forced, Func<HashSet<PawnKindDef>, PawnKindDef> pick, Func<Pawn, bool> make)
        {
            var refused = new HashSet<PawnKindDef>();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var kind = forced ?? pick(refused);
                if (kind == null) return null;
                if (refused.Contains(kind)) continue;
                var pawn = Generate(kind, map);
                if (make(pawn)) return pawn;
                Discard(pawn);
                if (forced != null) return null;
                refused.Add(kind);
            }
            return null;
        }

        internal static bool TryFindEntryCell(Map map, out IntVec3 cell, float extraRoadChance = 0f)
            => RCellFinder.TryFindRandomPawnEntryCell(out cell, map, CellFinder.EdgeRoadChance_Animal + extraRoadChance);

        internal static float PointsOf(IncidentParms parms, Map map)
            => parms.points > 0f ? parms.points : BastleWorld.ThreatPointsNow(map);

        internal static Pawn Generate(PawnKindDef kind, Map map)
            => PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, null, PawnGenerationContext.NonPlayer, map.Tile));

        // A pawn generated for an event that then could not happen never reaches the map.
        internal static void Discard(Pawn pawn)
        {
            if (!pawn.Destroyed && !Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
        }

        internal static void MakeManhunter(Pawn pawn)
        {
            pawn.health.AddHediff(HediffDefOf.Scaria);
            pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent);
            pawn.mindState.exitMapAfterTick = Find.TickManager.TicksGame + Rand.Range(60000, 120000);
        }
    }

    /// <summary>One strong wild bastle passes through, as thrumbos do: neutral, to hunt or
    /// tame, and gone in a couple of days if left alone.</summary>
    public class IncidentWorker_BastleSighting : IncidentWorker
    {
        public override float BaseChanceThisGame => base.BaseChanceThisGame * BastleFrameworkMod.Settings.sightingFrequency;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!BastleWorld.Enabled(BastleWorldFeature.Sighting)) return false;
            Map map = (Map)parms.target;
            if (map.gameConditionManager.ConditionIsActive(GameConditionDefOf.ToxicFallout)) return false;
            if (ModsConfig.BiotechActive && map.gameConditionManager.ConditionIsActive(GameConditionDefOf.NoxiousHaze)) return false;
            return BastleEvents.WildKinds(map, false).Count > 0 && BastleEvents.TryFindEntryCell(map, out _, 0.2f);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (!BastleEvents.TryFindEntryCell(map, out IntVec3 entry, 0.2f)) return false;
            var kinds = BastleEvents.WildKinds(map, false);
            float points = BastleWorld.ThreatPointsNow(map) * BastleFrameworkMod.Settings.sightingStrength;
            Pawn pawn = BastleEvents.TryMakeBastle(map, parms.pawnKind,
                refused => kinds.Where(k => !refused.Contains(k.kind)).TryRandomElementByWeight(k => k.weight, out var pick) ? pick.kind : null,
                p => BastleWorld.MakeBastle(p, BastleWorld.RollTraitPoints(points)));
            if (pawn == null) return false;
            PawnKindDef kind = pawn.kindDef;
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(entry, map, 5), map, Rot4.Random);
            QuestUtility.AddQuestTag(pawn, parms.questTag);
            pawn.mindState.exitMapAfterTick = Find.TickManager.TicksGame + Rand.Range(90000, 150000);
            if (RCellFinder.TryFindRandomCellOutsideColonyNearTheCenterOfTheMap(entry, map, 10f, out IntVec3 center))
                pawn.mindState.forcedGotoPosition = CellFinder.RandomClosewalkCellNear(center, map, 10);

            SendStandardLetter(parms, pawn, kind.label.Named("KIND"));
            return true;
        }
    }

    /// <summary>A manhunter pack of one bastle kind in one discovery group. Points spent on
    /// traits (packTraitShare) leave the animal count, and every animal's traits read the
    /// curve at the event's full points: fewer animals than a vanilla pack, each stronger
    /// than a converted one.</summary>
    public class IncidentWorker_BastlePack : IncidentWorker
    {
        public override float BaseChanceThisGame => base.BaseChanceThisGame * BastleFrameworkMod.Settings.packFrequency;

        private static float CountPoints(float points)
            => Mathf.Max(AggressiveAnimalIncidentUtility.MinPoints, points * (1f - BastleFrameworkMod.Settings.packTraitShare));

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!BastleWorld.Enabled(BastleWorldFeature.Pack)) return false;
            Map map = (Map)parms.target;
            return AggressiveAnimalIncidentUtility.TryFindAggressiveAnimalKind(CountPoints(BastleEvents.PointsOf(parms, map)), map, out _)
                && BastleEvents.TryFindEntryCell(map, out _);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            float points = BastleEvents.PointsOf(parms, map);
            float countPoints = CountPoints(points);
            IntVec3 entry = parms.spawnCenter;
            if (!entry.IsValid && !BastleEvents.TryFindEntryCell(map, out entry)) return false;

            // Vanilla's picker is random, so asking again after a refused kind can give another.
            Pawn leader = BastleEvents.TryMakeBastle(map, parms.pawnKind,
                refused => AggressiveAnimalIncidentUtility.TryFindAggressiveAnimalKind(countPoints, map, out var k) ? k : null,
                p => BastleWorld.MakeBastle(p, BastleWorld.RollTraitPoints(points)));
            if (leader == null) return false;
            PawnKindDef kind = leader.kindDef;
            int count = parms.pawnCount > 0 ? parms.pawnCount : AggressiveAnimalIncidentUtility.GetAnimalsCount(kind, countPoints);
            var pack = new List<Pawn> { leader };
            for (int i = 1; i < count; i++)
            {
                Pawn p = BastleEvents.Generate(kind, map);
                BastleWorld.CopyPackLeader(leader, p);
                pack.Add(p);
            }

            Rot4 rot = Rot4.FromAngleFlat((map.Center - entry).AngleFlat);
            foreach (var p in pack)
            {
                GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(entry, map, 10), map, rot);
                QuestUtility.AddQuestTag(p, parms.questTag);
                BastleEvents.MakeManhunter(p);
            }
            SendStandardLetter(parms, leader, kind.GetLabelPlural().Named("KINDPLURAL"));
            Find.TickManager.slower.SignalForceNormalSpeedShort();
            return true;
        }
    }

    /// <summary>A single manhunting bastle wanders in. Its own kind takes at most
    /// RampageKindShare of the points; its traits read the curve at points x rampageStrength
    /// and are all Rare or better.</summary>
    public class IncidentWorker_BastleRampage : IncidentWorker
    {
        public override float BaseChanceThisGame => base.BaseChanceThisGame * BastleFrameworkMod.Settings.rampageFrequency;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!BastleWorld.Enabled(BastleWorldFeature.Rampage)) return false;
            Map map = (Map)parms.target;
            return BastleEvents.AnyTraitRollsAt(BastleEvents.RareAndUp)
                && BastleEvents.TryFindRampageKind(map, BastleEvents.PointsOf(parms, map), out _) && BastleEvents.TryFindEntryCell(map, out _);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            float points = BastleEvents.PointsOf(parms, map);
            IntVec3 entry = parms.spawnCenter;
            if (!entry.IsValid && !BastleEvents.TryFindEntryCell(map, out entry)) return false;

            var request = new BastleRollRequest { allowedRarities = BastleEvents.RareAndUp };
            float traitPoints = BastleWorld.RollTraitPoints(points * BastleFrameworkMod.Settings.rampageStrength);
            Pawn pawn = BastleEvents.TryMakeBastle(map, parms.pawnKind,
                refused => BastleEvents.TryFindRampageKind(map, points, out var k, refused) ? k : null,
                p => BastleWorld.MakeBastle(p, traitPoints, request));
            if (pawn == null) return false;
            PawnKindDef kind = pawn.kindDef;
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(entry, map, 5), map, Rot4.FromAngleFlat((map.Center - entry).AngleFlat));
            QuestUtility.AddQuestTag(pawn, parms.questTag);
            BastleEvents.MakeManhunter(pawn);
            SendStandardLetter(parms, pawn, kind.label.Named("KIND"));
            Find.TickManager.slower.SignalForceNormalSpeedShort();
            return true;
        }
    }
}
