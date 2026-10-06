using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace TitanFramework
{
    /// <summary>
    /// Lets titans use their trait abilities on their own.
    ///
    /// Vanilla only casts abilities for creatures whose think tree names that ability
    /// (warcall, longjump, mech smokepop, the gorehulk's spines). Titan abilities are
    /// rolled, so no tree can name them ahead of time. Instead this job giver runs after
    /// the creature's constant think tree (every 30 ticks, the same slot mech smokepop
    /// uses) and can interrupt whatever it is doing to cast:
    ///
    /// 1. Each trait's own <c>aiThinkTree</c>, if it has one. Free-form: a content mod can
    ///    copy a vanilla ability's conditions exactly, in or out of combat.
    /// 2. In combat only: an offensive ability (<c>ai_IsOffensive</c>) at the creature's
    ///    current enemy, the same way JobGiver_AIFightEnemy.GetAbilityJob picks one.
    /// 3. In combat only: a self-targeted non-offensive ability (buffs, heals).
    ///
    /// Every ability still goes through vanilla's own checks: <c>aiCanUse</c>, cooldown,
    /// range, and each effect comp's AICanTargetNow. Tamed animals and player mechs cast
    /// too (they can't be ordered to); player colonists don't, they have the gizmo.
    /// </summary>
    public class JobGiver_TitanAbilities : ThinkNode_JobGiver
    {
        internal static readonly JobGiver_TitanAbilities Instance = new JobGiver_TitanAbilities();

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!TitanFrameworkMod.Settings.abilityAI) return null;
            var titan = TitanUtility.PeekTitan(pawn);
            if (titan == null || !CanActNow(pawn)) return null;

            var traits = titan.TraitsForReading;

            // 1. Per-trait think trees.
            for (int i = 0; i < traits.Count; i++)
            {
                var tree = traits[i].def.aiThinkTree;
                if (tree?.thinkRoot == null) continue;
                var result = tree.thinkRoot.TryIssueJobPackage(pawn, default);
                if (result.IsValid) return result.Job;
            }

            if (pawn.abilities == null) return null;
            Thing enemy = CurrentEnemy(pawn);
            if (enemy == null) return null;
            // Vanilla's GetAbilityJob gate: the caster must be able to stand where it is.
            if (!pawn.Position.WalkableBy(pawn.Map, pawn)
                || !pawn.Map.pawnDestinationReservationManager.CanReserve(pawn.Position, pawn, pawn.Drafted))
                return null;

            // 2. Offensive abilities at the enemy.
            for (int i = 0; i < traits.Count; i++)
            {
                var defs = traits[i].def.abilities;
                if (defs == null) continue;
                for (int j = 0; j < defs.Count; j++)
                {
                    if (!defs[j].ai_IsOffensive) continue;
                    var ability = pawn.abilities.GetAbility(defs[j]);
                    if (ability == null || !ability.AICanTargetNow(enemy)) continue;
                    if (ability.verb.CanHitTarget(enemy)) return ability.GetJob(enemy, enemy);
                    var aoe = ability.AIGetAOETarget();
                    if (aoe.IsValid) return ability.GetJob(aoe, aoe);
                }
            }

            // 3. Self-targeted abilities while fighting.
            for (int i = 0; i < traits.Count; i++)
            {
                var defs = traits[i].def.abilities;
                if (defs == null) continue;
                for (int j = 0; j < defs.Count; j++)
                {
                    if (defs[j].ai_IsOffensive || defs[j].verbProperties?.targetParams?.canTargetSelf != true) continue;
                    var ability = pawn.abilities.GetAbility(defs[j]);
                    if (ability != null && ability.AICanTargetNow(pawn)) return ability.GetJob(pawn, pawn);
                }
            }
            return null;
        }

        /// <summary>Awake, on its feet, not on fire, not drafted, not mid-cast, and not
        /// in a mental state other than an aggressive one (a manhunter still casts).</summary>
        private static bool CanActNow(Pawn pawn)
        {
            if (!pawn.Spawned || pawn.Dead || pawn.Downed || !pawn.Awake() || pawn.IsBurning()) return false;
            if (pawn.Drafted || pawn.IsColonist) return false;
            if (pawn.InMentalState && !pawn.InAggroMentalState) return false;
            if (pawn.CurJob?.ability != null) return false;
            if (pawn.stances?.FullBodyBusy == true) return false;
            return true;
        }

        /// <summary>The creature's current hostile target: the fight job givers' enemy
        /// target, the target of a melee or ranged attack job (manhunters), or whoever
        /// is attacking it in melee.</summary>
        private static Thing CurrentEnemy(Pawn pawn)
        {
            Thing t = pawn.mindState?.enemyTarget;
            if (t == null)
            {
                var job = pawn.CurJob;
                if (job != null && (job.def == JobDefOf.AttackMelee || job.def == JobDefOf.AttackStatic))
                    t = job.targetA.Thing;
            }
            if (t == null) t = pawn.mindState?.meleeThreat;
            if (t == null || !t.Spawned || t.Map != pawn.Map) return null;
            if (t is Pawn p && (p.Dead || p.Downed)) return null;
            // A wild animal hit by a hunter isn't hostile to it, but is fighting back.
            return t.HostileTo(pawn) || t == pawn.mindState?.meleeThreat ? t : null;
        }
    }

    /// <summary>
    /// Reveals a trait when its ability's visible lead-in effect starts. Some abilities
    /// (fire spew) play their effect as a pre-cast action a few ticks before the ability
    /// resolves, and vanilla cancels the cast if the target becomes invalid in that
    /// window: the player sees flames but the ability never fires, so the usual
    /// Notify_PawnUsedVerb discovery never runs. Each pre-cast action is wrapped so the
    /// reveal happens on the same tick the effect appears. Abilities with no lead-in
    /// are still revealed on use through Notify_PawnUsedVerb.
    /// </summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.Notify_StartedCasting))]
    internal static class Patch_AbilityStartedCasting
    {
        private static readonly AccessTools.FieldRef<Ability, List<PreCastAction>> PreCastActions =
            AccessTools.FieldRefAccess<Ability, List<PreCastAction>>("preCastActions");

        // Vanilla appends to the list and doesn't clear it when a cast job ends early, so
        // only the entries this call adds are wrapped. An unwrapped leftover is harmless:
        // this call's fresh entries share its ticksAwayFromCast, and vanilla fires every
        // entry that has reached its threshold in the same loop, so the reveal still
        // happens on that tick.
        static void Prefix(Ability __instance, out int __state) => __state = PreCastActions(__instance)?.Count ?? 0;

        static void Postfix(Ability __instance, int __state)
        {
            var titan = TitanUtility.GetTitan(__instance.pawn);
            if (titan == null || !titan.HasHiddenTraitGranting(__instance.def)) return;
            var list = PreCastActions(__instance);
            if (list == null) return;
            Pawn pawn = __instance.pawn;
            AbilityDef def = __instance.def;
            for (int i = __state; i < list.Count; i++)
            {
                var original = list[i].action;
                list[i] = new PreCastAction
                {
                    ticksAwayFromCast = list[i].ticksAwayFromCast,
                    action = (a, b) =>
                    {
                        original?.Invoke(a, b);
                        TitanUtility.GetTitan(pawn)?.Notify_AbilityEffectStarted(def);
                    },
                };
            }
        }
    }

    /// <summary>Runs the titan ability job giver whenever the constant think tree has
    /// nothing to do. Ordinary pawns cost one registry lookup per 30 ticks.</summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), "DetermineNextConstantThinkTreeJob")]
    internal static class Patch_TitanAbilityAI
    {
        static void Postfix(Pawn ___pawn, ref ThinkResult __result)
        {
            // Vanilla starts these jobs against the constant tree and saves the job giver's
            // key in it; with no constant tree a saved job would throw on load.
            // ConstantThinkNodeRoot itself throws when the race has no constant tree.
            var root = ___pawn.thinker?.ConstantThinkTree?.thinkRoot;
            if (__result.IsValid || root == null || TitanUtility.PeekTitan(___pawn) == null) return;
            try
            {
                var r = JobGiver_TitanAbilities.Instance.TryIssueJobPackage(___pawn, default);
                // Record the tree's root as the source: our node has no save key, so a
                // titan saved mid-cast would otherwise log a missing think node on load.
                if (r.IsValid) __result = new ThinkResult(r.Job, root, r.Tag, r.FromQueue);
            }
            catch (System.Exception e)
            {
                // Vanilla's constant-tree call has its own try/catch; ours runs after it.
                Log.ErrorOnce($"[Titan Framework] Ability AI threw for {___pawn.def.defName}: {e}", ___pawn.def.shortHash ^ 0x7A11);
            }
        }
    }
}
