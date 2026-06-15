using System.Linq;
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
/// Unit tests for <see cref="DeepCavernBatFactory"/>.
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (Creature — Bat 1/1 at {1}{B}) plus Flying + Lifelink markers.
/// - ETB exiles a nonland card from a target opponent's hand (CR 701.21).
/// - ETB skips lands (oracle "nonland card").
/// - ETB with a land-only / empty hand no-ops cleanly.
/// - LTB returns the exiled card to its owner's hand (CR 603.6c).
/// - LTB without an exiled card no-ops.
/// </summary>
[Trait("Color", "B")]
public class DeepCavernBatFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void DeepCavernBat_Identity()
    {
        var c = DeepCavernBatFactory.Create(_alice);

        c.Name.Should().Be("Deep-Cavern Bat");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bat).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain(new[] { "Flying", "Lifelink" },
                "Deep-Cavern Bat has Flying + Lifelink (CR 702.9 / CR 702.15)");

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }

    [Fact]
    public void DeepCavernBat_Etb_ExilesNonlandFromOpponentHand()
    {
        var bat = DeepCavernBatFactory.Create(_alice);
        bat.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bat);

        var land = new Land("Swamp");
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(land);

        var spell = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        spell.SetOwner(_bob);
        spell.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(spell);

        var etb = bat.Abilities.OfType<TriggeredAbility>()
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
    public void DeepCavernBat_Etb_LandOnlyHand_NoExile()
    {
        var bat = DeepCavernBatFactory.Create(_alice);
        bat.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bat);

        var land = new Land("Swamp");
        land.SetOwner(_bob);
        land.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(land);

        var etb = bat.Abilities.OfType<TriggeredAbility>()
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
    public void DeepCavernBat_Ltb_ReturnsExiledCardToOwnersHand()
    {
        var bat = DeepCavernBatFactory.Create(_alice);
        bat.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bat);

        var spell = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        spell.SetOwner(_bob);
        spell.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(spell);

        var etb = bat.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });
        foreach (var e in etb.Effects) e.Execute();
        spell.Zone.Should().Be(ZoneType.Exile);

        var ltb = bat.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        spell.Zone.Should().Be(ZoneType.Hand,
            "LTB returns the exiled card to its owner's hand (CR 603.6c)");
        _bob.Zones.Hand.GetCards().Should().Contain(spell);
        _bob.Zones.Exile.GetCards().Should().NotContain(spell);
    }

    [Fact]
    public void DeepCavernBat_Ltb_WithoutExile_NoOp()
    {
        var bat = DeepCavernBatFactory.Create(_alice);
        bat.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bat);

        var ltb = bat.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
