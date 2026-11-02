using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// What makes a trait "seen in action". A trait can list several; any one of them
    /// reveals it, but only while the player can actually see the creature
    /// (BastleUtility.PlayerCanObserve). Code can always reveal a trait directly
    /// with BastleTrait.Discover(), which is what Manual is for: a trait with only
    /// Manual is revealed by its own traitClass, or by the content mod (study, autopsy).
    /// </summary>
    public enum BastleDiscoveryTrigger
    {
        Manual,
        /// <summary>The creature uses an attack granted by one of this trait's hediffs.</summary>
        UsesGrantedVerb,
        /// <summary>The creature casts one of this trait's abilities.</summary>
        UsesGrantedAbility,
        /// <summary>The creature uses any verb at all (attacks, casts).</summary>
        Attacks,
        /// <summary>The creature takes damage.</summary>
        TakesDamage,
        /// <summary>The creature dies.</summary>
        Dies,
        /// <summary>The creature eats something only this trait's diet lets it eat (an
        /// extraFoods thing, or a food type the trait adds that its race doesn't eat).</summary>
        EatsAddedFood,
        /// <summary>The creature is starving, finds nothing to eat, and food this trait's
        /// diet refuses (one its race would otherwise eat) is within reach.</summary>
        StarvesNearRefusedFood,
        /// <summary>The creature joins the player's faction: tamed, recruited, bought or
        /// rescued. For traits that only show once you live with it (meat yield, hunger).</summary>
        JoinsColony,
        /// <summary>The creature dodges a melee blow (vanilla's melee dodge).</summary>
        Dodges,
    }

    /// <summary>A hediff a trait adds to its creature, optionally on a body part.</summary>
    public class BastleTraitHediff
    {
        public HediffDef hediff;

        /// <summary>Body part to attach to. Null = whole body. The first matching
        /// non-missing part is used; if the creature has none, the hediff is skipped.</summary>
        public BodyPartDef part;

        public float severity = -1f;
    }

    public class BastleTraitDef : Def
    {
        // ---- the trait instance --------------------------------------------------

        /// <summary>Instance class: subclass BastleTrait for traits with behaviour or state.</summary>
        public Type traitClass = typeof(BastleTrait);

        // ---- generation ----------------------------------------------------------

        /// <summary>Relative weight among the traits that can roll at the same rarity.
        /// 0 = never rolled, only added by code.</summary>
        public float commonality = 1f;

        /// <summary>The trait's rarity. With scalesWithRarity it's the lowest it rolls at.</summary>
        public BastleRarity rarity = BastleRarity.Common;

        /// <summary>The trait can also roll at every higher rarity (up to MaxRarity), with
        /// its stage numbers and health factor scaled by the rarity strength table in the
        /// mod settings, relative to its own rarity. Its points and market value follow
        /// the rarity points table. rarityOverrides can change anything else.</summary>
        public bool scalesWithRarity;

        /// <summary>Changes at higher rarities: a better ability, a different look.</summary>
        public List<BastleRarityOverride> rarityOverrides;

        /// <summary>Points at the trait's own rarity, counted against the point target when
        /// rolling, and the base of its health and market value. Unset: the rarity's value
        /// from the mod settings. Negative: a drawback, which gives points back and only
        /// rolls through the drawback chance.</summary>
        public float pointCost = float.NaN;

        /// <summary>Market value added, as a fraction of the creature's own value (0.3 =
        /// +30%). Setting this or marketValueOffset replaces the price the trait would
        /// otherwise get from pointCost and the mod settings.</summary>
        public float? marketValuePercent;

        /// <summary>Flat market value added, in silver. See marketValuePercent.</summary>
        public float? marketValueOffset;

        /// <summary>Multiplies the creature's health (every body part's hit points) on top
        /// of the per-point bonus from the mod settings. 1.5 = half again as tough, 0.7 =
        /// frail. Applies before the trait is discovered. Each trait's factor is floored at
        /// 0.25, and so is the creature's total.</summary>
        public float healthScaleFactor = 1f;

        /// <summary>Draws the creature this much bigger (1.3 = 30% larger). Scales with
        /// rarity like healthScaleFactor. Drawing only: body size, hit box and food are
        /// unchanged. Applies before the trait is discovered (you can see the size).
        /// Not applied to humanlikes, whose zoomed-out drawing goes through a fixed-size
        /// render cache.</summary>
        public float drawSizeFactor = 1f;

        /// <summary>Damage dealt back to whoever hits the creature in melee.</summary>
        public BastleRetaliation retaliation;

        /// <summary>Heals the creature by part of the damage it deals.</summary>
        public BastleLifesteal lifesteal;

        /// <summary>Changes what it gives when shorn or milked.</summary>
        public BastleProducts products;

        /// <summary>A chance to shrug off a hit completely, and optionally send a shot
        /// back at whoever fired it.</summary>
        public BastleDeflect deflect;

        /// <summary>"When X happens, do Y": a stun on a hit, a frost blast when struck, a heal
        /// when downed. See BastleProc.</summary>
        public List<BastleProc> procs;

        /// <summary>Two traits sharing any exclusion tag cannot be on one creature.</summary>
        public List<string> exclusionTags;

        /// <summary>Free-form tags a content mod can filter rolls on ("Fire", "Deep", "Boss").</summary>
        public List<string> tags;

        // Which pawns may get it.
        public bool allowAnimals = true;
        public bool allowHumanlikes = true;
        public bool allowMechanoids = true;
        /// <summary>Anything that is none of the above (Anomaly entities, modded races).</summary>
        public bool allowOther = true;
        /// <summary>Only flesh races: no mechanoids or other non-flesh races.</summary>
        public bool requiresFlesh;
        /// <summary>Not on material creatures (BastleUtility.SetMaterial): for traits that
        /// work through meat, leather, blood, pain, toxins or rot, which a construct
        /// doesn't have. Removed if the creature later becomes a construct.</summary>
        public bool disallowConstructs;
        public float minBodySize = 0f;
        public float maxBodySize = 9999f;
        public List<ThingDef> allowedRaces;
        public List<ThingDef> disallowedRaces;

        // ---- discovery and naming -----------------------------------------------

        /// <summary>Starts identified (e.g. Flaming): shown in the name and tooltip from the start.</summary>
        public bool visible;

        public List<BastleDiscoveryTrigger> discoverWhen;

        /// <summary>Show a message when the player discovers this trait.</summary>
        public bool discoveryMessage = true;

        /// <summary>Word placed before the creature's kind once known ("venomous").</summary>
        [MustTranslate] public string namePrefix;

        /// <summary>Words placed after the creature's kind once known ("of the deep").</summary>
        [MustTranslate] public string nameSuffix;

        /// <summary>What a survivor would say about a creature with this trait, as a verb phrase
        /// in the third person with no full stop ("towers over the rest of its kind"). Quests
        /// put it after a subject: "Those who escaped it say it ...", "... one of the alphas ...".
        /// Without one, the quest names the trait's label instead.</summary>
        [MustTranslate] public string rumor;

        /// <summary>Order among prefixes/suffixes. Lower is further from the kind label.</summary>
        public int nameOrder;

        /// <summary>When a creature knows more naming traits than the "Most traits in a name"
        /// setting allows, higher priority wins a place first; ties go to the trait worth more
        /// points. Default 0. Only decides which words appear, not where (that is nameOrder).</summary>
        public int namePriority;

        // ---- inheritance ---------------------------------------------------------

        public bool heritable = true;

        /// <summary>Chance per carrying parent that a newborn inherits the trait.
        /// With both parents carrying it, the chances compound.</summary>
        public float inheritChance = 0.5f;

        // ---- effects -------------------------------------------------------------

        /// <summary>Merged with every other trait on the creature into the bastle hediff's
        /// single stage. See Hediff_Bastle.BuildStage for which fields merge.</summary>
        public HediffStage stage;

        /// <summary>Hediffs added alongside the trait (attacks via HediffComp_VerbGiver,
        /// anything a vanilla or modded HediffComp can do). Removed with the trait.</summary>
        public List<BastleTraitHediff> hediffs;

        /// <summary>Abilities granted while the trait is present.</summary>
        public List<AbilityDef> abilities;

        /// <summary>Optional AI for this trait, run every 30 ticks before the built-in
        /// combat casting (see JobGiver_BastleAbilities). Use it for abilities that need
        /// their own conditions, in or out of combat, e.g. a copy of vanilla's longjump
        /// escape branch. Runs whether or not the trait is discovered.</summary>
        public ThinkTreeDef aiThinkTree;

        /// <summary>Changes what the creature eats, forages and hunts. See BastleDietProps.</summary>
        public BastleDietProps diet;

        // ---- visuals -------------------------------------------------------------

        /// <summary>Glow overlays drawn from the creature's own body texture.</summary>
        public List<BastleOverlay> overlays;

        /// <summary>Particle effecters kept running on the creature (embers, smoke).</summary>
        public List<EffecterDef> effecters;

        /// <summary>Whether its looks show before it is discovered, as a hint (null: the
        /// setting "Looks hint at unknown traits"). A surface's or an event visual's own
        /// beforeIdentified overrides this. Showing reveals nothing.</summary>
        public bool? visualsBeforeIdentified;

        /// <summary>Older name: true is visualsBeforeIdentified true. Its false can't be told
        /// from unset, so it can't keep looks secret; visualsBeforeIdentified false does.</summary>
        public bool showVisualsWhenHidden;

        public bool? VisualsBeforeIdentified => showVisualsWhenHidden ? true : visualsBeforeIdentified;

        /// <summary>Flickering fire sprites drawn on the creature's body.</summary>
        public List<BastleFlames> flames;

        /// <summary>Moving effects masked to the creature's own shape: flames crawling up
        /// it, a sheen sweeping across it, glints. Made at runtime from its texture.</summary>
        public List<BastleSurface> surfaces;

        /// <summary>Scale effecters (spread and particle size) with the square root of
        /// body size, so a bear's embers cover a bear and a rat's cover a rat.</summary>
        public bool scaleEffectersWithBodySize = true;

        /// <summary>Real light on the map, like a lamp that follows the creature. Always on
        /// while the creature has the trait, discovered or not: a light you can see is no
        /// secret. Traits that glow are best made visible.</summary>
        public BastleGlow glow;

        /// <summary>One-off effects when something happens to it: a hit it lands, a blow it
        /// takes, a dodge, a heal, a deflected shot. Shown under the same rule as the other
        /// visuals (BastleTrait.ShowsVisual), with the Event effects setting.</summary>
        public List<BastleEventVisual> eventVisuals;

        /// <summary>Faded copies of it left behind while it runs.</summary>
        public BastleAfterimages afterimages;

        public bool HasVisuals => HasRenderVisuals || !effecters.NullOrEmpty() || glow != null || afterimages != null;
        public bool HasRenderVisuals => !overlays.NullOrEmpty() || !flames.NullOrEmpty() || !surfaces.NullOrEmpty();

        // ---- rarity --------------------------------------------------------------

        public float BasePoints => float.IsNaN(pointCost) ? rarity.Points() : pointCost;

        /// <summary>Abilities (and the AI to use them) are all this trait does: no stage,
        /// hediffs, code, combat effects, diet, size, health, visuals or rarity overrides.
        /// Such a trait isn't added where other traits already grant every one of its
        /// abilities (BastleUtility.CanAddTrait).</summary>
        public bool OnlyGrantsAbilities =>
            !abilities.NullOrEmpty() && stage == null && hediffs.NullOrEmpty() && traitClass == typeof(BastleTrait)
            && rarityOverrides.NullOrEmpty() && retaliation == null && lifesteal == null && products == null
            && deflect == null && procs.NullOrEmpty() && eventVisuals.NullOrEmpty() && diet == null && healthScaleFactor == 1f
            && drawSizeFactor == 1f && !HasVisuals;

        public bool IsDrawback => BasePoints < 0f;

        /// <summary>Tag for traits worth something mainly once the creature is tamed. They
        /// get their own roll (BastleRolling) and their points add no health.</summary>
        public const string UtilityTag = "Utility";

        public bool IsUtility => tags != null && tags.Contains(UtilityTag);

        /// <summary>Points when rolled at a rarity: a scaling trait follows the settings'
        /// points table relative to its own rarity.</summary>
        public float PointsAt(BastleRarity at)
        {
            float basePts = BasePoints;
            if (!scalesWithRarity || at == rarity) return basePts;
            float own = rarity.Points();
            return own > 0.0001f ? basePts * at.Points() / own : basePts;
        }

        /// <summary>A fixed price (marketValuePercent/Offset) grows with the points.</summary>
        public float ValueScaleAt(BastleRarity at)
        {
            float own = BasePoints;
            return scalesWithRarity && Math.Abs(own) > 0.0001f ? PointsAt(at) / own : 1f;
        }

        /// <summary>The override in effect at a rarity, or null.</summary>
        public BastleRarityOverride OverrideAt(BastleRarity at)
        {
            if (rarityOverrides == null) return null;
            BastleRarityOverride best = null;
            foreach (var o in rarityOverrides)
                if (o.minRarity <= at && (best == null || o.minRarity > best.minRarity)) best = o;
            return best;
        }

        /// <summary>Strength multiplier on the stage at a rarity, relative to the rarity the
        /// stage was written for (the trait's own, or the override's minRarity).</summary>
        public float StrengthAt(BastleRarity at)
        {
            if (!scalesWithRarity) return 1f;
            var o = OverrideAt(at);
            BastleRarity from = o?.stage != null ? o.minRarity : rarity;
            float baseMult = from.Strength();
            return baseMult > 0.0001f ? at.Strength() / baseMult : 1f;
        }

        public HediffStage StageAt(BastleRarity at) => OverrideAt(at)?.stage ?? stage;

        [Unsaved] private bool[] fitsCache;
        [Unsaved] private int fitsFor = -1;

        /// <summary>A rarity is offered only when no factor would be pushed onto the floor.
        /// There can be gaps: a trait whose factors run out at Rare can still roll at a
        /// Legendary override, whose stage is scaled from its own rarity.</summary>
        public bool FitsAt(BastleRarity at)
        {
            if (!scalesWithRarity) return at == rarity;
            if (at < rarity) return false;
            if (at == rarity) return true;
            if (fitsFor != BastleSettings.Version || fitsCache == null)
            {
                fitsCache = new bool[BastleRarityUtility.Count];
                for (var r = rarity; r <= BastleRarity.Legendary; r++)
                    fitsCache[(int)r] = r == rarity || ComputeFits(r);
                fitsFor = BastleSettings.Version;
            }
            return fitsCache[(int)at];
        }

        private bool ComputeFits(BastleRarity at)
        {
            float s = StrengthAt(at);
            // Unscaled (an override at its own rarity): the stage is used as written.
            if (Math.Abs(s - 1f) >= 0.0001f
                && 1f + (BastleStageScaler.LowestFactor(StageAt(at)) - 1f) * s < BastleStageScaler.MinFactor) return false;
            var oh = OverrideAt(at)?.healthScaleFactor;
            float h = oh ?? healthScaleFactor;
            float hs = StrengthFor(oh != null, at);
            return h >= 1f || Math.Abs(hs - 1f) < 0.0001f || 1f + (h - 1f) * hs >= BastleStageScaler.MinFactor;
        }

        /// <summary>Strength at a rarity relative to another (the rarity a value was written
        /// for). 1 for traits that don't scale.</summary>
        public float StrengthFrom(BastleRarity from, BastleRarity at)
        {
            if (!scalesWithRarity) return 1f;
            float b = from.Strength();
            return b > 0.0001f ? at.Strength() / b : 1f;
        }

        /// <summary>Strength for a value that the override at this rarity either sets
        /// (scaled from the override's minRarity) or leaves to the def (scaled from the
        /// trait's own rarity).</summary>
        public float StrengthFor(bool overrideSetsIt, BastleRarity at)
        {
            var o = overrideSetsIt ? OverrideAt(at) : null;
            return StrengthFrom(o?.minRarity ?? rarity, at);
        }

        /// <summary>The highest rarity the trait can roll at. Lower ones may be skipped
        /// (see FitsAt), so loop with CanAppearAt.</summary>
        public BastleRarity MaxRarity
        {
            get
            {
                if (!scalesWithRarity) return rarity;
                for (var r = BastleRarity.Legendary; r > rarity; r--)
                    if (FitsAt(r)) return r;
                return rarity;
            }
        }

        public bool CanAppearAt(BastleRarity at) => FitsAt(at);

        /// <summary>The nearest rarity at or below this one that the trait can roll at.</summary>
        public BastleRarity ClampRarity(BastleRarity at)
        {
            if (!scalesWithRarity || at <= rarity) return rarity;
            for (var r = at; r > rarity; r--)
                if (FitsAt(r)) return r;
            return rarity;
        }

        /// <summary>The trait's draw size factor at a rarity (1 = unchanged).</summary>
        public float DrawSizeAt(BastleRarity at)
        {
            var o = OverrideAt(at)?.drawSizeFactor;
            float f = o ?? drawSizeFactor;
            return f == 1f ? 1f : UnityEngine.Mathf.Max(0.25f, 1f + (f - 1f) * StrengthFor(o != null, at));
        }

        // ---- cached --------------------------------------------------------------

        [Unsaved] private bool? ticksCached;

        /// <summary>True when traitClass overrides TickInterval. The bastle hediff only
        /// iterates its traits on tick when at least one of them ticks.</summary>
        public bool Ticks
        {
            get
            {
                if (ticksCached == null)
                {
                    var m = traitClass?.GetMethod(nameof(BastleTrait.TickInterval), new[] { typeof(int) });
                    ticksCached = m != null && m.DeclaringType != typeof(BastleTrait);
                }
                return ticksCached.Value;
            }
        }

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            MarkProcVisuals(procs);
            if (rarityOverrides != null)
                foreach (var o in rarityOverrides)
                    MarkProcVisuals(o?.procs);
            // Diet traits are revealed by what the creature eats unless the def says otherwise.
            bool hasDiet = diet != null;
            if (rarityOverrides != null)
                foreach (var o in rarityOverrides)
                    hasDiet |= o?.diet != null;
            if (hasDiet && !visible && discoverWhen == null)
                discoverWhen = new List<BastleDiscoveryTrigger>
                    { BastleDiscoveryTrigger.EatsAddedFood, BastleDiscoveryTrigger.StarvesNearRefusedFood };
        }

        // A proc's own visuals play when it goes off, whatever their XML says.
        private static void MarkProcVisuals(List<BastleProc> list)
        {
            if (list == null) return;
            foreach (var p in list)
                if (p?.visuals != null)
                    foreach (var v in p.visuals)
                        if (v != null) v.on = BastleVisualEvent.Proc;
        }

        public bool DiscoveredBy(BastleDiscoveryTrigger trigger)
            => discoverWhen != null && discoverWhen.Contains(trigger);

        public bool ConflictsWith(BastleTraitDef other)
        {
            if (other == this) return true;
            if (exclusionTags == null || other.exclusionTags == null) return false;
            for (int i = 0; i < exclusionTags.Count; i++)
                if (other.exclusionTags.Contains(exclusionTags[i])) return true;
            return false;
        }

        /// <summary>Race and body filters only; conflicts with existing traits are
        /// checked by BastleUtility.CanAddTrait.</summary>
        public virtual bool AllowedOn(Pawn pawn)
        {
            var race = pawn.RaceProps;
            if (race.Humanlike) { if (!allowHumanlikes) return false; }
            else if (race.IsMechanoid) { if (!allowMechanoids) return false; }
            else if (race.Animal) { if (!allowAnimals) return false; }
            else if (!allowOther) return false;

            if (requiresFlesh && !race.IsFlesh) return false;
            if (!ProductsFit(products, pawn)) return false;
            if (rarityOverrides != null)
                foreach (var o in rarityOverrides)
                    if (!ProductsFit(o.products, pawn)) return false;
            if (race.baseBodySize < minBodySize || race.baseBodySize > maxBodySize) return false;
            if (allowedRaces != null && !allowedRaces.Contains(pawn.def)) return false;
            if (disallowedRaces != null && disallowedRaces.Contains(pawn.def)) return false;
            return true;
        }

        private static bool ProductsFit(BastleProducts p, Pawn pawn)
        {
            if (p == null) return true;
            if (p.ChangesWool && pawn.def.GetCompProperties<CompProperties_Shearable>() == null) return false;
            if (p.ChangesMilk && pawn.def.GetCompProperties<CompProperties_Milkable>() == null) return false;
            return true;
        }

        private static IEnumerable<string> ProcErrors(List<BastleProc> procs, List<BastleEventVisual> visuals, string owner)
        {
            if (procs != null)
                for (int i = 0; i < procs.Count; i++)
                {
                    if (procs[i] == null) { yield return owner + "procs has an empty entry"; continue; }
                    foreach (var e in procs[i].ConfigErrors(owner + "proc " + i)) yield return e;
                    if (procs[i].visuals != null)
                        foreach (var v in procs[i].visuals)
                            foreach (var e in v.ConfigErrors(owner + "proc " + i + " visual")) yield return e;
                }
            if (visuals != null)
                foreach (var v in visuals)
                {
                    if (v.on == BastleVisualEvent.Proc) yield return owner + "eventVisuals can't play on Proc; put them in the proc's own visuals";
                    foreach (var e in v.ConfigErrors(owner + "event visual")) yield return e;
                }
        }

        // Quests write the subject before it and the full stop after it.
        private static string RumorError(string phrase)
        {
            if (phrase.NullOrEmpty()) return null;
            if (char.IsUpper(phrase[0]) || phrase.TrimEnd().EndsWith("."))
                return $"rumor \"{phrase}\" should be a verb phrase that starts in lower case and has no full stop (\"towers over the rest of its kind\")";
            return null;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (traitClass == null || !typeof(BastleTrait).IsAssignableFrom(traitClass))
                yield return $"traitClass {traitClass} is not a BastleTrait";
            if (commonality < 0f) yield return "commonality is negative";
            if (RumorError(rumor) is string re) yield return re;
            if (rarityOverrides != null)
            {
                if (!scalesWithRarity) yield return "has rarityOverrides but doesn't scale with rarity, so it only ever rolls at its own rarity and the overrides are never used";
                foreach (var o in rarityOverrides)
                {
                    if (RumorError(o.rumor) is string ore) yield return $"rarityOverride at {o.minRarity}: {ore}";
                    if (o.minRarity <= rarity) yield return $"rarityOverride at {o.minRarity} is not above the trait's own rarity ({rarity}); set the base fields instead";
                    if (o.hediffs != null && !visible)
                        foreach (var h in o.hediffs)
                            if (h.hediff != null && (h.hediff.stages.NullOrEmpty() || h.hediff.stages.Any(s => s.becomeVisible)))
                                yield return $"rarityOverride hediff {h.hediff.defName} will show in the health tab before the trait is discovered. Give it a stage with <becomeVisible>false</becomeVisible>.";
                }
            }
            if (hediffs != null)
                foreach (var h in hediffs)
                {
                    if (h.hediff == null) { yield return "hediffs entry with no hediff"; continue; }
                    if (!visible && (h.hediff.stages.NullOrEmpty() || h.hediff.stages.Any(s => s.becomeVisible)))
                        yield return $"hediff {h.hediff.defName} will show in the health tab before the trait is discovered. Give it a stage with <becomeVisible>false</becomeVisible>; discovery makes it visible.";
                }
            if (diet != null && diet.hunts == true && (diet.removeFoodTypes & FoodTypeFlags.Corpse) != 0)
                yield return "diet hunts but removes Corpse: removing Corpse always stops hunting (vanilla's hunt job eats the kill without checking the diet), so hunts is ignored";
            if (diet?.extraFoods != null)
                foreach (var f in diet.extraFoods)
                {
                    if (f.thing == null) yield return "diet extraFoods entry with no thing";
                    else if (!f.thing.IsNutritionGivingIngestible && f.nutrition <= 0f)
                        yield return $"diet extraFoods {f.thing.defName} is not food, so it needs a nutrition above 0";
                }
            if (retaliation != null && retaliation.damageDef == null) yield return "retaliation has no damageDef";
            foreach (var e in ProcErrors(procs, eventVisuals, "")) yield return e;
            if (rarityOverrides != null)
                foreach (var o in rarityOverrides)
                    foreach (var e in ProcErrors(o.procs, o.eventVisuals, $"rarityOverride at {o.minRarity}: ")) yield return e;
            if (surfaces != null)
                foreach (var surface in surfaces)
                    foreach (var e in surface.ConfigErrors()) yield return e;
            if (rarityOverrides != null)
                foreach (var o in rarityOverrides)
                    if (o.surfaces != null)
                        foreach (var surface in o.surfaces)
                            foreach (var e in surface.ConfigErrors()) yield return $"rarityOverride at {o.minRarity}: {e}";
            if (GetModExtension<SheddingParams>()?.filth is ThingDef filth && !typeof(Filth).IsAssignableFrom(filth.thingClass))
                yield return $"SheddingParams filth {filth.defName} is not a filth (its thingClass is {filth.thingClass?.Name})";
            if (traitClass != null && typeof(BastleTrait_Aura).IsAssignableFrom(traitClass) && GetModExtension<AuraParams>() == null)
                yield return "traitClass is BastleTrait_Aura but there is no AuraParams in modExtensions";
            if (!visible && discoverWhen.NullOrEmpty() && traitClass == typeof(BastleTrait))
                yield return "not visible and has no discoverWhen: only code can ever reveal it (add <li>Manual</li> to discoverWhen to silence this)";
        }
    }
}
