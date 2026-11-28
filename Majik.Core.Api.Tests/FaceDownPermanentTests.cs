using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// CR 708.2 — a face-down permanent is a 2/2 creature with no name, text,
/// subtypes, colour or mana cost, and no abilities beyond those its face-down
/// state grants. CR 708.6 — its controller may look at it. Opponents see only
/// the 2/2 (plus what stays visible: tapped, counters, attachments) and a
/// face-down marker; the controller (and the all-revealed spectator view) also
/// sees the card itself.
/// </summary>
public class FaceDownPermanentTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameStateDto Snap(Player? viewer) =>
        StateSnapshotter.Snapshot(Guid.NewGuid(), 1, null, _alice, new[] { _alice, _bob }, new Majik.Core.Stack.Stack(), viewer);

    /// <summary>Bob manifests the top card of his library: a creature with an
    /// activated ability, so the wrapper also gets "turn face up".</summary>
    private ManifestedCreature ManifestForBob()
    {
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        goyf.SetOwner(_bob); goyf.SetController(_bob);
        goyf.AddAbility(new ActivatedAbility(goyf, _bob,
            effects: new IEffect[] { new Effect("Pump.", () => { }) }));
        _bob.Zones.Library.AddCard(goyf); goyf.SetZone(ZoneType.Library);
        return ManifestEffect.Resolve(_bob)!;
    }

    private static CardSnapshotDto Battlefield(GameStateDto s, Player owner) =>
        s.Players.Single(p => p.Id == owner.Id).Battlefield.Cards.Single();

    [Fact]
    public void AnOpponent_SeesOnlyAFaceDown2x2_WithTheMarker()
    {
        var wrapper = ManifestForBob();
        wrapper.Counters.Add(Majik.Core.Counters.CounterType.PlusOnePlusOne, 1);

        var dto = Battlefield(Snap(_alice), _bob);

        dto.InstanceId.Should().Be(wrapper.InstanceId);
        dto.Name.Should().Be(EventPayloadBuilder.FaceDownName);
        dto.ManaCost.Should().BeEmpty();
        dto.Types.Should().Equal("Creature");
        dto.Power.Should().NotBeNull();
        dto.Abilities.Should().BeEmpty("the turn-face-up ability would reveal that it is a creature card");
        dto.ProducedManaColors.Should().BeEmpty();
        dto.Counters.Should().ContainKey("+1/+1", "counters on a face-down permanent are visible");
        dto.IsFaceDown.Should().BeTrue();
        dto.FaceDownCard.Should().BeNull();
    }

    [Fact]
    public void ItsController_SeesTheRealCard_WithTheMarker()
    {
        var wrapper = ManifestForBob();

        foreach (var view in new[] { Snap(_bob), Snap(null) })
        {
            var dto = Battlefield(view, _bob);
            dto.InstanceId.Should().Be(wrapper.InstanceId);
            dto.Name.Should().Be("Tarmogoyf");
            dto.IsFaceDown.Should().BeTrue();
            dto.FaceDownCard!.Name.Should().Be("Tarmogoyf");
            dto.FaceDownCard.ManaCost.Should().Be("{1}{G}");
            dto.Abilities.Should().NotBeEmpty("the controller can turn it face up");
        }
    }

    [Fact]
    public void AFaceUpPermanent_CarriesNoMarker()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob); bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear); bear.SetZone(ZoneType.Battlefield);

        var dto = Battlefield(Snap(_alice), _bob);
        dto.IsFaceDown.Should().BeFalse();
        dto.FaceDownCard.Should().BeNull();
        dto.Name.Should().Be("Grizzly Bears");
    }

    [Fact]
    public void AFaceDownPermanentWithPrintedTypes_ShowsOnlyCreatureToAnOpponent()
    {
        var land = new Land("Secret Land");
        land.SetOwner(_bob); land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land); land.SetZone(ZoneType.Battlefield);
        land.MarkFaceDown();

        var dto = Battlefield(Snap(_alice), _bob);
        dto.Name.Should().Be(EventPayloadBuilder.FaceDownName);
        dto.Types.Should().Equal("Creature");
        dto.Power.Should().Be(2);
        dto.Toughness.Should().Be(2);
        dto.ProducedManaColors.Should().BeEmpty();
    }

    [Fact]
    public void ItsActivatedAbilityOnTheStack_IsNotDescribedToAnOpponent()
    {
        var wrapper = ManifestForBob();
        var turnFaceUp = wrapper.Abilities.OfType<FaceDownActivatedAbility>().Single();
        var stack = new Majik.Core.Stack.Stack();
        stack.Push(turnFaceUp);

        StateSnapshotter.Snapshot(Guid.NewGuid(), 1, null, _alice, new[] { _alice, _bob }, stack, _alice)
            .Stack.Single().Description.Should().NotContain("Tarmogoyf");
        StateSnapshotter.Snapshot(Guid.NewGuid(), 1, null, _alice, new[] { _alice, _bob }, stack, _bob)
            .Stack.Single().Description.Should().Contain("Tarmogoyf");

        var added = new Majik.Core.Domain.DomainEvents.StackObjectAddedEvent(turnFaceUp);
        EventPayloadBuilder.RequiresPerViewerMasking(added).Should().BeTrue();
        EventPayloadBuilder.Build(added, _alice).GetProperty("description").GetString().Should().NotContain("Tarmogoyf");

        var activated = new Majik.Core.Domain.DomainEvents.AbilityActivatedEvent(turnFaceUp);
        EventPayloadBuilder.RequiresPerViewerMasking(activated).Should().BeTrue();
        EventPayloadBuilder.Build(activated, _alice).GetProperty("sourceName").GetString().Should().Be(EventPayloadBuilder.FaceDownName);
        EventPayloadBuilder.Build(activated, _bob).GetProperty("sourceName").GetString().Should().Be("Tarmogoyf");
    }

    private sealed class TestContinuousEffect : ContinuousEffect
    {
        private readonly Permanent? _source;
        public TestContinuousEffect(Permanent? source) => _source = source;
        public override Layer Layer => Layer.PT_Modify;
        public override Permanent? Source => _source;
        public override bool AppliesTo(Creature creature) => false;
        public override void Apply(CreatureCharacteristics chars) { }
    }

    [Fact]
    public void AContinuousEffectFromAFaceDownSource_IsNotNamedToAnOpponent()
    {
        var wrapper = ManifestForBob();
        foreach (GameEvent e in new GameEvent[]
                 {
                     new ContinuousEffectAddedEvent(new TestContinuousEffect(wrapper)),
                     new ContinuousEffectRemovedEvent(new TestContinuousEffect(wrapper)),
                 })
        {
            EventPayloadBuilder.RequiresPerViewerMasking(e).Should().BeTrue();
            EventPayloadBuilder.Build(e, _alice).GetProperty("sourceName").GetString().Should().Be(EventPayloadBuilder.FaceDownName);
            EventPayloadBuilder.Build(e, _bob).GetProperty("sourceName").GetString().Should().Be("Tarmogoyf");
        }
    }

    [Fact]
    public void AnOpponentsFaceDownPermanent_IsMaskedOnTargetAndChoicePrompts()
    {
        var wrapper = ManifestForBob();
        var stack = new Majik.Core.Stack.Stack(new EventBus());
        var ctx = new Majik.Core.Game.GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain, stack);

        var targets = new RemoteAgent(_alice, _ => wrapper);
        _ = targets.ChooseTargetsAsync(ctx, new Majik.Core.Players.Agents.TargetRequest(
            "target creature", 1, 1, new object[] { wrapper }, BotIntent.None));
        var candidate = targets.PendingPayload!.Candidates!.Single();
        candidate.Name.Should().Be(EventPayloadBuilder.FaceDownName);
        candidate.IsFaceDown.Should().BeTrue();

        var choice = new RemoteAgent(_alice, _ => wrapper);
        _ = choice.ChooseAsync(ctx, new Majik.Core.Players.Agents.ChoiceRequest(
            Majik.Core.Players.Agents.ChoiceKind.PickOne, "choose a creature", 1, 1, new object[] { wrapper }));
        choice.PendingPayload!.Candidates!.Single().Name.Should().Be(EventPayloadBuilder.FaceDownName);
        choice.PendingPayload.ChoiceOptions!.Single().Label.Should().Be(EventPayloadBuilder.FaceDownName);

        var mine = new RemoteAgent(_bob, _ => wrapper);
        _ = mine.ChooseTargetsAsync(ctx, new Majik.Core.Players.Agents.TargetRequest(
            "target creature", 1, 1, new object[] { wrapper }, BotIntent.None));
        mine.PendingPayload!.Candidates!.Single().Name.Should().Be("Tarmogoyf");
    }

    [Fact]
    public void AFaceDownPermanentEnteringTheBattlefield_IsMaskedInItsCardMovedPayload()
    {
        var wrapper = ManifestForBob();
        var moved = new CardMovedEvent(wrapper, ZoneType.Library, ZoneType.Battlefield);

        EventPayloadBuilder.RequiresPerViewerMasking(moved).Should().BeTrue();
        var theirs = EventPayloadBuilder.Build(moved, _alice);
        theirs.GetProperty("cardName").GetString().Should().Be(EventPayloadBuilder.FaceDownName);
        theirs.GetProperty("manaCost").GetString().Should().BeEmpty();
        theirs.GetProperty("isFaceDown").GetBoolean().Should().BeTrue();
        theirs.GetProperty("abilities").EnumerateArray().Should().BeEmpty();
        var mine = EventPayloadBuilder.Build(moved, _bob);
        mine.GetProperty("cardName").GetString().Should().Be("Tarmogoyf");
        mine.GetProperty("isFaceDown").GetBoolean().Should().BeTrue();
    }
}
