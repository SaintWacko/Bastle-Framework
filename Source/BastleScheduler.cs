using System;
using System.Collections.Generic;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// Runs trait code outside the vanilla loop it was triggered from. Damage and death
    /// notifications arrive inside loops over a pawn's hediffs (and inside Pawn.Kill),
    /// where spawning pawns, moving one or dealing more damage is unsafe; queue it here
    /// and it runs at the start of the next game tick.
    ///
    /// Queued actions are not saved: a save made in the one tick between queueing and
    /// running loses them. Anything that must survive a save (Undying's rise, hours
    /// later) is kept on BastleGameComponent instead.
    /// </summary>
    public static class BastleScheduler
    {
        private static readonly List<Action> queue = new List<Action>();
        private static readonly List<Action> running = new List<Action>();

        public static void NextTick(Action action)
        {
            if (action != null) queue.Add(action);
        }

        internal static void Run()
        {
            if (queue.Count == 0) return;
            running.AddRange(queue);
            queue.Clear();
            foreach (var a in running)
            {
                try { a(); }
                catch (Exception e) { Log.Error("[Bastle Framework] Scheduled trait action threw: " + e); }
            }
            running.Clear();
        }

        internal static void Clear() => queue.Clear();
    }

    /// <summary>Ticks the scheduler, keeps Undying bastles' pending rises and hands out
    /// discovery group ids.</summary>
    public class BastleGameComponent : GameComponent
    {
        private List<Pawn> risingPawns = new List<Pawn>();
        private List<int> riseTicks = new List<int>();
        private int lastDiscoveryGroup;

        public BastleGameComponent(Game game)
        {
            // A new game or a load: drop anything queued in the previous one.
            BastleScheduler.Clear();
            BastleUtility.ClearRegistry();
        }

        public static BastleGameComponent Instance => Current.Game?.GetComponent<BastleGameComponent>();

        /// <summary>Resurrect a dead pawn at a later tick, if its corpse still exists.</summary>
        public void ScheduleRise(Pawn pawn, int atTick)
        {
            risingPawns.Add(pawn);
            riseTicks.Add(atTick);
        }

        internal int NextDiscoveryGroup() => ++lastDiscoveryGroup;

        public override void FinalizeInit()
        {
            // A group id reused by a new pack would silently link it to an old one; the
            // registry has every loaded bastle by now.
            lastDiscoveryGroup = Math.Max(lastDiscoveryGroup, BastleDiscoveryGroups.MaxGroupInUse());
        }

        public override void GameComponentTick()
        {
            BastleScheduler.Run();
            if (Find.TickManager.TicksGame % 2500 == 0) BastleUtility.PruneRegistry();
            if (risingPawns.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            for (int i = risingPawns.Count - 1; i >= 0; i--)
            {
                if (riseTicks[i] > now) continue;
                var p = risingPawns[i];
                risingPawns.RemoveAt(i);
                riseTicks.RemoveAt(i);
                try { BastleTrait_Undying.Rise(p); }
                catch (Exception e) { Log.Error("[Bastle Framework] Undying rise threw: " + e); }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // A butchered or burned corpse leaves a discarded pawn behind; nothing to raise.
                for (int i = risingPawns.Count - 1; i >= 0; i--)
                    if (risingPawns[i] == null || risingPawns[i].Discarded || risingPawns[i].Corpse == null)
                    { risingPawns.RemoveAt(i); riseTicks.RemoveAt(i); }
            }
            Scribe_Collections.Look(ref risingPawns, "risingPawns", LookMode.Reference);
            Scribe_Collections.Look(ref riseTicks, "riseTicks", LookMode.Value);
            Scribe_Values.Look(ref lastDiscoveryGroup, "lastDiscoveryGroup", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                risingPawns = risingPawns ?? new List<Pawn>();
                riseTicks = riseTicks ?? new List<int>();
                // A pawn that was destroyed (butchered, burned) saves as a null reference.
                for (int i = Math.Min(risingPawns.Count, riseTicks.Count) - 1; i >= 0; i--)
                    if (risingPawns[i] == null) { risingPawns.RemoveAt(i); riseTicks.RemoveAt(i); }
                if (risingPawns.Count != riseTicks.Count)
                {
                    int n = Math.Min(risingPawns.Count, riseTicks.Count);
                    risingPawns.RemoveRange(n, risingPawns.Count - n);
                    riseTicks.RemoveRange(n, riseTicks.Count - n);
                }
            }
        }
    }
}
