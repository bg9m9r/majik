using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for World Breaker (Battle for Zendikar, {5}{G}).
///
/// Covers:
///   - Card identity (5/5 Creature — Eldrazi at {5}{G}).
///   - Ability list (Reach keyword + ETB trigger + attack trigger +
///     graveyard activation).
///   - ETB resolution: exiles a chosen nonbasic land target; rejects a
///     basic land at resolution (CR 608.2b).
///   - Attack resolution: exiles a chosen coloured permanent; rejects a
///     colourless permanent at resolution.
///   - Graveyard activation: card in owner's graveyard returns to owner's
///     hand via the exile staging post; mana cost shape is {G}.
///   - NamedCardFactory dispatch.
/// </summary>
public class WorldBreakerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void WorldBreaker_IsEldrazi_5_5_AtCost5G()
    {
        var wb = WorldBreakerFactory.Create(_alice);

        wb.Name.Should().Be("World Breaker");
        wb.ManaCost.Should().Be("{5}{G}");
        wb.Power.Should().Be(5);
        wb.Toughness.Should().Be(5);
        wb.HasType(CardType.Creature).Should().BeTrue();
        wb.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        wb.Owner.Should().BeSameAs(_alice);
        wb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WorldBreaker_HasReach_ETB_AttackTrigger_GraveyardActivation()
    {
        var wb = WorldBreakerFactory.Create(_alice);

        wb.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Reach");

        wb.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB + attack trigger");

        wb.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "graveyard-zone return-to-hand activation");
    }

    [Fact]
    public void WorldBreaker_GraveyardActivation_HasGreenManaCost()
    {
        var wb = WorldBreakerFactory.Create(_alice);

        var activated = wb.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.OfType<ManaCostCost>().Single().Cost.ToString()
            .Should().Contain("G");
    }

    [Fact]
    public void WorldBreaker_ETBTrigger_ExilesChosenNonbasicLand()
    {
        var wb = WorldBreakerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wb);
        wb.SetZone(ZoneType.Battlefield);

        // Bob controls a non-basic land.
        var nonbasic = new Land("Wasteland");
        nonbasic.SetOwner(_bob);
        nonbasic.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(nonbasic);
        nonbasic.SetZone(ZoneType.Battlefield);

        var etb = wb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("ETB")));
        etb.SetChosenTargets(new[] { new object[] { nonbasic } });

        foreach (var eff in etb.Effects) eff.Execute();

        _bob.Zones.Battlefield.GetCards().Should().NotContain(nonbasic);
        _bob.Zones.Exile.GetCards().Should().Contain(nonbasic);
        nonbasic.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void WorldBreaker_ETBTrigger_BasicLand_Fizzles()
    {
        var wb = WorldBreakerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wb);
        wb.SetZone(ZoneType.Battlefield);

        var basicForest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        basicForest.SetOwner(_bob);
        basicForest.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(basicForest);
        basicForest.SetZone(ZoneType.Battlefield);

        var etb = wb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("ETB")));
        etb.SetChosenTargets(new[] { new object[] { basicForest } });

        foreach (var eff in etb.Effects) eff.Execute();

        // CR 608.2b — illegal target at resolution → no exile.
        _bob.Zones.Battlefield.GetCards().Should().Contain(basicForest);
        basicForest.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void WorldBreaker_AttackTrigger_ExilesChosenColouredPermanent()
    {
        var wb = WorldBreakerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wb);
        wb.SetZone(ZoneType.Battlefield);

        var greenBeast = new Creature("Beast", "{2}{G}", 3, 3);
        greenBeast.SetOwner(_bob);
        greenBeast.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(greenBeast);
        greenBeast.SetZone(ZoneType.Battlefield);

        var attackTrigger = wb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("attack")));
        attackTrigger.SetChosenTargets(new[] { new object[] { greenBeast } });

        foreach (var eff in attackTrigger.Effects) eff.Execute();

        _bob.Zones.Battlefield.GetCards().Should().NotContain(greenBeast);
        _bob.Zones.Exile.GetCards().Should().Contain(greenBeast);
        greenBeast.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void WorldBreaker_AttackTrigger_ColourlessPermanent_Fizzles()
    {
        var wb = WorldBreakerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wb);
        wb.SetZone(ZoneType.Battlefield);

        // Colourless artifact creature — should NOT be exiled (oracle says
        // "one or more colors").
        var golem = new Creature("Golem", "{4}", 4, 4);
        golem.SetOwner(_bob);
        golem.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(golem);
        golem.SetZone(ZoneType.Battlefield);

        var attackTrigger = wb.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Effects.Any(e => e.Description.Contains("attack")));
        attackTrigger.SetChosenTargets(new[] { new object[] { golem } });

        foreach (var eff in attackTrigger.Effects) eff.Execute();

        _bob.Zones.Battlefield.GetCards().Should().Contain(golem,
            "colourless permanent is not a legal target — fizzles per CR 608.2b");
    }

    [Fact]
    public void WorldBreaker_GraveyardActivation_ReturnsCardToOwnersHand()
    {
        var wb = WorldBreakerFactory.Create(_alice);
        // Put World Breaker in Alice's graveyard.
        _alice.Zones.Graveyard.AddCard(wb);
        wb.SetZone(ZoneType.Graveyard);

        var activated = wb.Abilities.OfType<ActivatedAbility>().Single();

        // Drive the activated ability's effect directly (mana payment is
        // gated by the activator pipeline; this test exercises resolution).
        foreach (var eff in activated.Effects) eff.Execute();

        _alice.Zones.Graveyard.GetCards().Should().NotContain(wb);
        _alice.Zones.Hand.GetCards().Should().Contain(wb);
        wb.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void WorldBreaker_GraveyardActivation_NoOpFromBattlefield()
    {
        var wb = WorldBreakerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(wb);
        wb.SetZone(ZoneType.Battlefield);

        var activated = wb.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in activated.Effects) eff.Execute();

        // Guard rejects activation from a non-graveyard zone.
        _alice.Zones.Battlefield.GetCards().Should().Contain(wb);
        wb.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WorldBreaker()
    {
        var card = NamedCardFactory.Create("World Breaker", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("World Breaker");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).Power.Should().Be(5);
        ((Creature)card).Toughness.Should().Be(5);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<KeywordAbility>().Should().Contain(k => k.Keyword == "Reach");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }
}
