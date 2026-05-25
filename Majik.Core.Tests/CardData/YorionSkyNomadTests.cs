using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="YorionSkyNomadFactory"/>.
///
/// Card: Yorion, Sky Nomad — Legendary Creature — Bird Serpent {3}{W/U}{W/U} 4/5 (Ikoria).
///   "Companion — Your starting deck contains at least twenty cards more
///    than the minimum deck size."
///   "Flying"
///   "When Yorion enters, exile any number of other nonland permanents
///    you own and control. Return those cards to the battlefield at the
///    beginning of the next end step."
///
/// Covers:
///   - Identity / dispatch.
///   - Flying keyword marker.
///   - Companion deck-construction predicate: 80+ pass, 79 fail.
///   - ETB resolve auto-picks every non-Yorion nonland permanent owned
///     AND controlled, moves them to exile.
///   - Other criteria (lands, opponent's permanents, Yorion itself) skipped.
///   - Returns the picked set so callers / tests can audit.
/// </summary>
public class YorionSkyNomadTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Yorion_Identity()
    {
        var c = YorionSkyNomadFactory.Create(_alice);

        c.Name.Should().Be("Yorion, Sky Nomad");
        c.ManaCost.Should().Be("{3}{W/U}{W/U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(5);
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        // Serpent subtype deferred — see factory xmldoc.
        c.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Yorion_HasFlying()
    {
        var c = YorionSkyNomadFactory.Create(_alice);
        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Yorion()
    {
        var card = NamedCardFactory.Create("Yorion, Sky Nomad", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Yorion, Sky Nomad");
    }

    // -----------------------------------------------------------------------
    // Companion deck-construction predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void CompanionRestriction_80CardDeck_Passes()
    {
        var deck = BuildDeck(80);
        YorionSkyNomadFactory.CompanionRestriction.IsSatisfiedBy(deck)
            .Should().BeTrue("80 ≥ 60+20");
    }

    [Fact]
    public void CompanionRestriction_79CardDeck_Fails()
    {
        var deck = BuildDeck(79);
        YorionSkyNomadFactory.CompanionRestriction.IsSatisfiedBy(deck)
            .Should().BeFalse("79 < 60+20");
    }

    [Fact]
    public void CompanionRestriction_60CardDeck_Fails()
    {
        var deck = BuildDeck(60);
        YorionSkyNomadFactory.CompanionRestriction.IsSatisfiedBy(deck)
            .Should().BeFalse("60 is the minimum, not 60+20");
    }

    [Fact]
    public void CompanionRestriction_OverstuffedDeck_Passes()
    {
        var deck = BuildDeck(120);
        YorionSkyNomadFactory.CompanionRestriction.IsSatisfiedBy(deck)
            .Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB resolve — exile half
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveEtb_ExilesEveryNonLandPermanent_OwnedAndControlled()
    {
        var yorion = YorionSkyNomadFactory.Create(_alice);
        PlaceOnBattlefield(yorion, _alice);

        var bear = PlaceOnBattlefield(new Creature("Bear", "{1}{G}", 2, 2), _alice);
        var enchantment = PlaceOnBattlefield(new Enchantment("Curse", "{1}{W}"), _alice);
        var land = PlaceOnBattlefield(new Land("Plains"), _alice);

        var exiled = YorionSkyNomadFactory.ResolveEtb(
            yorion, _alice, eventBus: null, triggers: null, zoneService: null, pickPermanents: null);

        exiled.Should().Contain(bear);
        exiled.Should().Contain(enchantment);
        exiled.Should().NotContain(land, "lands are excluded");
        exiled.Should().NotContain(yorion, "Yorion blinks 'other' permanents, not itself");

        bear.Zone.Should().Be(ZoneType.Exile);
        enchantment.Zone.Should().Be(ZoneType.Exile);
        land.Zone.Should().Be(ZoneType.Battlefield);
        yorion.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void ResolveEtb_SkipsOpponentsPermanents()
    {
        var yorion = YorionSkyNomadFactory.Create(_alice);
        PlaceOnBattlefield(yorion, _alice);

        var bobBear = PlaceOnBattlefield(new Creature("Bob's Bear", "{1}{G}", 2, 2), _bob);

        var exiled = YorionSkyNomadFactory.ResolveEtb(
            yorion, _alice, eventBus: null, triggers: null, zoneService: null, pickPermanents: null);

        exiled.Should().NotContain(bobBear);
        bobBear.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void ResolveEtb_SkipsPermanentsControlledButNotOwned()
    {
        var yorion = YorionSkyNomadFactory.Create(_alice);
        PlaceOnBattlefield(yorion, _alice);

        // Bear owned by Bob but stolen / control-changed to Alice.
        var stolenBear = new Creature("Stolen", "{1}{G}", 2, 2);
        stolenBear.SetOwner(_bob);
        stolenBear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(stolenBear);
        stolenBear.SetZone(ZoneType.Battlefield);

        var exiled = YorionSkyNomadFactory.ResolveEtb(
            yorion, _alice, eventBus: null, triggers: null, zoneService: null, pickPermanents: null);

        exiled.Should().NotContain(stolenBear,
            "Yorion requires 'you own AND control' — stolen creatures fail the own check");
    }

    [Fact]
    public void ResolveEtb_CustomPicker_RestrictsScope()
    {
        var yorion = YorionSkyNomadFactory.Create(_alice);
        PlaceOnBattlefield(yorion, _alice);

        var bear1 = PlaceOnBattlefield(new Creature("Bear 1", "{1}{G}", 2, 2), _alice);
        var bear2 = PlaceOnBattlefield(new Creature("Bear 2", "{1}{G}", 2, 2), _alice);

        var exiled = YorionSkyNomadFactory.ResolveEtb(
            yorion, _alice, eventBus: null, triggers: null, zoneService: null,
            pickPermanents: (_, _) => new[] { (Permanent)bear1 });

        exiled.Should().ContainSingle().Which.Should().BeSameAs(bear1);
        bear1.Zone.Should().Be(ZoneType.Exile);
        bear2.Zone.Should().Be(ZoneType.Battlefield, "custom picker omitted it");
    }

    [Fact]
    public void ResolveEtb_NoQualifyingPermanents_ReturnsEmpty()
    {
        var yorion = YorionSkyNomadFactory.Create(_alice);
        PlaceOnBattlefield(yorion, _alice);

        var exiled = YorionSkyNomadFactory.ResolveEtb(
            yorion, _alice, eventBus: null, triggers: null, zoneService: null, pickPermanents: null);

        exiled.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IReadOnlyList<ICard> BuildDeck(int size)
    {
        var deck = new List<ICard>(size);
        for (var i = 0; i < size; i++)
        {
            deck.Add(new Creature($"Bear {i}", "{1}{G}", 2, 2));
        }
        return deck;
    }

    private T PlaceOnBattlefield<T>(T card, Player owner) where T : Card
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        return card;
    }
}
