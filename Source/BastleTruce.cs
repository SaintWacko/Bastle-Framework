using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// A truce scoped to one map: the colony and every faction in it are not hostile to each
    /// other on that map, whatever their relations elsewhere. A global relation flip would leak
    /// (their caravans at home, their raiders already on the home map, and any goodwill change
    /// recomputing the relation), so instead the faction-relation calls inside GenHostility.HostileTo
    /// and the projectile friendly-fire checks are rewritten to ask HostileOn, which knows the map.
    /// Every relation call in those methods is rewritten, the prisoner host-faction checks included,
    /// so a colony prisoner of an outside faction is not hostile to the truce parties there either.
    /// Mental states, predators, prison breaks and manhunters are separate checks in vanilla and
    /// are untouched. About 230 other direct Faction.HostileTo calls (name colours, trading,
    /// reservations) still see the real relation, and so does any projectile class that overrides
    /// ImpactSomething without calling base.
    /// </summary>
    public static class BastleTruce
    {
        private static MapComponent_BastleTruce[] active = new MapComponent_BastleTruce[0];
        private static readonly HashSet<MethodBase> rewrittenHostility = new HashSet<MethodBase>();

        private static readonly MethodInfo RelationHostile =
            AccessTools.Method(typeof(FactionUtility), nameof(FactionUtility.HostileTo), new[] { typeof(Faction), typeof(Faction) });
        private static readonly MethodInfo TruceHostile = AccessTools.Method(typeof(BastleTruce), nameof(HostileOn));

        /// <summary>Both GenHostility.HostileTo overloads were rewritten. Without that a truce
        /// would do nothing, so hostile factions are never invited.</summary>
        public static bool Available => rewrittenHostility.Count >= 2;

        public static bool AnyActive => active.Length > 0;

        /// <summary>The faction relation as seen on the map <paramref name="at"/> is on.</summary>
        public static bool HostileOn(Faction a, Faction b, Thing at)
        {
            var truces = active;
            if (truces.Length != 0 && at != null)
            {
                Map map = at.MapHeld;
                for (int i = 0; i < truces.Length; i++)
                    if (truces[i].map == map && truces[i].Covers(a, b)) return false;
            }
            return a.HostileTo(b);
        }

        public static MapComponent_BastleTruce On(Map map) => map?.GetComponent<MapComponent_BastleTruce>();

        /// <summary>Starts (or widens) the truce on the map between the colony and these
        /// factions, and among the factions. Call before their pawns spawn.</summary>
        public static void Begin(Map map, IEnumerable<Faction> factions) => On(map)?.Begin(factions);

        /// <summary>The colony and the faction are under a truce on the map.</summary>
        public static bool WithPlayer(Map map, Faction faction) => On(map)?.WithPlayer(faction) == true;

        internal static void Register(MapComponent_BastleTruce truce)
        {
            if (System.Array.IndexOf(active, truce) >= 0) return;
            var next = new MapComponent_BastleTruce[active.Length + 1];
            active.CopyTo(next, 0);
            next[active.Length] = truce;
            active = next;
        }

        internal static void Unregister(MapComponent_BastleTruce truce)
        {
            int i = System.Array.IndexOf(active, truce);
            if (i < 0) return;
            var next = new MapComponent_BastleTruce[active.Length - 1];
            for (int j = 0, k = 0; j < active.Length; j++)
                if (j != i) next[k++] = active[j];
            active = next;
        }

        internal static void Clear() => active = new MapComponent_BastleTruce[0];

        // ldarg.0 is the Thing in both GenHostility overloads (static extensions) and the
        // projectile in Projectile's instance methods, so the map is always at hand.
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, MethodBase original, bool hostility)
        {
            var result = new List<CodeInstruction>();
            int replaced = 0;
            foreach (var ins in instructions)
            {
                if (RelationHostile != null && ins.Calls(RelationHostile))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.MoveLabelsFrom(ins);
                    load.MoveBlocksFrom(ins);
                    result.Add(load);
                    result.Add(new CodeInstruction(OpCodes.Call, TruceHostile));
                    replaced++;
                    continue;
                }
                result.Add(ins);
            }
            if (replaced == 0)
                Log.Warning("[Bastle Framework] Found no faction relation check in " + original?.DeclaringType?.Name + "." + original?.Name
                    + "; outbreak truces will not apply there" + (hostility ? ", and hostile factions will not attend outbreaks." : "."));
            else if (hostility)
                rewrittenHostility.Add(original);
            return result;
        }

        internal static void Notify_MemberTookDamage(Faction faction, Pawn member, DamageInfo dinfo)
        {
            Thing instigator = dinfo.Instigator;
            if (instigator == null || faction.IsPlayer || instigator.Faction == null || !instigator.Faction.IsPlayer) return;
            var truce = On(member.MapHeld);
            if (truce == null || !truce.WithPlayer(faction)) return;
            Pawn attacker = instigator as Pawn;
            if (attacker != null && attacker.InMentalState) return;
            if (!dinfo.Def.ExternalViolenceFor(member)) return;
            // Melee damage carries no intended target in vanilla; the attacker's job says who it meant.
            bool deliberate = dinfo.IntendedTarget == member
                || (dinfo.IntendedTarget == null && attacker?.CurJob != null && attacker.CurJob.targetA.Thing == member);
            if (deliberate) truce.Break(faction, captured: false);
        }
    }

    /// <summary>The truce on one map. The colony is always a party; factions lists the others,
    /// and brokenWithPlayer the ones the colony betrayed (their truce with the other factions
    /// stands). Ends by itself once no free pawn of any party but the colony is on the map.</summary>
    public class MapComponent_BastleTruce : MapComponent
    {
        private List<Faction> factions = new List<Faction>();
        private List<Faction> brokenWithPlayer = new List<Faction>();
        private Dictionary<Faction, int> paidGoodwill = new Dictionary<Faction, int>();
        private List<Faction> paidKeys;
        private List<int> paidValues;

        private const int CheckIntervalTicks = 60;

        public MapComponent_BastleTruce(Map map) : base(map) { }

        public bool Active => factions.Count > 0;

        public IReadOnlyList<Faction> Factions => factions;

        internal bool Covers(Faction a, Faction b)
        {
            if (a == null || b == null) return false;
            if (a.IsPlayer) return factions.Contains(b) && !brokenWithPlayer.Contains(b);
            if (b.IsPlayer) return factions.Contains(a) && !brokenWithPlayer.Contains(a);
            return factions.Contains(a) && factions.Contains(b);
        }

        public bool WithPlayer(Faction f) => f != null && factions.Contains(f) && !brokenWithPlayer.Contains(f);

        /// <summary>The faction was given this much goodwill for the fight here, and loses it
        /// if the colony breaks the truce before its fighters are gone.</summary>
        public void Notify_GoodwillPaid(Faction f, int goodwill)
        {
            // Zero is kept too (a faction already at the cap): the fight is won, so a break
            // afterwards must not claim they will not be paid.
            if (goodwill >= 0 && WithPlayer(f)) paidGoodwill[f] = goodwill;
        }

        public void Begin(IEnumerable<Faction> joining)
        {
            foreach (var f in joining)
                if (f != null && !f.IsPlayer && !factions.Contains(f)) factions.Add(f);
            if (!Active) return;
            BastleTruce.Register(this);
            RefreshTargets(factions);
        }

        /// <summary>The colony attacked or captured one of the faction's pawns on purpose.</summary>
        public void Break(Faction f, bool captured)
        {
            if (!WithPlayer(f)) return;
            brokenWithPlayer.Add(f);
            RefreshTargets(new List<Faction> { f });
            var outbreak = BastleOutbreakQuest.PartFor(map.Parent);
            outbreak?.Notify_TruceBroken(f);
            string goodwillLine = null;
            if (paidGoodwill.TryGetValue(f, out int paid))
            {
                paidGoodwill.Remove(f);
                if (paid > 0 && !f.defeated)
                {
                    // The truce letter below says whether they are hostile now, so vanilla's
                    // relations letter would only repeat it. Vanilla can enlarge a loss above the
                    // faction's natural goodwill, so the letter shows what was really taken.
                    int before = f.PlayerGoodwill;
                    Faction.OfPlayer.TryAffectGoodwillWith(f, -paid, canSendMessage: false, canSendHostilityLetter: false,
                        reason: BastleOutbreakDefOf.BF_OutbreakTruceBroken);
                    int lost = before - f.PlayerGoodwill;
                    if (lost > 0) goodwillLine = "BF_TruceBrokenGoodwillLost".Translate(lost);
                }
            }
            else if (outbreak != null) goodwillLine = "BF_TruceBrokenNoGoodwill".Translate();
            // After the goodwill is taken back, which may have made them hostile again.
            bool hostile = f.HostileTo(Faction.OfPlayer);
            string key = captured
                ? (hostile ? "BF_TruceBrokenCapturedHostile" : "BF_TruceBrokenCaptured")
                : (hostile ? "BF_TruceBrokenAttackedHostile" : "BF_TruceBrokenAttacked");
            TaggedString text = key.Translate(f.NameColored);
            if (goodwillLine != null) text += " " + goodwillLine;
            Find.LetterStack.ReceiveLetter("BF_TruceBrokenLabel".Translate(f.Name), text,
                hostile ? LetterDefOf.ThreatSmall : LetterDefOf.NegativeEvent, new List<Pawn>(map.mapPawns.SpawnedPawnsInFaction(f)));
        }

        public void End()
        {
            if (!Active) return;
            var was = new List<Faction>(factions);
            factions.Clear();
            brokenWithPlayer.Clear();
            paidGoodwill.Clear();
            BastleTruce.Unregister(this);
            RefreshTargets(was);
        }

        // UpdateTarget reweighs a target against every faction, so one call per pair of
        // parties reaches every target that belongs to one of them.
        private void RefreshTargets(List<Faction> changed)
        {
            var cache = map.attackTargetsCache;
            Faction player = Faction.OfPlayer;
            cache.Notify_FactionHostilityChanged(player, changed.Count > 0 ? changed[0] : player);
            for (int i = 1; i < changed.Count; i += 2)
                cache.Notify_FactionHostilityChanged(changed[i], i + 1 < changed.Count ? changed[i + 1] : changed[i]);
        }

        public override void MapComponentTick()
        {
            if (!Active || Find.TickManager.TicksGame % CheckIntervalTicks != 0) return;
            bool anyFree = false;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction f = factions[i];
                var pawns = map.mapPawns.SpawnedPawnsInFaction(f);
                bool betray = false;
                for (int j = 0; j < pawns.Count; j++)
                {
                    if (pawns[j].IsPrisonerOfColony) betray = true;
                    else anyFree = true;
                }
                if (betray && WithPlayer(f)) Break(f, captured: true);
            }
            if (!anyFree) End();
        }

        public override void MapRemoved() => BastleTruce.Unregister(this);

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref factions, "factions", LookMode.Reference);
            Scribe_Collections.Look(ref brokenWithPlayer, "brokenWithPlayer", LookMode.Reference);
            Scribe_Collections.Look(ref paidGoodwill, "paidGoodwill", LookMode.Reference, LookMode.Value, ref paidKeys, ref paidValues, logNullErrors: false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                factions = factions ?? new List<Faction>();
                brokenWithPlayer = brokenWithPlayer ?? new List<Faction>();
                paidGoodwill = paidGoodwill ?? new Dictionary<Faction, int>();
                factions.RemoveAll(f => f == null);
                brokenWithPlayer.RemoveAll(f => f == null);
                // Before the map's things respawn, so the attack target cache is built with the truce.
                if (Active) BastleTruce.Register(this);
            }
        }
    }

    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Thing) })]
    internal static class Patch_HostileToThing_Truce
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
            => BastleTruce.Rewrite(instructions, original, hostility: true);
    }

    [HarmonyPatch(typeof(GenHostility), nameof(GenHostility.HostileTo), new[] { typeof(Thing), typeof(Faction) })]
    internal static class Patch_HostileToFaction_Truce
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
            => BastleTruce.Rewrite(instructions, original, hostility: true);
    }

    // Friendly fire: shots pass truce parties as they pass allies. Runtime targets, so a
    // renamed method costs only this patch rather than every patch in the assembly.
    [HarmonyPatch]
    internal static class Patch_Projectile_Truce
    {
        private static bool Prepare() => TargetMethods().Any();

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var intercept = AccessTools.DeclaredMethod(typeof(Projectile), "CheckForFreeIntercept");
            if (intercept != null) yield return intercept;
            var impact = AccessTools.DeclaredMethod(typeof(Projectile), "ImpactSomething");
            if (impact != null) yield return impact;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
            => BastleTruce.Rewrite(instructions, original, hostility: false);
    }

    [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_MemberTookDamage))]
    internal static class Patch_MemberTookDamage_Truce
    {
        private static void Postfix(Faction __instance, Pawn member, DamageInfo dinfo)
        {
            if (!BastleTruce.AnyActive) return;
            BastleTruce.Notify_MemberTookDamage(__instance, member, dinfo);
        }
    }
}
