using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// One trait on one creature. Saved inside Hediff_Bastle, so subclasses can keep
    /// per-creature state by overriding ExposeData. Hooks are called by the bastle
    /// hediff; override only what you need (TickInterval is only called at all when
    /// some trait on the creature overrides it).
    /// </summary>
    public class BastleTrait : IExposable
    {
        public BastleTraitDef def;
        public bool discovered;

        /// <summary>Granted by the creature's material (BastleMaterialExtension.traits):
        /// removed when the material changes, and not inherited on its own.</summary>
        public bool fromMaterial;

        /// <summary>Set by the owning hediff after creation and after load.</summary>
        public Hediff_Bastle bastle;

        public Pawn Pawn => bastle?.pawn;

        // Saved as an int; -1 (saves from before rarity) means the def's own rarity.
        private int rarityValue = -1;

        /// <summary>The rarity this trait rolled at. Only scaling traits differ from the def's.
        /// Clamped when set, not when read: MaxRarity depends on the settings, and a trait
        /// whose rarity changed after its hediffs and abilities were granted would leave the
        /// old ones behind. Factors are floored when scaled, so any rarity is safe.</summary>
        public BastleRarity Rarity
        {
            get => rarityValue < 0 ? def.rarity : (BastleRarity)rarityValue;
            internal set { rarityValue = (int)def.ClampRarity(value); scaledStage = null; }
        }

        internal void SetRarityExact(BastleRarity r)
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
            else rarityValue = Mathf.Clamp(rarityValue, (int)def.rarity, (int)BastleRarity.Legendary);
        }

        /// <summary>The rarity override in effect, or null.</summary>
        public BastleRarityOverride Override => def.OverrideAt(Rarity);

        [Unsaved] private HediffStage scaledStage;
        [Unsaved] private int scaledFor = -1;

        /// <summary>This trait's stage at its rarity (override applied, numbers scaled).
        /// Rebuilt when the settings change. A subclass can override this to switch its
        /// stage on and off (return null for none); call bastle.StageChanged() whenever
        /// what it returns changes.</summary>
        public virtual HediffStage Stage
        {
            get
            {
                if (scaledFor != BastleSettings.Version || scaledStage == null)
                {
                    scaledStage = BastleStageScaler.Scale(def.StageAt(Rarity), def.StrengthAt(Rarity));
                    scaledFor = BastleSettings.Version;
                }
                return scaledStage;
            }
        }

        public float Points => def.PointsAt(Rarity);

        public float HealthScaleFactor
        {
            get
            {
                var o = Override?.healthScaleFactor;
                float f = o ?? def.healthScaleFactor;
                return f == 1f ? 1f : Mathf.Max(0f, 1f + (f - 1f) * def.StrengthFor(o != null, Rarity));
            }
        }

        public float DrawSizeFactor => def.DrawSizeAt(Rarity);
        public BastleRetaliation Retaliation => Override?.retaliation ?? def.retaliation;
        public BastleLifesteal Lifesteal => Override?.lifesteal ?? def.lifesteal;
        /// <summary>Rarity strength for the retaliation amount (relative to where it was written).</summary>
        public float RetaliationStrength => def.StrengthFor(Override?.retaliation != null, Rarity);
        public float LifestealStrength => def.StrengthFor(Override?.lifesteal != null, Rarity);
        public BastleProducts Products => Override?.products ?? def.products;
        public BastleDeflect Deflect => Override?.deflect ?? def.deflect;

        public List<BastleTraitHediff> Hediffs => Override?.hediffs ?? def.hediffs;
        public List<AbilityDef> Abilities => Override?.abilities ?? def.abilities;
        public ThinkTreeDef AiThinkTree => Override?.aiThinkTree ?? def.aiThinkTree;
        public BastleDietProps Diet => Override?.diet ?? def.diet;
        public List<BastleOverlay> Overlays => Override?.overlays ?? def.overlays;
        public List<EffecterDef> Effecters => Override?.effecters ?? def.effecters;
        public List<BastleFlames> Flames => Override?.flames ?? def.flames;
        public BastleGlow Glow => Override?.glow ?? def.glow;
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
            ? "BF_TraitWithRarity".Translate(BaseLabelCap, Rarity.Label()).ToString()
            : "BF_UnknownTrait".Translate().ToString();

        public virtual string Description => discovered ? (Override?.description ?? def.description) : "BF_UnknownTraitDesc".Translate().ToString();

        /// <summary>Reveal this trait. With notify, the player gets a message (when the
        /// def allows it). Returns true if it was hidden before.</summary>
        public bool Discover(bool notify = true)
        {
            if (discovered) return false;
            string oldLabel = Pawn?.LabelShort;
            discovered = true;
            bastle?.Notify_TraitDiscovered(this);
            // Packmates learn it quietly; this creature's message counts them instead.
            int packmates = BastleDiscoveryGroups.Share(bastle, def);
            if (notify && def.discoveryMessage && BastleFrameworkMod.Settings.discoveryMessages && Pawn != null && Pawn.Spawned)
            {
                var text = packmates > 0
                    ? "BF_TraitDiscoveredPack".Translate(oldLabel, Label, packmates)
                    : "BF_TraitDiscovered".Translate(oldLabel, Label);
                Messages.Message(text, Pawn, MessageTypeDefOf.NeutralEvent, historical: false);
            }
            PostDiscovered();
            return true;
        }

        /// <summary>Discover only if the trigger is listed and the player can see it happen.</summary>
        public void TryDiscoverBy(BastleDiscoveryTrigger trigger)
        {
            if (!discovered && def.DiscoveredBy(trigger) && BastleUtility.PlayerCanObserve(Pawn))
                Discover();
        }

        // ---- hooks ---------------------------------------------------------------

        /// <summary>After the trait is added (not on load).</summary>
        public virtual void PostAdded() { }

        /// <summary>When BastleUtility.CopyBastle copies this trait onto another creature:
        /// called on the new trait, after PostAdded, with the trait it was copied from.
        /// Override to copy any per-creature state your subclass keeps (charges,
        /// cooldowns, counters). The def, rarity and discovery are already copied.
        /// PostDiscovered is not called for traits copied as discovered, so copy anything
        /// it would have set up here.</summary>
        public virtual void CopyStateFrom(BastleTrait source) { }

        /// <summary>Before the trait is removed or the bastle hediff is removed.</summary>
        public virtual void PreRemoved() { }

        /// <summary>After the trait is revealed. Can run inside vanilla loops over the
        /// pawn's hediffs (damage, verb use, Thing.Ingested), so don't add or remove
        /// hediffs or traits here: queue it for TickInterval. Also runs on packmates that
        /// learn the trait quietly (see BastleUtility.LinkDiscovery); a trait this reveals
        /// from there is not passed on to the rest of the pack.</summary>
        public virtual void PostDiscovered() { }

        public virtual void TickInterval(int delta) { }

        public virtual void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt) { }

        public virtual void Notify_PawnUsedVerb(Verb verb, LocalTargetInfo target) { }

        /// <summary>The creature damaged something. Called inside vanilla's foreach over
        /// the creature's hediffs, so don't add or remove hediffs here (healing an injury
        /// to zero removes it): queue it for TickInterval.</summary>
        public virtual void Notify_PawnDamagedThing(Thing thing, DamageInfo dinfo, DamageWorker.DamageResult result) { }

        public virtual void Notify_PawnDied(DamageInfo? dinfo) { }

        /// <summary>Extra lines in the bastle hediff tooltip, only shown once discovered.</summary>
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
