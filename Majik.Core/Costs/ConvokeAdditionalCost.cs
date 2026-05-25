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
/// <para>Convoke is the creature analogue of <see cref="ImproviseAdditionalCost"/>
/// (CR 702.127 — artifacts). It is technically a cost-modifier rather than a
/// true alternative cost: the caster announces the spell at its printed mana
/// cost, then for each untapped creature they control that they choose to tap
/// during cost payment, the cost is reduced by either {1} (one generic pip)
/// OR by one pip matching ANY colour of that creature (CR 702.51b — the
/// per-tap colour-vs-generic choice is the caster's, made independently per
/// tapped creature).</para>
///
/// <para><b>Modeled as an <see cref="IAdditionalCost"/>:</b> the caller (a
/// player agent, a bot probe, or a test) pre-selects untapped creatures the
/// caster controls and hands the resulting <see cref="ConvokeAdditionalCost"/>
/// to <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/> via the
/// <c>additionalCosts</c> list. The cast flow's CR 601.2f additional-cost
/// loop calls <see cref="CanPay"/> then <see cref="Pay"/>; <see cref="Pay"/>
/// taps the selected creatures, and the cast-flow's mana-cost computation
/// calls <see cref="ApplyTo"/> to reduce both coloured and generic pips
/// before prompting the agent for the remaining mana payment.</para>
///
/// <para>Mirror of <see cref="ImproviseAdditionalCost"/>'s shape. Difference:
/// Improvise reduces only GENERIC pips per CR 702.127; Convoke reduces
/// generic OR a coloured pip matching any of the creature's colours
/// per CR 702.51. The reduction strategy implemented here is the same one
/// pinned by <see cref="ConvokeAlternativeCost.ReduceCost"/>: greedy generic
/// first, then peel coloured pips in WUBRG order constrained to the
/// creature's colours. This is deterministic and well-defined; richer
/// per-tap policy (preserving specific coloured pips) is a follow-up.</para>
///
/// <para>The legacy <see cref="ConvokeAlternativeCost"/> remains as a thin
/// IAlternativeCost discovery shim that wraps an underlying
/// <see cref="ConvokeAdditionalCost"/>, mirroring
/// <see cref="Majik.Core.Players.Agents.ImproviseAlternativeCost"/>. The cast
/// flow consumes Convoke through the additional-cost rail only — the
/// alt-cost shim exists for the bot's probe surface.</para>
///
/// <para>CR rule references: 702.51 (Convoke), 702.127 (Improvise analogue),
/// 605.1 (mana abilities resolve first), 117.7c (cost-reduction floor),
/// 601.2f (additional-cost timing).</para>
/// </summary>
public sealed class ConvokeAdditionalCost : IAdditionalCost
{
    /// <summary>The spell being cast with convoke.</summary>
    public ICard Source { get; }

    /// <summary>
    /// The creatures the caster has selected to tap as Convoke
    /// contributions. Each contributes one pip of cost reduction
    /// (CR 702.51 — {1} generic OR one coloured pip matching the
    /// creature's colour). Order is preserved for diagnostics + future
    /// downstream effects that count "creatures tapped for convoke".
    /// </summary>
    public IReadOnlyList<Creature> Chosen { get; }

    /// <summary>
    /// Total pip reduction granted by tapping the chosen creatures —
    /// equal to <c>Chosen.Count</c>. Always non-negative.
    /// </summary>
    public int ReductionAmount => Chosen.Count;

    public string Description =>
        Chosen.Count == 0
            ? "Convoke"
            : $"Convoke — tap {Chosen.Count} creature(s)";

