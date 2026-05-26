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
/// Unit tests for <see cref="BanishingLightFactory"/>.
///
/// Covers:
/// - Identity (Enchantment {2}{W}, owner / controller wired).
/// - NamedCardFactory dispatch.
/// - Two triggered abilities (ETB exile + LTB return).
/// - ETB exiles a target nonland permanent an opponent controls.
/// - ETB rejects lands at resolution time.
/// - ETB rejects controller-side permanents (not "an opponent controls").
/// - LTB returns the exiled card to the battlefield under its owner's
///   control.
/// - LTB no-ops cleanly when nothing was exiled.
/// </summary>
public class BanishingLightFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BanishingLight_Identity()
    {
        var c = BanishingLightFactory.Create(_alice);

        c.Name.Should().Be("Banishing Light");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }

    [Fact]
    public void BanishingLight_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Banishing Light", _alice);

        c.Should().BeOfType<Enchantment>();
        c.Name.Should().Be("Banishing Light");
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void BanishingLight_Etb_ExilesOpponentPermanent()
    {
        var light = BanishingLightFactory.Create(_alice);
        light.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(light);

        // Bob's creature is the target.
        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = light.Abilities.OfType<TriggeredAbility>()
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
    public void BanishingLight_Etb_RejectsLandTarget()
    {
        var light = BanishingLightFactory.Create(_alice);
        light.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(light);

        // Bob's land — should be rejected by the "nonland" gate even
        // if the target slot is filled (CR 608.2b illegal at resolution).
        var bobsLand = new Land("Forest");
        bobsLand.SetOwner(_bob);
        bobsLand.SetController(_bob);
        bobsLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsLand);

        var etb = light.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsLand },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobsLand.Zone.Should().Be(ZoneType.Battlefield,
            "lands are skipped by the printed 'nonland' filter");
    }

    [Fact]
    public void BanishingLight_Etb_RejectsControllerOwnPermanent()
    {
        var light = BanishingLightFactory.Create(_alice);
        light.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(light);

        // Alice's own creature — printed "an opponent controls" rejects
        // controller-side targets (CR 109.5).
        var aliceCreature = new Creature("Bird", "{1}{W}", 1, 2);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var etb = light.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { aliceCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            "ETB ignores controller-side permanents (oracle: 'an opponent controls')");
    }

    [Fact]
    public void BanishingLight_Ltb_ReturnsExiledCardUnderOwnersControl()
    {
        var light = BanishingLightFactory.Create(_alice);
        light.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(light);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        // ETB exile.
        var etb = light.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();
        bobsCreature.Zone.Should().Be(ZoneType.Exile);

        // LTB — Banishing Light leaves the battlefield.
        var ltb = light.Abilities.OfType<TriggeredAbility>()
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
    public void BanishingLight_Ltb_NoOpWhenNothingExiled()
    {
        var light = BanishingLightFactory.Create(_alice);
        light.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(light);

        // No ETB run — LTB should no-op without throwing.
        var ltb = light.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
