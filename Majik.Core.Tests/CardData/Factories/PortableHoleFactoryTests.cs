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
/// Unit tests for <see cref="PortableHoleFactory"/>.
///
/// Portable Hole (Adventures in the Forgotten Realms, {W}) — Artifact. Oracle
/// text (verified against Scryfall):
///   "When this artifact enters, exile target nonland permanent an opponent
///    controls with mana value 2 or less until this artifact leaves the
///    battlefield."
///
/// Same exile-on-ETB / return-on-LTB closure shape as
/// <see cref="BanishingLightFactory"/>, narrowed to an opponent's nonland
/// permanent with mana value 2 or less, on a {W} Artifact.
///
/// Covers:
/// - Identity (Artifact {W}, owner / controller wired, two triggered abilities).
/// - NamedCardFactory dispatch.
/// - ETB exiles a target nonland permanent (mv ≤ 2) an opponent controls.
/// - ETB rejects lands at resolution time.
/// - ETB rejects controller-side permanents (oracle: "an opponent controls").
/// - ETB rejects targets with mana value 3 or more (CR 202.3).
/// - LTB returns the exiled card to the battlefield under its owner's control.
/// - LTB no-ops cleanly when nothing was exiled.
/// </summary>
[Trait("Color", "W")]
public class PortableHoleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility Etb(Permanent card) =>
        card.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 1);

    private static TriggeredAbility Ltb(Permanent card) =>
        card.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 0);

    [Fact]
    public void PortableHole_Identity()
    {
        var c = PortableHoleFactory.Create(_alice);

        c.Name.Should().Be("Portable Hole");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }
    [Fact]
    public void PortableHole_Etb_ExilesOpponentLowMvPermanent()
    {
        var hole = PortableHoleFactory.Create(_alice);
        hole.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hole);

        // Bob's mv-2 creature is a legal target.
        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = Etb(hole);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsCreature } });
        foreach (var e in etb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles the targeted nonland permanent with mv ≤ 2 (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void PortableHole_Etb_RejectsLandTarget()
    {
        var hole = PortableHoleFactory.Create(_alice);
        hole.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hole);

        var bobsLand = new Land("Forest");
        bobsLand.SetOwner(_bob);
        bobsLand.SetController(_bob);
        bobsLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsLand);

        var etb = Etb(hole);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsLand } });
        foreach (var e in etb.Effects) e.Execute();

        bobsLand.Zone.Should().Be(ZoneType.Battlefield,
            "lands are skipped by the printed 'nonland' filter");
    }

    [Fact]
    public void PortableHole_Etb_RejectsControllerOwnPermanent()
    {
        var hole = PortableHoleFactory.Create(_alice);
        hole.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hole);

        var aliceCreature = new Creature("Bird", "{1}{W}", 1, 2);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var etb = Etb(hole);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { aliceCreature } });
        foreach (var e in etb.Effects) e.Execute();

        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            "ETB ignores controller-side permanents (oracle: 'an opponent controls')");
    }

    [Fact]
    public void PortableHole_Etb_RejectsHighManaValueTarget()
    {
        var hole = PortableHoleFactory.Create(_alice);
        hole.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hole);

        // mv 3 — above the printed "mana value 2 or less" cap (CR 202.3).
        var bobsCreature = new Creature("Tarmogoyf+", "{1}{G}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = Etb(hole);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsCreature } });
        foreach (var e in etb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Battlefield,
            "targets with mana value 3 or more are skipped by the 'mv ≤ 2' filter");
    }

    [Fact]
    public void PortableHole_Ltb_ReturnsExiledCardUnderOwnersControl()
    {
        var hole = PortableHoleFactory.Create(_alice);
        hole.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hole);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = Etb(hole);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsCreature } });
        foreach (var e in etb.Effects) e.Execute();
        bobsCreature.Zone.Should().Be(ZoneType.Exile);

        var ltb = Ltb(hole);
        foreach (var e in ltb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Battlefield,
            "LTB returns the exiled card to the battlefield");
        bobsCreature.Controller.Should().BeSameAs(_bob,
            "returned card is under its owner's control (CR 110.2)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Exile.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void PortableHole_Ltb_NoOpWhenNothingExiled()
    {
        var hole = PortableHoleFactory.Create(_alice);
        hole.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(hole);

        var ltb = Ltb(hole);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
