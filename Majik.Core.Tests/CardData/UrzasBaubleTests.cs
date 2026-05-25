using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="UrzasBaubleFactory"/>.
/// Sister to <see cref="MishrasBaubleTests"/> — same shape, modulo the
/// look-at-half (random hand-card peek instead of top-of-library peek).
/// </summary>
public class UrzasBaubleTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void UrzasBauble_IsArtifact()
    {
        var bauble = UrzasBaubleFactory.Create(_alice);
        bauble.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void UrzasBauble_NameIsCorrect()
    {
        var bauble = UrzasBaubleFactory.Create(_alice);
        bauble.Name.Should().Be("Urza's Bauble");
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsUrzasBaubleShape()
    {
        var dispatched = NamedCardFactory.Create("Urza's Bauble", _alice);
        dispatched.Should().BeOfType<Artifact>();
        dispatched.Name.Should().Be("Urza's Bauble");
    }

    [Fact]
    public void UrzasBauble_HasExactlyOneActivatedAbility()
    {
        var bauble = UrzasBaubleFactory.Create(_alice);
        bauble.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void UrzasBauble_Ability_HasTapAndSacrificeCosts()
    {
        var bauble = UrzasBaubleFactory.Create(_alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap);
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice);
    }

    [Fact]
    public void Activation_LookAtRandomHand_DoesNotMoveHandCard()
    {
        var inHand = new Card("Secret", "");
        _alice.Zones.Hand.AddCard(inHand);
        inHand.SetZone(ZoneType.Hand);

        var bauble = UrzasBaubleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(inHand,
            "look-at-random-hand-card is information-only — the card stays in hand");
    }

    [Fact]
    public void Activation_SacrificesBauble_MovesToGraveyard()
    {
        var bauble = UrzasBaubleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(bauble);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bauble);
        bauble.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void NextUpkeepStepStarted_DrawsACard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var top = new Card("Top", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var bauble = UrzasBaubleFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Library.GetCards().Should().NotContain(top);
    }

    [Fact]
    public void NonUpkeepStep_DoesNotFireDelayedDraw()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var top = new Card("Top", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var bauble = UrzasBaubleFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _alice));
        bus.Publish(new StepStartedEvent(PhaseStateType.Main, _alice));
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().Be(0);
        _alice.Zones.Hand.GetCards().Should().NotContain(top);
    }
}
