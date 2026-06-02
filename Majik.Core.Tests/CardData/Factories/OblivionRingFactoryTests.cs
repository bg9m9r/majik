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
/// Unit tests for <see cref="OblivionRingFactory"/>.
///
/// Oblivion Ring (Lorwyn / reprints, {2}{W}). Enchantment. Oracle text:
///   "When this enchantment enters, exile another target nonland permanent.
///    When this enchantment leaves the battlefield, return the exiled card to
///    the battlefield under its owner's control."
///
/// The original O-Ring template. Same exile-on-ETB / return-on-LTB shape as
/// <see cref="BanishingLightFactory"/>, with TWO differences from Banishing
/// Light:
///   - Targets "ANOTHER target nonland permanent" (CR 109.5 — "another"
///     excludes Oblivion Ring itself) instead of an opponent-controlled
///     permanent: Oblivion Ring can exile its OWN controller's permanents.
///   - No "an opponent controls" restriction.
///
/// Covers:
/// - Identity (Enchantment {2}{W}, owner / controller wired).
/// - Two triggered abilities (ETB exile + LTB return).
/// - ETB exiles a target nonland permanent (opponent's).
/// - ETB exiles the controller's OWN nonland permanent ("another", any
///   controller).
/// - ETB rejects lands at resolution time (CR 608.2b "nonland").
/// - ETB rejects Oblivion Ring itself ("another", CR 109.5).
/// - LTB returns the exiled card to the battlefield under its owner's control.
/// - LTB no-ops cleanly when nothing was exiled.
/// </summary>
[Trait("Color", "W")]
public class OblivionRingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void OblivionRing_Identity()
    {
        var c = OblivionRingFactory.Create(_alice);

        c.Name.Should().Be("Oblivion Ring");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }

    [Fact]
    public void OblivionRing_Dispatch_IsRegistered()
    {
        // NamedCardFactory.Create returns a vanilla shell for unknown names,
        // so a registered factory is distinguished by the real Enchantment
        // shape + its two triggered abilities.
        var card = NamedCardFactory.Create("Oblivion Ring", _alice);
        card.Should().BeOfType<Enchantment>(
            "[CardName(\"Oblivion Ring\")] must be wired into the dispatch table");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void OblivionRing_Etb_ExilesOpponentPermanent()
    {
        var ring = OblivionRingFactory.Create(_alice);
        ring.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ring);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = ring.Abilities.OfType<TriggeredAbility>()
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
    public void OblivionRing_Etb_ExilesControllerOwnPermanent()
    {
        var ring = OblivionRingFactory.Create(_alice);
        ring.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ring);

        // Oblivion Ring says "another target nonland permanent" — NO
        // opponent restriction. It can exile its own controller's permanent.
        var aliceCreature = new Creature("Bird", "{1}{W}", 1, 2);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var etb = ring.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { aliceCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        aliceCreature.Zone.Should().Be(ZoneType.Exile,
            "Oblivion Ring may exile any nonland permanent, including controller's own");
    }

    [Fact]
    public void OblivionRing_Etb_RejectsLandTarget()
    {
        var ring = OblivionRingFactory.Create(_alice);
        ring.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ring);

        var bobsLand = new Land("Forest");
        bobsLand.SetOwner(_bob);
        bobsLand.SetController(_bob);
        bobsLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsLand);

        var etb = ring.Abilities.OfType<TriggeredAbility>()
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
    public void OblivionRing_Etb_RejectsItself()
    {
        var ring = OblivionRingFactory.Create(_alice);
        ring.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ring);

        // "ANOTHER target nonland permanent" — CR 109.5: the source can't be
        // chosen. Even if the slot is filled with itself, the resolution
        // gate rejects it.
        var etb = ring.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ring },
        });
        foreach (var e in etb.Effects) e.Execute();

        ring.Zone.Should().Be(ZoneType.Battlefield,
            "'another' excludes Oblivion Ring itself (CR 109.5)");
    }

    [Fact]
    public void OblivionRing_Ltb_ReturnsExiledCardUnderOwnersControl()
    {
        var ring = OblivionRingFactory.Create(_alice);
        ring.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ring);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = ring.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();
        bobsCreature.Zone.Should().Be(ZoneType.Exile);

        var ltb = ring.Abilities.OfType<TriggeredAbility>()
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
    public void OblivionRing_Ltb_NoOpWhenNothingExiled()
    {
        var ring = OblivionRingFactory.Create(_alice);
        ring.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ring);

        var ltb = ring.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
