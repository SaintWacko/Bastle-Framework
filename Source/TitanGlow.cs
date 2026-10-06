using RimWorld;
using UnityEngine;
using Verse;

namespace TitanFramework
{
    /// <summary>
    /// A trait's light. XML:
    /// <code>&lt;glow&gt;&lt;radius&gt;5&lt;/radius&gt;&lt;color&gt;(255,140,50,0)&lt;/color&gt;&lt;/glow&gt;</code>
    /// Color is a vanilla ColorInt, 0-255 per channel like a lamp's glowColor (alpha 0).
    /// When several traits glow, the creature casts one light: the largest radius, in the
    /// colour of the trait that has it.
    /// </summary>
    public class TitanGlow
    {
        public float radius = 5f;
        public ColorInt color = new ColorInt(255, 140, 50, 0);
    }

    /// <summary>
    /// The light a titan casts. A vanilla CompGlower that is registered with the map's glow
    /// grid but never added to the pawn's comps, so it isn't saved, has no gizmos, and no
    /// GetComp&lt;CompGlower&gt; finds it. The glow grid only reads its parent's Position
    /// and thingIDNumber and its Props, radius and colour.
    ///
    /// The grid snapshots the light's cell on registration, so following the creature means
    /// deregistering and registering again. Each move re-floods this one light (Burst, off
    /// the main thread) and rebuilds the lighting mesh of the few map sections it covers;
    /// that mesh rebuild is the real cost, so a walking creature moves its light every
    /// <see cref="FollowCells"/> cells rather than every cell, and catches up when it stops.
    /// </summary>
    public class TitanGlower
    {
        /// <summary>How far a walking creature gets ahead of its light before the light moves.</summary>
        public static int FollowCells = 2;

        private readonly CompGlower comp;
        private Map map;
        private IntVec3 cell = IntVec3.Invalid;

        public TitanGlower(Pawn pawn, TitanGlow glow)
        {
            comp = new CompGlower
            {
                parent = pawn,
                props = new CompProperties_Glower
                {
                    glowRadius = Mathf.Clamp(glow.radius, 0.5f, 40f),
                    glowColor = glow.color,
                },
            };
        }

        public bool Registered => map != null;

        /// <summary>Called every tick by the map component while the creature is spawned.</summary>
        public void Update(Pawn pawn)
        {
            if (map == pawn.Map && cell == pawn.Position) return;
            if (map == pawn.Map && pawn.pather != null && pawn.pather.Moving
                && cell.DistanceToSquared(pawn.Position) < FollowCells * FollowCells)
                return;
            Remove();
            map = pawn.Map;
            cell = pawn.Position;
            map.glowGrid.RegisterGlower(comp);
        }

        public void Remove()
        {
            if (map == null) return;
            // A map that was just removed has already disposed its glow grid.
            if (map.glowGrid != null && Find.Maps.Contains(map))
                map.glowGrid.DeRegisterGlower(comp);
            map = null;
            cell = IntVec3.Invalid;
        }
    }
}
