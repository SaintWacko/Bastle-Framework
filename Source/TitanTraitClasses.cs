using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace TitanFramework
{
    // Trait classes shipped with the framework. Each reads its numbers from an optional
    // mod extension on the trait def, so a content mod can reuse the class with its own
    // tuning:
    //   <traitClass>TitanFramework.TitanTrait_Phasing</traitClass>
    //   <modExtensions><li Class="TitanFramework.PhasingParams"><chance>0.5</chance></li></modExtensions>

    // ---- Undying -----------------------------------------------------------------

    public class UndyingParams : DefModExtension
    {
        /// <summary>How long after death it rises (ticks; 2500 is an hour).</summary>
        public IntRange riseDelayTicks = new IntRange(2500, 5000);

        /// <summary>After rising, it stays dead the next time it dies within this long.</summary>
        public int cooldownTicks = 900000;
    }

    /// <summary>Rises from death a few hours later, healed, unless the corpse is
    /// destroyed, butchered or has dried out first. Then not again for a while.</summary>
    public class TitanTrait_Undying : TitanTrait
    {
        private int lastDeathTick = -9999999;
        private bool enraged;

        private UndyingParams P => def.GetModExtension<UndyingParams>() ?? Defaults;
        private static readonly UndyingParams Defaults = new UndyingParams();

        public override void Notify_PawnDied(DamageInfo? dinfo)
        {
            var pawn = Pawn;
            if (pawn?.Corpse == null) return;
            int now = Find.TickManager.TicksGame;
            if (now - lastDeathTick < P.cooldownTicks) return;
            lastDeathTick = now;
            // A wild one killed by someone comes back for them.
            enraged = pawn.Faction == null && dinfo?.Instigator is Pawn killer && killer.Faction != null;
            TitanGameComponent.Instance?.ScheduleRise(pawn, now + P.riseDelayTicks.RandomInRange);
        }

        internal static void Rise(Pawn pawn)
        {
            if (pawn == null || !pawn.Dead || pawn.Discarded) return;
            var corpse = pawn.Corpse;
            if (corpse == null || corpse.Destroyed || !corpse.SpawnedOrAnyParentSpawned) return;
            if (corpse.GetRotStage() == RotStage.Dessicated) return;
            if (!ResurrectionUtility.TryResurrect(pawn)) return;

            var trait = TitanUtility.GetTitan(pawn)?.TraitsForReading.OfType<TitanTrait_Undying>().FirstOrDefault();
            if (pawn.Spawned)
            {
                FleckMaker.ThrowSmoke(pawn.DrawPos, pawn.Map, 2f);
                FleckMaker.Static(pawn.Position, pawn.Map, FleckDefOf.PsycastSkipInnerExit, 1.5f);
                if (trait != null && trait.enraged)
                    pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter, forced: true, forceWake: true);
                if (TitanUtility.PlayerCanObserve(pawn))
                {
                    trait?.Discover();
                    Messages.Message("TF_UndyingRose".Translate(pawn.Named("PAWN")), pawn,
                        pawn.HostileTo(Faction.OfPlayer) ? MessageTypeDefOf.ThreatSmall : MessageTypeDefOf.PositiveEvent);
                }
            }
        }

        public override void CopyStateFrom(TitanTrait source)
        {
            if (source is TitanTrait_Undying u) lastDeathTick = u.lastDeathTick;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastDeathTick, "lastDeathTick", -9999999);
            Scribe_Values.Look(ref enraged, "enraged");
        }
    }

    // ---- Phasing -----------------------------------------------------------------

    public class PhasingParams : DefModExtension
    {
        /// <summary>Chance per hit taken.</summary>
        public float chance = 0.3f;
        public int cooldownTicks = 600;
        public int minDistance = 3;
        public int maxDistance = 8;
    }

    /// <summary>When hit, may blink a few tiles away.</summary>
    public class TitanTrait_Phasing : TitanTrait
    {
        private int nextBlinkTick;

        private PhasingParams P => def.GetModExtension<PhasingParams>() ?? Defaults;
        private static readonly PhasingParams Defaults = new PhasingParams();

        public override void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            var pawn = Pawn;
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed) return;
            int now = Find.TickManager.TicksGame;
            if (now < nextBlinkTick || !Rand.Chance(P.chance)) return;
            nextBlinkTick = now + P.cooldownTicks;
            // Moving the pawn mid-damage would confuse the attacker's verb; wait a tick.
            TitanScheduler.NextTick(() => Blink(pawn));
        }

        private void Blink(Pawn pawn)
        {
            if (!pawn.Spawned || pawn.Dead || pawn.Downed) return;
            var map = pawn.Map;
            var from = pawn.Position;
            var p = P;
            if (!CellFinder.TryFindRandomCellNear(from, map, p.maxDistance,
                    c => c.Standable(map) && !c.Fogged(map) && c.DistanceTo(from) >= p.minDistance
                         && c.GetFirstPawn(map) == null && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly),
                    out var to))
                return;
            FleckMaker.Static(from, map, FleckDefOf.PsycastSkipFlashEntry);
            SoundDefOf.Psycast_Skip_Entry.PlayOneShot(new TargetInfo(from, map));
            pawn.Position = to;
            pawn.Notify_Teleported();
            FleckMaker.Static(to, map, FleckDefOf.PsycastSkipInnerExit);
            if (!discovered && TitanUtility.PlayerCanObserve(pawn)) Discover();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextBlinkTick, "nextBlinkTick");
        }
    }

    // ---- Pack-caller -------------------------------------------------------------

    public class PackCallerParams : DefModExtension
    {
        public float radius = 35f;
        public int cooldownTicks = 2500;
    }

    /// <summary>When attacked, others of its kind and faction nearby join the fight
    /// against the attacker. Wild packmates turn manhunter against a factioned attacker,
    /// as wild animals do when they take revenge.</summary>
    public class TitanTrait_PackCaller : TitanTrait
    {
        private int nextCallTick;

        private PackCallerParams P => def.GetModExtension<PackCallerParams>() ?? Defaults;
        private static readonly PackCallerParams Defaults = new PackCallerParams();

        public override void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            var pawn = Pawn;
            if (pawn == null || !pawn.Spawned || pawn.Dead) return;
            if (!(dinfo.Instigator is Pawn attacker) || attacker == pawn || attacker.def == pawn.def) return;
            int now = Find.TickManager.TicksGame;
            if (now < nextCallTick) return;
            nextCallTick = now + P.cooldownTicks;
            TitanScheduler.NextTick(() => Call(pawn, attacker));
        }

        private void Call(Pawn pawn, Pawn attacker)
        {
            if (!pawn.Spawned || pawn.Dead || attacker.Dead || !attacker.Spawned || attacker.Map != pawn.Map) return;
            int called = 0;
            float radius = P.radius;
            foreach (var p in pawn.Map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (p == pawn || p.def != pawn.def || p.Faction != pawn.Faction) continue;
                if (p.Dead || p.Downed || p.Drafted || p.InAggroMentalState || p.roping?.IsRoped == true) continue;
                if (!p.Position.InHorDistOf(pawn.Position, radius)) continue;
                if (p.Faction == null && attacker.Faction != null)
                {
                    if (p.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter, forced: true, forceWake: true))
                        called++;
                }
                else if (p.Faction == null || attacker.HostileTo(p))
                {
                    RestUtility.WakeUp(p);
                    var job = JobMaker.MakeJob(JobDefOf.AttackMelee, attacker);
                    job.expiryInterval = 2500;
                    job.checkOverrideOnExpire = true;
                    p.jobs.StartJob(job, JobCondition.InterruptForced);
                    called++;
                }
            }
            LifeStageUtility.PlayNearestLifestageSound(pawn, ls => ls.soundCall, g => null, m => null, 1.5f);
            if (called > 0 && TitanUtility.PlayerCanObserve(pawn))
            {
                if (!discovered) Discover();
                Messages.Message("TF_PackCalled".Translate(pawn.Named("PAWN"), called), pawn, MessageTypeDefOf.NeutralEvent);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextCallTick, "nextCallTick");
        }
    }

    // ---- Dread aura --------------------------------------------------------------

    public class DreadAuraParams : DefModExtension
    {
        public float radius = 7.9f;

        /// <summary>Given to enemies in range and refreshed while they stay there. It
        /// should have a HediffComp_Disappears so it fades once they leave.</summary>
        public HediffDef hediff;
    }

    /// <summary>Living enemies near it are gripped by dread (TF_DreadStruck by default).
    /// Mechanoids and Anomaly entities are immune.</summary>
    public class TitanTrait_DreadAura : TitanTrait
    {
        private DreadAuraParams P => def.GetModExtension<DreadAuraParams>() ?? Defaults;
        private static readonly DreadAuraParams Defaults = new DreadAuraParams();

        public override void TickInterval(int delta)
        {
            var pawn = Pawn;
            if (pawn == null || !pawn.Spawned || pawn.Downed || !pawn.IsHashIntervalTick(250, delta)) return;
            var hediff = P.hediff ?? TitanDefOf.TF_DreadStruck;
            float radius = P.radius;
            bool any = false;
            var pawns = pawn.Map.mapPawns.AllPawnsSpawned;
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                var p = pawns[i];
                if (p == pawn || p.Dead || !p.RaceProps.IsFlesh || p.RaceProps.IsAnomalyEntity) continue;
                if (!p.Position.InHorDistOf(pawn.Position, radius) || !p.HostileTo(pawn)) continue;
                var h = p.health.hediffSet.GetFirstHediffOfDef(hediff);
                if (h == null) p.health.AddHediff(hediff);
                else h.TryGetComp<HediffComp_Disappears>()?.ResetElapsedTicks();
                any = true;
            }
            if (any && !discovered && TitanUtility.PlayerCanObserve(pawn)) Discover();
        }
    }

    // ---- Lightsick ---------------------------------------------------------------

    public class LightsickParams : DefModExtension
    {
        /// <summary>Ground light level at which the stage applies (0.5 is lit).</summary>
        public float minGlow = 0.5f;
    }

    /// <summary>The trait's stage only applies while the creature stands in light.</summary>
    public class TitanTrait_Lightsick : TitanTrait
    {
        private bool inLight;

        private LightsickParams P => def.GetModExtension<LightsickParams>() ?? Defaults;
        private static readonly LightsickParams Defaults = new LightsickParams();

        public override HediffStage Stage => inLight ? base.Stage : null;

        public override void TickInterval(int delta)
        {
            var pawn = Pawn;
            if (pawn == null || !pawn.IsHashIntervalTick(250, delta)) return;
            bool lit = pawn.Spawned && pawn.Map.glowGrid.GroundGlowAt(pawn.Position) >= P.minGlow;
            if (lit == inLight) return;
            inLight = lit;
            titan?.StageChanged();
        }

        public override string TipStringExtra => inLight ? "TF_LightsickActive".Translate().ToString() : null;
    }

    // ---- Regrowth ----------------------------------------------------------------

    public class RegrowthParams : DefModExtension
    {
        /// <summary>Lowest rarity at which lost parts grow back.</summary>
        public TitanRarity minRarity = TitanRarity.Legendary;
        public int intervalTicks = 7500;
    }

    /// <summary>From minRarity up, regrows one lost body part every interval.</summary>
    public class TitanTrait_Regrowth : TitanTrait
    {
        private RegrowthParams P => def.GetModExtension<RegrowthParams>() ?? Defaults;
        private static readonly RegrowthParams Defaults = new RegrowthParams();

        public override void TickInterval(int delta)
        {
            var p = P;
            if (Rarity < p.minRarity) return;
            var pawn = Pawn;
            if (pawn == null || pawn.Dead || !pawn.IsHashIntervalTick(p.intervalTicks, delta)) return;
            var hs = pawn.health.hediffSet;
            var missing = hs.GetMissingPartsCommonAncestors().FirstOrDefault(x => !hs.PartOrAnyAncestorHasDirectlyAddedParts(x.Part));
            if (missing == null) return;
            var part = missing.Part;
            // Health ticks iterate a copy of the hediff list, so this is safe here.
            pawn.health.RestorePart(part);
            if (TitanUtility.PlayerCanObserve(pawn))
            {
                if (!discovered) Discover();
                if (pawn.Faction == Faction.OfPlayer)
                    Messages.Message("TF_PartRegrown".Translate(pawn.Named("PAWN"), part.Label), pawn, MessageTypeDefOf.PositiveEvent);
            }
        }
    }

    // ---- Kindling ----------------------------------------------------------------

    public class KindlingParams : DefModExtension
    {
        public float igniteChance = 0.5f;
    }

    /// <summary>While the creature is on fire, its melee hits may set the target alight.</summary>
    public class TitanTrait_Kindling : TitanTrait
    {
        private KindlingParams P => def.GetModExtension<KindlingParams>() ?? Defaults;
        private static readonly KindlingParams Defaults = new KindlingParams();

        public override void Notify_PawnDamagedThing(Thing thing, DamageInfo dinfo, DamageWorker.DamageResult result)
        {
            var pawn = Pawn;
            if (pawn == null || TitanCombatUtility.InExtraDamage || !pawn.IsBurning()) return;
            if (!TitanCombatUtility.IsMelee(dinfo, thing, out _) || !Rand.Chance(P.igniteChance)) return;
            TitanScheduler.NextTick(() =>
            {
                if (thing.Spawned && !thing.Destroyed) thing.TryAttachFire(Rand.Range(0.15f, 0.3f), pawn);
            });
        }
    }

    // ---- Bane damage (Argent) ----------------------------------------------------

    public class BaneDamageParams : DefModExtension
    {
        /// <summary>Extra damage as a fraction of the damage dealt.</summary>
        public float factor = 0.5f;
        public bool entities = true;
        public bool mutants = true;
        public bool mechanoids = false;
        public bool meleeOnly = true;
    }

    /// <summary>Extra damage against certain kinds of creature (by default Anomaly
    /// entities and mutants such as shamblers and ghouls).</summary>
    public class TitanTrait_BaneDamage : TitanTrait
    {
        private BaneDamageParams P => def.GetModExtension<BaneDamageParams>() ?? Defaults;
        private static readonly BaneDamageParams Defaults = new BaneDamageParams();

        public override void Notify_PawnDamagedThing(Thing thing, DamageInfo dinfo, DamageWorker.DamageResult result)
        {
            if (TitanCombatUtility.InExtraDamage || result == null || result.totalDamageDealt <= 0f) return;
            var p = P;
            if (!(thing is Pawn victim) || !IsBane(victim, p)) return;
            if (p.meleeOnly && !TitanCombatUtility.IsMelee(dinfo, victim, out _)) return;
            float extra = result.totalDamageDealt * p.factor * def.StrengthFrom(def.rarity, Rarity);
            if (extra < 0.5f) return;
            var pawn = Pawn;
            var bonus = new DamageInfo(dinfo.Def, extra, dinfo.ArmorPenetrationInt, dinfo.Angle, pawn, null, dinfo.Weapon);
            TitanScheduler.NextTick(() =>
            {
                if (!victim.Dead && victim.Spawned) TitanCombatUtility.DealExtraDamage(victim, bonus);
            });
            if (!discovered && TitanUtility.PlayerCanObserve(pawn)) Discover();
        }

        private static bool IsBane(Pawn victim, BaneDamageParams p)
            => (p.entities && victim.RaceProps.IsAnomalyEntity)
               || (p.mutants && victim.IsMutant)
               || (p.mechanoids && victim.RaceProps.IsMechanoid);
    }

    // ---- Hydra -------------------------------------------------------------------

    public class HydraParams : DefModExtension
    {
        public int count = 2;
    }

    /// <summary>When it dies, young copies of it (every other trait, its material and
    /// what the player knew about it) spring up beside the corpse. The copies don't get
    /// this trait, so it happens once.</summary>
    public class TitanTrait_Hydra : TitanTrait
    {
        private HydraParams P => def.GetModExtension<HydraParams>() ?? Defaults;
        private static readonly HydraParams Defaults = new HydraParams();

        public override void Notify_PawnDied(DamageInfo? dinfo)
        {
            var pawn = Pawn;
            if (pawn?.Corpse == null || !pawn.Corpse.Spawned) return;
            Pawn killer = dinfo?.Instigator as Pawn;
            int count = P.count;
            var ownDef = def;
            // Inside Pawn.Kill: spawn on the next tick.
            TitanScheduler.NextTick(() => Split(pawn, killer, count, ownDef));
        }

        private static void Split(Pawn pawn, Pawn killer, int count, TitanTraitDef ownDef)
        {
            var corpse = pawn.Corpse;
            if (corpse == null || !corpse.Spawned) return;
            var map = corpse.Map;
            var pos = corpse.Position;
            var stages = pawn.RaceProps.lifeStageAges;
            float age = stages.Count >= 2 ? stages[1].minAge : 0f;
            var spawned = new List<Pawn>();
            for (int i = 0; i < count; i++)
            {
                var req = new PawnGenerationRequest(pawn.kindDef, pawn.Faction, forceGenerateNewPawn: true,
                    fixedBiologicalAge: age, fixedChronologicalAge: age);
                Pawn child = PawnGenerator.GeneratePawn(req);
                TitanUtility.CopyTitan(pawn, child, includeDiscovery: true);
                TitanUtility.RemoveTrait(child, ownDef);
                GenSpawn.Spawn(child, CellFinder.RandomClosewalkCellNear(pos, map, 2), map);
                FleckMaker.ThrowSmoke(child.DrawPos, map, 1.5f);
                spawned.Add(child);
            }
            if (spawned.Count == 0) return;
            if (pawn.Faction == null)
            {
                if (killer?.Faction != null)
                    foreach (var c in spawned)
                        c.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter, forced: true, forceWake: true);
            }
            else if (pawn.Faction != Faction.OfPlayer && pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                LordMaker.MakeNewLord(pawn.Faction, new LordJob_AssaultColony(pawn.Faction), map, spawned);
            }
            if (spawned.Any(c => TitanUtility.PlayerCanObserve(c)))
                Messages.Message("TF_HydraSplit".Translate(pawn.Named("PAWN"), spawned.Count), new LookTargets(spawned),
                    pawn.Faction == Faction.OfPlayer ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.ThreatSmall);
        }
    }
}
