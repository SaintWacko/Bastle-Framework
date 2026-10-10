using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// One trait on one creature. Saved inside Hediff_Titan, so subclasses can keep
    /// per-creature state by overriding ExposeData. Hooks are called by the titan
    /// hediff; override only what you need (TickInterval is only called at all when
    /// some trait on the creature overrides it).
    /// </summary>
    public class TitanTrait : IExposable
    {
        public TitanTraitDef def;
        public bool discovered;

        /// <summary>Granted by the creature's material (TitanMaterialExtension.traits):
        /// removed when the material changes, and not inherited on its own.</summary>
        public bool fromMaterial;

        /// <summary>Set by the owning hediff after creation and after load.</summary>
        public Hediff_Titan titan;

        public Pawn Pawn => titan?.pawn;

        // Saved as an int; -1 (saves from before rarity) means the def's own rarity.
        private int rarityValue = -1;

        /// <summary>The rarity this trait rolled at. Only scaling traits differ from the def's.
        /// Clamped when set, not when read: MaxRarity depends on the settings, and a trait
        /// whose rarity changed after its hediffs and abilities were granted would leave the
        /// old ones behind. Factors are floored when scaled, so any rarity is safe.</summary>
        public TitanRarity Rarity
        {
            get => rarityValue < 0 ? def.rarity : (TitanRarity)rarityValue;
            internal set { rarityValue = (int)def.ClampRarity(value); scaledStage = null; }
        }

        internal void SetRarityExact(TitanRarity r)
        {
            rarityValue = (int)r;
            scaledStage = null;
        }

        /// <summary>After load: keeps the saved rarity inside the def's fixed bounds (a def
        /// update may have raised its rarity or stopped it scaling). Deliberately not
        /// MaxRarity, which follows the settings and would quietly lower saved traits.</summary>
        internal void ClampLoadedRarity()
        {
            if (rarityValue < 0) return;
            if (!def.scalesWithRarity) rarityValue = (int)def.rarity;
            else rarityValue = Mathf.Clamp(rarityValue, (int)def.rarity, (int)TitanRarity.Legendary);
        }

        /// <summary>The rarity override in effect, or null.</summary>
        public TitanRarityOverride Override => def.OverrideAt(Rarity);

        [Unsaved] private HediffStage scaledStage;
        [Unsaved] private int scaledFor = -1;

        /// <summary>This trait's stage at its rarity (override applied, numbers scaled).
        /// Rebuilt when the settings change.</summary>
        public HediffStage Stage
        {
            get
            {
                if (scaledFor != TitanSettings.Version || scaledStage == null)
                {
                    scaledStage = TitanStageScaler.Scale(def.StageAt(Rarity), def.StrengthAt(Rarity));
                    scaledFor = TitanSettings.Version;
                }
                return scaledStage;
            }
        }

        public float Points => def.PointsAt(Rarity);

        public float HealthScaleFactor
        {
            get
            {
                float f = Override?.healthScaleFactor ?? def.healthScaleFactor;
                return f == 1f ? 1f : Mathf.Max(0f, 1f + (f - 1f) * def.StrengthAt(Rarity));
            }
        }

        public List<TitanTraitHediff> Hediffs => Override?.hediffs ?? def.hediffs;
        public List<AbilityDef> Abilities => Override?.abilities ?? def.abilities;
        public ThinkTreeDef AiThinkTree => Override?.aiThinkTree ?? def.aiThinkTree;
        public TitanDietProps Diet => Override?.diet ?? def.diet;
        public List<TitanOverlay> Overlays => Override?.overlays ?? def.overlays;
        public List<EffecterDef> Effecters => Override?.effecters ?? def.effecters;
        public List<TitanFlames> Flames => Override?.flames ?? def.flames;
        public TitanGlow Glow => Override?.glow ?? def.glow;
        public string NamePrefix => Override?.namePrefix ?? def.namePrefix;
        public string NameSuffix => Override?.nameSuffix ?? def.nameSuffix;

        public bool HasVisuals => !Overlays.NullOrEmpty() || !Effecters.NullOrEmpty() || !Flames.NullOrEmpty() || Glow != null;
        public bool HasRenderVisuals => !Overlays.NullOrEmpty() || !Flames.NullOrEmpty();

        /// <summary>Whether this trait's overlays and effecters should show right now.</summary>
        public virtual bool VisualsActive => HasVisuals && (discovered || def.showVisualsWhenHidden);

        /// <summary>Label without the rarity ("Flaming").</summary>
        public string BaseLabelCap => (Override?.label ?? def.label).CapitalizeFirst();

        /// <summary>"Flaming (rare)" once discovered, "???" before.</summary>
        public virtual string Label => discovered
            ? "TF_TraitWithRarity".Translate(BaseLabelCap, Rarity.Label()).ToString()
            : "TF_UnknownTrait".Translate().ToString();

        public virtual string Description => discovered ? (Override?.description ?? def.description) : "TF_UnknownTraitDesc".Translate().ToString();

        /// <summary>Reveal this trait. With notify, the player gets a message (when the
        /// def allows it). Returns true if it was hidden before.</summary>
        public bool Discover(bool notify = true)
        {
            if (discovered) return false;
            string oldLabel = Pawn?.LabelShort;
            discovered = true;
            titan?.Notify_TraitDiscovered(this);
            if (notify && def.discoveryMessage && TitanFrameworkMod.Settings.discoveryMessages && Pawn != null && Pawn.Spawned)
            {
                Messages.Message("TF_TraitDiscovered".Translate(oldLabel, Label),
                    Pawn, MessageTypeDefOf.NeutralEvent, historical: false);
            }
            PostDiscovered();
            return true;
        }

        /// <summary>Discover only if the trigger is listed and the player can see it happen.</summary>
        public void TryDiscoverBy(TitanDiscoveryTrigger trigger)
        {
            if (!discovered && def.DiscoveredBy(trigger) && TitanUtility.PlayerCanObserve(Pawn))
                Discover();
        }

        // ---- hooks ---------------------------------------------------------------

        /// <summary>After the trait is added (not on load).</summary>
        public virtual void PostAdded() { }

        /// <summary>When TitanUtility.CopyTitan copies this trait onto another creature:
        /// called on the new trait, after PostAdded, with the trait it was copied from.
        /// Override to copy any per-creature state your subclass keeps (charges,
        /// cooldowns, counters). The def, rarity and discovery are already copied.
        /// PostDiscovered is not called for traits copied as discovered, so copy anything
        /// it would have set up here.</summary>
        public virtual void CopyStateFrom(TitanTrait source) { }

        /// <summary>Before the trait is removed or the titan hediff is removed.</summary>
        public virtual void PreRemoved() { }

        public virtual void PostDiscovered() { }

        public virtual void TickInterval(int delta) { }

        public virtual void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt) { }

        public virtual void Notify_PawnUsedVerb(Verb verb, LocalTargetInfo target) { }

        public virtual void Notify_PawnDied(DamageInfo? dinfo) { }

        /// <summary>Extra lines in the titan hediff tooltip, only shown once discovered.</summary>
        public virtual string TipStringExtra => null;

        public virtual void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref discovered, "discovered");
            Scribe_Values.Look(ref fromMaterial, "fromMaterial");
            Scribe_Values.Look(ref rarityValue, "rarity", -1);
        }
    }
}
