using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using MajikStack = Majik.Core.Stack.Stack;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="OvalchaseDaredevilFactory"/> (Kaladesh, {3}{B}).
///
/// Creature — Human Pilot 4/2. Oracle text (verified against Scryfall):
///   "Whenever an artifact you control enters, you may return this card from
///    your graveyard to your hand."
///
/// Covers:
///   - Identity (Human Pilot 4/2 at {3}{B}).
///   - Artifact-enters trigger structure: active only while in the graveyard
///     (CR 603.6d — a graveyard-resident trigger).
///   - Mechanic: on resolution the Daredevil moves Graveyard → Hand.
///   - "You may": an agent that declines leaves it in the graveyard; the
///     legacy no-agent path auto-accepts.
///   - Live wiring: registered with a TriggerManager, an artifact entering
///     under the owner's control via ZoneService surfaces the trigger as
///     pending; an artifact entering under an opponent's control does NOT;
///     and the trigger is dormant while the Daredevil is on the battlefield.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "B")]
public class OvalchaseDaredevilFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutInGraveyard(Player p, ICard c)
    {
        p.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);
    }

    private static (ZoneService zones, MajikStack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new MajikStack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }

    private static Artifact PlayArtifact(ZoneService zones, Player controller, string name)
    {
        var art = new Artifact(name, "{1}");
        art.SetOwner(controller);
        art.SetController(controller);
        controller.Zones.Hand.AddCard(art);
        art.SetZone(ZoneType.Hand);
        zones.MoveCardTo(art, ZoneType.Battlefield, controller: controller);
        return art;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void OvalchaseDaredevil_Identity()
    {
        var c = OvalchaseDaredevilFactory.Create(_alice);

        c.Name.Should().Be("Ovalchase Daredevil");
        c.ManaCost.Should().Be("{3}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Pilot).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_OvalchaseDaredevil()
    {
        var card = NamedCardFactory.Create("Ovalchase Daredevil", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Ovalchase Daredevil");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Pilot).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Trigger structure (CR 603.6d — graveyard-resident)
    // -----------------------------------------------------------------------

    [Fact]
    public void ArtifactTrigger_IsActiveOnlyInGraveyard()
    {
        var c = OvalchaseDaredevilFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard);
        trigger.ActiveZones.Should().NotContain(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Mechanic (Graveyard → Hand)
    // -----------------------------------------------------------------------

    [Fact]
    public void ArtifactTrigger_ReturnsFromGraveyardToHand()
    {
        var daredevil = OvalchaseDaredevilFactory.Create(_alice);
        PutInGraveyard(_alice, daredevil);

        var trigger = daredevil.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(daredevil);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(daredevil);
        daredevil.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void ArtifactTrigger_NoOp_WhenNotInGraveyard()
    {
        // CR 603.6d — the return is re-checked at resolution. If the Daredevil
        // is no longer in the graveyard, nothing happens.
        var daredevil = OvalchaseDaredevilFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(daredevil);
        daredevil.SetZone(ZoneType.Battlefield);

        var trigger = daredevil.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().NotContain(daredevil);
        daredevil.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // "You may"
    // -----------------------------------------------------------------------

    [Fact]
    public void AgentDeclines_LeavesInGraveyard()
    {
        var agent = new ScriptedYesNoAgent(answer: false);
        var daredevil = OvalchaseDaredevilFactory.Create(
            _alice, zoneService: null, triggers: null, agent: agent);
        PutInGraveyard(_alice, daredevil);

        var trigger = daredevil.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(daredevil,
            "the 'may' return was declined");
        _alice.Zones.Hand.GetCards().Should().NotContain(daredevil);
    }

    [Fact]
    public void AgentAccepts_ReturnsToHand()
    {
        var agent = new ScriptedYesNoAgent(answer: true);
        var daredevil = OvalchaseDaredevilFactory.Create(
            _alice, zoneService: null, triggers: null, agent: agent);
        PutInGraveyard(_alice, daredevil);

        var trigger = daredevil.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(daredevil);
    }

    // -----------------------------------------------------------------------
    // Live wiring (CR 603.3 — artifact you control enters)
    // -----------------------------------------------------------------------

    [Fact]
    public void LiveWiring_ArtifactEntersUnderControl_ReturnsToHand()
    {
        var (zones, stack, triggers) = BuildEngine();

        var daredevil = OvalchaseDaredevilFactory.Create(
            _alice, zoneService: zones, triggers: triggers, agent: null);
        PutInGraveyard(_alice, daredevil);

        PlayArtifact(zones, _alice, "Ornithopter");

        triggers.PendingCount.Should().Be(1,
            "an artifact entering under the owner's control fires the trigger");

        triggers.PutPendingTriggersOnStack(activePlayer: _alice);
        var onStack = (TriggeredAbility)stack.Pop()!;
        onStack.Resolve();

        daredevil.Zone.Should().Be(ZoneType.Hand,
            "the Daredevil returns from graveyard to hand (CR 603.3)");
        _alice.Zones.Hand.GetCards().Should().Contain(daredevil);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(daredevil);
    }

    [Fact]
    public void LiveWiring_OpponentArtifactEnters_DoesNotFire()
    {
        var (zones, _, triggers) = BuildEngine();

        var daredevil = OvalchaseDaredevilFactory.Create(
            _alice, zoneService: zones, triggers: triggers, agent: null);
        PutInGraveyard(_alice, daredevil);

        PlayArtifact(zones, _bob, "Bob's Bauble");

        triggers.PendingCount.Should().Be(0,
            "only artifacts entering under the owner's control fire the trigger");
        daredevil.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void LiveWiring_DoesNotFire_WhenDaredevilIsOnBattlefield()
    {
        var (zones, _, triggers) = BuildEngine();

        var daredevil = OvalchaseDaredevilFactory.Create(
            _alice, zoneService: zones, triggers: triggers, agent: null);
        _alice.Zones.Battlefield.AddCard(daredevil);
        daredevil.SetZone(ZoneType.Battlefield);

        PlayArtifact(zones, _alice, "Ornithopter");

        triggers.PendingCount.Should().Be(0,
            "the graveyard-resident trigger (CR 603.6d) is dormant on the battlefield");
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

        public Task<bool> ChooseYesNoAsync(
            string question, BotIntent intent, CancellationToken ct = default)
            => Task.FromResult(_answer);

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
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> eligibleAttackers, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> attackers, IReadOnlyList<Permanent> eligibleBlockers, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
