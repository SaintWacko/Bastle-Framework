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

        /// <summary>Relative weight when rolling. 0 = never rolled, only added by code.</summary>
        public float commonality = 1f;

        /// <summary>Budget cost. Negative values are drawbacks that give points back.</summary>
        public float pointCost = 1f;

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
            if (race.baseBodySize < minBodySize || race.baseBodySize > maxBodySize) return false;
            if (allowedRaces != null && !allowedRaces.Contains(pawn.def)) return false;
            if (disallowedRaces != null && disallowedRaces.Contains(pawn.def)) return false;
            return true;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (traitClass == null || !typeof(TitanTrait).IsAssignableFrom(traitClass))
                yield return $"traitClass {traitClass} is not a TitanTrait";
            if (commonality < 0f) yield return "commonality is negative";
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
            if (!visible && discoverWhen.NullOrEmpty() && traitClass == typeof(TitanTrait))
                yield return "not visible and has no discoverWhen: only code can ever reveal it (add <li>Manual</li> to discoverWhen to silence this)";
        }
    }
}
