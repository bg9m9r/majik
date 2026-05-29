using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Oblivion Sower (Battle for Zendikar, {6}, Creature — Eldrazi 5/8).
///
/// Oracle: "When you cast this spell, target opponent exiles the top four
/// cards of their library, then you may put any number of land cards that
/// player owns from exile onto the battlefield under your control."
///
/// Covers:
/// - Identity (name, type, cost, P/T, subtype).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - One cast triggered ability, live on the Stack.
/// - Resolution exiles up to four cards of the OPPONENT's library.
/// - Land cards among the exiled four are eligible; non-lands are not.
/// - Default picker steals ALL eligible lands; they enter under the
///   controller's control while the opponent stays the owner.
/// - Custom picker can steal a subset / decline (empty).
/// - Short library exiles what remains, no throw.
/// </summary>
public class OblivionSowerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ICard Land(string name, Player owner)
    {
        var l = new Land(name);
        l.SetOwner(owner);
        return l;
    }

    [Fact]
    public void Identity_NameTypeCostPT()
    {
        var card = OblivionSowerFactory.Create(_alice);

        card.Name.Should().Be("Oblivion Sower");
        card.ManaCost.Should().Be("{6}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);
        card.ManaCostValue.TotalValue.Should().Be(6);

        var creature = card.Should().BeOfType<Creature>().Subject;
        creature.Power.Should().Be(5);
        creature.Toughness.Should().Be(8);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_OblivionSower()
    {
        var card = NamedCardFactory.Create("Oblivion Sower", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Oblivion Sower");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.Owner.Should().Be(_alice);
    }

    [Fact]
    public void Card_HasOneCastTriggeredAbility_LiveOnStack()
    {
        var card = OblivionSowerFactory.Create(_alice);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Should()
            .ContainSingle("Oblivion Sower prints one triggered ability — its cast trigger.")
            .Subject;

        trigger.ActiveZones.Should().Contain(ZoneType.Stack,
            "a 'when you cast this spell' trigger is live while the spell is on the stack.");
    }

    [Fact]
    public void Resolve_ExilesTopFourOfOpponentLibrary()
    {
        var cards = new ICard[]
        {
            new Sorcery("Top 1", "{R}"),
            new Sorcery("Top 2", "{R}"),
            new Sorcery("Top 3", "{R}"),
            new Sorcery("Top 4", "{R}"),
            new Sorcery("Bottom", "{R}"),
        };
        foreach (var c in cards)
        {
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var result = OblivionSowerFactory.ResolveCastTrigger(
            controller: _alice, opponent: _bob);

        result.Exiled.Should().HaveCount(4);
        result.Exiled.Select(c => c.Name).Should()
            .ContainInOrder("Top 1", "Top 2", "Top 3", "Top 4");
        _bob.Zones.Library.Count.Should().Be(1);
        _bob.Zones.Library.GetCards().Single().Name.Should().Be("Bottom");

        // No land cards exiled → nothing stolen, all four sit in Bob's exile.
        result.EligibleLands.Should().BeEmpty();
        result.Stolen.Should().BeEmpty();
        _bob.Zones.Exile.Count.Should().Be(4);
    }

    [Fact]
    public void Resolve_DefaultPicker_StealsAllExiledLands_UnderControllerControl()
    {
        // Top 4 of Bob's library: 2 lands, 1 instant, 1 creature.
        var island = Land("Island", _bob);
        var forest = Land("Forest", _bob);
        var bolt = new Instant("Lightning Bolt", "{R}"); bolt.SetOwner(_bob);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2); bear.SetOwner(_bob);

        foreach (var c in new ICard[] { island, forest, bolt, bear })
        {
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var result = OblivionSowerFactory.ResolveCastTrigger(
            controller: _alice, opponent: _bob);

        result.Exiled.Should().HaveCount(4);
        result.EligibleLands.Select(c => c.Name).Should()
            .BeEquivalentTo(new[] { "Island", "Forest" });
        result.Stolen.Select(c => c.Name).Should()
            .BeEquivalentTo(new[] { "Island", "Forest" });

        // Stolen lands: owner stays Bob, controller becomes Alice, on Alice's
        // battlefield (CR 110.2).
        foreach (var land in new[] { island, forest })
        {
            land.Zone.Should().Be(ZoneType.Battlefield);
            land.Owner.Should().Be(_bob);
            land.Controller.Should().Be(_alice);
        }
        _alice.Zones.Battlefield.GetCards().Select(c => c.Name).Should()
            .BeEquivalentTo(new[] { "Island", "Forest" });

        // Non-land exiled cards remain in Bob's exile.
        bolt.Zone.Should().Be(ZoneType.Exile);
        bear.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.Count.Should().Be(2);
    }

    [Fact]
    public void Resolve_CustomPicker_StealsSubset_Decline()
    {
        var island = Land("Island", _bob);
        var forest = Land("Forest", _bob);
        var mountain = Land("Mountain", _bob);
        var bolt = new Instant("Lightning Bolt", "{R}"); bolt.SetOwner(_bob);

        foreach (var c in new ICard[] { island, forest, mountain, bolt })
        {
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        // "any number" — controller takes only Island.
        var result = OblivionSowerFactory.ResolveCastTrigger(
            controller: _alice, opponent: _bob,
            chooseLands: lands => lands.Where(l => l.Name == "Island").ToList());

        result.EligibleLands.Should().HaveCount(3);
        result.Stolen.Select(c => c.Name).Should().BeEquivalentTo(new[] { "Island" });

        island.Controller.Should().Be(_alice);
        island.Zone.Should().Be(ZoneType.Battlefield);

        // The lands not chosen stay in Bob's exile (a card in exile has no
        // controller — controller is only set on the battlefield, CR 110.2).
        forest.Zone.Should().Be(ZoneType.Exile);
        mountain.Zone.Should().Be(ZoneType.Exile);
        forest.Owner.Should().Be(_bob);
    }

    [Fact]
    public void Resolve_DeclineMay_NoLandsStolen()
    {
        var island = Land("Island", _bob);
        foreach (var c in new ICard[] { island })
        {
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var result = OblivionSowerFactory.ResolveCastTrigger(
            controller: _alice, opponent: _bob,
            chooseLands: _ => Array.Empty<ICard>());

        result.EligibleLands.Should().ContainSingle();
        result.Stolen.Should().BeEmpty();
        island.Zone.Should().Be(ZoneType.Exile);
        island.Owner.Should().Be(_bob);
        _alice.Zones.Battlefield.Count.Should().Be(0);
    }

    [Fact]
    public void Resolve_ShortLibrary_ExilesWhatRemains_NoThrow()
    {
        var island = Land("Island", _bob);
        var bolt = new Instant("Lightning Bolt", "{R}"); bolt.SetOwner(_bob);
        foreach (var c in new ICard[] { island, bolt })
        {
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var result = OblivionSowerFactory.ResolveCastTrigger(
            controller: _alice, opponent: _bob);

        result.Exiled.Should().HaveCount(2);
        result.EligibleLands.Should().ContainSingle().Which.Name.Should().Be("Island");
        result.Stolen.Should().ContainSingle();
        island.Controller.Should().Be(_alice);
        _bob.Zones.Library.Count.Should().Be(0);
    }
}
