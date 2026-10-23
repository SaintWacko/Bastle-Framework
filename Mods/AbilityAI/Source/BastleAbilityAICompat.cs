using AbilityAI;
using RimWorld;
using Verse;

namespace BastleFramework.AbilityAICompat
{
    // Bastle's side of the Ability AI mod (saintwacko.AbilityAI). This assembly lives in Mods/AbilityAI/, which
    // LoadFolders.xml loads only when Ability AI is active, so it is the one place allowed to name Ability AI types.
    // The main BastleFramework.dll must not: a type there with a base, interface or field from AbilityAI.dll would make
    // RimWorld skip the whole assembly when Ability AI is missing (ReflectionTypeLoadException).
    //
    // Claim: one of the bastle's traits grants the ability. Anything else gets no opinion, so a bastle raider's own
    // psycasts are left to Ability AI's "abilities no mod claims" setting.
    // Veto: on the player's own pawns, an ability with no gizmo: every granting trait still hidden, or a tamed animal
    // that hasn't learned BF_TraitAbilities (Patch_AbilityGizmosVisible). The player can never set those to autocast,
    // and Ability AI's player stand-down would otherwise leave them uncast and hidden traits undiscovered. The veto
    // hands them back to JobGiver_BastleAbilities, which applies Bastle's instinct and training settings. That
    // hand-back is Ability AI's side of the contract (requested in .circinus/handoff/ability-ai.md).
    // Once the gizmo shows, Ability AI's autocast toggle decides, so a trained animal's abilities that Ability AI has
    // rules for are exempt from Bastle's "trained bastles also use abilities on their own" setting.
    //
    // Reveal needs nothing here: Patch_AbilityStartedCasting and Notify_PawnUsedVerb fire for Ability AI's casts the
    // same as for Bastle's own.
    public class BastleAbilityAIHook : IAbilityAIHook
    {
        public static readonly BastleAbilityAIHook Instance = new BastleAbilityAIHook();

        // Asked for every ready ability with an extension on every evaluated pawn (at most once per 30 ticks each),
        // so this stays a registry lookup and one pass over the traits.
        public AbilityAIVote Vote(Pawn pawn, Ability ability)
        {
            var control = BastleAbilityTraining.ControlOf(pawn, ability.def);
            if (control == BastleAbilityControl.NotFromTraits) return AbilityAIVote.NoOpinion;
            bool noGizmo = control == BastleAbilityControl.Hidden || control == BastleAbilityControl.Untrained;
            return noGizmo && pawn.Faction != null && pawn.Faction.IsPlayer ? AbilityAIVote.Veto : AbilityAIVote.Claim;
        }

        public void Notify_AICast(Pawn pawn, Ability ability, LocalTargetInfo target, LocalTargetInfo dest) { }
    }

    [StaticConstructorOnStartup]
    internal static class BastleAbilityAIStartup
    {
        static BastleAbilityAIStartup()
        {
            AbilityAIHooks.Register(BastleAbilityAIHook.Instance);
            BastleAbilityTraining.HasExternalAutocast = def => def.HasModExtension<AbilityAIExtension>();
        }
    }
}
