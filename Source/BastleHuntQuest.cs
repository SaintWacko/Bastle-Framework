using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace BastleFramework
{
    [DefOf]
    public static class BastleQuestDefOf
    {
        public static QuestScriptDef BF_BastleHunt;
        public static SitePartDef BF_BastleLair;

        static BastleQuestDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(BastleQuestDefOf));
    }

    /// <summary>
    /// The hunt quest's frequency slider. The storyteller's quest chooser multiplies a
    /// script's rootSelectionWeight by rootSelectionWeightFactorFromPointsCurve, so a flat
    /// curve at the slider's value scales the weight without touching it: a weight of 0
    /// would also drop the quest from the dev menu's root quests.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class BastleHuntQuest
    {
        static BastleHuntQuest() => ApplyFrequency();

        internal static void ApplyFrequency()
        {
            float f = BastleFrameworkMod.Settings.huntQuestFrequency;
            BastleQuestDefOf.BF_BastleHunt.rootSelectionWeightFactorFromPointsCurve = new SimpleCurve
            {
                new CurvePoint(0f, f),
                new CurvePoint(1f, f),
            };
        }

        /// <summary>Animals at a lair with this many pack points, the alpha included.</summary>
        internal static int AnimalCount(PawnKindDef kind, float packPoints)
            => Mathf.Clamp(Mathf.RoundToInt(packPoints / Mathf.Max(1f, kind.combatPower)), 1, 100);

        /// <summary>Points left for the pack once the alpha's share is taken.</summary>
        internal static float PackPoints(float threatPoints)
            => threatPoints * (1f - BastleFrameworkMod.Settings.huntAlphaShare);

        /// <summary>Generates the lair's alpha: Rare or better traits read from the site's
        /// threat points x huntStrength. A kind whose race refuses every trait is set aside
        /// for another manhunter kind of the region. Null if none would take traits.</summary>
        internal static Pawn MakeAlpha(SitePartParams parms, PlanetTile tile)
        {
            var request = new BastleRollRequest { allowedRarities = BastleEvents.RareAndUp };
            float traitPoints = BastleWorld.RollTraitPoints(parms.threatPoints * BastleFrameworkMod.Settings.huntStrength);
            float packPoints = parms.points > 0f ? parms.points : PackPoints(parms.threatPoints);
            return BastleEvents.TryMakeBastle(tile, null,
                refused => parms.animalKind != null && !refused.Contains(parms.animalKind)
                    ? parms.animalKind
                    : ManhunterPackGenStepUtility.TryGetAnimalsKind(packPoints, tile, out var k) ? k : null,
                p => BastleWorld.MakeBastle(p, traitPoints, request));
        }

        /// <summary>What the quest giver has heard: the alpha's strongest trait that isn't a
        /// drawback, discovered quietly. Null if it has none.</summary>
        internal static BastleTrait RevealRumor(Pawn alpha)
        {
            var bastle = BastleUtility.PeekBastle(alpha);
            if (bastle == null) return null;
            BastleTrait best = null;
            foreach (var t in bastle.TraitsForReading.InRandomOrder())
                if (!t.def.IsDrawback && (best == null || t.Points > best.Points)) best = t;
            best?.Discover(notify: false);
            return best;
        }
    }

    /// <summary>
    /// Off while the HuntQuest world switch is off or no trait can roll Rare or better;
    /// otherwise stores the lair's site part with its params (kind picked, points split)
    /// as storeAs. The site has no faction: the lair belongs to the animals.
    /// </summary>
    public class QuestNode_GetBastleLairParams : QuestNode
    {
        public SlateRef<PlanetTile> tile;

        [NoTranslate] public SlateRef<string> storeAs;

        protected override bool TestRunInt(Slate slate) => TrySetVars(slate);

        protected override void RunInt()
        {
            if (!TrySetVars(QuestGen.slate))
                Log.Error("[Bastle Framework] Could not resolve the bastle lair's site part.");
        }

        private bool TrySetVars(Slate slate)
        {
            if (!BastleWorld.Enabled(BastleWorldFeature.HuntQuest) || !BastleEvents.AnyTraitRollsAt(BastleEvents.RareAndUp))
                return false;
            var def = BastleQuestDefOf.BF_BastleLair;
            var parms = def.Worker.GenerateDefaultParams(slate.Get("points", 0f), tile.GetValue(slate), null);
            if (parms.animalKind == null) return false;
            slate.Set(storeAs.GetValue(slate), new List<SitePartDefWithParams> { new SitePartDefWithParams(def, parms) });
            return true;
        }
    }

    /// <summary>
    /// A bastle alpha and a manhunting pack of its own kind. parms.threatPoints is the
    /// whole site's threat (the alpha's traits read it); parms.points is the pack's share,
    /// fixed when the quest is made so the count in its description holds. The alpha is
    /// made with the quest and held here until the map is generated, so the quest can name
    /// a trait it has.
    /// </summary>
    public class SitePartWorker_BastleLair : SitePartWorker
    {
        public override SitePartParams GenerateDefaultParams(float myThreatPoints, PlanetTile tile, Faction faction)
        {
            var parms = base.GenerateDefaultParams(myThreatPoints, tile, faction);
            parms.points = BastleHuntQuest.PackPoints(parms.threatPoints);
            if (ManhunterPackGenStepUtility.TryGetAnimalsKind(parms.points, tile, out parms.animalKind))
            {
                parms.points = Mathf.Max(parms.points, parms.animalKind.combatPower);
                parms.threatPoints = Mathf.Max(parms.threatPoints, parms.points);
            }
            return parms;
        }

        public override void Notify_GeneratedByQuestGen(SitePart part, Slate slate, List<Rule> outExtraDescriptionRules, Dictionary<string, string> outExtraDescriptionConstants)
        {
            base.Notify_GeneratedByQuestGen(part, slate, outExtraDescriptionRules, outExtraDescriptionConstants);
            Pawn alpha = BastleHuntQuest.MakeAlpha(part.parms, part.site.Tile);
            BastleTrait rumor = null;
            if (alpha != null)
            {
                part.parms.animalKind = alpha.kindDef;
                rumor = BastleHuntQuest.RevealRumor(alpha);
                part.things = new ThingOwner<Pawn>(part, oneStackOnly: true) { dontTickContents = true };
                part.things.TryAdd(alpha);
            }

            PawnKindDef kind = part.parms.animalKind;
            int count = BastleHuntQuest.AnimalCount(kind, part.parms.points);
            outExtraDescriptionRules.Add(new Rule_String("kindLabel", kind.label));
            outExtraDescriptionRules.Add(new Rule_String("kindLabelPlural", kind.GetLabelPlural()));
            outExtraDescriptionRules.Add(new Rule_String("packLabel", count - 1 + " " + GenLabel.BestKindLabel(kind, Gender.None, plural: count - 1 != 1, count - 1)));
            outExtraDescriptionRules.Add(new Rule_String("rumorTrait", rumor != null ? rumor.Override?.label ?? rumor.def.label : ""));
            outExtraDescriptionConstants.Add("count", count.ToString());
            outExtraDescriptionConstants.Add("hasAlpha", (alpha != null).ToString());
            outExtraDescriptionConstants.Add("hasRumor", (rumor != null).ToString());
        }

        public override string GetPostProcessedThreatLabel(Site site, SitePart sitePart)
        {
            var kind = sitePart.parms.animalKind;
            if (kind == null) return base.GetPostProcessedThreatLabel(site, sitePart);
            int count = BastleHuntQuest.AnimalCount(kind, sitePart.parms.points);
            return base.GetPostProcessedThreatLabel(site, sitePart) + ": "
                + "KnownSiteThreatEnemyCountAppend".Translate(count.ToString(), GenLabel.BestKindLabel(kind, Gender.None, plural: true, count));
        }
    }

    /// <summary>Spawns the lair near the map's centre, where the player arrives at the
    /// edge, as vanilla's manhunter site does: the held alpha (or a new one, for a lair
    /// made without its quest), then the rest of the pack, all manhunting for good.</summary>
    public class GenStep_BastleLair : GenStep
    {
        public FloatRange defaultPointsRange = new FloatRange(300f, 500f);

        private const int MinRoomCells = 225;

        public override int SeedPart => 820461337;

        public override void Generate(Map map, GenStepParams parms)
        {
            var traverse = TraverseParms.For(TraverseMode.NoPassClosedDoors).WithFenceblocked(true);
            if (!RCellFinder.TryFindRandomCellNearTheCenterOfTheMapWith(
                    x => CellValidator(x, map) && !x.Fogged(map) && map.reachability.CanReachMapEdge(x, traverse) && x.GetRoom(map).CellCount >= MinRoomCells,
                    map, out IntVec3 center))
                return;

            SitePart part = parms.sitePart;
            var siteParms = part?.parms ?? new SitePartParams { threatPoints = defaultPointsRange.RandomInRange };
            if (siteParms.points <= 0f) siteParms.points = BastleHuntQuest.PackPoints(siteParms.threatPoints);

            Pawn alpha = null;
            if (part?.things != null && part.things.Any)
            {
                alpha = part.things[0] as Pawn;
                part.things.Remove(alpha);
                if (alpha != null && (alpha.Destroyed || alpha.Dead)) alpha = null;
            }
            if (alpha == null) alpha = BastleHuntQuest.MakeAlpha(siteParms, map.Tile);

            PawnKindDef kind = alpha?.kindDef ?? siteParms.animalKind;
            if (kind == null && !ManhunterPackGenStepUtility.TryGetAnimalsKind(siteParms.points, map.Tile, out kind)) return;
            int count = BastleHuntQuest.AnimalCount(kind, siteParms.points);

            var pack = new List<Pawn>();
            if (alpha != null) pack.Add(alpha);
            while (pack.Count < count) pack.Add(BastleEvents.Generate(kind, map.Tile));
            foreach (var p in pack)
            {
                if (!CellFinder.TryFindRandomSpawnCellForPawnNear(center, map, out IntVec3 cell, 10, x => CellValidator(x, map)))
                    cell = center;
                GenSpawn.Spawn(p, cell, map, Rot4.Random);
                BastleEvents.MakeManhunterNoExit(p);
            }
        }

        private static bool CellValidator(IntVec3 x, Map map)
        {
            if (!x.Standable(map)) return false;
            foreach (var r in MapGenerator.UsedRects)
                if (r.Contains(x)) return false;
            return true;
        }
    }
}
