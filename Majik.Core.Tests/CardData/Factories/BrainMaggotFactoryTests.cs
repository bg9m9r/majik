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
/// Unit tests for <see cref="BrainMaggotFactory"/>.
///
/// Covers:
/// - Identity (Enchantment Creature — Insect 1/1 at {1}{B}, owner /
///   controller wired).
/// - Multi-type stamping: both Creature and Enchantment types.
/// - NamedCardFactory dispatch.
/// - Two triggered abilities (ETB exile + LTB return).
/// - ETB exiles a nonland card from a target opponent's hand.
/// - ETB skips lands (oracle "nonland").
/// - ETB with empty target hand no-ops cleanly.
/// - LTB returns the exiled card to its owner's hand.
/// - LTB without an exiled card no-ops.
/// </summary>
[Trait("Color", "B")]
public class BrainMaggotFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BrainMaggot_Identity()
    {
        var c = BrainMaggotFactory.Create(_alice);

        c.Name.Should().Be("Brain Maggot");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Enchantment).Should().BeTrue(
            "Brain Maggot is an Enchantment Creature (CR 301.1 / 302.1)");
        c.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }
    [Fact]
    public void BrainMaggot_Etb_ExilesNonlandFromOpponentHand()
    {
        var maggot = BrainMaggotFactory.Create(_alice);
        maggot.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(maggot);

        // Bob's hand: a land + a spell. ETB should pick the nonland.
        var land = new Land("Swamp");
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(land);

        var spell = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        spell.SetOwner(_bob);
        spell.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(spell);

        var etb = maggot.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();

        spell.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles a nonland card from the target opponent's hand (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(spell);
        _bob.Zones.Hand.GetCards().Should().NotContain(spell);
        _bob.Zones.Hand.GetCards().Should().Contain(land,
            "lands are skipped by the printed 'nonland' filter");
    }

    [Fact]
    public void BrainMaggot_Etb_LandOnlyHand_NoExile()
    {
        var maggot = BrainMaggotFactory.Create(_alice);
        maggot.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(maggot);

        var land = new Land("Swamp");
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(land);

        var etb = maggot.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();

        land.Zone.Should().Be(ZoneType.Hand,
            "no nonland card in hand → no exile (CR 701.21 — printed 'nonland' filter)");
        _bob.Zones.Exile.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void BrainMaggot_Etb_EmptyHand_NoExile()
    {
        var maggot = BrainMaggotFactory.Create(_alice);
        maggot.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(maggot);

        var etb = maggot.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        // Bob's hand is empty.
        foreach (var e in etb.Effects) e.Execute();

        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void BrainMaggot_Ltb_ReturnsExiledCardToOwnersHand()
    {
        var maggot = BrainMaggotFactory.Create(_alice);
        maggot.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(maggot);

        // Bob has a nonland in hand.
        var spell = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        spell.SetOwner(_bob);
        spell.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(spell);

        // ETB exile.
        var etb = maggot.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();
        spell.Zone.Should().Be(ZoneType.Exile);

        // LTB — Maggot leaves the battlefield.
        var ltb = maggot.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        spell.Zone.Should().Be(ZoneType.Hand,
            "LTB returns the exiled card to its owner's hand");
        _bob.Zones.Hand.GetCards().Should().Contain(spell);
        _bob.Zones.Exile.GetCards().Should().NotContain(spell);
    }

    [Fact]
    public void BrainMaggot_Ltb_WithoutExile_NoOp()
    {
        var maggot = BrainMaggotFactory.Create(_alice);
        maggot.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(maggot);

        // No ETB run — LTB should no-op.
        var ltb = maggot.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        // Should not throw or move any phantom card.
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
