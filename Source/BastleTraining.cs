using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BastleFramework
{
    /// <summary>Who decides when a bastle's trait ability is cast.</summary>
    public enum BastleAbilityControl
    {
        /// <summary>No trait grants it, or something else does as well (a gene, a psylink, a
        /// title, gear): Bastle leaves it alone.</summary>
        NotFromTraits,
        /// <summary>Every granting trait is undiscovered: no gizmo, always cast by instinct, which reveals the trait.</summary>
        Hidden,
        /// <summary>Known, but the animal hasn't learned BF_TraitAbilities: no gizmo, cast on its own if the settings allow.</summary>
        Untrained,
        /// <summary>The gizmo shows: a trained animal, or a known ability on a pawn that needs no training.</summary>
        Player,
    }

    /// <summary>
    /// The trait abilities trainable. A tamed bastle only gets gizmos for its known trait
    /// abilities once it has learned BF_TraitAbilities; until then it may still cast them
    /// on its own (setting), and abilities from hidden traits are always cast by instinct,
    /// since use is how those traits are discovered. Only animals train: humanlike and mech
    /// bastles get their gizmos as soon as the trait is known.
    /// </summary>
    public static class BastleAbilityTraining
    {
        /// <summary>Set by an integration that gives the player its own autocast toggle for
        /// an ability (the Ability AI compat): true when that integration is the one deciding
        /// whether this pawn casts this ability on its own, so the "trained bastles also use
        /// abilities on their own" setting stays out of its way.</summary>
        public static Func<Pawn, Ability, bool> HasExternalAutocast;

        public static bool NeedsTraining(Pawn pawn) => pawn?.training != null && pawn.RaceProps.Animal;

        public static bool IsTrained(Pawn pawn) => pawn.training.HasLearned(BastleDefOf.BF_TraitAbilities);

        public static BastleAbilityControl ControlOf(Pawn pawn, AbilityDef def)
        {
            var bastle = BastleUtility.PeekBastle(pawn);
            return bastle == null ? BastleAbilityControl.NotFromTraits : ControlOf(bastle, pawn, def);
        }

        internal static BastleAbilityControl ControlOf(Hediff_Bastle bastle, Pawn pawn, AbilityDef def)
        {
            bool granted = false, known = false;
            var traits = bastle.TraitsForReading;
            for (int i = 0; i < traits.Count; i++)
            {
                var defs = traits[i].Abilities;
                if (defs == null || !defs.Contains(def)) continue;
                granted = true;
                if (traits[i].discovered) { known = true; break; }
            }
            if (!granted) return BastleAbilityControl.NotFromTraits;
            bool untrained = known && NeedsTraining(pawn) && !IsTrained(pawn);
            if (known && !untrained) return BastleAbilityControl.Player;
            // Only asked when the trait would hold the ability back, so the common case pays nothing.
            if (HasOtherSource(bastle, pawn, def)) return BastleAbilityControl.NotFromTraits;
            return known ? BastleAbilityControl.Untrained : BastleAbilityControl.Hidden;
        }

        /// <summary>Whether something besides a bastle trait gives the pawn this ability: its
        /// kind, a gene, a vanilla trait, a psylink, a hediff, gear, a title or a role. Vanilla
        /// shows and casts those as usual, so a trait granting the same def must not hide or
        /// hold it back, nor take it away when the trait goes. Another mod's own GainAbility
        /// call outside these isn't seen. With <paramref name="sharedEntryOnly"/>, only the
        /// sources that rely on the same tracker entry the trait made count: a title's or
        /// gear's own copy doesn't keep the trait's entry alive when the trait goes.</summary>
        internal static bool HasOtherSource(Hediff_Bastle bastle, Pawn pawn, AbilityDef def, bool sharedEntryOnly = false)
        {
            var tracker = pawn.abilities;
            if (tracker == null) return false;
            // Hediff abilities, equipment, apparel, titles, roles and mutations are their own
            // instances, outside the list GainAbility (and so a trait) fills.
            if (!sharedEntryOnly)
            {
                var all = tracker.AllAbilitiesForReading;
                for (int i = 0; i < all.Count; i++)
                    if (all[i].def == def && !tracker.abilities.Contains(all[i])) return true;
            }

            if (pawn.kindDef?.abilities?.Contains(def) == true) return true;
            if (def.IsPsycast && PsylinkSlot(pawn, tracker, def)) return true;
            var creep = pawn.creepjoiner;
            if (creep != null && (creep.benefit?.abilities?.Contains(def) == true
                || creep.downside?.abilities?.Contains(def) == true)) return true;
            if (pawn.genes != null)
            {
                var genes = pawn.genes.GenesListForReading;
                for (int i = 0; i < genes.Count; i++)
                    if (genes[i].def.abilities?.Contains(def) == true) return true;
            }
            var storyTraits = pawn.story?.traits?.allTraits;
            if (storyTraits != null)
                for (int i = 0; i < storyTraits.Count; i++)
                    if (storyTraits[i].CurrentData.abilities?.Contains(def) == true) return true;
            var hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs != null)
                for (int i = 0; i < hediffs.Count; i++)
                {
                    var comps = hediffs[i].def.comps;
                    if (comps == null || bastle.TraitGivesHediff(hediffs[i].def)) continue;
                    for (int j = 0; j < comps.Count; j++)
                        if (comps[j] is HediffCompProperties_GiveAbility give
                            && (give.abilityDef == def || give.abilityDefs?.Contains(def) == true)) return true;
                }
            return false;
        }

        // A psylink gives one psycast per level up to its own, but skips a level the list
        // already has a psycast for. So a trait's psycast counts as the psylink's only when
        // it is in range and the only one of its level: then the psylink has none there.
        private static bool PsylinkSlot(Pawn pawn, Pawn_AbilityTracker tracker, AbilityDef def)
        {
            if (!pawn.HasPsylink || pawn.GetPsylinkLevel() < def.level) return false;
            var list = tracker.abilities;
            for (int i = 0; i < list.Count; i++)
            {
                var other = list[i].def;
                if (other != def && other.IsPsycast && other.level == def.level) return false;
            }
            return true;
        }

        /// <summary>Whether any AI may cast this ability for the pawn without an order.
        /// Only the player's own pawns are ever held back, and not during a mental break.</summary>
        public static bool MayCastOnOwn(Pawn pawn, Ability ability)
        {
            if (pawn.Faction == null || !pawn.Faction.IsPlayer || pawn.InMentalState) return true;
            var bastle = BastleUtility.PeekBastle(pawn);
            if (bastle == null) return true;
            switch (ControlOf(bastle, pawn, ability.def))
            {
                case BastleAbilityControl.Untrained:
                    return BastleFrameworkMod.Settings.untrainedKnownAbilities;
                case BastleAbilityControl.Player:
                    return !NeedsTraining(pawn) || BastleFrameworkMod.Settings.trainedAbilitiesOnOwn
                        || (HasExternalAutocast?.Invoke(pawn, ability) ?? false);
                default:
                    return true;
            }
        }

        /// <summary>The same rule for a trait's own aiThinkTree, which can cast anything it
        /// likes. Bastle runs those trees itself, so no integration's autocast applies.</summary>
        internal static bool MayRunTreeOnOwn(Pawn pawn, BastleTrait trait)
        {
            if (!trait.discovered || pawn.Faction == null || !pawn.Faction.IsPlayer || pawn.InMentalState
                || !NeedsTraining(pawn)) return true;
            return IsTrained(pawn)
                ? BastleFrameworkMod.Settings.trainedAbilitiesOnOwn
                : BastleFrameworkMod.Settings.untrainedKnownAbilities;
        }

        internal static bool HasKnownAbilityTrait(Pawn pawn)
        {
            var bastle = BastleUtility.PeekBastle(pawn);
            if (bastle == null) return false;
            var traits = bastle.TraitsForReading;
            for (int i = 0; i < traits.Count; i++)
                if (traits[i].discovered && !traits[i].Abilities.NullOrEmpty()) return true;
            return false;
        }
    }

    /// <summary>BF_TraitAbilities is not a default trainable, so vanilla hides it from every
    /// animal; show it for bastles with a known ability trait, with vanilla's trainability
    /// check. Untrainable tags still win.</summary>
    [HarmonyPatch(typeof(Pawn_TrainingTracker), nameof(Pawn_TrainingTracker.CanAssignToTrain),
        new[] { typeof(TrainableDef), typeof(ThingDef), typeof(bool), typeof(Pawn) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
    internal static class Patch_CanAssignToTrain
    {
        static void Postfix(TrainableDef td, ThingDef pawnDef, Pawn pawn, ref bool visible, ref AcceptanceReport __result)
        {
            if (td != BastleDefOf.BF_TraitAbilities) return;
            // BackCompatibility's PostLoadInit wipes the steps of any trainable this refuses,
            // and the bastle registry may not hold the pawn yet at that point.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                visible = true;
                __result = true;
                return;
            }
            var untrainable = pawnDef.race?.untrainableTags;
            if (untrainable != null)
                for (int i = 0; i < untrainable.Count; i++)
                    if (td.MatchesTag(untrainable[i])) return;
            if (pawn == null || !BastleAbilityTraining.HasKnownAbilityTrait(pawn))
            {
                visible = false;
                __result = false;
                return;
            }
            visible = true;
            var trainability = TrainableUtility.GetTrainability(pawn) ?? pawnDef.race.trainability;
            if (pawn.BodySize < td.minBodySize)
                __result = new AcceptanceReport("CannotTrainTooSmall".Translate(pawn.LabelShort).Resolve());
            else if (td.requiredTrainability != null && trainability != null
                && trainability.intelligenceOrder < td.requiredTrainability.intelligenceOrder)
                __result = new AcceptanceReport("CannotTrainNotSmartEnough".Translate(td.requiredTrainability.label).Resolve());
            else
                __result = true;
        }
    }

    /// <summary>Holds back vanilla's AI and JobGiver_BastleAbilities (both ask
    /// AICanTargetNow) on the player's bastles, per BastleAbilityTraining.MayCastOnOwn.</summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.AICanTargetNow))]
    internal static class Patch_AbilityAICanTargetNow_Training
    {
        static void Postfix(Ability __instance, ref bool __result)
        {
            if (__result && __instance.pawn != null && !BastleAbilityTraining.MayCastOnOwn(__instance.pawn, __instance))
                __result = false;
        }
    }

    /// <summary>A gene, vanilla trait or hediff that gives the same ability as a bastle trait
    /// removes the one shared tracker entry when it goes, which would leave the trait's
    /// ability gone for good. Put the same instance back while a trait still grants it, so
    /// its cooldown and charges carry on.</summary>
    [HarmonyPatch(typeof(Pawn_AbilityTracker), nameof(Pawn_AbilityTracker.RemoveAbility))]
    internal static class Patch_RemoveAbility_KeepTraitAbility
    {
        static void Prefix(Pawn_AbilityTracker __instance, AbilityDef def, out Ability __state)
        {
            __state = null;
            if (def == null) return;
            var bastle = BastleUtility.PeekBastle(__instance.pawn);
            if (bastle == null || !bastle.TraitGrantsAbility(def)) return;
            __state = __instance.GetAbility(def);
        }

        static void Postfix(Pawn_AbilityTracker __instance, Ability __state)
        {
            if (__state == null || __instance.abilities.Contains(__state)) return;
            __instance.abilities.Add(__state);
            __instance.Notify_TemporaryAbilitiesChanged();
        }
    }
}
