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
using Planeswalker = Majik.Core.Cards.Planeswalker;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SemestersEndFactory"/> — Semester's End ({3}{W}):
/// "Exile any number of target creatures and/or planeswalkers you control. At
/// the beginning of the next end step, return each of them to the battlefield
/// under its owner's control. Each of them enters with an additional +1/+1
/// counter on it if it's a creature and an additional loyalty counter on it if
/// it's a planeswalker." (CR 701.21 exile + CR 603.7 delayed return + CR 614 +
/// CR 122.1b/c counters).
///
/// Covers identity / dispatch, the single multi-target definition shape, and
/// the "for many" exile-then-return-at-end-step-with-counters batch via the
/// declarative <c>exile_with_return</c> verb + its type-aware counter rider.
/// (Verb edge cases live in <c>JsonExileWithReturnTests</c>.)
/// </summary>
[Trait("Color", "W")]
public class SemestersEndFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly TriggerManager _triggers;

    public SemestersEndFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _triggers = new TriggerManager(_stack, _bus);
        TriggerManagerRegistry.Set(_triggers);
    }

    public void Dispose() => TriggerManagerRegistry.Clear();

    [Fact]
    public void Create_HasInstantShape_White_FourMana()
    {
        var card = SemestersEndFactory.Create(_alice);

        card.Name.Should().Be("Semester's End");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{W}");
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedFactory_Dispatches()
    {
        var card = NamedCardFactory.Create("Semester's End", _alice);
        card.Should().NotBeNull();
        card.Name.Should().Be("Semester's End");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void Definition_HasOneMultiTargetCreatureOrPlaneswalkerSlot()
    {
        var def = SemestersEndFactory.BuildDefinition();

        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(0, "\"any number of\"");
        def.TargetRequests[0].MaxTargets.Should().BeGreaterThan(1);
    }

    [Fact]
    public void ExilesMixedBatch_ReturnsWithCreaturePumpAndPlaneswalkerLoyalty()
    {
        var bear = NewControlledCreature(_alice, "Grizzly Bears", "{1}{G}");
        var pw = NewControlledPlaneswalker(_alice, "Ajani, Caller of the Pride", "{2}{W}", 4);

        var def = SemestersEndFactory.BuildDefinition();
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bear, pw } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob }));
        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Exile);
        pw.Zone.Should().Be(ZoneType.Exile);

        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        _triggers.PutPendingTriggersOnStack(_alice);
        while (_stack.Count > 0) _stack.Pop()!.Resolve();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        pw.Zone.Should().Be(ZoneType.Battlefield);
        bear.Controller.Should().BeSameAs(_alice, "CR 614 — under its owner's control");
        pw.Controller.Should().BeSameAs(_alice);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 122.1c — a creature returns with an additional +1/+1 counter");
        pw.Counters.Count(CounterType.Loyalty).Should().Be(1,
            "CR 122.1b — a planeswalker returns with an additional loyalty counter");
        pw.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
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

    private Planeswalker NewControlledPlaneswalker(Player owner, string name, string cost, int loyalty)
    {
        var pw = new Planeswalker(name, cost, loyalty);
        pw.SetOwner(owner);
        pw.SetController(owner);
        owner.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);
        return pw;
    }
}
