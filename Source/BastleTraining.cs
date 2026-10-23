using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BastleFramework
{
    /// <summary>Who decides when a bastle's trait ability is cast.</summary>
    public enum BastleAbilityControl
    {
        /// <summary>No trait grants it (a race or gene ability): Bastle leaves it alone.</summary>
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
            if (!known) return BastleAbilityControl.Hidden;
            if (NeedsTraining(pawn) && !IsTrained(pawn)) return BastleAbilityControl.Untrained;
            return BastleAbilityControl.Player;
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
}
