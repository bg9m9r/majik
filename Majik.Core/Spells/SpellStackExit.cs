using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Zones;

namespace Majik.Core.Spells;

/// <summary>How a spell left the stack.</summary>
public enum StackExitKind
{
    /// <summary>It resolved (CR 608.2n).</summary>
    Resolved,
    /// <summary>A spell or ability countered it (CR 701.6a).</summary>
    Countered,
    /// <summary>Every target was illegal on resolution, so it did not resolve
    /// (CR 608.2b).</summary>
    Fizzled,
}

/// <summary>
/// The one rule for where a spell's card goes when it leaves the stack, by
/// every exit. The caller names the zone the exit would put it in (the
/// printed-type default on resolution, the graveyard or a counter's own
/// destination), and this applies the replacements a spell carries:
/// <list type="bullet">
///   <item>a copy ceases to exist and its card never moves (CR 707.10a, 704.5e);</item>
///   <item>flashback and jump-start exile the card on any exit (CR 702.34a, 702.133a);</item>
///   <item>buyback returns it to its owner's hand, only as it resolves (CR 702.27a);</item>
///   <item>rebound exiles it, only as it resolves and only when cast from
///   hand (CR 702.88a; the upkeep recast is not modelled).</item>
/// </list>
/// Retrace (CR 702.81) and escape (CR 702.138) have no such rule: the card
/// goes where it normally would. Otherwise a counter's own destination
/// ("exile it instead", "put it on top of its owner's library") stands.
/// </summary>
public static class SpellStackExit
{
    /// <summary>The zone the card goes to, or null when it goes nowhere (a copy).</summary>
    public static ZoneType? Destination(ISpell spell, StackExitKind kind, ZoneType intended)
    {
        ArgumentNullException.ThrowIfNull(spell);
        if (spell is not Spell s) return intended;
        if (s.IsCopy) return null;
        if (s.ExileWhenLeavesStack) return ZoneType.Exile;
        if (kind == StackExitKind.Resolved && intended == ZoneType.Graveyard)
        {
            if (s.ReturnToHandOnResolution) return ZoneType.Hand;
            if (s.WasCastFromHand && HasRebound(s.Card)) return ZoneType.Exile;
        }
        return intended;
    }

    /// <summary>
    /// Move the card of a spell that has left <paramref name="stack"/> to its
    /// destination. Through the stack's zone service when it has one (zone
    /// lists, CardMovedEvent, zone-change replacements), else the owner's
    /// zone lists directly. A card already in its destination is left alone.
    /// Returns the destination (null for a copy).
    /// </summary>
    public static ZoneType? Leave(Majik.Core.Stack.Stack? stack, ISpell spell, StackExitKind kind, ZoneType intended)
    {
        var to = Destination(spell, kind, intended);
        if (to is not { } zone) return null;
        var card = spell.Card;
        if (card.Zone == zone) return zone;
        if (stack?.ZoneService is { } zones && card.Owner is { } owner)
        {
            zones.MoveCard(card, card.Zone, zone, owner);
            // ZoneManager only lists the card in its new zone when it was
            // listed in the old one: a card the caller never listed on the
            // stack still ends up listed where it went.
            if (card.Zone == zone && owner.Zones.GetZone(zone) is { } list && !list.GetCards().Contains(card))
                list.AddCard(card);
        }
        else
            MoveWithoutEvents(card, zone);
        return zone;
    }

    /// <summary>Owner's zone lists + the card's zone, with no event (the
    /// no-zone-service fallback, and the resolution cleanup effects that run
    /// before the resolver's own move).</summary>
    internal static void MoveWithoutEvents(ICard card, ZoneType to)
    {
        if (card.Owner is { } owner)
        {
            owner.Zones.GetZone(card.Zone)?.RemoveCard(card);
            owner.Zones.GetZone(to)?.AddCard(card);
        }
        card.SetZone(to);
    }

    private static bool HasRebound(ICard card) =>
        card.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Rebound", StringComparison.OrdinalIgnoreCase));
}