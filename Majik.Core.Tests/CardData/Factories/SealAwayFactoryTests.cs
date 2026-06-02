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
/// Unit tests for <see cref="SealAwayFactory"/> (Dominaria, {1}{W}).
///
/// Covers:
/// - Identity (Enchantment {1}{W}, owner / controller wired, Flash marker).
/// - NamedCardFactory dispatch.
/// - Two triggered abilities (ETB exile + LTB return).
/// - ETB exiles a tapped creature an opponent controls.
/// - ETB rejects untapped targets at resolution (printed "tapped").
/// - ETB rejects noncreature targets at resolution (printed "creature").
/// - ETB rejects controller-side permanents (printed "an opponent controls").
/// - LTB returns the exiled card to the battlefield under its owner's control.
/// - LTB no-ops cleanly when nothing was exiled.
/// </summary>
[Trait("Color", "W")]
public class SealAwayFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature TappedBobCreature(string name = "Tarmogoyf")
    {
        var c = new Creature(name, "{1}{G}", 0, 1);
        c.SetOwner(_bob);
        c.SetController(_bob);
        c.SetZone(ZoneType.Battlefield);
        c.Tap();
        _bob.Zones.Battlefield.AddCard(c);
        return c;
    }

    private Enchantment SealAwayOnBattlefield()
    {
        var seal = SealAwayFactory.Create(_alice);
        seal.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(seal);
        return seal;
    }

    private static TriggeredAbility EtbOf(Enchantment seal) =>
        seal.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 1);

    private static TriggeredAbility LtbOf(Enchantment seal) =>
        seal.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 0);

    [Fact]
    public void SealAway_Identity_WithFlash()
    {
        var c = SealAwayFactory.Create(_alice);

        c.Name.Should().Be("Seal Away");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flash", "Seal Away has Flash (CR 702.8)");
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }

    [Fact]
    public void SealAway_Etb_ExilesTappedOpponentCreature()
    {
        var seal = SealAwayOnBattlefield();
        var goyf = TappedBobCreature();

        var etb = EtbOf(seal);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { goyf } });
        foreach (var e in etb.Effects) e.Execute();

        goyf.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles the targeted tapped opponent creature (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(goyf);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goyf);
    }

    [Fact]
    public void SealAway_Etb_RejectsUntappedTarget()
    {
        var seal = SealAwayOnBattlefield();

        // An untapped opponent creature — must not be exiled (printed "tapped").
        var untapped = new Creature("Llanowar Elves", "{G}", 1, 1);
        untapped.SetOwner(_bob);
        untapped.SetController(_bob);
        untapped.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(untapped);

        var etb = EtbOf(seal);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { untapped } });
        foreach (var e in etb.Effects) e.Execute();

        untapped.Zone.Should().Be(ZoneType.Battlefield,
            "untapped creatures are skipped by the printed 'tapped' filter (CR 608.2b)");
    }

    [Fact]
    public void SealAway_Etb_RejectsNoncreatureTarget()
    {
        var seal = SealAwayOnBattlefield();

        // A tapped opponent land — must not be exiled (printed "creature").
        var land = new Land("Forest");
        land.SetOwner(_bob);
        land.SetController(_bob);
        land.SetZone(ZoneType.Battlefield);
        land.Tap();
        _bob.Zones.Battlefield.AddCard(land);

        var etb = EtbOf(seal);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { land } });
        foreach (var e in etb.Effects) e.Execute();

        land.Zone.Should().Be(ZoneType.Battlefield,
            "noncreatures are skipped by the printed 'creature' filter (CR 608.2b)");
    }

    [Fact]
    public void SealAway_Etb_RejectsControllerOwnCreature()
    {
        var seal = SealAwayOnBattlefield();

        // Alice's own tapped creature — printed "an opponent controls" rejects
        // controller-side targets (CR 109.5).
        var aliceCreature = new Creature("Bird", "{1}{W}", 1, 2);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        aliceCreature.Tap();
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var etb = EtbOf(seal);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { aliceCreature } });
        foreach (var e in etb.Effects) e.Execute();

        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            "ETB ignores controller-side permanents (oracle: 'an opponent controls')");
    }

    [Fact]
    public void SealAway_Ltb_ReturnsExiledCardUnderOwnersControl()
    {
        var seal = SealAwayOnBattlefield();
        var goyf = TappedBobCreature();

        var etb = EtbOf(seal);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { goyf } });
        foreach (var e in etb.Effects) e.Execute();
        goyf.Zone.Should().Be(ZoneType.Exile);

        // LTB — Seal Away leaves the battlefield.
        var ltb = LtbOf(seal);
        foreach (var e in ltb.Effects) e.Execute();

        goyf.Zone.Should().Be(ZoneType.Battlefield,
            "LTB returns the exiled card to the battlefield");
        goyf.Controller.Should().BeSameAs(_bob,
            "returned card is under its owner's control (CR 110.2)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(goyf);
        _bob.Zones.Exile.GetCards().Should().NotContain(goyf);
    }

    [Fact]
    public void SealAway_Ltb_NoOpWhenNothingExiled()
    {
        var seal = SealAwayOnBattlefield();

        // No ETB run — LTB should no-op without throwing.
        var ltb = LtbOf(seal);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
