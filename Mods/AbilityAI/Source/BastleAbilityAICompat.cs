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
    // Veto: on the player's own pawns, an ability whose every granting trait is still hidden. Its gizmo is hidden
    // (Patch_AbilityGizmosVisible), so the player can never set it to autocast, and Ability AI's player stand-down
    // would otherwise leave it uncast and the trait undiscovered. The veto hands it back to JobGiver_BastleAbilities.
    // That hand-back is Ability AI's side of the contract (requested in .circinus/handoff/ability-ai.md).
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
            var bastle = BastleUtility.GetBastle(pawn);
            if (bastle == null) return AbilityAIVote.NoOpinion;
            bool granted = false, known = false;
            var traits = bastle.TraitsForReading;
            for (int i = 0; i < traits.Count; i++)
            {
                var defs = traits[i].Abilities;
                if (defs == null || !defs.Contains(ability.def)) continue;
                granted = true;
                if (traits[i].discovered) { known = true; break; }
            }
            if (!granted) return AbilityAIVote.NoOpinion;
            return !known && pawn.Faction != null && pawn.Faction.IsPlayer ? AbilityAIVote.Veto : AbilityAIVote.Claim;
        }

        public void Notify_AICast(Pawn pawn, Ability ability, LocalTargetInfo target, LocalTargetInfo dest) { }
    }

    [StaticConstructorOnStartup]
    internal static class BastleAbilityAIStartup
    {
        static BastleAbilityAIStartup() => AbilityAIHooks.Register(BastleAbilityAIHook.Instance);
    }
}
