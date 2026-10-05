using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// The one hediff every titan carries, however many traits it has. It owns the
    /// trait list, merges every trait's stage into a single cached HediffStage, adds
    /// and removes the traits' extra hediffs and abilities, forwards events to the
    /// traits, and drives discovery.
    ///
    /// Performance: the stage is rebuilt only when the trait list changes. Traits are
    /// only iterated on tick when one of them overrides TickInterval. The event hooks
    /// (damage, verb use) are already delivered to every hediff by vanilla, so they
    /// cost us a short loop over the traits and nothing else.
    /// </summary>
    public class Hediff_Titan : HediffWithComps
    {
        private List<TitanTrait> traits = new List<TitanTrait>();

        [Unsaved] private HediffStage cachedStage;
        [Unsaved] private bool stageDirty = true;
        [Unsaved] private bool anyTicking;
        [Unsaved] private string prefixCache;
        [Unsaved] private string suffixCache;
        [Unsaved] private bool nameDirty = true;

        // Visuals. Overlays live in the pawn's render tree (DynamicPawnRenderNodeSetup_Titan);
        // effecters are ticked by MapComponent_TitanVisuals while the pawn is on its map.
        [Unsaved] private List<EffecterDef> activeEffecterDefs;
        [Unsaved] private readonly List<Effecter> liveEffecters = new List<Effecter>();
        [Unsaved] private Map trackedMap;
        [Unsaved] private int effectersSpawnedTick = -1;

        // Sub-effecters stop spawning after their def's lifespanMaxTicks (default
        // 999999, about 16 days), so live effecters are respawned well before that.
        private const int EffecterRefreshTicks = 600000;

        public List<TitanTrait> TraitsForReading => traits;

        public TitanTrait GetTrait(TitanTraitDef def)
        {
            for (int i = 0; i < traits.Count; i++)
                if (traits[i].def == def) return traits[i];
            return null;
        }

        public bool HasTrait(TitanTraitDef def) => GetTrait(def) != null;

        public float TotalPointCost => traits.Sum(t => t.def.pointCost);

        // ---- trait list mutation (use TitanUtility from outside) ----------------

        internal TitanTrait AddTraitInternal(TitanTraitDef def, bool discovered)
        {
            var trait = (TitanTrait)Activator.CreateInstance(def.traitClass);
            trait.def = def;
            trait.titan = this;
            trait.discovered = discovered || def.visible;
            traits.Add(trait);
            ApplyEffects(trait);
            trait.PostAdded();
            TraitsChanged(def);
            return trait;
        }

        internal void RemoveTraitInternal(TitanTrait trait)
        {
            if (!traits.Contains(trait)) return;
            trait.PreRemoved();
            traits.Remove(trait);
            RemoveEffects(trait);
            TraitsChanged(trait.def);
        }

        private void ApplyEffects(TitanTrait trait)
        {
            var def = trait.def;
            if (def.hediffs != null)
            {
                foreach (var th in def.hediffs)
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
            if (!def.abilities.NullOrEmpty())
            {
                TitanUtility.EnsureAbilityTracker(pawn);
                foreach (var ab in def.abilities)
                    pawn.abilities.GainAbility(ab);
            }
        }

        private void RemoveEffects(TitanTrait trait)
        {
            var def = trait.def;
            if (def.hediffs != null)
            {
                foreach (var th in def.hediffs)
                {
                    // Another trait may add the same hediff; leave it if so.
                    if (OtherTraitHasHediff(trait, th)) continue;
                    var existing = pawn.health.hediffSet.hediffs.FirstOrDefault(
                        x => x.def == th.hediff && (th.part == null || x.Part?.def == th.part));
                    if (existing != null) pawn.health.RemoveHediff(existing);
                }
            }
            if (!def.abilities.NullOrEmpty() && pawn.abilities != null)
            {
                foreach (var ab in def.abilities)
                {
                    if (traits.Any(t => t.def.abilities != null && t.def.abilities.Contains(ab))) continue;
                    pawn.abilities.RemoveAbility(ab);
                }
            }
        }

        private bool OtherTraitHasHediff(TitanTrait self, TitanTraitHediff th)
        {
            foreach (var t in traits)
            {
                if (t == self || t.def.hediffs == null) continue;
                foreach (var o in t.def.hediffs)
                    if (o.hediff == th.hediff && o.part == th.part) return true;
            }
            return false;
        }

        // ---- visuals -------------------------------------------------------------

        public bool HasActiveEffecters
        {
            get
            {
                if (activeEffecterDefs == null)
                {
                    activeEffecterDefs = new List<EffecterDef>();
                    foreach (var t in traits)
                        if (t.VisualsActive && t.def.effecters != null)
                            foreach (var e in t.def.effecters)
                                if (!activeEffecterDefs.Contains(e)) activeEffecterDefs.Add(e);
                }
                return activeEffecterDefs.Count > 0;
            }
        }

        /// <summary>Call when which visuals should show may have changed.</summary>
        public void VisualsChanged(TitanTraitDef cause = null)
        {
            activeEffecterDefs = null;
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
                CleanupEffecters();
            if (liveEffecters.Count == 0)
            {
                float scale = Mathf.Clamp(Mathf.Sqrt(pawn.BodySize), 0.5f, 2.5f);
                foreach (var def in activeEffecterDefs)
                    liveEffecters.Add(def.SpawnAttached(pawn, pawn.Map, EffecterScaled(def) ? scale : 1f));
                effectersSpawnedTick = now;
            }
            for (int i = 0; i < liveEffecters.Count; i++)
                liveEffecters[i].EffectTick(pawn, pawn);
        }

        private bool EffecterScaled(EffecterDef def)
        {
            foreach (var t in traits)
                if (t.def.effecters != null && t.def.effecters.Contains(def))
                    return t.def.scaleEffectersWithBodySize;
            return true;
        }

        internal void CleanupEffecters()
        {
            for (int i = 0; i < liveEffecters.Count; i++) liveEffecters[i].Cleanup();
            liveEffecters.Clear();
            trackedMap = null;
        }

        private void TraitsChanged(TitanTraitDef changed)
        {
            if (changed.HasVisuals) VisualsChanged(changed);
            var oldStage = cachedStage;
            stageDirty = true;
            nameDirty = true;
            RecalcTicking();
            if (pawn?.health?.hediffSet == null || !pawn.health.hediffSet.hediffs.Contains(this)) return;
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

        internal void Notify_TraitDiscovered(TitanTrait trait)
        {
            nameDirty = true;
            if (trait.def.HasVisuals) VisualsChanged(trait.def);
            if (trait.def.hediffs == null) return;
            foreach (var th in trait.def.hediffs)
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
            for (int i = 0; i < traits.Count; i++)
                if (traits[i].def.Ticks) { anyTicking = true; break; }
        }

        // ---- stage ---------------------------------------------------------------

        public override HediffStage CurStage
        {
            get
            {
                if (stageDirty || cachedStage == null)
                {
                    cachedStage = BuildStage();
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
        protected virtual HediffStage BuildStage()
        {
            var s = new HediffStage { becomeVisible = true };
            Dictionary<StatDef, float> offsets = null, factors = null;
            Dictionary<DamageDef, float> dmg = null;

            foreach (var trait in traits)
            {
                var t = trait.def.stage;
                if (t == null) continue;

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
            if (nameDirty) RebuildNameCache();
            if (prefixCache == null && suffixCache == null) return kindLabel;
            return prefixCache + kindLabel + suffixCache;
        }

        private void RebuildNameCache()
        {
            nameDirty = false;
            prefixCache = null;
            suffixCache = null;
            var known = traits.Where(t => t.discovered).OrderBy(t => t.def.nameOrder).ToList();
            var pre = known.Where(t => !t.def.namePrefix.NullOrEmpty()).Select(t => t.def.namePrefix).ToList();
            // Suffixes read outward from the kind label, so the lowest nameOrder goes last.
            var suf = known.Where(t => !t.def.nameSuffix.NullOrEmpty()).Select(t => t.def.nameSuffix).Reverse().ToList();
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
                return unknown > 0
                    ? "TF_HediffBracketsUnknown".Translate(known, unknown).ToString()
                    : "TF_HediffBrackets".Translate(known).ToString();
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
                foreach (var t in traits)
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.AppendLine(t.Label.Colorize(t.discovered ? ColoredText.TipSectionTitleColor : ColoredText.SubtleGrayColor));
                    if (!t.Description.NullOrEmpty()) sb.AppendLine("  " + t.Description);
                    if (!t.discovered) continue;
                    if (t.def.stage != null)
                        foreach (StatDrawEntry e in HediffStatsUtility.SpecialDisplayStats(t.def.stage, this))
                            if (e.ShouldDisplay())
                                sb.AppendLine("  - " + e.LabelCap + ": " + e.ValueString);
                    string extra = t.TipStringExtra;
                    if (!extra.NullOrEmpty()) sb.AppendLine("  " + extra);
                }
                return sb.ToString().TrimEndNewlines();
            }
        }

        // ---- lifecycle -----------------------------------------------------------

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            TitanUtility.Register(pawn, this);
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
            base.PostRemoved();
            TitanUtility.Unregister(pawn);
            VisualsChanged();
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (anyTicking)
            {
                for (int i = traits.Count - 1; i >= 0; i--)
                    if (traits[i].def.Ticks) traits[i].TickInterval(delta);
            }
            // Hand effecters to the map component, which ticks them every tick.
            if (pawn.Spawned && trackedMap != pawn.Map && HasActiveEffecters)
            {
                pawn.Map.GetComponent<MapComponent_TitanVisuals>()?.Track(this);
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
                t.TryDiscoverBy(TitanDiscoveryTrigger.TakesDamage);
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
                t.TryDiscoverBy(TitanDiscoveryTrigger.Attacks);
                if (abilityDef != null && t.def.abilities != null && t.def.abilities.Contains(abilityDef))
                    t.TryDiscoverBy(TitanDiscoveryTrigger.UsesGrantedAbility);
                if (verbHediff != null && t.def.hediffs != null && t.def.hediffs.Any(h => h.hediff == verbHediff))
                    t.TryDiscoverBy(TitanDiscoveryTrigger.UsesGrantedVerb);
            }
        }

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            for (int i = traits.Count - 1; i >= 0; i--)
            {
                var t = traits[i];
                t.Notify_PawnDied(dinfo);
                t.TryDiscoverBy(TitanDiscoveryTrigger.Dies);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref traits, "traits", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                traits = traits ?? new List<TitanTrait>();
                traits.RemoveAll(t => t?.def == null);
                foreach (var t in traits) t.titan = this;
                stageDirty = true;
                nameDirty = true;
                RecalcTicking();
                TitanUtility.Register(pawn, this);
            }
        }
    }
}
