using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Grammar;

namespace BastleFramework
{
    [DefOf]
    public static class BastleOutbreakDefOf
    {
        public static QuestScriptDef BF_BastleOutbreak;
        public static SitePartDef BF_BastleOutbreakSite;
        public static GameConditionDef BF_UncontainedOutbreak;
        public static HistoryEventDef BF_OutbreakFailed;

        static BastleOutbreakDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(BastleOutbreakDefOf));
    }

    /// <summary>
    /// The outbreak quest: one strain (a kind and a trait set, held as a pawn that never
    /// spawns) breeding at a site, 1 to 3 alphas that carry it plus traits of their own,
    /// and one or two other factions that send fighters to arrive with the colony. Every
    /// animal of the strain shares one discovery group, the escaped strain's later packs
    /// included. Frequency works as the hunt quest's: a flat points curve at the slider.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class BastleOutbreakQuest
    {
        static BastleOutbreakQuest() => ApplyFrequency();

        internal static void ApplyFrequency()
        {
            float f = BastleFrameworkMod.Settings.outbreakQuestFrequency;
            BastleOutbreakDefOf.BF_BastleOutbreak.rootSelectionWeightFactorFromPointsCurve = new SimpleCurve
            {
                new CurvePoint(0f, f),
                new CurvePoint(1f, f),
            };
        }

        internal static int AlphaCount(float questPoints) => questPoints < 1000f ? 1 : questPoints < 3000f ? 2 : 3;

        internal static float PackPoints(float threatPoints)
            => threatPoints * (1f - BastleFrameworkMod.Settings.outbreakTraitShare);

        /// <summary>The faction can attend: a humanlike faction with a leader and fighters,
        /// at peace with the colony (phase 1 has no truce).</summary>
        internal static bool CanAttend(Faction f)
            => f != null && !f.IsPlayer && !f.Hidden && !f.defeated && !f.temporary && f.def.humanlikeFaction
               && !f.def.permanentEnemy && !f.HostileTo(Faction.OfPlayer)
               && f.def.pawnGroupMakers != null && f.def.pawnGroupMakers.Any(m => m.kindDef == PawnGroupKindDefOf.Combat);

        /// <summary>The outbreak quest part that owns this site, if any.</summary>
        internal static QuestPart_BastleOutbreak PartFor(MapParent site)
        {
            if (site == null) return null;
            foreach (var q in Find.QuestManager.QuestsListForReading)
            {
                if (q.State != QuestState.Ongoing && q.State != QuestState.NotYetAccepted) continue;
                foreach (var p in q.PartsListForReading)
                    if (p is QuestPart_BastleOutbreak o && o.site == site) return o;
            }
            return null;
        }

        /// <summary>The strain: an animal of the region's manhunter kinds made a bastle at
        /// the quest's points x outbreakStrength. A kind that refuses traits is set aside.</summary>
        internal static Pawn MakeStrain(PawnKindDef preferred, float packPoints, float questPoints, PlanetTile tile)
        {
            float traitPoints = BastleWorld.RollTraitPoints(questPoints * BastleFrameworkMod.Settings.outbreakStrength);
            return BastleEvents.TryMakeBastle(tile, null,
                refused => preferred != null && !refused.Contains(preferred)
                    ? preferred
                    : ManhunterPackGenStepUtility.TryGetAnimalsKind(packPoints, tile, out var k) ? k : null,
                p => BastleWorld.MakeBastle(p, traitPoints));
        }

        /// <summary>An alpha: the strain copied (same discovery group), then extra traits at
        /// questPoints x outbreakAlphaStrength, Rare or better when any trait can be.</summary>
        internal static Pawn MakeAlpha(Pawn strain, float questPoints, PlanetTile tile)
        {
            Pawn alpha = BastleEvents.Generate(strain.kindDef, tile);
            BastleWorld.CopyPackLeader(strain, alpha);
            var request = BastleEvents.AnyTraitRollsAt(BastleEvents.RareAndUp)
                ? new BastleRollRequest { allowedRarities = BastleEvents.RareAndUp }
                : null;
            BastleWorld.MakeBastle(alpha, BastleWorld.RollTraitPoints(questPoints * BastleFrameworkMod.Settings.outbreakAlphaStrength), request);
            return alpha;
        }

        /// <summary>The alpha's strongest trait of its own (not the strain's) that isn't a
        /// drawback, discovered quietly for the quest text. Null if it has none.</summary>
        internal static BastleTrait RevealRumor(Pawn alpha, Pawn strain)
        {
            var bastle = BastleUtility.PeekBastle(alpha);
            var strainBastle = BastleUtility.PeekBastle(strain);
            if (bastle == null) return null;
            BastleTrait best = null;
            foreach (var t in bastle.TraitsForReading.InRandomOrder())
                if (!t.def.IsDrawback && strainBastle?.HasTrait(t.def) != true && (best == null || t.Points > best.Points)) best = t;
            best?.Discover(notify: false);
            return best;
        }

        internal static Pawn CopyOfStrain(Pawn strain, PawnKindDef kind, PlanetTile tile)
        {
            Pawn p = BastleEvents.Generate(kind, tile);
            if (strain != null) BastleWorld.CopyPackLeader(strain, p);
            return p;
        }
    }

    /// <summary>Picks who attends: the asker (a faction leader, stored as asker) and,
    /// at outbreakSecondFactionChance, a second faction (secondFaction, else null).</summary>
    public class QuestNode_GetBastleOutbreakFactions : QuestNode
    {
        [NoTranslate] public SlateRef<string> storeAskerAs = "asker";
        [NoTranslate] public SlateRef<string> storeSecondFactionAs = "secondFaction";

        protected override bool TestRunInt(Slate slate) => TrySetVars(slate);

        protected override void RunInt()
        {
            if (!TrySetVars(QuestGen.slate))
            {
                Log.Error("[Bastle Framework] Could not find factions for the bastle outbreak.");
                return;
            }
            // Slate values reach the grammar as rules only; the description's conditions need constants.
            QuestGen.AddQuestDescriptionConstants(new Dictionary<string, string>
            {
                { "hasSecondFaction", QuestGen.slate.Get<bool>("hasSecondFaction").ToString() },
            });
        }

        private bool TrySetVars(Slate slate)
        {
            var factions = Find.FactionManager.AllFactionsListForReading.Where(BastleOutbreakQuest.CanAttend).ToList();
            var askers = factions.Where(f => f.leader != null && !f.leader.Dead
                && !(f.leader.IsWorldPawn() && Find.WorldPawns.GetSituation(f.leader) == WorldPawnSituation.ReservedByQuest)).ToList();
            if (!askers.TryRandomElement(out var asker)) return false;
            Faction second = null;
            if (Rand.Chance(BastleFrameworkMod.Settings.outbreakSecondFactionChance))
                factions.Where(f => f != asker).TryRandomElement(out second);
            slate.Set(storeAskerAs.GetValue(slate), asker.leader);
            slate.Set(storeSecondFactionAs.GetValue(slate), second);
            slate.Set("hasSecondFaction", second != null);
            return true;
        }
    }

    /// <summary>Off while the OutbreakQuest switch is off; otherwise stores the outbreak
    /// site part, its threat points the quest's points x outbreakSize.</summary>
    public class QuestNode_GetBastleOutbreakParams : QuestNode
    {
        public SlateRef<PlanetTile> tile;

        [NoTranslate] public SlateRef<string> storeAs;

        protected override bool TestRunInt(Slate slate) => TrySetVars(slate);

        protected override void RunInt()
        {
            if (!TrySetVars(QuestGen.slate))
                Log.Error("[Bastle Framework] Could not resolve the bastle outbreak's site part.");
        }

        private bool TrySetVars(Slate slate)
        {
            if (!BastleWorld.Enabled(BastleWorldFeature.OutbreakQuest)) return false;
            var def = BastleOutbreakDefOf.BF_BastleOutbreakSite;
            float points = slate.Get("points", 0f) * BastleFrameworkMod.Settings.outbreakSize;
            var parms = def.Worker.GenerateDefaultParams(points, tile.GetValue(slate), null);
            if (parms.animalKind == null) return false;
            slate.Set(storeAs.GetValue(slate), new List<SitePartDefWithParams> { new SitePartDefWithParams(def, parms) });
            return true;
        }
    }

    /// <summary>
    /// The outbreak site. parms.threatPoints is the sized threat; parms.points the pack's
    /// share at the quest's start, which grows with the quest part's growth factor until
    /// the map is made. The strain and alphas are made here, so the description names the
    /// kind they really are, and handed to the quest part by QuestNode_BastleOutbreak.
    /// </summary>
    public class SitePartWorker_BastleOutbreak : SitePartWorker
    {
        public override SitePartParams GenerateDefaultParams(float myThreatPoints, PlanetTile tile, Faction faction)
        {
            var parms = base.GenerateDefaultParams(myThreatPoints, tile, faction);
            parms.points = BastleOutbreakQuest.PackPoints(parms.threatPoints);
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
            float questPoints = slate.Get("points", part.parms.threatPoints / Mathf.Max(1f, BastleFrameworkMod.Settings.outbreakSize));
            part.things = new ThingOwner<Pawn>(part) { dontTickContents = true };
            Pawn strain = BastleOutbreakQuest.MakeStrain(part.parms.animalKind, part.parms.points, questPoints, part.site.Tile);
            BastleTrait rumor = null;
            int alphas = 0;
            if (strain != null)
            {
                part.parms.animalKind = strain.kindDef;
                part.things.TryAdd(strain);
                alphas = BastleOutbreakQuest.AlphaCount(questPoints);
                for (int i = 0; i < alphas; i++)
                {
                    Pawn alpha = BastleOutbreakQuest.MakeAlpha(strain, questPoints, part.site.Tile);
                    if (i == 0) rumor = BastleOutbreakQuest.RevealRumor(alpha, strain);
                    part.things.TryAdd(alpha);
                }
            }
            if (part.parms.animalKind == null)
                ManhunterPackGenStepUtility.TryGetAnimalsKind(part.parms.points, part.site.Tile, out part.parms.animalKind);
            PawnKindDef kind = part.parms.animalKind;
            int count = kind != null ? BastleHuntQuest.AnimalCount(kind, part.parms.points) : 0;
            outExtraDescriptionRules.Add(new Rule_String("kindLabel", kind?.label ?? ""));
            outExtraDescriptionRules.Add(new Rule_String("kindLabelPlural", kind?.GetLabelPlural() ?? ""));
            outExtraDescriptionRules.Add(new Rule_String("rumorTrait", rumor != null ? rumor.Override?.label ?? rumor.def.label : ""));
            outExtraDescriptionRules.Add(new Rule_String("alphaCount", alphas.ToString()));
            // A rule for the text and a constant for the conditions: the grammar needs both.
            outExtraDescriptionRules.Add(new Rule_String("count", (count + alphas).ToString()));
            outExtraDescriptionConstants.Add("count", (count + alphas).ToString());
            outExtraDescriptionConstants.Add("alphaCount", alphas.ToString());
            outExtraDescriptionConstants.Add("hasStrain", (strain != null).ToString());
            outExtraDescriptionConstants.Add("hasRumor", (rumor != null).ToString());
        }

        public override string GetPostProcessedThreatLabel(Site site, SitePart sitePart)
        {
            var kind = sitePart.parms.animalKind;
            if (kind == null) return base.GetPostProcessedThreatLabel(site, sitePart);
            var outbreak = BastleOutbreakQuest.PartFor(site);
            int count = outbreak != null
                ? outbreak.AnimalCountNow
                : BastleHuntQuest.AnimalCount(kind, sitePart.parms.points);
            return base.GetPostProcessedThreatLabel(site, sitePart) + ": "
                + "KnownSiteThreatEnemyCountAppend".Translate(count.ToString(), GenLabel.BestKindLabel(kind, Gender.None, plural: true, count));
        }
    }

    /// <summary>Makes the quest part that runs the outbreak, taking the strain and alphas
    /// from the site part into it.</summary>
    public class QuestNode_BastleOutbreak : QuestNode
    {
        public SlateRef<Site> site;
        public SlateRef<Pawn> asker;
        public SlateRef<Faction> secondFaction;

        protected override bool TestRunInt(Slate slate) => true;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            Site s = site.GetValue(slate);
            var part = new QuestPart_BastleOutbreak
            {
                site = s,
                startTick = Find.TickManager.TicksGame,
                inSignalEnable = QuestGenUtility.HardcodedSignalWithQuestID("site.MapGenerated"),
                inSignalSuccess = QuestGenUtility.HardcodedSignalWithQuestID("site.AllEnemiesDefeated"),
                inSignalFail = QuestGenUtility.HardcodedSignalWithQuestID("outbreakFailed"),
                asker = asker.GetValue(slate)?.Faction,
            };
            if (part.asker != null) part.attendees.Add(new OutbreakAttendee { faction = part.asker });
            Faction second = secondFaction.GetValue(slate);
            if (second != null) part.attendees.Add(new OutbreakAttendee { faction = second });

            SitePart sp = s?.parts.FirstOrDefault(p => p.def == BastleOutbreakDefOf.BF_BastleOutbreakSite);
            if (sp != null)
            {
                part.basePackPoints = sp.parms.points;
                part.kind = sp.parms.animalKind;
                sp.things?.TryTransferAllToContainer(part.held);
            }
            QuestGen.quest.AddPart(part);
        }
    }

    /// <summary>One faction at the outbreak: who it sent, and how many.</summary>
    public class OutbreakAttendee : IExposable
    {
        public Faction faction;
        public List<Pawn> pawns = new List<Pawn>();
        public int brought;

        public int Survivors => pawns.Count(p => p != null && !p.Dead && p.Faction == faction && !p.IsPrisoner);

        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
            Scribe_Values.Look(ref brought, "brought", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pawns = pawns ?? new List<Pawn>();
                pawns.RemoveAll(p => p == null);
            }
        }
    }

    /// <summary>
    /// Runs the outbreak. Holds the strain (held[0], never spawned) and the alphas until the
    /// map is made; grows the outbreak by the days since the quest began until then; enabled
    /// when the map is generated, it waits for the colony to arrive and brings the attending
    /// factions in at the edge nearest them. Success pays each attendee goodwill by the share
    /// of its fighters that survived; failure costs goodwill with the asker and lets the strain
    /// loose as an uncontained outbreak.
    /// </summary>
    public class QuestPart_BastleOutbreak : QuestPartActivable, IThingHolder
    {
        public Site site;
        public PawnKindDef kind;
        public Faction asker;
        public float basePackPoints;
        public int startTick = -1;
        public int frozenTick = -1;
        public string inSignalSuccess;
        public string inSignalFail;
        public List<OutbreakAttendee> attendees = new List<OutbreakAttendee>();
        public ThingOwner<Pawn> held;

        private int colonyArrivedTick = -1;
        private bool alliesArrived;
        private float arrivalPackPoints;

        // Long enough for a whole caravan or every drop pod to land before the edge is chosen.
        private const int ArrivalDelayTicks = 120;

        public QuestPart_BastleOutbreak()
        {
            held = new ThingOwner<Pawn>(this) { dontTickContents = true };
        }

        public Pawn Strain => held.Count > 0 ? held[0] : null;

        public float GrowthFactor
        {
            get
            {
                var s = BastleFrameworkMod.Settings;
                int end = frozenTick >= 0 ? frozenTick : Find.TickManager.TicksGame;
                float days = Mathf.Max(0f, (end - startTick) / (float)GenDate.TicksPerDay);
                return Mathf.Min(Mathf.Max(1f, s.outbreakGrowthCap), 1f + s.outbreakGrowth * days);
            }
        }

        public float PackPointsNow => basePackPoints * GrowthFactor;

        public int AnimalCountNow
            => (kind != null ? BastleHuntQuest.AnimalCount(kind, PackPointsNow) : 0) + Mathf.Max(0, held.Count - 1);

        /// <summary>The map is being made: growth stops, and the alphas leave the holder for
        /// the map. The strain stays, for the escape or the cleanup.</summary>
        public List<Pawn> TakeAlphasAndFreeze()
        {
            frozenTick = Find.TickManager.TicksGame;
            var alphas = new List<Pawn>();
            for (int i = held.Count - 1; i >= 1; i--)
            {
                Pawn p = held[i];
                held.Remove(p);
                if (!p.Destroyed && !p.Dead) alphas.Add(p);
            }
            return alphas;
        }

        public override IEnumerable<Faction> InvolvedFactions
        {
            get
            {
                foreach (var f in base.InvolvedFactions) yield return f;
                foreach (var a in attendees)
                    if (a.faction != null) yield return a.faction;
            }
        }

        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                foreach (var t in base.QuestLookTargets) yield return t;
                if (site != null) yield return site;
            }
        }

        public override bool QuestPartReserves(Pawn p)
        {
            foreach (var a in attendees)
                if (a.pawns.Contains(p)) return true;
            return false;
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            if (signal.tag == inSignalSuccess) Succeed();
            else if (signal.tag == inSignalFail) Fail();
            base.Notify_QuestSignalReceived(signal);
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (alliesArrived || Find.TickManager.TicksGame % 30 != 0) return;
            Map map = site?.Map;
            if (map == null) return;
            if (colonyArrivedTick < 0)
            {
                if (map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Count > 0) colonyArrivedTick = Find.TickManager.TicksGame;
                return;
            }
            if (Find.TickManager.TicksGame - colonyArrivedTick < ArrivalDelayTicks) return;
            // Retried every check until the colony is on the map and an edge cell is found.
            alliesArrived = BringAttendees(map);
        }

        private bool BringAttendees(Map map)
        {
            var colony = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            if (colony.Count == 0) return false;
            arrivalPackPoints = PackPointsNow;
            IntVec3 anchor = colony[0].Position;
            IntVec3 sum = IntVec3.Zero;
            foreach (var p in colony) sum += p.Position;
            var centroid = new IntVec3(sum.x / colony.Count, 0, sum.z / colony.Count);
            if (!TryFindEdgeCellNear(map, centroid, anchor, out IntVec3 entry)) return false;

            var arrived = new List<Pawn>();
            var names = new List<string>();
            foreach (var a in attendees)
            {
                Faction f = a.faction;
                if (!BastleOutbreakQuest.CanAttend(f)) continue;
                float points = Mathf.Max(arrivalPackPoints * BastleFrameworkMod.Settings.outbreakAllyShare,
                    f.def.MinPointsToGeneratePawnGroup(PawnGroupKindDefOf.Combat));
                var parms = new PawnGroupMakerParms
                {
                    groupKind = PawnGroupKindDefOf.Combat,
                    tile = map.Tile,
                    faction = f,
                    points = points,
                    generateFightersOnly = true,
                };
                var pawns = PawnGroupMakerUtility.GeneratePawns(parms, warnOnZeroResults: false).ToList();
                if (pawns.Count == 0) continue;
                foreach (var p in pawns)
                {
                    if (!CellFinder.TryFindRandomSpawnCellForPawnNear(entry, map, out IntVec3 cell, 6)) cell = entry;
                    GenSpawn.Spawn(p, cell, map, Rot4.Random);
                }
                LordMaker.MakeNewLord(f, new LordJob_BastleOutbreakAllies(f, map.Center), map, pawns);
                a.pawns.AddRange(pawns);
                a.brought = pawns.Count;
                arrived.AddRange(pawns);
                names.Add(f.NameColored.Resolve());
            }
            if (arrived.Count == 0) return true;
            Find.LetterStack.ReceiveLetter("BF_OutbreakAlliesArrivedLabel".Translate(),
                "BF_OutbreakAlliesArrivedText".Translate(names.ToCommaList(useAnd: true), kind?.GetLabelPlural() ?? ""),
                LetterDefOf.NeutralEvent, arrived, quest: quest);
            return true;
        }

        // The edge nearest the colonists, reachable from where they stand, so the attendees
        // arrive beside them wherever they came in (a caravan's edge, or the edge closest to
        // drop pods).
        private static bool TryFindEdgeCellNear(Map map, IntVec3 centroid, IntVec3 anchor, out IntVec3 cell)
        {
            int west = centroid.x, east = map.Size.x - 1 - centroid.x, south = centroid.z, north = map.Size.z - 1 - centroid.z;
            int least = Mathf.Min(Mathf.Min(west, east), Mathf.Min(south, north));
            IntVec3 near = least == west ? new IntVec3(0, 0, centroid.z)
                : least == east ? new IntVec3(map.Size.x - 1, 0, centroid.z)
                : least == south ? new IntVec3(centroid.x, 0, 0)
                : new IntVec3(centroid.x, 0, map.Size.z - 1);
            bool Valid(IntVec3 x) => x.Standable(map) && map.reachability.CanReach(anchor, x, PathEndMode.OnCell, TraverseMode.PassDoors);
            return CellFinder.TryFindRandomEdgeCellNearWith(near, 20f, map, Valid, out cell)
                || CellFinder.TryFindRandomEdgeCellWith(Valid, map, 0f, out cell);
        }

        private void Succeed()
        {
            var lines = new List<string>();
            int most = BastleFrameworkMod.Settings.outbreakAllyGoodwill;
            foreach (var a in attendees)
            {
                if (a.faction == null || a.brought <= 0) continue;
                int survivors = a.Survivors;
                int goodwill = Mathf.RoundToInt(most * survivors / (float)a.brought);
                if (goodwill != 0 && !a.faction.defeated)
                    Faction.OfPlayer.TryAffectGoodwillWith(a.faction, goodwill, canSendMessage: false, reason: HistoryEventDefOf.QuestGoodwillReward);
                lines.Add("BF_OutbreakSurvivorsLine".Translate(a.faction.NameColored, survivors, a.brought, goodwill.ToStringWithSign()).Resolve());
            }
            if (lines.Count > 0)
                Find.LetterStack.ReceiveLetter("BF_OutbreakContainedLabel".Translate(),
                    "BF_OutbreakContainedText".Translate(kind?.GetLabelPlural() ?? "") + "\n\n" + lines.ToLineList("  - "),
                    LetterDefOf.PositiveEvent, (LookTargets)site, quest: quest);
        }

        private void Fail()
        {
            int penalty = BastleFrameworkMod.Settings.outbreakFailGoodwill;
            if (asker != null && !asker.defeated && penalty > 0)
                Faction.OfPlayer.TryAffectGoodwillWith(asker, -penalty, reason: BastleOutbreakDefOf.BF_OutbreakFailed);

            Pawn strain = Strain;
            if (strain == null || BastleFrameworkMod.Settings.outbreakUncontainedDays <= 0f) return;
            int duration = Mathf.RoundToInt(BastleFrameworkMod.Settings.outbreakUncontainedDays * Rand.Range(0.75f, 1.25f) * GenDate.TicksPerDay);
            var condition = (GameCondition_BastleOutbreak)GameConditionMaker.MakeCondition(BastleOutbreakDefOf.BF_UncontainedOutbreak, duration);
            condition.quest = quest;
            held.TryTransferToContainer(strain, condition.held);
            Find.World.gameConditionManager.RegisterCondition(condition);
            Find.LetterStack.ReceiveLetter("BF_OutbreakEscapedLabel".Translate(strain.kindDef.label),
                "BF_OutbreakEscapedText".Translate(strain.kindDef.GetLabelPlural()), LetterDefOf.NegativeEvent, LookTargets.Invalid, quest: quest);
        }

        public override void Notify_PawnDiscarded(Pawn pawn)
        {
            base.Notify_PawnDiscarded(pawn);
            foreach (var a in attendees) a.pawns.Remove(pawn);
        }

        public override void Cleanup()
        {
            base.Cleanup();
            // The strain is gone with the quest unless it escaped (moved to the condition).
            while (held.Count > 0)
            {
                Pawn p = held[0];
                held.Remove(p);
                BastleEvents.Discard(p);
            }
            foreach (var a in attendees) a.pawns.Clear();
        }

        public IThingHolder ParentHolder => null;

        public ThingOwner GetDirectlyHeldThings() => held;

        public void GetChildHolders(List<IThingHolder> outChildren)
            => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref site, "site");
            Scribe_Defs.Look(ref kind, "kind");
            Scribe_References.Look(ref asker, "asker");
            Scribe_Values.Look(ref basePackPoints, "basePackPoints", 0f);
            Scribe_Values.Look(ref startTick, "startTick", -1);
            Scribe_Values.Look(ref frozenTick, "frozenTick", -1);
            Scribe_Values.Look(ref inSignalSuccess, "inSignalSuccess");
            Scribe_Values.Look(ref inSignalFail, "inSignalFail");
            Scribe_Collections.Look(ref attendees, "attendees", LookMode.Deep);
            Scribe_Deep.Look(ref held, "held", this);
            Scribe_Values.Look(ref colonyArrivedTick, "colonyArrivedTick", -1);
            Scribe_Values.Look(ref alliesArrived, "alliesArrived", false);
            Scribe_Values.Look(ref arrivalPackPoints, "arrivalPackPoints", 0f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                attendees = attendees ?? new List<OutbreakAttendee>();
                held = held ?? new ThingOwner<Pawn>(this) { dontTickContents = true };
            }
        }
    }

    /// <summary>The attending factions' fighters hunt every hostile on the map, then
    /// leave once none is left. LordJob_AssistColony would leave after 25000 ticks
    /// whatever the fight, and never on victory.</summary>
    public class LordJob_BastleOutbreakAllies : LordJob
    {
        private Faction faction;
        private IntVec3 fallbackLocation;

        public LordJob_BastleOutbreakAllies() { }

        public LordJob_BastleOutbreakAllies(Faction faction, IntVec3 fallbackLocation)
        {
            this.faction = faction;
            this.fallbackLocation = fallbackLocation;
        }

        public override bool AddFleeToil => false;

        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph();
            var hunt = new LordToil_HuntEnemies(fallbackLocation);
            graph.AddToil(hunt);
            var exit = new LordToil_ExitMap(LocomotionUrgency.Walk);
            graph.AddToil(exit);
            var done = new Transition(hunt, exit);
            done.AddTrigger(new Trigger_TickCondition(() => !GenHostility.AnyHostileActiveThreatTo(Map, faction), 250));
            done.AddPreAction(new TransitionAction_Message("MessageFriendlyFightersLeaving".Translate(faction.def.pawnsPlural.CapitalizeFirst(), faction.Name)));
            done.AddPreAction(new TransitionAction_EnsureHaveExitDestination());
            done.AddPostAction(new TransitionAction_EndAllJobs());
            graph.AddTransition(done);
            return graph;
        }

        public override void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref fallbackLocation, "fallbackLocation");
        }
    }

    /// <summary>Spawns the outbreak around the map's centre in one cluster per alpha (one
    /// if none): the alphas the quest made, then copies of the strain up to the grown count,
    /// all manhunting for good. A site made without its quest makes its own strain.</summary>
    public class GenStep_BastleOutbreak : GenStep
    {
        public FloatRange defaultPointsRange = new FloatRange(600f, 900f);

        public override int SeedPart => 482915063;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (!GenStep_BastleLair.TryFindLairCenter(map, out IntVec3 center)) return;
            SitePart part = parms.sitePart;
            var outbreak = BastleOutbreakQuest.PartFor(map.Parent);
            Pawn strain;
            List<Pawn> alphas;
            float packPoints;
            bool ownStrain = false;
            if (outbreak != null)
            {
                strain = outbreak.Strain;
                packPoints = outbreak.PackPointsNow;
                alphas = outbreak.TakeAlphasAndFreeze();
            }
            else
            {
                var siteParms = part?.parms ?? new SitePartParams { threatPoints = defaultPointsRange.RandomInRange };
                if (siteParms.points <= 0f) siteParms.points = BastleOutbreakQuest.PackPoints(siteParms.threatPoints);
                packPoints = siteParms.points;
                float questPoints = siteParms.threatPoints / Mathf.Max(1f, BastleFrameworkMod.Settings.outbreakSize);
                strain = BastleOutbreakQuest.MakeStrain(siteParms.animalKind, packPoints, questPoints, map.Tile);
                ownStrain = true;
                alphas = new List<Pawn>();
                if (strain != null)
                    for (int i = BastleOutbreakQuest.AlphaCount(questPoints); i > 0; i--)
                        alphas.Add(BastleOutbreakQuest.MakeAlpha(strain, questPoints, map.Tile));
            }

            PawnKindDef kind = strain?.kindDef ?? part?.parms.animalKind;
            if (kind == null && !ManhunterPackGenStepUtility.TryGetAnimalsKind(packPoints, map.Tile, out kind)) return;
            int count = BastleHuntQuest.AnimalCount(kind, packPoints);

            var clusters = new List<IntVec3> { center };
            for (int i = 1; i < alphas.Count; i++)
                if (CellFinder.TryFindRandomCellNear(center, map, 18, x => GenStep_BastleLair.CellValidator(x, map) && map.reachability.CanReach(center, x, PathEndMode.OnCell, TraverseMode.NoPassClosedDoors), out IntVec3 c))
                    clusters.Add(c);

            var spawned = new List<(Pawn pawn, IntVec3 near)>();
            for (int i = 0; i < alphas.Count; i++) spawned.Add((alphas[i], clusters[i % clusters.Count]));
            for (int i = 0; i < count; i++)
                spawned.Add((BastleOutbreakQuest.CopyOfStrain(strain, kind, map.Tile), clusters[i % clusters.Count]));
            foreach (var (p, near) in spawned)
            {
                if (!CellFinder.TryFindRandomSpawnCellForPawnNear(near, map, out IntVec3 cell, 10, x => GenStep_BastleLair.CellValidator(x, map)))
                    cell = near;
                GenSpawn.Spawn(p, cell, map, Rot4.Random);
                BastleEvents.MakeManhunterNoExit(p);
            }
            if (ownStrain && strain != null) BastleEvents.Discard(strain);
        }
    }

    /// <summary>
    /// A strain the colony failed to stop, loose in the world for a while. While it lasts,
    /// IncidentWorker_BastleOutbreakSpread can bring a manhunter pack of it to a colony.
    /// The strain pawn is held here (never spawned); its packs join its discovery group, so
    /// what was learned at the site is still known. When it ends, other factions have
    /// cleaned it up.
    /// </summary>
    public class GameCondition_BastleOutbreak : GameCondition, IThingHolder
    {
        public ThingOwner<Pawn> held;

        public GameCondition_BastleOutbreak()
        {
            held = new ThingOwner<Pawn>(this) { dontTickContents = true };
        }

        public Pawn Strain => held.Count > 0 ? held[0] : null;

        public override string Label
            => Strain != null ? "BF_OutbreakConditionLabel".Translate(Strain.kindDef.label).ToString() : base.Label;

        public override string Description
            => Strain != null ? "BF_OutbreakConditionDesc".Translate(Strain.kindDef.GetLabelPlural()).ToString() : base.Description;

        public override void End()
        {
            Pawn strain = Strain;
            if (strain != null)
            {
                Messages.Message("BF_OutbreakCleanedUp".Translate(strain.kindDef.label), MessageTypeDefOf.NeutralEvent);
                held.Remove(strain);
                BastleEvents.Discard(strain);
            }
            base.End();
        }

        public IThingHolder ParentHolder => null;

        public ThingOwner GetDirectlyHeldThings() => held;

        public void GetChildHolders(List<IThingHolder> outChildren)
            => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref held, "held", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                held = held ?? new ThingOwner<Pawn>(this) { dontTickContents = true };
        }
    }

    /// <summary>A manhunter pack of an escaped strain: its kind, its traits, and whatever
    /// the colony already learned about it. Only while an uncontained outbreak whose kind
    /// can stand the map's season is active.</summary>
    public class IncidentWorker_BastleOutbreakSpread : IncidentWorker
    {
        public override float BaseChanceThisGame => base.BaseChanceThisGame * BastleFrameworkMod.Settings.outbreakSpreadFrequency;

        private static List<GameCondition_BastleOutbreak> Outbreaks(Map map)
        {
            var list = new List<GameCondition_BastleOutbreak>();
            foreach (var c in Find.World.gameConditionManager.ActiveConditions)
                if (c is GameCondition_BastleOutbreak o && o.Strain != null
                    && map.mapTemperature.SeasonAndOutdoorTemperatureAcceptableFor(o.Strain.def))
                    list.Add(o);
            return list;
        }

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!BastleWorld.Enabled(BastleWorldFeature.OutbreakQuest)) return false;
            Map map = (Map)parms.target;
            return Outbreaks(map).Count > 0 && BastleEvents.TryFindEntryCell(map, out _);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (!Outbreaks(map).TryRandomElement(out var outbreak)) return false;
            IntVec3 entry = parms.spawnCenter;
            if (!entry.IsValid && !BastleEvents.TryFindEntryCell(map, out entry)) return false;
            Pawn strain = outbreak.Strain;
            PawnKindDef kind = strain.kindDef;
            float countPoints = Mathf.Max(AggressiveAnimalIncidentUtility.MinPoints,
                BastleEvents.PointsOf(parms, map) * (1f - BastleFrameworkMod.Settings.outbreakTraitShare));
            int count = parms.pawnCount > 0 ? parms.pawnCount : AggressiveAnimalIncidentUtility.GetAnimalsCount(kind, countPoints);
            Rot4 rot = Rot4.FromAngleFlat((map.Center - entry).AngleFlat);
            var pack = new List<Pawn>();
            for (int i = 0; i < count; i++)
            {
                Pawn p = BastleOutbreakQuest.CopyOfStrain(strain, kind, map.Tile);
                GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(entry, map, 10), map, rot);
                QuestUtility.AddQuestTag(p, parms.questTag);
                BastleEvents.MakeManhunter(p);
                pack.Add(p);
            }
            SendStandardLetter(parms, pack, kind.label.Named("KIND"), kind.GetLabelPlural().Named("KINDPLURAL"));
            Find.TickManager.slower.SignalForceNormalSpeedShort();
            return true;
        }
    }
}
