using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RemoteIsleFactory"/> — the Onslaught blue
/// "Cycling {2}" tapland. Type line: <c>Land</c>. Oracle text (verified
/// against Scryfall):
///   "This land enters tapped.
///    {T}: Add {U}.
///    Cycling {2} ({2}, Discard this card: Draw a card.)"
///
/// Covers:
/// - Identity (Land producing {U}, CR 605.1 — mana abilities don't use the
///   stack).
/// - Cycling {2} ability shape (ManaCostCost("{2}") + DiscardSelfCost via the
///   shared <see cref="Majik.Core.Keywords.CyclingFactory"/> primitive, CR 702.32).
/// - End-to-end cycle: pays {2}, discards self, draws one card, publishes
///   <see cref="Majik.Core.Events.CardCycledEvent"/> when a bus is supplied.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Sheltered Thicket factory).
/// </summary>
[Trait("Color", "U")]
public class RemoteIsleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RemoteIsle_HasManaAbilityProducingBlue()
    {
        var land = (Land)NamedCardFactory.Create("Remote Isle", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.Blue.Should().Be(1, "{T}: Add {U}");
    }

    [Fact]
    public void RemoteIsle_HasCyclingActivatedAbility_WithGenericTwoAndDiscardSelfCosts()
    {
        var land = (Land)NamedCardFactory.Create("Remote Isle", _alice);
        var cycling = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;

        cycling.Costs.Should().HaveCount(2, "cycling = mana cost + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "Remote Isle's cycling cost is {2}");
        manaCost.Blue.Should().Be(0);
    }

    [Fact]
    public void RemoteIsle_HasCyclingKeywordMarker()
    {
        var land = (Land)NamedCardFactory.Create("Remote Isle", _alice);
        land.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
    }

    [Fact]
    public void RemoteIsle_Cycling_EndToEnd_PaysGenericTwoDiscardsSelfDrawsOne()
    {
        // Seed library so the draw resolves.
        var topCard = new Card("Counterspell", "{U}{U}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new Majik.Core.Events.EventBus();
        Majik.Core.Events.CardCycledEvent? captured = null;
        bus.Subscribe<Majik.Core.Events.CardCycledEvent>(e => captured = e);

        var isle = RemoteIsleFactory.Create(_alice, bus);
        _alice.Zones.Hand.AddCard(isle);
        isle.SetZone(ZoneType.Hand);

        // {2} generic — pay with two blue.
        _alice.AddManaToPool(ManaCost.Parse("{U}{U}"));

        var cycling = isle.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }
        isle.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycle drew one card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(isle);
    }
}
