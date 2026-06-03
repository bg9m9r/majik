using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="EerieInterludeFactory"/> — Eerie Interlude ({2}{W}):
/// "Exile any number of target creatures you control. Return those cards to
/// the battlefield under their owner's control at the beginning of the next
/// end step." (CR 701.21 exile + CR 603.7 delayed return + CR 614).
///
/// Covers identity / dispatch, the single multi-target definition shape, and
/// the "for many" exile-then-return-at-end-step batch via the declarative
/// <c>exile_with_return</c> verb. (Verb edge cases — decline, illegal target,
/// shape-only fallback — live in <c>JsonExileWithReturnTests</c>.)
/// </summary>
[Trait("Color", "W")]
public class EerieInterludeFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;

    public EerieInterludeFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        TriggerManagerRegistry.Set(_triggers);
    }

    public void Dispose() => TriggerManagerRegistry.Clear();

    [Fact]
    public void Create_HasInstantShape_White_ThreeMana()
    {
        var card = EerieInterludeFactory.Create(_alice);

        card.Name.Should().Be("Eerie Interlude");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{W}");
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedFactory_Dispatches()
    {
        var card = NamedCardFactory.Create("Eerie Interlude", _alice);
        card.Should().NotBeNull();
        card.Name.Should().Be("Eerie Interlude");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void Definition_HasOneMultiTargetCreatureSlot()
    {
        var def = EerieInterludeFactory.BuildDefinition();

        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(0, "\"any number of\"");
        def.TargetRequests[0].MaxTargets.Should().BeGreaterThan(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
    }

    [Fact]
    public void ExilesABatch_ThenReturnsItAtNextEndStep_UnderOwnersControl()
    {
        var a = NewControlledCreature(_alice, "Wall of Omens", "{1}{W}");
        var b = NewControlledCreature(_alice, "Savannah Lions", "{W}");

        var def = EerieInterludeFactory.BuildDefinition();
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { a, b } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob }));
        foreach (var e in effects) e.Execute();

        a.Zone.Should().Be(ZoneType.Exile);
        b.Zone.Should().Be(ZoneType.Exile);

        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        _triggers.PutPendingTriggersOnStack(_alice);
        while (_stack.Count > 0) _stack.Pop()!.Resolve();

        a.Zone.Should().Be(ZoneType.Battlefield);
        b.Zone.Should().Be(ZoneType.Battlefield);
        a.Controller.Should().BeSameAs(_alice, "CR 614 — under its owner's control");
        b.Controller.Should().BeSameAs(_alice);
        // Eerie Interlude adds NO counter (unlike Otherworldly Journey).
        a.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
    }

    private Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var bear = new Creature(name, cost, 2, 2);
        bear.SetOwner(owner);
        bear.SetController(owner);
        owner.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        return bear;
    }
}
