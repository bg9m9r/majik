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
/// Unit tests for <see cref="BorrowedTimeFactory"/>.
///
/// Borrowed Time (Murders at Karlov Manor, {2}{W}) — Enchantment.
///   "When this enchantment enters, exile target nonland permanent an
///    opponent controls until this enchantment leaves the battlefield."
///
/// Mechanics are the Banishing Light "Oblivion Ring" pair verbatim. Covers:
/// - Identity (Enchantment {2}{W}, owner / controller wired).
/// - Two triggered abilities (ETB exile + LTB return).
/// - ETB exiles a target nonland permanent an opponent controls.
/// - ETB rejects lands at resolution time (CR 608.2b).
/// - ETB rejects controller-side permanents ("an opponent controls", CR 109.5).
/// - LTB returns the exiled card under its owner's control (CR 110.2).
/// - LTB no-ops cleanly when nothing was exiled.
/// </summary>
[Trait("Color", "W")]
public class BorrowedTimeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BorrowedTime_Identity()
    {
        var c = BorrowedTimeFactory.Create(_alice);

        c.Name.Should().Be("Borrowed Time");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }

    [Fact]
    public void BorrowedTime_Etb_ExilesOpponentPermanent()
    {
        var enchantment = BorrowedTimeFactory.Create(_alice);
        enchantment.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(enchantment);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = enchantment.Abilities.OfType<TriggeredAbility>()
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
    public void BorrowedTime_Etb_RejectsLandTarget()
    {
        var enchantment = BorrowedTimeFactory.Create(_alice);
        enchantment.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(enchantment);

        var bobsLand = new Land("Forest");
        bobsLand.SetOwner(_bob);
        bobsLand.SetController(_bob);
        bobsLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsLand);

        var etb = enchantment.Abilities.OfType<TriggeredAbility>()
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
    public void BorrowedTime_Etb_RejectsControllerOwnPermanent()
    {
        var enchantment = BorrowedTimeFactory.Create(_alice);
        enchantment.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(enchantment);

        var aliceCreature = new Creature("Bird", "{1}{W}", 1, 2);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var etb = enchantment.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { aliceCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            "ETB ignores controller-side permanents (oracle: 'an opponent controls', CR 109.5)");
    }

    [Fact]
    public void BorrowedTime_Ltb_ReturnsExiledCardUnderOwnersControl()
    {
        var enchantment = BorrowedTimeFactory.Create(_alice);
        enchantment.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(enchantment);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = enchantment.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();
        bobsCreature.Zone.Should().Be(ZoneType.Exile);

        var ltb = enchantment.Abilities.OfType<TriggeredAbility>()
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
    public void BorrowedTime_Ltb_NoOpWhenNothingExiled()
    {
        var enchantment = BorrowedTimeFactory.Create(_alice);
        enchantment.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(enchantment);

        var ltb = enchantment.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
