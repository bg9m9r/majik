using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CastOutFactory"/>.
///
/// Cast Out (Hour of Devastation, {3}{W}). Oracle text:
///   "Flash
///    When this enchantment enters, exile target nonland permanent an
///    opponent controls until this enchantment leaves the battlefield.
///    Cycling {W} ({W}, Discard this card: Draw a card.)"
///
/// Cast Out shares the exile-on-ETB / return-on-LTB backbone with
/// Banishing Light (CR 701.21), and adds Flash (CR 702.8) plus
/// Cycling {W} (CR 702.32).
///
/// Covers:
/// - Identity (Enchantment {3}{W}, owner / controller wired).
/// - Flash keyword marker present (CR 702.8).
/// - Cycling keyword marker present (CR 702.32).
/// - Two triggered abilities (ETB exile + LTB return).
/// - ETB exiles a target nonland permanent an opponent controls.
/// - ETB rejects lands at resolution time (CR 608.2b).
/// - ETB rejects controller-side permanents ("an opponent controls").
/// - LTB returns the exiled card under its owner's control (CR 110.2).
/// - LTB no-ops cleanly when nothing was exiled.
/// </summary>
[Trait("Color", "W")]
public class CastOutFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CastOut_Identity()
    {
        var c = CastOutFactory.Create(_alice);

        c.Name.Should().Be("Cast Out");
        c.ManaCost.Should().Be("{3}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }

    [Fact]
    public void CastOut_HasFlashKeyword()
    {
        var c = CastOutFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flash",
                "Cast Out has Flash (CR 702.8)");
    }

    [Fact]
    public void CastOut_HasCyclingKeyword()
    {
        var c = CastOutFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling",
                "Cast Out has Cycling {W} (CR 702.32)");
    }

    [Fact]
    public void CastOut_Etb_ExilesOpponentPermanent()
    {
        var castOut = CastOutFactory.Create(_alice);
        castOut.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(castOut);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = castOut.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles the targeted nonland permanent (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void CastOut_Etb_RejectsLandTarget()
    {
        var castOut = CastOutFactory.Create(_alice);
        castOut.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(castOut);

        var bobsLand = new Land("Forest");
        bobsLand.SetOwner(_bob);
        bobsLand.SetController(_bob);
        bobsLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsLand);

        var etb = castOut.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsLand },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobsLand.Zone.Should().Be(ZoneType.Battlefield,
            "lands are skipped by the printed 'nonland' filter (CR 608.2b)");
    }

    [Fact]
    public void CastOut_Etb_RejectsControllerOwnPermanent()
    {
        var castOut = CastOutFactory.Create(_alice);
        castOut.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(castOut);

        var aliceCreature = new Creature("Bird", "{1}{W}", 1, 2);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var etb = castOut.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { aliceCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            "ETB ignores controller-side permanents ('an opponent controls', CR 109.5)");
    }

    [Fact]
    public void CastOut_Ltb_ReturnsExiledCardUnderOwnersControl()
    {
        var castOut = CastOutFactory.Create(_alice);
        castOut.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(castOut);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = castOut.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();
        bobsCreature.Zone.Should().Be(ZoneType.Exile);

        var ltb = castOut.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Battlefield,
            "LTB returns the exiled card to the battlefield");
        bobsCreature.Controller.Should().BeSameAs(_bob,
            "returned card is under its owner's control (CR 110.2)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Exile.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void CastOut_Ltb_NoOpWhenNothingExiled()
    {
        var castOut = CastOutFactory.Create(_alice);
        castOut.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(castOut);

        var ltb = castOut.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
