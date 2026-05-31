using System.Text.Json;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

public class StateSnapshotterTests
{
    private readonly EventBus _bus = new();

    [Fact]
    public void Snapshot_NewGame_NoCycles_SerializesCleanly()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var stack = new Majik.Core.Stack.Stack(_bus);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice, bob }, stack);

        dto.Players.Should().HaveCount(2);
        dto.ActivePlayerId.Should().Be(alice.Id);
        dto.Phase.Should().Be("PreCombatMain");
        dto.Stack.Should().BeEmpty();

        // Must round-trip — no cycles.
        var json = JsonSerializer.Serialize(dto);
        var back = JsonSerializer.Deserialize<GameStateDto>(json);
        back.Should().BeEquivalentTo(dto);
    }

    [Fact]
    public void Snapshot_CardInZone_AppearsInZoneDto()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice };
        alice.Zones.Library.AddCard(bear);
        var zones = new ZoneService(_bus);
        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: alice);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice }, new Majik.Core.Stack.Stack(_bus));

        var aliceDto = dto.Players.Single(p => p.Id == alice.Id);
        aliceDto.Battlefield.Cards.Should().ContainSingle()
            .Which.Name.Should().Be("Bear");
        aliceDto.Battlefield.Cards[0].Power.Should().Be(2);
        aliceDto.Battlefield.Cards[0].InstanceId.Should().Be(bear.InstanceId);
    }

    [Fact]
    public void Snapshot_StackHasSpell_DescribedInDto()
    {
        var alice = new Player("Alice", 20);
        var bolt = new Instant("Bolt", "R") { Owner = alice };
        var stack = new Majik.Core.Stack.Stack(_bus);
        var spell = new Majik.Core.Spells.Spell(bolt, alice);
        stack.Push(spell);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice, new[] { alice }, stack);

        dto.Stack.Should().ContainSingle();
        dto.Stack[0].Kind.Should().Be("Spell");
        dto.Stack[0].ControllerId.Should().Be(alice.Id);
        dto.Stack[0].Description.Should().Be("Bolt");
    }

    [Fact]
    public void Snapshot_StackHasActivatedAbility_DescriptionIncludesSourceNameAndEffect()
    {
        // PR #1003 follow-up: an IActivatedAbility on the stack used to surface
        // as the generic "ability" string, so the portal stack lane rendered
        // "ActivatedAbility" for every fetchland / planeswalker / sac-draw
        // activation. The DTO must now derive a human-readable description from
        // the source card name + the first effect's text — matching the
        // ITriggeredAbility case above ("<source name> trigger").
        var alice = new Player("Alice", 20);
        var fetch = new Majik.Core.Cards.Land("Windswept Heath") { Owner = alice };
        var effect = new Majik.Core.Abilities.Effect(
            "search library for Forest or Plains, put onto battlefield",
            () => { });
        var ability = new ActivatedAbility(
            source: fetch,
            controller: alice,
            effects: new Majik.Core.Abilities.IEffect[] { effect });
        var stack = new Majik.Core.Stack.Stack(_bus);
        stack.Push(ability);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice }, stack);

        var stackDto = dto.Stack.Should().ContainSingle().Subject;
        stackDto.Kind.Should().Be("ActivatedAbility");
        stackDto.ControllerId.Should().Be(alice.Id);
        stackDto.Description.Should().Be(
            "Windswept Heath: search library for Forest or Plains, put onto battlefield");
    }

    [Fact]
    public void Snapshot_StackHasActivatedAbility_EffectAlreadyLeadsWithSourceName_NoStutter()
    {
        // Effect descriptions that already prefix the card name (e.g. the
        // existing FetchLandCycleFactory closure "Windswept Heath: search
        // library for ...") shouldn't be turned into "Windswept Heath:
        // Windswept Heath: search library for ...".
        var alice = new Player("Alice", 20);
        var fetch = new Majik.Core.Cards.Land("Windswept Heath") { Owner = alice };
        var effect = new Majik.Core.Abilities.Effect(
            "Windswept Heath: search library for Forest or Plains, put onto battlefield",
            () => { });
        var ability = new ActivatedAbility(
            source: fetch,
            controller: alice,
            effects: new Majik.Core.Abilities.IEffect[] { effect });
        var stack = new Majik.Core.Stack.Stack(_bus);
        stack.Push(ability);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice }, stack);

        dto.Stack.Should().ContainSingle().Which.Description.Should().Be(
            "Windswept Heath: search library for Forest or Plains, put onto battlefield");
    }

    [Fact]
    public void Snapshot_StackHasActivatedAbility_NoEffects_FallsBackToSourceName()
    {
        var alice = new Player("Alice", 20);
        var fetch = new Majik.Core.Cards.Land("Windswept Heath") { Owner = alice };
        var ability = new ActivatedAbility(source: fetch, controller: alice);
        var stack = new Majik.Core.Stack.Stack(_bus);
        stack.Push(ability);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice }, stack);

        dto.Stack.Should().ContainSingle().Which.Description.Should().Be("Windswept Heath");
    }

    [Fact]
    public void Snapshot_ActivatedAbilityOnCard_AbilityDtoExposesId()
    {
        // A permanent with one IActivatedAbility — the DTO must carry the
        // ability's stable Guid so clients can reference it in
        // ActivateAbilityCommand(permanentInstanceId, abilityId).
        var alice = new Player("Alice", 20);
        var bear = new Creature("Pinger", "2U", 1, 1) { Owner = alice };
        alice.Zones.Library.AddCard(bear);
        var zones = new ZoneService(_bus);
        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: alice);

        var ability = new ActivatedAbility(bear, alice);
        bear.AddAbility(ability);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice }, new Majik.Core.Stack.Stack(_bus));

        var cardDto = dto.Players.Single(p => p.Id == alice.Id)
                         .Battlefield.Cards
                         .Single(c => c.InstanceId == bear.InstanceId);

        var abilityDto = cardDto.Abilities.Should().ContainSingle(a => a.Kind == "Activated")
                                .Subject;
        abilityDto.Id.Should().Be(ability.Id);
    }

    [Fact]
    public void Snapshot_StaticAbilityOnCard_AbilityDtoIdIsNull()
    {
        // Clients don't activate static abilities, so Id should remain null.
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice };
        alice.Zones.Library.AddCard(bear);
        var zones = new ZoneService(_bus);
        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: alice);

        bear.AddAbility(new KeywordAbility("Flying", bear, alice));

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice }, new Majik.Core.Stack.Stack(_bus));

        var cardDto = dto.Players.Single(p => p.Id == alice.Id)
                         .Battlefield.Cards
                         .Single(c => c.InstanceId == bear.InstanceId);

        var abilityDto = cardDto.Abilities.Should().ContainSingle(a => a.Kind == "Static")
                                .Subject;
        abilityDto.Id.Should().BeNull();
    }

    [Fact]
    public void Snapshot_PopulatesCounters_FromPermanentCounters()
    {
        // PLAN 04 — CardSnapshotDto.Counters mirrors Permanent.Counters so the
        // snapshot and the enriched CardMovedEvent / CounterAddedEvent payloads
        // agree on the counter map keyed by counter-type name.
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice };
        alice.Zones.Library.AddCard(bear);
        var zones = new ZoneService(_bus);
        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: alice);

        bear.Counters.Add(Majik.Core.Counters.CounterType.PlusOnePlusOne, 2);
        bear.Counters.Add(Majik.Core.Counters.CounterType.Charge, 1);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice }, new Majik.Core.Stack.Stack(_bus));

        var cardDto = dto.Players.Single(p => p.Id == alice.Id)
                         .Battlefield.Cards
                         .Single(c => c.InstanceId == bear.InstanceId);

        cardDto.Counters.Should().NotBeNull();
        cardDto.Counters!["+1/+1"].Should().Be(2);
        cardDto.Counters!["Charge"].Should().Be(1);
    }

    [Fact]
    public void Snapshot_NoCounters_YieldsEmptyCounterMap()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice };
        alice.Zones.Library.AddCard(bear);
        var zones = new ZoneService(_bus);
        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: alice);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice }, new Majik.Core.Stack.Stack(_bus));

        var cardDto = dto.Players.Single(p => p.Id == alice.Id)
                         .Battlefield.Cards
                         .Single(c => c.InstanceId == bear.InstanceId);

        cardDto.Counters.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Snapshot_Seq_ThreadsThroughToGameStateDto()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var dto = StateSnapshotter.Snapshot(
            Guid.NewGuid(), 1, PhaseStateType.PreCombatMain, alice,
            new[] { alice, bob }, new Majik.Core.Stack.Stack(_bus), seq: 42);

        dto.Seq.Should().Be(42);
    }
}
