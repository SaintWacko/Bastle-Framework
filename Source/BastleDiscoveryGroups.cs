using System.Collections.Generic;
using Verse;

namespace BastleFramework
{
    /// <summary>
    /// Pack discovery. Bastles with the same non-zero Hediff_Bastle.DiscoveryGroup share
    /// what the player knows about them: a trait discovered on one is quietly discovered
    /// on every other member carrying it. Links are permanent and ignore maps, so a tamed
    /// member reveals traits on wild packmates elsewhere.
    ///
    /// Members are found by scanning the bastle registry rather than keeping a group map:
    /// discovery is rare and the registry holds only bastles, while a map would have to
    /// follow every register, unregister, prune and load. Only grouped bastles pay the scan.
    /// </summary>
    internal static class BastleDiscoveryGroups
    {
        // Quiet discoveries go through BastleTrait.Discover too; this stops them fanning out again.
        private static bool sharing;

        internal static int NewGroup() => BastleGameComponent.Instance?.NextDiscoveryGroup() ?? 0;

        internal static List<Hediff_Bastle> Members(int group)
        {
            var list = new List<Hediff_Bastle>();
            if (group == 0) return list;
            // A snapshot: quiet discoveries may swap the registry, never this dictionary.
            foreach (var kv in BastleUtility.RegistrySnapshot)
                if (kv.Value.DiscoveryGroup == group && !kv.Key.Discarded && kv.Value.StillIn(kv.Key))
                    list.Add(kv.Value);
            return list;
        }

        internal static int MaxGroupInUse()
        {
            int max = 0;
            foreach (var kv in BastleUtility.RegistrySnapshot)
                if (kv.Value.DiscoveryGroup > max) max = kv.Value.DiscoveryGroup;
            return max;
        }

        /// <summary>Discovers <paramref name="def"/> on every other member of the source's
        /// group that carries it, without messages. Returns how many living members were
        /// revealed (the Hydra's corpse still belongs to its children's group).</summary>
        internal static int Share(Hediff_Bastle source, BastleTraitDef def)
        {
            if (sharing || source == null || source.DiscoveryGroup == 0) return 0;
            int revealed = 0;
            sharing = true;
            try
            {
                foreach (var member in Members(source.DiscoveryGroup))
                {
                    if (member == source) continue;
                    var trait = member.GetTrait(def);
                    if (trait != null && trait.Discover(notify: false) && !member.pawn.Dead) revealed++;
                }
            }
            finally { sharing = false; }
            return revealed;
        }

        internal static void Join(Hediff_Bastle bastle, int group)
        {
            if (bastle == null || group == 0) return;
            bastle.SetDiscoveryGroup(group);
            MergeKnowledge(group);
        }

        /// <summary>Makes every member know each trait any member knows, so a pack linked
        /// after some of it was identified agrees with itself. Quiet.</summary>
        internal static void MergeKnowledge(int group)
        {
            var members = Members(group);
            if (members.Count < 2) return;
            var known = new HashSet<BastleTraitDef>();
            foreach (var m in members)
                foreach (var t in m.TraitsForReading)
                    if (t.discovered) known.Add(t.def);
            if (known.Count == 0) return;
            // Restored rather than cleared: a PostDiscovered may link a pack mid-Share.
            bool wasSharing = sharing;
            sharing = true;
            try
            {
                foreach (var m in members)
                    foreach (var t in m.TraitsForReading.ToArray())
                        if (!t.discovered && known.Contains(t.def)) t.Discover(notify: false);
            }
            finally { sharing = wasSharing; }
        }

        /// <summary>Puts every bastle given into one group, absorbing whole groups any of
        /// them already belonged to. Returns the group id, or 0 when nothing was linked.</summary>
        internal static int Link(IEnumerable<Hediff_Bastle> bastles)
        {
            var list = new List<Hediff_Bastle>();
            var oldGroups = new HashSet<int>();
            int group = 0;
            foreach (var b in bastles)
            {
                if (b == null || list.Contains(b)) continue;
                list.Add(b);
                if (b.DiscoveryGroup == 0) continue;
                if (group == 0) group = b.DiscoveryGroup;
                else if (b.DiscoveryGroup != group) oldGroups.Add(b.DiscoveryGroup);
            }
            if (list.Count == 0) return 0;
            if (group == 0 && (group = NewGroup()) == 0) return 0;
            if (oldGroups.Count > 0)
                foreach (var kv in BastleUtility.RegistrySnapshot)
                    if (oldGroups.Contains(kv.Value.DiscoveryGroup)) kv.Value.SetDiscoveryGroup(group);
            foreach (var b in list) b.SetDiscoveryGroup(group);
            MergeKnowledge(group);
            return group;
        }
    }
}
