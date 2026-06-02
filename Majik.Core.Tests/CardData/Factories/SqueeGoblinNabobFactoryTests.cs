using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SqueeGoblinNabobFactory"/> (Mercadian Masques,
/// {2}{R}).
///
/// Legendary Creature — Goblin 1/1. Oracle text (verified against Scryfall):
///   "At the beginning of your upkeep, you may return this card from your
///    graveyard to your hand."
///
/// Covers:
///   - Identity (Legendary Goblin 1/1 at {2}{R}).
///   - Upkeep trigger structure: filtered to the controller's own upkeep
///     (CR 500.4) and active only while Squee is in the graveyard
///     (CR 603.6d — a graveyard-resident trigger).
///   - Mechanic: on resolution Squee moves Graveyard → Hand.
///   - "You may": an agent that declines leaves Squee in the graveyard; the
///     legacy no-agent path auto-accepts.
///   - Live wiring: registered with a TriggerManager, an Upkeep
///     StepStartedEvent for the controller surfaces the trigger as pending.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "R")]
public class SqueeGoblinNabobFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutInGraveyard(Player p, ICard c)
    {
        p.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);
    }

    [Fact]
    public void Squee_Identity()
    {
        var c = SqueeGoblinNabobFactory.Create(_alice);

        c.Name.Should().Be("Squee, Goblin Nabob");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Squee_UpkeepTrigger_IsActiveOnlyInGraveyard()
    {
        var c = SqueeGoblinNabobFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard);
        trigger.ActiveZones.Should().NotContain(ZoneType.Battlefield);
    }

    [Fact]
    public void Squee_Upkeep_ReturnsFromGraveyardToHand()
    {
        var squee = SqueeGoblinNabobFactory.Create(_alice);
        PutInGraveyard(_alice, squee);

        var trigger = squee.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(squee);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(squee);
        squee.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Squee_Upkeep_NoOp_WhenNotInGraveyard()
    {
        // CR 603.6d — the return is re-checked at resolution. If Squee is no
        // longer in the graveyard (e.g. already returned / exiled), nothing
        // happens.
        var squee = SqueeGoblinNabobFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(squee);
        squee.SetZone(ZoneType.Battlefield);

        var trigger = squee.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().NotContain(squee);
        squee.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Squee_AgentDeclines_LeavesInGraveyard()
    {
        var agent = new ScriptedYesNoAgent(answer: false);
        var squee = SqueeGoblinNabobFactory.Create(
            _alice, zoneService: null, triggers: null, agent: agent);
        PutInGraveyard(_alice, squee);

        var trigger = squee.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(squee,
            "the 'may' return was declined");
        _alice.Zones.Hand.GetCards().Should().NotContain(squee);
    }

    [Fact]
    public void Squee_AgentAccepts_ReturnsToHand()
    {
        var agent = new ScriptedYesNoAgent(answer: true);
        var squee = SqueeGoblinNabobFactory.Create(
            _alice, zoneService: null, triggers: null, agent: agent);
        PutInGraveyard(_alice, squee);

        var trigger = squee.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(squee);
    }

    [Fact]
    public void Squee_LiveWiring_UpkeepStepRegistersPendingTrigger_OnlyControllersOwn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var squee = SqueeGoblinNabobFactory.Create(
            _alice, zoneService: null, triggers: triggers, agent: null);
        PutInGraveyard(_alice, squee);

        // Bob's upkeep — does NOT trigger (only the controller's own).
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(0,
            "Squee only triggers on its owner's own upkeep");

        // Alice's upkeep — trigger surfaces as pending.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Squee()
    {
        var card = NamedCardFactory.Create("Squee, Goblin Nabob", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Squee, Goblin Nabob");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    /// <summary>
    /// Scripted agent answering ChooseYesNoAsync with a fixed value. All other
    /// <see cref="IPlayerAgent"/> members fall through to the interface's
    /// default implementations (none are exercised by these tests).
    /// </summary>
    private sealed class ScriptedYesNoAgent : IPlayerAgent
    {
        private readonly bool _answer;
        public ScriptedYesNoAgent(bool answer) => _answer = answer;

        // A concrete class member implementing the interface beats the
        // interface's default ChooseYesNoAsync (which would auto-accept the
        // upside intent), so a scripted `false` genuinely declines.
        public Task<bool> ChooseYesNoAsync(
            string question, BotIntent intent, CancellationToken ct = default)
            => Task.FromResult(_answer);

        // Remaining non-default members are never exercised by these tests.
        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
