using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.51 — Convoke. "Your creatures can help cast this spell. Each
/// creature you tap while casting this spell pays for {1} or one mana of
/// that creature's color."
///
/// This class is the infrastructure hook for Convoke cost reduction. A
/// Convoke spell carries this object so the cast flow CAN consult it,
/// even though the v1 implementation does NOT yet reduce the cost.
///
/// <para><b>v1 status — lossy by design.</b> The full Convoke flow needs:</para>
/// <list type="number">
///   <item>Prompt the caster (agent) to select untapped creatures they
///         control to tap as Convoke contributions.</item>
///   <item>For each tapped creature, reduce one {1} OR one pip matching
///         a colour of that creature (CR 702.51b).</item>
///   <item>Honor the floor that the reduced cost still has to be paid in
///         legal mana (CR 117.7c — coloured pips cannot go below 0).</item>
///   <item>Tap the chosen creatures as part of cost payment (CR 601.2f /
///         118.12 — additional cost timing).</item>
/// </list>
///
/// <para>The runtime currently treats Convoke as a no-op cost modifier:
/// <see cref="ReduceCost"/> returns the printed cost unchanged. The
/// caster still pays full mana. Wiring the actual tap-creature prompt
/// is a follow-up — once <see cref="SpellCastFlow"/> grows a
/// Convoke-aware cost-reduction hook, the strategy in
/// <see cref="ReduceCost"/> can fill in the tap selection.</para>
///
/// <para>Despite the name, Convoke is technically a cost MODIFIER, not
/// the spell-replacing alternative cost shape of Flashback / Madness. We
/// implement <see cref="IAlternativeCost"/> here only because that
/// interface is the closest existing seam — <see cref="AlternativeManaCost"/>
/// returns the printed cost unchanged, <see cref="CanCastFor"/> mirrors
/// regular casting (hand zone, owned), and <see cref="OnResolved"/> is a
/// no-op. The interface lets the template surface a "this spell has
/// Convoke" marker without inventing a new <c>SpellDefinition</c> slot.</para>
///
/// <para>CR rule references: 702.51 (Convoke definition), 117.7
/// (cost-reduction floor), 601.2f (additional-cost timing).</para>
/// </summary>
public sealed class ConvokeAlternativeCost : IAlternativeCost
{
    /// <summary>The spell's printed mana cost. v1 returns this unchanged
    /// because actual creature-tap reduction is not yet wired.</summary>
    public ManaCost AlternativeManaCost { get; }

    public string Description => "Convoke";

    public ConvokeAlternativeCost(ManaCost printedCost)
    {
        AlternativeManaCost = printedCost ?? throw new ArgumentNullException(nameof(printedCost));
    }

    /// <summary>
    /// Future-hook: given the printed cost + a creature pick list, return
    /// the cost AFTER applying Convoke reductions per CR 702.51b. v1
    /// returns <paramref name="printedCost"/> unchanged when
    /// <paramref name="tappedCreatures"/> is empty or null. When creatures
    /// are supplied, applies the deterministic strategy: each creature
    /// removes one generic pip first; once generic is exhausted, each
    /// creature removes one coloured pip of any of its colours (in
    /// W,U,B,R,G order).
    ///
    /// Not called from the cast flow today — exposed so tests + future
    /// callers can exercise the reduction in isolation. CR 117.7c floor
    /// (no negative pips) is honoured.
    /// </summary>
    public static ManaCost ReduceCost(ManaCost printedCost, IReadOnlyList<Creature>? tappedCreatures)
    {
        if (printedCost == null) throw new ArgumentNullException(nameof(printedCost));
        if (tappedCreatures == null || tappedCreatures.Count == 0) return printedCost;

        var generic = printedCost.Generic;
        var w = printedCost.White;
        var u = printedCost.Blue;
        var b = printedCost.Black;
        var r = printedCost.Red;
        var g = printedCost.Green;

        foreach (var c in tappedCreatures)
        {
            if (c == null) continue;
            if (generic > 0) { generic--; continue; }

            // Out of generic — try to discount a coloured pip matching one
            // of the creature's colours. We can't easily inspect a
            // Creature's colour identity here without coupling to the
            // colour subsystem; v1 picks any remaining coloured pip in
            // WUBRG order. Real implementation will consult
            // creature.ManaCost / colour identity per CR 702.51b.
            if (w > 0) { w--; continue; }
            if (u > 0) { u--; continue; }
            if (b > 0) { b--; continue; }
            if (r > 0) { r--; continue; }
            if (g > 0) { g--; continue; }
            // All pips paid; further creatures contribute nothing.
            break;
        }

        return BuildCost(generic, w, u, b, r, g, printedCost.HasX);
    }

    private static ManaCost BuildCost(int gen, int w, int u, int bl, int r, int g, bool hasX)
    {
        var s = (hasX ? "X" : "") + (gen > 0 ? gen.ToString() : "")
              + new string('W', w) + new string('U', u) + new string('B', bl)
              + new string('R', r) + new string('G', g);
        return ManaCost.Parse(s);
    }

    public bool CanCastFor(ICard card, Player caster) =>
        card != null
        && card.Zone == ZoneType.Hand
        && ReferenceEquals(card.Owner, caster);

    public void OnResolved(ICard card, Player caster)
    {
        // No post-resolution side-effect. Convoke only affects cost
        // payment; resolution proceeds normally (CR 702.51c).
    }
}
