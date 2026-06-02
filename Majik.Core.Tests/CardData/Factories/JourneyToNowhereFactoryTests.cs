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
/// Unit tests for <see cref="JourneyToNowhereFactory"/>.
///
/// Covers:
/// - Identity (Enchantment {1}{W}, owner / controller wired).
/// - Two triggered abilities (ETB exile + LTB return).
/// - ETB exiles a target creature an opponent controls.
/// - ETB exiles a creature the controller owns (no "an opponent
///   controls" restriction — unlike Banishing Light).
/// - ETB rejects a non-creature target at resolution time.
/// - LTB returns the exiled card to the battlefield under its owner's
///   control.
/// - LTB no-ops cleanly when nothing was exiled.
/// </summary>
[Trait("Color", "W")]
public class JourneyToNowhereFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void JourneyToNowhere_Identity()
    {
        var c = JourneyToNowhereFactory.Create(_alice);

        c.Name.Should().Be("Journey to Nowhere");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }

    [Fact]
    public void JourneyToNowhere_Etb_ExilesOpponentCreature()
    {
        var journey = JourneyToNowhereFactory.Create(_alice);
        journey.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(journey);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = journey.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles the targeted creature (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void JourneyToNowhere_Etb_ExilesControllerOwnCreature()
    {
        // Journey to Nowhere reads "target creature" with NO opponent
        // restriction (Rule 115.4) — self-targeting is legal, unlike
        // Banishing Light's "an opponent controls".
        var journey = JourneyToNowhereFactory.Create(_alice);
        journey.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(journey);

        var aliceCreature = new Creature("Bird", "{1}{W}", 1, 2);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var etb = journey.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { aliceCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        aliceCreature.Zone.Should().Be(ZoneType.Exile,
            "'target creature' has no controller restriction");
        _alice.Zones.Exile.GetCards().Should().Contain(aliceCreature);
    }

    [Fact]
    public void JourneyToNowhere_Etb_RejectsNonCreatureTarget()
    {
        var journey = JourneyToNowhereFactory.Create(_alice);
        journey.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(journey);

        // A non-creature permanent is illegal at resolution (CR 608.2b)
        // — the printed "target creature" gate rejects it.
        var bobsEnchantment = new Enchantment("Pacifism", "{1}{W}", null, null);
        bobsEnchantment.SetOwner(_bob);
        bobsEnchantment.SetController(_bob);
        bobsEnchantment.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsEnchantment);

        var etb = journey.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsEnchantment },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobsEnchantment.Zone.Should().Be(ZoneType.Battlefield,
            "non-creatures are skipped by the printed 'creature' filter");
    }

    [Fact]
    public void JourneyToNowhere_Ltb_ReturnsExiledCardUnderOwnersControl()
    {
        var journey = JourneyToNowhereFactory.Create(_alice);
        journey.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(journey);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        // ETB exile.
        var etb = journey.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();
        bobsCreature.Zone.Should().Be(ZoneType.Exile);

        // LTB — Journey to Nowhere leaves the battlefield.
        var ltb = journey.Abilities.OfType<TriggeredAbility>()
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
    public void JourneyToNowhere_Ltb_NoOpWhenNothingExiled()
    {
        var journey = JourneyToNowhereFactory.Create(_alice);
        journey.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(journey);

        // No ETB run — LTB should no-op without throwing.
        var ltb = journey.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
