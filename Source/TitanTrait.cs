using RimWorld;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// One trait on one creature. Saved inside Hediff_Titan, so subclasses can keep
    /// per-creature state by overriding ExposeData. Hooks are called by the titan
    /// hediff; override only what you need (TickInterval is only called at all when
    /// some trait on the creature overrides it).
    /// </summary>
    public class TitanTrait : IExposable
    {
        public TitanTraitDef def;
        public bool discovered;

        /// <summary>Set by the owning hediff after creation and after load.</summary>
        public Hediff_Titan titan;

        public Pawn Pawn => titan?.pawn;

        /// <summary>Whether this trait's overlays and effecters should show right now.</summary>
        public virtual bool VisualsActive => def.HasVisuals && (discovered || def.showVisualsWhenHidden);

        public virtual string Label => discovered ? def.LabelCap.ToString() : "TF_UnknownTrait".Translate().ToString();

        public virtual string Description => discovered ? def.description : "TF_UnknownTraitDesc".Translate().ToString();

        /// <summary>Reveal this trait. With notify, the player gets a message (when the
        /// def allows it). Returns true if it was hidden before.</summary>
        public bool Discover(bool notify = true)
        {
            if (discovered) return false;
            string oldLabel = Pawn?.LabelShort;
            discovered = true;
            titan?.Notify_TraitDiscovered(this);
            if (notify && def.discoveryMessage && Pawn != null && Pawn.Spawned)
            {
                Messages.Message("TF_TraitDiscovered".Translate(oldLabel, def.label),
                    Pawn, MessageTypeDefOf.NeutralEvent, historical: false);
            }
            PostDiscovered();
            return true;
        }

        /// <summary>Discover only if the trigger is listed and the player can see it happen.</summary>
        public void TryDiscoverBy(TitanDiscoveryTrigger trigger)
        {
            if (!discovered && def.DiscoveredBy(trigger) && TitanUtility.PlayerCanObserve(Pawn))
                Discover();
        }

        // ---- hooks ---------------------------------------------------------------

        /// <summary>After the trait is added (not on load).</summary>
        public virtual void PostAdded() { }

        /// <summary>Before the trait is removed or the titan hediff is removed.</summary>
        public virtual void PreRemoved() { }

        public virtual void PostDiscovered() { }

        public virtual void TickInterval(int delta) { }

        public virtual void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt) { }

        public virtual void Notify_PawnUsedVerb(Verb verb, LocalTargetInfo target) { }

        public virtual void Notify_PawnDied(DamageInfo? dinfo) { }

        /// <summary>Extra lines in the titan hediff tooltip, only shown once discovered.</summary>
        public virtual string TipStringExtra => null;

        public virtual void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref discovered, "discovered");
        }
    }
}
