using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// The one hediff every bastle carries, however many traits it has. It owns the
    /// trait list, merges every trait's stage into a single cached HediffStage, adds
    /// and removes the traits' extra hediffs and abilities, forwards events to the
    /// traits, and drives discovery.
    ///
    /// Performance: the stage is rebuilt only when the trait list changes. Traits are
    /// only iterated on tick when one of them overrides TickInterval. The event hooks
    /// (damage, verb use) are already delivered to every hediff by vanilla, so they
    /// cost us a short loop over the traits and nothing else.
    /// </summary>
    public class Hediff_Bastle : HediffWithComps
    {
        private List<BastleTrait> traits = new List<BastleTrait>();

        /// <summary>The stuff this creature is made of, or null for flesh. Set through
        /// BastleUtility.SetMaterial.</summary>
        private ThingDef material;

        [Unsaved] private HediffStage materialStage;

        [Unsaved] private HediffStage cachedStage;
        [Unsaved] private bool stageDirty = true;
        [Unsaved] private bool anyTicking;
        [Unsaved] private string prefixCache;
        [Unsaved] private string suffixCache;
        [Unsaved] private bool nameDirty = true;
        [Unsaved] private int nameVersion = -1;

        // Visuals. Overlays live in the pawn's render tree (DynamicPawnRenderNodeSetup_Bastle);
        // effecters are ticked by MapComponent_BastleVisuals while the pawn is on its map.
        [Unsaved] private List<EffecterDef> activeEffecterDefs;
        [Unsaved] private readonly List<Effecter> liveEffecters = new List<Effecter>();
        [Unsaved] private Map trackedMap;
        [Unsaved] private int effectersSpawnedTick = -1;
        [Unsaved] private BastleGlow activeGlow;
        [Unsaved] private bool glowResolved;
        [Unsaved] private BastleGlower glower;

        // Sub-effecters stop spawning after their def's lifespanMaxTicks (default
        // 999999, about 16 days), so live effecters are respawned well before that.
        private const int EffecterRefreshTicks = 600000;

        public List<BastleTrait> TraitsForReading => traits;

        public BastleTrait GetTrait(BastleTraitDef def)
        {
            for (int i = 0; i < traits.Count; i++)
                if (traits[i].def == def) return traits[i];
            return null;
        }

        public bool HasTrait(BastleTraitDef def) => GetTrait(def) != null;

        public float TotalPointCost => traits.Sum(t => t.Points);

        public List<BastleTrait> Traits => traits;

        public ThingDef Material => material;

        [Unsaved] private BastleDiet diet;
        [Unsaved] private HediffStage dietBuiltFor;

        /// <summary>Merged diet of every trait plus the material, or null when nothing
        /// changes what this creature eats. Rebuilt with the stage, which is marked dirty
        /// whenever traits, material or settings change.</summary>
        public BastleDiet Diet
        {
            get
            {
                if (stageDirty || cachedStage == null)
                {
                    cachedStage = BuildStage();
                    stageDirty = false;
                    cardStage = null;
                }
                if (dietBuiltFor != cachedStage)
                {
                    diet = BastleDiet.Build(traits, material);
                    dietBuiltFor = cachedStage;
                }
                return diet;
            }
        }

        // Where this hediff last sat in the pawn's hediff list. Checking that slot first
        // makes StillIn O(1) almost always; the list only shifts when an earlier hediff
        // goes. Racing pre-draw threads may both rewrite it with the same answer.
        [Unsaved] private int setIndex = -1;

        /// <summary>Still in this pawn's hediff list. Safe from any thread.</summary>
        internal bool StillIn(Pawn p)
        {
            if (pawn != p) return false;
            var list = p.health?.hediffSet?.hediffs;
            if (list == null) return false;
            int i = setIndex;
            if ((uint)i < (uint)list.Count && list[i] == this) return true;
            i = list.IndexOf(this);
            setIndex = i;
            return i >= 0;
        }

        [Unsaved] private bool abilityAI;
        [Unsaved] private HediffStage abilityAIBuiltFor;

        /// <summary>Some trait grants an ability or an AI think tree, so the ability job
        /// giver has something to try. Rebuilt with the stage.</summary>
        public bool HasAbilityAI
        {
            get
            {
                if (stageDirty || cachedStage == null)
                {
                    cachedStage = BuildStage();
                    stageDirty = false;
                    cardStage = null;
                }
                if (abilityAIBuiltFor != cachedStage)
                {
                    abilityAI = traits.Any(t => t.AiThinkTree != null || !t.Abilities.NullOrEmpty());
                    abilityAIBuiltFor = cachedStage;
                }
                return abilityAI;
            }
        }

        [Unsaved] private float healthFactor = 1f;
        [Unsaved] private HediffStage healthBuiltFor;

        /// <summary>Multiplier on the creature's health scale from every trait, hidden ones
        /// included. Rebuilt with the stage, like Diet. Read by Pawn.HealthScale.</summary>
        public float HealthFactor
        {
            get
            {
                if (stageDirty || cachedStage == null)
                {
                    cachedStage = BuildStage();
                    stageDirty = false;
                    cardStage = null;
                }
                if (healthBuiltFor != cachedStage)
                {
                    healthFactor = BastleHealth.Factor(traits);
                    healthBuiltFor = cachedStage;
                }
                return healthFactor;
            }
        }

        /// <summary>Made of a material: no pain, blood or rot.</summary>
        public bool IsConstruct => material != null;

        public HediffStage MaterialStage
        {
            get
            {
                if (material == null) return null;
                return materialStage ?? (materialStage = BastleMaterialUtility.BuildStage(pawn, material));
            }
        }

        internal void SetMaterialInternal(ThingDef stuff)
        {
            if (stuff == material) return;
            material = stuff;
            materialStage = null;

            // Drop what the old material granted (unless the new one grants it too), and
            // anything a construct can't have.
            var newTraits = BastleMaterialUtility.Ext(stuff)?.traits;
            foreach (var t in traits.ToList())
                if ((t.fromMaterial && (newTraits == null || !newTraits.Contains(t.def)))
                    || (stuff != null && t.def.disallowConstructs))
                    RemoveTraitInternal(t);
            // Add what the new one grants, unless the creature already has it or a
            // trait it has excludes it.
            if (newTraits != null)
                foreach (var def in newTraits)
                    if (!HasTrait(def) && !def.disallowConstructs && !traits.Any(t => t.def.ConflictsWith(def)))
                        AddTraitInternal(def, false, fromMaterial: true);

            ApplyStatue(dirty: true);
            stageDirty = true;
            nameDirty = true;
            if (pawn?.health?.hediffSet != null && pawn.health.hediffSet.hediffs.Contains(this))
                pawn.health.Notify_HediffChanged(this);
        }

        /// <summary>Draws the creature as a statue of its material: vanilla's own statue
        /// rendering (CompStatue uses it), which tints every node in the stuff's colour.
        /// The renderer isn't saved, so this is reapplied when the render tree is set up.</summary>
        internal void ApplyStatue(bool dirty)
        {
            var renderer = pawn?.Drawer?.renderer;
            if (renderer == null) return;
            Color? want = material?.stuffProps?.color;
            if (renderer.StatueColor == want) return;
            renderer.SetStatue(material);
            if (dirty) renderer.SetAllGraphicsDirty();
        }

        // ---- trait list mutation (use BastleUtility from outside) ----------------

        /// <param name="exactRarity">Keep the rarity as given instead of clamping it to
        /// what the current settings allow (used when copying a bastle).</param>
        internal BastleTrait AddTraitInternal(BastleTraitDef def, bool discovered, bool fromMaterial = false, BastleRarity? rarity = null, bool exactRarity = false)
        {
            var trait = (BastleTrait)Activator.CreateInstance(def.traitClass);
            trait.def = def;
            trait.bastle = this;
            trait.discovered = discovered || def.visible;
            trait.fromMaterial = fromMaterial;
            if (exactRarity && rarity != null) trait.SetRarityExact(rarity.Value);
            else trait.Rarity = rarity ?? def.rarity;
            traits.Add(trait);
            ApplyEffects(trait);
            trait.PostAdded();
            TraitsChanged(trait);
            return trait;
        }

        internal void RemoveTraitInternal(BastleTrait trait)
        {
            if (!traits.Contains(trait)) return;
            trait.PreRemoved();
            traits.Remove(trait);
            RemoveEffects(trait);
            TraitsChanged(trait);
        }

        private void ApplyEffects(BastleTrait trait)
        {
            var hediffs = trait.Hediffs;
            var abilities = trait.Abilities;
            if (hediffs != null)
            {
                foreach (var th in hediffs)
                {
                    // Another trait already added this hediff: adding it again would
                    // merge into it and stack the severity.
                    if (OtherTraitHasHediff(trait, th)) continue;
                    BodyPartRecord part = null;
                    if (th.part != null)
                    {
                        part = pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault(p => p.def == th.part);
                        if (part == null) continue;
                    }
                    var h = HediffMaker.MakeHediff(th.hediff, pawn, part);
                    if (th.severity > 0f) h.Severity = th.severity;
                    // A hediff whose stage has becomeVisible=false stays out of the
                    // health tab until this flag is set, which discovery does.
                    if (trait.discovered) h.SetVisible();
                    pawn.health.AddHediff(h, part);
                }
            }
            if (!abilities.NullOrEmpty())
            {
                BastleUtility.EnsureAbilityTracker(pawn);
                foreach (var ab in abilities)
                    pawn.abilities.GainAbility(ab);
            }
        }

        private void RemoveEffects(BastleTrait trait)
        {
            var hediffs = trait.Hediffs;
            var abilities = trait.Abilities;
            if (hediffs != null)
            {
                foreach (var th in hediffs)
                {
                    // Another trait may add the same hediff; leave it if so.
                    if (OtherTraitHasHediff(trait, th)) continue;
                    var existing = pawn.health.hediffSet.hediffs.FirstOrDefault(
                        x => x.def == th.hediff && (th.part == null || x.Part?.def == th.part));
                    if (existing != null) pawn.health.RemoveHediff(existing);
                }
            }
            if (!abilities.NullOrEmpty() && pawn.abilities != null)
            {
                foreach (var ab in abilities)
                {
                    if (traits.Any(t => t != trait && t.Abilities != null && t.Abilities.Contains(ab))) continue;
                    pawn.abilities.RemoveAbility(ab);
                }
            }
        }

        private bool OtherTraitHasHediff(BastleTrait self, BastleTraitHediff th)
        {
            foreach (var t in traits)
            {
                if (t == self || t.Hediffs == null) continue;
                foreach (var o in t.Hediffs)
                    if (o.hediff == th.hediff && o.part == th.part) return true;
            }
            return false;
        }

        // ---- visuals -------------------------------------------------------------

        public bool HasActiveEffecters
        {
            get
            {
                if (!BastleFrameworkMod.Settings.effectsEnabled) return false;
                if (activeEffecterDefs == null)
                {
                    activeEffecterDefs = new List<EffecterDef>();
                    foreach (var t in traits)
                        if (t.VisualsActive && t.Effecters != null)
                            foreach (var e in t.Effecters)
                                if (!activeEffecterDefs.Contains(e)) activeEffecterDefs.Add(e);
                }
                return activeEffecterDefs.Count > 0;
            }
        }

        /// <summary>The light this creature casts: the largest glow among its traits,
        /// discovered or not.</summary>
        public BastleGlow ActiveGlow
        {
            get
            {
                if (!BastleFrameworkMod.Settings.lightsEnabled) return null;
                if (!glowResolved)
                {
                    glowResolved = true;
                    activeGlow = null;
                    foreach (var t in traits)
                    {
                        var g = t.Glow;
                        if (g != null && (activeGlow == null || g.radius > activeGlow.radius))
                            activeGlow = g;
                    }
                }
                return activeGlow;
            }
        }

        /// <summary>Has something the map component has to run: effecters or a light.</summary>
        public bool HasMapVisuals => HasActiveEffecters || ActiveGlow != null;

        /// <summary>Call when which visuals should show may have changed.</summary>
        public void VisualsChanged(BastleTrait cause = null)
        {
            activeEffecterDefs = null;
            glowResolved = false;
            glower?.Remove();
            glower = null;
            CleanupEffecters();
            // The render tree is only rebuilt when overlays could have changed:
            // rebuilding it is not free, and discovery of a stat-only trait
            // shouldn't pay for it.
            if ((cause == null || cause.HasRenderVisuals) && pawn?.Drawer?.renderer != null)
                pawn.Drawer.renderer.SetAllGraphicsDirty();
        }

        internal void EffectersTick()
        {
            if (!HasActiveEffecters) return;
            int now = Find.TickManager.TicksGame;
            if (liveEffecters.Count > 0 && now - effectersSpawnedTick > EffecterRefreshTicks)
                CleanupEffecters(keepTracked: true);
            if (liveEffecters.Count == 0)
            {
                float scale = Mathf.Clamp(Mathf.Sqrt(pawn.BodySize), 0.5f, 2.5f);
                foreach (var def in activeEffecterDefs)
                {
                    // Not SpawnAttached: with offsetMode TrueCenterCompensated (the default)
                    // it stores DrawPos minus the cell centre as a fixed offset. The sprayer
                    // already spawns at the live DrawPos, so for a pawn caught mid-step that
                    // offset is counted twice and the effect trails up to a cell off to
                    // one side for the effecter's whole life.
                    var eff = new Effecter(def) { scale = EffecterScaled(def) ? scale : 1f, offset = Vector3.zero };
                    eff.Trigger(pawn, pawn);
                    liveEffecters.Add(eff);
                }
                effectersSpawnedTick = now;
            }
            for (int i = 0; i < liveEffecters.Count; i++)
                liveEffecters[i].EffectTick(pawn, pawn);
        }

        private bool EffecterScaled(EffecterDef def)
        {
            foreach (var t in traits)
                if (t.Effecters != null && t.Effecters.Contains(def))
                    return t.def.scaleEffectersWithBodySize;
            return true;
        }

        /// <summary>Moves the light with the creature. Runs even when nobody is looking:
        /// light changes what pawns on that map can see.</summary>
        internal void GlowTick()
        {
            var g = ActiveGlow;
            if (g == null) return;
            // The glow grid finds a light to remove by its thing's id, so a race with its
            // own CompGlower would have the wrong light removed. That race keeps its own.
            if (glower == null)
            {
                if (pawn.GetComp<CompGlower>() != null) return;
                glower = new BastleGlower(pawn, g);
            }
            glower.Update(pawn);
        }

        internal Map TrackedMap => trackedMap;

        /// <summary>The mod settings changed: rebuild everything derived from them. The
        /// map component drops this bastle if it no longer has effecters or a light, and
        /// TickInterval picks it up again if it gained one.</summary>
        internal void SettingsChanged()
        {
            activeEffecterDefs = null;
            glowResolved = false;
            glower?.Remove();
            glower = null;
            CleanupEffecters(keepTracked: true);
            RecalcTicking();
            // The health-per-point slider changes every bastle, flesh or not.
            materialStage = null;
            stageDirty = true;
            if (pawn?.health?.hediffSet != null && pawn.health.hediffSet.hediffs.Contains(this))
            {
                SyncInjuries();
                pawn.health.Notify_HediffChanged(this);
            }
        }

        /// <summary>The health factor the creature's injuries are currently sized for.
        /// Saved, so a slider change made while the save wasn't loaded is caught on load.</summary>
        private float appliedHealth = 1f;

        /// <summary>Injury severity is in absolute hit points. When the health factor
        /// changes, scale every injury so each part keeps the same fraction of its
        /// health: otherwise a lower factor can kill a wounded bastle on the spot, and a
        /// round trip down and up would heal it for free. Call after marking the stage
        /// dirty.</summary>
        private void SyncInjuries()
        {
            float newFactor = HealthFactor;
            if (appliedHealth > 0f && Mathf.Abs(newFactor - appliedHealth) > 0.0001f)
            {
                float scale = newFactor / appliedHealth;
                // Copy: raising a staged injury's severity can notify the health tracker.
                foreach (var h in pawn.health.hediffSet.hediffs.ToList())
                    if (h is Hediff_Injury injury)
                        injury.Severity *= scale;
            }
            appliedHealth = newFactor;
        }

        internal void CleanupEffecters(bool keepTracked = false)
        {
            if (!keepTracked) glower?.Remove();
            if (liveEffecters.Count == 0) { if (!keepTracked) trackedMap = null; return; }
            for (int i = 0; i < liveEffecters.Count; i++) liveEffecters[i].Cleanup();
            liveEffecters.Clear();
            if (!keepTracked) trackedMap = null;
        }

        private void TraitsChanged(BastleTrait changed)
        {
            if (changed.HasVisuals) VisualsChanged(changed);
            var oldStage = cachedStage;
            stageDirty = true;
            nameDirty = true;
            RecalcTicking();
            if (pawn?.health?.hediffSet == null || !pawn.health.hediffSet.hediffs.Contains(this)) return;
            SyncInjuries();
            pawn.health.Notify_HediffChanged(this);

            // Vanilla only refreshes needs and disabled work when a hediff is added or
            // removed, not when its stage changes in place, so do it here when either
            // the old or the new stage touches them.
            var newStage = CurStage;
            bool needsChanged = !(oldStage?.enablesNeeds).NullOrEmpty() || !(oldStage?.disablesNeeds).NullOrEmpty()
                || !newStage.enablesNeeds.NullOrEmpty() || !newStage.disablesNeeds.NullOrEmpty();
            if (needsChanged && !pawn.Dead) pawn.needs?.AddOrRemoveNeedsAsAppropriate();
            if ((oldStage?.disabledWorkTags ?? WorkTags.None) != newStage.disabledWorkTags)
                pawn.Notify_DisabledWorkTypesChanged();
        }

        /// <summary>A trait's Stage now returns something different (a trait that switches
        /// its stage on and off). Rebuilds the merged stage and refreshes the pawn's
        /// stats and capacities. Not for changes that alter needs: use the trait list.</summary>
        public void StageChanged()
        {
            stageDirty = true;
            cardStage = null;
            if (pawn?.health?.hediffSet != null && pawn.health.hediffSet.hediffs.Contains(this))
                pawn.health.Notify_HediffChanged(this);
        }

        internal void Notify_TraitDiscovered(BastleTrait trait)
        {
            nameDirty = true;
            cardStage = null;
            if (trait.HasVisuals) VisualsChanged(trait);
            if (trait.Hediffs == null) return;
            foreach (var th in trait.Hediffs)
                foreach (var h in pawn.health.hediffSet.hediffs.ToList())
                    if (h.def == th.hediff && (th.part == null || h.Part?.def == th.part))
                    {
                        h.SetVisible();
                        pawn.health.Notify_HediffChanged(h);
                    }
        }

        private void RecalcTicking()
        {
            anyTicking = false;
            float scale = 1f;
            for (int i = 0; i < traits.Count; i++)
            {
                if (traits[i].def.Ticks) anyTicking = true;
                scale *= traits[i].DrawSizeFactor;
            }
            // Read every frame when the draw matrix is built, so no render refresh is needed.
            drawScale = scale;
        }

        /// <summary>Product of every trait's draw size factor. Kept as a plain field so the
        /// parallel pre-draw threads can read it (see Patch_PawnRenderer_GetDrawParms).</summary>
        [Unsaved] private float drawScale = 1f;
        public float DrawScale => drawScale;

        // Lifesteal healing waits for the next tick: it is earned inside vanilla's
        // foreach over this pawn's hediffs.
        [Unsaved] private float pendingHeal;

        // ---- stage ---------------------------------------------------------------

        /// <summary>The stage without undiscovered traits. Used in place of CurStage only
        /// while this bastle's info card is drawing (BastleHiding), so the card's numbers
        /// give no hint of hidden traits. Rebuilt when the stage changes or a trait is
        /// discovered.</summary>
        [Unsaved] internal HediffStage cardStage;

        public bool AnyHiddenTrait
        {
            get
            {
                for (int i = 0; i < traits.Count; i++)
                    if (!traits[i].discovered) return true;
                return false;
            }
        }

        /// <summary>Adds the hediffs that only undiscovered traits grant. A hediff that a
        /// discovered trait also grants is left out: the player already knows about it.</summary>
        internal void CollectHiddenHediffs(HashSet<Hediff> into)
        {
            var hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < traits.Count; i++)
            {
                var t = traits[i];
                if (t.discovered || t.Hediffs == null) continue;
                foreach (var th in t.Hediffs)
                {
                    if (DiscoveredTraitGrants(th)) continue;
                    for (int j = 0; j < hediffs.Count; j++)
                        if (hediffs[j].def == th.hediff && (th.part == null || hediffs[j].Part?.def == th.part)) into.Add(hediffs[j]);
                }
            }
        }

        private bool DiscoveredTraitGrants(BastleTraitHediff th)
        {
            for (int i = 0; i < traits.Count; i++)
            {
                var t = traits[i];
                if (!t.discovered || t.Hediffs == null) continue;
                foreach (var x in t.Hediffs)
                    if (x.hediff == th.hediff && x.part == th.part) return true;
            }
            return false;
        }

        /// <summary>True when every trait that grants this ability is undiscovered.</summary>
        internal bool AbilityOnlyFromHiddenTraits(AbilityDef abilityDef)
        {
            bool any = false;
            for (int i = 0; i < traits.Count; i++)
            {
                var t = traits[i];
                if (t.Abilities == null || !t.Abilities.Contains(abilityDef)) continue;
                if (t.discovered) return false;
                any = true;
            }
            return any;
        }

        public override HediffStage CurStage
        {
            get
            {
                if (BastleHiding.HidingFor(pawn))
                {
                    if (stageDirty)
                    {
                        cachedStage = BuildStage();
                        stageDirty = false;
                        cardStage = null;
                    }
                    if (cardStage == null) cardStage = BuildStage(discoveredOnly: true);
                    return cardStage;
                }
                if (stageDirty || cachedStage == null)
                {
                    cachedStage = BuildStage();
                    cardStage = null;
                    stageDirty = false;
                }
                return cachedStage;
            }
        }

        /// <summary>
        /// Merges each trait's stage. Factors multiply, offsets add, lists union.
        /// Stat offsets/factors are merged per stat because vanilla reads only the
        /// first entry for a given stat from a stage's list.
        /// Not merged (use a trait hediff instead): death/vomit/mental break MTBs,
        /// forgetMemoryThoughtMtbDays, pctConditionalThoughtsNullified,
        /// pctAllThoughtNullification, overrideMoodBase, allowedMentalBreakIntensities,
        /// lifeThreatening, tale, severity-scaled stat lists and the *EffectMultiplier
        /// stats, part-only fields (partEfficiencyOffset, partIgnoreMissingHP,
        /// destroyPart), and label/tooltip fields.
        /// </summary>
        protected virtual HediffStage BuildStage(bool discoveredOnly = false)
        {
            var s = new HediffStage { becomeVisible = true };
            Dictionary<StatDef, float> offsets = null, factors = null;
            Dictionary<DamageDef, float> dmg = null;

            var stages = new List<HediffStage>(traits.Count + 1);
            foreach (var trait in traits)
            {
                var st = trait.Stage;
                if (st != null && (!discoveredOnly || trait.discovered)) stages.Add(st);
            }
            if (MaterialStage != null) stages.Add(MaterialStage);

            foreach (var t in stages)
            {

                s.painFactor *= t.painFactor;
                s.painOffset += t.painOffset;
                s.totalBleedFactor *= t.totalBleedFactor;
                if (t.naturalHealingFactor >= 0f)
                    s.naturalHealingFactor = (s.naturalHealingFactor < 0f ? 1f : s.naturalHealingFactor) * t.naturalHealingFactor;
                if (t.regeneration >= 0f)
                    s.regeneration = (s.regeneration < 0f ? 0f : s.regeneration) + t.regeneration;
                s.hungerRateFactor *= t.hungerRateFactor;
                s.hungerRateFactorOffset += t.hungerRateFactorOffset;
                s.restFallFactor *= t.restFallFactor;
                s.restFallFactorOffset += t.restFallFactorOffset;
                s.fertilityFactor *= t.fertilityFactor;
                s.foodPoisoningChanceFactor *= t.foodPoisoningChanceFactor;
                s.socialFightChanceFactor *= t.socialFightChanceFactor;
                s.opinionOfOthersFactor *= t.opinionOfOthersFactor;
                s.severityGainFactor *= t.severityGainFactor;
                s.blocksSleeping |= t.blocksSleeping;
                s.blocksMentalBreaks |= t.blocksMentalBreaks;
                s.blocksInspirations |= t.blocksInspirations;
                s.preventVacuumBurns |= t.preventVacuumBurns;
                s.removeRoamMtb |= t.removeRoamMtb;
                s.disabledWorkTags |= t.disabledWorkTags;

                Union(ref s.makeImmuneTo, t.makeImmuneTo);
                Union(ref s.enablesNeeds, t.enablesNeeds);
                Union(ref s.disablesNeeds, t.disablesNeeds);
                Concat(ref s.hediffGivers, t.hediffGivers);
                Concat(ref s.mentalStateGivers, t.mentalStateGivers);
                if (t.capMods != null) s.capMods.AddRange(t.capMods);

                if (t.statOffsets != null)
                    foreach (var m in t.statOffsets)
                    {
                        offsets = offsets ?? new Dictionary<StatDef, float>();
                        offsets.TryGetValue(m.stat, out float v);
                        offsets[m.stat] = v + m.value;
                    }
                if (t.statFactors != null)
                    foreach (var m in t.statFactors)
                    {
                        factors = factors ?? new Dictionary<StatDef, float>();
                        float v = factors.TryGetValue(m.stat, out float f) ? f : 1f;
                        factors[m.stat] = v * m.value;
                    }
                if (t.damageFactors != null)
                    foreach (var d in t.damageFactors)
                    {
                        dmg = dmg ?? new Dictionary<DamageDef, float>();
                        float v = dmg.TryGetValue(d.damageDef, out float f) ? f : 1f;
                        dmg[d.damageDef] = v * d.factor;
                    }
            }

            if (offsets != null)
                s.statOffsets = offsets.Select(kv => new StatModifier { stat = kv.Key, value = kv.Value }).ToList();
            if (factors != null)
                s.statFactors = factors.Select(kv => new StatModifier { stat = kv.Key, value = kv.Value }).ToList();
            if (dmg != null)
                s.damageFactors = dmg.Select(kv => new DamageFactor { damageDef = kv.Key, factor = kv.Value }).ToList();
            return s;
        }

        private static void Union<T>(ref List<T> into, List<T> from)
        {
            if (from == null) return;
            into = into ?? new List<T>();
            foreach (var x in from) if (!into.Contains(x)) into.Add(x);
        }

        private static void Concat<T>(ref List<T> into, List<T> from)
        {
            if (from == null) return;
            into = into ?? new List<T>();
            into.AddRange(from);
        }

        // ---- naming --------------------------------------------------------------

        /// <summary>Applies discovered traits' prefixes and suffixes to a kind label.</summary>
        public string DecorateKindLabel(string kindLabel)
        {
            if (nameDirty || nameVersion != BastleSettings.Version) RebuildNameCache();
            if (prefixCache == null && suffixCache == null) return kindLabel;
            return prefixCache + kindLabel + suffixCache;
        }

        private void RebuildNameCache()
        {
            nameDirty = false;
            nameVersion = BastleSettings.Version;
            prefixCache = null;
            suffixCache = null;

            // Pick which known traits get a word: highest namePriority first, then the
            // trait worth the most points, up to the setting's limit. Only one trait may
            // add a suffix; a later trait with both keeps just its prefix, and one with
            // only a suffix gives its place to the next trait.
            int limit = BastleFrameworkMod.Settings.maxNameTraits;
            var shown = new List<(BastleTrait trait, bool prefix, bool suffix)>();
            bool suffixTaken = false;
            foreach (var t in traits.Where(t => t.discovered)
                         .OrderByDescending(t => t.def.namePriority).ThenByDescending(t => t.Points))
            {
                if (shown.Count >= limit) break;
                bool prefix = !t.NamePrefix.NullOrEmpty();
                bool suffix = !t.NameSuffix.NullOrEmpty() && !suffixTaken;
                if (!prefix && !suffix) continue;
                suffixTaken |= suffix;
                shown.Add((t, prefix, suffix));
            }

            var known = shown.OrderBy(s => s.trait.def.nameOrder).ToList();
            var pre = known.Where(s => s.prefix).Select(s => s.trait.NamePrefix).ToList();
            // The material always shows and sits next to the kind: "flaming jade bear".
            if (material != null) pre.Add(BastleMaterialUtility.Adjective(material));
            // Suffixes read outward from the kind label, so the lowest nameOrder goes last.
            var suf = known.Where(s => s.suffix).Select(s => s.trait.NameSuffix).Reverse().ToList();
            if (pre.Count > 0) prefixCache = string.Join(" ", pre) + " ";
            if (suf.Count > 0) suffixCache = " " + string.Join(" ", suf);
        }

        // ---- display -------------------------------------------------------------

        public override string LabelInBrackets
        {
            get
            {
                int unknown = traits.Count(t => !t.discovered);
                string known = (traits.Count - unknown).ToString();
                if (traits.Count == 0) return material?.label;
                return unknown > 0
                    ? "BF_HediffBracketsUnknown".Translate(known, unknown).ToString()
                    : "BF_HediffBrackets".Translate(known).ToString();
            }
        }

        public override string TipStringExtra
        {
            get
            {
                // base.TipStringExtra is deliberately not called: it would list the
                // merged stage's effects, which includes the hidden traits' numbers.
                // Each discovered trait lists its own stage effects instead.
                var sb = new StringBuilder();
                if (material != null)
                {
                    sb.AppendLine("BF_MaterialTipTitle".Translate(material.LabelCap).ToString().Colorize(ColoredText.TipSectionTitleColor));
                    sb.AppendLine("  " + "BF_MaterialTipDesc".Translate());
                    foreach (StatDrawEntry e in HediffStatsUtility.SpecialDisplayStats(MaterialStage, null))
                        if (e.ShouldDisplay())
                            sb.AppendLine("  - " + e.LabelCap + ": " + e.ValueString);
                }
                foreach (var t in traits)
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.AppendLine(t.Label.Colorize(t.discovered ? ColoredText.TipSectionTitleColor : ColoredText.SubtleGrayColor));
                    if (!t.Description.NullOrEmpty()) sb.AppendLine("  " + t.Description);
                    if (!t.discovered) continue;
                    if (t.Stage != null)
                        foreach (StatDrawEntry e in HediffStatsUtility.SpecialDisplayStats(t.Stage, null))
                            if (e.ShouldDisplay())
                                sb.AppendLine("  - " + e.LabelCap + ": " + e.ValueString);
                    string traitHealth = BastleHealth.DescribeTrait(t);
                    if (traitHealth != null) sb.AppendLine("  - " + traitHealth);
                    foreach (string line in BastleDiet.Describe(t.Diet))
                        sb.AppendLine("  - " + line);
                    string extra = t.TipStringExtra;
                    if (!extra.NullOrEmpty()) sb.AppendLine("  " + extra);
                }
                string health = BastleHealth.DescribeTotal(traits);
                if (health != null) sb.AppendLine().AppendLine(health);
                return sb.ToString().TrimEndNewlines();
            }
        }

        // ---- lifecycle -----------------------------------------------------------

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            BastleUtility.Register(pawn, this);
        }

        public override void PostRemoved()
        {
            // Strip what the traits added. Copy: RemoveEffects checks the live list.
            var all = traits.ToList();
            traits.Clear();
            foreach (var t in all)
            {
                t.PreRemoved();
                RemoveEffects(t);
            }
            // Health is back to the race's, so size the injuries back down with it.
            if (appliedHealth > 0f && Mathf.Abs(appliedHealth - 1f) > 0.0001f && pawn?.health?.hediffSet != null)
            {
                float scale = 1f / appliedHealth;
                foreach (var h in pawn.health.hediffSet.hediffs.ToList())
                    if (h is Hediff_Injury injury)
                        injury.Severity *= scale;
            }
            appliedHealth = 1f;
            base.PostRemoved();
            BastleUtility.Unregister(pawn);
            VisualsChanged();
            if (material != null)
            {
                material = null;
                materialStage = null;
                ApplyStatue(dirty: true);
            }
        }

        [Unsaved] private bool statueChecked;

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            // The render tree setup reapplies the statue after a load; this covers races
            // whose tree never runs dynamic setups.
            if (!statueChecked && material != null)
            {
                statueChecked = true;
                ApplyStatue(dirty: true);
            }
            if (pendingHeal > 0f)
            {
                float heal = pendingHeal;
                pendingHeal = 0f;
                BastleCombatUtility.HealInjuries(pawn, heal);
            }
            if (anyTicking)
            {
                for (int i = traits.Count - 1; i >= 0; i--)
                    if (traits[i].def.Ticks) traits[i].TickInterval(delta);
            }
            // Hand effecters to the map component, which ticks them every tick.
            if (pawn.Spawned && trackedMap != pawn.Map && HasMapVisuals)
            {
                pawn.Map.GetComponent<MapComponent_BastleVisuals>()?.Track(this);
                trackedMap = pawn.Map;
            }
        }

        public override void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.Notify_PawnPostApplyDamage(dinfo, totalDamageDealt);
            for (int i = traits.Count - 1; i >= 0; i--)
            {
                var t = traits[i];
                t.Notify_PawnPostApplyDamage(dinfo, totalDamageDealt);
                t.TryDiscoverBy(BastleDiscoveryTrigger.TakesDamage);
                if (t.Retaliation != null) BastleCombatUtility.TryRetaliate(t, dinfo);
            }
        }

        public override void Notify_PawnDamagedThing(Thing thing, DamageInfo dinfo, DamageWorker.DamageResult result)
        {
            base.Notify_PawnDamagedThing(thing, dinfo, result);
            for (int i = traits.Count - 1; i >= 0; i--)
            {
                var t = traits[i];
                t.Notify_PawnDamagedThing(thing, dinfo, result);
                if (t.Lifesteal != null) pendingHeal += BastleCombatUtility.LifestealFrom(t, thing, dinfo, result);
            }
        }

        /// <summary>True when an undiscovered trait grants this ability.</summary>
        internal bool HasHiddenTraitGranting(AbilityDef abilityDef)
        {
            for (int i = 0; i < traits.Count; i++)
                if (!traits[i].discovered && traits[i].Abilities != null && traits[i].Abilities.Contains(abilityDef))
                    return true;
            return false;
        }

        /// <summary>The ability's visible lead-in effect started (fire spew's flames play
        /// 17 ticks before the ability resolves, and the cast can still be cancelled in
        /// that window). The player saw it, so reveal the trait that granted it.</summary>
        internal void Notify_AbilityEffectStarted(AbilityDef abilityDef)
        {
            for (int i = traits.Count - 1; i >= 0; i--)
            {
                var t = traits[i];
                if (!t.discovered && t.Abilities != null && t.Abilities.Contains(abilityDef))
                    t.TryDiscoverBy(BastleDiscoveryTrigger.UsesGrantedAbility);
            }
        }

        /// <summary>Next tick a starving search for refused food may run (see
        /// BastleDietUtility.TryDiscoverRefusal). Not saved: a reload only allows one early check.</summary>
        internal int nextRefusalCheck;

        public override void Notify_IngestedThing(Thing thing, int amount)
        {
            base.Notify_IngestedThing(thing, amount);
            if (thing != null) Notify_Ate(thing.def);
        }

        /// <summary>Reveals hidden traits whose diet is what let the creature eat this.
        /// Also called by the non-food eating job, which doesn't go through Thing.Ingested.</summary>
        internal void Notify_Ate(ThingDef food)
        {
            var removed = Diet?.remove ?? FoodTypeFlags.None;
            for (int i = 0; i < traits.Count; i++)
            {
                var t = traits[i];
                if (!t.discovered && t.def.DiscoveredBy(BastleDiscoveryTrigger.EatsAddedFood) && BastleDiet.TraitAdds(t.Diet, food, pawn, removed))
                    t.TryDiscoverBy(BastleDiscoveryTrigger.EatsAddedFood);
            }
        }

        public override void Notify_PawnUsedVerb(Verb verb, LocalTargetInfo target)
        {
            base.Notify_PawnUsedVerb(verb, target);
            AbilityDef abilityDef = (verb as IAbilityVerb)?.Ability?.def;
            HediffDef verbHediff = (verb.DirectOwner as HediffComp)?.parent?.def;

            for (int i = traits.Count - 1; i >= 0; i--)
            {
                var t = traits[i];
                t.Notify_PawnUsedVerb(verb, target);
                if (t.discovered) continue;
                t.TryDiscoverBy(BastleDiscoveryTrigger.Attacks);
                if (abilityDef != null && t.Abilities != null && t.Abilities.Contains(abilityDef))
                    t.TryDiscoverBy(BastleDiscoveryTrigger.UsesGrantedAbility);
                if (verbHediff != null && t.Hediffs != null && t.Hediffs.Any(h => h.hediff == verbHediff))
                    t.TryDiscoverBy(BastleDiscoveryTrigger.UsesGrantedVerb);
            }
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            for (int i = traits.Count - 1; i >= 0; i--)
            {
                var t = traits[i];
                t.Notify_PawnDied(dinfo);
                t.TryDiscoverBy(BastleDiscoveryTrigger.Dies);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref traits, "traits", LookMode.Deep);
            Scribe_Defs.Look(ref material, "material");
            // Default 0, not 1: saves from before this field have injuries already sized
            // for the current factor, and 0 makes SyncInjuries record it without scaling.
            Scribe_Values.Look(ref appliedHealth, "appliedHealth", 0f, forceSave: true);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                materialStage = null;
                traits = traits ?? new List<BastleTrait>();
                traits.RemoveAll(t => t?.def == null);
                foreach (var t in traits)
                {
                    t.bastle = this;
                    t.ClampLoadedRarity();
                }
                stageDirty = true;
                nameDirty = true;
                RecalcTicking();
                BastleUtility.Register(pawn, this);
                // After the load finishes, so the whole pawn is resolved. Outside a long
                // event ExecuteWhenFinished would run right now, mid-load, so skip it
                // then: the next trait or settings change syncs from the saved value.
                if (LongEventHandler.AnyEventNowOrWaiting)
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        if (pawn?.health?.hediffSet == null || !pawn.health.hediffSet.hediffs.Contains(this)) return;
                        SyncInjuries();
                        pawn.health.Notify_HediffChanged(this);
                    });
            }
        }
    }
}
