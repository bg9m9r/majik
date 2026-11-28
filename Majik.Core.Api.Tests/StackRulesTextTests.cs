using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// C20 — a stack entry carries its ability's own text (CR 113.2: an ability is
/// the text that generated it; CR 602.1: "cost: effect") and the id of the
/// ability it comes from, masked like the rest of the entry when its source
/// is hidden or face down (CR 706, CR 708.2).
/// </summary>
public class StackRulesTextTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private T OnBattlefield<T>(T card) where T : Card
    {
        card.SetOwner(_alice); card.SetController(_alice);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);
        return card;
    }

    private StackObjectDto Top(Majik.Core.Stack.Stack stack, Player? viewer) =>
        StateSnapshotter.Snapshot(Guid.Empty, 1, StepStateType.PreCombatMain, _alice,
            new[] { _alice, _bob }, stack, viewer).Stack.Last();

    [Fact]
    public void AnActivatedAbility_ShipsCostColonEffect_AndThePrintedAbilitysId()
    {
        var relic = OnBattlefield(new Artifact("Relic", "{1}"));
        var printed = new ActivatedAbility(relic, _alice,
            costs: new ICost[] { new ManaCostCost("2R") },
            effects: new IEffect[] { new Effect("Draw a card.", () => { }) });
        relic.AddAbility(printed);
        var stack = new Majik.Core.Stack.Stack(_bus);
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("{2}{R}"));

        // As TurnDriver/GameFacade DispatchActivate: the costs being paid are passed.
        new AbilityActivator(stack, _bus).ActivateAbility(printed, _alice, costs: printed.Costs);

        var dto = Top(stack, _bob);
        dto.Kind.Should().Be("ActivatedAbility");
        dto.RulesText.Should().Be("{2}{R}: Draw a card.");
        dto.AbilityId.Should().Be(printed.Id, "the card's AbilityDto.Id, not the stack copy's");
        dto.Id.Should().NotBe(printed.Id, "the stack object is a copy (CR 602.2a)");
    }

    [Fact]
    public void ATriggeredAbility_ShipsItsText_AndTheIdTheTriggeredEventCarried()
    {
        var bear = OnBattlefield(new Creature("Bear", "{1}{G}", 2, 2));
        var trigger = new TriggeredAbility(bear, _alice, Triggers.OnEnterBattlefieldSelf(bear),
            effects: new IEffect[] { new Effect("You gain 1 life.", () => { }) });
        var stack = new Majik.Core.Stack.Stack(_bus);
        stack.Push(trigger);

        var dto = Top(stack, _bob);
        dto.Description.Should().Be("Bear trigger");
        dto.RulesText.Should().Be("You gain 1 life.");
        dto.AbilityId.Should().Be(trigger.Id);
        var payload = EventPayloadBuilder.Build(new TriggeredAbilityTriggeredEvent(trigger, new GameStartedEvent()), _bob);
        payload.GetProperty("abilityId").GetGuid().Should().Be(dto.AbilityId!.Value);
    }

    [Fact]
    public void AFaceDownSourcesAbility_HasNoText_ForAPlayerWhoMayNotLook()
    {
        var morph = OnBattlefield(new Creature("Secret Morph", "{3}", 2, 2));
        morph.MarkFaceDown();
        var ability = new ActivatedAbility(morph, _alice,
            effects: new IEffect[] { new Effect("Secret Morph deals 2 damage.", () => { }) });
        var stack = new Majik.Core.Stack.Stack(_bus);
        stack.Push(ability);

        Top(stack, _bob).RulesText.Should().BeNull();
        Top(stack, _alice).RulesText.Should().Be("Secret Morph deals 2 damage.");
    }

    [Fact]
    public void ASpell_HasNoRulesText_ItsCardCarriesTheOracleText()
    {
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice); bolt.SetController(_alice); bolt.SetZone(ZoneType.Stack);
        var stack = new Majik.Core.Stack.Stack(_bus);
        stack.Push(new Majik.Core.Spells.Spell(bolt, _alice));

        var dto = Top(stack, _bob);
        dto.RulesText.Should().BeNull();
        dto.AbilityId.Should().BeNull();
    }

    [Fact]
    public void TheStackEvent_DescribesAnActivatedAbility_LikeTheSnapshot()
    {
        var relic = OnBattlefield(new Artifact("Relic", "{1}"));
        var ability = new ActivatedAbility(relic, _alice,
            effects: new IEffect[] { new Effect("Draw a card.", () => { }) });
        var bare = new ActivatedAbility(relic, _alice);

        EventPayloadBuilder.Build(new StackObjectAddedEvent(ability), _bob)
            .GetProperty("description").GetString().Should().Be("Relic: Draw a card.");
        EventPayloadBuilder.Build(new StackObjectAddedEvent(bare), _bob)
            .GetProperty("description").GetString().Should().Be("Relic ability");
    }
}