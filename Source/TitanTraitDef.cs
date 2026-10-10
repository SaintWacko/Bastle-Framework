using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// What makes a trait "seen in action". A trait can list several; any one of them
    /// reveals it, but only while the player can actually see the creature
    /// (TitanUtility.PlayerCanObserve). Code can always reveal a trait directly
    /// with TitanTrait.Discover(), which is what Manual is for: a trait with only
    /// Manual is revealed by its own traitClass, or by the content mod (study, autopsy).
    /// </summary>
    public enum TitanDiscoveryTrigger
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
    }

    /// <summary>A hediff a trait adds to its creature, optionally on a body part.</summary>
    public class TitanTraitHediff
    {
        public HediffDef hediff;

        /// <summary>Body part to attach to. Null = whole body. The first matching
        /// non-missing part is used; if the creature has none, the hediff is skipped.</summary>
        public BodyPartDef part;

        public float severity = -1f;
    }

    public class TitanTraitDef : Def
    {
        // ---- the trait instance --------------------------------------------------

        /// <summary>Instance class: subclass TitanTrait for traits with behaviour or state.</summary>
        public Type traitClass = typeof(TitanTrait);

        // ---- generation ----------------------------------------------------------

        /// <summary>Relative weight among the traits that can roll at the same rarity.
        /// 0 = never rolled, only added by code.</summary>
        public float commonality = 1f;

        /// <summary>The trait's rarity. With scalesWithRarity it's the lowest it rolls at.</summary>
        public TitanRarity rarity = TitanRarity.Common;

        /// <summary>The trait can also roll at every higher rarity (up to MaxRarity), with
        /// its stage numbers and health factor scaled by the rarity strength table in the
        /// mod settings, relative to its own rarity. Its points and market value follow
        /// the rarity points table. rarityOverrides can change anything else.</summary>
        public bool scalesWithRarity;

        /// <summary>Changes at higher rarities: a better ability, a different look.</summary>
        public List<TitanRarityOverride> rarityOverrides;

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
        public TitanRetaliation retaliation;

        /// <summary>Heals the creature by part of the damage it deals.</summary>
        public TitanLifesteal lifesteal;

        /// <summary>Changes what it gives when shorn or milked.</summary>
        public TitanProducts products;

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
        /// <summary>Not on material creatures (TitanUtility.SetMaterial): for traits that
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

        public List<TitanDiscoveryTrigger> discoverWhen;

        /// <summary>Show a message when the player discovers this trait.</summary>
        public bool discoveryMessage = true;

        /// <summary>Word placed before the creature's kind once known ("venomous").</summary>
        [MustTranslate] public string namePrefix;

        /// <summary>Words placed after the creature's kind once known ("of the deep").</summary>
        [MustTranslate] public string nameSuffix;

        /// <summary>Order among prefixes/suffixes. Lower is further from the kind label.</summary>
        public int nameOrder;

        // ---- inheritance ---------------------------------------------------------

        public bool heritable = true;

        /// <summary>Chance per carrying parent that a newborn inherits the trait.
        /// With both parents carrying it, the chances compound.</summary>
        public float inheritChance = 0.5f;

        // ---- effects -------------------------------------------------------------

        /// <summary>Merged with every other trait on the creature into the titan hediff's
        /// single stage. See Hediff_Titan.BuildStage for which fields merge.</summary>
        public HediffStage stage;

        /// <summary>Hediffs added alongside the trait (attacks via HediffComp_VerbGiver,
        /// anything a vanilla or modded HediffComp can do). Removed with the trait.</summary>
        public List<TitanTraitHediff> hediffs;

        /// <summary>Abilities granted while the trait is present.</summary>
        public List<AbilityDef> abilities;

        /// <summary>Optional AI for this trait, run every 30 ticks before the built-in
        /// combat casting (see JobGiver_TitanAbilities). Use it for abilities that need
        /// their own conditions, in or out of combat, e.g. a copy of vanilla's longjump
        /// escape branch. Runs whether or not the trait is discovered.</summary>
        public ThinkTreeDef aiThinkTree;

        /// <summary>Changes what the creature eats, forages and hunts. See TitanDietProps.</summary>
        public TitanDietProps diet;

        // ---- visuals -------------------------------------------------------------

        /// <summary>Glow overlays drawn from the creature's own body texture.</summary>
        public List<TitanOverlay> overlays;

        /// <summary>Particle effecters kept running on the creature (embers, smoke).</summary>
        public List<EffecterDef> effecters;

        /// <summary>Show overlays and effecters before the trait is discovered. They
        /// give the trait away, so this is off by default.</summary>
        public bool showVisualsWhenHidden;

        /// <summary>Flickering fire sprites drawn on the creature's body.</summary>
        public List<TitanFlames> flames;

        /// <summary>Scale effecters (spread and particle size) with the square root of
        /// body size, so a bear's embers cover a bear and a rat's cover a rat.</summary>
        public bool scaleEffectersWithBodySize = true;

        /// <summary>Real light on the map, like a lamp that follows the creature. Always on
        /// while the creature has the trait, discovered or not: a light you can see is no
        /// secret. Traits that glow are best made visible.</summary>
        public TitanGlow glow;

        public bool HasVisuals => !overlays.NullOrEmpty() || !effecters.NullOrEmpty() || !flames.NullOrEmpty() || glow != null;
        public bool HasRenderVisuals => !overlays.NullOrEmpty() || !flames.NullOrEmpty();

        // ---- rarity --------------------------------------------------------------

        public float BasePoints => float.IsNaN(pointCost) ? rarity.Points() : pointCost;

        public bool IsDrawback => BasePoints < 0f;

        /// <summary>Points when rolled at a rarity: a scaling trait follows the settings'
        /// points table relative to its own rarity.</summary>
        public float PointsAt(TitanRarity at)
        {
            float basePts = BasePoints;
            if (!scalesWithRarity || at == rarity) return basePts;
            float own = rarity.Points();
            return own > 0.0001f ? basePts * at.Points() / own : basePts;
        }

        /// <summary>A fixed price (marketValuePercent/Offset) grows with the points.</summary>
        public float ValueScaleAt(TitanRarity at)
        {
            float own = BasePoints;
            return scalesWithRarity && Math.Abs(own) > 0.0001f ? PointsAt(at) / own : 1f;
        }

        /// <summary>The override in effect at a rarity, or null.</summary>
        public TitanRarityOverride OverrideAt(TitanRarity at)
        {
            if (rarityOverrides == null) return null;
            TitanRarityOverride best = null;
            foreach (var o in rarityOverrides)
                if (o.minRarity <= at && (best == null || o.minRarity > best.minRarity)) best = o;
            return best;
        }

        /// <summary>Strength multiplier on the stage at a rarity, relative to the rarity the
        /// stage was written for (the trait's own, or the override's minRarity).</summary>
        public float StrengthAt(TitanRarity at)
        {
            if (!scalesWithRarity) return 1f;
            var o = OverrideAt(at);
            TitanRarity from = o?.stage != null ? o.minRarity : rarity;
            float baseMult = from.Strength();
            return baseMult > 0.0001f ? at.Strength() / baseMult : 1f;
        }

        public HediffStage StageAt(TitanRarity at) => OverrideAt(at)?.stage ?? stage;

        [Unsaved] private bool[] fitsCache;
        [Unsaved] private int fitsFor = -1;

        /// <summary>A rarity is offered only when no factor would be pushed onto the floor.
        /// There can be gaps: a trait whose factors run out at Rare can still roll at a
        /// Legendary override, whose stage is scaled from its own rarity.</summary>
        public bool FitsAt(TitanRarity at)
        {
            if (!scalesWithRarity) return at == rarity;
            if (at < rarity) return false;
            if (at == rarity) return true;
            if (fitsFor != TitanSettings.Version || fitsCache == null)
            {
                fitsCache = new bool[TitanRarityUtility.Count];
                for (var r = rarity; r <= TitanRarity.Legendary; r++)
                    fitsCache[(int)r] = r == rarity || ComputeFits(r);
                fitsFor = TitanSettings.Version;
            }
            return fitsCache[(int)at];
        }

        private bool ComputeFits(TitanRarity at)
        {
            float s = StrengthAt(at);
            // Unscaled (an override at its own rarity): the stage is used as written.
            if (Math.Abs(s - 1f) >= 0.0001f
                && 1f + (TitanStageScaler.LowestFactor(StageAt(at)) - 1f) * s < TitanStageScaler.MinFactor) return false;
            var oh = OverrideAt(at)?.healthScaleFactor;
            float h = oh ?? healthScaleFactor;
            float hs = StrengthFor(oh != null, at);
            return h >= 1f || Math.Abs(hs - 1f) < 0.0001f || 1f + (h - 1f) * hs >= TitanStageScaler.MinFactor;
        }

        /// <summary>The highest rarity the trait can roll at. Lower ones may be skipped
        /// (see FitsAt), so loop with CanAppearAt.</summary>
        /// <summary>Strength at a rarity relative to another (the rarity a value was written
        /// for). 1 for traits that don't scale.</summary>
        public float StrengthFrom(TitanRarity from, TitanRarity at)
        {
            if (!scalesWithRarity) return 1f;
            float b = from.Strength();
            return b > 0.0001f ? at.Strength() / b : 1f;
        }

        /// <summary>Strength for a value that the override at this rarity either sets
        /// (scaled from the override's minRarity) or leaves to the def (scaled from the
        /// trait's own rarity).</summary>
        public float StrengthFor(bool overrideSetsIt, TitanRarity at)
        {
            var o = overrideSetsIt ? OverrideAt(at) : null;
            return StrengthFrom(o?.minRarity ?? rarity, at);
        }

        public TitanRarity MaxRarity
        {
            get
            {
                if (!scalesWithRarity) return rarity;
                for (var r = TitanRarity.Legendary; r > rarity; r--)
                    if (FitsAt(r)) return r;
                return rarity;
            }
        }

        public bool CanAppearAt(TitanRarity at) => FitsAt(at);

        /// <summary>The nearest rarity at or below this one that the trait can roll at.</summary>
        public TitanRarity ClampRarity(TitanRarity at)
        {
            if (!scalesWithRarity || at <= rarity) return rarity;
            for (var r = at; r > rarity; r--)
                if (FitsAt(r)) return r;
            return rarity;
        }

        /// <summary>The trait's draw size factor at a rarity (1 = unchanged).</summary>
        public float DrawSizeAt(TitanRarity at)
        {
            var o = OverrideAt(at)?.drawSizeFactor;
            float f = o ?? drawSizeFactor;
            return f == 1f ? 1f : UnityEngine.Mathf.Max(0.25f, 1f + (f - 1f) * StrengthFor(o != null, at));
        }

        // ---- cached --------------------------------------------------------------

        [Unsaved] private bool? ticksCached;

        /// <summary>True when traitClass overrides TickInterval. The titan hediff only
        /// iterates its traits on tick when at least one of them ticks.</summary>
        public bool Ticks
        {
            get
            {
                if (ticksCached == null)
                {
                    var m = traitClass.GetMethod(nameof(TitanTrait.TickInterval), new[] { typeof(int) });
                    ticksCached = m != null && m.DeclaringType != typeof(TitanTrait);
                }
                return ticksCached.Value;
            }
        }

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            // Diet traits are revealed by what the creature eats unless the def says otherwise.
            bool hasDiet = diet != null;
            if (rarityOverrides != null)
                foreach (var o in rarityOverrides)
                    hasDiet |= o?.diet != null;
            if (hasDiet && !visible && discoverWhen == null)
                discoverWhen = new List<TitanDiscoveryTrigger>
                    { TitanDiscoveryTrigger.EatsAddedFood, TitanDiscoveryTrigger.StarvesNearRefusedFood };
        }

        public bool DiscoveredBy(TitanDiscoveryTrigger trigger)
            => discoverWhen != null && discoverWhen.Contains(trigger);

        public bool ConflictsWith(TitanTraitDef other)
        {
            if (other == this) return true;
            if (exclusionTags == null || other.exclusionTags == null) return false;
            for (int i = 0; i < exclusionTags.Count; i++)
                if (other.exclusionTags.Contains(exclusionTags[i])) return true;
            return false;
        }

        /// <summary>Race and body filters only; conflicts with existing traits are
        /// checked by TitanUtility.CanAddTrait.</summary>
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

        private static bool ProductsFit(TitanProducts p, Pawn pawn)
        {
            if (p == null) return true;
            if (p.ChangesWool && pawn.def.GetCompProperties<CompProperties_Shearable>() == null) return false;
            if (p.ChangesMilk && pawn.def.GetCompProperties<CompProperties_Milkable>() == null) return false;
            return true;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (traitClass == null || !typeof(TitanTrait).IsAssignableFrom(traitClass))
                yield return $"traitClass {traitClass} is not a TitanTrait";
            if (commonality < 0f) yield return "commonality is negative";
            if (rarityOverrides != null)
            {
                if (!scalesWithRarity) yield return "has rarityOverrides but doesn't scale with rarity, so it only ever rolls at its own rarity and the overrides are never used";
                foreach (var o in rarityOverrides)
                {
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
            if (!visible && discoverWhen.NullOrEmpty() && traitClass == typeof(TitanTrait))
                yield return "not visible and has no discoverWhen: only code can ever reveal it (add <li>Manual</li> to discoverWhen to silence this)";
        }
    }
}
