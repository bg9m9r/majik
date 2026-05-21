using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AgathasSoulCauldronFactory"/>.
///
/// Covers:
/// - Card identity (name, Artifact type)
/// - Owner and controller assignment
/// - Activated ability shape: single Tap cost
/// - Exile effect: moves first graveyard card to Exile zone
/// - Counter effect: when exiled card is a creature, +1/+1 counter on first battlefield creature
/// - Counter effect: no counter when exiled card is not a creature
/// - Effect is a no-op when graveyard is empty
/// </summary>
public class AgathasSoulCauldronTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void AgathasSoulCauldron_NameIsCorrect()
    {
        var c = AgathasSoulCauldronFactory.Create(_alice);

        c.Name.Should().Be("Agatha's Soul Cauldron");
    }

    [Fact]
    public void AgathasSoulCauldron_IsArtifact()
    {
        var c = AgathasSoulCauldronFactory.Create(_alice);

        c.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void AgathasSoulCauldron_OwnerAndControllerAreSet()
    {
        var c = AgathasSoulCauldronFactory.Create(_alice);

        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void AgathasSoulCauldron_HasExactlyOneActivatedAbility()
    {
        var c = AgathasSoulCauldronFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "only the {T}: exile ability is wired in v1");
    }

    [Fact]
    public void AgathasSoulCauldron_TapAbility_HasSingleTapCost()
    {
        var c = AgathasSoulCauldronFactory.Create(_alice);
        var ability = c.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(1, "only a tap cost");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(cost => cost.CostType == AdditionalCostType.Tap,
                "the {T} cost");
    }

    // -----------------------------------------------------------------------
    // Exile effect — card movement
    // -----------------------------------------------------------------------

    [Fact]
    public void AgathasSoulCauldron_ExileEffect_MovesFirstGraveyardCardToExile()
    {
        var alice = new Player("Alice", 20);
        var card = new Card("Dead Card", "");
        alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);

        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        var ability = cauldron.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Exile.GetCards().Should().Contain(card,
            "the exile effect moves the graveyard card to exile");
        alice.Zones.Graveyard.GetCards().Should().NotContain(card,
            "the card is removed from the graveyard");
        card.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void AgathasSoulCauldron_ExileEffect_EmptyGraveyard_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        // Graveyard intentionally empty

        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        var ability = cauldron.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow("empty graveyard is a no-op");
        alice.Zones.Exile.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // +1/+1 counter placement
    // -----------------------------------------------------------------------

    [Fact]
    public void AgathasSoulCauldron_ExileEffect_CreatureCard_AddsCounterToFirstBattlefieldCreature()
    {
        var alice = new Player("Alice", 20);

        // Put a creature card in the graveyard.
        var deadCreature = new Creature("Dead Bear", "1G", 2, 2);
        deadCreature.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(deadCreature);
        deadCreature.SetZone(ZoneType.Graveyard);

        // Put a creature on the battlefield to receive the counter.
        var liveCreature = new Creature("Live Bear", "1G", 2, 2);
        liveCreature.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(liveCreature);
        liveCreature.SetZone(ZoneType.Battlefield);

        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        var ability = cauldron.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        liveCreature.Counters.Count(CounterType.PlusOnePlusOne)
            .Should().Be(1, "a creature card was exiled so the battlefield creature gains +1/+1");
    }

    [Fact]
    public void AgathasSoulCauldron_ExileEffect_NonCreatureCard_DoesNotAddCounter()
    {
        var alice = new Player("Alice", 20);

        // Put a non-creature card in the graveyard.
        var instant = new Instant("Shock", "R");
        instant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(instant);
        instant.SetZone(ZoneType.Graveyard);

        // Put a creature on the battlefield.
        var liveCreature = new Creature("Live Bear", "1G", 2, 2);
        liveCreature.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(liveCreature);
        liveCreature.SetZone(ZoneType.Battlefield);

        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        var ability = cauldron.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        liveCreature.Counters.Count(CounterType.PlusOnePlusOne)
            .Should().Be(0, "a non-creature card was exiled — no counter placed");
    }

    [Fact]
    public void AgathasSoulCauldron_ExileEffect_CreatureCard_NoBattlefieldCreature_DoesNotThrow()
    {
        var alice = new Player("Alice", 20);

        // Creature card in graveyard, nothing on battlefield.
        var deadCreature = new Creature("Dead Bear", "1G", 2, 2);
        deadCreature.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(deadCreature);
        deadCreature.SetZone(ZoneType.Graveyard);

        var cauldron = AgathasSoulCauldronFactory.Create(alice);
        var ability = cauldron.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow("no creature to buff is silently handled");
    }
}