    public ConvokeAdditionalCost(ICard source, IReadOnlyList<Creature> chosen)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (chosen == null) throw new ArgumentNullException(nameof(chosen));
        // Defensive copy — value object semantics; mirrors ImproviseAdditionalCost.
        Chosen = chosen.ToList();
    }

    /// <summary>
    /// CR 702.51a — legality check. Each chosen creature must be on the
    /// battlefield, controlled by the caster, untapped, and of card type
    /// <see cref="CardType.Creature"/>. Duplicates are rejected (tapping
    /// the same permanent twice is nonsense; CR 605.1 / 118.12 — each cost
    /// is paid once).
    /// </summary>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;

        var seen = new HashSet<Creature>(ReferenceEqualityComparer.Instance);
        foreach (var c in Chosen)
        {
            if (c == null) return false;
            if (c.Zone != ZoneType.Battlefield) return false;
            if (!ReferenceEquals(c.Controller, caster)) return false;
            if (!c.HasType(CardType.Creature)) return false;
            if (c.IsTapped) return false;
            if (!seen.Add(c)) return false;
        }

        return true;
    }

    /// <summary>
    /// CR 702.51a + CR 605.1 — tap each chosen creature. The cast flow has
    /// already settled mana-ability activations (the agent's
    /// ChooseManaSources prompt fires AFTER the additional-cost loop), so
    /// tapping here happens strictly after any tap-for-mana on the same
    /// creature could have occurred (CR 601.2g cost-payment order).
    ///
    /// Returns false on illegal selection. Already-paid creatures (tapped
    /// mid-flight by an outside effect) are NOT untapped — the cost
    /// short-circuits and reports failure so the cast flow can rewind
    /// per CR 601.2g.
    /// </summary>
    public bool Pay(Player caster)
    {
        if (!CanPay(caster)) return false;

        foreach (var c in Chosen)
        {
            c.Tap();
        }

        return true;
    }

    /// <summary>
    /// Compute the post-convoke mana cost. Each tapped creature reduces the
    /// cost by either {1} generic OR one coloured pip matching one of the
    /// creature's colours (CR 702.51b). v1 strategy is deterministic:
    /// each creature consumes one generic pip if any remains; once generic
    /// is exhausted, each creature consumes one coloured pip in WUBRG order
    /// constrained to its own colours. Coloured pips not matching any of
    /// the creature's colours cannot be reduced (CR 702.51b — "a creature
    /// pays {1} or one mana of THAT creature's color"). The cost is floored
    /// at zero per CR 117.7c (no negative pips).
    ///
    /// <para>X is preserved verbatim — convoke does not interact with the X
    /// marker itself, only with the pips generated once X is announced.
    /// Callers casting an X-spell apply <see cref="ApplyTo"/> AFTER the
    /// cast flow folds the X value into generic mana, so an {X}{G} cast at
    /// X=3 with 3 creatures tapped yields {0}{G} as expected.</para>
    /// </summary>
    public ManaCost ApplyTo(ManaCost printedCost)
    {
        if (printedCost == null) throw new ArgumentNullException(nameof(printedCost));

        var generic = printedCost.Generic;
        var w = printedCost.White;
        var u = printedCost.Blue;
        var b = printedCost.Black;
        var r = printedCost.Red;
        var g = printedCost.Green;

        foreach (var c in Chosen)
        {
            if (c == null) continue;

            // CR 702.51b — generic first (caster's choice; the
            // deterministic policy here is "generic if any, else coloured
            // matching the creature's colour"). This minimises consumption
            // of coloured pips, which is the strictly more-restrictive pay
            // mode — generic is fungible.
            if (generic > 0) { generic--; continue; }

            // Out of generic — consult the creature's colour identity per
            // CR 105 (CardColors derives WUBRG from the printed mana cost
            // OR token-colour override). Try to peel a coloured pip
            // matching one of the creature's colours, in WUBRG order.
            var colours = CardColors.GetColors(c);

            if (w > 0 && colours.Contains(ManaColor.White)) { w--; continue; }
            if (u > 0 && colours.Contains(ManaColor.Blue))  { u--; continue; }
            if (b > 0 && colours.Contains(ManaColor.Black)) { b--; continue; }
            if (r > 0 && colours.Contains(ManaColor.Red))   { r--; continue; }
            if (g > 0 && colours.Contains(ManaColor.Green)) { g--; continue; }

            // Creature is colourless OR its colour(s) don't match any
            // remaining coloured pip — the tap still pays but contributes
            // nothing further (CR 702.51b — a colourless creature can
            // contribute only generic; if no generic remains the tap is
            // wasted). The cost is left as-is and we continue checking
            // subsequent creatures (which may still match).
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

    /// <summary>
    /// Convenience: enumerate untapped creatures the caster currently
    /// controls. Mirrors the bot's selection-source helper used by
    /// <see cref="Majik.Core.Players.Agents.ConvokeAltCostProbe"/>.
    /// Returned in battlefield-iteration order. Excludes the spell being
    /// cast (per CR 702.51 — "creatures YOU control"; the spell's own
    /// card is in hand or being moved, not yet on the battlefield).
    /// </summary>
    public static IReadOnlyList<Creature> AvailableCreatures(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        // CR 702.51 + 109.5 — "your creatures" = battlefield permanents
        // the caster controls with CardType.Creature. Covers Creatures
        // AND Artifact-Creatures (Esika's Chariot, Wurmcoil Engine), since
        // both are Creature instances.
        return caster.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.HasType(CardType.Creature)
                        && !c.IsTapped
                        && ReferenceEquals(c.Controller, caster))
            .ToList();
    }
}
