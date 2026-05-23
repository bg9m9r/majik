using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.66 — Delve. "For each generic mana in this spell's total cost,
/// you may exile a card from your graveyard rather than pay that mana."
///
/// This is NOT an alternative cost (CR 118.9) and NOT an additional cost
/// (CR 601.2f) — it's a cost-modification at mana-payment time. The
/// colored portion of the cost must still be paid normally; only the
/// generic portion may be substituted with graveyard-exile payments.
///
/// Delve is paid when the spell is cast, not when it resolves (CR 702.66b),
/// so the chosen cards are exiled inside the cast flow before the spell
/// goes on the stack. After exile they ride with the spell (other cards
/// like Murktide Regent inspect "cards exiled with it" — that's why we
/// expose <see cref="Chosen"/> as a read-only list after construction).
///
/// Modeled as a value object: the caller (a player agent, a test, or a
/// future bot probe) pre-selects the cards from the graveyard and hands
/// the resulting <see cref="DelveCost"/> to <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>
/// via the dedicated <c>delveCost</c> parameter. The cast flow validates
/// + applies it.
///
/// <para>
/// IPlayerAgent does not currently expose a "choose N cards from graveyard"
/// prompt — see the cast-flow integration note. Tests and bots construct
/// <see cref="DelveCost"/> with a deterministic selection; an agent-driven
/// prompt is deferred (parallels the alt-cost-probe wiring style).
/// </para>
/// </summary>
public sealed class DelveCost
{
    /// <summary>The spell being cast with delve.</summary>
    public ICard Source { get; }

    /// <summary>
    /// The cards selected from the caster's graveyard to exile. One card
    /// per generic mana reduced. Order is preserved so downstream effects
    /// (e.g. Murktide Regent's enter-with-counters trigger) can count them
    /// or inspect them by reference equality.
    /// </summary>
    public IReadOnlyList<ICard> Chosen { get; }

    /// <summary>
    /// The amount of generic mana this delve payment removes from the
    /// spell's total cost — equal to <c>Chosen.Count</c>. Always non-negative.
    /// </summary>
    public int ReductionAmount => Chosen.Count;

    public DelveCost(ICard source, IReadOnlyList<ICard> chosen)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (chosen == null) throw new ArgumentNullException(nameof(chosen));
        // Defensive copy — caller may mutate their list after construction;
        // the cost value is supposed to be immutable.
        Chosen = chosen.ToList();
    }

    /// <summary>
    /// CR 702.66a — legality check. Each chosen card must be in the
    /// caster's graveyard at cast-announcement time, and the requested
    /// reduction can't exceed the spell's printed generic mana count.
    /// </summary>
    public bool CanPay(Player caster, ManaCost printedCost)
    {
        if (caster == null) return false;
        if (printedCost == null) return false;

        // CR 702.66 — delve only reduces generic mana, capped by the
        // generic count of the spell's printed cost.
        if (Chosen.Count > printedCost.Generic) return false;

        // Each chosen card must currently sit in the caster's graveyard
        // and be owned by the caster (CR 702.66 — "a card from your
        // graveyard"). Duplicates are rejected — exiling the same card
        // twice is nonsense.
        var seen = new HashSet<ICard>(ReferenceEqualityComparer.Instance);
        foreach (var c in Chosen)
        {
            if (c == null) return false;
            if (c.Zone != ZoneType.Graveyard) return false;
            if (!ReferenceEquals(c.Owner, caster)) return false;
            if (!seen.Add(c)) return false;
        }

        return true;
    }

    /// <summary>
    /// Apply the delve payment: exile each chosen card from the caster's
    /// graveyard. Used by <see cref="Majik.Core.Game.SpellCastFlow"/> at
    /// cast time (CR 702.66b — delve is paid when cast).
    /// </summary>
    public void Pay(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        var printed = ManaCost.Parse(Source.ManaCost);
        if (!CanPay(caster, printed))
        {
            throw new InvalidOperationException(
                $"Cannot pay Delve cost for {Source.Name} — selection invalid for current game state.");
        }

        foreach (var c in Chosen)
        {
            // Move from graveyard to exile. We mirror
            // FlashbackAlternativeCost.OnResolved's direct-zone-mutation
            // style here rather than going through ZoneService: at
            // cast-announcement time the caster's ZoneService isn't
            // necessarily available, and the engine's other cost-pay
            // implementations (FlashbackAlternativeCost, the
            // SacrificeBasicLandCost in CostPayment) all use the
            // direct-zone path for cost resolution.
            if (c.Owner != null)
            {
                c.Owner.Zones.Graveyard.RemoveCard(c);
                c.Owner.Zones.Exile.AddCard(c);
            }
            c.SetZone(ZoneType.Exile);
        }
    }

    /// <summary>
    /// Compute the post-delve mana cost: reduce generic by
    /// <see cref="ReductionAmount"/>, floored at 0. Other mana components
    /// (colored, hybrid, phyrexian, X) are preserved verbatim.
    /// </summary>
    public ManaCost ApplyTo(ManaCost printedCost)
    {
        if (printedCost == null) throw new ArgumentNullException(nameof(printedCost));
        var newGeneric = Math.Max(0, printedCost.Generic - ReductionAmount);
        return printedCost.WithGeneric(newGeneric);
    }
}
