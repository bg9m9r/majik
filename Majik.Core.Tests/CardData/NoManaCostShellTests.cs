using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// First-class no-mana-cost shell (CR 117.7c / 202.1a / 601.2a).
///
/// Crashing Footfalls, Living End and Glimpse of Tomorrow are printed with
/// a genuinely EMPTY mana cost (Scryfall <c>mana_cost == ""</c>, cmc 0) —
/// their only legal cast path is the printed alternative-cast mechanic
/// (Suspend / a cascade-into free cast), never paying mana from hand.
///
/// Until now the engine gave each a pragmatic stand-in printed cost (so the
/// shell carried a non-zero mana value) and they were allowlisted out of the
/// JSON-card-def semantic-parity audit (the seed row carries the real empty
/// cost, MV 0). This suite locks in the first-class model:
/// <list type="bullet">
/// <item>printed cost is empty → mana value 0 (matches the seed), and</item>
/// <item>the card is uncastable from hand by paying mana — it carries a
///       <see cref="Card.RestrictedCastZones"/> entry for
///       <see cref="ZoneType.Hand"/> (CR 601.2a), exactly the shape Lotus
///       Bloom uses. The alternative-cast free cast resolves from Exile, so
///       the hand restriction never blocks it.</item>
/// </list>
/// </summary>
public class NoManaCostShellTests
{
    private readonly Player _alice = new("Alice", 20);

    public static IEnumerable<object[]> EmptyCostCards()
    {
        yield return new object[] { "Crashing Footfalls" };
        yield return new object[] { "Living End" };
        yield return new object[] { "Glimpse of Tomorrow" };
    }

    [Theory]
    [MemberData(nameof(EmptyCostCards))]
    public void PrintedCost_IsGenuinelyEmpty_ManaValueZero(string name)
    {
        var card = NamedCardFactory.Create(name, _alice);

        // CR 202.1a — a card with no mana cost has an empty mana cost...
        card.ManaCost.Should().BeEmpty(
            $"{name} has no printed mana cost (Scryfall mana_cost == \"\").");

        // CR 202.3 — ...and therefore mana value 0, matching the seed row.
        var concrete = card.Should().BeAssignableTo<Card>().Subject;
        concrete.ManaCostValue.TotalValue.Should().Be(0,
            $"a card with no printed mana cost has mana value 0 (CR 202.3).");
    }

    [Theory]
    [MemberData(nameof(EmptyCostCards))]
    public void CannotBeCastFromHand_OnlyViaAltCast(string name)
    {
        var card = NamedCardFactory.Create(name, _alice);
        var concrete = card.Should().BeAssignableTo<Card>().Subject;

        // CR 601.2a / 117.7c — no printed mana cost means there is no mana
        // cost to pay, so the card can't be cast from hand for its mana cost.
        // The only legal cast paths (Suspend / cascade free cast) resolve from
        // Exile, which this restriction does not touch.
        concrete.RestrictedCastZones.Should().Contain(ZoneType.Hand,
            $"{name} has no printed mana cost — it is uncastable from hand " +
            "(CR 601.2a); only the alternative-cast mechanic can cast it.");
    }
}
