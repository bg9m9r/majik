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
/// Unit tests for <see cref="LeoninRelicWarderFactory"/>.
///
/// Covers:
/// - Identity (Creature {W}{W} 2/2 Cat Cleric, owner / controller wired).
/// - Two triggered abilities (ETB exile + LTB return).
/// - ETB exiles a target artifact.
/// - ETB exiles a target enchantment (and works for the controller's own
///   permanents — the printed text is not "an opponent controls").
/// - ETB rejects a non-artifact/non-enchantment target at resolution.
/// - LTB returns the exiled card to the battlefield under its owner's control.
/// - LTB no-ops cleanly when nothing was exiled ("you may" decline / no
///   legal target).
/// </summary>
[Trait("Color", "W")]
public class LeoninRelicWarderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility Etb(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 1);

    private static TriggeredAbility Ltb(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 0);

    [Fact]
    public void LeoninRelicWarder_Identity()
    {
        var c = LeoninRelicWarderFactory.Create(_alice);

        c.Name.Should().Be("Leonin Relic-Warder");
        c.ManaCost.Should().Be("{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }

    [Fact]
    public void LeoninRelicWarder_NamedFactoryDispatch()
    {
        var card = NamedCardFactory.Create("Leonin Relic-Warder", _alice);
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Leonin Relic-Warder");
    }

    [Fact]
    public void LeoninRelicWarder_Etb_ExilesArtifact()
    {
        var warder = LeoninRelicWarderFactory.Create(_alice);
        warder.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(warder);

        // Bob's artifact is the target.
        var bobsArtifact = new Artifact("Sol Ring", "{1}");
        bobsArtifact.SetOwner(_bob);
        bobsArtifact.SetController(_bob);
        bobsArtifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsArtifact);

        var etb = Etb(warder);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsArtifact },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobsArtifact.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles the targeted artifact (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(bobsArtifact);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobsArtifact);
    }

    [Fact]
    public void LeoninRelicWarder_Etb_ExilesControllersOwnEnchantment()
    {
        var warder = LeoninRelicWarderFactory.Create(_alice);
        warder.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(warder);

        // Alice's own enchantment — printed text is "target artifact or
        // enchantment" (NOT "an opponent controls"), so self-targeting is
        // legal (Rule 115.4).
        var aliceEnchantment = new Enchantment("Pacifism", "{1}{W}", null, null);
        aliceEnchantment.SetOwner(_alice);
        aliceEnchantment.SetController(_alice);
        aliceEnchantment.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceEnchantment);

        var etb = Etb(warder);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { aliceEnchantment },
        });
        foreach (var e in etb.Effects) e.Execute();

        aliceEnchantment.Zone.Should().Be(ZoneType.Exile,
            "ETB can exile the controller's own enchantment (no opponent gate)");
    }

    [Fact]
    public void LeoninRelicWarder_Etb_RejectsNonArtifactNonEnchantmentTarget()
    {
        var warder = LeoninRelicWarderFactory.Create(_alice);
        warder.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(warder);

        // A plain creature is neither artifact nor enchantment — rejected at
        // resolution (CR 608.2b).
        var bobsCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = Etb(warder);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Battlefield,
            "non-artifact / non-enchantment targets are skipped");
    }

    [Fact]
    public void LeoninRelicWarder_Ltb_ReturnsExiledCardUnderOwnersControl()
    {
        var warder = LeoninRelicWarderFactory.Create(_alice);
        warder.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(warder);

        var bobsArtifact = new Artifact("Sol Ring", "{1}");
        bobsArtifact.SetOwner(_bob);
        bobsArtifact.SetController(_bob);
        bobsArtifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsArtifact);

        // ETB exile.
        var etb = Etb(warder);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsArtifact },
        });
        foreach (var e in etb.Effects) e.Execute();
        bobsArtifact.Zone.Should().Be(ZoneType.Exile);

        // LTB — Leonin Relic-Warder leaves the battlefield.
        var ltb = Ltb(warder);
        foreach (var e in ltb.Effects) e.Execute();

        bobsArtifact.Zone.Should().Be(ZoneType.Battlefield,
            "LTB returns the exiled card to the battlefield");
        bobsArtifact.Controller.Should().BeSameAs(_bob,
            "returned card is under its owner's control (CR 110.2)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobsArtifact);
        _bob.Zones.Exile.GetCards().Should().NotContain(bobsArtifact);
    }

    [Fact]
    public void LeoninRelicWarder_Ltb_NoOpWhenNothingExiled()
    {
        var warder = LeoninRelicWarderFactory.Create(_alice);
        warder.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(warder);

        // No ETB run (declined "you may" / no legal target) — LTB no-ops
        // without throwing.
        var ltb = Ltb(warder);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
