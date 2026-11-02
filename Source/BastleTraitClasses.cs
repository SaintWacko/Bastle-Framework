using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace BastleFramework
{
    // Trait classes shipped with the framework. Each reads its numbers from an optional
    // mod extension on the trait def, so a content mod can reuse the class with its own
    // tuning:
    //   <traitClass>BastleFramework.BastleTrait_Phasing</traitClass>
    //   <modExtensions><li Class="BastleFramework.PhasingParams"><chance>0.5</chance></li></modExtensions>

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
    public class BastleTrait_Undying : BastleTrait
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
            BastleGameComponent.Instance?.ScheduleRise(pawn, now + P.riseDelayTicks.RandomInRange);
        }

        internal static void Rise(Pawn pawn)
        {
            if (pawn == null || !pawn.Dead || pawn.Discarded) return;
            var corpse = pawn.Corpse;
            if (corpse == null || corpse.Destroyed || !corpse.SpawnedOrAnyParentSpawned) return;
            if (corpse.GetRotStage() == RotStage.Dessicated) return;
            if (!ResurrectionUtility.TryResurrect(pawn)) return;

            var trait = BastleUtility.GetBastle(pawn)?.TraitsForReading.OfType<BastleTrait_Undying>().FirstOrDefault();
            if (pawn.Spawned)
            {
                FleckMaker.ThrowSmoke(pawn.DrawPos, pawn.Map, 2f);
                FleckMaker.Static(pawn.Position, pawn.Map, FleckDefOf.PsycastSkipInnerExit, 1.5f);
                if (trait != null && trait.enraged)
                    pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter, forced: true, forceWake: true);
                if (BastleUtility.PlayerCanObserve(pawn))
                {
                    trait?.Discover();
                    Messages.Message("BF_UndyingRose".Translate(pawn.Named("PAWN")), pawn,
                        pawn.HostileTo(Faction.OfPlayer) ? MessageTypeDefOf.ThreatSmall : MessageTypeDefOf.PositiveEvent);
                }
            }
        }

        public override void CopyStateFrom(BastleTrait source)
        {
            if (source is BastleTrait_Undying u) lastDeathTick = u.lastDeathTick;
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
    public class BastleTrait_Phasing : BastleTrait
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
            BastleScheduler.NextTick(() => Blink(pawn));
        }

        private void Blink(Pawn pawn)
        {
            if (!pawn.Spawned || pawn.Dead || pawn.Downed || pawn.roping?.IsRoped == true) return;
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
            if (!discovered && BastleUtility.PlayerCanObserve(pawn)) Discover();
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
    public class BastleTrait_PackCaller : BastleTrait
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
            BastleScheduler.NextTick(() => Call(pawn, attacker));
        }

        private void Call(Pawn pawn, Pawn attacker)
        {
            if (!pawn.Spawned || pawn.Dead || attacker.Dead || !attacker.Spawned || attacker.Map != pawn.Map) return;
            int called = 0;
            float radius = P.radius;
            foreach (var p in pawn.Map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (p == pawn || p.def != pawn.def || p.Faction != pawn.Faction) continue;
                if (p.Dead || p.Downed || p.Drafted || p.InMentalState || p.roping?.IsRoped == true) continue;
                // Never pull colonists, slaves or prisoners into a fight, whatever def reuses this.
                if (p.RaceProps.Humanlike && (p.Faction == Faction.OfPlayer || p.IsPrisoner)) continue;
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
            if (called == 0) return;
            LifeStageUtility.PlayNearestLifestageSound(pawn, ls => ls.soundCall, g => null, m => null, 1.5f);
            if (BastleUtility.PlayerCanObserve(pawn))
            {
                if (!discovered) Discover();
                Messages.Message("BF_PackCalled".Translate(pawn.Named("PAWN"), called), pawn, MessageTypeDefOf.NeutralEvent);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextCallTick, "nextCallTick");
        }
    }

    // ---- Aura --------------------------------------------------------------------

    /// <summary>Settings for BastleTrait_Aura, in the trait def's modExtensions. Every
    /// aura (dread, irradiated, rallying) is one of these; nothing about who or what it
    /// affects is in code.</summary>
    public class AuraParams : DefModExtension
    {
        public float radius = 5.9f;

        /// <summary>How often it reaches out, in ticks (60 is a second). A scan is cheap:
        /// Ideology's Combat Command scans every tick.</summary>
        public int intervalTicks = 60;

        /// <summary>Walls, closed doors and other sight-blocking buildings stop it. The
        /// ring is drawn to match.</summary>
        public bool blockedByWalls = true;

        // ---- Effect

        public HediffDef hediff;

        /// <summary>0: give the hediff and keep refreshing its HediffComp_Disappears while
        /// the target stays in range (a buff or debuff that fades on leaving). Above 0: add
        /// this much severity per day of exposure instead (a buildup, like toxic fallout's
        /// 0.4).</summary>
        public float severityPerDay;

        /// <summary>Buildup only: each stat's (1 - value) on the target multiplies the
        /// severity, as ToxicResistance does for fallout.</summary>
        public List<StatDef> resistStats;

        // ---- Who. Allies are its own faction and factions allied to it; a wild one's
        // allies are wild creatures of its own kind. Enemies are anyone hostile to it.
        // Neutrals are everyone else.

        public bool affectsSelf;
        public bool affectsAllies;
        public bool affectsNeutrals;
        public bool affectsEnemies;
        public bool affectsHumanlikes = true;
        public bool affectsAnimals = true;
        public bool affectsMechanoids;
        public bool affectsEntities;

        /// <summary>Only creatures of the same race as the source.</summary>
        public bool targetSameRace;

        /// <summary>Only bastles carrying at least one of these traits.</summary>
        public List<BastleTraitDef> targetTraits;

        // ---- When

        public bool whileDowned;
        public bool whileAsleep = true;

        /// <summary>Only while drafted, in an aggressive mental state, holding an enemy
        /// target, or within 20 seconds of fighting.</summary>
        public bool onlyInCombat;

        /// <summary>Reveal the trait when it affects someone while the player can see it.</summary>
        public bool discoverOnEffect = true;

        // ---- Visuals

        /// <summary>Draw the range around it while it is selected, the way a psycast's
        /// targeting ring shows. Only once the trait is known, and only while no glow is
        /// up (no Ideology, showGlow off, effects off, or the aura idle).</summary>
        public bool showRing = true;
        public Color ringColor = Color.white;

        /// <summary>A fleck spawned on it every pulseIntervalTicks while active, such as
        /// PsycastAreaEffect. Off by default.</summary>
        public FleckDef pulseFleck;
        public int pulseIntervalTicks = 150;

        /// <summary>Fleck scale; below 0 it is sized from the radius.</summary>
        public float pulseScale = -1f;

        /// <summary>With Ideology: Combat Command's soft ground glow, sized to the radius,
        /// shown while the aura is active and known. It is a plain circle that ignores
        /// walls, and it replaces the selection ring while it is up. The ring (which does
        /// stop at walls) shows instead without Ideology, with effects off, or while the
        /// aura is idle.</summary>
        public bool showGlow = true;

        /// <summary>The glow's colour. Unset (alpha 0) uses ringColor.</summary>
        public Color glowColor = new Color(0f, 0f, 0f, 0f);

        /// <summary>The glow is additive, so the colour is scaled down by this. 0.125 is
        /// about Combat Command's brightness.</summary>
        public float glowStrength = 0.125f;

        /// <summary>A different attached mote to use as the glow. Defaults to
        /// BF_Mote_AuraGlow, which only exists with Ideology.</summary>
        public ThingDef glowMote;

        /// <summary>With Ideology: Combat Command's line from each pawn it affects back to
        /// it, while it keeps affecting them. Skipped when the hediff has its own
        /// HediffComp_Link (that draws the line instead).</summary>
        public bool showLinks;

        /// <summary>The line's colour. Unset (alpha 0) uses ringColor.</summary>
        public Color linkColor = new Color(0f, 0f, 0f, 0f);

        /// <summary>A different line mote (a MoteDualAttached). Defaults to
        /// BF_Mote_AuraLink, which only exists with Ideology.</summary>
        public ThingDef linkMote;

        internal Color LinkInstanceColor => linkColor.a > 0f ? linkColor : ringColor;

        internal Color GlowInstanceColor
        {
            get
            {
                var c = glowColor.a > 0f ? glowColor : ringColor;
                return new Color(c.r * glowStrength, c.g * glowStrength, c.b * glowStrength, 1f);
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (hediff == null) yield return "aura has no hediff";
            if (radius <= 0f || radius > GenRadial.MaxRadialPatternRadius)
                yield return $"aura radius {radius} must be above 0 and at most {GenRadial.MaxRadialPatternRadius}";
            if (intervalTicks < 1) yield return "aura intervalTicks must be at least 1";
            if (pulseFleck != null && pulseIntervalTicks < 1) yield return "aura pulseIntervalTicks must be at least 1";
            if (glowMote != null && !typeof(MoteAttached).IsAssignableFrom(glowMote.thingClass))
                yield return $"aura glowMote {glowMote.defName} must have a MoteAttached thingClass";
            if (linkMote != null && !typeof(MoteDualAttached).IsAssignableFrom(linkMote.thingClass))
                yield return $"aura linkMote {linkMote.defName} must have a MoteDualAttached thingClass";
            if (!affectsSelf && !affectsAllies && !affectsNeutrals && !affectsEnemies)
                yield return "aura affects nobody: set affectsAllies, affectsEnemies, affectsNeutrals or affectsSelf";
            if (hediff != null && severityPerDay <= 0f)
            {
                var d = hediff.comps?.OfType<HediffCompProperties_Disappears>().FirstOrDefault();
                if (d == null)
                    yield return $"aura hediff {hediff.defName} has no HediffCompProperties_Disappears, so it never wears off after leaving the aura (or set severityPerDay for a buildup)";
                // +15: off-camera pawns tick in batches of up to 15, so a refresh can be late.
                else if (d.disappearsAfterTicks.min <= intervalTicks + 15)
                    yield return $"aura hediff {hediff.defName} can wear off ({d.disappearsAfterTicks.min} ticks) before the aura refreshes it ({intervalTicks} ticks), so it will flicker";
            }
        }
    }

    /// <summary>Gives a hediff to creatures near it, filtered by allegiance, race and
    /// traits (see AuraParams). Shows its range when selected, a ground glow with
    /// Ideology, and can pulse. A hediff with HediffComp_Link draws a line back to it.</summary>
    public class BastleTrait_Aura : BastleTrait
    {
        private const int CombatMemoryTicks = 1200;

        private AuraParams cached;
        public AuraParams Params => cached ?? (cached = def.GetModExtension<AuraParams>() ?? Defaults);
        private static readonly AuraParams Defaults = new AuraParams();

        // The glow mote needs maintaining every tick, which batched hediff ticks can't
        // promise, so MapComponent_BastleVisuals maintains it. Not saved: the mote dies on
        // its own without maintenance, and TickInterval re-registers after a load.
        private Mote glow;
        private Map glowMap;

        private ThingDef GlowDef => Params.showGlow ? Params.glowMote ?? BastleDefOf.BF_Mote_AuraGlow : null;

        /// <summary>Null when this aura draws no lines of its own: showLinks off, no
        /// Ideology, or the hediff has HediffComp_Link and draws its own.</summary>
        private ThingDef LinkDef => Params.showLinks && !(Params.hediff?.HasComp(typeof(HediffComp_Link)) ?? false)
            ? Params.linkMote ?? BastleDefOf.BF_Mote_AuraLink : null;

        // Lines to pawns it affected recently: last hit tick, and the live mote.
        private readonly Dictionary<Pawn, int> linkHits = new Dictionary<Pawn, int>();
        private readonly Dictionary<Pawn, Mote> links = new Dictionary<Pawn, Mote>();
        private static readonly List<Pawn> tmpLinkPawns = new List<Pawn>();

        public override void TickInterval(int delta)
        {
            var pawn = Pawn;
            if (pawn == null || !pawn.Spawned) return;
            var P = Params;
            if (glowMap != pawn.Map && (GlowDef != null || LinkDef != null))
            {
                pawn.Map.GetComponent<MapComponent_BastleVisuals>()?.TrackGlow(this);
                glowMap = pawn.Map;
            }
            bool doEffect = pawn.IsHashIntervalTick(P.intervalTicks, delta);
            bool doPulse = P.pulseFleck != null && pawn.IsHashIntervalTick(P.pulseIntervalTicks, delta);
            if ((!doEffect && !doPulse) || !IsActive(pawn, P)) return;
            if (doPulse) Pulse(pawn, P);
            if (doEffect) Apply(pawn, P);
        }

        public static bool IsActive(Pawn pawn, AuraParams P)
        {
            if (pawn.Dead || (!P.whileDowned && pawn.Downed) || (!P.whileAsleep && !pawn.Awake())) return false;
            return !P.onlyInCombat || InCombat(pawn);
        }

        private static bool InCombat(Pawn p)
        {
            if (p.Drafted || p.InAggroMentalState) return true;
            var m = p.mindState;
            if (m == null) return false;
            int now = Find.TickManager.TicksGame;
            return m.enemyTarget != null
                || now - m.lastEngageTargetTick < CombatMemoryTicks
                || now - m.lastAttackTargetTick < CombatMemoryTicks;
        }

        private void Apply(Pawn pawn, AuraParams P)
        {
            if (P.hediff == null) return;
            var map = pawn.Map;
            var center = pawn.Position;
            bool any = false;
            bool lines = LinkDef != null;
            int now = Find.TickManager.TicksGame;
            var pawns = map.mapPawns.AllPawnsSpawned;
            // Backwards: a buildup can kill, which removes the pawn from this list.
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                var p = pawns[i];
                if (p.Dead || !p.Position.InHorDistOf(center, P.radius) || !Affects(pawn, p, P)) continue;
                if (P.blockedByWalls && p != pawn && !GenSight.LineOfSight(center, p.Position, map)) continue;
                if (!Give(pawn, p, P)) continue;
                any = true;
                if (lines && p != pawn) linkHits[p] = now;
            }
            if (any && P.discoverOnEffect && !discovered && BastleUtility.PlayerCanObserve(pawn)) Discover();
        }

        public static bool Affects(Pawn source, Pawn p, AuraParams P)
        {
            var r = p.RaceProps;
            bool raceOk = r.IsAnomalyEntity ? P.affectsEntities
                : r.IsMechanoid ? P.affectsMechanoids
                : r.Humanlike ? P.affectsHumanlikes
                : P.affectsAnimals;
            if (!raceOk) return false;
            if (p == source) return P.affectsSelf;
            if (P.targetSameRace && p.def != source.def) return false;
            if (P.targetTraits != null)
            {
                var b = BastleUtility.PeekBastle(p);
                if (b == null) return false;
                bool has = false;
                for (int i = 0; i < P.targetTraits.Count && !has; i++) has = b.HasTrait(P.targetTraits[i]);
                if (!has) return false;
            }
            if (p.HostileTo(source)) return P.affectsEnemies;
            var f = source.Faction;
            bool ally = f != null
                ? p.Faction == f || (p.Faction != null && p.Faction.RelationKindWith(f) == FactionRelationKind.Ally)
                : p.Faction == null && p.def == source.def;
            return ally ? P.affectsAllies : P.affectsNeutrals;
        }

        private static bool Give(Pawn source, Pawn p, AuraParams P)
        {
            if (P.severityPerDay > 0f)
            {
                float amount = P.severityPerDay * P.intervalTicks / GenDate.TicksPerDay;
                if (P.resistStats != null)
                    for (int i = 0; i < P.resistStats.Count; i++)
                        amount *= Mathf.Max(0f, 1f - p.GetStatValue(P.resistStats[i]));
                if (amount <= 0f) return false;
                HealthUtility.AdjustSeverity(p, P.hediff, amount);
                return true;
            }
            var h = p.health.hediffSet.GetFirstHediffOfDef(P.hediff);
            if (h == null) h = p.health.AddHediff(P.hediff);
            else h.TryGetComp<HediffComp_Disappears>()?.ResetElapsedTicks();
            // Combat Command's link line, when the hediff asks for one. Kept on the first
            // source; HediffComp_Link drops the hediff when that source leaves the map.
            var link = h?.TryGetComp<HediffComp_Link>();
            if (link != null && p != source && (link.other == null || !link.other.SpawnedOrAnyParentSpawned))
            {
                link.other = source;
                link.drawConnection = true;
            }
            return true;
        }

        /// <summary>Every tick from MapComponent_BastleVisuals. False stops tracking.</summary>
        internal bool GlowTick(Map map)
        {
            var pawn = Pawn;
            var gd = GlowDef;
            var ld = LinkDef;
            if ((gd == null && ld == null) || pawn == null || !pawn.Spawned || pawn.Map != map
                || BastleUtility.PeekBastle(pawn) != bastle || !bastle.Traits.Contains(this))
            {
                // After a map change the new map may already own the mote: leave it.
                if (glow == null || glow.Destroyed || glow.Map == map) { DestroyGlow(); DestroyLinks(); }
                if (glowMap == map) glowMap = null;
                return false;
            }
            var P = Params;
            bool show = (discovered || def.showVisualsWhenHidden) && BastleFrameworkMod.Settings.effectsEnabled
                && map == Find.CurrentMap && !pawn.Position.Fogged(map) && IsActive(pawn, P);
            if (!show) { DestroyGlow(); DestroyLinks(); return true; }
            if (gd == null) DestroyGlow();
            else
            {
                if (glow == null || glow.Destroyed)
                {
                    // BF_Mote_AuraGlow is 2 cells across at scale 1.
                    glow = MoteMaker.MakeAttachedOverlay(pawn, gd, Vector3.zero, P.radius + 0.1f);
                    glow.instanceColor = P.GlowInstanceColor;
                }
                glow.Maintain();
            }
            if (ld == null) DestroyLinks();
            else LinksTick(pawn, map, ld, P);
            return true;
        }

        private void LinksTick(Pawn pawn, Map map, ThingDef ld, AuraParams P)
        {
            if (linkHits.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            // A line lasts until the next scan could have renewed it (+15 for off-camera
            // tick batching), and drops as soon as the target walks out of range.
            int keep = P.intervalTicks + 15;
            tmpLinkPawns.Clear();
            tmpLinkPawns.AddRange(linkHits.Keys);
            for (int i = 0; i < tmpLinkPawns.Count; i++)
            {
                var p = tmpLinkPawns[i];
                links.TryGetValue(p, out var m);
                if (now - linkHits[p] > keep || p.Dead || !p.Spawned || p.Map != map
                    || !p.Position.InHorDistOf(pawn.Position, P.radius) || p.Position.Fogged(map))
                {
                    if (m != null && !m.Destroyed) m.Destroy();
                    links.Remove(p);
                    linkHits.Remove(p);
                    continue;
                }
                if (m == null || m.Destroyed)
                {
                    m = MoteMaker.MakeInteractionOverlay(ld, p, pawn);
                    m.instanceColor = P.LinkInstanceColor;
                    links[p] = m;
                }
                m.Maintain();
            }
            tmpLinkPawns.Clear();
        }

        private void DestroyGlow()
        {
            if (glow != null && !glow.Destroyed) glow.Destroy();
            glow = null;
        }

        private void DestroyLinks()
        {
            foreach (var m in links.Values) if (m != null && !m.Destroyed) m.Destroy();
            links.Clear();
            linkHits.Clear();
        }

        public override void PreRemoved() { DestroyGlow(); DestroyLinks(); }

        private void Pulse(Pawn pawn, AuraParams P)
        {
            var map = pawn.Map;
            if (map != Find.CurrentMap || !BastleFrameworkMod.Settings.effectsEnabled) return;
            if (!(discovered || def.showVisualsWhenHidden) || pawn.Position.Fogged(map)) return;
            // PsycastAreaEffect is 2.3 tiles across at scale 1 and grows as it fades.
            float scale = P.pulseScale > 0f ? P.pulseScale : P.radius * 0.7f;
            FleckMaker.Static(pawn.DrawPos, map, P.pulseFleck, scale);
        }

        /// <summary>Per frame while selected (MapComponent_BastleVisuals).</summary>
        internal void DrawRing()
        {
            var pawn = Pawn;
            var P = Params;
            // While the Ideology glow is up it shows the range instead (it ignores walls;
            // accepted). With effects off or the aura idle, the ring comes back.
            if (!P.showRing || (glow != null && !glow.Destroyed) || !discovered || pawn?.Map == null || P.radius > GenRadial.MaxRadialPatternRadius) return;
            var map = pawn.Map;
            var c = pawn.Position;
            if (P.blockedByWalls)
                GenDraw.DrawRadiusRing(c, P.radius, P.ringColor, x => x.InBounds(map) && GenSight.LineOfSight(c, x, map));
            else
                GenDraw.DrawRadiusRing(c, P.radius, P.ringColor);
        }
    }

    // ---- Lightsick ---------------------------------------------------------------

    public class LightsickParams : DefModExtension
    {
        /// <summary>Ground light level at which the stage applies (0.5 is lit).</summary>
        public float minGlow = 0.5f;
    }

    /// <summary>The trait's stage only applies while the creature stands in light.</summary>
    public class BastleTrait_Lightsick : BastleTrait
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
            bastle?.StageChanged();
        }

        public override string TipStringExtra => inLight ? "BF_LightsickActive".Translate().ToString() : null;

        public override void ExposeData()
        {
            base.ExposeData();
            // Saved so the merged stage is right from the first tick after a load.
            Scribe_Values.Look(ref inLight, "inLight");
        }
    }

    // ---- Regrowth ----------------------------------------------------------------

    public class RegrowthParams : DefModExtension
    {
        /// <summary>Lowest rarity at which lost parts grow back.</summary>
        public BastleRarity minRarity = BastleRarity.Legendary;
        public int intervalTicks = 7500;
    }

    /// <summary>From minRarity up, regrows one lost body part every interval.</summary>
    public class BastleTrait_Regrowth : BastleTrait
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
            if (BastleUtility.PlayerCanObserve(pawn))
            {
                if (!discovered) Discover();
                if (pawn.Faction == Faction.OfPlayer)
                    Messages.Message("BF_PartRegrown".Translate(pawn.Named("PAWN"), part.Label), pawn, MessageTypeDefOf.PositiveEvent);
            }
        }
    }

    // ---- Kindling ----------------------------------------------------------------

    public class KindlingParams : DefModExtension
    {
        public float igniteChance = 0.5f;
    }

    /// <summary>While the creature is on fire, its melee hits may set the target alight.</summary>
    public class BastleTrait_Kindling : BastleTrait
    {
        private KindlingParams P => def.GetModExtension<KindlingParams>() ?? Defaults;
        private static readonly KindlingParams Defaults = new KindlingParams();

        public override void Notify_PawnDamagedThing(Thing thing, DamageInfo dinfo, DamageWorker.DamageResult result)
        {
            var pawn = Pawn;
            if (pawn == null || BastleCombatUtility.InExtraDamage || !pawn.IsBurning()) return;
            if (!BastleCombatUtility.IsMelee(dinfo, thing, out _) || !Rand.Chance(P.igniteChance)) return;
            BastleScheduler.NextTick(() =>
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
    public class BastleTrait_BaneDamage : BastleTrait
    {
        private BaneDamageParams P => def.GetModExtension<BaneDamageParams>() ?? Defaults;
        private static readonly BaneDamageParams Defaults = new BaneDamageParams();

        public override void Notify_PawnDamagedThing(Thing thing, DamageInfo dinfo, DamageWorker.DamageResult result)
        {
            if (BastleCombatUtility.InExtraDamage || result == null || result.totalDamageDealt <= 0f) return;
            var p = P;
            if (!(thing is Pawn victim) || !IsBane(victim, p)) return;
            if (p.meleeOnly && !BastleCombatUtility.IsMelee(dinfo, victim, out _)) return;
            float extra = result.totalDamageDealt * p.factor * def.StrengthFrom(def.rarity, Rarity);
            if (extra < 0.5f) return;
            var pawn = Pawn;
            var bonus = new DamageInfo(dinfo.Def, extra, dinfo.ArmorPenetrationInt, dinfo.Angle, pawn, null, dinfo.Weapon);
            BastleScheduler.NextTick(() =>
            {
                if (!victim.Dead && victim.Spawned) BastleCombatUtility.DealExtraDamage(victim, bonus);
            });
            if (!discovered && BastleUtility.PlayerCanObserve(pawn)) Discover();
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

    /// <summary>On a HediffDef: a Hydra split gives the young this hediff too, at the
    /// parent's severity. Scaria carries it (Patches/BF_HydraInherited.xml); a content mod
    /// puts it on a hediff that keeps a creature in a state of its own (Dungeon Madness's
    /// dungeon-mad).</summary>
    public class BastleSplitInherited : DefModExtension { }

    /// <summary>When it dies, young copies of it (every other trait, its material and
    /// what the player knew about it) spring up beside the corpse. The copies don't get
    /// this trait, so it happens once. They take on the parent's mental state, its lord
    /// and its BastleSplitInherited hediffs.</summary>
    public class BastleTrait_Hydra : BastleTrait
    {
        private HydraParams P => def.GetModExtension<HydraParams>() ?? Defaults;
        private static readonly HydraParams Defaults = new HydraParams();

        // Read in Notify_PawnKilled: by Notify_PawnDied vanilla has dropped the pawn from
        // its lord and nulled its mindState. Not saved; used on the next tick.
        private MentalStateDef stateAtDeath;
        private Lord lordAtDeath;

        public override void Notify_PawnKilled()
        {
            var pawn = Pawn;
            stateAtDeath = pawn?.MentalStateDef;
            lordAtDeath = pawn?.GetLord();
        }

        public override void Notify_PawnDied(DamageInfo? dinfo)
        {
            var pawn = Pawn;
            var state = stateAtDeath;
            var lord = lordAtDeath;
            stateAtDeath = null;
            lordAtDeath = null;
            if (pawn?.Corpse == null || !pawn.Corpse.Spawned) return;
            Pawn killer = dinfo?.Instigator as Pawn;
            int count = P.count;
            var ownDef = def;
            // Inside Pawn.Kill: spawn on the next tick.
            BastleScheduler.NextTick(() => Split(pawn, killer, count, ownDef, state, lord));
        }

        private static void Split(Pawn pawn, Pawn killer, int count, BastleTraitDef ownDef, MentalStateDef state, Lord lord)
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
                BastleUtility.CopyBastle(pawn, child, includeDiscovery: true, linkDiscovery: true);
                BastleUtility.RemoveTrait(child, ownDef);
                GenSpawn.Spawn(child, CellFinder.RandomClosewalkCellNear(pos, map, 2), map);
                FleckMaker.ThrowSmoke(child.DrawPos, map, 1.5f);
                spawned.Add(child);
            }
            if (spawned.Count == 0) return;
            var inherited = pawn.health.hediffSet.hediffs.Where(h => h.def.HasModExtension<BastleSplitInherited>()).ToList();
            foreach (var c in spawned)
            {
                foreach (var h in inherited)
                {
                    if (c.health.hediffSet.HasHediff(h.def)) continue;
                    if (h.Part != null && c.health.hediffSet.PartIsMissing(h.Part)) continue;
                    // Same kind, so the same BodyDef: the parent's part record is the young's too.
                    var copy = HediffMaker.MakeHediff(h.def, c, h.Part);
                    // A marked hediff may be what killed the parent; the young start just short of that.
                    copy.Severity = h.def.lethalSeverity > 0f ? Mathf.Min(h.Severity, h.def.lethalSeverity * 0.9f) : h.Severity;
                    c.health.AddHediff(copy);
                }
                // The state may already be up: an inherited hediff can start it in PostAdd.
                // A social fight needs the opponent it was started with.
                if (state != null && c.MentalStateDef != state && !typeof(MentalState_SocialFighting).IsAssignableFrom(state.stateClass))
                    c.mindState.mentalStateHandler.TryStartMentalState(state, forced: true, forceWake: true, transitionSilently: true);
            }
            // A ritual's lord holds roles for the pawns it started with.
            bool keepLord = lord != null && lord.faction == pawn.Faction && lord.Map == map
                && map.lordManager.lords.Contains(lord) && !(lord.LordJob is LordJob_Ritual);
            if (keepLord)
            {
                lord.AddPawns(spawned);
            }
            else if (pawn.Faction == null)
            {
                if (state == null && killer?.Faction != null)
                    foreach (var c in spawned)
                        c.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter, forced: true, forceWake: true);
            }
            else if (pawn.Faction != Faction.OfPlayer && pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                LordMaker.MakeNewLord(pawn.Faction, new LordJob_AssaultColony(pawn.Faction), map, spawned);
            }
            if (spawned.Any(c => BastleUtility.PlayerCanObserve(c)))
            {
                // The children were copied without it, so only the corpse can show what happened.
                BastleUtility.GetBastle(pawn)?.GetTrait(ownDef)?.Discover(notify: false);
                Messages.Message("BF_HydraSplit".Translate(pawn.Named("PAWN"), spawned.Count), new LookTargets(spawned),
                    pawn.Faction == Faction.OfPlayer ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.ThreatSmall);
            }
        }
    }

    // ---- Shedding ----------------------------------------------------------------

    public class SheddingParams : DefModExtension
    {
        public ThingDef filth;
        /// <summary>Ticks between clumps (2500 is an hour).</summary>
        public IntRange intervalTicks = new IntRange(5000, 10000);
    }

    /// <summary>Leaves a clump of filth where it stands every few hours.</summary>
    public class BastleTrait_Shedding : BastleTrait
    {
        private int nextShedTick;

        private SheddingParams P => def.GetModExtension<SheddingParams>() ?? Defaults;
        private static readonly SheddingParams Defaults = new SheddingParams();

        public override void TickInterval(int delta)
        {
            var pawn = Pawn;
            if (pawn == null || !pawn.Spawned || pawn.Dead) return;
            int now = Find.TickManager.TicksGame;
            if (now < nextShedTick) return;
            // The first pass only sets the clock, so a creature doesn't shed the moment it spawns.
            if (nextShedTick != 0)
                FilthMaker.TryMakeFilth(pawn.Position, pawn.Map, P.filth ?? ThingDefOf.Filth_Hair);
            nextShedTick = now + P.intervalTicks.RandomInRange;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextShedTick, "nextShedTick");
        }
    }

    // ---- Sweltering --------------------------------------------------------------

    public class SwelteringParams : DefModExtension
    {
        /// <summary>As CompHeatPusher's heatPerSecond: a heater is 21.</summary>
        public float heatPerSecond = 6f;
        /// <summary>It stops warming a room that is already this hot.</summary>
        public float maxTemperature = 35f;
    }

    /// <summary>Gives off heat like a small heater, indoors only, up to a ceiling.</summary>
    public class BastleTrait_Sweltering : BastleTrait
    {
        private const int Interval = 250;

        private SwelteringParams P => def.GetModExtension<SwelteringParams>() ?? Defaults;
        private static readonly SwelteringParams Defaults = new SwelteringParams();

        public override void TickInterval(int delta)
        {
            var pawn = Pawn;
            if (pawn == null || !pawn.Spawned || pawn.Dead || !pawn.IsHashIntervalTick(Interval, delta)) return;
            var room = pawn.GetRoom();
            if (room == null || room.UsesOutdoorTemperature || room.Temperature >= P.maxTemperature) return;
            // CompHeatPusher's rare tick pushes heatPerSecond x 4.17 every 250 ticks.
            GenTemperature.PushHeat(pawn, P.heatPerSecond * Interval / 60f);
        }
    }
}
