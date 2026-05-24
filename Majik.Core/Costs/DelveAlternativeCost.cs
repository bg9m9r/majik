using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.66 — Delve, expressed as an <see cref="IAlternativeCost"/> wrapper
/// so the bot's <see cref="Majik.Core.Players.Agents.IAlternativeCostProbe"/>
/// stream can surface it alongside Pitch / Flashback / Overload candidates.
///
/// <para>Delve itself is technically a cost-modification rather than a true
/// alternative cost (the colored pips still must be paid as printed). This
/// wrapper bridges the two worlds: <see cref="AlternativeManaCost"/> reports
/// the printed cost with the generic portion reduced by the number of
/// graveyard cards the caller selected, and <see cref="OnResolved"/> exiles
/// the chosen cards from the caster's graveyard (mirroring
/// <see cref="DelveCost.Pay"/>'s direct-zone-mutation style).</para>
///
/// <para>The probe constructs one of these per candidate graveyard slice
/// (typically a single "maximum delve" pick — exile as many cards as there
/// are generic pips). At cast time the bot bids the wrapper as an alt cost;
/// <see cref="Majik.Core.Game.SpellCastFlow"/>'s alt-cost arm walks the
/// reduced <see cref="AlternativeManaCost"/> for the mana payment, then this
/// class's <see cref="OnResolved"/> moves the chosen cards graveyard → exile
/// after the spell resolves.</para>
///
/// <para>This is intentionally a separate type from
/// <see cref="DelveCost"/> — that class remains the canonical cast-flow
/// primitive (called via the <c>delveCost</c> parameter on
/// <c>SpellCastFlow.CastAsync</c>); this wrapper is the discovery-surface
/// adapter so the bot's existing alt-cost rail can pick up delve options
/// without a separate parallel API.</para>
/// </summary>
public sealed class DelveAlternativeCost : IAlternativeCost
{
    /// <summary>Cards chosen from the caster's graveyard. Each contributes
    /// one generic-mana reduction (CR 702.66a). Order preserved so consumers
    /// (Murktide Regent etc.) can count or inspect them.</summary>
    public IReadOnlyList<ICard> Chosen { get; }

    /// <summary>The printed mana cost of the spell — kept for description /
    /// audit. The reduced cost is exposed via
    /// <see cref="AlternativeManaCost"/>.</summary>
    public ManaCost PrintedCost { get; }

    public string Description =>
        $"Delve — exile {Chosen.Count} graveyard card(s), pay {AlternativeManaCost}";

    public ManaCost AlternativeManaCost { get; }

    public DelveAlternativeCost(ManaCost printedCost, IReadOnlyList<ICard> chosen)
    {
        PrintedCost = printedCost ?? throw new ArgumentNullException(nameof(printedCost));
        if (chosen == null) throw new ArgumentNullException(nameof(chosen));

        // Defensive copy — value object semantics.
        Chosen = chosen.ToList();

        // CR 702.66 — only generic pips reduce; colored portion is preserved.
        var reduction = Math.Min(Chosen.Count, printedCost.Generic);
        AlternativeManaCost = printedCost.WithGeneric(printedCost.Generic - reduction);
    }

    /// <summary>
    /// CR 702.66a — delve is legal when the spell is being cast from hand
    /// and each chosen card sits in the caster's graveyard. Mirrors
    /// <see cref="DelveCost.CanPay"/> for the graveyard slice plus the
    /// from-hand zone gate alt-cost callers expect.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (card == null || caster == null) return false;
        if (card.Zone != ZoneType.Hand) return false;
        if (!ReferenceEquals(card.Owner, caster)) return false;

        // Don't allow more delve cards than the spell has generic pips.
        if (Chosen.Count > PrintedCost.Generic) return false;

        var seen = new HashSet<ICard>(ReferenceEqualityComparer.Instance);
        foreach (var c in Chosen)
        {
            if (c == null) return false;
            // The spell itself is being cast from hand, so it cannot be
            // chosen as a delve exile (sanity).
            if (ReferenceEquals(c, card)) return false;
            if (c.Zone != ZoneType.Graveyard) return false;
            if (!ReferenceEquals(c.Owner, caster)) return false;
            if (!seen.Add(c)) return false;
        }

        return true;
    }

    /// <summary>
    /// Exile the chosen cards from the caster's graveyard. Mirrors
    /// <see cref="DelveCost.Pay"/>'s direct-zone-mutation style — by the
    /// time alt-cost <see cref="OnResolved"/> fires, the cast flow has
    /// already taken mana payment for <see cref="AlternativeManaCost"/>,
    /// so this step finalises the delve side-effect.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));

        foreach (var c in Chosen)
        {
            if (c.Owner != null && c.Zone == ZoneType.Graveyard)
            {
                c.Owner.Zones.Graveyard.RemoveCard(c);
                c.Owner.Zones.Exile.AddCard(c);
                c.SetZone(ZoneType.Exile);
            }
        }
    }
}
